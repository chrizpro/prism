using System;
using System.Linq;
using Prism.Commands;
using Prism.Kernel;

namespace Prism.Core;

/// <summary>
/// The shared gizmo on a command: when a command's numbers are a position or an orientation (a primitive's place,
/// a moved face, a component, a transform), it declares a <see cref="GizmoSpec"/> and the one gizmo everyone knows
/// moves those numbers. Feature sizes stay on <see cref="HandleSpec"/> arrows. W / E / R switch move, rotate and scale.
/// </summary>
public sealed partial class CadSession
{
    enum GizmoDrag { None, Axis, Free, Plane, Rotate, Scale }
    GizmoDrag _gizmoDrag; int _gizmoAxis = -1, _gizmoHot = -1; bool _gizmoHotCenter;
    int _cmdGizmoMode;                      // 0 move, 1 rotate, 2 scale: what the gizmo is doing now
    Command? _gizmoCmd;
    double[] _gizmoStart = new double[7];   // X Y Z Rx Ry Rz Scale at press
    Vec3 _gizmoC0, _gizmoP0; double _gizmoT0, _gizmoAng0; Plane3 _gizmoPlane; Vec3[] _gizmoAxes = GizmoVisual.WorldAxes;

    /// <summary>The operation the command gizmo shows (0 move, 1 rotate, 2 scale). Falls back to one the command supports.</summary>
    public int CommandGizmoMode
    {
        get
        {
            if (ActiveCommand?.Gizmo is not { } g) return 0;
            if (g.Supports(_cmdGizmoMode)) return _cmdGizmoMode;
            return g.Supports(0) ? 0 : g.Supports(1) ? 1 : 2;
        }
        set { if (ActiveCommand?.Gizmo is { } g && g.Supports(value)) { _cmdGizmoMode = value; Invalidate(); } }
    }

    GizmoSpec? CurrentGizmo
    {
        get
        {
            if (ActiveCommand?.Gizmo is not { } g || ActiveTool != null) return null;
            if (!ReferenceEquals(_gizmoCmd, ActiveCommand)) { _gizmoCmd = ActiveCommand; _gizmoHot = -1; _gizmoDrag = GizmoDrag.None; }
            return g;
        }
    }

    bool GizmoAllows(GizmoSpec g, int op, int axis) => op switch
    {
        0 => axis >= GizmoVisual.PlaneBase ? g.Move[(axis - GizmoVisual.PlaneBase + 1) % 3] != null && g.Move[(axis - GizmoVisual.PlaneBase + 2) % 3] != null : g.Move[axis] != null,
        1 => g.Turn[axis] != null,
        _ => g.Scale != null,
    };

    (int axis, bool center, int op) GizmoHit(GizmoSpec g, double x, double y)
    {
        int mode = CommandGizmoMode;
        var hit = GizmoVisual.Hit(this, x, y, _ => g.Center, g.Axes, mode, planes: g.Move.Count(p => p != null) >= 2, allow: (op, axis) => GizmoAllows(g, op, axis));
        if (hit.center && !(hit.operation == 0 ? g.Move.Any(p => p != null) : hit.operation == 2 && g.Scale != null)) return (-1, false, mode);
        return hit;
    }

