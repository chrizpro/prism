using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Core.Sketching;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Solid;

/// <summary>
/// Split Body and Split Face with a whole sketch however it is clicked, with a movable cutting plane, and with Apply (owner request 2026-11-23).
/// </summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/solid-details/README.md">Living guide.</seealso>
[Trait("Feature", "Solid")]
[Trait("Layer", "Headless")]
public class SplitWithPlaneAndSketchTests
{
    const string BodyTool = "Splitting tool (plane, face, surface, solid or sketch)";

    /// <summary>A line across a 100 mm box at y = 50 and height <paramref name="z"/>, edge to edge, drawn as two segments that meet at x = 50.</summary>
    static SceneObject TwoSegmentLine(CadSession session, double z)
    {
        var sketch = new SketchModel();
        int a = sketch.AddPoint(0, 50), b = sketch.AddPoint(50, 50), c = sketch.AddPoint(100, 50);
        sketch.AddLine(a, b); sketch.AddLine(b, c);
        session.EnterSketch(new Plane3(new Vec3(0, 0, z), Vec3.UnitX, Vec3.UnitY), sketch);
        session.ExitSketch(keep: true);
        return session.Objects.Last(o => o.Kind == ObjKind.Sketch);
    }

    static SelItem TopFace(SceneObject body, double z) => new(body.Id, SelKind.Face,
        Enumerable.Range(0, body.Shape.Count(SubKind.Face)).Single(i => Math.Abs(body.Shape.Sub(SubKind.Face, i).Props().Center.Z - z) < 1e-6));

    static T Open<T>(CadSession session, Func<T> make) where T : Command
    {
        session.SelectItems([]);
        session.StartCommand(() => make());
        return Assert.IsType<T>(session.ActiveCommand);
    }

    /// <summary>Fill a pick the way a click does, so the tool updates its panel (the scenario helper alone does not raise the change).</summary>
    static void Click(Command command, string label, SelItem item)
    {
        command.ConfigureInputs(() => { var slot = command.Slots.Single(s => s.Label == label); slot.Items.Clear(); Assert.True(slot.TryAdd(item), $"{label} takes {item.Kind}"); });
        command.OnChanged();
    }

    [AvaloniaFact(DisplayName = "SOL-H201 | Split Body cuts with the whole sketch when one line of a closed sketch is clicked")]
    [Trait("TestId", "SOL-H201")]
    public void SketchAreaPickMeansWholeSketch()
    {
        var session = new CadSession();
        var body = CreateBoxBody(session, Vec3.Zero, 100, 100, 100);
        var sketch = CreateRectangleSketch(session, Plane3.XY, 20, 20, 40, 40);
        var split = Open(session, () => new SplitBodyCommand());
        Click(split, "Body to split", SelItem.Of(body));
        Click(split, BodyTool, new SelItem(sketch.Id, SelKind.Entity, sketch.SketchData!.Entities[0].Id, 0));   // alone, one line would not cut
        var output = BuildOutput(split);
        var volumes = new[] { Assert.Single(output.Replace).New.Props().Volume, Assert.Single(output.Add).Shape!.Props().Volume }.Order().ToArray();
        AssertNear(40000, volumes[0], "Plug (mm³)");
        AssertNear(960000, volumes[1], "Body with the pocket (mm³)");
    }

    [AvaloniaFact(DisplayName = "SOL-H202 | Split Face cuts with the whole sketch when one line of a two-line sketch is clicked")]
    [Trait("TestId", "SOL-H202")]
    public void SketchLinePickMeansWholeSketch()
    {
        var session = new CadSession();
        var body = CreateBoxBody(session, Vec3.Zero, 100, 100, 100);
        var line = TwoSegmentLine(session, 100);
        var split = Open(session, () => new SplitFaceCommand());
        Click(split, "Face to split", TopFace(body, 100));
        Click(split, "Splitting plane, edge or sketch", new SelItem(line.Id, SelKind.Edge, 0));   // one segment reaches only half way across
        var output = BuildOutput(split);
        Assert.Equal(7, Assert.Single(output.Replace).New.Count(SubKind.Face));
    }

    [AvaloniaFact(DisplayName = "SOL-H203 | Split Body's movable plane starts through the body, cuts it in half, and Apply keeps it there for the next body")]
    [Trait("TestId", "SOL-H203")]
    public void MovablePlaneSplitsBodiesInARow()
    {
        var session = new CadSession();
        var first = CreateBoxBody(session, Vec3.Zero, 100, 100, 100);
        var second = CreateBoxBody(session, new Vec3(200, 0, 0), 100, 100, 40);
        var split = Open(session, () => new SplitBodyCommand());
        SetToggle(split, "Cut with a movable plane", true);
        Click(split, "Body to split", SelItem.Of(first));
        Assert.False(split.Slots.Single(s => s.Label == BodyTool).IsVisible);
        Assert.NotNull(split.Gizmo);
        Assert.Equal(50, split.Params.OfType<NumberParam>().Single(p => p.Label == "Plane Z").Value, 6);
        SetNumber(split, "Plane Z", 30);
        split.RepeatApply!.Run();
        Assert.Equal(3, session.Objects.Count(o => o.Kind == ObjKind.Solid));
        AssertNear(300000, session.Find(first.Id)!.Shape.Props().Volume, "First box below the plane (mm³)");

        var next = Assert.IsType<SplitBodyCommand>(session.ActiveCommand);
        Assert.NotSame(split, next);
        Assert.True(next.Params.OfType<BoolParam>().Single(p => p.Label == "Cut with a movable plane").Value);
        Click(next, "Body to split", SelItem.Of(second));
        Assert.Equal(30, next.Params.OfType<NumberParam>().Single(p => p.Label == "Plane Z").Value, 6);   // kept, not moved to the new body's centre
        session.CommitCommand();
        Assert.Null(session.ActiveCommand);
        Assert.Equal(4, session.Objects.Count(o => o.Kind == ObjKind.Solid));
        AssertNear(300000, session.Find(second.Id)!.Shape.Props().Volume, "Second box below the plane (mm³)");
    }

