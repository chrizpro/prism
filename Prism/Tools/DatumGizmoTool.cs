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
    enum Drag { None, Axis, Free, Plane, Rotate, ScaleAxis, ScaleAll, ScalePlane }
    public GizmoBinding Binding { get; } = new();
    public int Mode { get; set; }              // 0 move, 1 rotate, 2 scale (only while the Move tool holds something)
    /// <summary>Move-tool mode: a click that misses the handles binds whatever was picked as the gizmo's payload.</summary>
    public bool MoveMode { get; set; }
    /// <summary>
    /// Move pivot (D, owner request 2026-11-23): the handles move and turn only the gizmo while what it holds stays put and stays held. A click on a corner
    /// puts the pivot there; a click on a straight edge puts it on the edge and lines the gizmo's X up with it, so the X ring turns about that edge.
    /// </summary>
    public bool PivotMode { get; private set; }
    /// <summary>The user placed the pivot (in Move pivot, or by dragging the gizmo while it held nothing): grabbing something keeps it instead of centring.</summary>
    bool _pivotPlaced;
    /// <summary>Scaling needs something held and is not a placement: rings and arrows are enough to place a pivot.</summary>
    public bool CanScale => MoveMode && !PivotMode && _targets.Count > 0;
    Vec3 _scale = new(1, 1, 1);                // the factors of the scale drag in progress, along the gizmo's axes
    double _d0;
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

    public string Prompt => MoveMode && PivotMode
        ? "Move pivot: the handles move and turn only the gizmo. Click a corner to put the pivot there, or a straight edge to hinge on it. Press D again to go back."
        : MoveMode && _targets.Count > 0
        ? $"Moving {_targets.Count} item(s) — drag an arrow, block, ring or scale handle (hold Alt to drag a copy), or type values in the Gizmo panel. D moves the pivot. Click empty space to re-pick; Esc releases."
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
        MoveMode = false; PivotMode = false; _pivotPlaced = false;     // the pivot resets each time the gizmo is shown (owner decision 2026-11-23)
        _drag = Drag.None;
    }

    /// <summary>D: switch between moving what the gizmo holds and moving the gizmo itself.</summary>
    public void TogglePivot(CadSession s)
    {
        if (!MoveMode) return;
        PivotMode = !PivotMode;
        if (PivotMode) { _pivotPlaced = true; Mode = Math.Min(Mode, 1); }
        _applied = Frame;                       // nothing the pivot did is carried over to the held items
        s.Say(PivotMode ? "Move pivot: drag the gizmo, or click a corner or a straight edge to put the pivot there. D again goes back to moving what it holds."
            : _targets.Count > 0 ? "Back to moving what the gizmo holds, about the new pivot." : "Pivot placed. Click something to grab it.");
        s.RaiseState(); s.Invalidate();
    }

    /// <summary>The panel typed a new gizmo position or angle while nothing is held, or in Move pivot: that is placing the pivot.</summary>
    public void NotePivotEdited() { if (PivotMode || _targets.Count == 0) _pivotPlaced = true; }

    /// <summary>In Move pivot, a click on a corner moves the pivot there; on a straight edge it moves onto the edge (to a corner or the middle when near one) and turns X along it.</summary>
    bool PlacePivotAt(CadSession s, PointerInfo p)
    {
        var hit = s.Pick(p.X, p.Y, SelFilter.Vertex | SelFilter.Edge);
        if (hit is not { } h || s.ShapeOf(h.Item) is not { } shape) return false;
        var f = Frame;
        if (h.Item.Kind == SelKind.Vertex)
        {
            Binding.Adopt(new Plane3(shape.Props().Center, f.U, f.V));
            s.Say("Pivot on the corner.");
        }
        else
        {
            var e = Occ.EdgeInfoOf(shape);
            Vec3 at = h.Point;
            foreach (var key in new[] { e.Start, e.End, (e.Start + e.End) * 0.5 })
                if (s.Camera.Project(key) is { } k && Math.Sqrt(Math.Pow(k.x - p.X, 2) + Math.Pow(k.y - p.Y, 2)) < 14) { at = key; break; }
            if (e.Type == "Line" && (e.End - e.Start).Length > 1e-9)
            {
                var x = (e.End - e.Start).Normalized();
                var helper = Math.Abs(Vec3.Dot(x, f.V)) < 0.9 ? f.V : f.Normal;
                var y = (helper - x * Vec3.Dot(helper, x)).Normalized();
                Binding.Adopt(new Plane3(at, x, y));
                s.Say("Pivot on the edge, with the gizmo's X along it: the X ring turns about this edge.");
            }
            else
            {
                Binding.Adopt(new Plane3(at, f.U, f.V));
                s.Say("Pivot on the edge.");
            }
        }
        _applied = Frame; _pivotPlaced = true;
        s.RaiseState(); s.Invalidate();
        return true;
    }

    /// <summary>Center an item the gizmo can grab: a datum's own frame, an object's mesh bounds.</summary>
    static Vec3? TargetCenter(CadSession s, SelItem item)
    {
        if (item.Kind == SelKind.Datum) return s.DatumOf(item)?.Frame.Origin;
        var o = s.Find(item.Obj);
        if (o == null || !o.Visible) return null;
        var m = o.Mesh;
        // a curve or sketch has no triangles, only edge lines: its box still says where it is (it used to be refused, so the gizmo moved alone)
        return m.Positions.Length >= 3 || m.EdgePoints.Length >= 3 ? (m.Min + m.Max) * 0.5 : null;
    }

    /// <summary>Binds targets so the gizmo moves them. A single datum target hands its frame to the gizmo directly.</summary>
    public void BindTargets(CadSession s, IEnumerable<SelItem> items)
    {
        var wanted = items.Where(t => TargetCenter(s, t) != null).ToList();
        // parts held by a joint or grounded are not free to drag: say why instead of silently doing nothing on the drag
        var held = wanted.Where(t => t.Kind != SelKind.Datum && s.Find(t.Obj) is { } o && s.WhyPartIsHeld(o) != null).ToList();
        foreach (var t in held) s.Say(s.WhyPartIsHeld(s.Find(t.Obj)!)!, true);
        var added = wanted.Except(held).ToList();
        if (added.Count == 0 && held.Count > 0) { s.RaiseState(); s.Invalidate(); return; }
        _targets.AddRange(added.Where(t => !_targets.Contains(t)));
        if (_targets.Count == 0) { _applied = Frame; return; }
        if (_targets.Count == 1 && _targets[0].Kind == SelKind.Datum && s.DatumOf(_targets[0]) is { } datum)
            Binding.Adopt(datum.Frame);
        else if (added.Count > 0)
        {
            var c = added.Select(t => TargetCenter(s, t)!.Value).Aggregate(Vec3.Zero, (a, b) => a + b) / added.Count;
            if (Binding.Frame == null) { Binding.Begin(c); Binding.SetOrigin(c); }
            else if (!_pivotPlaced) Binding.SetOrigin(c);     // a pivot the user placed stays where it is
        }
        _applied = Frame; _checkpointed = false;
        s.Say($"{_targets.Count} target(s) bound to the gizmo — drag a handle or type values in the panel.");
        s.RaiseState(); s.Invalidate();
    }

    /// <summary>Releases the payload without hiding the gizmo.</summary>
    public void ClearTargets(CadSession s) { _targets.Clear(); _applied = Frame; if (Mode == 2) Mode = 0; s.RaiseState(); }

    (Vec3 u, Vec3 v) RingBasis(int a) => a == 0 ? (_axes[1], _axes[2]) : a == 1 ? (_axes[2], _axes[0]) : (_axes[0], _axes[1]);

    static double? AxisParam(Vec3 o, Vec3 d, Vec3 c, Vec3 a)
    {
        var w = c - o; double b = Vec3.Dot(a, d), dd = Vec3.Dot(a, w), ee = Vec3.Dot(d, w), den = 1 - b * b;
        return den < 1e-6 ? null : (b * ee - dd) / den;
    }

    (int axis, bool center, int operation) Hit(CadSession s, double x, double y) =>
        GizmoVisual.Hit(s, x, y, _ => Frame.Origin, Axes, Mode, planes: true, allow: (op, _) => op != 2 || CanScale);

    public bool PointerDown(CadSession s, PointerInfo p)
    {
        if (!p.Left) return false;
        var (axis, center, op) = Hit(s, p.X, p.Y);
        axis = GizmoVisual.AsPlane(axis, op, p.Ctrl);
        if (axis < 0 && !center)
        {
            if (!MoveMode) return false;
            if (PivotMode) return PlacePivotAt(s, p);
            var hit = s.Pick(p.X, p.Y, SelFilter.AnyObject | SelFilter.Plane | SelFilter.Axis | SelFilter.Datum);
            if (hit is not { Item: { Kind: not SelKind.Origin } item }) return false;
            ClearTargets(s);
            BindTargets(s, [item]);
            return true;
        }
        if (op != Mode) Mode = op;
        _checkpointed = false;
        // Alt-drag with the Move tool leaves the originals where they are and drags copies
        if (p.Alt && MoveMode && Mode == 0) CopyTargetsForDrag(s);
        _axes = Binding.Axes; _frame0 = Frame; _c0 = _frame0.Origin; _axis = axis;
        var (o, d) = s.Camera.Ray(p.X, p.Y);
        if (Mode == 2)
        {
            _scale = new(1, 1, 1);
            if (center)
            {
                _drag = Drag.ScaleAll; var pc = s.Camera.Project(_c0);
                _d0 = pc == null ? 50 : Math.Max(10, Math.Sqrt((pc.Value.x - p.X) * (pc.Value.x - p.X) + (pc.Value.y - p.Y) * (pc.Value.y - p.Y)));
            }
            else if (axis >= GizmoVisual.PlaneBase)
            {
                _drag = Drag.ScalePlane;
                var (pu, pv) = GizmoVisual.PlaneAxes(_axes, axis); _plane = new Plane3(_c0, pu, pv);
                _start = _plane.Intersect(o, d) ?? _c0 + pu; _d0 = Math.Max(1e-6, (_start - _c0).Length);
            }
            else
            {
                _drag = Drag.ScaleAxis;
                var tt = AxisParam(o, d, _c0, _axes[axis]) ?? 0; _t0 = Math.Abs(tt) < 1e-6 ? 1e-6 : tt;
            }
            s.Invalidate();
            return true;
        }
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
            // scaling leaves the gizmo where it is: the factors are shown as a box and applied when the handle is let go
            case Drag.ScaleAxis:
                if (AxisParam(o, d, _c0, _axes[_axis]) is double ts)
                {
                    double f = GizmoVisual.SnapScale(Math.Clamp(ts / _t0, 0.02, 50), p.Alt);
                    _scale = new(_axis == 0 ? f : 1, _axis == 1 ? f : 1, _axis == 2 ? f : 1);
                    s.Say($"Scale {f:0.###} along {(_axis == 0 ? "X" : _axis == 1 ? "Y" : "Z")}");
                }
                break;
            case Drag.ScalePlane:
                if (_plane.Intersect(o, d) is { } sh)
                {
                    double f = GizmoVisual.SnapScale(Math.Clamp((sh - _c0).Length / _d0, 0.02, 50), p.Alt);
                    int k = _axis - GizmoVisual.PlaneBase;
                    _scale = new(k == 0 ? 1 : f, k == 1 ? 1 : f, k == 2 ? 1 : f);
                    s.Say($"Scale {f:0.###} in the {GizmoVisual.PlaneName(_axis)} plane");
                }
                break;
            case Drag.ScaleAll:
                if (s.Camera.Project(_c0) is { } pc)
                {
                    double dist = Math.Max(4, Math.Sqrt((pc.x - p.X) * (pc.x - p.X) + (pc.y - p.Y) * (pc.y - p.Y)));
                    double f = GizmoVisual.SnapScale(Math.Clamp(dist / _d0, 0.02, 50), p.Alt);
                    _scale = new(f, f, f);
                    s.Say($"Scale {f:0.###}");
                }
                break;
        }
        s.RaiseState(); s.Invalidate();
    }

    List<SceneObject>? _dragCopies;

    void CopyTargetsForDrag(CadSession s)
    {
        // only what can be copied is swapped for a copy; anything else in the selection (an assembly component, say) keeps moving as itself
        var objects = _targets.Where(t => t.Kind == SelKind.Object && s.IsCopyable(t.Obj)).ToList();
        if (objects.Count == 0 || s.CopyForDrag(objects.Select(t => t.Obj)) is not { } copies) return;
        _targets.RemoveAll(objects.Contains);
        _targets.AddRange(copies.Select(SelItem.Of));
        _dragCopies = copies;
        s.Say($"Dragging {copies.Count} cop{(copies.Count == 1 ? "y" : "ies")}; the originals stay where they are.");
    }

    public void PointerUp(CadSession s, PointerInfo p)
    {
        bool scaling = _drag is Drag.ScaleAxis or Drag.ScaleAll or Drag.ScalePlane;
        bool dragged = _drag != Drag.None && !scaling;
        _drag = Drag.None;
        if (scaling) { ScaleTargets(s, _scale); _scale = new(1, 1, 1); }
        if (dragged && (PivotMode || _targets.Count == 0)) _pivotPlaced = true;
        if (dragged) SyncTargets(s);        // the last frame of the drag always lands, however recently the previous one did
        if (_dragCopies is { } copies) { _dragCopies = null; s.SelectObjects(copies.Select(c => c.Id)); }
        s.RaiseState();
    }

    public bool Key(CadSession s, string key)
    {
        if (key != "Escape") return false;
        if (_targets.Count > 0) { ClearTargets(s); s.Say("Move targets released — gizmo still up."); return true; }
        s.HideDatumGizmo();
        return true;
    }

    /// <summary>Scale what the gizmo holds about the pivot, by these factors along the gizmo's own axes, as one undo step.</summary>
    public void ScaleTargets(CadSession s, Vec3 factors)
    {
        if (!CanScale || Math.Abs(factors.X - 1) + Math.Abs(factors.Y - 1) + Math.Abs(factors.Z - 1) < 1e-9) return;
        var f = Frame; bool first = true;
        foreach (var t in _targets.ToArray()) { s.ScaleTarget(t, f.Origin, [f.U, f.V, f.Normal], factors, checkpoint: first); first = false; }
        _applied = Frame;
        s.RaiseState(); s.Invalidate();
    }

    /// <summary>
    /// Applies the gizmo frame's motion to the bound targets. Dragging updates the frame in
    /// <see cref="PointerMove"/>; typed panel values update it through <see cref="GizmoBinding"/> —
    /// either way the delta from the last synced frame becomes one rigid transform per target.
    /// </summary>
    void SyncTargets(CadSession s)
    {
        var frame = Binding.Frame;
        if (_targets.Count == 0 || frame is not { } f || PivotMode) { _applied = frame; return; }
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
        for (int op = 0; op < (CanScale ? 3 : 2); op++)
            GizmoVisual.DrawOperation(o, s, c, Axes, op, Mode, op == Mode ? _hotAxis : -1, op == Mode && _hotCenter, planes: true);
        if (_drag is Drag.ScaleAxis or Drag.ScaleAll or Drag.ScalePlane) DrawScaledBox(s, o);
    }

    /// <summary>While a scale handle is held, the held items' box as it will be after the scale.</summary>
    void DrawScaledBox(CadSession s, Overlay o)
    {
        var f = Frame; Vec3[] ax = [f.U, f.V, f.Normal];
        var pts = new List<Vec3>();
        foreach (var t in _targets)
            if (t.Kind != SelKind.Datum && s.Find(t.Obj) is { } obj && obj.Mesh is { } m && m.Positions.Length >= 3) { pts.Add(m.Min); pts.Add(m.Max); pts.Add(new(m.Min.X, m.Max.Y, m.Min.Z)); pts.Add(new(m.Max.X, m.Min.Y, m.Max.Z)); }
        if (pts.Count == 0) return;
        double[] lo = [double.MaxValue, double.MaxValue, double.MaxValue], hi = [double.MinValue, double.MinValue, double.MinValue];
        foreach (var q in pts) for (int i = 0; i < 3; i++) { double v = Vec3.Dot(q - f.Origin, ax[i]) * (i == 0 ? _scale.X : i == 1 ? _scale.Y : _scale.Z); lo[i] = Math.Min(lo[i], v); hi[i] = Math.Max(hi[i], v); }
        Vec3 P(int a, int b, int c) => f.Origin + ax[0] * (a == 0 ? lo[0] : hi[0]) + ax[1] * (b == 0 ? lo[1] : hi[1]) + ax[2] * (c == 0 ? lo[2] : hi[2]);
        for (int a = 0; a < 2; a++) for (int b = 0; b < 2; b++) { o.AddLine(P(0, a, b), P(1, a, b), GizmoVisual.Accent, 1.5f); o.AddLine(P(a, 0, b), P(a, 1, b), GizmoVisual.Accent, 1.5f); o.AddLine(P(a, b, 0), P(a, b, 1), GizmoVisual.Accent, 1.5f); }
    }

    public void Deactivate(CadSession s) { }
}
