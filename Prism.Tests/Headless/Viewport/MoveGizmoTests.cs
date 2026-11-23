using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Prism.ViewModels;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Viewport;

/// <summary>
/// The ribbon Move gizmo on whole objects. GZM-H001 to H004 pin what it did before the pivot work (owner request 2026-11-23: guard against regressions);
/// the rest cover Move pivot (D), snapping the pivot, a pivot kept across a re-grab, scale, and grabbing a body by picking all of its faces.
/// Alt-drag copies are SEL-H029; Direct Edit's move, pull and tilt are the DirectEditTests; a sketch's gizmo is SKT-H045/H046.
/// </summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/gizmo-workflows/README.md">Gizmo guide.</seealso>
[Trait("Feature", "Viewport")]
[Trait("Layer", "Headless")]
public class MoveGizmoTests
{
    /// <summary>A 100 x 10 x 10 mm bar from the origin along X, viewed from the front, and the Move gizmo holding it.</summary>
    static (MainViewModel vm, CadSession s, SceneObject bar, DatumGizmoTool gizmo) Bar()
    {
        var vm = new MainViewModel(); var s = vm.Session;
        var bar = CreateBoxBody(s, Vec3.Zero, 100, 10, 10);
        s.Camera.Width = 800; s.Camera.Height = 600; s.Camera.SetView(0, 0); s.Camera.Fit(new(-20, -20, -20), new(120, 20, 120));
        s.SelectItems([SelItem.Of(bar)]);
        vm.MoveWithGizmo();
        return (vm, s, bar, Assert.IsType<DatumGizmoTool>(s.Datum));
    }

    /// <summary>Let the gizmo carry its held items to its frame, as the next drawn frame does.</summary>
    static void Sync(CadSession s) => s.Datum!.BuildOverlay(s, new Overlay());

    static (Vec3 min, Vec3 max) Box(CadSession s, SceneObject o) { var p = s.Find(o.Id)!.Shape.Props(); return (p.Min, p.Max); }

    static void AssertVec(Vec3 expected, Vec3 actual, string what)
    {
        AssertClose(expected.X, actual.X, 1e-4, what + " X"); AssertClose(expected.Y, actual.Y, 1e-4, what + " Y"); AssertClose(expected.Z, actual.Z, 1e-4, what + " Z");
    }

    [AvaloniaFact(DisplayName = "GZM-H001 | Move holds the selected body with the gizmo at its centre; a datum held alone lends the gizmo its own frame")]
    [Trait("TestId", "GZM-H001")]
    public void FirstGrabCentres()
    {
        var (vm, s, bar, gizmo) = Bar();
        Assert.Equal([SelItem.Of(bar)], gizmo.Targets);
        AssertVec(new(50, 5, 5), gizmo.Frame.Origin, "gizmo at the bar's centre");
        Assert.True(gizmo.MoveMode);

        var datum = new Plane3(new Vec3(7, 8, 9), Vec3.UnitY, Vec3.UnitZ);
        s.HideDatumGizmo(); s.ShowDatumGizmo(Vec3.Zero);
        var held = Assert.IsType<DatumGizmoTool>(s.Datum);
        held.Binding.Adopt(datum);
        AssertVec(datum.Origin, held.Frame.Origin, "adopted datum origin");
        AssertVec(datum.U, held.Frame.U, "adopted datum direction");
    }

    [AvaloniaFact(DisplayName = "GZM-H002 | A typed 90° turn about Z turns the held bar about its centre, as one undo step")]
    [Trait("TestId", "GZM-H002")]
    public void TurnsAboutTheCentre()
    {
        var (vm, s, bar, _) = Bar();
        vm.GizmoRotationZ = 90;
        Sync(s);
        var (min, max) = Box(s, bar);
        AssertVec(new(45, -45, 0), min, "turned bar min");
        AssertVec(new(55, 55, 10), max, "turned bar max");
        s.Undo();
        AssertVec(new(0, 0, 0), Box(s, bar).min, "undone bar min");
    }

    [AvaloniaFact(DisplayName = "GZM-H003 | Esc releases the held body and keeps the gizmo; a second Esc hides it")]
    [Trait("TestId", "GZM-H003")]
    public void EscReleasesThenHides()
    {
        var (_, s, _, gizmo) = Bar();
        Assert.True(gizmo.Key(s, "Escape"));
        Assert.Empty(gizmo.Targets);
        Assert.True(s.DatumVisible);
        Assert.True(gizmo.Key(s, "Escape"));
        Assert.False(s.DatumVisible);
    }