    bool CommandGizmoDown(PointerInfo p)
    {
        if (!p.Left || CurrentGizmo is not { } g) return false;
        var (axis, center, op) = GizmoHit(g, p.X, p.Y);
        axis = GizmoVisual.AsPlane(axis, op, p.Ctrl);
        if (axis < 0 && !center) return false;
        if (axis >= GizmoVisual.PlaneBase && !GizmoAllows(g, 0, axis)) return false;
        if (op != CommandGizmoMode) _cmdGizmoMode = op;
        _gizmoAxes = g.Axes; _gizmoC0 = g.Center; _gizmoAxis = axis;
        for (int i = 0; i < 3; i++) { _gizmoStart[i] = g.Move[i]?.Value ?? 0; _gizmoStart[3 + i] = g.Turn[i]?.Value ?? 0; }
        _gizmoStart[6] = g.Scale?.Value ?? 1;
        var (o, d) = Camera.Ray(p.X, p.Y);
        if (op == 1)
        {
            if (axis < 0) return false;
            _gizmoDrag = GizmoDrag.Rotate;
            var (u, v) = RingBasis(axis); _gizmoPlane = new Plane3(_gizmoC0, u, v);
            var h = _gizmoPlane.Intersect(o, d) ?? _gizmoC0 + u;
            _gizmoAng0 = Math.Atan2(Vec3.Dot(h - _gizmoC0, v), Vec3.Dot(h - _gizmoC0, u));
        }
        else if (op == 2)
        {
            _gizmoDrag = GizmoDrag.Scale; _gizmoPlane = new Plane3(_gizmoC0, Camera.Right, Camera.Up);
            _gizmoP0 = _gizmoPlane.Intersect(o, d) ?? _gizmoC0;
        }
        else if (axis >= GizmoVisual.PlaneBase)
        {
            _gizmoDrag = GizmoDrag.Plane;
            var (u, v) = GizmoVisual.PlaneAxes(_gizmoAxes, axis); _gizmoPlane = new Plane3(_gizmoC0, u, v);
            _gizmoP0 = _gizmoPlane.Intersect(o, d) ?? _gizmoC0;
        }
        else if (center)
        {
            _gizmoDrag = GizmoDrag.Free; _gizmoPlane = new Plane3(_gizmoC0, Camera.Right, Camera.Up);
            _gizmoP0 = _gizmoPlane.Intersect(o, d) ?? _gizmoC0;
        }
        else { _gizmoDrag = GizmoDrag.Axis; _gizmoT0 = AxisParam(o, d, _gizmoC0, _gizmoAxes[axis]) ?? 0; }
        _typed = "";
        Invalidate();
        return true;
    }

    (Vec3 u, Vec3 v) RingBasis(int a) => a == 0 ? (_gizmoAxes[1], _gizmoAxes[2]) : a == 1 ? (_gizmoAxes[2], _gizmoAxes[0]) : (_gizmoAxes[0], _gizmoAxes[1]);

    static double? AxisParam(Vec3 o, Vec3 d, Vec3 c, Vec3 a)
    {
        var w = c - o; double b = Vec3.Dot(a, d), dd = Vec3.Dot(a, w), ee = Vec3.Dot(d, w), den = 1 - b * b;
        return den < 1e-6 ? null : (b * ee - dd) / den;
    }

