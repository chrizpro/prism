using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Solid;

/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/modeling/combine/README.md">Living guide.</seealso>
[Trait("Feature", "Solid")]
[Trait("Layer", "Headless")]
public class CombineTests
{
    [AvaloniaFact(DisplayName = "SOL-H010 | Union of overlapping 10 mm cubes produces 1500 mm³")]
    [Trait("TestId", "SOL-H010")]
    public void UnionReplacesBaseAndConsumesTool()
    {
        var session = new CadSession();
        var first = CreateBoxBody(session, Vec3.Zero, 10, 10, 10);
        var second = CreateBoxBody(session, new Vec3(5, 0, 0), 10, 10, 10);
        var command = StartCommand(session, first, () => new BooleanCommand(BooleanCommand.Kind.Union));
        command.ConfigureInputs(() =>
        {
            SetSelection(command, "Base body", first);
            SetSelection(command, "Other bodies", second);
        });
        var output = command.TryBuild();
        Assert.True(output != null, command.Error);
        var replacement = Assert.Single(output.Replace);
        Assert.Equal(first.Id, replacement.Old.Id);
        AssertValidSolid(replacement.New);
        AssertNear(1500, replacement.New.Props().Volume, "Boolean volume (mm³)");
        Assert.Equal(second.Id, Assert.Single(output.Remove).Id);
    }

    [AvaloniaFact(DisplayName = "SOL-H011 | Subtract of overlapping 10 mm cubes produces 500 mm³")]
    [Trait("TestId", "SOL-H011")]
    public void SubtractKeepsToolWhenRequested()
    {
        var session = new CadSession();
        var first = CreateBoxBody(session, Vec3.Zero, 10, 10, 10);
        var second = CreateBoxBody(session, new Vec3(5, 0, 0), 10, 10, 10);
        var command = StartCommand(session, first, () => new BooleanCommand(BooleanCommand.Kind.Subtract));
        command.ConfigureInputs(() =>
        {
            SetSelection(command, "Body to cut from", first);
            SetSelection(command, "Tool bodies", second);
            SetToggle(command, "Keep tool bodies", true);
        });
        var output = command.TryBuild();
        Assert.True(output != null, command.Error);
        var replacement = Assert.Single(output.Replace);
        Assert.Equal(first.Id, replacement.Old.Id);
        AssertValidSolid(replacement.New);
        AssertNear(500, replacement.New.Props().Volume, "Boolean volume (mm³)");
        Assert.Empty(output.Remove);
    }

    [AvaloniaFact(DisplayName = "SOL-H012 | Intersect of overlapping 10 mm cubes produces 500 mm³")]
    [Trait("TestId", "SOL-H012")]
    public void IntersectKeepsOnlyOverlap()
    {
        var session = new CadSession();
        var first = CreateBoxBody(session, Vec3.Zero, 10, 10, 10);
        var second = CreateBoxBody(session, new Vec3(5, 0, 0), 10, 10, 10);
        var command = StartCommand(session, first, () => new BooleanCommand(BooleanCommand.Kind.Intersect));
        command.ConfigureInputs(() =>
        {
            SetSelection(command, "Base body", first);
            SetSelection(command, "Other bodies", second);
        });
        var output = command.TryBuild();
        Assert.True(output != null, command.Error);
        var replacement = Assert.Single(output.Replace);
        Assert.Equal(first.Id, replacement.Old.Id);
        AssertValidSolid(replacement.New);
        AssertNear(500, replacement.New.Props().Volume, "Boolean volume (mm³)");
        Assert.Equal(second.Id, Assert.Single(output.Remove).Id);
    }

}
