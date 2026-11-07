using Avalonia.Headless.XUnit;
using Prism.Core;
using Prism.Core.Sketching;
using Prism.Kernel;
using Prism.ViewModels;
using Xunit;

namespace Prism.HeadlessTests.Sketch;

/// <summary>The sketch Transform button puts the gizmo's panel on a selection of points and curves.</summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/unified-sketch.md">Two ways to sketch.</seealso>
[Trait("Feature", "Sketch")]
[Trait("Layer", "Headless")]
public class SketchTransformTests
{
    [AvaloniaFact(DisplayName = "SKT-H045 | Transform opens the gizmo panel on the selected points; typed scale and rotation transform them without a bind step")]
    [Trait("TestId", "SKT-H045")]
    public void TransformPanelActsOnSelection()
    {
        var vm = new MainViewModel();
        var session = vm.Session;
        session.EnterSketch(Plane3.XY, alignView: false);
        var sketch = session.Sketch!;
        sketch.Set3DEnabled(session, true);
        sketch.SetDrawingMode(session, flat: true, adjustView: false);
        var model = sketch.Model;
        int a = model.AddPoint(0, 0), b = model.AddPoint(40, 0);
        model.AddLine(a, b);
        sketch.Solve(session);
        sketch.SetTool(session, new SelectTool());
        sketch.SelPoints.Add(a); sketch.SelPoints.Add(b);

        vm.TransformSketchSelection();
        Assert.True(vm.ShowGizmoPanel);
        Assert.StartsWith("Scale the selection", vm.GizmoScaleHeader);

        // a shape selected whole moves with its plane, so measure in the world
        Vec3 World(int id) { var p = sketch.Model.P(id); return sketch.Plane.PointAt(p.X, p.Y) + sketch.Plane.Normal * p.Z; }
        vm.GizmoRotationZ = 90;
        Assert.InRange(Math.Abs(World(b).X - World(a).X), 0, 0.5);        // the line now runs along Y
        Assert.InRange(Math.Abs(World(b).Y - World(a).Y), 39.5, 40.5);
        vm.GizmoScaleX = 0.5;                                              // squash the two points together along the gizmo's X, which the turn carried onto the line
        Assert.InRange((World(b) - World(a)).Length, 19.5, 20.5);
    }

    [AvaloniaFact(DisplayName = "SKT-H046 | Dragging a scale block squashes the selection along that one gizmo axis only")]
    [Trait("TestId", "SKT-H046")]
    public void ScaleBlockScalesOneAxis()
    {
        var session = new CadSession();
        session.EnterSketch(Plane3.XY, alignView: false);
        var sketch = session.Sketch!;
        sketch.Set3DEnabled(session, true);
        sketch.SetDrawingMode(session, flat: true, adjustView: false);
        session.Camera.Orthographic = true; session.Camera.SetView(0, 90);        // straight down
        var model = sketch.Model;
        int a = model.AddPoint(20, 10), b = model.AddPoint(60, 30);
        model.AddLine(a, b);
        sketch.Solve(session);
        sketch.SetTool(session, new SelectTool());
        sketch.SelPoints.Add(a); sketch.SelPoints.Add(b);
        Assert.True(session.Key("GizmoSize"));

        var origin = sketch.GizmoFrame.Origin; double length = GizmoVisual.Length(session, origin);
        var tip = session.Camera.Project(origin + sketch.GizmoFrame.U * length * 1.18)!.Value;
        var grab = session.Camera.Project(origin + sketch.GizmoFrame.U * length * 0.59)!.Value;   // half way back toward the center: scale 0.5
        session.PointerMove(new(tip.x, tip.y, false, false, false, false));
        session.PointerDown(new(tip.x, tip.y, true, false, false, false));
        session.PointerMove(new(grab.x, grab.y, true, false, false, false));
        session.PointerUp(new(grab.x, grab.y, false, false, false, false));

        double Y(int id) => sketch.Model.P(id).Y;
        Assert.InRange(Y(a), 9.9, 10.1); Assert.InRange(Y(b), 29.9, 30.1);        // Y untouched
        Assert.InRange(sketch.Model.P(b).X - sketch.Model.P(a).X, 19, 21);        // X squashed from 40 to 20
    }
}
