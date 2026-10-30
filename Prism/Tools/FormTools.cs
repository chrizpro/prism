using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core;
using Prism.Core.Form;
using Prism.Kernel;

namespace Prism.Commands;

static class FormOverlay
{
    const uint CageColor = 0xFFE2F1FF, CreaseColor = 0xFFFF62A5, VertColor = 0xFFE2F1FF;
    static SubdMesh? _cachedFor; static int _cf, _cv; static List<(int a, int b)> _edges = [];

    public static List<(int a, int b)> Edges(SubdMesh m)
    {
        if (!ReferenceEquals(m, _cachedFor) || _cf != m.F.Count || _cv != m.V.Count)
        {
            _edges = m.Edges().ToList(); _cachedFor = m; _cf = m.F.Count; _cv = m.V.Count;
        }
        return _edges;
    }

    public static void Cage(Overlay o, SubdMesh m, bool vertices = true)
    {
        foreach (var (a, b) in Edges(m))
        {
            bool crease = m.Crease.TryGetValue(SubdMesh.Key(a, b), out var s) && s > 0;
            o.AddLine(m.V[a], m.V[b], 0xEE101923, crease ? 5.5f : 4.5f);
            o.AddLine(m.V[a], m.V[b], crease ? CreaseColor : CageColor, crease ? 3f : 2f);
        }
        if (vertices) foreach (var v in m.V) {o.AddDot(v,0xFF101923,10);o.AddDot(v, VertColor, 6);}
    }
}

/// <summary>Select cage components and move them with a three-axis manipulator (or freely in the screen plane).</summary>
public sealed class FormEditTool(EditFormCommand cmd) : IViewportTool
{
    enum Drag { None, Axis, Free, Box, Lasso, Rotate, ScaleAxis, ScaleAll, Plane, ScalePlane }
    readonly List<(double x, double y)> _lasso = [];
    Drag _drag; int _axis;
    Vec3 _c0, _hit0; double _t0;
    Vec3[] _baseAll = [];
    double _ang0, _ang, _d0; Plane3 _ringPlane;
    Plane3 _plane;
    Dictionary<int, Vec3> _base = [];
    double _bx0, _by0, _bx1, _by1;
    int _hotV = -1, _hotF = -1, _hotAxis = -1; long _hotE = -1; bool _hotCenter;
    public GizmoBinding Binding {get;}=new();
    Vec3[] Axes=>_drag!=Drag.None?_dragAxes:Binding.Axes;
    Vec3[] _dragAxes=GizmoVisual.WorldAxes;
    Plane3 _bindingStart;
    int _hotMode = -1;

    public string Prompt => cmd.LoopArmed
        ? "Insert edge loop: click an edge to add a loop across it (count and position are in the panel). Esc or choosing another tool ends it."
        : "Click to select; drag the gizmo to move, rotate (R) or scale (S); T goes back to move, E extrudes. Drag a colored square to move or scale two axes at once, or hold Ctrl while dragging an arrow to move the plane across it. Drag on empty space to box-select (hold Ctrl to draw a lasso). Shift adds to the selection.";
    public bool Dragging => _drag != Drag.None;

    List<(Vec3 a, Vec3 b)> _loopLines = [];

    public void ResetLoopPreview() { _hotE = -1; _loopLines = []; cmd.S?.Invalidate(); }

    // The preview is the loop itself, built on a scratch copy so it always matches what a click would do.
    void UpdateLoopPreview()
    {
        _loopLines = [];
        if (_hotE < 0 || cmd.Work == null) return;
        var trial = cmd.Work.Clone(); var (a, b) = SubdMesh.Unkey(_hotE);
        var added = FormOps.InsertEdgeLoops(trial, a, b, cmd.LoopCount, cmd.LoopPosition).ToHashSet();
        foreach (var (x, y) in FormOverlay.Edges(trial)) if (added.Contains(x) && added.Contains(y)) _loopLines.Add((trial.V[x], trial.V[y]));
    }

    public void SelectionChanged() { cmd.SelectionNotify(); cmd.S?.Invalidate(); }

    int Mode => cmd.TransformMode.Index;     // 0 move, 1 rotate, 2 scale

    (Vec3 c, double len)? Gizmo(CadSession s, int? operation = null)
    {
        if (cmd.Work == null || !Binding.Positioning&&!cmd.MovingVertices().Any()) return null;
        Binding.CheckTargets(cmd.MovingVertices());
        var c = Binding.Center((operation ?? Mode) == 0 ? cmd.SelectionCenter() : cmd.PivotPoint());
        return (c, GizmoVisual.Length(s,c));
    }

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

