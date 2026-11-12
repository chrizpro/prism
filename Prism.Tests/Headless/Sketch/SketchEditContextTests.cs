using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Core.Sketching;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Sketch;

/// <summary>
/// Editing a sketch that already feeds features rolls history back so its inputs can be referenced;
/// the downstream objects stay on screen as faint context, and the features keep using only the
/// curves they were made from — curves drawn later are not silently pulled in.
/// </summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/modeling/sketches/README.md">Sketch editing, constraints and dimensions.</seealso>
[Trait("Feature", "Sketch")]
[Trait("Layer", "Headless")]
public class SketchEditContextTests
{
    static (CadSession session, SceneObject sketch, SceneObject surface) SketchWithSurface()
    {
        var session = new CadSession();
        var sketch = CreateRectangleSketch(session, Plane3.XY, 40, 30);
        var extrude = Begin<ExtrudeCommand>(session, () => new ExtrudeCommand(surface: true));
        Pick(extrude, "Profile", sketch);
        session.CommitCommand();
        var surface = session.Objects.Single(o => o.Kind == ObjKind.Surface);
        return (session, sketch, surface);
    }

    static void AddRectangle(SketchContext sketch, CadSession session, double u, double v)
    {
        sketch.Edit(session, "Add rectangle", () =>
        {
            var m = sketch.Model;
            int a = m.AddPoint(u, v), b = m.AddPoint(u + 30, v), c = m.AddPoint(u + 30, v + 30), d = m.AddPoint(u, v + 30);
            m.AddLine(a, b); m.AddLine(b, c); m.AddLine(c, d); m.AddLine(d, a);
        });
    }

    [AvaloniaFact(DisplayName = "SKT-H048 | Editing a sketch keeps downstream objects visible as faint context")]
    [Trait("TestId", "SKT-H048")]
    public void DownstreamObjectsStayVisibleAsContext()
    {
        var (session, sketch, surface) = SketchWithSurface();

        Assert.True(session.EditSketch(session.Find(sketch.Id)!));
        Assert.DoesNotContain(session.Objects, o => o.Id == surface.Id);
        var ghost = Assert.Single(session.ContextItems);
        Assert.True(ghost.Context);
        Assert.True(ghost.NoEdges);       // faces only: edge outlines read as stray curves against the sketch
        Assert.NotNull(ghost.Shape);

        session.ExitSketch(true);
        Assert.Empty(session.ContextItems);
        Assert.Contains(session.Objects, o => o.Kind == ObjKind.Surface);      // the rebuilt feature keeps the model, with a fresh id
    }

    [AvaloniaFact(DisplayName = "SKT-H049 | Curves added while editing do not join the feature that consumed the sketch")]
    [Trait("TestId", "SKT-H049")]
    public void NewSketchCurvesDoNotJoinCapturedProfile()
    {
        var (session, sketch, surface) = SketchWithSurface();

        var recipe = Assert.IsAssignableFrom<FeatureRecipe>(session.Current.Recipe);
        Assert.NotEmpty(recipe.Slots[0]);
        Assert.All(recipe.Slots[0], item => Assert.Equal(SelKind.Entity, item.Kind));

        Assert.True(session.EditSketch(session.Find(sketch.Id)!));
        AddRectangle(session.Sketch!, session, 60, 0);      // a second curve set, offset in X
        session.ExitSketch(true);

        var bounds = session.Objects.Single(o => o.Kind == ObjKind.Surface).Shape.Props();
        AssertNear(40, bounds.Max.X, "Surface maximum X (mm)");      // an unbound sketch would reach 90
        AssertNear(20, bounds.Max.Z, "Surface height (mm)");
    }

    [AvaloniaFact(DisplayName = "SKT-H050 | Editing a bound curve still rebuilds the feature (binding is live)")]
    [Trait("TestId", "SKT-H050")]
    public void MovingBoundEntityPropagates()
    {
        var (session, sketch, surface) = SketchWithSurface();

        Assert.True(session.EditSketch(session.Find(sketch.Id)!));
        session.Sketch!.Edit(session, "Move corner", () =>
            session.Sketch.Model.Points.OrderByDescending(p => p.X).First().X += 10);
        session.ExitSketch(true);

        var bounds = session.Objects.Single(o => o.Kind == ObjKind.Surface).Shape.Props();
        AssertNear(50, bounds.Max.X, "Surface maximum X (mm)");
    }

    [AvaloniaFact(DisplayName = "SKT-H051 | Deleting a bound curve fails the rebuild instead of extruding a different curve")]
    [Trait("TestId", "SKT-H051")]
    public void DeletingBoundEntityFailsRebuild()
    {
        var (session, sketch, surface) = SketchWithSurface();
        int doomed = session.Current.Recipe!.Slots[0][0].Index;

        Assert.True(session.EditSketch(session.Find(sketch.Id)!));
        session.Sketch!.Edit(session, "Delete curve", () =>
            session.Sketch.Model.Entities.RemoveAll(e => e.Id == doomed));
        session.ExitSketch(true);

        Assert.NotNull(session.RebuildIssue);
        Assert.DoesNotContain(session.Objects, o => o.Kind == ObjKind.Surface);
    }

    static SceneObject SketchWithSecondLoneLine(CadSession session, SceneObject sketch)
    {
        Assert.True(session.EditSketch(session.Find(sketch.Id)!));
        session.Sketch!.Edit(session, "Add line", () =>
        {
            var m = session.Sketch.Model;
            int a = m.AddPoint(60, 0), b = m.AddPoint(90, 0);
            m.AddLine(a, b);
        });
        session.ExitSketch(true);
        return session.Objects.Single(o => o.Kind == ObjKind.Sketch);
    }

