using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core.Sketching;
using Prism.Kernel;

namespace Prism.Core;

/// <summary>Where two sketch curves cross: the point and the curve that crosses the one being trimmed.</summary>
public readonly record struct Crossing(double X, double Y, int Cutter);

/// <summary>2D crossing maths for the trim tool. Every curve is reduced to line segments and circular arcs.</summary>
public static class SketchCross
{
    public abstract record Prim(int Ent);
    public sealed record Seg(double Ax, double Ay, double Bx, double By, int Ent) : Prim(Ent);
    public sealed record Arcp(double Cx, double Cy, double R, double A0, double Sweep, int Ent) : Prim(Ent);

    const double Eps = 1e-9;
    static double Norm(double a) { while (a < 0) a += 2 * Math.PI; while (a >= 2 * Math.PI) a -= 2 * Math.PI; return a; }

    /// <summary>Angle from the arc's start (counter-clockwise); NaN when the point is outside the arc's span.</summary>
    static bool InArc(Arcp a, double x, double y)
    {
        if (a.Sweep >= 2 * Math.PI - 1e-9) return true;
        double rel = Norm(Math.Atan2(y - a.Cy, x - a.Cx) - a.A0);
        return rel <= a.Sweep + 1e-7 || rel >= 2 * Math.PI - 1e-7;
    }

    public static List<Prim> Prims(SketchModel m, SkEntity e)
    {
        switch (e.Type)
        {
            case EntType.Line: { var a = m.P(e.A); var b = m.P(e.B); return [new Seg(a.X, a.Y, b.X, b.Y, e.Id)]; }
            case EntType.Circle: { var c = m.P(e.A); return [new Arcp(c.X, c.Y, e.R, 0, 2 * Math.PI, e.Id)]; }
            case EntType.Arc: { var (cx, cy, r, a0, sw) = m.ArcGeometry(e); return [new Arcp(cx, cy, r, a0, sw, e.Id)]; }
            case EntType.Ellipse:
            {
                var pts = Enumerable.Range(0, 129).Select(i => m.EllipsePoint(e, i * 2 * Math.PI / 128)).ToList();
                return Poly(pts, e.Id);
            }
            case EntType.Spline: return Poly(m.SplineSamples(e), e.Id);
            default: return [];
        }
    }

    static List<Prim> Poly(List<(double x, double y)> pts, int id)
    {
        var l = new List<Prim>();
        for (int i = 0; i + 1 < pts.Count; i++) l.Add(new Seg(pts[i].x, pts[i].y, pts[i + 1].x, pts[i + 1].y, id));
        return l;
    }

    public static List<(double x, double y)> Hit(Prim a, Prim b)
    {
        var r = new List<(double, double)>();
        switch (a, b)
        {
            case (Seg s, Seg t): SegSeg(s, t, r); break;
            case (Seg s, Arcp c): SegArc(s, c, r); break;
            case (Arcp c, Seg s): SegArc(s, c, r); break;
            case (Arcp c, Arcp d): ArcArc(c, d, r); break;
        }
        return r;
    }

    static void SegSeg(Seg s, Seg t, List<(double, double)> r)
    {
        double dx = s.Bx - s.Ax, dy = s.By - s.Ay, ex = t.Bx - t.Ax, ey = t.By - t.Ay;
        double den = dx * ey - dy * ex;
        if (Math.Abs(den) < 1e-12) return;
        double u = ((t.Ax - s.Ax) * ey - (t.Ay - s.Ay) * ex) / den, v = ((t.Ax - s.Ax) * dy - (t.Ay - s.Ay) * dx) / den;
        if (u < -1e-7 || u > 1 + 1e-7 || v < -1e-7 || v > 1 + 1e-7) return;
        r.Add((s.Ax + dx * u, s.Ay + dy * u));
    }

