using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Prism.Kernel;

namespace Prism.Core;

/// <summary>Shared geometry, screen sizing and picking for sketch, cage and NURBS manipulators.</summary>
public static class GizmoVisual
{
    public static readonly Vec3[] WorldAxes = [Vec3.UnitX, Vec3.UnitY, Vec3.UnitZ];
    public static uint[] AxisColors => Application.Current?.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Light
        ? [0xFFD13438, 0xFF16824A, 0xFF2F7BF0] : [0xFFE5484D, 0xFF3FCF8E, 0xFF4C94FF];
    static uint ResourceColor(string key, uint dark, uint light)
    {
        if (Application.Current is not { } app) return dark;
        if ((app.Resources.TryGetResource(key, app.ActualThemeVariant, out var value) || app.Styles.TryGetResource(key, app.ActualThemeVariant, out value)) && value is SolidColorBrush brush) return brush.Color.ToUInt32();
        return app.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Light ? light : dark;
    }
    public static uint Ink => ResourceColor("Ink", 0xFFE8EDF5, 0xFF1B2432);
    public static uint Accent => ResourceColor("Accent", 0xFF4C94FF, 0xFF2F7BF0);
    public static double Length(CadSession session, Vec3 center) => session.Camera.PixelSizeAt(center) * GizmoSettings.Current.Pixels;
    public static Vec3 UniformPoint(CadSession s, Vec3 c, double length) => c + (s.Camera.Right - s.Camera.Up).Normalized() * length * 0;
    public static List<Vec3> Ring(Vec3 center, Vec3 u, Vec3 v, double length) => Enumerable.Range(0, 73)
        .Select(i => center + (u * Math.Cos(i * Math.PI / 36) + v * Math.Sin(i * Math.PI / 36)) * length * .8).ToList();
    public static void Arrow(Overlay overlay, Vec3 origin, Vec3 direction, double length, uint color, float width)
    {
        var axis = direction.Normalized(); var tip = origin + axis * length;
        overlay.AddLine(origin, tip, color, width);
        var u = Vec3.Cross(axis, Math.Abs(axis.Z) < .9 ? Vec3.UnitZ : Vec3.UnitX).Normalized(); var v = Vec3.Cross(axis, u);
        var back = tip - axis * length * .15;
        for (int i = 0; i < 8; i++)
        {
            double a = i * Math.PI / 4, b = (i + 1) * Math.PI / 4;
            overlay.AddTri(tip, back + (u * Math.Cos(a) + v * Math.Sin(a)) * length * .05, back + (u * Math.Cos(b) + v * Math.Sin(b)) * length * .05, color);
        }
    }
    public static void Square(Overlay o, CadSession s, Vec3 center, double length, uint color)
    {
        var u = s.Camera.Right * length * .05; var v = s.Camera.Up * length * .05;
        o.AddTri(center - u - v, center + u - v, center + u + v, color);
        o.AddTri(center - u - v, center + u + v, center - u + v, color);
    }
    /// <summary>Handle numbers 3..5 are the plane handles: 3 + k is the plane that has axis k as its normal (X: YZ, Y: ZX, Z: XY).</summary>
    public const int PlaneBase = 3;
    public static int AsPlane(int axis, int operation, bool modifier) => modifier && axis is >= 0 and < 3 && operation != 1 ? PlaneBase + axis : axis;
    public static string PlaneName(int axis) => axis switch { 3 => "YZ", 4 => "ZX", _ => "XY" };
    public static (Vec3 u, Vec3 v) PlaneAxes(Vec3[] axes, int plane) => (axes[(plane - PlaneBase + 1) % 3], axes[(plane - PlaneBase + 2) % 3]);
    static Vec3[] PlaneCorners(Vec3 c, Vec3[] axes, int plane, double length)
    {
        var (u, v) = PlaneAxes(axes, plane);
        return [c + u * length * .22 + v * length * .22, c + u * length * .55 + v * length * .22, c + u * length * .55 + v * length * .55, c + u * length * .22 + v * length * .55];
    }
    static bool Inside(double x, double y, (double x, double y)[] q)
    {
        int sign = 0;
        for (int i = 0; i < q.Length; i++)
        {
            var a = q[i]; var b = q[(i + 1) % q.Length];
            double cross = (b.x - a.x) * (y - a.y) - (b.y - a.y) * (x - a.x);
            int sg = Math.Sign(cross);
            if (sg == 0) continue;
            if (sign == 0) sign = sg; else if (sg != sign) return false;
        }
        return true;
    }
    static bool PlaneUsable(GizmoSettings settings, int plane) => settings.AxisVisible((plane - PlaneBase + 1) % 3) && settings.AxisVisible((plane - PlaneBase + 2) % 3);
    public static void DrawOperation(Overlay o, CadSession s, Vec3 c, Vec3[] axes, int operation, int active, int hotAxis = -1, bool hotCenter = false, bool axisScale = true, bool planar = false, bool planes = false)
    {
        var settings = GizmoSettings.Current;
        if (!settings.Allows(operation, active)) return;
        double length = Length(s, c); float width = settings.Stroke;
        for (int axis = 0; axis < 3; axis++)
        {
            if(!settings.AxisVisible(axis))continue;
            if (planar && (operation == 1 ? axis != 2 : axis == 2) || operation == 2 && !axisScale) continue;
            uint color = hotAxis == axis ? Accent : AxisColors[axis];
            float stroke = hotAxis == axis ? width + 1.5f : width;
            if (operation == 0) Arrow(o, c, axes[axis], length, color, stroke);
            else if (operation == 1) o.AddPolyline(Ring(c, axes[(axis + 1) % 3], axes[(axis + 2) % 3], length), color, stroke);
            else
            {
                var tip = c + axes[axis] * length * 1.18;
                o.AddLine(c, tip, color, stroke); Square(o, s, tip, length, color);
            }
            if (settings.Labels && (operation == 0 || !settings.Allows(0, active)))
                o.AddLabel(c + axes[axis] * length * (operation == 2 ? 1.32 : 1.12), axes[axis].DistanceTo(WorldAxes[axis]) < 1e-6 ? new[] { "X", "Y", "Z" }[axis] : new[] { "U", "V", "N" }[axis], false);
        }
        if (planes && operation != 1)
            for (int k = 0; k < 3; k++)
            {
                if (!PlaneUsable(settings, PlaneBase + k)) continue;
                bool hot = hotAxis == PlaneBase + k;
                uint color = hot ? Accent : 0xFFFFFFFF;
                var q = PlaneCorners(c, axes, PlaneBase + k, length);
                uint fill = (color & 0x00FFFFFF) | (hot ? 0xC0000000 : 0xB0000000);
                o.AddTri(q[0], q[1], q[2], fill); o.AddTri(q[0], q[2], q[3], fill);
                o.AddPolyline(q, color, hot ? width + 2 : width + 1, true);
            }
        if (operation == 2) Square(o, s, UniformPoint(s, c, length), length * 2.2, hotCenter ? Accent : 0xFFFFFFFF);
        if (operation == active && operation != 2) { o.AddDot(c, 0xFFFFFFFF, operation == 0 ? 15 : 11); o.AddDot(c, hotCenter && operation == 0 ? Accent : Ink, operation == 0 ? 11 : 7); }
    }
    public static (int axis, bool center, int operation) Hit(CadSession s, double x, double y, Func<int, Vec3> center, Vec3[] axes, int active, bool planes = false, bool planar = false, bool axisScale = true)
    {
        var settings = GizmoSettings.Current; double best = settings.Tolerance;
        (int axis, bool center, int operation) hit = (-1, false, active);
        void Segment(Vec3 a, Vec3 b, int axis, int op)
        {
            if (s.Camera.Project(a) is not { } pa || s.Camera.Project(b) is not { } pb) return;
            if (op != 1 && Math.Abs(pa.x - pb.x) + Math.Abs(pa.y - pb.y) < 20) return;
            var (_, distance) = Picking.SegDist(x, y, pa.x, pa.y, pb.x, pb.y);
            if (distance < best) { best = distance; hit = (axis, false, op); }
        }
        void Point(Vec3 p, int op)
        {
            if (s.Camera.Project(p) is not { } q) return;
            double distance = Math.Sqrt(Math.Pow(x - q.x, 2) + Math.Pow(y - q.y, 2));
            if (distance < best) { best = distance; hit = (-1, true, op); }
        }
        foreach (int op in new[] { 0, 1, 2 }.Where(op => settings.Allows(op, active)))
        {
            var c = center(op); double length = Length(s, c);
            if (op == 0) Point(c, 0);
            if (op == 2) Point(UniformPoint(s, c, length), 2);
            if (planes && op != 1)
                for (int k = 0; k < 3; k++)
                {
                    if (!PlaneUsable(settings, PlaneBase + k)) continue;
                    var q = PlaneCorners(c, axes, PlaneBase + k, length).Select(p => s.Camera.Project(p)).ToArray();
                    if (q.Any(p => p == null)) continue;
                    if (Inside(x, y, q.Select(p => (p!.Value.x, p.Value.y)).ToArray())) { best = 0; hit = (PlaneBase + k, false, op); }
                }
            for (int axis = 0; axis < 3; axis++)
            {
                if(!settings.AxisVisible(axis))continue;
                if (planar && (op == 1 ? axis != 2 : axis == 2) || op == 2 && !axisScale) continue;
                if (op == 1)
                {
                    var points = Ring(c, axes[(axis + 1) % 3], axes[(axis + 2) % 3], length);
                    for (int i = 1; i < points.Count; i++) Segment(points[i - 1], points[i], axis, op);
                }
                else if (op == 0) Segment(c + axes[axis] * length * .22, c + axes[axis] * length, axis, op);
                else
                {
                    var tip = c + axes[axis] * length * 1.18;
                    if (s.Camera.Project(tip) is { } p)
                    {
                        double distance = Math.Sqrt(Math.Pow(x - p.x, 2) + Math.Pow(y - p.y, 2));
                        if (distance < best) { best = distance; hit = (axis, false, op); }
                    }
                }
            }
        }
        return hit;
    }
    static double Quantize(double value, double step) => double.IsFinite(step) && step > 1e-9 ? Math.Round(value / step, MidpointRounding.AwayFromZero) * step : value;
    public static Vec3 SnapMove(Vec3 delta, bool bypass = false)
    {
        var settings = GizmoSettings.Current;
        return !settings.Snap || bypass ? delta : new(Quantize(delta.X, settings.MoveStep), Quantize(delta.Y, settings.MoveStep), Quantize(delta.Z, settings.MoveStep));
    }
    public static double SnapAngle(double radians, bool bypass = false) => !GizmoSettings.Current.Snap || bypass ? radians : Quantize(radians, GizmoSettings.Current.AngleStep * Math.PI / 180);
    public static double SnapScale(double factor, bool bypass = false) => Math.Clamp(!GizmoSettings.Current.Snap || bypass ? factor : 1 + Quantize(factor - 1, GizmoSettings.Current.ScaleStep / 100), .02, 50);
}
