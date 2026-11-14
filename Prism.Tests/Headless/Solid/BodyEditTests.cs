using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Solid;

/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/modeling/solid-details/README.md">Living guide.</seealso>
[Trait("Feature", "Solid")]
[Trait("Layer", "Headless")]
public class BodyEditTests
{
    [AvaloniaFact(DisplayName = "SOL-H030 | A plain through-hole removes the analytic cylinder volume")]
    [Trait("TestId", "SOL-H030")]
    public void ThroughHoleRemovesCylinder()
    {
        var session = new CadSession();
        var source = CreateBoxBody(session, Vec3.Zero, 20, 20, 12);
        var command = StartCommand(session, source, () => new HoleCommand());
        command.ConfigureInputs(() => SetSelection(command, command.Slots[0].Label, source));
        using var result = Replacement(command, source);
        AssertNear(4800 - Math.PI * 9 * 12, result.Props().Volume, "Drilled volume (mm³)");
    }

    [AvaloniaFact(DisplayName = "SOL-H031 | Fillet removes the expected cross-section along a straight edge")]
    [Trait("TestId", "SOL-H031")]
    public void FilletRoundsStraightEdge()
    {
        var session = new CadSession();
        var source = CreateBoxBody(session, Vec3.Zero, 10, 10, 10);
        var command = StartCommand(session, source, () => new FilletCommand());
        int edge = Enumerable.Range(0, source.Shape.Count(SubKind.Edge)).First(index =>
        {
            using var candidate = source.Shape.Sub(SubKind.Edge, index);
            return candidate.Props().Size.Z > 9;
        });
        command.ConfigureInputs(() =>
        {
            Assert.True(command.Slots[0].TryAdd(new SelItem(source.Id, SelKind.Edge, edge)));
            SetNumber(command, "Radius", 2);
        });
        using var result = Replacement(command, source);
        AssertNear(1000 - 10 * (4 - Math.PI), result.Props().Volume, "Dressed volume (mm³)");
    }

    [AvaloniaFact(DisplayName = "SOL-H032 | Chamfer removes the expected cross-section along a straight edge")]
    [Trait("TestId", "SOL-H032")]
    public void ChamferBevelsStraightEdge()
    {
        var session = new CadSession();
        var source = CreateBoxBody(session, Vec3.Zero, 10, 10, 10);
        var command = StartCommand(session, source, () => new ChamferCommand());
        int edge = Enumerable.Range(0, source.Shape.Count(SubKind.Edge)).First(index =>
        {
            using var candidate = source.Shape.Sub(SubKind.Edge, index);
            return candidate.Props().Size.Z > 9;
        });
        command.ConfigureInputs(() =>
        {
            Assert.True(command.Slots[0].TryAdd(new SelItem(source.Id, SelKind.Edge, edge)));
            SetNumber(command, "Distance", 2);
        });
        using var result = Replacement(command, source);
        AssertNear(980, result.Props().Volume, "Dressed volume (mm³)");
    }

    [AvaloniaFact(DisplayName = "SOL-H033 | Shell opens a 2 mm wall box at the selected top face")]
    [Trait("TestId", "SOL-H033")]
    public void ShellOpensSelectedFace()
    {
        var session = new CadSession();
        var source = CreateBoxBody(session, Vec3.Zero, 10, 10, 10);
        var command = StartCommand(session, source, () => new ShellCommand());
        command.ConfigureInputs(() =>
        {
            command.Slots[0].Items.Clear();
            Assert.True(command.Slots[0].TryAdd(FindFace(source, Vec3.UnitZ)));
        });
        using var result = Replacement(command, source);
        AssertNear(1000 - 6 * 6 * 8, result.Props().Volume, "Hollow body volume (mm³)");
    }

    [AvaloniaFact(DisplayName = "SOL-H034 | An overlapping tool splits a cube into two solids and conserves volume")]
    [Trait("TestId", "SOL-H034")]
    public void SplitProducesEqualPieces()
    {
        var session = new CadSession();
        var source = CreateBoxBody(session, Vec3.Zero, 10, 10, 10);
        var tool = CreateBoxBody(session, new Vec3(5, -1, -1), 10, 12, 12);
        var command = StartCommand(session, source, () => new SplitBodyCommand());
        command.ConfigureInputs(() =>
        {
            SetSelection(command, "Body to split", source);
            SetSelection(command, "Splitting tool (plane, face, surface or solid)", tool);
        });
        var output = command.TryBuild();
        Assert.True(output != null, command.Error);
        using var first = Assert.Single(output.Replace).New;
        using var second = Assert.Single(output.Add).Shape!;
        AssertValidSolid(first);
        AssertValidSolid(second);
        AssertNear(500, first.Props().Volume, "First piece (mm³)");
        AssertNear(500, second.Props().Volume, "Second piece (mm³)");
    }

    [AvaloniaFact(DisplayName = "SOL-H035 | Emboss joins a 2 × 3 mm raised rectangle to the top face")]
    [Trait("TestId", "SOL-H035")]
    public void EmbossRaisesClosedProfile()
    {
        var session = new CadSession();
        var source = CreateBoxBody(session, Vec3.Zero, 10, 10, 10);
        var profile = CreateRectangleSketch(session, Plane3.XY with { Origin = new Vec3(0, 0, 10) }, 2, 3, 2, 2);
        session.StartCommand(() => new EmbossCommand());
        var command = Assert.IsType<EmbossCommand>(session.ActiveCommand);
        command.ConfigureInputs(() =>
        {
            SetSelection(command, "Sketch (text or shape)", profile);
            SetSelection(command, "Target body (found automatically)", source);
        });
        using var result = Replacement(command, source);
        AssertNear(1006, result.Props().Volume, "Embossed volume (mm³)");
    }

