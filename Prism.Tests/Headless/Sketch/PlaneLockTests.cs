using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Prism.Core;
using Prism.Core.Sketching;
using Prism.Kernel;
using Xunit;

namespace Prism.HeadlessTests.Sketch;

/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/unified-sketch.md">Drawing in space: plane lock.</seealso>
[Trait("Feature", "Sketch")]
[Trait("Layer", "Headless")]
public class PlaneLockTests
{
    /// <summary>Rests the pointer on one of the gizmo's side plane squares until the cue shows, then holds Ctrl to lock it.</summary>
    static (CadSession session, SketchContext sketch) LockSidePlane()
    {
        var now = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
        var saved = SketchContext.Clock; SketchContext.Clock = () => now;
        try
        {
            var session = new CadSession();
            session.EnterSketch(Plane3.XY, alignView: false);
            var sketch = session.Sketch!;
            sketch.Set3DEnabled(session, true);
            sketch.SetDrawingMode(session, flat: false, adjustView: false);
            session.Camera.Orthographic = false; session.Camera.Yaw = -.6; session.Camera.Pitch = .5;
            sketch.SetTool(session, new SplineTool(false));
            var g = session.Camera.Project(sketch.GizmoFrame.Origin)!.Value;
            for (double r = 8; r < 90 && sketch.LockedPlane < 0; r += 3)
                for (double a = 0; a < 6.28 && sketch.LockedPlane < 0; a += .15)
                {
                    double x = g.x + r * Math.Cos(a), y = g.y + r * Math.Sin(a);
                    session.PointerMove(new(x, y, false, false, false, false));
                    if (sketch.GizmoHit(session, x, y, false) is not (5 or 6)) continue;   // XZ or YZ: a plane other than the drawing plane
                    now += TimeSpan.FromSeconds(2);
                    session.PointerMove(new(x, y, false, false, false, false));
                    session.PointerMove(new(x, y, false, false, true, false));
                }
            Assert.True(sketch.LockedPlane > 0, "Ctrl locks a side plane");
            return (session, sketch);
        }
        finally { SketchContext.Clock = saved; }
    }

    [AvaloniaFact(DisplayName = "SKT-H023 | With a plane locked by Ctrl, a click on the gizmo starts the curve instead of dragging the gizmo")]
    [Trait("TestId", "SKT-H023")]
    public void CtrlLockedClickOnGizmoStartsCurve()
    {
        var (session, sketch) = LockSidePlane();
        var origin = sketch.GizmoFrame.Origin;
        var g = session.Camera.Project(origin)!.Value;
        session.PointerDown(new(g.x, g.y, true, false, true, false));
        session.PointerMove(new(g.x + 1, g.y + 1, true, false, true, false));
        session.PointerUp(new(g.x + 1, g.y + 1, false, false, true, false));
        var first = ((SplineTool)sketch.Tool!).LastPlacedWorld(session);
        Assert.NotNull(first);
        Assert.True(first!.Value.DistanceTo(origin) < 1, $"the first point is placed at the gizmo, got {first}");
        Assert.True(sketch.LockedPlane > 0, "the lock holds while Ctrl stays down");
    }

    static void Click(CadSession session, Vec3 world)
    {
        var pixel = session.Camera.Project(world)!.Value;
        session.PointerMove(new(pixel.x, pixel.y, false, false, false, false));
        session.PointerDown(new(pixel.x, pixel.y, true, false, false, false));
        session.PointerUp(new(pixel.x, pixel.y, false, false, false, false));
    }