    [AvaloniaFact(DisplayName = "SKT-H052 | A whole-profile click picks the profile a curve belongs to, not the whole sketch")]
    [Trait("TestId", "SKT-H052")]
    public void WholeProfilePickSelectsConnectedComponent()
    {
        var session = new CadSession();
        var sketch = SketchWithSecondLoneLine(session, CreateRectangleSketch(session, Plane3.XY, 40, 30));

        // edge 4 is the lone line (the four rectangle lines emit first): picking it extrudes only that line
        var extrude = Begin<ExtrudeCommand>(session, () => new ExtrudeCommand(surface: true));
        var slot = extrude.Slots.Single(s => s.Label == "Profile");
        Assert.True(extrude.Pick(new SelItem(sketch.Id, SelKind.Edge, 4), additive: false));
        var item = Assert.Single(slot.Items);
        Assert.Equal(SelKind.Entity, item.Kind);
        session.CommitCommand();

        var bounds = session.Objects.Single(o => o.Kind == ObjKind.Surface).Shape.Props();
        AssertNear(60, bounds.Min.X, "Surface minimum X (mm)");
        AssertNear(90, bounds.Max.X, "Surface maximum X (mm)");      // an unbound sketch pick would start at 0

        // and clicking one rectangle side picks the whole loop: four entity items, not one edge
        var extrude2 = Begin<ExtrudeCommand>(session, () => new ExtrudeCommand(surface: true));
        var slot2 = extrude2.Slots.Single(s => s.Label == "Profile");
        Assert.True(extrude2.Pick(new SelItem(sketch.Id, SelKind.Edge, 0), additive: false));
        Assert.Equal(4, slot2.Items.Count);
        Assert.All(slot2.Items, i => Assert.Equal(SelKind.Entity, i.Kind));
        session.CancelCommand();
    }

    [AvaloniaFact(DisplayName = "SKT-H053 | A single-edge pick on a sketch binds the entity, not a baked edge index")]
    [Trait("TestId", "SKT-H053")]
    public void SingleEdgePickBindsSketchEntity()
    {
        var session = new CadSession();
        var sketch = SketchWithSecondLoneLine(session, CreateRectangleSketch(session, Plane3.XY, 40, 30));

        var extrude = Begin<ExtrudeCommand>(session, () => new ExtrudeCommand(surface: true));
        var slot = extrude.Slots.Single(s => s.Label == "Profile");
        slot.SelectionRuleIndex = 1;    // "Single edges"
        Assert.True(extrude.Pick(new SelItem(sketch.Id, SelKind.Edge, 4), additive: false));
        var item = Assert.Single(slot.Items);
        Assert.Equal(SelKind.Entity, item.Kind);
        session.CancelCommand();
    }

    [AvaloniaFact(DisplayName = "SKT-H054 | An edge selection seeds the profile slot by entity, not by whole sketch or index")]
    [Trait("TestId", "SKT-H054")]
    public void EdgeSelectionSeedsEntity()
    {
        var session = new CadSession();
        var sketch = SketchWithSecondLoneLine(session, CreateRectangleSketch(session, Plane3.XY, 40, 30));

        session.SelectItems([new SelItem(sketch.Id, SelKind.Edge, 4)]);
        session.StartCommand(() => new ExtrudeCommand(surface: true));
        var command = Assert.IsType<ExtrudeCommand>(session.ActiveCommand);
        var item = Assert.Single(command.Slots[0].Items);
        Assert.Equal(SelKind.Entity, item.Kind);
        session.CancelCommand();
    }

    [AvaloniaFact(DisplayName = "SKT-H055 | Finishing a sketch edit does not select the whole sketch for the next tool to consume")]
    [Trait("TestId", "SKT-H055")]
    public void EditExitLeavesNoSelection()
    {
        var (session, sketch, surface) = SketchWithSurface();

        Assert.True(session.EditSketch(session.Find(sketch.Id)!));
        session.ExitSketch(true);

        // A selected whole sketch would seed every curve it has into the next profile tool; the rebuilt
        // surface may legitimately be selected, like any freshly made object.
        Assert.DoesNotContain(session.Selection, i => i.Obj == sketch.Id);
    }

    [AvaloniaFact(DisplayName = "SKT-H056 | A viewport click on a sketch curve feeds the profile slot its connected entities")]
    [Trait("TestId", "SKT-H056")]
    public void ViewportPickFeedsEntityToProfileSlot()
    {
        var session = new CadSession();
        var sketch = SketchWithSecondLoneLine(session, CreateRectangleSketch(session, Plane3.XY, 40, 30));
        session.Camera.SetView(0, 90);      // straight down at XY, like the view while editing a flat sketch

        var extrude = Begin<ExtrudeCommand>(session, () => new ExtrudeCommand(surface: true));
        var slot = extrude.Slots.Single(s => s.Label == "Profile");

        // click the lone line at (75,0) — what Pick sees is what Command.Pick gets
        var px = session.Camera.Project(new Vec3(75, 0, 0))!.Value;
        var hit = session.Pick(px.x, px.y, slot.PickFilter);
        Assert.NotNull(hit);
        Assert.Equal(sketch.Id, hit.Value.Item.Obj);
        Assert.Equal(SelKind.Edge, hit.Value.Item.Kind);
        Assert.Equal(4, hit.Value.Item.Index);        // the lone line is baked edge 4
        Assert.True(extrude.Pick(hit.Value.Item, additive: false));
        var item = Assert.Single(slot.Items);
        Assert.Equal(SelKind.Entity, item.Kind);
        session.CommitCommand();

        var bounds = session.Objects.Single(o => o.Kind == ObjKind.Surface).Shape.Props();
        AssertNear(60, bounds.Min.X, "Surface minimum X (mm)");
        AssertNear(90, bounds.Max.X, "Surface maximum X (mm)");
    }
}
