using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Commands;
using Prism.Core;
using Prism.Core.Form;
using Prism.Kernel;

namespace Prism.Commands;

/// <summary>Click to drop curve points on the command's work plane.</summary>
public sealed class PlacePointsTool(CurveByPointsCommand cmd) : IViewportTool
{
    public List<Vec3> Points { get; } = [];
    Vec3? _hot;
    public string Prompt => "Click to place points. Backspace removes the last point.";

    (Vec3 p, bool snapped)? Snap(CadSession s, double x, double y)
    {
        var (o, d) = s.Camera.Ray(x, y);
        var pl = cmd.Plane;
        var hit = pl.Intersect(o, d);
        if (hit == null) return null;
        int near = Picking.NearestPoint(s.Camera, Points, x, y, 9);
        if (near >= 0) return (Points[near], true);
        if (cmd.Is3D)
        {
            // snap onto existing geometry, otherwise stay on the plane through the last point
            if (Picking.NearestEdge(s.Camera, s.Objects, x, y, 10) is { } edge) return (edge.point, true);
            var (u3, v3) = pl.Project(hit.Value);
            if (!s.SketchGridSnap) return (pl.PointAt(u3, v3), false);
        }
        double g = GridMath.Spacing(s.Camera);
        var (u, v) = pl.Project(hit.Value);
        return (pl.PointAt(Math.Round(u / g) * g, Math.Round(v / g) * g), false);
    }

    public bool PointerDown(CadSession s, PointerInfo p)
    {
        if (!p.Left) return false;
        if (Snap(s, p.X, p.Y) is { } sn)
        {
            if (Points.Count > 0 && Points[^1].DistanceTo(sn.p) < 1e-6) return true;
            Points.Add(sn.p); cmd.Changed();
        }
        return true;
    }

    public void PointerMove(CadSession s, PointerInfo p) { _hot = Snap(s, p.X, p.Y)?.p; if (_hot is { } h) s.Cursor3 = h; s.Invalidate(); }
    public void PointerUp(CadSession s, PointerInfo p) { }

    public bool Key(CadSession s, string key)
    {
        if (key == "Back" && Points.Count > 0) { Points.RemoveAt(Points.Count - 1); cmd.Changed(); return true; }
        if (key == "Tab" && cmd.Is3D) { cmd.NextPlane(); return true; }
        if (key == "RightClick") { if (Points.Count >= 2) s.CommitCommand(); return true; }
        return false;
    }

    public void BuildOverlay(CadSession s, Overlay o)
    {
        if (Points.Count > 0 && cmd.Points.Count >= 2 == false && _hot != null) o.AddLine(Points[^1], _hot.Value, 0xFF7AA7E8, 1.5f);
        if (cmd.Through == false) o.AddPolyline(Points, 0xFF6F86A6, 1.5f);
        if (_hot is { } h) o.AddDot(h, 0xFF1F6FEB, 6);
        for (int i = 0; i < Points.Count; i++) o.AddDot(Points[i], i == Points.Count - 1 ? 0xFFFF8A1F : 0xFF3D5A80, 8);
    }

    public void Deactivate(CadSession s) { }
}

/// <summary>Select and drag NURBS control points, optionally with proportional influence on neighbours.</summary>
public sealed class EditPointsTool(EditPointsCommand cmd) : IViewportTool
{
    enum Drag { None, Point, Axis, Free, Rotate, ScaleAxis, ScaleAll, Plane, ScalePlane }
    public HashSet<int> Selected { get; } = [];
    public bool Dragging => _drag != Drag.None;
    int _hot = -1, _hotAxis = -1; bool _hotCenter;
    Drag _drag; int _axis;
    Vec3[] _base = [];
    double[] _weights = [];
    Vec3 _start, _c0; double _t0, _ang0, _d0;
    Plane3 _dragPlane, _ringPlane;
    public GizmoBinding Binding {get;}=new();
    Vec3[] Axes=>_drag!=Drag.None?_dragAxes:Binding.Axes;
    Vec3[] _dragAxes=GizmoVisual.WorldAxes;
    Plane3 _bindingStart;
    int _hotMode = -1;
    public string Prompt => "Click a control point (Shift adds), or drag in empty space to select points inside a rectangle (or the lasso chosen in Select; Ctrl takes away). Drag the point itself, or use the gizmo: arrows move, rings rotate (R), blocks scale (S), T for move. Set an influence radius for soft edits.";

