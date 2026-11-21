using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Solid;

/// <summary>
/// Move works on every kind of object (owner report 2026-11-19: "I sometimes find myself moving the gizmo, and the object didn't move"). A sketch
/// used to lose its sketch data and keep its old plane, and a datum plane kept the plane it stands for where it was.
/// </summary>
[Trait("Feature", "Solid")]
[Trait("Layer", "Headless")]
public sealed class MoveEveryKindTests
{
    static void Move(CadSession s, SceneObject obj, Action<Command> set, TransformCommand.Kind kind = TransformCommand.Kind.Move)
    {
        s.SelectItems([SelItem.Of(obj)]);
        s.StartCommand(() => new TransformCommand(kind));
        var cmd = s.ActiveCommand!;
        cmd.ConfigureInputs(() => set(cmd));
        Assert.True(cmd.Slots[0].Items.Count == 1, $"{obj.Kind} was not taken into the Objects slot");
        Assert.True(cmd.TryBuild() != null, $"{obj.Kind}: {cmd.Error}");
        s.CommitCommand();
    }

    [AvaloniaFact(DisplayName = "SOL-H178 | Move carries solids, curves, surfaces, sketches and datum planes; a sketch stays editable and a datum plane's plane moves too")]
    [Trait("TestId", "SOL-H178")]
    public void MoveEveryKind()
    {
        var s = new CadSession();
        var solid = s.AddObject(BoxAt(new Vec3(0, 0, 0), 10, 10, 10), "Box", ObjKind.Solid);
        var curve = s.AddObject(Occ.Line(new Vec3(0, 20, 0), new Vec3(10, 20, 0)), "Line", ObjKind.Curve);
        using (var face = BoxAt(new Vec3(0, 60, 0), 10, 10, 1)) s.AddObject(face.Sub(SubKind.Face, 0), "Sheet", ObjKind.Surface);
        var surface = s.Objects.Last();
        var sketch = CreateRectangleSketch(s, Plane3.XY, 5, 5, 0, 40);
        var datumPlane = new Plane3(new Vec3(0, 0, 30), Vec3.UnitX, Vec3.UnitY);
        using (var rect = Occ.Polyline([new Vec3(-5, -5, 30), new Vec3(5, -5, 30), new Vec3(5, 5, 30), new Vec3(-5, 5, 30)], true))
            s.AddObject(Occ.Profile([rect], true), "Plane", ObjKind.Plane, plane: datumPlane);
        var plane = s.Objects.Last();
        foreach (var obj in new[] { solid, curve, surface, sketch, plane })
        {
            var before = s.Find(obj.Id)!.Shape!.Props().Center;
            Move(s, s.Find(obj.Id)!, c => SetNumber(c, "Δ X", 10));
            var after = s.Find(obj.Id)!;
            AssertClose(before.X + 10, after.Shape!.Props().Center.X, 1e-6, $"{obj.Kind} moved");
            Assert.Equal(obj.Kind, after.Kind);
        }
        var movedSketch = s.Find(sketch.Id)!;
        Assert.NotNull(movedSketch.SketchData);
        AssertClose(10, movedSketch.SketchPlane!.Value.Origin.X, 1e-9, "sketch plane moved");
        AssertClose(10, s.Find(plane.Id)!.SketchPlane!.Value.Origin.X, 1e-9, "datum plane's plane moved");

        // a turn keeps the sketch a sketch too, its plane turned with it
        Move(s, movedSketch, c => { SetChoice(c, "Axis", "Z"); SetNumber(c, "Angle", 90); }, TransformCommand.Kind.Rotate);
        var turned = s.Find(sketch.Id)!;
        Assert.NotNull(turned.SketchData);
        Assert.True(Math.Abs(turned.SketchPlane!.Value.U.Y) > 0.999, $"sketch plane's X axis turned to Y, got {turned.SketchPlane.Value.U}");
    }
}
