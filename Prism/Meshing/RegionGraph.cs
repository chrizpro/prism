using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Kernel;

namespace Prism.Meshing;

/// <summary>An ordered polyline along the border of a region. Neighbour: region index across it, -1 free-form/unassigned, -2 open mesh edge.</summary>
public sealed record BoundaryLoop(List<Vec3> Points, bool Closed, int Neighbour = -1);

/// <summary>Two regions that touch, with their shared border polylines.</summary>
public sealed class RegionEdge
{
    public int A { get; init; }
    public int B { get; init; }
    public double Length { get; init; }
    public List<BoundaryLoop> Borders { get; init; } = [];
    /// <summary>Average angle between the triangle normals either side of the border (0 = tangent, large = sharp crease).</summary>
    public double MeanAngleDeg { get; init; }
}

public enum CurveKind { Line, Circle, Polyline }

/// <summary>Where two fitted surfaces meet along a border. Exact = computed in closed form (line, circle); otherwise the border was snapped onto both surfaces numerically.</summary>
public sealed record IntersectionCurve(CurveKind Kind, List<Vec3> Points, Vec3 Origin, Vec3 Direction, double Radius, bool Exact, double MaxDev, bool Closed);

/// <summary>Region adjacency, boundary loops, analytic intersection edges and same-primitive merging for a segmented mesh.</summary>
public sealed class RegionGraph
{
    public TriMesh Mesh { get; }
    public IReadOnlyList<FitRegion> Regions { get; }
    /// <summary>Region index per triangle, -1 when unassigned.</summary>
    public int[] Label { get; }
    public List<RegionEdge> Edges { get; } = [];
    readonly List<(int a, int b)>[] _bnd;
    readonly List<int>[] _bndNb;

    RegionGraph(TriMesh m, IReadOnlyList<FitRegion> regions)
    {
        Mesh = m; Regions = regions; Label = new int[m.TriCount]; Array.Fill(Label, -1);
        for (int i = 0; i < regions.Count; i++) foreach (int t in regions[i].Tris) Label[t] = i;
        _bnd = new List<(int, int)>[regions.Count].Select(_ => new List<(int, int)>()).ToArray();
        _bndNb = new List<int>[regions.Count].Select(_ => new List<int>()).ToArray();
    }

    public static RegionGraph Build(TriMesh m, IReadOnlyList<FitRegion> regions)
    {
        var g = new RegionGraph(m, regions); var nb = m.Topology().Neighbors;
        var pair = new Dictionary<(int, int), (List<(int, int)> segs, double len, double ang, int n)>();
        for (int t = 0; t < m.TriCount; t++)
        {
            int la = g.Label[t]; if (la < 0) continue;
            for (int k = 0; k < 3; k++)
            {
                int n2 = nb[3 * t + k]; int lb = n2 < 0 ? -2 : g.Label[n2];
                if (lb == la) continue;
                int v0 = m.T[3 * t + k], v1 = m.T[3 * t + (k + 1) % 3];
                g._bnd[la].Add((v0, v1)); g._bndNb[la].Add(lb);
                if (lb >= 0 && la < lb)
                {
                    var key = (la, lb); if (!pair.TryGetValue(key, out var e)) e = ([], 0, 0, 0);
                    e.segs.Add((v0, v1));
                    double ang = Math.Acos(Math.Clamp(Vec3.Dot(m.FaceNormal(t), m.FaceNormal(n2)), -1, 1)) * 180 / Math.PI;
                    pair[key] = (e.segs, e.len + (m.P(v0) - m.P(v1)).Length, e.ang + ang, e.n + 1);
                }
            }
        }
        foreach (var ((a, b), e) in pair.OrderBy(p => p.Key))
            g.Edges.Add(new RegionEdge { A = a, B = b, Length = e.len, MeanAngleDeg = e.ang / Math.Max(e.n, 1), Borders = Chain(m, e.segs).Select(c => new BoundaryLoop(c.pts, c.closed, b)).ToList() });
        return g;
    }

