using System;
using System.Linq;
using Prism.Commands;
using Prism.Kernel;

namespace Prism.Core;

/// <summary>
/// The shared gizmo on a command: when a command's numbers are a position or an orientation (a primitive's place,
/// a moved face, a component, a transform), it declares a <see cref="GizmoSpec"/> and the one gizmo everyone knows
/// moves those numbers. Feature sizes stay on <see cref="HandleSpec"/> arrows. W / E / R switch move, rotate and scale;
/// typing a number sets the engaged axis, Tab steps to the next one.
/// </summary>
public sealed partial class CadSession
{
    enum GizmoDrag { None, Axis, Free, Plane, Rotate, Scale }
    GizmoDrag _gizmoDrag; int _gizmoAxis = -1, _gizmoHot = -1; bool _gizmoHotCenter;
    int _cmdGizmoMode;                      // 0 move, 1 rotate, 2 scale: what the gizmo is doing now
    Command? _gizmoCmd;
    double[] _gizmoStart = new double[7];   // X Y Z Rx Ry Rz Scale at press
    Vec3 _gizmoC0, _gizmoP0; double _gizmoT0, _gizmoAng0; Plane3 _gizmoPlane; Vec3[] _gizmoAxes = GizmoVisual.WorldAxes;
    bool _gizmoFocus;                       // the gizmo was touched last, so typed numbers go to it rather than to a size handle
    int _gizmoTypeAxis = -1;                // the axis a typed number sets

    /// <summary>The operation the command gizmo shows (0 move, 1 rotate, 2 scale). Falls back to one the command supports.</summary>
    public int CommandGizmoMode
    {
        get
        {
            if (ActiveCommand?.Gizmo is not { } g) return 0;
            if (g.Supports(_cmdGizmoMode)) return _cmdGizmoMode;
            return g.Supports(0) ? 0 : g.Supports(1) ? 1 : 2;
        }
        set { if (ActiveCommand?.Gizmo is { } g && g.Supports(value)) { _cmdGizmoMode = value; _typed = ""; _gizmoTypeAxis = -1; Invalidate(); } }
    }

    /// <summary>What the command gizmo shows right now, for automation and tests: its spec, arrow length in model units and operation.</summary>
    public (GizmoSpec spec, double length, int mode)? CommandGizmoInfo => CurrentGizmo is { } g ? (g, GizmoVisual.Length(this, g.Center), CommandGizmoMode) : null;

    GizmoSpec? CurrentGizmo
    {
        get
        {
            if (ActiveCommand?.Gizmo is not { } g || ActiveTool != null) return null;
            ResetTypingFor(ActiveCommand);       // note the command before anything focuses the gizmo, so a hover's focus survives the first key
            if (!ReferenceEquals(_gizmoCmd, ActiveCommand)) { _gizmoCmd = ActiveCommand; _gizmoHot = -1; _gizmoHotCenter = false; _gizmoDrag = GizmoDrag.None; _gizmoFocus = false; _gizmoTypeAxis = -1; }
            return g;
        }
    }

    static bool GizmoPlanes(GizmoSpec g) => GizmoSettings.Current.PlaneHandles && g.Move.Count(p => p != null) >= 2;

    static bool GizmoAllows(GizmoSpec g, int op, int axis) => op switch
    {
        0 => axis >= GizmoVisual.PlaneBase ? g.Move[(axis - GizmoVisual.PlaneBase + 1) % 3] != null && g.Move[(axis - GizmoVisual.PlaneBase + 2) % 3] != null : g.Move[axis] != null,
        1 => g.Turn[axis] != null,
        _ => g.Scale != null,
    };

    static NumberParam? GizmoParam(GizmoSpec g, int op, int axis) => op switch
    {
        0 => axis is >= 0 and < 3 ? g.Move[axis] : null,
        1 => axis is >= 0 and < 3 ? g.Turn[axis] : null,
        _ => g.Scale,
    };

    (int axis, bool center, int op) GizmoHit(GizmoSpec g, double x, double y)
    {
        int mode = CommandGizmoMode;
        // the factor is one number, so scale shows only the centre block (axisScale: false)
        var hit = GizmoVisual.Hit(this, x, y, _ => g.Center, g.Axes, mode, planes: GizmoPlanes(g), axisScale: false, allow: (op, axis) => GizmoAllows(g, op, axis));
        if (!g.Supports(hit.operation)) return (-1, false, mode);
        if (hit.center && !(hit.operation == 0 ? g.Move.Any(p => p != null) : hit.operation == 2 && g.Scale != null)) return (-1, false, mode);
        return hit;
    }