    [AvaloniaFact(DisplayName = "GZM-H004 | Clicking another body while one is held swaps to it and centres the gizmo on it")]
    [Trait("TestId", "GZM-H004")]
    public void ClickingAnotherBodyCentresOnIt()
    {
        var (_, s, bar, gizmo) = Bar();
        var other = CreateBoxBody(s, new Vec3(0, 0, 60), 20, 10, 20);
        var at = s.Camera.Project(new Vec3(5, 0, 75))!.Value;
        Assert.True(gizmo.PointerDown(s, new PointerInfo(at.x, at.y, true, false, false, false)));
        Assert.Equal([SelItem.Of(other)], gizmo.Targets);
        AssertVec(new(10, 5, 70), gizmo.Frame.Origin, "gizmo at the other body's centre");
        AssertVec(new(0, 0, 0), Box(s, bar).min, "the bar did not move");
    }

    static void Click(CadSession s, Vec3 world)
    {
        var at = s.Camera.Project(world)!.Value;
        s.PointerDown(new PointerInfo(at.x, at.y, true, false, false, false));
        s.PointerUp(new PointerInfo(at.x, at.y, false, false, false, false));
    }

    [AvaloniaFact(DisplayName = "GZM-H005 | Move pivot (D) moves only the gizmo; a corner click puts the pivot there, and a -90° turn about Y then stands the bar up on that corner")]
    [Trait("TestId", "GZM-H005")]
    public void PivotOnACornerStandsTheBarUp()
    {
        var (vm, s, bar, gizmo) = Bar();
        Assert.True(vm.ToggleGizmoPivot());
        Assert.True(gizmo.PivotMode);
        Assert.Equal("Done placing the pivot (D)", vm.GizmoPivotLabel);
        vm.GizmoOriginX = 80;                                      // typing moves the gizmo, not the bar
        Sync(s);
        AssertVec(new(0, 0, 0), Box(s, bar).min, "bar still in place");
        AssertClose(80, gizmo.Frame.Origin.X, 1e-9, "gizmo moved");
        Click(s, new Vec3(0, 0, 0));                               // the bar's front bottom-left corner
        AssertVec(new(0, 0, 0), gizmo.Frame.Origin, "pivot on the corner");
        Assert.True(vm.ToggleGizmoPivot());
        Assert.False(gizmo.PivotMode);
        Assert.Equal([SelItem.Of(bar)], gizmo.Targets);            // still held

        vm.GizmoRotationY = -90;
        Sync(s);
        var (min, max) = Box(s, bar);
        AssertVec(new(-10, 0, 0), min, "stood-up bar min");
        AssertVec(new(0, 10, 100), max, "stood-up bar max");
        AssertClose(10000, s.Find(bar.Id)!.Shape.Props().Volume, 1e-3, "volume (mm³)");
        s.Undo();
        AssertVec(new(0, 0, 0), Box(s, bar).min, "one undo puts it back");
    }

    [AvaloniaFact(DisplayName = "GZM-H006 | In Move pivot a click on a straight edge puts the pivot on it (its middle when near) with the gizmo's X along it")]
    [Trait("TestId", "GZM-H006")]
    public void PivotOnAnEdgeAlignsX()
    {
        var (vm, s, _, gizmo) = Bar();
        vm.ToggleGizmoPivot();
        vm.GizmoRotationZ = 30;                                    // turn the gizmo away from the edge first
        Click(s, new Vec3(51, 0, 0));                              // near the middle of the front bottom edge
        AssertVec(new(50, 0, 0), gizmo.Frame.Origin, "pivot at the edge's middle");
        AssertClose(1, Math.Abs(Vec3.Dot(gizmo.Frame.U, Vec3.UnitX)), 1e-9, "gizmo X along the edge");
    }

    [AvaloniaFact(DisplayName = "GZM-H007 | A pivot the user placed is kept when something is grabbed again, and resets the next time Move opens")]
    [Trait("TestId", "GZM-H007")]
    public void PlacedPivotIsKeptThenResets()
    {
        var (vm, s, bar, gizmo) = Bar();
        gizmo.Key(s, "Escape");                                    // release the bar, keep the gizmo
        vm.GizmoOriginX = 0; vm.GizmoOriginY = 0; vm.GizmoOriginZ = 0;
        Click(s, new Vec3(30, 0, 5));                              // grab the bar again
        Assert.Equal([SelItem.Of(bar)], gizmo.Targets);
        AssertVec(new(0, 0, 0), gizmo.Frame.Origin, "the placed pivot stayed");

        s.HideDatumGizmo();
        s.SelectItems([SelItem.Of(bar)]);
        vm.MoveWithGizmo();
        AssertVec(new(50, 5, 5), s.Datum!.Frame.Origin, "a new Move centres again");
    }