    public IEnumerable<int> NeighboursOf(int region) => Edges.Where(e => e.A == region || e.B == region).Select(e => e.A == region ? e.B : e.A);
    public RegionEdge? EdgeBetween(int a, int b) => Edges.FirstOrDefault(e => (e.A == a && e.B == b) || (e.A == b && e.B == a));

    /// <summary>All borders of a region, ordered and oriented counter-clockwise about its outward normal. Outer boundary plus holes.</summary>
    public List<BoundaryLoop> BoundaryLoops(int region)
    {
        var res = new List<BoundaryLoop>();
        foreach (var c in Chain(Mesh, _bnd[region]))
            res.Add(new BoundaryLoop(c.pts, c.closed, c.pts.Count > 0 ? NeighbourOfChain(region, c.ids) : -1));
        return res;
    }

    int NeighbourOfChain(int region, List<int> ids)
    {
        var votes = new Dictionary<int, int>(); var set = ids.Count >= 2 ? (a: ids[0], b: ids[1]) : (a: -1, b: -1);
        for (int i = 0; i + 1 < ids.Count; i++)
            for (int j = 0; j < _bnd[region].Count; j++)
                if (_bnd[region][j] == (ids[i], ids[i + 1])) { votes[_bndNb[region][j]] = votes.GetValueOrDefault(_bndNb[region][j]) + 1; break; }
        return votes.Count == 0 ? -1 : votes.OrderByDescending(v => v.Value).First().Key;
    }

    /// <summary>Chains directed edges into ordered polylines. Open chains start where nothing leads in; the rest are loops.</summary>
    internal static List<(List<Vec3> pts, bool closed, List<int> ids)> Chain(TriMesh m, List<(int a, int b)> segs)
    {
        var outs = new Dictionary<int, List<int>>(); var indeg = new Dictionary<int, int>();
        for (int i = 0; i < segs.Count; i++)
        {
            if (!outs.TryGetValue(segs[i].a, out var l)) outs[segs[i].a] = l = []; l.Add(i);
            indeg[segs[i].b] = indeg.GetValueOrDefault(segs[i].b) + 1;
        }
        var used = new bool[segs.Count]; var res = new List<(List<Vec3>, bool, List<int>)>();
        void Walk(int first)
        {
            var ids = new List<int> { segs[first].a, segs[first].b }; used[first] = true; int cur = segs[first].b; bool closed = false;
            while (true)
            {
                if (cur == ids[0]) { closed = true; break; }
                if (!outs.TryGetValue(cur, out var l)) break;
                int nxt = l.FirstOrDefault(i => !used[i], -1); if (nxt < 0) break;
                used[nxt] = true; cur = segs[nxt].b; ids.Add(cur);
            }
            if (closed) ids.RemoveAt(ids.Count - 1);
            res.Add((ids.Select(m.P).ToList(), closed, ids));
        }
        for (int i = 0; i < segs.Count; i++) if (!used[i] && indeg.GetValueOrDefault(segs[i].a) == 0) Walk(i);
        for (int i = 0; i < segs.Count; i++) if (!used[i]) Walk(i);
        return res.OrderByDescending(r => r.Item1.Count).ToList();
    }

