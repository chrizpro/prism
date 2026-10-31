using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core.Sketching;

namespace Prism.Core;

// =============================================================== Select / drag
/// <summary>Select sketch geometry and drag it; the solver keeps every constraint satisfied while you drag.</summary>
public sealed class SelectTool(bool selectionOnly = false) : SketchTool
{
    enum Drag { None, Point, Entity, Radius }
    Drag _drag; bool _down, _started;
    int _dragPoint = -1, _dragEnt = -1;
    double _px0, _py0, _u0, _v0;
    readonly Dictionary<int, (double u, double v)> _start = [];
    SketchModel? _snapshot;
    bool _geometryTranslation;
    Prism.Kernel.Plane3 _dragPlane;
    bool _lasso = Recall("Select") == 1;
    bool _region; double _rx0, _ry0, _rx1, _ry1; readonly List<(double x, double y)> _path = [];
    static readonly ToolOption[] SelOpts = [new("Box", "Drag a box: left to right selects what is fully inside, right to left selects whatever it touches", "rect"), new("Lasso", "Draw around what you want to select (anything the loop touches)", "spline")];
    public override IReadOnlyList<ToolOption> Options => SelOpts;
    public override int OptionIndex => _lasso ? 1 : 0;
    protected override void ApplyOption(int index) => _lasso = index == 1;
    /// <summary>Box or lasso selection, set by a tool that wraps this one (Edit Geometry).</summary>
    public void UseLasso(bool lasso) => _lasso = lasso;

    bool InRegion(double x, double y, bool window)
    {
        if (!_lasso) return x >= Math.Min(_rx0, _rx1) && x <= Math.Max(_rx0, _rx1) && y >= Math.Min(_ry0, _ry1) && y <= Math.Max(_ry0, _ry1);
        bool c = false;
        for (int i = 0, j = _path.Count - 1; i < _path.Count; j = i++)
            if ((_path[i].y > y) != (_path[j].y > y) && x < (_path[j].x - _path[i].x) * (y - _path[i].y) / (_path[j].y - _path[i].y) + _path[i].x) c = !c;
        return c;
    }

    void FinishRegion(CadSession s)
    {
        _region = false;
        var sk = Sk(s);
        if (Math.Abs(_rx1 - _rx0) + Math.Abs(_ry1 - _ry0) < 4) { s.NotifySketchChanged(); s.Invalidate(); return; }
        bool window = !_lasso && _rx1 >= _rx0;
        var cam = s.Camera; int n = 0;
        foreach (var (e, pts) in sk.Model.Polylines(sk.Plane))
        {
            if(!sk.EntityVisible(e))continue;
            var px = pts.Select(q => cam.Project(q)).ToList(); if (px.Any(q => q == null)) continue;
            bool any = false, all = true;
            for (int i = 0; i + 1 < px.Count; i++)
            {
                var a = px[i]!.Value; var b = px[i + 1]!.Value;
                int steps = Math.Max(1, (int)Math.Ceiling(Math.Sqrt((b.x - a.x) * (b.x - a.x) + (b.y - a.y) * (b.y - a.y)) / 5));
                for (int k = 0; k <= steps; k++)
                {
                    bool ins = InRegion(a.x + (b.x - a.x) * k / steps, a.y + (b.y - a.y) * k / steps, window);
                    any |= ins; all &= ins;
                }
            }
            if (window ? all : any) { sk.SelEntities.Add(e.Id); n++; }
        }
        foreach (var e in sk.Model.Entities.Where(e => e.Type == EntType.Point))
        {
            var q = cam.Project(sk.Plane.PointAt(sk.Model.P(e.A).X, sk.Model.P(e.A).Y));
            if (q != null && InRegion(q.Value.x, q.Value.y, window)) { sk.SelEntities.Add(e.Id); n++; }
        }
        s.Say(n == 0 ? "Nothing in that area." : $"{n} selected. Delete removes them; pick a constraint tool to apply it to them.");
        s.NotifySketchChanged(); s.Invalidate();
    }

    public override void BuildOverlay(CadSession s, Overlay o)
    {
        if (!_region) return;
        var sk = Sk(s);
        if (!_lasso) { o.AddRect(_rx0, _ry0, _rx1, _ry1); return; }
        var pts = _path.Select(q => sk.PlanePoint(s, q.x, q.y)).Where(q => q != null).Select(q => sk.Plane.PointAt(q!.Value.u, q!.Value.v)).ToList();
        o.AddPolyline(pts, 0xFF4C94FF, 1.6f, true);
    }

    /// <summary>Every curve joined to this one end to end (sharing or coinciding points), construction and normal kept apart.</summary>
    public static List<int> ConnectedShape(SketchModel m, int start)
    {
        var first = m.E(start);
        if(first.Group!=null)return m.Entities.Where(e=>e.Group==first.Group).Select(e=>e.Id).ToList();
        var seen = new HashSet<int> { start }; var queue = new Queue<int>([start]);
        while (queue.Count > 0)
        {
            var e = m.E(queue.Dequeue());
            var group = m.PointsOf(e).SelectMany(pid => m.CoincidentGroup(pid)).ToHashSet();
            foreach (var o in m.Entities)
            {
                if (seen.Contains(o.Id) || o.Construction != first.Construction || o.Type == EntType.Point) continue;
                if (m.PointsOf(o).Any(group.Contains) && (o.Type is EntType.Line or EntType.Arc or EntType.Spline)) { seen.Add(o.Id); queue.Enqueue(o.Id); }
            }
        }
        return seen.ToList();
    }

    public override string Name => "Select";
    public override void Deactivate(CadSession s)
    {
        if (_geometryTranslation) Sk(s).EndSelectionTranslation(s,cancel:true);
        _region=false;_down=false;_started=false;_geometryTranslation=false;_drag=Drag.None;_snapshot=null;
        base.Deactivate(s);
    }
    public override string Prompt => "Select: click geometry (Shift adds), drag points or curves to move them, Delete removes. Double-click a dimension to change its value. Double-click a curve to select the whole shape it belongs to (a rectangle's four sides). Click a constraint symbol to select it, then Delete removes it. Drag on empty space to box- or lasso-select.";