    [AvaloniaFact(DisplayName = "SOL-H204 | Split Face's movable plane turned upright divides the top face, and a plane that misses is refused")]
    [Trait("TestId", "SOL-H204")]
    public void MovablePlaneSplitsAFace()
    {
        var session = new CadSession();
        var body = CreateBoxBody(session, Vec3.Zero, 100, 100, 100);
        var split = Open(session, () => new SplitFaceCommand());
        SetToggle(split, "Cut with a movable plane", true);
        Click(split, "Face to split", TopFace(body, 100));
        Assert.False(split.Slots.Single(s => s.Label == "Splitting plane, edge or sketch").IsVisible);
        SetNumber(split, "Plane turn Y", 90);
        Assert.Equal(7, Assert.Single(BuildOutput(split).Replace).New.Count(SubKind.Face));
        Assert.Equal("Apply", split.RepeatApply?.Label);

        SetNumber(split, "Plane X", 500);
        Assert.Null(split.TryBuild());
    }

    /// <summary>The side face of a 100 mm cube whose centre is at x = 0.</summary>
    static SelItem SideFace(SceneObject body) => new(body.Id, SelKind.Face,
        Enumerable.Range(0, body.Shape.Count(SubKind.Face)).Single(i => Math.Abs(body.Shape.Sub(SubKind.Face, i).Props().Center.X) < 1e-6));

    [AvaloniaFact(DisplayName = "SOL-H205 | Split Face Through all with a level plane splits all four sides of a cube and keeps one solid of the same volume")]
    [Trait("TestId", "SOL-H205")]
    public void ThroughAllWithPlane()
    {
        var session = new CadSession();
        var body = CreateBoxBody(session, Vec3.Zero, 100, 100, 100);
        var split = Open(session, () => new SplitFaceCommand());
        SetToggle(split, "Cut with a movable plane", true);
        Click(split, "Face to split", SideFace(body));
        SetChoice(split, "Extent", "Through all (every face the tool crosses)");
        var result = Assert.Single(BuildOutput(split).Replace).New;
        Assert.Equal(1, result.Count(SubKind.Solid));
        Assert.Equal(10, result.Count(SubKind.Face));
        AssertNear(1000000, result.Props().Volume, "Volume (mm³)");
    }

    [AvaloniaFact(DisplayName = "SOL-H206 | Split Face Selected face with the same plane splits only the picked side")]
    [Trait("TestId", "SOL-H206")]
    public void SelectedFaceOnlyByDefault()
    {
        var session = new CadSession();
        var body = CreateBoxBody(session, Vec3.Zero, 100, 100, 100);
        var split = Open(session, () => new SplitFaceCommand());
        SetToggle(split, "Cut with a movable plane", true);
        Click(split, "Face to split", SideFace(body));
        Assert.Equal("Selected face", split.Params.OfType<ChoiceParam>().Single(p => p.Label == "Extent").Selected);
        Assert.Equal(7, Assert.Single(BuildOutput(split).Replace).New.Count(SubKind.Face));
    }

    [AvaloniaFact(DisplayName = "SOL-H207 | Split Face Through all with a sketch line across the top splits the top, the bottom and the two sides it crosses; an edge is refused")]
    [Trait("TestId", "SOL-H207")]
    public void ThroughAllWithSketch()
    {
        var session = new CadSession();
        var body = CreateBoxBody(session, Vec3.Zero, 100, 100, 100);
        var line = TwoSegmentLine(session, 100);
        var split = Open(session, () => new SplitFaceCommand());
        Click(split, "Face to split", TopFace(body, 100));
        Click(split, "Splitting plane, edge or sketch", SelItem.Of(line));
        SetChoice(split, "Extent", "Through all (every face the tool crosses)");
        var result = Assert.Single(BuildOutput(split).Replace).New;
        Assert.Equal(1, result.Count(SubKind.Solid));
        Assert.Equal(10, result.Count(SubKind.Face));
        AssertNear(1000000, result.Props().Volume, "Volume (mm³)");

        Click(split, "Splitting plane, edge or sketch", new SelItem(body.Id, SelKind.Edge, 0));
        Assert.Null(split.TryBuild());
    }
}