    int Mode => cmd.TransformMode.Index;     // 0 move, 1 rotate, 2 scale

    /// <summary>Control points inside a rectangle or lasso: Ctrl takes them away, otherwise they are added.</summary>
    void SelectInside(CadSession s, RegionSelect.Region region, bool remove)
    {
        var m = cmd.Model; if (m == null) return;
        for (int i = 0; i < m.Points.Length; i++)
            if (s.Camera.Project(m.Points[i]) is { } q && region.Contains(q.x, q.y)) { if (remove) Selected.Remove(i); else Selected.Add(i); }
        cmd.SelectionChanged(); s.Invalidate();
    }

    public void Reset() { Binding.Reset();Selected.Clear(); _hot = -1; _drag = Drag.None; cmd.SelectionChanged(); }

    // ---------------------------------------------------------------- gizmo geometry
    public Vec3? GizmoCentre()
    {
        Binding.CheckTargets(Selected);
        if(Binding.Positioning)return Binding.Frame?.Origin;
        var m = cmd.Model; if (m == null || Selected.Count == 0) return null;
        var sum = Vec3.Zero; foreach (int i in Selected) sum += m.Points[i];
        return Binding.Center(sum / Selected.Count);
    }

    (Vec3 c, double len)? Gizmo(CadSession s) => GizmoCentre() is { } c ? (c, GizmoVisual.Length(s,c)) : null;

    (Vec3 u, Vec3 v) RingBasis(int axis) => axis == 0 ? (Axes[1], Axes[2]) : axis == 1 ? (Axes[2], Axes[0]) : (Axes[0], Axes[1]);

    IEnumerable<Vec3> RingPoints(Vec3 c, double r, int axis)
    {
        var (u, v) = RingBasis(axis);
        for (int i = 0; i <= 64; i++) { double a = 2 * Math.PI * i / 64; yield return c + (u * Math.Cos(a) + v * Math.Sin(a)) * r; }
    }

    static double ToSeg(CadSession s, double px, double py, Vec3 a, Vec3 b)
    {
        var pa = s.Camera.Project(a); var pb = s.Camera.Project(b);
        if (pa == null || pb == null) return double.MaxValue;
        double dx = pb.Value.x - pa.Value.x, dy = pb.Value.y - pa.Value.y, l2 = dx * dx + dy * dy;
        double t = l2 < 1e-9 ? 0 : Math.Clamp(((px - pa.Value.x) * dx + (py - pa.Value.y) * dy) / l2, 0, 1);
        double qx = pa.Value.x + dx * t - px, qy = pa.Value.y + dy * t - py;
        return Math.Sqrt(qx * qx + qy * qy);
    }

    static double? AxisParam(Vec3 o, Vec3 d, Vec3 c, Vec3 a)
    {
        var w = c - o; double b = Vec3.Dot(a, d), dd = Vec3.Dot(a, w), ee = Vec3.Dot(d, w), den = 1 - b * b;
        if (den < 1e-6) return null;
        return (b * ee - dd) / den;
    }

    (int axis, bool center, int operation) HitGizmo(CadSession s, double x, double y)
    {
        if (Gizmo(s) is not { } g) return (-1, false, Mode);
        return GizmoVisual.Hit(s, x, y, _ => g.c, Axes, Mode, planes: true);
    }

    // ---------------------------------------------------------------- weights and applying a transform
    double[] Weights(Vec3[] basePts)
    {
        var w = new double[basePts.Length];
        double radius = cmd.Radius.Value; var fall = cmd.FalloffKind;
        for (int j = 0; j < w.Length; j++)
        {
            if (Selected.Contains(j)) { w[j] = 1; continue; }
            if (radius <= 0) continue;
            foreach (int i in Selected) w[j] = Math.Max(w[j], FalloffMath.Weight(fall, basePts[i].DistanceTo(basePts[j]), radius));
        }
        return w;
    }