    public override bool PointerDown(CadSession s, PointerInfo p)
    {
        if (!p.Left) return false;
        var sk = Sk(s);
        int editPoint=(sk.GeometryEditing||!sk.Is3DEnabled)?sk.PickPoint(s,p.X,p.Y):-1;
        if (!selectionOnly && editPoint<0 && sk.PickDimension(s, p.X, p.Y) is { } dim)
        {
            if (p.Clicks >= 2) sk.EditDimension(s, dim);
            return true;
        }
        if (!selectionOnly && editPoint<0 && sk.PickGlyph(s, p.X, p.Y) is { } glyph) { sk.SelectConstraint(s, glyph); return true; }
        sk.SelConstraint = -1;
        int pid = editPoint>=0?editPoint:sk.PickPoint(s, p.X, p.Y);
        int eid = pid < 0 ? sk.PickEntity(s, p.X, p.Y) : -1;
        if (!selectionOnly && p.Clicks >= 2 && eid >= 0 && sk.Model.E(eid).Type == EntType.Spline)
        {
            sk.SelEntities.Clear(); sk.SelPoints.Clear(); sk.SelEntities.Add(eid);
            sk.SetTool(s, new SketchGeometryEditTool(1));
            return true;
        }
        if (p.Clicks >= 2 && eid >= 0)
        {
            // double-click: the whole shape this curve is joined to (all four sides of a rectangle, a whole outline...)
            if (!p.Shift) { sk.SelEntities.Clear(); sk.SelPoints.Clear(); }
            var chain = ConnectedShape(sk.Model, eid);
            foreach (var id in chain) sk.SelEntities.Add(id);
            s.Say($"Selected the whole shape ({chain.Count} curve{(chain.Count == 1 ? "" : "s")}). Delete removes all of it.");
            s.NotifySketchChanged(); s.Invalidate();
            return true;
        }
        bool keepSelection = !p.Shift && (pid >= 0 && sk.SelPoints.Count==1 && sk.SelEntities.Count==0 && sk.SelPoints.Contains(pid) || eid >= 0 && sk.SelEntities.Contains(eid));
        if (!p.Shift && !keepSelection) { sk.SelEntities.Clear(); sk.SelPoints.Clear(); }
        _drag = Drag.None; _down = false; _started = false;
        _geometryTranslation = false; _dragPlane = sk.Plane;
        var uv = sk.PlanePoint(s, p.X, p.Y);
        if (pid >= 0)
        {
            if (!keepSelection && (!sk.SelPoints.Remove(pid) || !p.Shift)) sk.SelPoints.Add(pid);
            if (!selectionOnly && pid != 0 && uv is { }) { _drag = Drag.Point; _dragPoint = pid; _down = true; }
        }
        else if (eid >= 0)
        {
            if (!keepSelection && (!sk.SelEntities.Remove(eid) || !p.Shift)) sk.SelEntities.Add(eid);
            var e = sk.Model.E(eid);
            if (!selectionOnly && uv is { } && e.Type != EntType.Point)
            {
                _dragEnt = eid; _down = true;
                _drag = e.Type == EntType.Circle && !sk.GeometryEditing && sk.SelEntities.Count == 1 ? Drag.Radius : Drag.Entity;
                _start.Clear();
                foreach (var q in sk.SelPoints.Concat(sk.SelEntities.SelectMany(id=>sk.Model.PointsOf(sk.Model.E(id)))).Distinct()) { var pt = sk.Model.P(q); _start[q] = (pt.X, pt.Y); }
            }
        }
        if (pid < 0 && eid < 0) { _region = true; _rx0 = _rx1 = p.X; _ry0 = _ry1 = p.Y; _path.Clear(); _path.Add((p.X, p.Y)); }
        _px0 = p.X; _py0 = p.Y; if (uv is { } a) { _u0 = a.u; _v0 = a.v; }
        s.NotifySketchChanged();
        s.Invalidate();
        return true;
    }

    public override void PointerMove(CadSession s, PointerInfo p)
    {
        var sk = Sk(s);
        if (_region && p.Left)
        {
            _rx1 = p.X; _ry1 = p.Y;
            var last = _path[^1]; if (Math.Abs(p.X - last.x) + Math.Abs(p.Y - last.y) > 3) _path.Add((p.X, p.Y));
            s.Invalidate(); return;
        }
        if (_down && p.Left)
        {
            if (!_started)
            {
                if (Math.Abs(p.X - _px0) + Math.Abs(p.Y - _py0) < 4) return;
                _started = true; _snapshot = sk.Model.Clone();
                if (p.Alt && _drag == Drag.Entity) CopyDraggedEntities(sk);
                if (_drag == Drag.Entity && sk.GeometryEditing) { sk.BeginSelectionTranslation(); _geometryTranslation = true; }
            }
            var (rayOrigin,rayDirection)=s.Camera.Ray(p.X,p.Y);
            if (_dragPlane.Intersect(rayOrigin,rayDirection) is not { } world) return;
            var uv = _dragPlane.Project(world);
            switch (_drag)
            {
                case Drag.Point:
                    if(sk.GeometryEditing)sk.DragGeometryPoint(s,_dragPoint,uv.u,uv.v);
                    else sk.Solve(s, [(_dragPoint, uv.u, uv.v)]);
                    break;
                case Drag.Entity:
                {
                    double du = uv.u - _u0, dv = uv.v - _v0;
                    if (_geometryTranslation) sk.PreviewSelectionTranslation(s,_dragPlane.U*du+_dragPlane.V*dv);
                    else sk.Solve(s, _start.Select(kv => (kv.Key, kv.Value.u + du, kv.Value.v + dv)).ToList());
                    break;
                }
                case Drag.Radius:
                {
                    var e = sk.Model.E(_dragEnt); var c = sk.Model.P(e.A);
                    sk.Solve(s, null, (_dragEnt, Dist(c.X, c.Y, uv.u, uv.v)));
                    break;
                }
            }
            s.Invalidate();
            return;
        }
        // hover feedback
        int hp = sk.PickPoint(s, p.X, p.Y), he = hp < 0 ? sk.PickEntity(s, p.X, p.Y) : -1;
        if (hp != sk.HoverPoint || he != sk.HoverEntity) { sk.HoverPoint = hp; sk.HoverEntity = he; s.Invalidate(); }
        sk.Cursor = null;
        if (sk.PlanePoint(s, p.X, p.Y) is { } w) s.Cursor3 = sk.Plane.PointAt(w.u, w.v);
    }