    (int axis, bool center, int operation) HitGizmo(CadSession s, double x, double y)
    {
        if (Gizmo(s) is not { } g) return (-1, false, Mode);
        return GizmoVisual.Hit(s, x, y, operation => Gizmo(s,operation)!.Value.c, Axes, Mode, planes: true);
    }

    // ---- component picking
    int PickVertex(CadSession s, double x, double y) => cmd.Work == null ? -1 : Picking.NearestPoint(s.Camera, cmd.Work.V, x, y, 12);

    long PickEdge(CadSession s, double x, double y)
    {
        if (cmd.Work == null) return -1;
        long best = -1; double bd = 9;
        foreach (var (a, b) in FormOverlay.Edges(cmd.Work))
        {
            double d = ToSeg(s, x, y, cmd.Work.V[a], cmd.Work.V[b]);
            if (d < bd) { bd = d; best = SubdMesh.Key(a, b); }
        }
        return best;
    }

    int PickFace(CadSession s, double x, double y)
    {
        var mesh = s.Preview.FirstOrDefault()?.Mesh;
        if (mesh == null) return -1;
        var h = Picking.RayMesh(s.Camera, mesh, x, y);
        return h is { } r ? mesh.TriFace[r.tri] : -1;
    }

    public bool PointerDown(CadSession s, PointerInfo p)
    {
        if(cmd.HasPanelStep)return true;
        if (cmd.Work == null || !p.Left) return false;
        if (cmd.LoopArmed)
        {
            if (p.Clicks < 2 && PickEdge(s, p.X, p.Y) is var loopEdge && loopEdge >= 0) cmd.InsertLoopAt(loopEdge);
            return true;
        }
        if(p.Clicks>=2)
        {
            long seed=cmd.SelMode switch {FormMode.Vertex=>PickVertex(s,p.X,p.Y),FormMode.Edge=>PickEdge(s,p.X,p.Y),FormMode.Face=>PickFace(s,p.X,p.Y),_=>-1};
            if(seed>=0){cmd.SelectComponentLoop(seed,p.Shift,p.X,p.Y);return Selected();}
        }
        var (axis, center, operation) = HitGizmo(s, p.X, p.Y);
        axis = GizmoVisual.AsPlane(axis, operation, p.Ctrl);
        if (axis >= 0 || center)
        {
            _hotMode = operation; _hotAxis = axis; _hotCenter = center; s.Invalidate();
            if (Mode != operation) cmd.ForceMode(operation);
            var vs = cmd.MovingVertices().ToList();
            if(!Binding.Positioning)cmd.PushUndo();
            _bindingStart=Binding.Frame??new Plane3(cmd.SelectionCenter(),Vec3.UnitX,Vec3.UnitY);
            _dragAxes=Binding.Axes;
            _base = vs.ToDictionary(v => v, v => cmd.Work.V[v]);
            _baseAll = cmd.Work.V.ToArray();
            _c0 = Gizmo(s)!.Value.c;
            var (o, d) = s.Camera.Ray(p.X, p.Y);
            if (Mode == 1)
            {
                _drag = Drag.Rotate; _axis = axis;
                var (ru, rv) = RingBasis(axis); _ringPlane = new Plane3(_c0, ru, rv);
                var h0 = _ringPlane.Intersect(o, d) ?? _c0 + ru;
                _ang0 = Math.Atan2(Vec3.Dot(h0 - _c0, rv), Vec3.Dot(h0 - _c0, ru)); _ang = 0;
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
                    var (pu, pv) = GizmoVisual.PlaneAxes(Axes, axis); _plane = new Plane3(_c0, pu, pv);
                    _hit0 = _plane.Intersect(o, d) ?? _c0 + pu; _d0 = Math.Max(1e-6, (_hit0 - _c0).Length);
                }
                else
                {
                    _drag = Drag.ScaleAxis; _axis = axis;
                    var tt = AxisParam(o, d, _c0, Axes[axis]) ?? 0; _t0 = Math.Abs(tt) < 1e-6 ? 1e-6 : tt;
                }
            }
            else if (axis >= GizmoVisual.PlaneBase)
            {
                _drag = Drag.Plane; _axis = axis;
                var (pu, pv) = GizmoVisual.PlaneAxes(Axes, axis); _plane = new Plane3(_c0, pu, pv);
                _hit0 = _plane.Intersect(o, d) ?? _c0;
            }
            else if (center)
            {
                _drag = Drag.Free; _plane = new Plane3(_c0, s.Camera.Right, s.Camera.Up);
                _hit0 = _plane.Intersect(o, d) ?? _c0;
            }
            else { _drag = Drag.Axis; _axis = axis; _t0 = AxisParam(o, d, _c0, Axes[axis]) ?? 0; }
            return true;
        }

