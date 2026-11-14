using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Solid;

/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/modeling/transform/README.md">Living guide.</seealso>
[Trait("Feature", "Solid")]
[Trait("Layer", "Headless")]
public class TransformTests
{
    [AvaloniaFact(DisplayName = "SOL-H020 | Move translates the box by the typed delta")]
    [Trait("TestId", "SOL-H020")]
    public void MoveTranslatesBox()
    {
        var session = new CadSession();
        var source = CreateBoxBody(session, new Vec3(1, 2, 3), 2, 4, 6);
        var command = StartCommand(session, source, () => new TransformCommand(TransformCommand.Kind.Move));
        command.ConfigureInputs(() =>
        {
            SetNumber(command, "Δ X", 7);
            SetNumber(command, "Δ Y", -2);
        });
        var output = command.TryBuild();
        Assert.True(output != null, command.Error);
        Assert.Empty(output.Add);
        var shape = Assert.Single(output.Replace).New;
        AssertValidSolid(shape);
        AssertBounds(shape, new Vec3(8, 0, 3), new Vec3(10, 4, 9));
        AssertNear(48, shape.Props().Volume, "Transformed volume (mm³)");
    }

    [AvaloniaFact(DisplayName = "SOL-H021 | A 90° world-Z rotation swaps the box dimensions")]
    [Trait("TestId", "SOL-H021")]
    public void RotateAboutWorldZSwapsDimensions()
    {
        var session = new CadSession();
        var source = CreateBoxBody(session, new Vec3(1, 2, 3), 2, 4, 6);
        var command = StartCommand(session, source, () => new TransformCommand(TransformCommand.Kind.Rotate));
        command.ConfigureInputs(() =>
        {
            SetChoice(command, "Pivot", "World origin");
            SetNumber(command, "Angle", 90);
        });
        var output = command.TryBuild();
        Assert.True(output != null, command.Error);
        Assert.Empty(output.Add);
        var shape = Assert.Single(output.Replace).New;
        AssertValidSolid(shape);
        AssertBounds(shape, new Vec3(-6, 1, 3), new Vec3(-2, 3, 9));
        AssertNear(48, shape.Props().Volume, "Transformed volume (mm³)");
    }

    [AvaloniaFact(DisplayName = "SOL-H022 | A factor of two multiplies volume by eight")]
    [Trait("TestId", "SOL-H022")]
    public void ScaleDoublesDimensions()
    {
        var session = new CadSession();
        var source = CreateBoxBody(session, new Vec3(1, 2, 3), 2, 4, 6);
        var command = StartCommand(session, source, () => new TransformCommand(TransformCommand.Kind.Scale));
        command.ConfigureInputs(() =>
        {
            SetNumber(command, "Factor", 2);
        });
        var output = command.TryBuild();
        Assert.True(output != null, command.Error);
        Assert.Empty(output.Add);
        var shape = Assert.Single(output.Replace).New;
        AssertValidSolid(shape);
        AssertBounds(shape, new Vec3(0, 0, 0), new Vec3(4, 8, 12));
        AssertNear(384, shape.Props().Volume, "Transformed volume (mm³)");
    }