    public override void PointerUp(CadSession s, PointerInfo p)
    {
        var sk = Sk(s);
        if (_region) { _rx1 = p.X; _ry1 = p.Y; FinishRegion(s); return; }
        if (_started && _snapshot != null)
        {
            if (_geometryTranslation) sk.EndSelectionTranslation(s);
            else if (sk.Result.Converged) sk.PushSnapshot(_snapshot);
            else { sk.Restore(s, _snapshot); s.Say("That move conflicts with the constraints.", true); }
        }
        _down = false; _started = false; _drag = Drag.None; _snapshot = null;
        _geometryTranslation = false;
        s.NotifySketchChanged();
        s.Invalidate();
    }

    /// <summary>Alt-drag copies: clone the picked entities, keep the originals, and drag the clones.</summary>
    void CopyDraggedEntities(SketchContext sk)
    {
        var m = sk.Model;
        var map = new Dictionary<int, int>();
        int P(int pid)
        {
            if (!map.TryGetValue(pid, out int np)) { var q = m.P(pid); np = m.AddPoint(q.X, q.Y); map[pid] = np; }
            return np;
        }
        var copies = new List<int>();
        foreach (int id in sk.SelEntities.ToList())
        {
            var e = m.EntityOrNull(id); if (e == null) continue;
            int c = e.Type switch
            {
                EntType.Line => m.AddLine(P(e.A), P(e.B), e.Construction),
                EntType.Circle => m.AddCircle(P(e.A), e.R, e.Construction),
                EntType.Arc => m.AddArc(P(e.A), P(e.B), P(e.C), e.Construction),
                EntType.Ellipse => m.AddEllipse(P(e.A), P(e.B), e.R, e.Construction),
                EntType.Spline => m.AddSpline(e.Pts.Select(P).ToList(), e.Closed, e.ControlPoints, e.Construction),
                EntType.Point => m.AddPointEntity(P(e.A), e.Construction),
                _ => -1,
            };
            if (c >= 0) copies.Add(c);
        }
        if (copies.Count == 0) return;
        sk.SelEntities.Clear(); foreach (int c in copies) sk.SelEntities.Add(c);
        sk.SelPoints.Clear();
        _start.Clear();
        foreach (var q in sk.SelEntities.SelectMany(id => m.PointsOf(m.E(id)))) { var pt = m.P(q); _start[q] = (pt.X, pt.Y); }
    }

    public override bool Key(CadSession s, string key)
    {
        var sk = Sk(s);
        switch (key)
        {
            case "Delete": return sk.DeleteSelected(s) || true;
            case "Escape":
                if (sk.SelEntities.Count + sk.SelPoints.Count > 0 || sk.SelConstraint >= 0) { sk.SelEntities.Clear(); sk.SelPoints.Clear(); sk.SelConstraint = -1; s.NotifySketchChanged(); s.Invalidate(); return true; }
                return false;
        }
        return false;
    }
}

// =============================================================== Constraints
/// <summary>Pick the geometry a constraint applies to (or select it first and then press the button).</summary>
public sealed class ConstraintTool(CType type) : SketchTool
{
    public override string PanelTitle => type + " constraint";
    public override string PanelInputTitle => "Select geometry";
    readonly List<(bool pt, int id)> _picks = [];
    public override string Name => "Constrain";
    public override string Mode => type.ToString();

    public override string Prompt => type switch
    {
        CType.Horizontal => "Horizontal: pick one line to make it level, or pick two points - the second point moves level with the first.",
        CType.Vertical => "Vertical: pick one line to make it upright, or pick two points - the second point moves in line above/below the first.",
        CType.Coincident => "Coincident: pick two points (the second moves onto the first), or a point and then a curve to put the point on it.",
        CType.Parallel => "Parallel: pick the reference line first, then the line to change - the second turns to match the first.",
        CType.Perpendicular => "Perpendicular: pick the reference line first, then the line to change - the second turns square to the first.",
        CType.Collinear => "Collinear: pick the reference line first, then the line to move - the second slides onto the first one's line.",
        CType.Equal => "Equal: pick the reference first, then the one to change - the second gets the first one's length (or radius). Works on two lines or two circles/arcs.",
        CType.Tangent => "Tangent: pick the reference first (a line or circle/arc), then the one to change - the second moves to touch the first. Works for line + circle/arc or two circles/arcs.",
        CType.Concentric => "Concentric: pick the reference circle/arc first, then the circle to move - the second takes the first one's center.",
        CType.Midpoint => "Midpoint: pick a point and a line (either order). The line stays put and the point moves to its middle.",
        CType.Symmetric => "Symmetric: pick the reference point, then the point to change, then the mirror line - the second point moves to the mirror image of the first.",
        CType.Fixed => "Fix: pick a point or a curve to lock it where it is. Fixed geometry never moves when you add other constraints - everything else moves around it.",
        _ => type.ToString(),
    };