    static void SegArc(Seg s, Arcp c, List<(double, double)> r)
    {
        double dx = s.Bx - s.Ax, dy = s.By - s.Ay, fx = s.Ax - c.Cx, fy = s.Ay - c.Cy;
        double a = dx * dx + dy * dy, b = 2 * (fx * dx + fy * dy), k = fx * fx + fy * fy - c.R * c.R;
        if (a < 1e-18) return;
        double disc = b * b - 4 * a * k;
        if (disc < 0) return;
        double sq = Math.Sqrt(disc);
        foreach (var t in disc < 1e-12 ? [-b / (2 * a)] : new[] { (-b - sq) / (2 * a), (-b + sq) / (2 * a) })
        {
            if (t < -1e-7 || t > 1 + 1e-7) continue;
            double x = s.Ax + dx * t, y = s.Ay + dy * t;
            if (InArc(c, x, y)) r.Add((x, y));
        }
    }

    static void ArcArc(Arcp p, Arcp q, List<(double, double)> r)
    {
        double dx = q.Cx - p.Cx, dy = q.Cy - p.Cy, d = Math.Sqrt(dx * dx + dy * dy);
        if (d < Eps || d > p.R + q.R + 1e-7 || d < Math.Abs(p.R - q.R) - 1e-7) return;
        double a = (p.R * p.R - q.R * q.R + d * d) / (2 * d), h2 = p.R * p.R - a * a, h = h2 > 0 ? Math.Sqrt(h2) : 0;
        double mx = p.Cx + dx * a / d, my = p.Cy + dy * a / d;
        foreach (var sgn in h < 1e-9 ? [0.0] : new[] { 1.0, -1.0 })
        {
            double x = mx - dy * h / d * sgn, y = my + dx * h / d * sgn;
            if (InArc(p, x, y) && InArc(q, x, y)) r.Add((x, y));
        }
    }
}

/// <summary>What clicking a curve with the Trim tool would do.</summary>
public sealed class TrimPlan
{
    public int Entity;
    public EntType Type;
    /// <summary>Everything between the crossings on either side of the click goes; missing sides mean "to the end".</summary>
    public Crossing? Lo, Hi;
    public double LoT, HiT;          // parameters of Lo / Hi (line: 0-1, arc: radians from the start, circle: absolute radians)
    public bool DeleteAll;
    public bool Unsupported;
    public double ClickT;
}

public sealed class TrimTool : SketchTool
{
    public override string Name => "Trim";
    public override string Prompt => "Trim: click the part of a line, arc or circle you want to remove. It is cut back to the nearest crossing curves on each side; a curve nothing crosses is deleted.";

    TrimPlan? _hover;

    // ------------------------------------------------------------ planning
    static double Rel(double a, double a0) { double r = a - a0; while (r < 0) r += 2 * Math.PI; while (r >= 2 * Math.PI) r -= 2 * Math.PI; return r; }

    public static TrimPlan? Plan(SketchContext sk, int eid, double u, double v)
    {
        var m = sk.Model; var e = m.EntityOrNull(eid); if (e == null) return null;
        var plan = new TrimPlan { Entity = eid, Type = e.Type };
        if (e.Type is not (EntType.Line or EntType.Circle or EntType.Arc)) { plan.Unsupported = true; return plan; }
        var target = SketchCross.Prims(m, e)[0];
        var hits = new List<(double t, Crossing c)>();
        foreach (var o in m.Entities)
        {
            if (o.Id == eid || o.Type == EntType.Point) continue;
            foreach (var prim in SketchCross.Prims(m, o))
                foreach (var (x, y) in SketchCross.Hit(target, prim))
                {
                    double t = Param(target, x, y);
                    // a crossing at the very end of the curve cannot split it (usually the corner it shares with a neighbour)
                    if (target is SketchCross.Seg && (t < 1e-6 || t > 1 - 1e-6)) continue;
                    if (target is SketchCross.Arcp { Sweep: < 2 * Math.PI - 1e-9 } a && (t < 1e-6 || t > a.Sweep - 1e-6)) continue;
                    if (hits.Any(h => Math.Abs(h.t - t) < 1e-6)) continue;
                    hits.Add((t, new Crossing(x, y, o.Id)));
                }
        }
        var (cu, cv, _) = sk.ClosestOn(e, u, v);
        double tc = Param(target, cu, cv);
        plan.ClickT = tc;
        hits.Sort((a, b) => a.t.CompareTo(b.t));
        bool circle = e.Type == EntType.Circle;
        if (hits.Count == 0 || circle && hits.Count < 2) { plan.DeleteAll = true; return plan; }
        if (circle)
        {
            var lo = hits.LastOrDefault(h => h.t < tc); var hi = hits.FirstOrDefault(h => h.t > tc);
            if (lo.c == default) lo = hits[^1];
            if (hi.c == default) hi = hits[0];
            plan.Lo = lo.c; plan.LoT = lo.t; plan.Hi = hi.c; plan.HiT = hi.t;
            return plan;
        }
        var l = hits.LastOrDefault(h => h.t < tc); var hh = hits.FirstOrDefault(h => h.t > tc);
        if (l.c != default) { plan.Lo = l.c; plan.LoT = l.t; }
        if (hh.c != default) { plan.Hi = hh.c; plan.HiT = hh.t; }
        return plan;
    }

