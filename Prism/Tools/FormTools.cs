using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core;
using Prism.Core.Form;
using Prism.Kernel;

namespace Prism.Commands;

static class FormOverlay
{
    const uint CageColor = 0xFF6F9BFF, CreaseColor = 0xFFE0307A, VertColor = 0xFFBFD4F5;
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
            o.AddLine(m.V[a], m.V[b], crease ? CreaseColor : CageColor, crease ? 2.6f : 1.4f);
        }
        if (vertices) foreach (var v in m.V) o.AddDot(v, VertColor, 6);
    }
}

/// <summary>Select cage components and move them with a three-axis manipulator (or freely in the screen plane).</summary>
public sealed class FormEditTool(EditFormCommand cmd) : IViewportTool
{
    enum Drag { None, Axis, Free, Box, Lasso, Rotate, ScaleAxis, ScaleAll }
    readonly List<(double x, double y)> _lasso = [];
    Drag _drag; int _axis;
    Vec3 _c0, _hit0; double _t0;
    Vec3[] _baseAll = [];
    double _ang0, _ang, _d0; Plane3 _ringPlane;
    Plane3 _plane;
    Dictionary<int, Vec3> _base = [];
    double _bx0, _by0, _bx1, _by1;
    int _hotV = -1, _hotF = -1, _hotAxis = -1; long _hotE = -1; bool _hotCenter;
    static readonly Vec3[] Axes = [Vec3.UnitX, Vec3.UnitY, Vec3.UnitZ];
    static readonly uint[] AxisColors = [0xFFE5484D, 0xFF2EA043, 0xFF2F78E4];

    public string Prompt => "Click to select; drag the gizmo to move, rotate (E) or scale (R); W goes back to move. Drag on empty space to box-select (hold Ctrl to draw a lasso). Shift adds to the selection.";
    public bool Dragging => _drag != Drag.None;

    public void SelectionChanged() { cmd.SelectionNotify(); cmd.S?.Invalidate(); }

    int Mode => cmd.TransformMode.Index;     // 0 move, 1 rotate, 2 scale

    (Vec3 c, double len)? Gizmo(CadSession s)
    {
        if (cmd.Work == null || !cmd.MovingVertices().Any()) return null;
        var c = Mode == 0 ? cmd.SelectionCenter() : cmd.PivotPoint();
        return (c, s.Camera.PixelSizeAt(c) * 95);
    }

    static (Vec3 u, Vec3 v) RingBasis(int axis) => axis == 0 ? (Vec3.UnitY, Vec3.UnitZ) : axis == 1 ? (Vec3.UnitZ, Vec3.UnitX) : (Vec3.UnitX, Vec3.UnitY);

    static IEnumerable<Vec3> RingPoints(Vec3 c, double r, int axis)
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