    // allowed pick sequences (true = point, false = curve)
    static readonly Dictionary<CType, bool[][]> Patterns = new()
    {
        [CType.Horizontal] = [[false], [true, true]], [CType.Vertical] = [[false], [true, true]],
        [CType.Coincident] = [[true, true], [true, false]],
        [CType.Parallel] = [[false, false]], [CType.Perpendicular] = [[false, false]], [CType.Collinear] = [[false, false]],
        [CType.Equal] = [[false, false]], [CType.Tangent] = [[false, false]], [CType.Concentric] = [[false, false]],
        [CType.Midpoint] = [[true, false], [false, true]],
        [CType.Symmetric] = [[true, true, false]],
        [CType.Fixed] = [[true], [false]],
    };

    bool Extends(bool next)
    {
        foreach (var pat in Patterns[type])
        {
            if (pat.Length <= _picks.Count) continue;
            bool ok = true;
            for (int i = 0; i < _picks.Count; i++) if (pat[i] != _picks[i].pt) { ok = false; break; }
            if (ok && pat[_picks.Count] == next) return true;
        }
        return false;
    }

    bool Complete()
    {
        var pats = Patterns[type];
        bool exact = pats.Any(p => p.Length == _picks.Count && p.Select((x, i) => x == _picks[i].pt).All(b => b));
        bool longer = pats.Any(p => p.Length > _picks.Count && p.Take(_picks.Count).Select((x, i) => x == _picks[i].pt).All(b => b));
        return exact && !longer;
    }

    public override void Activate(CadSession s)
    {
        var sk = Sk(s);
        _picks.Clear();
        // geometry that is already selected is used right away
        var pre = sk.SelPoints.Select(p => (true, p)).Concat(sk.SelEntities.Select(e => (false, e))).ToList();
        if (pre.Count == 0) return;
        foreach (var pick in pre) { if (Extends(pick.Item1)) _picks.Add(pick); }
        if (_picks.Count > 0 && Complete() && Apply(s)) sk.SetTool(s, new SelectTool());
        else _picks.Clear();
    }

    public override bool PointerDown(CadSession s, PointerInfo p)
    {
        if (!p.Left) return false;
        var sk = Sk(s);
        int pid = sk.PickPoint(s, p.X, p.Y), eid = sk.PickEntity(s, p.X, p.Y);
        if (pid >= 0 && Extends(true)) _picks.Add((true, pid));
        else if (eid >= 0 && Extends(false)) _picks.Add((false, eid));
        else if (pid >= 0 || eid >= 0) { s.Say("That does not fit here. " + Prompt, true); return true; }
        else { _picks.Clear(); s.Invalidate(); return true; }
        if (Complete()) { if (Apply(s)) s.Say($"{SketchModel.Describe(type)} applied. Pick more, or press Esc to finish."); _picks.Clear(); }
        else s.Say($"{type}: {_picks.Count} picked - choose the next one.");
        sk.SelEntities.Clear(); sk.SelPoints.Clear();
        s.Invalidate();
        return true;
    }

    public override void PointerMove(CadSession s, PointerInfo p)
    {
        var sk = Sk(s);
        int hp = sk.PickPoint(s, p.X, p.Y), he = sk.PickEntity(s, p.X, p.Y);
        if (hp != sk.HoverPoint || he != sk.HoverEntity) { sk.HoverPoint = hp; sk.HoverEntity = hp >= 0 ? -1 : he; s.Invalidate(); }
        if (sk.PlanePoint(s, p.X, p.Y) is { } w) s.Cursor3 = sk.Plane.PointAt(w.u, w.v);
    }

    public override bool Key(CadSession s, string key)
    {
        if (key is "Escape" or "RightClick") { if (_picks.Count > 0) { _picks.Clear(); s.Invalidate(); return true; } Sk(s).SetTool(s, new SelectTool()); return true; }
        return false;
    }

    public override void BuildOverlay(CadSession s, Overlay o)
    {
        var sk = Sk(s);
        foreach (var (pt, id) in _picks)
        {
            if (pt) { if (sk.Model.PointOrNull(id) is { } p) o.AddDot(sk.Plane.PointAt(p.X, p.Y), 0xFFFF8A1F, 11); }
            else foreach (var (e, pts) in sk.Model.Polylines(sk.Plane)) if (e.Id == id) o.AddPolyline(pts, 0xFFFF8A1F, 4f);
        }
    }