    // ------------------------------------------------------------------------------------------ intersections
    /// <summary>
    /// The curve where regions a and b meet along one of their shared borders. Plane/plane gives a line, a plane square to (or any plane through)
    /// a cylinder, cone, sphere or torus gives a circle, a plane parallel to a cylinder axis gives a line; everything else is the border snapped
    /// onto both surfaces by alternating projection (Exact = false).
    /// </summary>
    public IntersectionCurve? Intersect(RegionEdge e, int borderIndex = 0, double tol = 0.05)
    {
        if (borderIndex >= e.Borders.Count) return null;
        var border = e.Borders[borderIndex]; var ra = Regions[e.A]; var rb = Regions[e.B];
        if (border.Points.Count < 2) return null;
        var pts = border.Points;
        if (ra.Kind != PrimKind.Plane && rb.Kind == PrimKind.Plane) (ra, rb) = (rb, ra);
        IntersectionCurve? exact = null;
        if (ra.Kind == PrimKind.Plane) exact = IntersectWithPlane(ra, rb, pts, border.Closed, tol);
        if (exact != null) return Finish(exact, Regions[e.A], Regions[e.B]);
        // numeric: alternate projections
        var snapped = new List<Vec3>(pts.Count);
        foreach (var p0 in pts)
        {
            var p = p0;
            for (int it = 0; it < 10; it++) { p = SurfaceMath.Closest(Regions[e.A], p); p = SurfaceMath.Closest(Regions[e.B], p); }
            snapped.Add(p);
        }
        var s2 = Poly2Simplify(snapped, tol * 0.2, border.Closed);
        return Finish(new IntersectionCurve(CurveKind.Polyline, s2, Vec3.Zero, Vec3.Zero, 0, false, 0, border.Closed), Regions[e.A], Regions[e.B]);
    }

    static IntersectionCurve Finish(IntersectionCurve c, FitRegion a, FitRegion b)
    {
        double dev = 0; foreach (var p in c.Points) dev = Math.Max(dev, Math.Max(SurfaceMath.Distance(a, p), SurfaceMath.Distance(b, p)));
        return c with { MaxDev = dev };
    }

    static List<Vec3> Poly2Simplify(List<Vec3> p, double tol, bool closed)
    {
        if (p.Count < 4) return p;
        // simplify in 3D by distance to chord (Douglas-Peucker)
        var keep = new bool[p.Count]; keep[0] = keep[^1] = true;
        var st = new Stack<(int, int)>(); st.Push((0, p.Count - 1));
        while (st.Count > 0)
        {
            var (a, b) = st.Pop(); var ab = p[b] - p[a]; double l2 = Vec3.Dot(ab, ab), best = 0; int idx = -1;
            for (int i = a + 1; i < b; i++) { double t = l2 < 1e-18 ? 0 : Math.Clamp(Vec3.Dot(p[i] - p[a], ab) / l2, 0, 1); double d = (p[i] - (p[a] + ab * t)).Length; if (d > best) { best = d; idx = i; } }
            if (idx >= 0 && best > tol) { keep[idx] = true; st.Push((a, idx)); st.Push((idx, b)); }
        }
        return p.Where((_, i) => keep[i]).ToList();
    }