    (int axis, bool center) HitGizmo(CadSession s, double x, double y)
    {
        if (Gizmo(s) is not { } g) return (-1, false);
        if (Mode == 1)
        {
            int bestRing = -1; double bestD = 8;
            for (int i = 0; i < 3; i++)
            {
                var pts = RingPoints(g.c, g.len, i).ToList();
                for (int k = 0; k + 1 < pts.Count; k++) { double dd = ToSeg(s, x, y, pts[k], pts[k + 1]); if (dd < bestD) { bestD = dd; bestRing = i; } }
            }
            return (bestRing, false);
        }
        var pc = s.Camera.Project(g.c);
        if (pc != null && Math.Sqrt((pc.Value.x - x) * (pc.Value.x - x) + (pc.Value.y - y) * (pc.Value.y - y)) <= 11) return (-1, true);
        int best = -1; double bd = 9;
        for (int i = 0; i < 3; i++)
        {
            double d = ToSeg(s, x, y, g.c + Axes[i] * (g.len * 0.18), g.c + Axes[i] * g.len);
            if (d < bd) { bd = d; best = i; }
        }
        return (best, false);
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
        if (cmd.Work == null || !p.Left) return false;
        var (axis, center) = HitGizmo(s, p.X, p.Y);
        if (axis >= 0 || center)
        {
            var vs = cmd.MovingVertices().ToList();
            cmd.PushUndo();
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
                else
                {
                    _drag = Drag.ScaleAxis; _axis = axis;
                    var tt = AxisParam(o, d, _c0, Axes[axis]) ?? 0; _t0 = Math.Abs(tt) < 1e-6 ? 1e-6 : tt;
                }
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
        if (mode == FormMode.Vertex && PickVertex(s, p.X, p.Y) is var v && v >= 0) { Toggle(cmd.Verts, v, p.Shift); return Selected(); }
        if (mode == FormMode.Edge && PickEdge(s, p.X, p.Y) is var e && e >= 0) { Toggle(cmd.Edges, e, p.Shift); return Selected(); }
        if (mode == FormMode.Face && PickFace(s, p.X, p.Y) is var f && f >= 0) { Toggle(cmd.Faces, f, p.Shift); return Selected(); }

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
                if (AxisParam(o, d, _c0, Axes[_axis]) is double t) Move(Axes[_axis] * (t - _t0));
                return;
            case Drag.Free:
                if (_plane.Intersect(o, d) is { } h) Move(h - _hit0);
                return;
            case Drag.Rotate:
            {
                if (_ringPlane.Intersect(o, d) is not { } rh) return;
                var (ru, rv) = RingBasis(_axis);
                double a = Math.Atan2(Vec3.Dot(rh - _c0, rv), Vec3.Dot(rh - _c0, ru));
                double delta = a - _ang0; while (delta > Math.PI) delta -= 2 * Math.PI; while (delta < -Math.PI) delta += 2 * Math.PI;
                _ang = delta;
                Transform(FormTransform.RotateAbout(_c0, Axes[_axis], delta));
                s.Say($"Rotate {delta * 180 / Math.PI:0.#}° about {(_axis == 0 ? "X" : _axis == 1 ? "Y" : "Z")}");
                return;
            }
            case Drag.ScaleAxis:
            {
                if (AxisParam(o, d, _c0, Axes[_axis]) is not double ts) return;
                double f = Math.Clamp(ts / _t0, 0.02, 50);
                Transform(FormTransform.ScaleAbout(_c0, _axis == 0 ? f : 1, _axis == 1 ? f : 1, _axis == 2 ? f : 1));
                s.Say($"Scale {f:0.###} along {(_axis == 0 ? "X" : _axis == 1 ? "Y" : "Z")}");
                return;
            }
            case Drag.ScaleAll:
            {
                var pc = s.Camera.Project(_c0); if (pc == null) return;
                double dist = Math.Max(4, Math.Sqrt((pc.Value.x - p.X) * (pc.Value.x - p.X) + (pc.Value.y - p.Y) * (pc.Value.y - p.Y)));
                double f = Math.Clamp(dist / _d0, 0.02, 50);
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
        var (axis, center) = HitGizmo(s, p.X, p.Y);
        int hv = -1, hf = -1; long he = -1;
        if (axis < 0 && !center)
            switch (cmd.SelMode)
            {
                case FormMode.Vertex: hv = PickVertex(s, p.X, p.Y); break;
                case FormMode.Edge: he = PickEdge(s, p.X, p.Y); break;
                default: hf = PickFace(s, p.X, p.Y); break;
            }
        if (axis != _hotAxis || center != _hotCenter || hv != _hotV || he != _hotE || hf != _hotF)
        { _hotAxis = axis; _hotCenter = center; _hotV = hv; _hotE = he; _hotF = hf; s.Invalidate(); }
    }

    void Move(Vec3 delta) => Transform(FormTransform.MoveBy(delta));

    // every drag goes through the command so soft selection and live symmetry apply to move, rotate and scale alike
    void Transform(Func<Vec3, Vec3> xf)
    {
        cmd.ApplyXf(_baseAll, xf);
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
        if (key == "Delete") { cmd.Params.OfType<ActionParam>().FirstOrDefault(p => p.Label == "Delete selection")?.Run(); return true; }
        if (key == "Char:W") { cmd.ForceMode(0); s.Say("Move gizmo."); return true; }
        if (key == "Char:E") { cmd.ForceMode(1); s.Say("Rotate gizmo: drag a ring."); return true; }
        if (key == "Char:R") { cmd.ForceMode(2); s.Say("Scale gizmo: drag an axis handle, or the centre for uniform scale."); return true; }
        if (key == "Escape" && (cmd.Verts.Count + cmd.Edges.Count + cmd.Faces.Count) > 0) { cmd.ClearSelection(); return true; }
        return false;
    }

    public void BuildOverlay(CadSession s, Overlay o)
    {
        var m = cmd.Work; if (m == null) return;
        FormOverlay.Cage(o, m, vertices: cmd.SelMode == FormMode.Vertex || true);
        if (m.Frozen.Count > 0) for (int f = 0; f < m.F.Count; f++) if (m.FaceFrozen(f)) AddFaceTris(o, m, f, 0x5580A8FF);

        // hover + selection highlights
        if (_hotE >= 0) { var (a, b) = SubdMesh.Unkey(_hotE); o.AddLine(m.V[a], m.V[b], 0xFF35B6FF, 4f); }
        if (_hotV >= 0 && _hotV < m.V.Count) o.AddDot(m.V[_hotV], 0xFF35B6FF, 12);
        if (_hotF >= 0 && _hotF < m.F.Count) AddFaceTris(o, m, _hotF, 0x5535B6FF);
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

        // manipulator
        if (Gizmo(s) is { } g)
        {
            if (Mode == 1)
            {
                for (int i = 0; i < 3; i++)
                {
                    bool hot = _hotAxis == i || (_drag == Drag.Rotate && _axis == i);
                    o.AddPolyline(RingPoints(g.c, g.len, i).ToList(), hot ? 0xFFFFC53D : AxisColors[i], hot ? 4.5f : 3f);
                }
                o.AddDot(g.c, 0xFFFFFFFF, 9);
            }
            else
            {
                for (int i = 0; i < 3; i++)
                {
                    bool hot = _hotAxis == i || ((_drag == Drag.Axis || _drag == Drag.ScaleAxis) && _axis == i);
                    var tip = g.c + Axes[i] * g.len;
                    o.AddLine(g.c, tip, hot ? 0xFFFFC53D : AxisColors[i], hot ? 4.5f : 3f);
                    o.AddDot(tip, hot ? 0xFFFFC53D : AxisColors[i], Mode == 2 ? 17 : 13);        // scale handles are bigger "blocks"
                }
                o.AddDot(g.c, _hotCenter || _drag == Drag.Free || _drag == Drag.ScaleAll ? 0xFFFFC53D : 0xFFFFFFFF, Mode == 2 ? 16 : 12);
            }
        }
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
        double r = Math.Max((hi - lo).Length * 0.7, 10); const uint col = 0x90FFC53D;
        switch (sym.Mode)
        {
            case SymMode.MirrorX: o.AddPolyline([new(0, -r, -r), new(0, r, -r), new(0, r, r), new(0, -r, r)], col, 1.4f, true); o.AddLine(new(0, -r, 0), new(0, r, 0), col, 1f); o.AddLine(new(0, 0, -r), new(0, 0, r), col, 1f); break;
            case SymMode.MirrorY: o.AddPolyline([new(-r, 0, -r), new(r, 0, -r), new(r, 0, r), new(-r, 0, r)], col, 1.4f, true); o.AddLine(new(-r, 0, 0), new(r, 0, 0), col, 1f); o.AddLine(new(0, 0, -r), new(0, 0, r), col, 1f); break;
            case SymMode.MirrorZ: o.AddPolyline([new(-r, -r, 0), new(r, -r, 0), new(r, r, 0), new(-r, r, 0)], col, 1.4f, true); o.AddLine(new(-r, 0, 0), new(r, 0, 0), col, 1f); o.AddLine(new(0, -r, 0), new(0, r, 0), col, 1f); break;
            case SymMode.Radial:
                for (int k = 0; k < sym.Count; k++) { double a = 2 * Math.PI * k / sym.Count; o.AddLine(Vec3.Zero, new Vec3(Math.Cos(a) * r, Math.Sin(a) * r, 0), col, 1.2f); }
                o.AddLine(new(0, 0, -r), new(0, 0, r), col, 1f);
                break;
        }
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