    /// <summary>Order matters: the first pick is the reference and the second one moves to match it. Done here so the solver starts from the right place.</summary>
    void Presnap(SketchModel m)
    {
        SkPoint PT(int i) => m.P(_picks[i].id);
        SkEntity E(int i) => m.E(_picks[i].id);
        (double dx, double dy, double len) Dir(SkEntity l) { var a = m.P(l.A); var b = m.P(l.B); double dx = b.X - a.X, dy = b.Y - a.Y; return (dx, dy, Math.Max(Math.Sqrt(dx * dx + dy * dy), 1e-12)); }
        switch (type)
        {
            case CType.Coincident when _picks.Count == 2 && _picks[0].pt && _picks[1].pt: { var a = PT(0); var b = PT(1); b.X = a.X; b.Y = a.Y; break; }
            case CType.Horizontal when _picks.Count == 2 && _picks[0].pt: PT(1).Y = PT(0).Y; break;
            case CType.Vertical when _picks.Count == 2 && _picks[0].pt: PT(1).X = PT(0).X; break;
            case CType.Midpoint when _picks.Count == 2:
            {
                var pt = _picks.First(x => x.pt).id; var ln = m.E(_picks.First(x => !x.pt).id);
                if (ln.Type != EntType.Line) break;
                var a = m.P(ln.A); var b = m.P(ln.B); var q = m.P(pt); q.X = (a.X + b.X) / 2; q.Y = (a.Y + b.Y) / 2; break;
            }
            case CType.Equal when _picks.Count == 2 && !_picks[0].pt:
            {
                var e1 = E(0); var e2 = E(1);
                if (e1.Type == EntType.Line && e2.Type == EntType.Line)
                {
                    var (_, _, l1) = Dir(e1); var (dx, dy, l2) = Dir(e2); var a = m.P(e2.A); var b = m.P(e2.B);
                    b.X = a.X + dx / l2 * l1; b.Y = a.Y + dy / l2 * l1;
                }
                else if (e1.Type == EntType.Circle && e2.Type == EntType.Circle) e2.R = e1.R;
                break;
            }
            case CType.Parallel or CType.Perpendicular when _picks.Count == 2 && !_picks[0].pt:
            {
                var e1 = E(0); var e2 = E(1); if (e1.Type != EntType.Line || e2.Type != EntType.Line) break;
                var (ux, uy, ul) = Dir(e1); var (dx, dy, l2) = Dir(e2);
                double tx = ux / ul, ty = uy / ul; if (type == CType.Perpendicular) { (tx, ty) = (-ty, tx); }
                if (tx * dx + ty * dy < 0) { tx = -tx; ty = -ty; }
                var a = m.P(e2.A); var b = m.P(e2.B); b.X = a.X + tx * l2; b.Y = a.Y + ty * l2; break;
            }
            case CType.Collinear when _picks.Count == 2 && !_picks[0].pt:
            {
                var e1 = E(0); var e2 = E(1); if (e1.Type != EntType.Line || e2.Type != EntType.Line) break;
                var a1 = m.P(e1.A); var (ux, uy, ul) = Dir(e1); ux /= ul; uy /= ul;
                foreach (var pid in new[] { e2.A, e2.B }) { var q = m.P(pid); double t = (q.X - a1.X) * ux + (q.Y - a1.Y) * uy; q.X = a1.X + ux * t; q.Y = a1.Y + uy * t; }
                break;
            }
            case CType.Tangent when _picks.Count == 2 && !_picks[0].pt:
            {
                var e1 = E(0); var e2 = E(1);
                bool r1 = e1.Type is EntType.Circle or EntType.Arc, r2 = e2.Type is EntType.Circle or EntType.Arc;
                if (r1 && r2)
                {
                    var c1 = m.P(e1.A); var c2 = m.P(e2.A); double ra = m.Radius(e1), rb = m.Radius(e2);
                    double dx = c2.X - c1.X, dy = c2.Y - c1.Y, d = Math.Max(Math.Sqrt(dx * dx + dy * dy), 1e-9);
                    double want = Math.Abs(d - (ra + rb)) <= Math.Abs(d - Math.Abs(ra - rb)) ? ra + rb : Math.Abs(ra - rb);
                    if (e2.Type == EntType.Circle) { c2.X = c1.X + dx / d * want; c2.Y = c1.Y + dy / d * want; }
                }
                else if (!r1 && r2 && e2.Type == EntType.Circle)         // line first, circle second: the circle moves to touch the line
                {
                    var a = m.P(e1.A); var (ux, uy, ul) = Dir(e1); ux /= ul; uy /= ul;
                    var c = m.P(e2.A); double side = (c.X - a.X) * -uy + (c.Y - a.Y) * ux, r = e2.R;
                    double shift = (side >= 0 ? r : -r) - side; c.X += -uy * shift; c.Y += ux * shift;
                }
                else if (r1 && !r2)                                      // circle first, line second: the line moves to touch the circle
                {
                    var c = m.P(e1.A); double r = m.Radius(e1); var a = m.P(e2.A); var b = m.P(e2.B); var (ux, uy, ul) = Dir(e2); ux /= ul; uy /= ul;
                    double side = (c.X - a.X) * -uy + (c.Y - a.Y) * ux, shift = side - (side >= 0 ? r : -r);
                    a.X += -uy * shift; a.Y += ux * shift; b.X += -uy * shift; b.Y += ux * shift;
                }
                break;
            }
            case CType.Symmetric when _picks.Count == 3:
            {
                var ax = E(2); if (ax.Type != EntType.Line) break;
                var a = m.P(ax.A); var (ux, uy, ul) = Dir(ax); ux /= ul; uy /= ul;
                var p1 = PT(0); var p2 = PT(1);
                double t = (p1.X - a.X) * ux + (p1.Y - a.Y) * uy, fx = a.X + ux * t, fy = a.Y + uy * t;
                p2.X = 2 * fx - p1.X; p2.Y = 2 * fy - p1.Y; break;
            }
            case CType.Concentric when _picks.Count == 2 && !_picks[0].pt:
            {
                var e1 = E(0); var e2 = E(1);
                if (e1.Type is EntType.Circle or EntType.Arc && e2.Type == EntType.Circle) { var a = m.P(e1.A); var b = m.P(e2.A); b.X = a.X; b.Y = a.Y; }
                break;
            }
        }
    }

    bool Apply(CadSession s)
    {
        var sk = Sk(s);
        sk.PreChange = () => Presnap(sk.Model);
        try { return ApplyInner(s); } finally { sk.PreChange = null; }
    }