    void Transform(CadSession s, Func<Vec3, Vec3> xf)
    {
        if(Binding.Positioning){Binding.Transform(_bindingStart,xf);s.Invalidate();return;}
        var pts = (Vec3[])_base.Clone();
        for (int j = 0; j < pts.Length; j++) if (_weights[j] > 0) pts[j] = _base[j] + (xf(_base[j]) - _base[j]) * _weights[j];
        try { cmd.ApplyPoints(pts);if(Binding.Bound)Binding.Transform(_bindingStart,xf);cmd.Touch(); }
        catch (KernelException e) { s.Say(e.Message, true); }
    }

    void BeginDrag(Vec3[] pts) { _base = (Vec3[])pts.Clone(); _weights = Weights(_base);_dragAxes=Binding.Axes;_bindingStart=Binding.Frame??new Plane3(GizmoCentre()??Vec3.Zero,Vec3.UnitX,Vec3.UnitY); }

    // ---------------------------------------------------------------- pointer
    public bool PointerDown(CadSession s, PointerInfo p)
    {
        var m = cmd.Model;
        if (m == null || !p.Left) return false;

        // Shift always means "add this point to the selection", even when the gizmo sits over it
        var (axis, center, operation) = p.Shift ? (-1, false, Mode) : HitGizmo(s, p.X, p.Y); axis = GizmoVisual.AsPlane(axis, operation, p.Ctrl);
        if (axis >= 0 || center)
        {
            _hotMode = operation; _hotAxis = axis; _hotCenter = center; s.Invalidate();
            if (Mode != operation) cmd.TransformMode.Index = operation;
            BeginDrag(m.Points); _c0 = GizmoCentre()!.Value;
            var (o, d) = s.Camera.Ray(p.X, p.Y);
            if (Mode == 1)
            {
                _drag = Drag.Rotate; _axis = axis;
                var (ru, rv) = RingBasis(axis); _ringPlane = new Plane3(_c0, ru, rv);
                var h0 = _ringPlane.Intersect(o, d) ?? _c0 + ru;
                _ang0 = Math.Atan2(Vec3.Dot(h0 - _c0, rv), Vec3.Dot(h0 - _c0, ru));
            }
            else if (Mode == 2)
            {
                if (center)
                {
                    _drag = Drag.ScaleAll; var pc = s.Camera.Project(_c0);
                    _d0 = pc == null ? 50 : Math.Max(10, Math.Sqrt((pc.Value.x - p.X) * (pc.Value.x - p.X) + (pc.Value.y - p.Y) * (pc.Value.y - p.Y)));
                }
                else if (axis >= GizmoVisual.PlaneBase)
                {
                    _drag = Drag.ScalePlane; _axis = axis;
                    var (pu, pv) = GizmoVisual.PlaneAxes(Axes, axis); _dragPlane = new Plane3(_c0, pu, pv);
                    _start = _dragPlane.Intersect(o, d) ?? _c0 + pu; _d0 = Math.Max(1e-6, (_start - _c0).Length);
                }
                else { _drag = Drag.ScaleAxis; _axis = axis; var tt = AxisParam(o, d, _c0, Axes[axis]) ?? 0; _t0 = Math.Abs(tt) < 1e-6 ? 1e-6 : tt; }
            }
            else if (axis >= GizmoVisual.PlaneBase)
            {
                _drag = Drag.Plane; _axis = axis;
                var (pu, pv) = GizmoVisual.PlaneAxes(Axes, axis); _dragPlane = new Plane3(_c0, pu, pv);
                _start = _dragPlane.Intersect(o, d) ?? _c0;
            }
            else if (center) { _drag = Drag.Free; _dragPlane = new Plane3(_c0, s.Camera.Right, s.Camera.Up); _start = _dragPlane.Intersect(o, d) ?? _c0; }
            else { _drag = Drag.Axis; _axis = axis; _t0 = AxisParam(o, d, _c0, Axes[axis]) ?? 0; }
            s.Invalidate();
            return true;
        }

        int i = Picking.NearestPoint(s.Camera, m.Points, p.X, p.Y, 12);
        if (i < 0)
        {
            if (!p.Shift && !p.Ctrl) { Selected.Clear(); cmd.SelectionChanged(); }
            s.BeginToolRegion(p, (region, up) => SelectInside(s, region, up.Ctrl));
            s.Invalidate(); return true;
        }
        if (p.Shift) { if (!Selected.Remove(i)) Selected.Add(i); }
        else if (!Selected.Contains(i)) { Selected.Clear(); Selected.Add(i); }
        cmd.SelectionChanged();
        if (!Selected.Contains(i)) { s.Invalidate(); return true; }
        if(Binding.Positioning){s.Invalidate();return true;}

        // dragging a point itself moves the selection in the plane parallel to the screen
        _dragPlane = new Plane3(m.Points[i], s.Camera.Right, s.Camera.Up);
        var (ro, rd) = s.Camera.Ray(p.X, p.Y);
        _start = _dragPlane.Intersect(ro, rd) ?? m.Points[i];
        BeginDrag(m.Points); _drag = Drag.Point;
        s.Invalidate();
        return true;
    }

