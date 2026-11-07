using Avalonia.Headless.XUnit;
using Prism.Core;
using Prism.Core.Sketching;
using Prism.Kernel;
using Xunit;

namespace Prism.HeadlessTests.Sketch;

/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/unified-sketch.md">Drawing in space across planes.</seealso>
[Trait("Feature", "Sketch")]
[Trait("Layer", "Headless")]
public class SpaceSplineTests
{
    static readonly Vec3[] Path = [new(10, 15, 0), new(30, 0, 0), new(50, 0, 20), new(70, 0, 30)];

    /// <summary>Draws two points on XY, switches the gizmo to its XZ plane and draws two more there, as a user drawing in space would.</summary>
    static (CadSession session, SketchContext sketch, SplineTool tool) DrawAcrossPlanes()
    {
        var session = new CadSession();
        session.EnterSketch(Plane3.XY, alignView: false);
        var sketch = session.Sketch!;
        sketch.Set3DEnabled(session, true);
        sketch.SetDrawingMode(session, flat: false, adjustView: false);
        sketch.SetTool(session, new SplineTool(false));
        foreach (var point in Path[..2]) { var (u, v) = sketch.Plane.Project(point); ((SplineTool)sketch.Tool!).Feed(session, u, v); }
        sketch.PresetPlane(session, 1);   // the gizmo's XZ plane, through the last placed point
        var tool = Assert.IsType<SplineTool>(sketch.Tool);
        foreach (var point in Path[2..]) { var (u, v) = sketch.Plane.Project(point); tool.Feed(session, u, v); }
        return (session, sketch, tool);
    }

    static IEnumerable<(SketchSection section, SkEntity spline)> Splines(SketchContext sketch) =>
        sketch.DrawingPlanes.SelectMany(section => section.Model.Entities.Where(e => e.Type == EntType.Spline).Select(e => (section, e)));

    [AvaloniaFact(DisplayName = "SKT-H019 | A spline continued onto another gizmo plane stays one curve")]
    [Trait("TestId", "SKT-H019")]
    public void SplineContinuesAcrossPlanes()
    {
        var (session, sketch, tool) = DrawAcrossPlanes();
        Assert.Empty(Splines(sketch));                     // nothing is committed by the plane switch
        Assert.True(Math.Abs(sketch.Plane.Normal.Y) > .99, "drawing moved to the XZ plane");
        tool.FinishFeed(session);

        var (home, spline) = Assert.Single(Splines(sketch));
        Assert.Same(sketch.DrawingPlanes[0], home);        // it belongs to the plane it was started on
        var points = home.Model.PointsOf(spline).Select(id => home.Model.P(id))
            .Select(p => home.Plane.PointAt(p.X, p.Y) + home.Plane.Normal * p.Z).ToList();
        Assert.Equal(Path.Length, points.Count);           // the shared point is not doubled
        for (int i = 0; i < Path.Length; i++)
            Assert.True(points[i].DistanceTo(Path[i]) < 1e-6, $"point {i} at {points[i]}, expected {Path[i]}");
        Assert.True(Math.Abs(sketch.Plane.Normal.Y) > .99, "the user keeps drawing on the plane they switched to");
    }

    /// <summary>Clicks through the session as the viewport does, so snapping decides where the point lands.</summary>
    static void Click(CadSession session, Vec3 world)
    {
        var pixel = session.Camera.Project(world)!.Value;
        session.PointerMove(new(pixel.x, pixel.y, false, false, false, false));
        session.PointerDown(new(pixel.x, pixel.y, true, false, false, false));
        session.PointerUp(new(pixel.x, pixel.y, false, false, false, false));
    }

    static Vec3 FirstSplinePoint(SketchContext sketch)
    {
        var (section, spline) = Assert.Single(Splines(sketch));
        var p = section.Model.P(section.Model.PointsOf(spline).First());
        return section.Plane.PointAt(p.X, p.Y) + section.Plane.Normal * p.Z;
    }

    static SketchContext DrawInSpace(CadSession session, Plane3 plane)
    {
        session.EnterSketch(plane, alignView: false);
        var sketch = session.Sketch!;
        sketch.Set3DEnabled(session, true);
        sketch.SetDrawingMode(session, flat: false, adjustView: false);
        session.Camera.Orthographic = false; session.Camera.Yaw = -.6; session.Camera.Pitch = .5;
        return sketch;
    }