    [AvaloniaFact(DisplayName = "GZM-H008 | While holding something the gizmo scales too: typed 0.5 on X about a corner pivot halves the bar, and dragging the X square stretches it")]
    [Trait("TestId", "GZM-H008")]
    public void ScalesAboutThePivot()
    {
        var (vm, s, bar, gizmo) = Bar();
        Assert.Equal(["Move", "Rotate", "Scale"], vm.GizmoOperations);
        Assert.True(vm.GizmoShowsScale);
        vm.ToggleGizmoPivot(); Click(s, new Vec3(0, 0, 0)); vm.ToggleGizmoPivot();
        vm.GizmoScaleX = 0.5;
        var (min, max) = Box(s, bar);
        AssertVec(new(0, 0, 0), min, "halved bar min");
        AssertVec(new(50, 10, 10), max, "halved bar max");
        s.Undo();
        AssertVec(new(100, 10, 10), Box(s, bar).max, "undo restores the length");

        vm.GizmoActiveOperation = 2;
        var c = gizmo.Frame.Origin; double length = GizmoVisual.Length(s, c);
        var tip = s.Camera.Project(c + Vec3.UnitX * length * 1.18)!.Value;
        var far = s.Camera.Project(c + Vec3.UnitX * length * 1.18 * 1.5)!.Value;
        s.PointerDown(new PointerInfo(tip.x, tip.y, true, false, false, true));     // Alt: no snapping to scale steps
        s.PointerMove(new PointerInfo(far.x, far.y, true, false, false, true));
        AssertVec(new(100, 10, 10), Box(s, bar).max, "nothing changes until the handle is let go");
        s.PointerUp(new PointerInfo(far.x, far.y, false, false, false, true));
        AssertClose(150, Box(s, bar).max.X, 0.5, "stretched to 1.5 times along X");
        AssertClose(10, Box(s, bar).max.Z, 1e-6, "height unchanged");

        gizmo.Key(s, "Escape");
        Assert.Equal(["Move", "Rotate"], vm.GizmoOperations);       // nothing held: nothing to scale
        Assert.Equal(0, gizmo.Mode);
    }

    [AvaloniaFact(DisplayName = "GZM-H009 | Move grabs a whole body when every one of its faces is picked; some of its faces still open Move to slide them")]
    [Trait("TestId", "GZM-H009")]
    public void AllFacesMeanTheBody()
    {
        var vm = new MainViewModel(); var s = vm.Session;
        var box = CreateBoxBody(s, Vec3.Zero, 10, 10, 10);
        s.SelectItems(Enumerable.Range(0, 6).Select(i => new SelItem(box.Id, SelKind.Face, i)));
        vm.MoveWithGizmo();
        Assert.Null(s.ActiveCommand);
        Assert.Equal([SelItem.Of(box)], s.Datum!.Targets);
        s.HideDatumGizmo();

        s.SelectItems(Enumerable.Range(0, 5).Select(i => new SelItem(box.Id, SelKind.Face, i)));
        vm.MoveWithGizmo();
        Assert.IsType<TransformCommand>(s.ActiveCommand);
    }

    [AvaloniaFact(DisplayName = "GZM-H010 | On Direct Modeling, Move sets the face gizmo aside while the Move gizmo is up, and the face gizmo comes back when it closes")]
    [Trait("TestId", "GZM-H010")]
    public void MoveWorksOnDirectModeling()
    {
        var vm = new MainViewModel(); var s = vm.Session;
        var box = CreateBoxBody(s, Vec3.Zero, 10, 10, 10);
        vm.EnterDirectEdit();
        Assert.IsType<DirectEditCommand>(s.ActiveCommand);
        s.SelectItems(Enumerable.Range(0, 6).Select(i => new SelItem(box.Id, SelKind.Face, i)));
        vm.MoveWithGizmo();
        Assert.Null(s.ActiveCommand);
        Assert.True(s.DatumVisible);
        Assert.Equal([SelItem.Of(box)], s.Datum!.Targets);
        vm.GizmoOriginX = 20;                                      // a typed move raises state changes; the face gizmo must not take over
        Sync(s);
        Assert.True(s.DatumVisible);
        AssertClose(20, s.Find(box.Id)!.Shape.Props().Center.X, 1e-6, "box centre moved with the gizmo to X 20");
        s.HideDatumGizmo();
        Assert.IsType<DirectEditCommand>(s.ActiveCommand);
    }

    [AvaloniaFact(DisplayName = "GZM-H011 | D is the Move pivot key and does nothing without the Move gizmo")]
    [Trait("TestId", "GZM-H011")]
    public void PivotKey()
    {
        var action = Assert.Single(Hotkeys.Actions, h => h.Id == "gizmo.pivot");
        Assert.Equal("D", action.Default);
        var vm = new MainViewModel();
        Assert.False(vm.ToggleGizmoPivot());
        vm.Session.ShowDatumGizmo(Vec3.Zero);                      // a datum gizmo that is not the Move tool
        Assert.False(vm.ToggleGizmoPivot());
    }
}