    [AvaloniaFact(DisplayName = "SKT-H025 | After a tool change, the next shape drawn in space starts on the plane facing the view")]
    [Trait("TestId", "SKT-H025")]
    public void NewShapeFollowsTheView()
    {
        var session = new CadSession();
        session.EnterSketch(Plane3.XY, alignView: false);
        var sketch = session.Sketch!;
        sketch.Set3DEnabled(session, true);
        sketch.SetDrawingMode(session, flat: false, adjustView: false);
        session.Camera.Orthographic = false; session.Camera.Yaw = -.6; session.Camera.Pitch = .5;
        sketch.SetTool(session, new LineTool(0));
        Click(session, new Vec3(0, 0, 0));
        Click(session, new Vec3(20, 10, 0));
        Assert.True(Math.Abs(sketch.Plane.Normal.Z) > .99, "an angled view keeps drawing on XY");

        // Change tools, then turn to the Front view, where XY is edge-on: the next shape goes on the front-facing plane.
        sketch.SetTool(session, new SplineTool(false));
        session.Camera.SetView(0, 0); session.Camera.Orthographic = true;
        Vec3[] targets = [new(5, 0, 10), new(25, 0, 20), new(40, 0, 5)];
        foreach (var target in targets) Click(session, target);
        ((SplineTool)sketch.Tool!).FinishFeed(session);

        Assert.True(Math.Abs(sketch.Plane.Normal.Y) > .99, "drawing moved to the gizmo's XZ plane");
        var section = sketch.DrawingPlanes[sketch.ActivePlane];
        var spline = Assert.Single(section.Model.Entities, e => e.Type == EntType.Spline);
        var points = section.Model.PointsOf(spline).Select(id => section.Model.P(id)).Select(p => section.Plane.PointAt(p.X, p.Y) + section.Plane.Normal * p.Z).ToList();
        Assert.Equal(targets.Length, points.Count);
        for (int i = 0; i < targets.Length; i++)
        {
            Assert.True(Math.Abs(points[i].X - targets[i].X) < 1e-6 && Math.Abs(points[i].Z - targets[i].Z) < 1e-6, $"point {i} at {points[i]} under the click {targets[i]}");
            Assert.True(Math.Abs(points[i].Y - points[0].Y) < 1e-6, "every point lies on one front-facing plane");
        }
    }

    [AvaloniaFact(DisplayName = "SKT-H026 | A new curve started on an existing endpoint attaches to it, even when its plane is nearly edge-on")]
    [Trait("TestId", "SKT-H026")]
    public void NewCurveStartsOnExistingEndpoint()
    {
        var session = new CadSession();
        session.EnterSketch(Plane3.XY, alignView: false);
        var sketch = session.Sketch!;
        sketch.Set3DEnabled(session, true);
        sketch.SetDrawingMode(session, flat: false, adjustView: false);
        var first = new SplineTool(false);
        sketch.SetTool(session, first);
        first.Feed(session, 0, 0); first.Feed(session, 20, 10); first.Feed(session, 40, 0);
        first.FinishFeed(session);

        // Turn until XY is close to edge-on (but not a snapped view), then start a new curve on the old curve's end.
        session.Camera.Orthographic = false; session.Camera.Yaw = -.6; session.Camera.Pitch = .12;
        sketch.SetTool(session, new SplineTool(false));
        var end = new Vec3(40, 0, 0);
        Click(session, end);
        var start = ((SplineTool)sketch.Tool!).LastPlacedWorld(session);
        Assert.NotNull(start);
        Assert.True(start!.Value.DistanceTo(end) < 1e-6, $"the new curve starts on the endpoint, got {start}");
    }

    [AvaloniaFact(DisplayName = "SKT-H024 | Ctrl that holds a plane lock orbits with a right drag; Shift still pans")]
    [Trait("TestId", "SKT-H024")]
    public void CtrlLockedRightDragOrbits()
    {
        var plain = new CadSession();
        Assert.True(Prism.Rendering.GlViewport.PansWith(plain, KeyModifiers.Control), "Ctrl + right drag pans normally");
        var (session, _) = LockSidePlane();
        Assert.False(Prism.Rendering.GlViewport.PansWith(session, KeyModifiers.Control), "Ctrl holding a plane lock orbits");
        Assert.True(Prism.Rendering.GlViewport.PansWith(session, KeyModifiers.Shift | KeyModifiers.Control), "Shift still pans");
    }
}
