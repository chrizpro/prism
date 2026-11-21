using Avalonia.Headless.XUnit;
using Prism.Core;
using Prism.Core.Sketching;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Sketch;

/// <summary>Closing a spline by clicking back near its first point, however that point was placed. Owner report 2026-11-16: it was hard to snap to the start to close the loop.</summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/modeling/sketches/README.md">Sketch drawing modes.</seealso>
[Trait("Feature", "Sketch")]
[Trait("Layer", "Headless")]
public class SplineCloseTests
{
    static (double x, double y) Screen(CadSession session, double u, double v)
    {
        var p = session.Camera.Project(session.Sketch!.Plane.PointAt(u, v));
        Assert.NotNull(p);
        return (p.Value.x, p.Value.y);
    }

    static void Click(CadSession session, SplineTool tool, (double x, double y) at)
    {
        tool.PointerMove(session, new PointerInfo(at.x, at.y, false, false, false, false));
        tool.PointerDown(session, new PointerInfo(at.x, at.y, true, false, false, false, 1));
        tool.PointerUp(session, new PointerInfo(at.x, at.y, false, false, false, false));
    }

    static (CadSession session, SplineTool tool) FourFreePoints(bool controlPoints = false)
    {
        var session = new CadSession();
        session.EnterSketch(Plane3.XY);
        var tool = new SplineTool(controlPoints);
        session.Sketch!.SetTool(session, tool);
        foreach (var (u, v) in new[] { (10.0, 10.0), (50, 20), (70, -20), (30, -40) }) Click(session, tool, Screen(session, u, v));
        return (session, tool);
    }

    [AvaloniaFact(DisplayName = "SKT-H020 | Near the first point of a spline placed freely, the pointer snaps onto it and the point lights up")]
    [Trait("TestId", "SKT-H020")]
    public void PointerSnapsToTheFirstPoint()
    {
        var (session, tool) = FourFreePoints();
        var near = Screen(session, 10, 10); near.x += 8; near.y -= 5;                    // about 9 pixels away
        tool.PointerMove(session, new PointerInfo(near.x, near.y, false, false, false, false));
        var cursor = session.Sketch!.Cursor;
        Assert.NotNull(cursor);
        AssertNear(10, cursor.Value.U, "Snapped U (mm)"); AssertNear(10, cursor.Value.V, "Snapped V (mm)");
        Assert.Equal("Click to close the spline", session.Sketch.Hint);
        var overlay = new Overlay(); tool.BuildOverlay(session, overlay);
        Assert.Contains(overlay.Dots, d => d.Color == 0xFFFFC53D);                          // the larger marker on the first point

        var far = Screen(session, 10, 10); far.x += 60;                                      // well away: no snap, no hint
        tool.PointerMove(session, new PointerInfo(far.x, far.y, false, false, false, false));
        Assert.NotEqual("Click to close the spline", session.Sketch.Hint);
        var overlayFar = new Overlay(); tool.BuildOverlay(session, overlayFar);
        Assert.DoesNotContain(overlayFar.Dots, d => d.Color == 0xFFFFC53D);
    }

    [AvaloniaFact(DisplayName = "SKT-H021 | Clicking near the first point closes the spline into one closed curve through all four points")]
    [Trait("TestId", "SKT-H021")]
    public void ClickingNearTheStartClosesIt()
    {
        var (session, tool) = FourFreePoints();
        var near = Screen(session, 10, 10); near.x += 8;
        Click(session, tool, near);
        var spline = Assert.Single(session.Sketch!.Model.Entities);
        Assert.True(spline.Closed, "the spline must be closed");
        Assert.Equal(4, spline.Pts.Count);                                                  // the closing click is not a fifth point, and no real point is lost
        using var curve = Assert.Single(session.Sketch.Model.ToShapes(session.Sketch.Plane));
        Assert.True(curve.IsValid);
    }

    [AvaloniaFact(DisplayName = "SKT-H022 | Fewer than three points cannot close, and control-point splines do not close by clicking the start")]
    [Trait("TestId", "SKT-H022")]
    public void CloseNeedsThreePointsAndFitMode()
    {
        var session = new CadSession();
        session.EnterSketch(Plane3.XY);
        var tool = new SplineTool(false);
        session.Sketch!.SetTool(session, tool);
        Click(session, tool, Screen(session, 10, 10));
        Click(session, tool, Screen(session, 50, 20));
        var near = Screen(session, 10, 10); near.x += 4;
        tool.PointerMove(session, new PointerInfo(near.x, near.y, false, false, false, false));
        Assert.NotEqual("Click to close the spline", session.Sketch.Hint);                 // two points so far: nothing to close

        var (session2, tool2) = FourFreePoints(controlPoints: true);
        var near2 = Screen(session2, 10, 10); near2.x += 4;
        tool2.PointerMove(session2, new PointerInfo(near2.x, near2.y, false, false, false, false));
        Assert.NotEqual("Click to close the spline", session2.Sketch!.Hint);
    }
}