    static double Param(SketchCross.Prim p, double x, double y) => p switch
    {
        SketchCross.Seg s => ((x - s.Ax) * (s.Bx - s.Ax) + (y - s.Ay) * (s.By - s.Ay)) / Math.Max((s.Bx - s.Ax) * (s.Bx - s.Ax) + (s.By - s.Ay) * (s.By - s.Ay), 1e-18),
        SketchCross.Arcp a => a.Sweep >= 2 * Math.PI - 1e-9 ? Rel(Math.Atan2(y - a.Cy, x - a.Cx), 0) : Rel(Math.Atan2(y - a.Cy, x - a.Cx), a.A0),
        _ => 0,
    };

    // ------------------------------------------------------------ applying
    static bool CanAttach(SketchModel m, int cutter) => m.EntityOrNull(cutter)?.Type is EntType.Line or EntType.Circle or EntType.Arc or EntType.Ellipse;

    int NewEnd(SketchModel m, Crossing c)
    {
        int p = m.AddPoint(c.X, c.Y);
        if (CanAttach(m, c.Cutter)) m.Add(CType.PointOnEntity, 0, 0, false, p, c.Cutter);
        return p;
    }

    static void Apply(SketchContext sk, TrimPlan plan)
    {
        var m = sk.Model; var e = m.E(plan.Entity);
        bool horiz = m.Constraints.Any(c => c.Type == CType.Horizontal && c.Refs.Length == 1 && c.Refs[0] == e.Id);
        bool vert = m.Constraints.Any(c => c.Type == CType.Vertical && c.Refs.Length == 1 && c.Refs[0] == e.Id);
        bool cons = e.Construction;
        var tool = new TrimTool();
        if (plan.DeleteAll) { m.RemoveEntity(e.Id); return; }
        switch (e.Type)
        {
            case EntType.Line:
            {
                if (plan.Lo is { } lo) { int q = tool.NewEnd(m, lo); int l = m.AddLine(e.A, q, cons); if (horiz) m.Add(CType.Horizontal, refs: l); if (vert) m.Add(CType.Vertical, refs: l); }
                if (plan.Hi is { } hi) { int q = tool.NewEnd(m, hi); int l = m.AddLine(q, e.B, cons); if (horiz) m.Add(CType.Horizontal, refs: l); if (vert) m.Add(CType.Vertical, refs: l); }
                m.RemoveEntity(e.Id);
                break;
            }
            case EntType.Arc:
            {
                int a = e.A, b = e.B, c = e.C;
                if (plan.Lo is { } lo) { int q = tool.NewEnd(m, lo); m.AddArc(a, b, q, cons); }
                if (plan.Hi is { } hi) { int q = tool.NewEnd(m, hi); m.AddArc(a, q, c, cons); }
                m.RemoveEntity(e.Id);
                break;
            }
            case EntType.Circle:
            {
                // the part that stays runs counter-clockwise from the crossing after the click to the crossing before it
                int a = e.A; int start = tool.NewEnd(m, plan.Hi!.Value), end = tool.NewEnd(m, plan.Lo!.Value);
                m.AddArc(a, start, end, cons);
                m.RemoveEntity(e.Id);
                break;
            }
        }
    }

