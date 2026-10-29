using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core.Sketching;

namespace Prism.Core;

/// <summary>Adding and removing the points of a sketch spline (fit points or control points).</summary>
public static class SplineOps
{
    /// <summary>The fewest points a spline can keep: a cubic needs four control points, a fit spline two (three when closed).</summary>
    public static int MinPoints(SkEntity e) => e.ControlPoints ? 4 : e.Closed ? 3 : 2;
    public static bool CanRemove(SkEntity e) => e.Pts.Count > MinPoints(e);

    static (double x, double y, double t) OnSegment((double X, double Y) a, (double X, double Y) b, double u, double v)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y, l2 = Math.Max(dx * dx + dy * dy, 1e-18);
        double t = Math.Clamp(((u - a.X) * dx + (v - a.Y) * dy) / l2, 0, 1);
        return (a.X + dx * t, a.Y + dy * t, t);
    }

    /// <summary>Where along the point list a new point near (u, v) belongs, and where it should sit.</summary>
    public static (int index, double x, double y) InsertSpot(SketchModel m, SkEntity e, double u, double v)
    {
        // closest spot on the drawn curve
        double bd = double.MaxValue, qx = u, qy = v;
        var s = m.SplineSamples(e);
        for (int i = 0; i + 1 < s.Count; i++)
        {
            var (x, y, _) = OnSegment(s[i], s[i + 1], u, v);
            double d = (x - u) * (x - u) + (y - v) * (y - v);
            if (d < bd) { bd = d; qx = x; qy = y; }
        }
        // the stretch between two neighbouring points that the spot lies in
        var pts = e.Pts.Select(i => (m.P(i).X, m.P(i).Y)).ToList();
        int n = pts.Count, segs = e.Closed && !e.ControlPoints ? n : n - 1, best = 0; double sd = double.MaxValue; (double x, double y) onPoly = (qx, qy);
        for (int i = 0; i < segs; i++)
        {
            var (x, y, _) = OnSegment(pts[i], pts[(i + 1) % n], qx, qy);
            double d = (x - qx) * (x - qx) + (y - qy) * (y - qy);
            if (d < sd) { sd = d; best = i; onPoly = (x, y); }
        }
        // fit points sit on the curve; a control point sits on the control polygon (the curve then follows it)
        return (best + 1, e.ControlPoints ? onPoly.x : qx, e.ControlPoints ? onPoly.y : qy);
    }

    /// <summary>Insert a point into the spline near (u, v). Returns the new point's id.</summary>
    public static int Insert(SketchModel m, SkEntity e, double u, double v)
    {
        var (index, x, y) = InsertSpot(m, e, u, v);
        int id = m.AddPoint(x, y);
        e.Pts.Insert(Math.Min(index, e.Pts.Count), id);
        return id;
    }

    /// <summary>Take a point out of the spline (it stays on screen only if something else uses it).</summary>
    public static bool Remove(SketchModel m, SkEntity e, int pid)
    {
        if (!CanRemove(e) || !e.Pts.Remove(pid)) return false;
        m.DropPointIfUnshared(pid);
        return true;
    }
}

/// <summary>Reshape a spline after drawing it: add points on the curve, drag them, remove them.</summary>
public sealed class SplineEditTool : SketchTool
{
    public override string Name => "Edit Spline";
    public override string Prompt => "Edit spline: click a spline, then click along it to add a point (drag to shape it), drag points to move them, right-click a point or press Delete to remove it. Press C to close or open the curve.";

    int _spline = -1;
    int _dragPt = -1; bool _down, _started; SketchModel? _snap;
    (double u, double v)? _ghost;

    public override void Activate(CadSession s)
    {
        var sk = Sk(s);
        _spline = sk.SelEntities.FirstOrDefault(id => sk.Model.EntityOrNull(id)?.Type == EntType.Spline, -1);
        if (_spline < 0) _spline = sk.SelPoints.Select(p => sk.Model.Entities.FirstOrDefault(e => e.Type == EntType.Spline && e.Pts.Contains(p))?.Id ?? -1).FirstOrDefault(i => i >= 0, -1);
        SetSelection(sk);
    }

