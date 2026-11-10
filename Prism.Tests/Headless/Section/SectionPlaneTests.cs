using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Section;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/site/guide/modeling.md#section-view">Section View guide.</seealso>
[Trait("Feature", "Section")]
[Trait("Layer", "Headless")]
public sealed class SectionPlaneTests
{
    [AvaloniaFact(DisplayName = "SEC-H001 | The movable section centers on an off-origin box and follows its gizmo")]
    [Trait("TestId", "SEC-H001")]
    public void MovableSectionTracksTravelTiltAndDisplaySize()
    {
        var session = new CadSession();
        var body = CreateBoxBody(session, new Vec3(25, 40, 60), 20, 30, 40);
        session.ZoomExtents();
        session.Camera.Width = 1000;
        session.Camera.Height = 800;
        var command = Begin(session, () => new SectionCommand());
        SetChoice(command, "Section plane", "Movable plane");
        BuildOutput(command);
        var initial = Assert.IsType<CadSession.ClipPlane>(session.Clip);
        Assert.Equal(new Vec3(35, 55, 80), initial.Origin);
        Assert.Equal("600 mm²", Result(command, "Section area"));
        Assert.Equal(4, command.Handles.Count);

        var travel = command.Handles[0];
        var from = session.Camera.Project(CadSession.HandleTip(travel))!.Value;
        var to = session.Camera.Project(travel.Origin + travel.Dir * 5)!.Value;
        Assert.True(session.PointerDown(new PointerInfo(from.x, from.y, true, false, false, false)));
        session.PointerMove(new PointerInfo(to.x, to.y, true, false, false, true));
        session.PointerUp(new PointerInfo(to.x, to.y, false, false, false, true));
        Assert.Equal(85, session.Clip!.Origin.Z, 5);

        SetNumber(command, "Tilt about X", 30);
        SetNumber(command, "Tilt about Y", 20);
        BuildOutput(command);
        var tilted = session.Clip!;
        Assert.InRange(tilted.Normal.Z, 0.81, 0.82);
        var overlay = new Overlay();
        command.BuildOverlay(session, overlay);
        Assert.Equal(2, overlay.Tris.Count);
        foreach (var triangle in overlay.Tris)
            foreach (var point in new[] { triangle.A, triangle.B, triangle.C })
                Assert.Equal(0, Vec3.Dot(point - tilted.Origin, tilted.Normal), 8);
        SetNumber(command, "Plane display size", 150);
        BuildOutput(command);
        Assert.Equal(tilted, session.Clip);
        Assert.Same(body, Assert.Single(session.Objects));
        session.CancelCommand();
        Assert.Null(session.Clip);
    }
}