    bool ApplyInner(CadSession s)
    {
        var sk = Sk(s); var m = sk.Model;
        string Fail(string why) { s.Say(why, true); return why; }
        SkEntity E(int i) => m.E(_picks[i].id);
        bool IsLine(int i) => E(i).Type == EntType.Line;
        bool IsRound(int i) => E(i).Type is EntType.Circle or EntType.Arc;
        string name = SketchModel.Describe(type);

        switch (type)
        {
            case CType.Horizontal or CType.Vertical:
                if (_picks[0].pt) return sk.Edit(s, name, () => m.Add(type, refs: [_picks[0].id, _picks[1].id]));
                if (!IsLine(0)) { Fail($"{name} needs a line."); return false; }
                return sk.Edit(s, name, () => m.Add(type, refs: _picks[0].id));
            case CType.Coincident:
                if (_picks[1].pt) return sk.Edit(s, name, () => m.Add(CType.Coincident, refs: [_picks[0].id, _picks[1].id]));
                return sk.Edit(s, "Point on curve", () => m.Add(CType.PointOnEntity, refs: [_picks[0].id, _picks[1].id]));
            case CType.Parallel or CType.Perpendicular or CType.Collinear:
                if (!IsLine(0) || !IsLine(1)) { Fail($"{name} needs two lines."); return false; }
                return sk.Edit(s, name, () => m.Add(type, refs: [_picks[0].id, _picks[1].id]));
            case CType.Equal:
                if (!((IsLine(0) && IsLine(1)) || (IsRound(0) && IsRound(1)))) { Fail("Equal needs two lines or two circles/arcs."); return false; }
                return sk.Edit(s, name, () => m.Add(type, refs: [_picks[0].id, _picks[1].id]));
            case CType.Concentric:
                if (!IsRound(0) || !IsRound(1)) { Fail("Concentric needs two circles or arcs."); return false; }
                return sk.Edit(s, name, () => m.Add(type, refs: [_picks[0].id, _picks[1].id]));
            case CType.Tangent:
            {
                bool ok = (IsLine(0) && IsRound(1)) || (IsRound(0) && IsLine(1)) || (IsRound(0) && IsRound(1));
                if (!ok) { Fail("Tangent needs a line and a circle/arc, or two circles/arcs."); return false; }
                double sign = 1;
                if (IsRound(0) && IsRound(1))
                {
                    var a = E(0); var b = E(1); var ca = m.P(a.A); var cb = m.P(b.A);
                    double d = Dist(ca.X, ca.Y, cb.X, cb.Y), ra = m.Radius(a), rb = m.Radius(b);
                    sign = Math.Abs(d - (ra + rb)) <= Math.Abs(d - Math.Abs(ra - rb)) ? 1 : -1;
                }
                return sk.Edit(s, name, () => m.Add(CType.Tangent, sign, 0, false, _picks[0].id, _picks[1].id));
            }
            case CType.Midpoint:
            {
                var pt = _picks.First(x => x.pt).id; var ln = _picks.First(x => !x.pt).id;
                if (m.E(ln).Type != EntType.Line) { Fail("Midpoint needs a line."); return false; }
                return sk.Edit(s, name, () => m.Add(CType.Midpoint, refs: [pt, ln]));
            }
            case CType.Symmetric:
                if (m.E(_picks[2].id).Type != EntType.Line) { Fail("Symmetric needs a line as the mirror axis."); return false; }
                return sk.Edit(s, name, () => m.Add(CType.Symmetric, refs: [_picks[0].id, _picks[1].id, _picks[2].id]));
            case CType.Fixed:
                return sk.Edit(s, name, () =>
                {
                    var pts = _picks[0].pt ? new[] { _picks[0].id } : m.PointsOf(m.E(_picks[0].id)).Distinct().ToArray();
                    foreach (var pid in pts) { var pt = m.P(pid); m.Add(CType.Fixed, pt.X, pt.Y, false, pid); }
                });
        }
        return false;
    }
}

// =============================================================== Dimensions
public enum DimMode { Smart, Horizontal, Vertical }

/// <summary>Click geometry to dimension it, then type the value. The solver moves the sketch to match.</summary>
public sealed class DimensionTool(DimMode mode) : SketchTool
{
    public override string PanelInputTitle => "Select geometry";
    public override string PanelSettingsTitle => "Dimension value";
    public override string PanelActionTitle => "Apply dimension";
    readonly List<(bool pt, int id)> _picks = [];
    bool _waiting,_dragging;
    double _downX,_downY,_value,_sign=1;
    CType _pendingType;int[] _pendingRefs=[];int _previewLine=-1;
    int _pa=-1,_pb=-1,_lineId=-1;bool _smart;
    (double u,double v)? _position;
    public override IReadOnlyList<ToolControl>? Panel =>
    [new(ToolControlKind.Info,"step",_waiting?"Set the driving value and Apply. Drag a line outward to position its dimension.":Prompt),
     new(ToolControlKind.Number,"value","Value",Number:_value,Unit:_pendingType==CType.Angle?"degrees":"mm",Enabled:_waiting),
     new(ToolControlKind.Button,"apply","Apply dimension",Enabled:_waiting,Primary:true)];
    public override void PanelSet(CadSession s,string id,object? value)
    {
        if(id=="value"&&value is double number&&number>0){_value=number;s.Invalidate();return;}
        if(id=="apply"&&_waiting)Place(s,false);
        s.RaiseState();s.Invalidate();
    }

    /// <summary>Put the pending dimension where the pointer is. Clicking places it and opens the value box right there in the viewport; the panel's Apply keeps the measured value.</summary>
    void Place(CadSession s,bool editValue)
    {
        var sk=Sk(s);SkConstraint? made=null;
        Resolve(sk);
        if(!sk.Edit(s,"That dimension",()=>{made=sk.Model.Add(_pendingType,_sign*_value,0,true,_pendingRefs);if(_position is { } p){made.LabelU=p.u;made.LabelV=p.v;}}))return;
        _waiting=false;_previewLine=-1;_smart=false;_pa=_pb=_lineId=-1;
        if(editValue&&made!=null&&sk.Model.Constraints.FirstOrDefault(x=>x.Id==made.Id) is { } fresh)sk.EditDimension(s,fresh);
    }

    /// <summary>Smart dimension: where the pointer sits decides the kind. Beside the two points gives their distance along the line between them; above or below the span gives the horizontal distance; left or right of it the vertical one.</summary>
    void Resolve(SketchContext sk)
    {
        if(!_smart||_position is not { } c||_pa<0||sk.Model.PointOrNull(_pa) is not { } a||sk.Model.PointOrNull(_pb) is not { } b)return;
        bool inX=c.u>=Math.Min(a.X,b.X)&&c.u<=Math.Max(a.X,b.X),inY=c.v>=Math.Min(a.Y,b.Y)&&c.v<=Math.Max(a.Y,b.Y);
        if(inX&&!inY&&Math.Abs(b.X-a.X)>1e-6){_pendingType=CType.HDistance;_pendingRefs=[_pa,_pb];_sign=b.X>=a.X?1:-1;_value=Math.Abs(b.X-a.X);}
        else if(inY&&!inX&&Math.Abs(b.Y-a.Y)>1e-6){_pendingType=CType.VDistance;_pendingRefs=[_pa,_pb];_sign=b.Y>=a.Y?1:-1;_value=Math.Abs(b.Y-a.Y);}
        else{_pendingType=_lineId>=0?CType.LineLength:CType.Distance;_pendingRefs=_lineId>=0?[_lineId]:[_pa,_pb];_sign=1;_value=Dist(a.X,a.Y,b.X,b.Y);}
    }