        var mode = cmd.SelMode;
        if (mode == FormMode.Object){cmd.SelectWholeObject();return Selected();}
        if (mode == FormMode.Vertex && PickVertex(s, p.X, p.Y) is var v && v >= 0) {Toggle(cmd.Verts, v, p.Shift); return Selected(); }
        if (mode == FormMode.Edge && PickEdge(s, p.X, p.Y) is var e && e >= 0) {Toggle(cmd.Edges, e, p.Shift); return Selected(); }
        if (mode == FormMode.Face && PickFace(s, p.X, p.Y) is var f && f >= 0) {Toggle(cmd.Faces, f, p.Shift); return Selected(); }

        // empty space: start a box selection
        _drag = p.Ctrl ? Drag.Lasso : Drag.Box; _bx0 = _bx1 = p.X; _by0 = _by1 = p.Y;
        _lasso.Clear(); if (p.Ctrl) _lasso.Add((p.X, p.Y));
        if (!p.Shift) cmd.ClearSelection();
        return true;
    }

    bool Selected() { SelectionChanged(); return true; }

    void Toggle<T>(HashSet<T> set, T item, bool additive)
    {
        if (!additive) { cmd.Verts.Clear(); cmd.Edges.Clear(); cmd.Faces.Clear(); set.Add(item); }
        else if (!set.Remove(item)) set.Add(item);
    }

    static double? AxisParam(Vec3 o, Vec3 d, Vec3 c, Vec3 a)
    {
        var w = c - o; double b = Vec3.Dot(a, d), dd = Vec3.Dot(a, w), ee = Vec3.Dot(d, w), den = 1 - b * b;
        if (den < 1e-6) return null;
        return (b * ee - dd) / den;
    }

    public void PointerMove(CadSession s, PointerInfo p)
    {
        if (cmd.Work == null) return;
        var (o, d) = s.Camera.Ray(p.X, p.Y);
        switch (_drag)
        {
            case Drag.Axis:
                if (AxisParam(o, d, _c0, Axes[_axis]) is double t) Move(Axes[_axis] * (t - _t0), p.Alt);
                return;
            case Drag.Free:
                if (_plane.Intersect(o, d) is { } h) Move(h - _hit0, p.Alt);
                return;
            case Drag.Plane:
                if (_plane.Intersect(o, d) is { } ph) { Move(ph - _hit0, p.Alt); s.Say($"Move in the {GizmoVisual.PlaneName(_axis)} plane"); }
                return;
            case Drag.ScalePlane:
            {
                if (_plane.Intersect(o, d) is not { } sh) return;
                double fp = GizmoVisual.SnapScale(Math.Clamp((sh - _c0).Length / _d0, 0.02, 50), p.Alt);
                int k = _axis - GizmoVisual.PlaneBase;
                Transform(GizmoBinding.Scale(_c0, Axes, k == 0 ? 1 : fp, k == 1 ? 1 : fp, k == 2 ? 1 : fp));
                s.Say($"Scale {fp:0.###} in the {GizmoVisual.PlaneName(_axis)} plane");
                return;
            }
            case Drag.Rotate:
            {
                if (_ringPlane.Intersect(o, d) is not { } rh) return;
                var (ru, rv) = RingBasis(_axis);
                double a = Math.Atan2(Vec3.Dot(rh - _c0, rv), Vec3.Dot(rh - _c0, ru));
                double delta = a - _ang0; while (delta > Math.PI) delta -= 2 * Math.PI; while (delta < -Math.PI) delta += 2 * Math.PI;
                delta = GizmoVisual.SnapAngle(delta, p.Alt); _ang = delta;
                Transform(FormTransform.RotateAbout(_c0, Axes[_axis], delta));
                s.Say($"Rotate {delta * 180 / Math.PI:0.#}° about {(_axis == 0 ? "X" : _axis == 1 ? "Y" : "Z")}");
                return;
            }
            case Drag.ScaleAxis:
            {
                if (AxisParam(o, d, _c0, Axes[_axis]) is not double ts) return;
                double f = GizmoVisual.SnapScale(Math.Clamp(ts / _t0, 0.02, 50), p.Alt);
                Transform(GizmoBinding.Scale(_c0,Axes, _axis == 0 ? f : 1, _axis == 1 ? f : 1, _axis == 2 ? f : 1));
                s.Say($"Scale {f:0.###} along {(_axis == 0 ? "X" : _axis == 1 ? "Y" : "Z")}");
                return;
            }
            case Drag.ScaleAll:
            {
                var pc = s.Camera.Project(_c0); if (pc == null) return;
                double dist = Math.Max(4, Math.Sqrt((pc.Value.x - p.X) * (pc.Value.x - p.X) + (pc.Value.y - p.Y) * (pc.Value.y - p.Y)));
                double f = GizmoVisual.SnapScale(Math.Clamp(dist / _d0, 0.02, 50), p.Alt);
                Transform(FormTransform.ScaleAbout(_c0, f, f, f));
                s.Say($"Scale {f:0.###}");
                return;
            }
            case Drag.Box:
                _bx1 = p.X; _by1 = p.Y; s.Invalidate();
                return;
            case Drag.Lasso:
                if (_lasso.Count == 0 || Math.Abs(p.X - _lasso[^1].x) + Math.Abs(p.Y - _lasso[^1].y) > 3) _lasso.Add((p.X, p.Y));
                _bx1 = p.X; _by1 = p.Y; s.Invalidate();
                return;
        }
        // hover feedback
        if (cmd.LoopArmed)
        {
            long le = PickEdge(s, p.X, p.Y);
            if (le != _hotE) { _hotE = le; _hotV = _hotF = _hotAxis = -1; _hotCenter = false; UpdateLoopPreview(); s.Invalidate(); }
            return;
        }        var (axis, center, operation) = HitGizmo(s, p.X, p.Y);
        axis = GizmoVisual.AsPlane(axis, operation, p.Ctrl);
        int hv = -1, hf = -1; long he = -1;
        if (axis < 0 && !center)
            switch (cmd.SelMode)
            {
                case FormMode.Vertex: hv = PickVertex(s, p.X, p.Y); break;
                case FormMode.Edge: he = PickEdge(s, p.X, p.Y); break;
                case FormMode.Object: hf = PickFace(s, p.X, p.Y) >= 0 ? -2 : -1; break;
                default: hf = PickFace(s, p.X, p.Y); break;
            }
        if (operation != _hotMode || axis != _hotAxis || center != _hotCenter || hv != _hotV || he != _hotE || hf != _hotF)
        { _hotMode = operation; _hotAxis = axis; _hotCenter = center; _hotV = hv; _hotE = he; _hotF = hf; s.Invalidate(); }
    }

    void Move(Vec3 delta, bool bypass = false) => Transform(FormTransform.MoveBy(GizmoVisual.SnapMove(delta,bypass)));

    // every drag goes through the command so soft selection and live symmetry apply to move, rotate and scale alike
    void Transform(Func<Vec3, Vec3> xf)
    {
        if(Binding.Positioning){Binding.Transform(_bindingStart,xf);cmd.S.Invalidate();return;}
        cmd.ApplyXf(_baseAll, xf);
        if(Binding.Bound)Binding.Transform(_bindingStart,xf);
        if (cmd.StickToScan) cmd.SnapToScan(_base.Keys);
    }

    public void PointerUp(CadSession s, PointerInfo p)
    {
        if (_drag == Drag.Lasso && cmd.Work != null)
        {
            if (_lasso.Count > 2)
            {
                bool In(Vec3 q) { var c = s.Camera.Project(q); return c != null && FormSelect.InPolygon(_lasso, c.Value.x, c.Value.y); }
                var w = cmd.Work;
                switch (cmd.SelMode)
                {
                    case FormMode.Vertex: for (int v = 0; v < w.V.Count; v++) if (In(w.V[v])) cmd.Verts.Add(v); break;
                    case FormMode.Edge: foreach (var (a, b) in FormOverlay.Edges(w)) if (In((w.V[a] + w.V[b]) * 0.5)) cmd.Edges.Add(SubdMesh.Key(a, b)); break;
                    default: for (int f = 0; f < w.F.Count; f++) if (In(w.FaceCenter(f))) cmd.Faces.Add(f); break;
                }
            }
            _lasso.Clear(); SelectionChanged();
        }
        if (_drag == Drag.Box && cmd.Work != null)
        {
            double x0 = Math.Min(_bx0, _bx1), x1 = Math.Max(_bx0, _bx1), y0 = Math.Min(_by0, _by1), y1 = Math.Max(_by0, _by1);
            if (x1 - x0 > 4 || y1 - y0 > 4)
            {
                bool Inside(int v) { var q = s.Camera.Project(cmd.Work.V[v]); return q != null && q.Value.x >= x0 && q.Value.x <= x1 && q.Value.y >= y0 && q.Value.y <= y1; }
                switch (cmd.SelMode)
                {
                    case FormMode.Vertex: for (int v = 0; v < cmd.Work.V.Count; v++) if (Inside(v)) cmd.Verts.Add(v); break;
                    case FormMode.Edge: foreach (var (a, b) in FormOverlay.Edges(cmd.Work)) if (Inside(a) && Inside(b)) cmd.Edges.Add(SubdMesh.Key(a, b)); break;
                    default: for (int f = 0; f < cmd.Work.F.Count; f++) if (cmd.Work.F[f].All(Inside)) cmd.Faces.Add(f); break;
                }
            }
            SelectionChanged();
        }
        _drag = Drag.None;
        s.Invalidate();
    }

    public bool Key(CadSession s, string key)
    {
        if(Binding.Positioning&&key is "Escape" or "Enter")
        {if(key=="Escape")Binding.Cancel();else if(!Binding.Bind(cmd.MovingVertices()))s.Say("Select cage geometry before binding the gizmo.",true);s.RaiseState();s.Invalidate();return true;}
        if(cmd.HasPanelStep)return false;
        if (cmd.LoopArmed && key == "Escape") { cmd.DisarmLoop(); s.Say("Insert edge loop finished."); return true; }
        if (key == "Delete") { cmd.Params.OfType<ActionParam>().FirstOrDefault(p => p.Label == "Delete selection")?.Run(); return true; }
        if (key == "Char:T") { cmd.ForceMode(0); s.Say("Move gizmo."); return true; }
        if (key == "Char:R") { cmd.ForceMode(1); s.Say("Rotate gizmo: drag a ring."); return true; }
        if (key == "Char:S") { cmd.ForceMode(2); s.Say("Scale gizmo: drag an axis handle, or the diagonal square for uniform scale."); return true; }
        if (key == "Escape" && (cmd.ObjectSelected || cmd.Verts.Count + cmd.Edges.Count + cmd.Faces.Count > 0)) { cmd.ClearSelection(); return true; }
        return false;
    }

    public void BuildOverlay(CadSession s, Overlay o)
    {
        var m = cmd.Work; if (m == null) return;
        FormOverlay.Cage(o, m, vertices: cmd.SelMode == FormMode.Vertex);
        if (m.Frozen.Count > 0) for (int f = 0; f < m.F.Count; f++) if (m.FaceFrozen(f)) AddFaceTris(o, m, f, 0x5580A8FF);

        // hover + selection highlights
        if (_hotE >= 0) { var (a, b) = SubdMesh.Unkey(_hotE); o.AddLine(m.V[a], m.V[b], 0xFF35B6FF, 4f); }
        foreach (var (a, b) in _loopLines) o.AddLine(a, b, 0xFF3DDC84, 4f);
        if (_hotV >= 0 && _hotV < m.V.Count) o.AddDot(m.V[_hotV], 0xFF35B6FF, 12);
        if (cmd.ObjectSelected) for (int f = 0; f < m.F.Count; f++) AddFaceTris(o, m, f, 0x44FF8A1F);
        if (_hotF == -2) { for (int f = 0; f < m.F.Count; f++) AddFaceTris(o, m, f, 0x5535B6FF); }
        else if (_hotF >= 0 && _hotF < m.F.Count) AddFaceTris(o, m, _hotF, 0x5535B6FF);
        foreach (var f in cmd.Faces.Where(f => f < m.F.Count))
        {
            AddFaceTris(o, m, f, 0x66FF8A1F);
            var fc = m.F[f]; for (int i = 0; i < fc.Length; i++) o.AddLine(m.V[fc[i]], m.V[fc[(i + 1) % fc.Length]], 0xFFFF8A1F, 2.6f);
        }
        foreach (var k in cmd.Edges) { var (a, b) = SubdMesh.Unkey(k); if (a < m.V.Count && b < m.V.Count) o.AddLine(m.V[a], m.V[b], 0xFFFF8A1F, 4.2f); }
        foreach (var v in cmd.MovingVertices()) o.AddDot(m.V[v], 0xFFFF8A1F, 10);
        DrawSymmetry(s, o, m);
        DrawSoft(o, m);
        if (cmd.ShowPoles) FormPoleOverlay.Draw(o, m, FormQuality.Analyze(m, 1));

        // Every editor uses the same arrowheads, rotation rings, scale blocks and screen sizing.
        for (int operation = 0; !cmd.HasPanelStep && !cmd.LoopArmed && operation < 3; operation++)
            if (Gizmo(s, operation) is { } g)
                GizmoVisual.DrawOperation(o, s, g.c, Axes, operation, Mode,
                    _hotMode == operation ? _hotAxis : -1, _hotMode == operation && _hotCenter, planes: true);
        if (_drag == Drag.Box) o.AddRect(_bx0, _by0, _bx1, _by1);
        // the lasso is shown as a trail of small boxes along the path (the overlay has no 2D polyline)
        if (_drag == Drag.Lasso) foreach (var (x, y) in _lasso) o.AddRect(x - 1.5, y - 1.5, x + 1.5, y + 1.5);
    }

    /// <summary>The mirror plane (or spokes) of live symmetry, and the mirror images of the selected points.</summary>
    void DrawSymmetry(CadSession s, Overlay o, SubdMesh m)
    {
        var sym = cmd.Sym; if (!sym.Active || m.V.Count == 0) return;
        var lo = m.V.Aggregate((a, b) => new Vec3(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z)));
        var hi = m.V.Aggregate((a, b) => new Vec3(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z)));
        double tol = Math.Max((hi - lo).Length * 1e-4, 1e-4); const uint col = 0xFFFFC53D; var og = sym.Origin;
        // the seam is drawn as colored cage edges instead of a plane, so it stays out of the way
        bool OnSeam(Vec3 p) => sym.Mode switch
        {
            SymMode.MirrorX => Math.Abs(p.X - og.X) < tol,
            SymMode.MirrorY => Math.Abs(p.Y - og.Y) < tol,
            SymMode.MirrorZ => Math.Abs(p.Z - og.Z) < tol,
            _ => Math.Abs(p.X - og.X) < tol && Math.Abs(p.Y - og.Y) < tol,
        };
        foreach (var (ea, eb) in m.Edges()) if (OnSeam(m.V[ea]) && OnSeam(m.V[eb])) o.AddLine(m.V[ea], m.V[eb], col, 4.5f);
        var partners = cmd.Partners(); if (partners == null) return;
        foreach (var v in cmd.MovingVertices()) if (v < partners.Length) foreach (var (p, _) in partners[v]) if (p != v) o.AddDot(m.V[p], 0xFF6FD3FF, 8);
    }

    /// <summary>With soft selection on, show how strongly each nearby point will follow.</summary>
    void DrawSoft(Overlay o, SubdMesh m)
    {
        if (cmd.SoftRadius <= 0) return;
        var sel = cmd.MovingVertices().ToHashSet(); if (sel.Count == 0) return;
        var w = FormTransform.Weights(m, m.V.ToArray(), sel, cmd.SoftRadius, cmd.SoftFalloff);
        for (int i = 0; i < w.Length; i++)
            if (w[i] > 0.02 && w[i] < 1 && !sel.Contains(i))
            {
                byte a = (byte)(80 + 175 * w[i]);
                o.AddDot(m.V[i], ((uint)a << 24) | 0x00FFC53D, (float)(6 + 7 * w[i]));
            }
    }

    static void AddFaceTris(Overlay o, SubdMesh m, int f, uint color)
    {
        var fc = m.F[f]; var c = m.FaceCenter(f);
        for (int i = 0; i < fc.Length; i++) o.AddTri(c, m.V[fc[i]], m.V[fc[(i + 1) % fc.Length]], color);
    }

    public void Deactivate(CadSession s) { }
}