    public void PointerMove(CadSession s, PointerInfo p)
    {
        var m = cmd.Model;
        if (m == null) return;
        if (_drag == Drag.None)
        {
            int h = Picking.NearestPoint(s.Camera, m.Points, p.X, p.Y, 12);
            var (ax, ce, operation) = HitGizmo(s, p.X, p.Y); ax = GizmoVisual.AsPlane(ax, operation, p.Ctrl);
            if (operation != _hotMode || h != _hot || ax != _hotAxis || ce != _hotCenter) { _hotMode = operation; _hot = h; _hotAxis = ax; _hotCenter = ce; s.Invalidate(); }
            return;
        }
        var (o, d) = s.Camera.Ray(p.X, p.Y);
        switch (_drag)
        {
            case Drag.Point:
            case Drag.Free:
                if (_dragPlane.Intersect(o, d) is { } hit) { var delta = hit - _start; Transform(s, FormTransform.MoveBy(GizmoVisual.SnapMove(delta,p.Alt))); }
                break;
            case Drag.Plane:
                if (_dragPlane.Intersect(o, d) is { } ph) { Transform(s, FormTransform.MoveBy(GizmoVisual.SnapMove(ph - _start, p.Alt))); s.Say($"Move in the {GizmoVisual.PlaneName(_axis)} plane"); }
                break;
            case Drag.ScalePlane:
            {
                if (_dragPlane.Intersect(o, d) is not { } sh) break;
                double fp = GizmoVisual.SnapScale(Math.Clamp((sh - _c0).Length / _d0, 0.02, 50), p.Alt);
                int k = _axis - GizmoVisual.PlaneBase;
                Transform(s, GizmoBinding.Scale(_c0, Axes, k == 0 ? 1 : fp, k == 1 ? 1 : fp, k == 2 ? 1 : fp));
                s.Say($"Scale {fp:0.###} in the {GizmoVisual.PlaneName(_axis)} plane");
                break;
            }
            case Drag.Axis:
                if (AxisParam(o, d, _c0, Axes[_axis]) is double t) Transform(s, FormTransform.MoveBy(GizmoVisual.SnapMove(Axes[_axis] * (t - _t0),p.Alt)));
                break;
            case Drag.Rotate:
            {
                if (_ringPlane.Intersect(o, d) is not { } rh) break;
                var (ru, rv) = RingBasis(_axis);
                double a = Math.Atan2(Vec3.Dot(rh - _c0, rv), Vec3.Dot(rh - _c0, ru));
                double delta = a - _ang0; while (delta > Math.PI) delta -= 2 * Math.PI; while (delta < -Math.PI) delta += 2 * Math.PI;
                delta = GizmoVisual.SnapAngle(delta, p.Alt);
                Transform(s, FormTransform.RotateAbout(_c0, Axes[_axis], delta));
                s.Say($"Rotate {delta * 180 / Math.PI:0.#}° about {(_axis == 0 ? "X" : _axis == 1 ? "Y" : "Z")}");
                break;
            }
            case Drag.ScaleAxis:
            {
                if (AxisParam(o, d, _c0, Axes[_axis]) is not double ts) break;
                double f = GizmoVisual.SnapScale(Math.Clamp(ts / _t0, 0.02, 50), p.Alt);
                Transform(s, GizmoBinding.Scale(_c0,Axes, _axis == 0 ? f : 1, _axis == 1 ? f : 1, _axis == 2 ? f : 1));
                s.Say($"Scale {f:0.###} along {(_axis == 0 ? "X" : _axis == 1 ? "Y" : "Z")}");
                break;
            }
            case Drag.ScaleAll:
            {
                var pc = s.Camera.Project(_c0); if (pc == null) break;
                double dist = Math.Max(4, Math.Sqrt((pc.Value.x - p.X) * (pc.Value.x - p.X) + (pc.Value.y - p.Y) * (pc.Value.y - p.Y)));
                double f = GizmoVisual.SnapScale(Math.Clamp(dist / _d0, 0.02, 50), p.Alt);
                Transform(s, FormTransform.ScaleAbout(_c0, f, f, f));
                s.Say($"Scale {f:0.###}");
                break;
            }
        }
    }