    public override string Name => "Dimension";
    public override string Mode => mode == DimMode.Smart ? "Smart" : mode.ToString();
    static readonly ToolOption[] Opts = [new("Smart", "The dimension type follows what you pick", "dimension"), new("Horizontal", "Horizontal distance", "dimension-h"), new("Vertical", "Vertical distance", "dimension-v")];
    public override IReadOnlyList<ToolOption> Options => Opts;
    public override int OptionIndex => (int)mode;
    protected override void ApplyOption(int index) => mode = (DimMode)index;
    protected override void ResetState(CadSession s) { _picks.Clear(); _waiting = _dragging = false; _previewLine = -1; _position = null; _pendingRefs = []; _smart = false; _pa = _pb = _lineId = -1; }
    public override string Prompt => mode switch
    {
        DimMode.Horizontal => "Horizontal dimension: pick two points, or a line.",
        DimMode.Vertical => "Vertical dimension: pick two points, or a line.",
        _ => "Dimension: pick a line (length), a circle (diameter), an arc (radius), two points, a point and a line, or two lines (angle).",
    };

    public override bool PointerDown(CadSession s, PointerInfo p)
    {
        if (!p.Left) return true;
        var sk = Sk(s);
        if (_waiting)
        {
            // a second line instead of a placement turns a length into an angle
            int other=sk.PickEntity(s,p.X,p.Y);
            if(_lineId>=0&&other>=0&&other!=_lineId&&sk.Model.E(other).Type==EntType.Line&&sk.PickPoint(s,p.X,p.Y)<0)
            {_picks.Clear();_picks.Add((false,_lineId));_picks.Add((false,other));_waiting=false;_lineId=-1;Finish(s,false);s.Invalidate();return true;}
            if(sk.PlanePoint(s,p.X,p.Y) is { } at)_position=at;
            Place(s,true);s.RaiseState();s.Invalidate();return true;
        }
        if (sk.PickDimension(s, p.X, p.Y) is { } existing) { if (p.Clicks >= 2) sk.EditDimension(s, existing); return true; }
        int pid = sk.PickPoint(s, p.X, p.Y), eid = sk.PickEntity(s, p.X, p.Y);
        if (pid >= 0) _picks.Add((true, pid));
        else if (eid >= 0 && sk.Model.E(eid).Type != EntType.Point) _picks.Add((false, eid));
        else
        {
            // empty space: a lone line becomes a length dimension
            if (_picks.Count == 1 && !_picks[0].pt) Finish(s, true);
            else _picks.Clear();
            s.Invalidate();
            return true;
        }
        _downX=p.X;_downY=p.Y;
        if(_picks.Count==1&&!_picks[0].pt&&sk.Model.E(_picks[0].id).Type==EntType.Line){Finish(s,true);s.Invalidate();return true;}
        TryFinish(s);
        s.Invalidate();
        return true;
    }

    public override void PointerMove(CadSession s, PointerInfo p)
    {
        var sk = Sk(s);
        if(sk.PlanePoint(s,p.X,p.Y) is { } location&&_waiting){_position=location;Resolve(sk);s.RaiseState();}
        if (_waiting) s.Invalidate();
        int hp = sk.PickPoint(s, p.X, p.Y), he = sk.PickEntity(s, p.X, p.Y);
        if (hp != sk.HoverPoint || he != sk.HoverEntity) { sk.HoverPoint = hp; sk.HoverEntity = hp >= 0 ? -1 : he; s.Invalidate(); }
        if (sk.PlanePoint(s, p.X, p.Y) is { } w) s.Cursor3 = sk.Plane.PointAt(w.u, w.v);
    }

    void TryFinish(CadSession s)
    {
        var m = Sk(s).Model;
        if (_picks.Count == 1)
        {
            if (_picks[0].pt) return;
            var e = m.E(_picks[0].id);
            if (mode != DimMode.Smart && e.Type == EntType.Line) { Finish(s, false); return; }
            if (e.Type is EntType.Circle or EntType.Arc) Finish(s, false);
            else if (e.Type != EntType.Line) { _picks.Clear(); s.Say("That cannot be dimensioned directly.", true); }
            else s.Say("Click a second line for an angle, or click empty space for the length.");
        }
        else if (_picks.Count == 2) Finish(s, false);
    }