    void SetSelection(SketchContext sk)
    {
        sk.SelEntities.Clear(); sk.SelPoints.Clear();
        if (_spline >= 0 && sk.Model.EntityOrNull(_spline) != null) sk.SelEntities.Add(_spline); else _spline = -1;
    }

    static SkEntity? SplineWith(SketchContext sk, int pid) => sk.Model.Entities.FirstOrDefault(e => e.Type == EntType.Spline && e.Pts.Contains(pid));

    int PickSplinePoint(CadSession s, SketchContext sk, double px, double py)
    {
        // points of the spline being edited win; otherwise any spline point
        int best = -1; double bd = 11;
        foreach (var p in sk.Model.Points)
        {
            if (p.Id == 0 || SplineWith(sk, p.Id) is not { } e) continue;
            var q = s.Camera.Project(sk.Plane.PointAt(p.X, p.Y)); if (q == null) continue;
            double d = Math.Sqrt((q.Value.x - px) * (q.Value.x - px) + (q.Value.y - py) * (q.Value.y - py)) - (e.Id == _spline ? 3 : 0);
            if (d < bd) { bd = d; best = p.Id; }
        }
        return best;
    }

    int PickSpline(CadSession s, SketchContext sk, double px, double py)
    {
        int eid = sk.PickEntity(s, px, py, 9);
        return eid >= 0 && sk.Model.EntityOrNull(eid)?.Type == EntType.Spline ? eid : -1;
    }

    public override bool PointerDown(CadSession s, PointerInfo p)
    {
        if (!p.Left) return false;
        var sk = Sk(s);
        if (sk.PickGlyph(s, p.X, p.Y) is { } glyph) { sk.SelectConstraint(s, glyph); return true; }
        _down = false; _started = false; _dragPt = -1;
        int pid = PickSplinePoint(s, sk, p.X, p.Y);
        if (pid >= 0)
        {
            _spline = SplineWith(sk, pid)!.Id; SetSelection(sk); sk.SelPoints.Add(pid);
            if (pid != 0) { _dragPt = pid; _down = true; }
            s.NotifySketchChanged(); s.Invalidate(); return true;
        }
        int eid = PickSpline(s, sk, p.X, p.Y);
        if (eid >= 0 && eid != _spline) { _spline = eid; SetSelection(sk); s.Say("Spline selected. Click along it to add points, drag points to reshape it."); s.NotifySketchChanged(); s.Invalidate(); return true; }
        if (eid >= 0 && sk.PlanePoint(s, p.X, p.Y) is { } uv)
        {
            int made = -1;
            if (sk.Edit(s, "Adding that point", () => { var e = sk.Model.E(_spline); made = SplineOps.Insert(sk.Model, e, uv.u, uv.v); }) && made >= 0)
            {
                sk.SelPoints.Clear(); sk.SelPoints.Add(made);
                _dragPt = made; _down = true;           // keep the button down to shape it right away
                s.Say("Point added. Drag to shape the curve, right-click it to remove it again.");
            }
            s.Invalidate(); return true;
        }
        _spline = -1; SetSelection(sk);
        s.NotifySketchChanged(); s.Invalidate();
        return true;
    }

    public override void PointerMove(CadSession s, PointerInfo p)
    {
        var sk = Sk(s);
        if (_down && p.Left && _dragPt >= 0)
        {
            if (!_started)
            {
                _started = true; _snap = sk.Model.Clone();
            }
            if (sk.PlanePoint(s, p.X, p.Y) is { } uv) { sk.Solve(s, [(_dragPt, uv.u, uv.v)]); s.Invalidate(); }
            return;
        }
        int hp = PickSplinePoint(s, sk, p.X, p.Y);
        int he = hp < 0 ? PickSpline(s, sk, p.X, p.Y) : -1;
        _ghost = null;
        if (he >= 0 && he == _spline && sk.PlanePoint(s, p.X, p.Y) is { } w)
        {
            var spot = SplineOps.InsertSpot(sk.Model, sk.Model.E(he), w.u, w.v);
            _ghost = (spot.x, spot.y);
        }
        sk.HoverPoint = hp; sk.HoverEntity = he;
        sk.Cursor = null;
        if (sk.PlanePoint(s, p.X, p.Y) is { } c) s.Cursor3 = sk.Plane.PointAt(c.u, c.v);
        s.Invalidate();
    }