    public void PointerUp(CadSession s, PointerInfo p)
    {
        if (_drag == Drag.None) return;
        _drag = Drag.None;
        cmd.SelectionChanged();
    }

    public bool Key(CadSession s, string key)
    {
        if(Binding.Positioning&&key is "Escape" or "Enter")
        {if(key=="Escape")Binding.Cancel();else if(!Binding.Bind(Selected))s.Say("Select control points before binding the gizmo.",true);s.RaiseState();s.Invalidate();return true;}
        if (key == "Char:T") { cmd.TransformMode.Index = 0; s.Say("Move gizmo."); cmd.Touch(); return true; }
        if (key == "Char:R") { cmd.TransformMode.Index = 1; s.Say("Rotate gizmo: drag a ring."); cmd.Touch(); return true; }
        if (key == "Char:S") { cmd.TransformMode.Index = 2; s.Say("Scale gizmo: drag a block, or the centre for uniform scale."); cmd.Touch(); return true; }
        if (key == "Escape" && Selected.Count > 0) { Selected.Clear(); cmd.SelectionChanged(); s.Invalidate(); return true; }
        return false;
    }

    public void BuildOverlay(CadSession s, Overlay o)
    {
        var m = cmd.Model;
        if (m == null) return;
        m.BuildNetOverlay(o);
        for (int i = 0; i < m.Points.Length; i++)
        {
            bool sel = Selected.Contains(i);
            o.AddDot(m.Points[i], sel ? 0xFFFF8A1F : i == _hot ? 0xFF00A8FF : 0xFF2F4A73, sel || i == _hot ? 11 : 8);
        }
        if (Gizmo(s) is not { } g) return;
        for (int operation = 0; operation < 3; operation++)
            GizmoVisual.DrawOperation(o, s, g.c, Axes, operation, Mode,
                _hotMode == operation ? _hotAxis : -1, _hotMode == operation && _hotCenter, planes: true);

    }

    public void Deactivate(CadSession s) { }
}
/// <summary>Brush that deforms the control net around the point under the cursor.</summary>
public sealed class SculptTool(SculptCommand cmd) : IViewportTool
{
    bool _down;
    Vec3 _p, _n;               // brush centre / normal on the surface
    bool _has;
    double _x0, _y0, _pxSize;
    Vec3[] _base = [];
    public string Prompt => "Drag on the surface to sculpt. The ring shows the brush radius.";

    (Vec3 p, Vec3 n)? Probe(CadSession s, double x, double y)
    {
        var mesh = s.Preview.FirstOrDefault()?.Mesh;
        if (mesh == null) return null;
        var r = Picking.RayMesh(s.Camera, mesh, x, y);
        return r is { } h ? (h.point, h.normal) : null;
    }