    [AvaloniaFact(DisplayName = "SOL-H110 | Scale per axis stretches each direction by its own factor, and a sphere becomes an ellipsoid")]
    [Trait("TestId", "SOL-H110")]
    public void ScalePerAxisStretches()
    {
        var session = new CadSession();
        var source = CreateBoxBody(session, new Vec3(1, 2, 3), 2, 4, 6);
        var command = StartCommand(session, source, () => new TransformCommand(TransformCommand.Kind.Scale));
        command.ConfigureInputs(() => { SetChoice(command, "Scale type", "Per axis (stretch)"); SetNumber(command, "Factor X", 2); SetNumber(command, "Factor Y", 1); SetNumber(command, "Factor Z", 0.5); });
        var output = command.TryBuild();
        Assert.True(output != null, command.Error);
        var shape = Assert.Single(output.Replace).New;
        AssertValidSolid(shape);
        AssertBounds(shape, new Vec3(0, 2, 4.5), new Vec3(4, 6, 7.5));       // each side grows or shrinks about the box centre
        AssertNear(48, shape.Props().Volume, "Stretched volume (mm³)");

        var ball = session.AddObject(Occ.Sphere(5), "Ball", ObjKind.Solid);
        var stretch = StartCommand(session, ball, () => new TransformCommand(TransformCommand.Kind.Scale));
        stretch.ConfigureInputs(() => { SetChoice(stretch, "Scale type", "Per axis (stretch)"); SetNumber(stretch, "Factor X", 2); SetNumber(stretch, "Factor Y", 1); SetNumber(stretch, "Factor Z", 1); });
        var ellipsoid = Assert.Single(stretch.TryBuild()!.Replace).New;
        AssertValidSolid(ellipsoid);
        AssertNear(4.0 / 3 * Math.PI * 10 * 5 * 5, ellipsoid.Props().Volume, "Ellipsoid volume (mm³)");
    }

    [AvaloniaFact(DisplayName = "SOL-H111 | Per-axis scale puts an arrow on each face of the objects, and uniform scale keeps the gizmo block")]
    [Trait("TestId", "SOL-H111")]
    public void ScalePerAxisHasFaceArrows()
    {
        var session = new CadSession();
        var source = CreateBoxBody(session, Vec3.Zero, 10, 10, 10);
        var command = StartCommand(session, source, () => new TransformCommand(TransformCommand.Kind.Scale));
        command.ConfigureInputs(() => SetSelection(command, command.Slots[0].Label, source));
        Assert.Empty(command.Handles);
        Assert.NotNull(command.Gizmo);                                       // uniform: the block on the gizmo
        command.ConfigureInputs(() => { SetChoice(command, "Scale type", "Per axis (stretch)"); SetNumber(command, "Factor X", 1); SetNumber(command, "Factor Y", 1); SetNumber(command, "Factor Z", 1); }); command.OnChanged();
        Assert.Null(command.Gizmo);
        Assert.Equal(3, command.Handles.Count);
        var x = command.Handles.Single(h => h.Param.Label == "Factor X");
        AssertPoint(new Vec3(10, 5, 5), CadSession.HandleTip(x), 1e-9, "The X arrow's tip sits on the face the factor stretches");
        Assert.False(command.Params.OfType<NumberParam>().Single(p => p.Label == "Factor").IsVisible);
    }

    [AvaloniaFact(DisplayName = "SOL-H023 | Default XY mirror makes a reflected copy")]
    [Trait("TestId", "SOL-H023")]
    public void MirrorCreatesReflectedCopy()
    {
        var session = new CadSession();
        var source = CreateBoxBody(session, new Vec3(1, 2, 3), 2, 4, 6);
        var command = StartCommand(session, source, () => new TransformCommand(TransformCommand.Kind.Mirror));
        var output = command.TryBuild();
        Assert.True(output != null, command.Error);
        Assert.Empty(output.Replace);
        var shape = Assert.Single(output.Add).Shape!;
        AssertValidSolid(shape);
        AssertBounds(shape, new Vec3(1, 2, -9), new Vec3(3, 6, -3));
        AssertNear(48, shape.Props().Volume, "Transformed volume (mm³)");
    }

    [AvaloniaFact(DisplayName = "SOL-H024 | Duplicate keeps the original and adds a second body")]
    [Trait("TestId", "SOL-H024")]
    public void DuplicatePreservesOriginal()
    {
        var session = new CadSession();
        var source = CreateBoxBody(session, Vec3.Zero, 2, 4, 6);
        session.SelectItems([SelItem.Of(source)]);
        session.Duplicate();
        var copy = Assert.Single(session.Objects, obj => obj.Id != source.Id);
        Assert.Contains(session.Objects, obj => obj.Id == source.Id);
        AssertValidSolid(copy.Shape);
        AssertNear(48, copy.Shape.Props().Volume, "Duplicate volume (mm³)");
    }

}
