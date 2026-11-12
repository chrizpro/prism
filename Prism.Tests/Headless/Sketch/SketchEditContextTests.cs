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
}