    /// <summary>Hover tracking (no button) or the drag in progress. Returns true when the gizmo consumed the move.</summary>
    bool CommandGizmoMove(PointerInfo p)
    {
        if (CurrentGizmo is not { } g) return false;
        if (_gizmoDrag == GizmoDrag.None)
        {
            var (a, c, op) = GizmoHit(g, p.X, p.Y); a = GizmoVisual.AsPlane(a, op, p.Ctrl);
            if (a != _gizmoHot || c != _gizmoHotCenter) { _gizmoHot = a; _gizmoHotCenter = c; Invalidate(); }
            return a >= 0 || c;
        }
        if (!p.Left) { _gizmoDrag = GizmoDrag.None; return false; }
        var (o, d) = Camera.Ray(p.X, p.Y);
        switch (_gizmoDrag)
        {
            case GizmoDrag.Free:
            case GizmoDrag.Plane:
                if (_gizmoPlane.Intersect(o, d) is { } hit) MoveBy(g, GizmoVisual.SnapMove(hit - _gizmoP0, p.Alt));
                break;
            case GizmoDrag.Axis:
                if (AxisParam(o, d, _gizmoC0, _gizmoAxes[_gizmoAxis]) is double t) MoveBy(g, GizmoVisual.SnapMove(_gizmoAxes[_gizmoAxis] * (t - _gizmoT0), p.Alt));
                break;
            case GizmoDrag.Rotate:
            {
                if (_gizmoPlane.Intersect(o, d) is not { } rh) break;
                var (u, v) = RingBasis(_gizmoAxis);
                double delta = Math.Atan2(Vec3.Dot(rh - _gizmoC0, v), Vec3.Dot(rh - _gizmoC0, u)) - _gizmoAng0;
                while (delta > Math.PI) delta -= 2 * Math.PI; while (delta < -Math.PI) delta += 2 * Math.PI;
                delta = GizmoVisual.SnapAngle(delta, p.Alt);
                if (g.Turn[_gizmoAxis] is { } turn) Set(turn, _gizmoStart[3 + _gizmoAxis] + delta * 180 / Math.PI);
                break;
            }
            case GizmoDrag.Scale:
            {
                if (_gizmoPlane.Intersect(o, d) is not { } sh || g.Scale is not { } scale) break;
                double len = GizmoVisual.Length(this, _gizmoC0);
                double factor = GizmoVisual.SnapScale(1 + Vec3.Dot(sh - _gizmoP0, (Camera.Right + Camera.Up).Normalized()) / len, p.Alt);
                Set(scale, _gizmoStart[6] * factor);
                break;
            }
        }
        Invalidate();
        return true;
    }

    void MoveBy(GizmoSpec g, Vec3 delta)
    {
        // the gizmo's axes may not be the world's (a face's own frame): each parameter takes the component along its axis
        for (int i = 0; i < 3; i++)
            if (g.Move[i] is { } param) Set(param, _gizmoStart[i] + Vec3.Dot(delta, g.Axes[i]) * g.Mul);
    }

    static void Set(NumberParam p, double value)
    {
        double rounded = Math.Round(value, 6);
        if (Math.Abs(p.Value - rounded) > 1e-12) p.Value = rounded;
    }

    bool CommandGizmoUp()
    {
        if (_gizmoDrag == GizmoDrag.None) return false;
        _gizmoDrag = GizmoDrag.None; Invalidate();
        return true;
    }

    bool CommandGizmoKey(string key)
    {
        if (CurrentGizmo is not { } g) return false;
        int op = key switch { "Char:W" => 0, "Char:E" => 1, "Char:R" => 2, _ => -1 };
        if (op < 0 || !g.Supports(op)) return false;
        CommandGizmoMode = op;
        return true;
    }

    void BuildCommandGizmoOverlay(Overlay o)
    {
        if (CurrentGizmo is not { } g) return;
        int mode = CommandGizmoMode;
        for (int op = 0; op < 3; op++)
        {
            if (!g.Supports(op)) continue;
            GizmoVisual.DrawOperation(o, this, g.Center, g.Axes, op, mode, op == mode ? _gizmoHot : -1, op == mode && _gizmoHotCenter,
                planes: g.Move.Count(p => p != null) >= 2, allow: (oper, axis) => GizmoAllows(g, oper, axis));
        }
        // the engaged handle reads its value; nothing is labelled at rest
        if (_gizmoDrag == GizmoDrag.None) return;
        string text = _gizmoDrag switch
        {
            GizmoDrag.Rotate when g.Turn[_gizmoAxis] is { } t => $"{t.Value:0.#}°",
            GizmoDrag.Scale when g.Scale is { } s => $"×{s.Value:0.###}",
            GizmoDrag.Axis when g.Move[_gizmoAxis] is { } m => $"{m.Value:0.##} {m.Unit}",
            _ => string.Join("  ", g.Move.Where(m => m != null).Select(m => $"{m!.Value:0.##}")) + (g.Move.Any(m => m != null) ? " " + g.Move.First(m => m != null)!.Unit : ""),
        };
        o.AddLabel(g.Center + Camera.Up * GizmoVisual.Length(this, g.Center) * 1.25, text, true, 4);
    }
}