    /// <summary>Starts a spline on the circle (radius 20 on world XY) while drawing on a plane 30 mm above it, then checks where it began.</summary>
    static void AssertSplineStartsOnCircle(CadSession session, SketchContext sketch)
    {
        sketch.SetTool(session, new SplineTool(false));
        Click(session, new Vec3(20, 0, 0));
        Click(session, new Vec3(10, 10, 30));
        Click(session, new Vec3(-5, 15, 30));
        ((SplineTool)sketch.Tool!).FinishFeed(session);
        var start = FirstSplinePoint(sketch);
        Assert.True(Math.Abs(start.Z) < 1e-6, $"first point keeps the circle's depth, got {start}");
        Assert.True(Math.Abs(new Vec3(start.X, start.Y, 0).Length - 20) < 0.05, $"first point lies on the circle, got {start}");
    }

    [AvaloniaFact(DisplayName = "SKT-H021 | A spline in space starts on another sketch's circle at its real depth")]
    [Trait("TestId", "SKT-H021")]
    public void SplineStartsOnOtherSketchCircle()
    {
        var session = new CadSession();
        session.EnterSketch(Plane3.XY, alignView: false);
        var flat = session.Sketch!;
        flat.Edit(session, "circle", () => flat.Model.AddCircle(flat.Model.AddPoint(0, 0), 20));
        session.ExitSketch(true);
        AssertSplineStartsOnCircle(session, DrawInSpace(session, Plane3.XY with { Origin = new Vec3(0, 0, 30) }));
    }

    [AvaloniaFact(DisplayName = "SKT-H028 | With plane, a curve started on an existing curve takes its plane from that click, in a casually tilted view")]
    [Trait("TestId", "SKT-H028")]
    public void NewCurveFromExistingCurveTakesPlaneFromClick()
    {
        var session = new CadSession();
        session.EnterSketch(Plane3.XY, alignView: false);
        var flat = session.Sketch!;
        flat.Edit(session, "circle", () => flat.Model.AddCircle(flat.Model.AddPoint(0, 0), 20));
        session.ExitSketch(true);
        var sketch = DrawInSpace(session, Plane3.XY with { Origin = new Vec3(0, 0, 30) });   // the current plane is 30 mm above the circle
        session.Camera.Pitch = 1.2;                           // looking mostly down, but well short of a straight-on top view
        sketch.SetTool(session, new SplineTool(false));
        Click(session, new Vec3(20, 0, 0));                  // on the circle, off the current plane
        Click(session, new Vec3(-5, 15, 0));
        ((SplineTool)sketch.Tool!).FinishFeed(session);
        var (section, spline) = Assert.Single(Splines(sketch));
        foreach (int id in section.Model.PointsOf(spline))
        {
            var p = section.Model.P(id);
            var world = section.Plane.PointAt(p.X, p.Y) + section.Plane.Normal * p.Z;
            Assert.True(Math.Abs(world.Z) < 1e-6, $"every point lies on the plane through the circle point, got {world}");
        }
    }

    [AvaloniaFact(DisplayName = "SKT-H042 | No plane: a curve started on a curve of another sketch begins at its real height and carries on at that height")]
    [Trait("TestId", "SKT-H042")]
    public void NoPlaneBeginsOnExistingGeometry()
    {
        var session = new CadSession();
        session.EnterSketch(Plane3.XY with { Origin = new Vec3(0, 0, 30) }, alignView: false);
        var other = session.Sketch!;
        other.Edit(session, "circle", () => other.Model.AddCircle(other.Model.AddPoint(0, 0), 20));
        session.ExitSketch(true);

        session.EnterSketch(Plane3.XY, alignView: false);
        var sketch = session.Sketch!;
        sketch.SetDrawingMode(session, flat: true, adjustView: false);       // No plane
        session.Camera.SetView(0, 90); session.Camera.Orthographic = true;   // looking straight down
        sketch.SetTool(session, new SplineTool(false));
        Click(session, new Vec3(20, 0, 30));                                  // on the circle, 30 mm above the sketch plane
        Click(session, new Vec3(-5, 15, 0));
        ((SplineTool)sketch.Tool!).FinishFeed(session);
        var (section, spline) = Assert.Single(Splines(sketch));
        var ends = section.Model.PointsOf(spline).Select(id => section.Model.P(id)).Select(p => section.Plane.PointAt(p.X, p.Y) + section.Plane.Normal * p.Z).ToList();
        Assert.All(ends, w => Assert.True(Math.Abs(w.Z - 30) < 1e-6, $"every point lies 30 mm up, got {w}"));
        Assert.True(new Vec3(ends[0].X, ends[0].Y, 0).DistanceTo(new Vec3(20, 0, 0)) < 0.5 || Math.Abs(new Vec3(ends[0].X, ends[0].Y, 0).Length - 20) < 0.5, "the curve begins on the circle");
    }