    /// <summary>Scripted trim: remove the part of curve eid nearest to the sketch point (u,v).</summary>
    public static bool Run(CadSession s, int eid, double u, double v)
    {
        var sk = s.Sketch ?? throw new InvalidOperationException("No sketch is open.");
        var plan = Plan(sk, eid, u, v) ?? throw new ArgumentException($"No curve e{eid}.");
        if (plan.Unsupported) throw new InvalidOperationException("Trim works on lines, arcs and circles.");
        return sk.Edit(s, "That trim", () => { sk.SelEntities.Clear(); sk.SelPoints.Clear(); Apply(sk, plan); });
    }

    // ------------------------------------------------------------ input
    public override bool PointerDown(CadSession s, PointerInfo p)
    {
        if (!p.Left) return false;
        var sk = Sk(s);
        int eid = sk.PickEntity(s, p.X, p.Y, 9);
        if (eid < 0 || sk.PlanePoint(s, p.X, p.Y) is not { } uv) return true;
        var plan = Plan(sk, eid, uv.u, uv.v);
        if (plan == null) return true;
        if (plan.Unsupported) { s.Say("Trim works on lines, arcs and circles. Delete splines and ellipses whole with the Delete key.", true); return true; }
        sk.Edit(s, "That trim", () => { sk.SelEntities.Clear(); sk.SelPoints.Clear(); Apply(sk, plan); });
        _hover = null; sk.HoverEntity = -1;
        s.Invalidate();
        return true;
    }

    public override void PointerMove(CadSession s, PointerInfo p)
    {
        var sk = Sk(s);
        int eid = sk.PickEntity(s, p.X, p.Y, 9);
        _hover = null;
        if (eid >= 0 && sk.PlanePoint(s, p.X, p.Y) is { } uv && Plan(sk, eid, uv.u, uv.v) is { Unsupported: false } plan) _hover = plan;
        sk.HoverEntity = -1; sk.Cursor = null;
        if (sk.PlanePoint(s, p.X, p.Y) is { } w) s.Cursor3 = sk.Plane.PointAt(w.u, w.v);
        s.Invalidate();
    }

    public override bool Key(CadSession s, string key) => false;

    public override void BuildOverlay(CadSession s, Overlay o)
    {
        if (_hover is not { } plan) return;
        var sk = Sk(s); var m = sk.Model; var e = m.EntityOrNull(plan.Entity); if (e == null) return;
        const uint red = 0xFFFF5A5F;
        var pts = new List<Vec3>();
        switch (e.Type)
        {
            case EntType.Line:
            {
                var a = m.P(e.A); var b = m.P(e.B);
                (double x, double y) At(double t) => (a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
                var p0 = plan.Lo is { } lo ? (lo.X, lo.Y) : (a.X, a.Y); var p1 = plan.Hi is { } hi ? (hi.X, hi.Y) : (b.X, b.Y);
                if (plan.DeleteAll) { p0 = (a.X, a.Y); p1 = (b.X, b.Y); }
                pts.Add(W(sk, p0.Item1, p0.Item2)); pts.Add(W(sk, p1.Item1, p1.Item2));
                break;
            }
            case EntType.Arc or EntType.Circle:
            {
                double cx, cy, r, a0, sw;
                if (e.Type == EntType.Circle) { var c = m.P(e.A); cx = c.X; cy = c.Y; r = e.R; a0 = 0; sw = 2 * Math.PI; }
                else (cx, cy, r, a0, sw) = m.ArcGeometry(e);
                double from, to;
                if (plan.DeleteAll) { from = 0; to = sw; }
                else if (e.Type == EntType.Circle) { from = plan.LoT; to = plan.HiT; if (to <= from) to += 2 * Math.PI; }
                else { from = plan.Lo != null ? plan.LoT : 0; to = plan.Hi != null ? plan.HiT : sw; }
                int n = Math.Max(6, (int)((to - from) / (2 * Math.PI) * 96));
                for (int i = 0; i <= n; i++) { double ang = a0 + from + (to - from) * i / n; pts.Add(W(sk, cx + r * Math.Cos(ang), cy + r * Math.Sin(ang))); }
                break;
            }
        }
        if (pts.Count > 1) o.AddPolyline(pts, red, 4.5f);
        foreach (var c in new[] { plan.Lo, plan.Hi }) if (c is { } cc && !plan.DeleteAll) o.AddDot(W(sk, cc.X, cc.Y), red, 8);
    }
}
