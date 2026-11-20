using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core.Sketching;
using Prism.Kernel;

namespace Prism.Core;

/// <summary>
/// Adds a point to a curve that already exists: a line becomes two lines, an arc two arcs, a circle two arcs (the clicked point and its
/// opposite), and a spline gains a point where it was clicked.
/// </summary>
public static class InsertOps
{
    /// <summary>Which curves can take an inserted point.</summary>
    public static bool CanInsert(SkEntity e) => e.Type is EntType.Line or EntType.Arc or EntType.Circle or EntType.Spline;

    static double Angle(double cx, double cy, double u, double v) => Math.Atan2(v - cy, u - cx);
    static double Turn(double a) { while (a < 0) a += 2 * Math.PI; while (a >= 2 * Math.PI) a -= 2 * Math.PI; return a; }

    /// <summary>Splits the curve at (u, v) in the sketch plane. Returns the id of the new point, or -1 when the curve cannot be split there.</summary>
    public static int Insert(SketchModel m, int entityId, double u, double v, (int index, Vec3 point)? splineSpot = null)
    {
        var e = m.EntityOrNull(entityId);
        if (e == null) return -1;
        switch (e.Type)
        {
            case EntType.Line: return SplitLine(m, e, u, v);
            case EntType.Arc: return SplitArc(m, e, u, v);
            case EntType.Circle: return SplitCircle(m, e, u, v);
            case EntType.Spline:
            {
                if (splineSpot is { } spot)
                {
                    int made = m.AddPoint(spot.point.X, spot.point.Y); m.P(made).Z = spot.point.Z;
                    e.Pts.Insert(Math.Min(spot.index, e.Pts.Count), made);
                    return made;
                }
                return SplineOps.Insert(m, e, u, v);
            }
            default: return -1;
        }
    }

    static void Copy(SkEntity from, SkEntity to) { to.Group = from.Group; }

    static int SplitLine(SketchModel m, SkEntity e, double u, double v)
    {
        var a = m.P(e.A); var b = m.P(e.B);
        double dx = b.X - a.X, dy = b.Y - a.Y, l2 = dx * dx + dy * dy;
        if (l2 < 1e-12) return -1;
        double t = Math.Clamp(((u - a.X) * dx + (v - a.Y) * dy) / l2, 0.01, 0.99);
        var constraints = m.Constraints.Where(c => !c.Hidden && c.Type is CType.Horizontal or CType.Vertical && c.Refs.Length == 1 && c.Refs[0] == e.Id).Select(c => c.Type).ToList();
        int made = m.AddPoint(a.X + dx * t, a.Y + dy * t);
        m.DropEntityConstraints(e.Id);                        // a length or tangency of the whole line no longer means the same thing
        int end = e.B; e.B = made;
        int second = m.AddLine(made, end, e.Construction); Copy(e, m.E(second));
        m.Add(CType.Collinear, 0, 0, false, e.Id, second);    // the two halves stay one straight line
        foreach (var kind in constraints) { m.Add(kind, 0, 0, false, e.Id); m.Add(kind, 0, 0, false, second); }
        return made;
    }

    static int SplitArc(SketchModel m, SkEntity e, double u, double v)
    {
        var c = m.P(e.A); var s = m.P(e.B); var f = m.P(e.C);
        double r = Math.Sqrt((s.X - c.X) * (s.X - c.X) + (s.Y - c.Y) * (s.Y - c.Y));
        double a0 = Angle(c.X, c.Y, s.X, s.Y), sweep = Turn(Angle(c.X, c.Y, f.X, f.Y) - a0);
        if (r < 1e-9 || sweep < 1e-6) return -1;
        double t = Turn(Angle(c.X, c.Y, u, v) - a0);
        if (t > sweep) t = t - sweep < 2 * Math.PI - t ? sweep : 0;          // a click beyond an end belongs to the nearer end
        t = Math.Clamp(t, sweep * 0.01, sweep * 0.99);
        int made = m.AddPoint(c.X + r * Math.Cos(a0 + t), c.Y + r * Math.Sin(a0 + t));
        m.DropEntityConstraints(e.Id);
        int end = e.C; e.C = made;
        int second = m.AddArc(e.A, made, end, e.Construction); Copy(e, m.E(second));
        return made;
    }