    public bool PointerDown(CadSession s, PointerInfo p)
    {
        if (cmd.Model == null || !p.Left) return false;
        if (Probe(s, p.X, p.Y) is not { } hit) return true;
        (_p, _n) = hit; _has = true; _down = true;
        _x0 = p.X; _y0 = p.Y;
        _pxSize = s.Camera.PixelSizeAt(_p);
        _base = (Vec3[])cmd.Model.Points.Clone();
        return true;
    }

    public void PointerMove(CadSession s, PointerInfo p)
    {
        var m = cmd.Model;
        if (m == null) return;
        if (!_down)
        {
            var h = Probe(s, p.X, p.Y);
            _has = h != null; if (h is { } v) (_p, _n) = v;
            s.Invalidate();
            return;
        }
        double R = cmd.Radius.Value;
        var fall = cmd.FalloffKind;
        var pts = (Vec3[])_base.Clone();
        switch (cmd.Mode.Index)
        {
            case 0: // pull along normal: dragging up pulls out
            {
                double d = (_y0 - p.Y) * _pxSize;
                for (int j = 0; j < pts.Length; j++) pts[j] = _base[j] + _n * (d * FalloffMath.Weight(fall, _base[j].DistanceTo(_p), R));
                break;
            }
            case 1: // follow the cursor in the screen plane
            {
                var delta = (s.Camera.Right * (p.X - _x0) - s.Camera.Up * (p.Y - _y0)) * _pxSize;
                for (int j = 0; j < pts.Length; j++) pts[j] = _base[j] + delta * FalloffMath.Weight(fall, _base[j].DistanceTo(_p), R);
                break;
            }
            default: // relax toward neighbour average; accumulates while dragging
            {
                pts = (Vec3[])m.Points.Clone();
                var cur = m.Points; int rows = m.Net.Rows, cols = m.Net.Cols;
                double k = cmd.Strength.Value * 0.35;
                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < cols; c++)
                    {
                        int j = r * cols + c;
                        double w = FalloffMath.Weight(fall, cur[j].DistanceTo(_p), R);
                        if (w <= 0) continue;
                        Vec3 sum = Vec3.Zero; int n = 0;
                        if (!m.IsSurface) { if (r > 0) { sum += cur[j - 1]; n++; } if (r < rows - 1) { sum += cur[j + 1]; n++; } }
                        else
                        {
                            if (r > 0) { sum += cur[j - cols]; n++; }
                            if (r < rows - 1) { sum += cur[j + cols]; n++; }
                            if (c > 0) { sum += cur[j - 1]; n++; }
                            if (c < cols - 1) { sum += cur[j + 1]; n++; }
                        }
                        if (n > 0) pts[j] = Vec3.Lerp(cur[j], sum / n, Math.Clamp(k * w, 0, 1));
                    }
                break;
            }
        }
        try { cmd.ApplyPoints(pts); cmd.Touch(); }
        catch (KernelException e) { s.Say(e.Message, true); }
    }

    public void PointerUp(CadSession s, PointerInfo p) { _down = false; }
    public bool Key(CadSession s, string key) => false;

    public void BuildOverlay(CadSession s, Overlay o)
    {
        if (cmd.Model == null) return;
        if (cmd.Model.Points.Length <= 400) cmd.Model.BuildNetOverlay(o, 0x806F86A6);
        if (!_has) return;
        var u = Vec3.Cross(_n, Math.Abs(_n.Z) < 0.9 ? Vec3.UnitZ : Vec3.UnitX).Normalized();
        var v = Vec3.Cross(_n, u);
        double R = cmd.Radius.Value;
        var ring = Enumerable.Range(0, 73).Select(i => { double a = i * Math.PI * 2 / 72; return _p + _n * (R * 0.02) + u * (Math.Cos(a) * R) + v * (Math.Sin(a) * R); }).ToList();
        o.AddPolyline(ring, _down ? 0xFFFF8A1F : 0xFF1F6FEB, 2.5f);
        o.AddDot(_p, _down ? 0xFFFF8A1F : 0xFF1F6FEB, 7);
    }

    public void Deactivate(CadSession s) { }
}