/// <summary>Click to lay down the corners of a new face; snaps to existing cage vertices so faces connect.</summary>
public sealed class CreateFaceTool(CreateFaceCommand cmd) : IViewportTool
{
    readonly List<(int existing, Vec3 pos)> _pts = [];
    Vec3? _hot; int _hotVert = -1;
    public string Prompt => "Click to place face corners (click an existing corner to connect). Enter or click the first point to close the face.";

    (int existing, Vec3 pos)? Snap(CadSession s, double x, double y)
    {
        int v = Picking.NearestPoint(s.Camera, cmd.Work.V, x, y, 12);
        if (v >= 0) return (v, cmd.Work.V[v]);
        var (o, d) = s.Camera.Ray(x, y);
        if (cmd.SnapToScan && ScanSurface.Raycast(s, o, d) is { } sh) return (-1, sh.point + sh.normal * cmd.ScanOffset);
        var pl = cmd.WorkPlane ?? Plane3.XY;
        var hit = pl.Intersect(o, d);
        if (hit == null) return null;
        double g = GridMath.Spacing(s.Camera);
        var (u, w) = pl.Project(hit.Value);
        return (-1, pl.PointAt(Math.Round(u / g) * g, Math.Round(w / g) * g));
    }

    void Finish(CadSession s)
    {
        if (_pts.Count >= 3)
        {
            // distinct corners only
            var pts = new List<(int, Vec3)>();
            foreach (var p in _pts) if (!pts.Any(q => (q.Item1 >= 0 && q.Item1 == p.existing) || q.Item2.DistanceTo(p.pos) < 1e-6)) pts.Add(p);
            if (pts.Count >= 3) cmd.AddPolygon(pts, s.Camera.EyeDirection);
        }
        _pts.Clear(); s.Invalidate();
    }