    [AvaloniaFact(DisplayName = "SKT-H043 | A new sketch is a plain 2D sketch; a NURBS sketch is a 3D sketch with No plane")]
    [Trait("TestId", "SKT-H043")]
    public void NewSketchDefaults()
    {
        var session = new CadSession();
        session.EnterSketch(Plane3.XY, alignView: false);
        Assert.False(session.Sketch!.Is3DEnabled);
        session.ExitSketch(false);
        session.EnterSketch(Plane3.XY, new SketchModel { SharedNurbsCurves = true }, alignView: false);
        Assert.True(session.Sketch!.Is3DEnabled);
        Assert.True(session.Sketch.IsDrawingFlat);
    }

    [AvaloniaFact(DisplayName = "SKT-H044 | No plane: a new curve in empty space lies on the plane facing the view, however far the view is tilted")]
    [Trait("TestId", "SKT-H044")]
    public void NoPlaneFollowsTheViewEverywhere()
    {
        var session = new CadSession();
        session.EnterSketch(Plane3.XY, alignView: false);
        var sketch = session.Sketch!;
        sketch.Set3DEnabled(session, true);
        sketch.SetDrawingMode(session, flat: true, adjustView: false);
        session.Camera.Orthographic = true; session.Camera.SetView(0, 20);     // mostly a front view, tilted well short of straight on
        sketch.SetTool(session, new SplineTool(false));
        Click(session, new Vec3(10, 0, 10));
        Click(session, new Vec3(40, 0, 25));
        ((SplineTool)sketch.Tool!).FinishFeed(session);
        var (section, spline) = Assert.Single(Splines(sketch));
        foreach (int id in section.Model.PointsOf(spline))
        {
            var p = section.Model.P(id);
            var world = section.Plane.PointAt(p.X, p.Y) + section.Plane.Normal * p.Z;
            Assert.True(Math.Abs(world.Y) < 1e-6, $"the curve lies on the plane facing the view, got {world}");
        }
    }

    [AvaloniaFact(DisplayName = "SKT-H022 | A spline in space starts on a curve from another drawing plane of the same sketch")]
    [Trait("TestId", "SKT-H022")]
    public void SplineStartsOnOtherDrawingPlaneCurve()
    {
        var session = new CadSession();
        var sketch = DrawInSpace(session, Plane3.XY);
        sketch.Edit(session, "circle", () => sketch.Model.AddCircle(sketch.Model.AddPoint(0, 0), 20));
        sketch.SetDrawingPlane(session, Plane3.XY with { Origin = new Vec3(0, 0, 30) }, forceNew: true);
        Assert.Equal(1, sketch.ActivePlane);
        AssertSplineStartsOnCircle(session, sketch);
    }

    [AvaloniaFact(DisplayName = "SKT-H027 | A new curve starts on the off-plane endpoint of a spline drawn across planes")]
    [Trait("TestId", "SKT-H027")]
    public void NewCurveStartsOnOffPlaneEndpoint()
    {
        var (session, sketch, tool) = DrawAcrossPlanes();
        tool.FinishFeed(session);
        var end = Path[0];                                  // stored as an offset from the plane the spline now lives on
        var (home, spline) = Assert.Single(Splines(sketch));
        var stored = home.Model.P(home.Model.PointsOf(spline).First());
        Assert.True(Math.Abs(stored.Z) > 1, "this endpoint really sits off its plane");

        // From the side the spline's plane is edge-on, as in the reported view; the endpoint must still be reachable.
        session.Camera.SetView(90, 20); session.Camera.Orthographic = false;
        sketch.SetTool(session, new SplineTool(false));
        Click(session, end);
        var start = ((SplineTool)sketch.Tool!).LastPlacedWorld(session);
        Assert.NotNull(start);
        Assert.True(start!.Value.DistanceTo(end) < 1e-6, $"the new curve starts on the endpoint, got {start}");
    }

    [AvaloniaFact(DisplayName = "SKT-H020 | One undo removes a spline drawn across planes")]
    [Trait("TestId", "SKT-H020")]
    public void SplineAcrossPlanesUndoesInOneStep()
    {
        var (session, sketch, tool) = DrawAcrossPlanes();
        tool.FinishFeed(session);
        Assert.Single(Splines(sketch));
        Assert.True(sketch.TryUndo(session));
        Assert.Empty(Splines(sketch));
    }
}
