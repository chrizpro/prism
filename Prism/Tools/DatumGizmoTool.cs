using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core;
using Prism.Core.Form;
using Prism.Kernel;

namespace Prism.Commands;

/// <summary>
/// A free-standing datum gizmo shown in plain model view. Alone it only moves its own frame;
/// with <see cref="MoveMode"/> on, clicks bind scene objects or datums and the gizmo moves them.
/// </summary>
public sealed class DatumGizmoTool : IViewportTool
{
    enum Drag { None, Axis, Free, Plane, Rotate }
    public GizmoBinding Binding { get; } = new();
    public int Mode { get; set; }              // 0 move, 1 rotate
    /// <summary>Move-tool mode: a click that misses the handles binds whatever was picked as the gizmo's payload.</summary>
    public bool MoveMode { get; set; }
    readonly List<SelItem> _targets = [];
    /// <summary>The objects/datums the gizmo currently moves.</summary>
    public IReadOnlyList<SelItem> Targets => _targets;
    Plane3? _applied;                          // gizmo frame the targets were last synced to
    bool _checkpointed;                        // one history step per drag gesture
    const int CommitIntervalMs = 120;
    readonly System.Diagnostics.Stopwatch _sinceCommit = new();
    Drag _drag; int _axis, _hotAxis = -1; bool _hotCenter;
    Vec3 _c0, _start; double _t0, _ang0;
    Plane3 _plane, _frame0;
    Vec3[] _axes = GizmoVisual.WorldAxes;

    public string Prompt => MoveMode && _targets.Count > 0
        ? $"Moving {_targets.Count} item(s) — drag an arrow, block or ring, or type values in the Gizmo panel. Click empty space to re-pick; Esc releases."
        : MoveMode
            ? "Move: click anything to grab it — objects, meshes, datums — then drag the gizmo. Esc hides it."
            : "Drag an arrow to move the datum, a block to move in a plane, or a ring to rotate it. Esc hides it.";
    public Plane3 Frame => Binding.Frame ?? new Plane3(Vec3.Zero, Vec3.UnitX, Vec3.UnitY);
    public Vec3[] Axes => _drag != Drag.None ? _axes : Binding.Axes;

    public void Place(Vec3 origin)
    {
        Binding.Reset();
        Binding.Begin(origin);
        Binding.SetOrigin(origin);
        _targets.Clear(); _applied = Frame; _checkpointed = false;
        MoveMode = false;
        _drag = Drag.None;
    }

    /// <summary>Center an item the gizmo can grab: a datum's own frame, an object's mesh bounds.</summary>
    static Vec3? TargetCenter(CadSession s, SelItem item)
    {
        if (item.Kind == SelKind.Datum) return s.DatumOf(item)?.Frame.Origin;
        var o = s.Find(item.Obj);
        if (o == null || !o.Visible) return null;
        var m = o.Mesh;
        return m.Positions.Length >= 3 ? (m.Min + m.Max) * 0.5 : null;
    }

    /// <summary>Binds targets so the gizmo moves them. A single datum target hands its frame to the gizmo directly.</summary>
    public void BindTargets(CadSession s, IEnumerable<SelItem> items)
    {
        var added = items.Where(t => TargetCenter(s, t) != null).ToList();
        _targets.AddRange(added.Where(t => !_targets.Contains(t)));
        if (_targets.Count == 0) { _applied = Frame; return; }
        if (_targets.Count == 1 && _targets[0].Kind == SelKind.Datum && s.DatumOf(_targets[0]) is { } datum)
            Binding.Adopt(datum.Frame);
        else if (added.Count > 0)
        {
            var c = added.Select(t => TargetCenter(s, t)!.Value).Aggregate(Vec3.Zero, (a, b) => a + b) / added.Count;
            if (Binding.Frame == null) { Binding.Begin(c); Binding.SetOrigin(c); }
            else Binding.SetOrigin(c);
        }
        _applied = Frame; _checkpointed = false;
        s.Say($"{_targets.Count} target(s) bound to the gizmo — drag a handle or type values in the panel.");
        s.RaiseState(); s.Invalidate();
    }

    /// <summary>Releases the payload without hiding the gizmo.</summary>
    public void ClearTargets(CadSession s) { _targets.Clear(); _applied = Frame; s.RaiseState(); }

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
        if (axis < 0 && !center)
        {
            if (!MoveMode) return false;
            var hit = s.Pick(p.X, p.Y, SelFilter.AnyObject | SelFilter.Plane | SelFilter.Axis | SelFilter.Datum);
            if (hit is not { Item: { Kind: not SelKind.Origin } item }) return false;
            ClearTargets(s);
            BindTargets(s, [item]);
            return true;
        }
        if (op != Mode) Mode = op;
        _checkpointed = false;
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

    public void PointerUp(CadSession s, PointerInfo p)
    {
        bool dragged = _drag != Drag.None;
        _drag = Drag.None;
        if (dragged) SyncTargets(s);        // the last frame of the drag always lands, however recently the previous one did
        s.RaiseState();
    }

    public bool Key(CadSession s, string key)
    {
        if (key != "Escape") return false;
        if (_targets.Count > 0) { ClearTargets(s); s.Say("Move targets released — gizmo still up."); return true; }
        s.HideDatumGizmo();
        return true;
    }

    /// <summary>
    /// Applies the gizmo frame's motion to the bound targets. Dragging updates the frame in
    /// <see cref="PointerMove"/>; typed panel values update it through <see cref="GizmoBinding"/> —
    /// either way the delta from the last synced frame becomes one rigid transform per target.
    /// </summary>
    void SyncTargets(CadSession s)
    {
        var frame = Binding.Frame;
        if (_targets.Count == 0 || frame is not { } f) { _applied = frame; return; }
        _targets.RemoveAll(t => t.Kind != SelKind.Datum && s.Find(t.Obj) == null);
        _targets.RemoveAll(t => t.Kind == SelKind.Datum && s.DatumOf(t) == null);
        if (_targets.Count == 0) { _applied = f; return; }
        if (_applied is not { } a)
        {
            _applied = f;
            return;
        }
        if (a.Equals(f)) return;
        // While a handle is held the gizmo follows the pointer at full rate, but the document (browser tree, history, datum drawing)
        // is rewritten at most ~8 times a second; PointerUp flushes whatever is left.
        if (_drag != Drag.None && _sinceCommit.IsRunning && _sinceCommit.ElapsedMilliseconds < CommitIntervalMs) return;
        _sinceCommit.Restart();
        var from = Core.Assembly.Xform.FromFrame(a.Origin, a.U, a.Normal);
        var to = Core.Assembly.Xform.FromFrame(f.Origin, f.U, f.Normal);
        var delta = to * from.Inverse();
        if (delta.IsIdentity) { _applied = f; return; }
        bool first = !_checkpointed; _checkpointed = true;
        _applied = f;       // set first: a transform can reselect (rebinding the gizmo), which must not see a stale frame
        foreach (var t in _targets.ToArray()) s.TransformTarget(t, delta, checkpoint: first);
        s.Invalidate();
    }

    public void BuildOverlay(CadSession s, Overlay o)
    {
        SyncTargets(s);
        var c = Frame.Origin;
        for (int op = 0; op < 2; op++)
            GizmoVisual.DrawOperation(o, s, c, Axes, op, Mode, op == Mode ? _hotAxis : -1, op == Mode && _hotCenter, planes: true);
    }

    public void Deactivate(CadSession s) { }
}
