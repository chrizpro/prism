using Avalonia.Headless.XUnit;
using Prism.Core;
using Prism.Kernel;
using Prism.Rendering;
using Xunit;

namespace Prism.HeadlessTests.Viewport;

/// <summary>
/// The reference grid's plane (<see cref="CadSession.DisplayGridPlane"/>, used by the viewport grid and the
/// axis cross): an orthographic view snapped to a world axis shows the grid of the plane being looked at —
/// flat and 3D sketches alike; perspective keeps the one grid of the plane picked for the sketch or command.
/// </summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/unified-sketch.md">Unified sketch: the reference grid.</seealso>
[Trait("Feature", "Viewport grid")]
[Trait("Layer", "Headless")]
public class GridPlaneTests
{
    [AvaloniaFact(DisplayName = "GRID-H001 | A flat sketch on XY looked at from the front in ortho shows the front (XZ) grid")]
    [Trait("TestId", "GRID-H001")]
    public void OrthoFrontViewShowsFrontGrid()
    {
        var s = new CadSession();
        s.EnterSketch(Plane3.XY);                    // flat sketch: orthographic, looking straight down
        Assert.True(s.Camera.Orthographic);
        s.Camera.SetView(0, 0);                      // the snapped Front view
        Assert.Equal(Plane3.XZ, s.DisplayGridPlane());
        Assert.Equal(Plane3.XZ, GlViewport.DisplayGridPlane(s));
    }

    [AvaloniaFact(DisplayName = "GRID-H002 | In perspective the grid stays on the sketch's entry plane even at a snapped view")]
    [Trait("TestId", "GRID-H002")]
    public void PerspectiveKeepsEntryPlane()
    {
        var s = new CadSession();
        s.EnterSketch(Plane3.XY);
        s.Sketch!.SetDrawingMode(s, flat: false);    // drawing in space returns to perspective
        Assert.False(s.Camera.Orthographic);
        s.Camera.SetView(0, 0);
        Assert.Equal(Plane3.XY, s.DisplayGridPlane());

        s.Ortho = true;                              // a 3D sketch in ortho follows the view like a flat one
        Assert.Equal(Plane3.XZ, s.DisplayGridPlane());
    }

    [AvaloniaFact(DisplayName = "GRID-H003 | A flat sketch on a tilted plane keeps its own grid when the ortho view is off-axis")]
    [Trait("TestId", "GRID-H003")]
    public void TiltedSketchKeepsOwnPlane()
    {
        var s = new CadSession();
        var tilted = new Plane3(Vec3.Zero, Vec3.UnitX, new Vec3(0, 1, 1).Normalized());
        s.EnterSketch(tilted);                       // ortho looking normal to the tilted plane: not axis-aligned
        Assert.True(s.Camera.Orthographic);
        Assert.Null(s.Camera.AxisFacingPlane());
        Assert.Equal(tilted, s.DisplayGridPlane());
    }

    [AvaloniaFact(DisplayName = "GRID-H004 | With nothing picked, an ortho snapped view shows its plane; a perspective iso shows the ground")]
    [Trait("TestId", "GRID-H004")]
    public void IdleViewsFollowTheCamera()
    {
        var s = new CadSession();
        s.Camera.SetView(0, 0);
        s.Camera.Orthographic = true;
        Assert.Equal(Plane3.XZ, s.DisplayGridPlane());

        s.Camera.Orthographic = false;
        s.Camera.SetView(-35, 28);                   // orbited perspective: the single reference grid is the ground plane
        Assert.Equal(Plane3.XY, s.DisplayGridPlane());
    }
}