    [AvaloniaFact(DisplayName = "SOL-H036 | Rib adds a 6 × 3 × 2 mm wall to the top of a block")]
    [Trait("TestId", "SOL-H036")]
    public void RibJoinsOpenSketchWall()
    {
        var session = new CadSession();
        var source = CreateBoxBody(session, Vec3.Zero, 10, 10, 10);
        var sketch = new Prism.Core.Sketching.SketchModel();
        int first = sketch.AddPoint(2, 0);
        int last = sketch.AddPoint(8, 0);
        sketch.AddLine(first, last);
        session.EnterSketch(Plane3.XZ with { Origin = new Vec3(0, 5, 10) }, sketch);
        session.ExitSketch(keep: true);
        var profile = Assert.Single(session.Objects, item => item.Kind == ObjKind.Sketch);
        var command = StartCommand(session, profile, () => new RibCommand());
        command.ConfigureInputs(() =>
        {
            SetSelection(command, "Rib sketch", profile);
            SetSelection(command, "Target body", source);
            SetChoice(command, "Rib extends", "Fixed height");
            SetNumber(command, "Height", 3);
        });
        using var result = Replacement(command, source);
        AssertNear(1036, result.Props().Volume, "Ribbed volume (mm³)");
    }

    [AvaloniaFact(DisplayName = "SOL-H109 | A diagonal rib line fills the web between itself and the corner of an L-shaped body")]
    [Trait("TestId", "SOL-H109")]
    public void RibFillsTheWebToTheBody()
    {
        var session = new CadSession();
        var arm = CreateBoxBody(session, Vec3.Zero, 40, 10, 10);
        var post = CreateBoxBody(session, new Vec3(30, 0, 0), 10, 10, 40);
        var source = session.AddObject(Occ.Fuse(arm.Shape, post.Shape), "L body", ObjKind.Solid);
        double before = source.Shape.Props().Volume;
        var sketch = new Prism.Core.Sketching.SketchModel();
        int first = sketch.AddPoint(10, 10);
        int last = sketch.AddPoint(30, 30);
        sketch.AddLine(first, last);
        session.EnterSketch(Plane3.XZ with { Origin = new Vec3(0, 5, 0) }, sketch);
        session.ExitSketch(keep: true);
        var profile = Assert.Single(session.Objects, item => item.Kind == ObjKind.Sketch);
        var command = StartCommand(session, profile, () => new RibCommand());
        command.ConfigureInputs(() =>
        {
            SetSelection(command, "Rib sketch", profile);
            SetSelection(command, "Target body", source);
        });
        using var result = Replacement(command, source);
        Assert.Equal(1, result.Count(SubKind.Solid));
        AssertNear(before + 200 * 2, result.Props().Volume, "The web is the 20 x 20 triangle, 2 mm thick (mm³)");
    }

    [AvaloniaFact(DisplayName = "SOL-H116 | A rib line lying on a face has no web to fill, so the default Rib refuses instead of building an oversized slab")]
    [Trait("TestId", "SOL-H116")]
    public void RibLineOnAFaceHasNoWeb()
    {
        var session = new CadSession();
        var source = CreateBoxBody(session, Vec3.Zero, 60, 40, 4);
        var sketch = new Prism.Core.Sketching.SketchModel();
        int first = sketch.AddPoint(10, 4);
        int last = sketch.AddPoint(50, 4);
        sketch.AddLine(first, last);
        session.EnterSketch(Plane3.XZ with { Origin = new Vec3(0, 20, 0) }, sketch);
        session.ExitSketch(keep: true);
        var profile = Assert.Single(session.Objects, item => item.Kind == ObjKind.Sketch);
        var command = StartCommand(session, profile, () => new RibCommand());
        command.ConfigureInputs(() =>
        {
            SetSelection(command, "Rib sketch", profile);
            SetSelection(command, "Target body", source);
        });
        AssertBuildFails(command, "Fixed height");
        Assert.Equal(4 * 60 * 40, source.Shape.Props().Volume, 3);
    }

    [AvaloniaFact(DisplayName = "SOL-H037 | Drafting one cube wall changes volume by the analytic wedge volume")]
    [Trait("TestId", "SOL-H037")]
    public void DraftTiltsWallAboutNeutralFace()
    {
        var session = new CadSession();
        var source = CreateBoxBody(session, Vec3.Zero, 10, 10, 10);
        var command = StartCommand(session, source, () => new DraftFaceCommand());
        command.ConfigureInputs(() =>
        {
            Assert.True(command.Slots[0].TryAdd(FindFace(source, Vec3.UnitX)));
            Assert.True(command.Slots[1].TryAdd(FindFace(source, Vec3.UnitZ)));
            SetNumber(command, "Draft angle", 10);
        });
        using var result = Replacement(command, source);
        AssertNear(500 * Math.Tan(Math.PI / 18), Math.Abs(result.Props().Volume - 1000),
            "Draft wedge volume (mm³)");
    }

    static SelItem FindFace(SceneObject source, Vec3 normal)
    {
        int index = Enumerable.Range(0, source.Shape.Count(SubKind.Face)).Single(i =>
        {
            using var face = source.Shape.Sub(SubKind.Face, i);
            return Vec3.Dot(Occ.FaceInfoOf(face).Normal, normal) > 0.99;
        });
        return new SelItem(source.Id, SelKind.Face, index);
    }

    static Shape Replacement(Command command, SceneObject source)
    {
        var output = command.TryBuild();
        Assert.True(output != null, command.Error);
        var replacement = Assert.Single(output.Replace);
        Assert.Equal(source.Id, replacement.Old.Id);
        AssertValidSolid(replacement.New);
        return replacement.New;
    }
}