    public bool PointerDown(CadSession s, PointerInfo p)
    {
        if (!p.Left) return false;
        if (Snap(s, p.X, p.Y) is not { } sn) return true;
        if (_pts.Count >= 3 && (sn.existing >= 0 ? sn.existing == _pts[0].existing : sn.pos.DistanceTo(_pts[0].pos) < 1e-6)) { Finish(s); return true; }
        if (p.Clicks >= 2 && _pts.Count >= 3) { Finish(s); return true; }
        if (_pts.Count > 0 && (sn.existing >= 0 ? sn.existing == _pts[^1].existing : sn.pos.DistanceTo(_pts[^1].pos) < 1e-6)) return true;
        _pts.Add(sn);
        s.Invalidate();
        return true;
    }

    public void PointerMove(CadSession s, PointerInfo p)
    {
        var sn = Snap(s, p.X, p.Y);
        _hot = sn?.pos; _hotVert = sn?.existing ?? -1;
        if (_hot is { } h) s.Cursor3 = h;
        s.Invalidate();
    }

    public void PointerUp(CadSession s, PointerInfo p) { }

    public bool Key(CadSession s, string key)
    {
        switch (key)
        {
            case "Enter": if (_pts.Count >= 3) { Finish(s); return true; } return false;
            case "RightClick": if (_pts.Count >= 3) Finish(s); else _pts.Clear(); s.Invalidate(); return true;
            case "Escape": if (_pts.Count > 0) { _pts.Clear(); s.Invalidate(); return true; } return false;
            case "Back": if (_pts.Count > 0) { _pts.RemoveAt(_pts.Count - 1); s.Invalidate(); return true; } return false;
        }
        return false;
    }

    public void BuildOverlay(CadSession s, Overlay o)
    {
        FormOverlay.Cage(o, cmd.Work);
        for (int i = 0; i + 1 < _pts.Count; i++) o.AddLine(_pts[i].pos, _pts[i + 1].pos, 0xFF1F6FEB, 2.4f);
        if (_pts.Count > 0 && _hot is { } h) o.AddLine(_pts[^1].pos, h, 0xFF7AA7E8, 2f);
        if (_pts.Count >= 3 && _hot is { } h2) o.AddLine(h2, _pts[0].pos, 0x807AA7E8, 1.5f);
        for (int i = 0; i < _pts.Count; i++) o.AddDot(_pts[i].pos, i == 0 ? 0xFFFF8A1F : 0xFF1F6FEB, i == 0 ? 11 : 8);
        if (_hot is { } hh) o.AddDot(hh, _hotVert >= 0 ? 0xFF35B6FF : 0xFF1F6FEB, _hotVert >= 0 ? 12 : 7);
    }

    public void Deactivate(CadSession s) { _pts.Clear(); }
}
