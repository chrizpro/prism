using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Viewport;

/// <summary>
/// The Extrude arrow stands on the profile it was made for, wherever that profile is, and Zoom Selected brings the selection into view
/// (owner reports 2026-11-15: the arrow showed at the origin for a rectangle far away).
/// </summary>
[Trait("Feature", "Viewport handles")]
[Trait("Layer", "Headless")]
public class ExtrudeHandleTests
{
    [AvaloniaFact(DisplayName = "HDL-H040 | The Extrude arrow stands on a rectangle drawn far from the origin, even after an earlier extrude at the origin")]
    [Trait("TestId", "HDL-H040")]
    public void ExtrudeArrowFollowsTheProfile()
    {
        var session = new CadSession();
        var near = CreateRectangleSketch(session, Plane3.XY, 20, 20);
        var first = StartCommand(session, near, () => new ExtrudeCommand());
        Assert.NotEmpty(first.Handles);
        session.AcceptCommandPanel();

        var far = CreateRectangleSketch(session, Plane3.XY, 40, 30, 100, 100);
        var second = StartCommand(session, far, () => new ExtrudeCommand());
        var handle = second.Handles[0];
        AssertPoint(new Vec3(120, 115, 0), handle.Origin, 1, "The arrow stands in the middle of the far rectangle (mm)");
    }

    [AvaloniaFact(DisplayName = "HDL-H042 | The Extrude arrow stands on the curves picked, also for a chain of edges or an open outline far from the origin")]
    [Trait("TestId", "HDL-H042")]
    public void ExtrudeArrowFollowsPickedCurves()
    {
        var session = new CadSession();
        var open = CreateRectangleSketch(session, Plane3.XY, 40, 30, 100, 100, closed: false);
        var chain = StartCommand(session, open, () => new ExtrudeCommand(surface: true));
        AssertPoint(new Vec3(120, 115, 0), chain.Handles[0].Origin, 1, "Open outline: the arrow stands in the middle of it (mm)");
        session.CancelCommand();

        var closed = CreateRectangleSketch(session, Plane3.XY, 40, 30, 100, 100);
        var edge = Begin(session, () => new ExtrudeCommand(surface: true));
        Assert.True(edge.Pick(new SelItem(closed.Id, SelKind.Edge, 0), additive: false));
        Assert.NotEmpty(edge.Handles);
        AssertPoint(new Vec3(120, 115, 0), edge.Handles[0].Origin, 1, "A whole clicked outline: the arrow is in the middle of it, not at the origin (mm)");
    }

    [AvaloniaFact(DisplayName = "HDL-H041 | Zoom Selected centres a far-away body, and says so when nothing is selected")]
    [Trait("TestId", "HDL-H041")]
    public void ZoomSelectedCentresTheSelection()
    {
        var session = new CadSession();
        session.Camera.Width = 800; session.Camera.Height = 600;
        var near = CreateBoxBody(session, Vec3.Zero, 10, 10, 10);
        var far = CreateBoxBody(session, new Vec3(500, 300, 0), 20, 20, 20);
        session.SelectItems([]);
        Assert.False(session.ZoomSelection());
        session.SelectItems([SelItem.Of(far)]);
        Assert.True(session.ZoomSelection());
        AssertPoint(new Vec3(510, 310, 10), session.Camera.Target, 1e-6, "The view is centred on the selected body (mm)");
        Assert.InRange(session.Camera.Distance, 20, 400);
    }
}