    /// <summary>The gizmo takes the typed numbers: it was hovered or dragged last, or the command has no size handles to type into.</summary>
    bool GizmoHasFocus => _gizmoFocus || ActiveCommand is { Handles.Count: 0 };

    void FocusGizmo(int op, int axis)
    {
        int typeAxis = op == 2 ? 0 : axis is >= 0 and < 3 ? axis : _gizmoTypeAxis;
        if (!_gizmoFocus || typeAxis != _gizmoTypeAxis) _typed = "";
        _gizmoFocus = true; _gizmoTypeAxis = typeAxis;
    }

    bool CommandGizmoDown(PointerInfo p)
    {
        if (!p.Left || CurrentGizmo is not { } g) return false;
        var (axis, center, op) = GizmoHit(g, p.X, p.Y);
        axis = GizmoVisual.AsPlane(axis, op, p.Ctrl);
        if (axis < 0 && !center) return false;
        if (axis >= GizmoVisual.PlaneBase && !GizmoAllows(g, 0, axis)) return false;
        if (op == 1 && axis < 0) return false;
        if (op != CommandGizmoMode) _cmdGizmoMode = op;
        g.Engage?.Invoke(op, axis);
        g = ActiveCommand?.Gizmo ?? g;       // engaging may have changed what the command offers (the turn axis)
        FocusGizmo(op, axis); _typed = "";
        _gizmoAxes = g.Axes; _gizmoC0 = g.Center; _gizmoAxis = axis;
        for (int i = 0; i < 3; i++) { _gizmoStart[i] = g.Move[i]?.Value ?? 0; _gizmoStart[3 + i] = g.Turn[i]?.Value ?? 0; }
        _gizmoStart[6] = g.Scale?.Value ?? 1;
        var (o, d) = Camera.Ray(p.X, p.Y);
        if (op == 1)
        {
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
        Invalidate();
        return true;
    }

    (Vec3 u, Vec3 v) RingBasis(int a) => (_gizmoAxes[(a + 1) % 3], _gizmoAxes[(a + 2) % 3]);

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
            if (p.Left) return false;        // another drag (a size handle, a region) is in progress: leave the hover alone
            var (a, c, op) = GizmoHit(g, p.X, p.Y); a = GizmoVisual.AsPlane(a, op, p.Ctrl);
            if (a != _gizmoHot || c != _gizmoHotCenter) { _gizmoHot = a; _gizmoHotCenter = c; Invalidate(); }
            if (a >= 0 || c) { if (op == CommandGizmoMode) FocusGizmo(op, a); if (_handleHover) { _handleHover = false; Invalidate(); } }
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

    void ClearGizmoHover() { if (_gizmoHot >= 0 || _gizmoHotCenter) { _gizmoHot = -1; _gizmoHotCenter = false; Invalidate(); } }

    bool CommandGizmoUp()
    {
        if (_gizmoDrag == GizmoDrag.None) return false;
        _gizmoDrag = GizmoDrag.None; Invalidate();
        return true;
    }

    /// <summary>The parameter a typed number lands in: the engaged axis of the current operation, or the first one the command offers.</summary>
    NumberParam? GizmoTypeTarget(GizmoSpec g)
    {
        int mode = CommandGizmoMode;
        if (mode == 2) return g.Scale;
        if (GizmoParam(g, mode, _gizmoTypeAxis) == null) _gizmoTypeAxis = Enumerable.Range(0, 3).Where(i => GizmoParam(g, mode, i) != null).DefaultIfEmpty(-1).First();
        return GizmoParam(g, mode, _gizmoTypeAxis);
    }

    bool CommandGizmoKey(string key)
    {
        if (CurrentGizmo is not { } g) return false;
        ResetTypingFor(ActiveCommand);
        int op = key switch { "Char:W" => 0, "Char:E" => 1, "Char:R" => 2, _ => -1 };
        if (op >= 0) { if (!g.Supports(op)) return false; CommandGizmoMode = op; _gizmoFocus = true; return true; }
        if (!GizmoHasFocus || GizmoTypeTarget(g) is not { } target) return false;
        bool digit = key.StartsWith("Char:") && key.Length == 6 && (char.IsDigit(key[5]) || key[5] == '.' || key[5] == ',');
        if ((digit || key == "Minus") && _typed.Length == 0) g.Engage?.Invoke(CommandGizmoMode, _gizmoTypeAxis);
        if (digit) { _typed += key[5] == ',' ? "." : key[5].ToString(); ApplyTyped(target); return true; }
        switch (key)
        {
            case "Minus": _typed = _typed.StartsWith('-') ? _typed[1..] : "-" + _typed; ApplyTyped(target); return true;
            case "Back": if (_typed.Length == 0) return false; _typed = _typed[..^1]; ApplyTyped(target); return true;
            case "Tab":
            {
                int mode = CommandGizmoMode; if (mode == 2) return false;
                var axes = Enumerable.Range(0, 3).Where(i => GizmoParam(g, mode, i) != null).ToList(); if (axes.Count < 2) return false;
                _gizmoTypeAxis = axes[(axes.IndexOf(_gizmoTypeAxis) + 1) % axes.Count]; _typed = "";
                g.Engage?.Invoke(mode, _gizmoTypeAxis);
                Invalidate(); return true;
            }
            case "Enter": if (_typed.Length == 0) return false; ApplyTyped(target); _typed = ""; Invalidate(); return true;
            case "Escape": if (_typed.Length == 0) return false; _typed = ""; Invalidate(); return true;
        }
        return false;
    }

    void BuildCommandGizmoOverlay(Overlay o)
    {
        if (CurrentGizmo is not { } g) return;
        int mode = CommandGizmoMode;
        for (int op = 0; op < 3; op++)
        {
            if (!g.Supports(op)) continue;
            GizmoVisual.DrawOperation(o, this, g.Center, g.Axes, op, mode, op == mode ? _gizmoHot : -1, op == mode && _gizmoHotCenter,
                axisScale: false, planes: GizmoPlanes(g), allow: (oper, axis) => GizmoAllows(g, oper, axis));
        }
        // the engaged handle reads its value (dragged, hovered or being typed into); nothing is labelled at rest
        double length = GizmoVisual.Length(this, g.Center);
        var above = g.Center + Camera.Up * length * 1.3;
        string Moves() => string.Join("  ", g.Move.Where(m => m != null).Select(m => $"{m!.Value:0.##}")) + (g.Move.FirstOrDefault(m => m != null) is { } first && first.Unit.Length > 0 ? " " + first.Unit : "");
        string One(NumberParam p) => mode == 1 ? $"{p.Value:0.#}°" : mode == 2 ? $"×{p.Value:0.###}" : $"{p.Value:0.##}{(p.Unit.Length > 0 ? " " + p.Unit : "")}";
        Vec3 AxisSpot(int axis) => mode == 0 && axis is >= 0 and < 3 ? g.Center + g.Axes[axis] * length * 1.3 : above;
        if (_typed.Length > 0 && GizmoHasFocus && GizmoTypeTarget(g) is { } typing)
        {
            string unit = mode == 1 ? " °" : mode == 2 ? " ×" : typing.Unit.Length > 0 ? " " + typing.Unit : "";
            o.AddLabel(AxisSpot(_gizmoTypeAxis), $"{_typed}_{unit}  -  Enter to apply", true, 4);
        }
        else if (_gizmoDrag != GizmoDrag.None)
        {
            if (_gizmoDrag is GizmoDrag.Axis or GizmoDrag.Rotate or GizmoDrag.Scale && GizmoParam(g, mode, _gizmoAxis) is { } dragged) o.AddLabel(AxisSpot(_gizmoAxis), One(dragged), true, 4);
            else o.AddLabel(above, Moves(), true, 4);
        }
        else if (_gizmoHot is >= 0 and < 3 && GizmoParam(g, mode, _gizmoHot) is { } hovered) o.AddLabel(AxisSpot(_gizmoHot), One(hovered), true, 4);
        else if (_gizmoHot >= GizmoVisual.PlaneBase || _gizmoHotCenter) o.AddLabel(above, mode == 2 && g.Scale is { } s ? One(s) : Moves(), true, 4);
    }
}