    void Finish(CadSession s, bool lone)
    {
        var sk = Sk(s); var m = sk.Model;
        CType t; int[] refs; double value;
        double Ang(SkEntity a, SkEntity b)
        {
            var a1 = m.P(a.A); var a2 = m.P(a.B); var b1 = m.P(b.A); var b2 = m.P(b.B);
            double d1x = a2.X - a1.X, d1y = a2.Y - a1.Y, d2x = b2.X - b1.X, d2y = b2.Y - b1.Y;
            return Math.Atan2(d1x * d2y - d1y * d2x, d1x * d2x + d1y * d2y) * 180 / Math.PI;
        }
        var p0 = _picks[0];
        if (_picks.Count == 1)
        {
            var e = m.E(p0.id);
            if (e.Type == EntType.Line)
            {
                var a = m.P(e.A); var b = m.P(e.B);
                if (mode == DimMode.Horizontal) { t = CType.HDistance; refs = [e.A, e.B]; value = b.X - a.X; }
                else if (mode == DimMode.Vertical) { t = CType.VDistance; refs = [e.A, e.B]; value = b.Y - a.Y; }
                else { t = CType.LineLength; refs = [e.Id]; value = Dist(a.X, a.Y, b.X, b.Y); }
            }
            else if (e.Type == EntType.Circle) { t = CType.Diameter; refs = [e.Id]; value = 2 * e.R; }
            else { t = CType.Radius; refs = [e.Id]; value = m.Radius(e); }
        }
        else
        {
            var p1 = _picks[1];
            if (p0.pt && p1.pt)
            {
                var a = m.P(p0.id); var b = m.P(p1.id);
                if (mode == DimMode.Horizontal) { t = CType.HDistance; value = b.X - a.X; }
                else if (mode == DimMode.Vertical) { t = CType.VDistance; value = b.Y - a.Y; }
                else { t = CType.Distance; value = Dist(a.X, a.Y, b.X, b.Y); }
                refs = [p0.id, p1.id];
            }
            else if (p0.pt != p1.pt)
            {
                int pt = p0.pt ? p0.id : p1.id, ln = p0.pt ? p1.id : p0.id;
                var l = m.E(ln);
                if (l.Type != EntType.Line) { _picks.Clear(); s.Say("A point can be dimensioned to a line only.", true); return; }
                var a = m.P(l.A); var b = m.P(l.B); var q = m.P(pt);
                double dx = b.X - a.X, dy = b.Y - a.Y, len = Math.Max(Dist(0, 0, dx, dy), 1e-9);
                t = CType.PointLineDistance; refs = [pt, ln]; value = Math.Abs(((q.X - a.X) * dy - (q.Y - a.Y) * dx) / len);
            }
            else
            {
                var a = m.E(p0.id); var b = m.E(p1.id);
                if (a.Type != EntType.Line || b.Type != EntType.Line) { _picks.Clear(); s.Say("An angle needs two lines.", true); return; }
                t = CType.Angle; refs = [a.Id, b.Id]; value = Ang(a, b);
            }
        }
        _picks.Clear();
        double shown = t is CType.Angle or CType.HDistance or CType.VDistance ? Math.Abs(value) : value;
        double sign = value < 0 ? -1 : 1;
        _waiting = true;
        _pendingType=t;_pendingRefs=refs;_value=shown;_sign=sign;
        _pa=_pb=_lineId=-1;_smart=false;
        if(t==CType.LineLength){_lineId=refs[0];var ln=m.E(_lineId);_pa=ln.A;_pb=ln.B;_smart=mode==DimMode.Smart;}
        else if(t==CType.Distance){_pa=refs[0];_pb=refs[1];_smart=mode==DimMode.Smart;}
        else if(t is CType.HDistance or CType.VDistance){_pa=refs[0];_pb=refs[1];}
        _position??=_pa>=0&&m.PointOrNull(_pa) is { } pp?(pp.X,pp.Y):null;
        s.Say("Move to place the dimension, then click. Its value opens for typing. Esc cancels.");s.RaiseState();s.Invalidate();
    }

    public override bool Key(CadSession s, string key)
    {
        if(key=="Enter"&&_waiting){Place(s,true);s.RaiseState();s.Invalidate();return true;}
        if (key == "Enter" && _picks.Count == 1 && !_picks[0].pt) { Finish(s, true); return true; }
        if (key is "Escape" or "RightClick") { if (_picks.Count > 0 || _waiting) { ResetState(s); s.RaiseState(); s.Invalidate(); return true; } Sk(s).SetTool(s, new SelectTool()); return true; }
        return false;
    }

    public override void BuildOverlay(CadSession s, Overlay o)
    {
        var sk = Sk(s);
        if(_waiting&&_pa>=0&&_position is { } position&&_pendingType is CType.Distance or CType.LineLength or CType.HDistance or CType.VDistance&&sk.Model.PointOrNull(_pa) is { } A&&sk.Model.PointOrNull(_pb) is { } B)
        {
            double ax=A.X,ay=A.Y,bx=B.X,by=B.Y,ux,uy;
            if(_pendingType==CType.HDistance){ux=bx>=ax?1:-1;uy=0;by=ay;}
            else if(_pendingType==CType.VDistance){ux=0;uy=by>=ay?1:-1;bx=ax;}
            else{double dx=bx-ax,dy=by-ay,len=Math.Max(Math.Sqrt(dx*dx+dy*dy),1e-9);ux=dx/len;uy=dy/len;}
            double nx=-uy,ny=ux,off=(position.u-ax)*nx+(position.v-ay)*ny;
            var p1=sk.Plane.PointAt(ax+nx*off,ay+ny*off);var p2=sk.Plane.PointAt(bx+nx*off,by+ny*off);
            uint dc=SketchContext.DimColor;
            o.AddLine(sk.Plane.PointAt(A.X,A.Y),p1,dc,1.2f);o.AddLine(sk.Plane.PointAt(B.X,B.Y),p2,dc,1.2f);o.AddLine(p1,p2,dc,1.6f);
            double ps=s.Camera.PixelSizeAt(p1),ql=Math.Max(Math.Sqrt((bx-ax)*(bx-ax)+(by-ay)*(by-ay)),1e-9),qx=(bx-ax)/ql,qy=(by-ay)/ql;
            SketchContext.DimArrow(o,sk.Plane,ps,ax+nx*off,ay+ny*off,-qx,-qy,dc);SketchContext.DimArrow(o,sk.Plane,ps,bx+nx*off,by+ny*off,qx,qy,dc);
            string kind=_pendingType==CType.HDistance?"horizontal":_pendingType==CType.VDistance?"vertical":"aligned";
            o.AddLabel((p1+p2)/2,$"{_value:0.##} mm  ·  {kind}",true);
        }
        foreach (var (pt, id) in _picks)
        {
            if (pt) { if (sk.Model.PointOrNull(id) is { } p) o.AddDot(sk.Plane.PointAt(p.X, p.Y), 0xFFFF8A1F, 11); }
            else foreach (var (e, pts) in sk.Model.Polylines(sk.Plane)) if (e.Id == id) o.AddPolyline(pts, 0xFFFF8A1F, 4f);
        }
    }
}