    public override void PointerUp(CadSession s, PointerInfo p)
    {
        var sk = Sk(s);
        if (_started && _snap != null)
        {
            if (sk.Result.Converged) sk.PushSnapshot(_snap);
            else { sk.Restore(s, _snap); s.Say("That move conflicts with the constraints.", true); }
        }
        _down = false; _started = false; _dragPt = -1; _snap = null;
        s.NotifySketchChanged(); s.Invalidate();
    }

    bool RemovePoint(CadSession s, int pid)
    {
        var sk = Sk(s);
        if (SplineWith(sk, pid) is not { } e) return false;
        if (!SplineOps.CanRemove(e)) { s.Say($"A {(e.ControlPoints ? "control-point" : "fit-point")} spline needs at least {SplineOps.MinPoints(e)} points.", true); return true; }
        bool ok = sk.Edit(s, "Removing that point", () => SplineOps.Remove(sk.Model, sk.Model.E(e.Id), pid));
        if (ok) { sk.SelPoints.Clear(); sk.HoverPoint = -1; s.Say("Point removed."); }
        s.Invalidate();
        return true;
    }

    public override bool Key(CadSession s, string key)
    {
        var sk = Sk(s);
        switch (key)
        {
            case "RightClick":
            {
                int hp = sk.HoverPoint >= 0 ? sk.HoverPoint : -1;
                if (hp >= 0 && RemovePoint(s, hp)) return true;
                if (_spline >= 0) { _spline = -1; SetSelection(sk); s.NotifySketchChanged(); s.Invalidate(); return true; }
                sk.SetTool(s, new SelectTool()); return true;
            }
            case "Delete" or "Back":
                if (sk.SelPoints.Count == 1 && RemovePoint(s, sk.SelPoints.First())) return true;
                if (sk.HoverPoint >= 0 && RemovePoint(s, sk.HoverPoint)) return true;
                if (_spline >= 0) { sk.SelEntities.Clear(); sk.SelEntities.Add(_spline); sk.DeleteSelected(s); _spline = -1; return true; }
                return true;
            case "Char:C":
                if (_spline >= 0 && sk.Model.EntityOrNull(_spline) is { } e)
                {
                    if (e.ControlPoints) { s.Say("Control-point splines stay open.", true); return true; }
                    if (e.Pts.Count < 3) { s.Say("A spline needs at least three points to close.", true); return true; }
                    sk.Edit(s, e.Closed ? "Opening that spline" : "Closing that spline", () => { var x = sk.Model.E(_spline); x.Closed = !x.Closed; });
                    SetSelection(sk); return true;
                }
                return false;
            case "Escape":
                if (_spline >= 0) { _spline = -1; SetSelection(sk); s.NotifySketchChanged(); s.Invalidate(); return true; }
                return false;
        }
        return false;
    }

    public override void BuildOverlay(CadSession s, Overlay o)
    {
        var sk = Sk(s);
        if (_spline >= 0 && sk.Model.EntityOrNull(_spline) is { } e)
        {
            var pts = e.Pts.Select(i => sk.Plane.PointAt(sk.Model.P(i).X, sk.Model.P(i).Y)).ToList();
            if (e.Closed && !e.ControlPoints && pts.Count > 2) pts.Add(pts[0]);
            o.AddPolyline(pts, e.ControlPoints ? 0xB06F86A6 : 0x606F86A6, 1.2f);          // the point polygon
            foreach (var i in e.Pts) o.AddDot(sk.Plane.PointAt(sk.Model.P(i).X, sk.Model.P(i).Y), sk.SelPoints.Contains(i) ? 0xFFFFC53D : 0xFFFF8A1F, 11);
        }
        if (_ghost is { } g) o.AddDot(sk.Plane.PointAt(g.u, g.v), 0xFF35B6FF, 10);
    }
}
