using System;
using Prism.Core;
using Prism.Core.Form;
using Prism.Kernel;

namespace Prism.Commands;

/// <summary>A free-standing datum gizmo shown in plain model view. It only moves and rotates its own frame.</summary>
public sealed class DatumGizmoTool : IViewportTool
{
    enum Drag { None, Axis, Free, Plane, Rotate }
    public GizmoBinding Binding { get; } = new();
    public int Mode { get; set; }              // 0 move, 1 rotate
    Drag _drag; int _axis, _hotAxis = -1; bool _hotCenter;
    Vec3 _c0, _start; double _t0, _ang0;
    Plane3 _plane, _frame0;
    Vec3[] _axes = GizmoVisual.WorldAxes;

    public string Prompt => "Drag an arrow to move the datum, a block to move in a plane, or a ring to rotate it. Esc hides it.";
    public Plane3 Frame => Binding.Frame ?? new Plane3(Vec3.Zero, Vec3.UnitX, Vec3.UnitY);
    public Vec3[] Axes => _drag != Drag.None ? _axes : Binding.Axes;

    public void Place(Vec3 origin)
    {
        Binding.Reset();
        Binding.Begin(origin);
        Binding.SetOrigin(origin);
        _drag = Drag.None;
    }

    (Vec3 u, Vec3 v) RingBasis(int a) => a == 0 ? (_axes[1], _axes[2]) : a == 1 ? (_axes[2], _axes[0]) : (_axes[0], _axes[1]);

    static double? AxisParam(Vec3 o, Vec3 d, Vec3 c, Vec3 a)
    {
        var w = c - o; double b = Vec3.Dot(a, d), dd = Vec3.Dot(a, w), ee = Vec3.Dot(d, w), den = 1 - b * b;
        return den < 1e-6 ? null : (b * ee - dd) / den;
    }

    (int axis, bool center, int operation) Hit(CadSession s, double x, double y) =>
        GizmoVisual.Hit(s, x, y, _ => Frame.Origin, Axes, Mode, planes: true);

    public bool PointerDown(CadSession s, PointerInfo p)
    {
        if (!p.Left) return false;
        var (axis, center, op) = Hit(s, p.X, p.Y);
        axis = GizmoVisual.AsPlane(axis, op, p.Ctrl);
        if (axis < 0 && !center) return false;
        if (op != Mode) Mode = op;
        _axes = Binding.Axes; _frame0 = Frame; _c0 = _frame0.Origin; _axis = axis;
        var (o, d) = s.Camera.Ray(p.X, p.Y);
        if (Mode == 1)
        {
            if (axis < 0) return false;
            _drag = Drag.Rotate;
            var (u, v) = RingBasis(axis); _plane = new Plane3(_c0, u, v);
            var h = _plane.Intersect(o, d) ?? _c0 + u;
            _ang0 = Math.Atan2(Vec3.Dot(h - _c0, v), Vec3.Dot(h - _c0, u));
        }
        else if (axis >= GizmoVisual.PlaneBase)
        {
            _drag = Drag.Plane;
            var (u, v) = GizmoVisual.PlaneAxes(_axes, axis); _plane = new Plane3(_c0, u, v);
            _start = _plane.Intersect(o, d) ?? _c0;
        }
        else if (center)
        {
            _drag = Drag.Free; _plane = new Plane3(_c0, s.Camera.Right, s.Camera.Up);
            _start = _plane.Intersect(o, d) ?? _c0;
        }
        else { _drag = Drag.Axis; _t0 = AxisParam(o, d, _c0, _axes[axis]) ?? 0; }
        s.Invalidate();
        return true;
    }

    public void PointerMove(CadSession s, PointerInfo p)
    {
        if (_drag == Drag.None)
        {
            var (a, c, op) = Hit(s, p.X, p.Y); a = GizmoVisual.AsPlane(a, op, p.Ctrl);
            if (a != _hotAxis || c != _hotCenter) { _hotAxis = a; _hotCenter = c; s.Invalidate(); }
            return;
        }
        var (o, d) = s.Camera.Ray(p.X, p.Y);
        switch (_drag)
        {
            case Drag.Free:
            case Drag.Plane:
                if (_plane.Intersect(o, d) is { } hit) Binding.Transform(_frame0, FormTransform.MoveBy(GizmoVisual.SnapMove(hit - _start, p.Alt)));
                break;
            case Drag.Axis:
                if (AxisParam(o, d, _c0, _axes[_axis]) is double t) Binding.Transform(_frame0, FormTransform.MoveBy(GizmoVisual.SnapMove(_axes[_axis] * (t - _t0), p.Alt)));
                break;
            case Drag.Rotate:
            {
                if (_plane.Intersect(o, d) is not { } rh) break;
                var (u, v) = RingBasis(_axis);
                double delta = Math.Atan2(Vec3.Dot(rh - _c0, v), Vec3.Dot(rh - _c0, u)) - _ang0;
                while (delta > Math.PI) delta -= 2 * Math.PI; while (delta < -Math.PI) delta += 2 * Math.PI;
                delta = GizmoVisual.SnapAngle(delta, p.Alt);
                Binding.Transform(_frame0, FormTransform.RotateAbout(_c0, _axes[_axis], delta));
                s.Say($"Rotate {delta * 180 / Math.PI:0.#}°");
                break;
            }
        }
        s.RaiseState(); s.Invalidate();
    }

    public void PointerUp(CadSession s, PointerInfo p) { _drag = Drag.None; s.RaiseState(); }

    public bool Key(CadSession s, string key)
    {
        if (key != "Escape") return false;
        s.HideDatumGizmo();
        return true;
    }

    public void BuildOverlay(CadSession s, Overlay o)
    {
        var c = Frame.Origin;
        for (int op = 0; op < 2; op++)
            GizmoVisual.DrawOperation(o, s, c, Axes, op, Mode, op == Mode ? _hotAxis : -1, op == Mode && _hotCenter, planes: true);
    }

    public void Deactivate(CadSession s) { }
}