    static IntersectionCurve? IntersectWithPlane(FitRegion pl, FitRegion o, List<Vec3> border, bool closed, double tol)
    {
        var n = pl.Axis; var po = pl.Origin;
        IntersectionCurve Line(Vec3 p0, Vec3 d)
        {
            d = d.Normalized(); double t0 = border.Min(q => Vec3.Dot(q - p0, d)), t1 = border.Max(q => Vec3.Dot(q - p0, d));
            return new IntersectionCurve(CurveKind.Line, [p0 + d * t0, p0 + d * t1], p0, d, 0, true, 0, false);
        }
        IntersectionCurve? CircleAt(Vec3 c, double rho)
        {
            if (rho < 1e-9) return null;
            var (u, v) = SurfaceMath.Basis(n); double prev = 0, total = 0, first = 0, mn = 0, mx = 0;
            for (int i = 0; i < border.Count; i++)
            {
                var d = border[i] - c; double a = Math.Atan2(Vec3.Dot(d, v), Vec3.Dot(d, u));
                if (i == 0) { first = a; prev = a; }
                else { double da = a - prev; while (da > Math.PI) da -= 2 * Math.PI; while (da < -Math.PI) da += 2 * Math.PI; total += da; prev = a; }
                mn = Math.Min(mn, total); mx = Math.Max(mx, total);
            }
            bool full = closed || mx - mn > 2 * Math.PI * 0.98;
            double a0 = full ? 0 : first + mn, a1 = full ? 2 * Math.PI : first + mx;
            int segs = Math.Clamp((int)Math.Ceiling((a1 - a0) / (2 * Math.Acos(Math.Clamp(1 - Math.Max(tol * 0.25, 1e-6) / rho, -1, 1)) + 1e-9)), 8, 720);
            var pts = new List<Vec3>();
            for (int i = 0; i <= segs; i++) { double a = a0 + (a1 - a0) * i / segs; pts.Add(c + u * (rho * Math.Cos(a)) + v * (rho * Math.Sin(a))); }
            return new IntersectionCurve(CurveKind.Circle, pts, c, n, rho, true, 0, full);
        }
        double bandMean(Vec3 c) => border.Average(q => { var d = q - c; return (d - n * Vec3.Dot(d, n)).Length; });

        switch (o.Kind)
        {
            case PrimKind.Plane:
            {
                var d = Vec3.Cross(n, o.Axis); double l2 = Vec3.Dot(d, d); if (l2 < 1e-6) return null;
                double d1 = Vec3.Dot(n, po), d2 = Vec3.Dot(o.Axis, o.Origin);
                var p0 = (Vec3.Cross(o.Axis, d) * d1 + Vec3.Cross(d, n) * d2) / l2;
                return Line(p0, d);
            }
            case PrimKind.Sphere:
            {
                double dist = Vec3.Dot(o.Origin - po, n); double r2 = o.Radius * o.Radius - dist * dist; if (r2 <= 0) return null;
                return CircleAt(o.Origin - n * dist, Math.Sqrt(r2));
            }
            case PrimKind.Cylinder:
            {
                double dn = Vec3.Dot(o.Axis, n);
                if (Math.Abs(dn) > 0.9998) { var c = o.Origin + o.Axis * (Vec3.Dot(po - o.Origin, n) / dn); return CircleAt(c, o.Radius); }
                if (Math.Abs(dn) < 0.0175)
                {
                    double dist = Vec3.Dot(o.Origin - po, n); if (Math.Abs(dist) >= o.Radius) return null;
                    var side = Vec3.Cross(o.Axis, n).Normalized(); double off = Math.Sqrt(o.Radius * o.Radius - dist * dist);
                    var foot = o.Origin - n * dist;
                    var c1 = foot + side * off; var c2 = foot - side * off;
                    var pick = border.Average(q => (q - c1 - o.Axis * Vec3.Dot(q - c1, o.Axis)).Length) <= border.Average(q => (q - c2 - o.Axis * Vec3.Dot(q - c2, o.Axis)).Length) ? c1 : c2;
                    return Line(pick, o.Axis);
                }
                return null;
            }
            case PrimKind.Cone:
            {
                double dn = Vec3.Dot(o.Axis, n); if (Math.Abs(dn) < 0.9998) return null;
                double t = Vec3.Dot(po - o.Origin, n) / dn; if (t <= 0) return null;
                return CircleAt(o.Origin + o.Axis * t, t * Math.Tan(o.HalfAngle * Math.PI / 180));
            }
            case PrimKind.Torus:
            {
                double dn = Vec3.Dot(o.Axis, n); if (Math.Abs(dn) < 0.9998) return null;
                double t = Vec3.Dot(po - o.Origin, n) / dn; if (Math.Abs(t) >= o.MinorRadius) return null;
                var c = o.Origin + o.Axis * t; double w = Math.Sqrt(o.MinorRadius * o.MinorRadius - t * t);
                double m = bandMean(c); double r1 = o.Radius + w, r2 = o.Radius - w;
                return CircleAt(c, Math.Abs(m - r1) <= Math.Abs(m - r2) ? r1 : r2);
            }
            default: return null;
        }
    }

    // ------------------------------------------------------------------------------------------ merging
    static bool Analytic(PrimKind k) => k is PrimKind.Plane or PrimKind.Cylinder or PrimKind.Sphere or PrimKind.Cone or PrimKind.Torus;

