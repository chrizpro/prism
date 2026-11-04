using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Solid;

/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/modeling/patterns/README.md">Living pattern guide.</seealso>
[Trait("Feature", "Solid")]
[Trait("Layer", "Headless")]
public class PatternTests
{
    [AvaloniaFact(DisplayName = "SOL-H040 | Linear pattern makes copies at 20, 40 and 60 mm")]
    [Trait("TestId", "SOL-H040")]
    public void LinearUsesInstanceSpacing()
    {
        var session = new CadSession();
        var source = CreateBoxBody(session, new Vec3(-1, -1, -1), 2, 2, 2);
        var command = StartCommand(session, source, () => new LinearPatternCommand());
        AssertCenters(command, new(20, 0, 0), new(40, 0, 0), new(60, 0, 0));
    }

    [AvaloniaFact(DisplayName = "SOL-H041 | Four circular instances occupy the cardinal positions")]
    [Trait("TestId", "SOL-H041")]
    public void CircularFullTurnDoesNotDuplicateOriginal()
    {
        var session = new CadSession();
        var source = CreateBoxBody(session, new Vec3(9, -1, -1), 2, 2, 2);
        var command = StartCommand(session, source, () => new CircularPatternCommand());
        command.ConfigureInputs(() => SetCount(command, "Count", 4));
        AssertCenters(command, new(0, 10, 0), new(-10, 0, 0), new(0, -10, 0));
    }

    [AvaloniaFact(DisplayName = "SOL-H042 | Three instances on a 20 mm straight path include both ends")]
    [Trait("TestId", "SOL-H042")]
    public void AlongCurveUsesEquallySpacedPositions()
    {
        var session = new CadSession();
        var source = CreateBoxBody(session, new Vec3(-1, -1, -1), 2, 2, 2);
        var path = CreateCurve(session, [Vec3.Zero, new Vec3(20, 0, 0)]);
        var command = StartCommand(session, source, () => new CurvePatternCommand());
        command.ConfigureInputs(() =>
        {
            SetSelection(command, "Path (edges or curves)", path);
            SetCount(command, "Count", 3);
            SetChoice(command, "Orientation", "Keep orientation");
        });
        AssertCenters(command, new(10, 0, 0), new(20, 0, 0));
    }

    [AvaloniaFact(DisplayName = "SOL-H043 | Square fill uses a 3 × 3 grid inside the boundary margin")]
    [Trait("TestId", "SOL-H043")]
    public void FillKeepsGridCentersInsideMargin()
    {
        var session = new CadSession();
        var source = CreateBoxBody(session, new Vec3(-1, -1, -1), 2, 2, 2);
        var region = CreateRectangleSketch(session, Plane3.XY, 30, 30, -15, -15);
        var command = StartCommand(session, source, () => new FillPatternCommand());
        command.ConfigureInputs(() =>
        {
            SetSelection(command, "Region to fill (planar face or closed sketch)", region);
            SetChoice(command, "Layout", "Square grid");
            SetNumber(command, "Spacing", 10);
        });
        AssertCenters(command, new(-10, -10, 0), new(-10, 0, 0), new(-10, 10, 0),
            new(0, -10, 0), new(0, 10, 0), new(10, -10, 0), new(10, 0, 0), new(10, 10, 0));
    }

    [AvaloniaFact(DisplayName = "SOL-H044 | Table rows place copies at their typed offsets")]
    [Trait("TestId", "SOL-H044")]
    public void TableUsesOffsetsFromOriginal()
    {
        var session = new CadSession();
        var source = CreateBoxBody(session, new Vec3(1, 2, 3), 2, 2, 2);
        var command = StartCommand(session, source, () => new TablePatternCommand());
        command.ConfigureInputs(() =>
            command.Params.OfType<TextParam>().Single(p => p.Label == "Positions").Value = "10, 0, 0\n0, 10, 4");
        AssertCenters(command, new(12, 3, 4), new(2, 13, 8));
    }

    static void SetCount(Command command, string label, int count) =>
        command.Params.OfType<IntParam>().Single(p => p.Label == label).Value = count;

    static void AssertCenters(Command command, params Vec3[] expected)
    {
        var output = command.TryBuild();
        Assert.True(output != null, command.Error);
        Assert.Empty(output.Replace);
        Assert.Equal(expected.Length, output.Add.Count);
        var centers = output.Add.Select(copy =>
        {
            Assert.NotNull(copy.Shape);
            using var shape = copy.Shape;
            AssertValidSolid(shape);
            AssertNear(8, shape.Props().Volume, "Instance volume (mm³)");
            return shape.Props().Center;
        }).ToList();
        foreach (var center in expected)
            Assert.Single(centers, actual => actual.DistanceTo(center) < 0.0001);
    }
}