    static int SplitCircle(SketchModel m, SkEntity e, double u, double v)
    {
        var c = m.P(e.A); double r = e.R;
        if (r < 1e-9) return -1;
        double at = Angle(c.X, c.Y, u, v);
        int first = m.AddPoint(c.X + r * Math.Cos(at), c.Y + r * Math.Sin(at));
        int opposite = m.AddPoint(c.X - r * Math.Cos(at), c.Y - r * Math.Sin(at));
        m.DropEntityConstraints(e.Id);
        m.Entities.Remove(e);
        Copy(e, m.E(m.AddArc(e.A, first, opposite, e.Construction)));
        Copy(e, m.E(m.AddArc(e.A, opposite, first, e.Construction)));
        return first;
    }
}

/// <summary>Click a curve to add a point there. See <see cref="InsertOps"/> for what each curve becomes.</summary>
public sealed class InsertPointTool : SketchTool
{
    public override string Name => "Insert point";
    public override string Prompt => "Insert point: click a line, arc, circle or spline to add a point there. A line becomes two lines, an arc two arcs, a circle two arcs; a spline gains a point. Esc finishes.";

    public override bool PointerDown(CadSession s, PointerInfo p)
    {
        if (!p.Left) return false;
        var sk = Sk(s);
        int eid = sk.PickEntity(s, p.X, p.Y, 9);
        if (eid < 0 || sk.Model.EntityOrNull(eid) is not { } e || !InsertOps.CanInsert(e)) { s.Say("Click a line, arc, circle or spline to add a point.", true); return true; }
        if (sk.PlanePoint(s, p.X, p.Y) is not { } uv) return true;
        if (Snap(s, sk, e, p.X, p.Y) is { } snapped) uv = snapped;
        (int index, Vec3 point)? spot = e.Type == EntType.Spline ? SplineOps.ScreenInsertSpot(s, sk, e, p.X, p.Y) : null;
        int made = -1;
        if (sk.Edit(s, "Inserting that point", () => made = InsertOps.Insert(sk.Model, eid, uv.u, uv.v, spot)) && made >= 0)
        {
            sk.SelEntities.Clear(); sk.SelPoints.Clear(); sk.SelPoints.Add(made);
            s.Say("Point added. Click another curve, or press Esc to finish.");
        }
        s.NotifySketchChanged(); s.Invalidate();
        return true;
    }

    (double u, double v)? _snap;

    /// <summary>
    /// The point on the curve a click near (px, py) should take: the curve's midpoint or a place another curve crosses it, when one is within
    /// 10 pixels on screen. Null means "exactly where clicked".
    /// </summary>
    public static (double u, double v)? Snap(CadSession s, SketchContext sk, SkEntity e, double px, double py)
    {
        var m = sk.Model; var spots = new List<(double u, double v)>();
        if (e.Type == EntType.Line) { var a = m.P(e.A); var b = m.P(e.B); spots.Add(((a.X + b.X) / 2, (a.Y + b.Y) / 2)); }
        else if (e.Type == EntType.Arc) { var (cx, cy, r, a0, sw) = m.ArcGeometry(e); spots.Add((cx + r * Math.Cos(a0 + sw / 2), cy + r * Math.Sin(a0 + sw / 2))); }
        foreach (var mine in SketchCross.Prims(m, e))
            foreach (var o in m.Entities)
            {
                if (o.Id == e.Id || o.Type == EntType.Point) continue;
                foreach (var prim in SketchCross.Prims(m, o)) spots.AddRange(SketchCross.Hit(mine, prim));
            }
        (double u, double v)? best = null; double bestD = 10;
        foreach (var (u, v) in spots)
        {
            if (s.Camera.Project(sk.Plane.PointAt(u, v)) is not { } at) continue;
            double d = Math.Sqrt((at.x - px) * (at.x - px) + (at.y - py) * (at.y - py));
            if (d < bestD) { bestD = d; best = (u, v); }
        }
        return best;
    }

    public override void PointerMove(CadSession s, PointerInfo p)
    {
        var sk = Sk(s);
        int he = sk.PickEntity(s, p.X, p.Y, 9);
        sk.HoverEntity = he >= 0 && sk.Model.EntityOrNull(he) is { } e && InsertOps.CanInsert(e) ? he : -1;
        _snap = sk.HoverEntity >= 0 ? Snap(s, sk, sk.Model.E(sk.HoverEntity), p.X, p.Y) : null;
        sk.HoverPoint = -1; sk.Cursor = null;
        if (sk.PlanePoint(s, p.X, p.Y) is { } c) s.Cursor3 = sk.Plane.PointAt(c.u, c.v);
        s.Invalidate();
    }

    public override void BuildOverlay(CadSession s, Overlay o)
    {
        if (_snap is { } at && s.Sketch is { } sk) o.AddDot(sk.Plane.PointAt(at.u, at.v), 0xFF2BC4D1, 9);
    }
}