    static double RmsTo(TriMesh m, FitRegion surf, FitRegion tris, double cosAngle)
    {
        int stride = Math.Max(1, tris.Tris.Length / 300); double se = 0; int n = 0; int badN = 0;
        for (int i = 0; i < tris.Tris.Length; i += stride)
        {
            int t = tris.Tris[i]; var c = (m.P(m.T[3 * t]) + m.P(m.T[3 * t + 1]) + m.P(m.T[3 * t + 2])) / 3;
            double d = SurfaceMath.Distance(surf, c); se += d * d; n++;
            if (SurfaceMath.Normal(surf, c) is { } sn && Math.Abs(Vec3.Dot(sn, m.FaceNormal(t))) < cosAngle) badN++;
        }
        if (n == 0) return double.MaxValue;
        return badN > n * 0.1 ? double.MaxValue : Math.Sqrt(se / n);
    }

    /// <summary>Do two regions describe the same surface (same kind, each region's triangles within tol of the other's surface)?</summary>
    public static bool SameSurface(TriMesh m, FitRegion a, FitRegion b, double tol, double angleDeg)
    {
        if (a.Kind != b.Kind || !Analytic(a.Kind)) return false;
        double cos = Math.Cos(angleDeg * Math.PI / 180);
        if (a.Kind == PrimKind.Plane && Math.Abs(Vec3.Dot(a.Axis, b.Axis)) < cos) return false;
        if (a.Kind is PrimKind.Cylinder or PrimKind.Cone && Math.Abs(Vec3.Dot(a.Axis, b.Axis)) < cos) return false;
        return RmsTo(m, a, b, cos) <= tol && RmsTo(m, b, a, cos) <= tol;
    }

    /// <summary>Repeatedly merges touching regions that fit the same primitive within tolerance (e.g. one cylinder split in two by noise).</summary>
    public static List<FitRegion> MergeSame(TriMesh m, IReadOnlyList<FitRegion> regions, double tol, double angleDeg = 10)
    {
        var list = regions.ToList();
        for (int guard = 0; guard < 200; guard++)
        {
            var g = Build(m, list); bool merged = false;
            foreach (var e in g.Edges.OrderByDescending(e => e.Length))
            {
                var a = list[e.A]; var b = list[e.B];
                if (!SameSurface(m, a, b, tol, angleDeg)) continue;
                var big = a.Area >= b.Area ? a : b;
                var tris = a.Tris.Concat(b.Tris).ToArray();
                var next = PrimitiveFit2.Finish(m, big, tris);
                list[e.A] = next; list.RemoveAt(e.B); merged = true; break;
            }
            if (!merged) break;
        }
        return list.OrderByDescending(r => r.Area).ToList();
    }

    // ------------------------------------------------------------------------------------------ report
    public sealed record RegionReport(int Index, string Name, int Triangles, double Area, double Rms, double Max, double Mean, double PercentInTolerance);

    /// <summary>Vertex-to-surface distance statistics per region against its own fitted surface (pure C#, no kernel).</summary>
    public static List<RegionReport> Report(TriMesh m, IReadOnlyList<FitRegion> regions, double tol)
    {
        var res = new List<RegionReport>();
        for (int i = 0; i < regions.Count; i++)
        {
            var r = regions[i]; var vs = new HashSet<int>(); foreach (int t in r.Tris) { vs.Add(m.T[3 * t]); vs.Add(m.T[3 * t + 1]); vs.Add(m.T[3 * t + 2]); }
            int stride = Math.Max(1, vs.Count / 1500); double se = 0, sum = 0, mx = 0; int n = 0, within = 0, k = 0;
            foreach (int v in vs.OrderBy(x => x)) { if (k++ % stride != 0) continue; double d = SurfaceMath.Distance(r, m.P(v)); se += d * d; sum += d; mx = Math.Max(mx, d); n++; if (d <= tol) within++; }
            res.Add(new RegionReport(i, r.Describe(), r.Tris.Length, r.Area, Math.Sqrt(se / Math.Max(n, 1)), mx, sum / Math.Max(n, 1), 100.0 * within / Math.Max(n, 1)));
        }
        return res;
    }
}
