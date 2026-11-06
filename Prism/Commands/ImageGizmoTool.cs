using System;
using Prism.Core;
using Prism.Kernel;

namespace Prism.Commands;

/// <summary>Planar reference-image manipulator using the shared gizmo renderer and picking.</summary>
public sealed class ImageGizmoTool(IImageGizmoCommand cmd) : IViewportTool
{
    enum Drag { None, Axis, Plane, Rotate, Scale }
    Drag drag;
    int axis, hotAxis = -1, hotOperation = -1;
    bool hotCenter;
    Vec3 center, start, basisU, basisV;
    Vec3[] axes = [];
    Plane3 dragPlane;
    double startU, startV, startTurn, startWidth, startParam, startAngle, startDistance;
    public string Prompt => "Drag the gizmo. T moves, R rotates, and S scales; choose the same modes in the panel.";

    static double? AxisParam(Vec3 o, Vec3 d, Vec3 c, Vec3 a)
    {
        var w = c - o; double b = Vec3.Dot(a, d), dd = Vec3.Dot(a, w), ee = Vec3.Dot(d, w), den = 1 - b * b;
        return den < 1e-6 ? null : (b * ee - dd) / den;
    }

    static Vec3[] FrameAxes(Plane3 p) => [p.U.Normalized(), p.V.Normalized(), p.Normal.Normalized()];
    (int axis, bool center, int operation) Hit(CadSession s, double x, double y, Plane3 p) =>
        GizmoVisual.Hit(s, x, y, _ => p.Origin, FrameAxes(p), cmd.GizmoMode.Index, planar: true, axisScale: false);

    public bool PointerDown(CadSession s, PointerInfo p)
    {
        if (!p.Left || cmd.GizmoPlane is not { } plane) return false;
        var (picked, pickedCenter, op) = Hit(s, p.X, p.Y, plane);
        if (picked < 0 && !pickedCenter) return false;
        if (op == 1 && picked != 2 || op == 2 && !pickedCenter) return false;
        cmd.GizmoMode.Index = op;
        center = plane.Origin; axes = FrameAxes(plane); axis = picked;
        double reverse = -cmd.GizmoTurn.Value * Math.PI / 180;
        basisU = ImageUtil.Rotate(axes[0], axes[2], reverse);
        basisV = ImageUtil.Rotate(axes[1], axes[2], reverse);
        startU = cmd.GizmoU.Value; startV = cmd.GizmoV.Value;
        startTurn = cmd.GizmoTurn.Value; startWidth = cmd.GizmoWidth.Value;
        var (o, d) = s.Camera.Ray(p.X, p.Y);
        dragPlane = new Plane3(center, axes[0], axes[1]);
        if (op == 0 && pickedCenter)
        {
            if (dragPlane.Intersect(o, d) is not { } hit) return false;
            start = hit; drag = Drag.Plane;
        }
        else if (op == 0)
        {
            if (AxisParam(o, d, center, axes[axis]) is not { } t) return false;
            startParam = t; drag = Drag.Axis;
        }
        else if (op == 1)
        {
            if (dragPlane.Intersect(o, d) is not { } hit) return false;
            startAngle = Math.Atan2(Vec3.Dot(hit - center, axes[1]), Vec3.Dot(hit - center, axes[0])); drag = Drag.Rotate;
        }
        else
        {
            var projected = s.Camera.Project(center); if (projected == null) return false;
            startDistance = Math.Max(10, Math.Sqrt(Math.Pow(projected.Value.x - p.X, 2) + Math.Pow(projected.Value.y - p.Y, 2))); drag = Drag.Scale;
        }
        s.Invalidate(); return true;
    }

    public void PointerMove(CadSession s, PointerInfo p)
    {
        if (drag == Drag.None)
        {
            if (cmd.GizmoPlane is not { } plane) return;
            var (a, c, op) = Hit(s, p.X, p.Y, plane);
            if (a != hotAxis || c != hotCenter || op != hotOperation) { hotAxis = a; hotCenter = c; hotOperation = op; s.Invalidate(); }
            return;
        }
        var (o, d) = s.Camera.Ray(p.X, p.Y);
        Vec3? delta = drag switch
        {
            Drag.Axis when AxisParam(o, d, center, axes[axis]) is { } t => axes[axis] * (t - startParam),
            Drag.Plane when dragPlane.Intersect(o, d) is { } hit => hit - start,
            _ => null
        };
        if (delta is { } movement)
        {
            // Snap in the picture's original parameter frame, not world coordinates.
            double u = Vec3.Dot(movement, basisU), v = Vec3.Dot(movement, basisV);
            if (GizmoSettings.Current.Snap && !p.Alt && GizmoSettings.Current.MoveStep > 0)
            { u = Math.Round(u / GizmoSettings.Current.MoveStep) * GizmoSettings.Current.MoveStep; v = Math.Round(v / GizmoSettings.Current.MoveStep) * GizmoSettings.Current.MoveStep; }
            cmd.GizmoU.Value = startU + u; cmd.GizmoV.Value = startV + v;
        }
        else if (drag == Drag.Rotate && dragPlane.Intersect(o, d) is { } hit)
        {
            double angle = Math.Atan2(Vec3.Dot(hit - center, axes[1]), Vec3.Dot(hit - center, axes[0])) - startAngle;
            while (angle > Math.PI) angle -= 2 * Math.PI; while (angle < -Math.PI) angle += 2 * Math.PI;
            cmd.GizmoTurn.Value = startTurn + GizmoVisual.SnapAngle(angle, p.Alt) * 180 / Math.PI;
        }
        else if (drag == Drag.Scale && s.Camera.Project(center) is { } pc)
        {
            double distance = Math.Sqrt(Math.Pow(pc.x - p.X, 2) + Math.Pow(pc.y - p.Y, 2));
            cmd.GizmoWidth.Value = startWidth * GizmoVisual.SnapScale(Math.Max(4, distance) / startDistance, p.Alt);
        }
        s.Invalidate();
    }
    public void PointerUp(CadSession s, PointerInfo p) { drag = Drag.None; s.Invalidate(); }
    public bool Key(CadSession s, string key)
    {
        int mode = key switch { "Char:T" => 0, "Char:R" => 1, "Char:S" => 2, _ => -1 };
        if (mode < 0) return false;
        cmd.GizmoMode.Index = mode; s.Invalidate(); return true;
    }
    public void BuildOverlay(CadSession s, Overlay o)
    {
        if (cmd.GizmoPlane is not { } plane) return;
        var frame = FrameAxes(plane);
        for (int op = 0; op < 3; op++)
            GizmoVisual.DrawOperation(o, s, plane.Origin, frame, op, cmd.GizmoMode.Index, op == hotOperation ? hotAxis : -1, op == hotOperation && hotCenter, axisScale: false, planar: true);
    }
    public void Deactivate(CadSession s) { }
}
