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
