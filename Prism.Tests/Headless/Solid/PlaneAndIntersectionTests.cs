using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Solid;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/construction-planes/README.md">Construction planes.</seealso>
[Trait("Feature", "Solid")]
[Trait("Layer", "Headless")]
public class PlaneAndIntersectionTests
{
    [AvaloniaFact(DisplayName = "SOL-H164 | A plane through three points is drawn centred on them and large enough to cover all three")]
    [Trait("TestId", "SOL-H164")]
    public void ThreePointPlaneCoversThePoints()
    {
        var session = new CadSession();
        var command = Begin(session, () => new ConstructionPlaneCommand());
        SetChoice(command, "Method", "Through three points");
        double[] xyz = [0, 0, 0, 200, 0, 0, 0, 100, 0];
        string[] names = ["Point 1 X", "Point 1 Y", "Point 1 Z", "Point 2 X", "Point 2 Y", "Point 2 Z", "Point 3 X", "Point 3 Y", "Point 3 Z"];
        for (int i = 0; i < 9; i++) SetNumber(command, names[i], xyz[i]);
        var output = BuildOutput(command);
        var drawn = Assert.Single(output.Add).Shape!.Props();
        // the outline reaches past every one of the three points
        foreach (var p in new Vec3[] { new(0, 0, 0), new(200, 0, 0), new(0, 100, 0) })
        {
            Assert.True(p.X >= drawn.Min.X - 1e-6 && p.X <= drawn.Max.X + 1e-6, $"x {p.X} inside {drawn.Min.X}..{drawn.Max.X}");
            Assert.True(p.Y >= drawn.Min.Y - 1e-6 && p.Y <= drawn.Max.Y + 1e-6, $"y {p.Y} inside {drawn.Min.Y}..{drawn.Max.Y}");
        }
        var plane = Assert.Single(output.Add).Plane!.Value;
        AssertClose(200.0 / 3, plane.Origin.X, 1e-9, "Plane centred on the three points (X)");
    }

    [AvaloniaFact(DisplayName = "SOL-H165 | Intersection Curve of two surfaces can delete both of them afterwards")]
    [Trait("TestId", "SOL-H165")]
    public void IntersectionCanDeleteTheOriginals()
    {
        var session = new CadSession();
        var a = session.AddObject(Occ.Profile([Occ.Polyline([new(-20, -20, 0), new(20, -20, 0), new(20, 20, 0), new(-20, 20, 0)], true)], true), "Flat", ObjKind.Surface);
        var b = session.AddObject(Occ.Profile([Occ.Polyline([new(0, -20, -20), new(0, 20, -20), new(0, 20, 20), new(0, -20, 20)], true)], true), "Wall", ObjKind.Surface);

        var keep = Begin(session, () => new IntersectCurvesCommand());
        Pick(keep, "First", a); Pick(keep, "Second", b);
        keep.OnChanged();
        Assert.Empty(BuildOutput(keep).Remove);
        var delete = Begin(session, () => new IntersectCurvesCommand());
        Pick(delete, "First", a); Pick(delete, "Second", b);
        delete.OnChanged();
        var option = delete.Params.OfType<BoolParam>().Single(p => p.Label.StartsWith("Delete the two originals"));
        Assert.True(option.IsVisible);
        option.Value = true;
        var output = BuildOutput(delete);
        Assert.Equal(new[] { a.Id, b.Id }.Order(), output.Remove.Select(o => o.Id).Order());
        AssertClose(40, Assert.Single(output.Add).Shape!.Props().Size.Y, 1e-6, "Intersection line length (mm)");
    }

    [AvaloniaFact(DisplayName = "SOL-H166 | A plane tangent to a round face has an on-screen angle handle")]
    [Trait("TestId", "SOL-H166")]
    public void TangentPlaneHasAHandle()
    {
        var session = new CadSession();
        var drum = session.AddObject(CylinderAt(new(0, 0, 0), 10, 20), "Drum", ObjKind.Solid);
        var command = Begin(session, () => new ConstructionPlaneCommand());
        SetChoice(command, "Method", "Tangent to a round face");
        Assert.Empty(command.Handles);
        int side = Enumerable.Range(0, drum.Shape.Count(SubKind.Face)).First(i => Occ.FaceInfoOf(drum.Shape.Sub(SubKind.Face, i)).Type == "Cylinder");
        Pick(command, "Round face (hole, boss or fillet)", new SelItem(drum.Id, SelKind.Face, side));
        Assert.Single(command.Handles);
    }
}
