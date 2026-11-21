using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Core.Sketching;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Solid;

/// <summary>Split Body with a sketch as the cutting tool. Owner-approved 2026-11-16, see the Split Body section of the living guide.</summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/modeling/solid-details/README.md">Living guide.</seealso>
[Trait("Feature", "Solid")]
[Trait("Layer", "Headless")]
public class SplitBodyBySketchTests
{
    static SceneObject LineSketch(CadSession session, double u0, double v0, double u1, double v1)
    {
        var sketch = new SketchModel();
        sketch.AddLine(sketch.AddPoint(u0, v0), sketch.AddPoint(u1, v1));
        session.EnterSketch(Plane3.XY, sketch);
        session.ExitSketch(keep: true);
        return session.Objects.Last(o => o.Kind == ObjKind.Sketch);
    }

    static SplitBodyCommand Split(CadSession session, SceneObject body, SceneObject sketch)
    {
        var command = StartCommand(session, body, () => new SplitBodyCommand());
        command.ConfigureInputs(() =>
        {
            SetSelection(command, "Body to split", body);
            SetSelection(command, "Splitting tool (plane, face, surface or solid)", sketch);
        });
        return command;
    }

    [AvaloniaFact(DisplayName = "SOL-H160 | A line drawn across a box splits it into two equal halves")]
    [Trait("TestId", "SOL-H160")]
    public void LineAcrossBodySplitsInTwo()
    {
        var session = new CadSession();
        var body = CreateBoxBody(session, Vec3.Zero, 100, 100, 100);
        var sketch = LineSketch(session, 50, -10, 50, 110);
        var output = Split(session, body, sketch).TryBuild();
        Assert.NotNull(output);
        using var first = Assert.Single(output.Replace).New;
        using var second = Assert.Single(output.Add).Shape!;
        AssertNear(500000, first.Props().Volume, "First half (mm³)");
        AssertNear(500000, second.Props().Volume, "Second half (mm³)");
    }

    [AvaloniaFact(DisplayName = "SOL-H161 | A closed shape inside the outline gives the plug and a body with a pocket")]
    [Trait("TestId", "SOL-H161")]
    public void ClosedLoopGivesPlugAndPocket()
    {
        var session = new CadSession();
        var body = CreateBoxBody(session, Vec3.Zero, 100, 100, 100);
        var sketch = CreateRectangleSketch(session, Plane3.XY, 20, 20, 40, 40);
        var output = Split(session, body, sketch).TryBuild();
        Assert.NotNull(output);
        using var first = Assert.Single(output.Replace).New;
        using var second = Assert.Single(output.Add).Shape!;
        var volumes = new[] { first.Props().Volume, second.Props().Volume }.Order().ToArray();
        AssertNear(40000, volumes[0], "Plug (mm³)");
        AssertNear(960000, volumes[1], "Body with the pocket (mm³)");
    }

    [AvaloniaFact(DisplayName = "SOL-H162 | A line that stops inside the body is refused")]
    [Trait("TestId", "SOL-H162")]
    public void LineStoppingInsideIsRefused()
    {
        var session = new CadSession();
        var body = CreateBoxBody(session, Vec3.Zero, 100, 100, 100);
        var sketch = LineSketch(session, 50, -10, 50, 50);
        var command = Split(session, body, sketch);
        Assert.Null(command.TryBuild());
        Assert.Contains("stops inside the body", command.Error);
    }

    [AvaloniaFact(DisplayName = "SOL-H163 | Split Body deletes the cutting sketch by default and keeps it when the option is off")]
    [Trait("TestId", "SOL-H163")]
    public void CuttingSketchIsDeletedUnlessKept()
    {
        foreach (bool keep in new[] { false, true })
        {
            var session = new CadSession();
            var body = CreateBoxBody(session, Vec3.Zero, 100, 100, 100);
            var sketch = LineSketch(session, 50, -10, 50, 110);
            var command = Split(session, body, sketch);
            if (keep) command.Params.OfType<BoolParam>().Single(p => p.Label == "Delete the cutting tool afterward").Value = false;
            var output = command.TryBuild();
            Assert.NotNull(output);
            Assert.Equal(keep, output.Remove.Count == 0);
        }
    }
}
