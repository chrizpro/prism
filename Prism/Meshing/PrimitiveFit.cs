using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Kernel;

namespace Prism.Meshing;

public enum PrimKind { Plane, Cylinder, Sphere, Cone }

/// <summary>A group of triangles that fits one analytic surface: the basis for rebuilding a scan as CAD.</summary>
public sealed class FitRegion
{
    public PrimKind Kind { get; init; }
    public int[] Tris { get; init; } = [];
    /// <summary>Plane: a point on it. Cylinder/cone: a point on the axis. Sphere: the centre.</summary>
    public Vec3 Origin { get; init; }
    /// <summary>Plane: the unit normal. Cylinder/cone: the axis direction.</summary>
    public Vec3 Axis { get; init; }
    public double Radius { get; init; }
    /// <summary>Cone half-angle in degrees.</summary>
    public double HalfAngle { get; init; }
    /// <summary>Extent along the axis (cylinder/cone) measured from Origin.</summary>
    public double T0 { get; init; }
    public double T1 { get; init; }
    public double Rms { get; init; }
    public double Area { get; init; }
    /// <summary>True when the surface faces away from its axis/centre (a boss or ball); false for holes and sockets.</summary>
    public bool Convex { get; init; }
    public Vec3 Min { get; init; }
    public Vec3 Max { get; init; }
    public string Describe() => Kind switch
    {
        PrimKind.Plane => "Plane",
        PrimKind.Cylinder => $"{(Convex ? "Cylinder" : "Hole")}  Ø{2 * Radius:0.###}",
        PrimKind.Sphere => $"{(Convex ? "Sphere" : "Spherical socket")}  Ø{2 * Radius:0.###}",
        _ => $"Cone  {HalfAngle:0.#}°",
    };
}

/// <summary>Finds planes, cylinders, spheres and cones in a mesh by RANSAC on triangle positions and normals, then refits each by least squares.</summary>
public static class PrimitiveFit
{
    public sealed record Options(double Tolerance, double AngleDeg, int MinTris, bool Planes = true, bool Cylinders = true, bool Spheres = true, bool Cones = true, int MaxRegions = 40, int Seed = 7);

    internal sealed class Ctx
    {
        public TriMesh M = null!; public Vec3[] C = []; public Vec3[] N = []; public double[] A = []; public int[] Nb = []; public bool[] Sliver = []; public int[] VStart = [], VTri = []; public Vec3[] SN = [];
        public double Eps, CosTol, SinTol;
    }

    public static Action<string>? Log;

    /// <summary>Per-mesh data reused between queries (triangle centres, normals, adjacency).</summary>
    public sealed class Prepared { internal Ctx C = null!; internal Options O = null!; }

    public static Prepared Prepare(TriMesh m, Options o) => new() { C = BuildCtx(m, o), O = o };

    static Ctx BuildCtx(TriMesh m, Options o)
    {
        int nt = m.TriCount;
        var cx = new Ctx { M = m, C = new Vec3[nt], N = new Vec3[nt], A = new double[nt], Sliver = new bool[nt], Eps = o.Tolerance, CosTol = Math.Cos(o.AngleDeg * Math.PI / 180), SinTol = Math.Sin(o.AngleDeg * Math.PI / 180) };
        for (int t = 0; t < nt; t++)
        {
            cx.C[t] = (m.P(m.T[3 * t]) + m.P(m.T[3 * t + 1]) + m.P(m.T[3 * t + 2])) / 3; cx.A[t] = m.TriArea(t);
            cx.N[t] = new Vec3(m.FaceNormals[3 * t], m.FaceNormals[3 * t + 1], m.FaceNormals[3 * t + 2]);
            // long thin triangles have unreliable normals (a little noise swings them), so they are judged by their corners instead
            double e0 = m.P(m.T[3 * t]).DistanceTo(m.P(m.T[3 * t + 1])), e1 = m.P(m.T[3 * t + 1]).DistanceTo(m.P(m.T[3 * t + 2])), e2 = m.P(m.T[3 * t + 2]).DistanceTo(m.P(m.T[3 * t]));
            double emax = Math.Max(e0, Math.Max(e1, e2));
            cx.Sliver[t] = emax > 1e-12 && cx.A[t] / (emax * emax) < 0.08;
        }
        cx.Nb = m.Topology().Neighbors;
        // triangles around each vertex: regions grow across shared vertices so one noisy triangle does not cut a surface in two
        cx.VStart = new int[m.VertexCount + 1];
        for (int i = 0; i < m.T.Length; i++) cx.VStart[m.T[i] + 1]++;
        for (int i = 0; i < m.VertexCount; i++) cx.VStart[i + 1] += cx.VStart[i];
        var fillPos = (int[])cx.VStart.Clone(); cx.VTri = new int[m.T.Length];
        for (int t = 0; t < nt; t++) for (int k = 0; k < 3; k++) cx.VTri[fillPos[m.T[3 * t + k]]++] = t;
        // edge-preserving smoothed normals: average with neighbours that point roughly the same way, never across a crease
        cx.SN = new Vec3[nt];
        for (int t = 0; t < nt; t++)
        {
            Vec3 acc = Vec3.Zero;
            for (int k = 0; k < 3; k++)
                for (int j = cx.VStart[m.T[3 * t + k]]; j < cx.VStart[m.T[3 * t + k] + 1]; j++)
                {
                    int u = cx.VTri[j];
                    if (Vec3.Dot(cx.N[u], cx.N[t]) >= 0.5) acc += cx.N[u] * Math.Max(cx.A[u], 1e-12);
                }
            cx.SN[t] = acc.Length > 1e-12 ? acc.Normalized() : cx.N[t];
        }
        return cx;
    }

    public static List<FitRegion> Detect(TriMesh m, Options o)
    {
        int nt = m.TriCount;
        var cx = BuildCtx(m, o);
        var free = Enumerable.Range(0, nt).Where(t => cx.A[t] > 1e-12).ToList();
        var assigned = new bool[nt];
        var rng = new Random(o.Seed);
        var regions = new List<FitRegion>();
        double totalArea = free.Sum(t => cx.A[t]);
        int misses = 0;
        while (regions.Count < o.MaxRegions && misses < 4)
        {
            var pool = free.Where(t => !assigned[t]).ToList();
            if (pool.Count < 3) break;
            var cum = new double[pool.Count]; double acc = 0;
            for (int i = 0; i < pool.Count; i++) { acc += cx.A[pool[i]]; cum[i] = acc; }
            if (acc < totalArea * 0.02) break;                       // what is left is free-form
            int AreaPick() { double r = rng.NextDouble() * acc; int k = Array.BinarySearch(cum, r); if (k < 0) k = ~k; return pool[Math.Min(k, pool.Count - 1)]; }
            // scoring sample: half proportional to area (big flat faces), half uniform (many small curved facets)
            var sa = Enumerable.Range(0, 2500).Select(_ => AreaPick()).ToList();
            var su = Enumerable.Range(0, 2500).Select(_ => pool[rng.Next(pool.Count)]).ToList();
            (PrimKind kind, Vec3 o0, Vec3 ax, double r, double half)? best = null; double bestScore = 0;
            for (int it = 0; it < 320; it++)
            {
                // a seed triangle and two more from its neighbourhood: curved surfaces need normals that are near each other
                int a = it % 2 == 0 ? AreaPick() : pool[rng.Next(pool.Count)];
                var near = Neighbourhood(cx, a, assigned, 60);
                int b = near[rng.Next(near.Count)], d = near[rng.Next(near.Count)];
                var cands = Candidates(cx, o, a, b, d, rng).ToList();
                // least-squares fits to the whole neighbourhood survive noisy normals far better than two sampled normals
                if (near.Count >= 12)
                {
                    var seed = (PrimKind.Plane, cx.C[a], cx.N[a], 0.0, 0.0);
                    if (o.Spheres && Refit(cx, PrimKind.Sphere, Neighbourhood(cx, a, assigned, 500), seed) is { } sf && sf.Item4 < cx.M.Size.Length * 0.75) cands.Add(sf);
                    if (o.Cylinders && Refit(cx, PrimKind.Cylinder, near, seed) is { } cf && cf.Item4 < cx.M.Size.Length * 0.75) cands.Add(cf);
                }
                foreach (var cand in cands)
                {
                    int ha = 0, hu = 0;
                    foreach (int t in sa) if (Inlier(cx, cand, t)) ha++;
                    foreach (int t in su) if (Inlier(cx, cand, t)) hu++;
                    double score = 0.5 * ha / sa.Count + 0.5 * hu / su.Count;
                    if (score > bestScore) { bestScore = score; best = cand; }
                }
            }
            Log?.Invoke($"best {best?.kind} score {bestScore:0.###} pool {pool.Count} o={best?.o0} ax={best?.ax} r={best?.r:0.##} sampleA {sa.Count}");
            if (best == null) { misses++; continue; }
            var cur = best.Value;
            List<int>? comp = null;
            for (int pass = 0; pass < 3; pass++)
            {
                var inl = pool.Where(t => Inlier(cx, cur, t)).ToHashSet();
                comp = LargestComponent(cx, inl);
                Log?.Invoke($"  pass {pass}: inliers {inl.Count} comp {comp.Count} area {comp.Sum(t => cx.A[t]):0.#}");
                if (!Big(cx, comp, o, totalArea)) { comp = null; break; }
                if (comp.Count < 8) break;      // too few triangles for a stable least-squares fit; keep the sampled surface
                var refit = Refit(cx, cur.kind, comp, cur);
                if (refit == null) break;
                cur = refit.Value;
            }
            if (comp == null) { misses++; continue; }
            comp = GrowByDistance(cx, comp, cur, assigned);
            var region = Describe(cx, cur, comp);
            if (region.Kind == PrimKind.Cone && region.Rms > o.Tolerance * 0.35) { misses++; continue; }      // sloppy cones are almost always noise
            regions.Add(region);
            foreach (int t in comp) assigned[t] = true;
            misses = 0;
        }
        return regions.OrderByDescending(r => r.Area).ToList();
    }

    /// <summary>Absorb neighbouring triangles whose corners all lie on the fitted surface even if noise spoiled their normals.</summary>
    static List<int> GrowByDistance(Ctx c, List<int> comp, (PrimKind kind, Vec3 o, Vec3 ax, double r, double half) p, bool[] assigned)
    {
        var inSet = new HashSet<int>(comp); var res = new List<int>(comp);
        for (int i = 0; i < res.Count; i++)
            for (int k = 0; k < 3; k++)
            {
                int v = c.M.T[3 * res[i] + k];
                for (int j = c.VStart[v]; j < c.VStart[v + 1]; j++)
                {
                    int t = c.VTri[j];
                    if (assigned[t] || inSet.Contains(t) || c.A[t] < 1e-12) continue;
                    bool ok = true;
                    for (int q = 0; q < 3 && ok; q++) if (PointDist(p.kind, p.o, p.ax, p.r, p.half, c.M.P(c.M.T[3 * t + q])) > c.Eps * 1.2) ok = false;
                    if (ok) { inSet.Add(t); res.Add(t); }
                }
            }
        return res;
    }

    static bool Big(Ctx c, List<int> comp, Options o, double totalArea) { double a = comp.Sum(t => c.A[t]); return comp.Count >= 2 && (a >= totalArea * 0.004 || (comp.Count >= o.MinTris && a >= totalArea * 0.0015)); }

    /// <summary>Up to max triangles around a seed (breadth-first over shared edges), ignoring triangles already claimed by a region.</summary>
    static List<int> Neighbourhood(Ctx c, int seed, bool[] assigned, int max)
    {
        var res = new List<int> { seed }; var seen = new HashSet<int> { seed };
        for (int i = 0; i < res.Count && res.Count < max; i++)
            for (int k = 0; k < 3; k++)
            {
                int nb = c.Nb[3 * res[i] + k];
                // do not walk across a crease (e.g. from a hole wall onto the face around it): the neighbourhood must be one smooth surface
                if (nb >= 0 && !assigned[nb] && Vec3.Dot(c.SN[res[i]], c.SN[nb]) > 0.82 && seen.Add(nb)) res.Add(nb);
            }
        return res;
    }


    /// <summary>
    /// The surface (plane, cylinder, sphere or cone) the triangle 'seed' belongs to, grown across the mesh by distance from the fitted surface.
    /// Robust to noisy normals because it fits the neighbourhood by least squares. Null when nothing fits.
    /// </summary>
    public static FitRegion? FitAround(Prepared prep, int seed)
    {
        var cx = prep.C; var o = prep.O;
        var none = new bool[cx.M.TriCount];
        var near = Neighbourhood(cx, seed, none, 60); var big = Neighbourhood(cx, seed, none, 500); var huge = Neighbourhood(cx, seed, none, 2500);
        var seedTuple = (PrimKind.Plane, cx.C[seed], cx.N[seed], 0.0, 0.0);
        var cands = new List<(PrimKind kind, Vec3 o0, Vec3 ax, double r, double half)>();
        void Try(PrimKind k, List<int> tris)
        {
            if (tris.Count < 6) return;
            if (Refit(cx, k, tris, (k, cx.C[seed], cx.N[seed], 0.0, 0.0)) is { } f && (k == PrimKind.Plane || f.Item4 < cx.M.Size.Length * 0.75)) cands.Add(f);
        }
        if (o.Planes) { Try(PrimKind.Plane, near); Try(PrimKind.Plane, big); Try(PrimKind.Plane, huge); }
        if (o.Cylinders) { Try(PrimKind.Cylinder, near); Try(PrimKind.Cylinder, big); Try(PrimKind.Cylinder, huge); }
        if (o.Spheres) { Try(PrimKind.Sphere, huge); Try(PrimKind.Sphere, big); Try(PrimKind.Sphere, near); }
        if (o.Planes) cands.Add(seedTuple);
        (PrimKind kind, Vec3 o0, Vec3 ax, double r, double half)? best = null; double bestScore = 0;
        foreach (var cand in cands)
        {
            // the clicked triangle itself may be a noisy outlier, so it is not required to pass the normal test
            double score = 0; foreach (int t in huge) if (Inlier(cx, cand, t)) score += cx.A[t];
            Log?.Invoke($"  cand {cand.kind} r={cand.r:0.##} score {score:0.##}");
            if (score > bestScore) { bestScore = score; best = cand; }
        }
        if (best == null) return null;
        var cur = best.Value; List<int>? comp = null;
        for (int pass = 0; pass < 8; pass++)
        {
            var inl = new HashSet<int>(); for (int t = 0; t < cx.M.TriCount; t++) if (Inlier(cx, cur, t)) inl.Add(t);
            inl.Add(seed);
            int before = comp?.Count ?? 0;
            comp = ComponentOf(cx, inl, seed);
            if (comp.Count < 8) break;
            if (pass > 1 && comp.Count <= before) break;
            var refit = Refit(cx, cur.kind, comp, cur);
            if (refit == null) break;
            cur = refit.Value;
        }
        if (comp == null || comp.Count == 0) return null;
        comp = GrowByDistance(cx, comp, cur, none);
        return Describe(cx, cur, comp);
    }

    static List<int> ComponentOf(Ctx c, HashSet<int> set, int seed)
    {
        var seen = new HashSet<int> { seed }; var comp = new List<int> { seed };
        for (int i = 0; i < comp.Count; i++)
            for (int k = 0; k < 3; k++)
            {
                int v = c.M.T[3 * comp[i] + k];
                for (int j = c.VStart[v]; j < c.VStart[v + 1]; j++) { int nb = c.VTri[j]; if (set.Contains(nb) && seen.Add(nb)) comp.Add(nb); }
            }
        return comp;
    }

    // ---------------------------------------------------------------- candidates
    static IEnumerable<(PrimKind, Vec3, Vec3, double, double)> Candidates(Ctx c, Options o, int a, int b, int d, Random rng)
    {
        if (o.Planes) yield return (PrimKind.Plane, c.C[a], c.N[a], 0, 0);
        if (o.Spheres || o.Cylinders || o.Cones)
        {
            var n1 = c.N[a]; var n2 = c.N[b];
            var cr = Vec3.Cross(n1, n2);
            if (cr.Length > 0.1)
            {
                if (o.Spheres)
                {
                    // closest points between the two normal lines
                    var w0 = c.C[a] - c.C[b]; double aa = 1, bb = Vec3.Dot(n1, n2), cc = 1, dd = Vec3.Dot(n1, w0), ee = Vec3.Dot(n2, w0);
                    double den = aa * cc - bb * bb;
                    if (Math.Abs(den) > 1e-6)
                    {
                        double s = (bb * ee - cc * dd) / den, t = (aa * ee - bb * dd) / den;
                        var p1 = c.C[a] + n1 * s; var p2 = c.C[b] + n2 * t;
                        var ctr = (p1 + p2) * 0.5; double r = (c.C[a].DistanceTo(ctr) + c.C[b].DistanceTo(ctr)) / 2;
                        if (r > 1e-3 && r < c.M.Size.Length * 5 && p1.DistanceTo(p2) < Math.Max(c.Eps * 3, r * 0.05)) yield return (PrimKind.Sphere, ctr, Vec3.UnitZ, r, 0);
                    }
                }
                if (o.Cylinders)
                {
                    var ax = cr.Normalized();
                    var u = Math.Abs(ax.X) < 0.9 ? Vec3.Cross(ax, Vec3.UnitX).Normalized() : Vec3.Cross(ax, Vec3.UnitY).Normalized(); var v = Vec3.Cross(ax, u);
                    // 2D: lines through each centroid along its (projected) normal
                    (double x, double y) P1 = (Vec3.Dot(c.C[a], u), Vec3.Dot(c.C[a], v)), P2 = (Vec3.Dot(c.C[b], u), Vec3.Dot(c.C[b], v));
                    (double x, double y) D1 = (Vec3.Dot(n1, u), Vec3.Dot(n1, v)), D2 = (Vec3.Dot(n2, u), Vec3.Dot(n2, v));
                    double den = D1.x * D2.y - D1.y * D2.x;
                    if (Math.Abs(den) > 1e-6)
                    {
                        double s = ((P2.x - P1.x) * D2.y - (P2.y - P1.y) * D2.x) / den;
                        var cen2 = (x: P1.x + s * D1.x, y: P1.y + s * D1.y);
                        double r = Math.Abs(s);
                        var origin = u * cen2.x + v * cen2.y;
                        if (r > 1e-3 && r < c.M.Size.Length * 5) yield return (PrimKind.Cylinder, origin, ax, r, 0);
                    }
                }
                if (o.Cones)
                {
                    // cone from three normals: axis is the direction making equal angles with all of them
                    var n3 = c.N[d]; var m3 = Vec3.Cross(n1 - n2, n1 - n3);
                    if (m3.Length > 1e-3)
                    {
                        var ax = m3.Normalized(); double k1 = Vec3.Dot(n1, ax), k2 = Vec3.Dot(n2, ax), k3 = Vec3.Dot(n3, ax);
                        double ca = (k1 + k2 + k3) / 3;                          // cos(angle between normal and axis) = sin(half angle)
                        if (Math.Abs(ca) > 0.05 && Math.Abs(ca) < 0.95 && Math.Abs(k1 - k2) < 0.08 && Math.Abs(k1 - k3) < 0.08)
                        {
                            // apex: intersection of the three tangent planes n_i . x = n_i . c_i
                            double[,] A = { { n1.X, n1.Y, n1.Z, Vec3.Dot(n1, c.C[a]) }, { n2.X, n2.Y, n2.Z, Vec3.Dot(n2, c.C[b]) }, { n3.X, n3.Y, n3.Z, Vec3.Dot(n3, c.C[d]) } };
                            var apex = Solve3(A);
                            if (apex != null)
                            {
                                if (Vec3.Dot(c.C[a] - apex.Value, ax) < 0) ax = -ax;
                                double ang = Math.Asin(Math.Abs(ca));   // semi-angle between the generator and the axis
                                if (ang > 0.09 && ang < 1.31) yield return (PrimKind.Cone, apex.Value, ax, 0, ang * 180 / Math.PI);
                            }
                        }
                    }
                }
            }
        }
    }

    static Vec3? Solve3(double[,] A)
    {
        for (int c = 0; c < 3; c++)
        {
            int p = c; for (int r = c + 1; r < 3; r++) if (Math.Abs(A[r, c]) > Math.Abs(A[p, c])) p = r;
            if (Math.Abs(A[p, c]) < 1e-9) return null;
            if (p != c) for (int k = 0; k < 4; k++) (A[c, k], A[p, k]) = (A[p, k], A[c, k]);
            for (int r = c + 1; r < 3; r++) { double f = A[r, c] / A[c, c]; for (int k = c; k < 4; k++) A[r, k] -= f * A[c, k]; }
        }
        var x = new double[3];
        for (int r = 2; r >= 0; r--) { double s = A[r, 3]; for (int k = r + 1; k < 3; k++) s -= A[r, k] * x[k]; x[r] = s / A[r, r]; }
        return new Vec3(x[0], x[1], x[2]);
    }

    // ---------------------------------------------------------------- inlier tests
    static bool Inlier(Ctx c, (PrimKind kind, Vec3 o, Vec3 ax, double r, double half) p, int t) => Inlier(c, p.kind, p.o, p.ax, p.r, p.half, t);

    static double PointDist(PrimKind kind, Vec3 o, Vec3 ax, double r, double half, Vec3 pt)
    {
        var d = pt - o;
        switch (kind)
        {
            case PrimKind.Plane: return Math.Abs(Vec3.Dot(d, ax));
            case PrimKind.Sphere: return Math.Abs(d.Length - r);
            case PrimKind.Cylinder: return Math.Abs((d - ax * Vec3.Dot(d, ax)).Length - r);
            default:
            {
                double h = Vec3.Dot(d, ax); if (h < -1e-9) return double.MaxValue;
                return Math.Abs((d - ax * h).Length - h * Math.Tan(half * Math.PI / 180)) * Math.Cos(half * Math.PI / 180);
            }
        }
    }

    static bool Inlier(Ctx c, PrimKind kind, Vec3 o, Vec3 ax, double r, double half, int t)
    {
        if (c.Sliver[t])
        {
            for (int k = 0; k < 3; k++) if (PointDist(kind, o, ax, r, half, c.M.P(c.M.T[3 * t + k])) > c.Eps * 1.2) return false;
            return true;
        }
        var pt = c.C[t]; var n = c.N[t];
        switch (kind)
        {
            case PrimKind.Plane:
                return Math.Abs(Vec3.Dot(pt - o, ax)) < c.Eps && Math.Abs(Vec3.Dot(n, ax)) > c.CosTol;
            case PrimKind.Sphere:
            {
                var d = pt - o; double l = d.Length; if (l < 1e-9) return false;
                return Math.Abs(l - r) < c.Eps && Math.Abs(Vec3.Dot(n, d / l)) > c.CosTol;
            }
            case PrimKind.Cylinder:
            {
                var d = pt - o; d -= ax * Vec3.Dot(d, ax); double l = d.Length; if (l < 1e-9) return false;
                return Math.Abs(l - r) < c.Eps && Math.Abs(Vec3.Dot(n, ax)) < c.SinTol * 1.2 && Math.Abs(Vec3.Dot(n, d / l)) > c.CosTol;
            }
            default:
            {
                var d = pt - o; double h = Vec3.Dot(d, ax); if (h < -c.Eps) return false;
                var rad = d - ax * h; double l = rad.Length; if (l < 1e-9) return false;
                double rExpected = h * Math.Tan(half * Math.PI / 180);
                if (Math.Abs(l - rExpected) * Math.Cos(half * Math.PI / 180) > c.Eps) return false;
                // normal of a cone: perpendicular to the generator, tilted from the radial direction by the half angle
                var radial = rad / l; double expectAx = Math.Sin(half * Math.PI / 180), expectRad = Math.Cos(half * Math.PI / 180);
                var e1 = radial * expectRad + ax * expectAx; var e2 = radial * expectRad - ax * expectAx;
                return Math.Max(Math.Abs(Vec3.Dot(n, e1)), Math.Abs(Vec3.Dot(n, e2))) > c.CosTol;
            }
        }
    }

    static List<int> LargestComponent(Ctx c, HashSet<int> set)
    {
        // close single-triangle gaps: a triangle whose neighbours across two or more edges are inliers belongs too
        for (int pass = 0; pass < 2; pass++)
        {
            var add = new List<int>();
            foreach (int t in set)
                for (int k = 0; k < 3; k++)
                {
                    int nb = c.Nb[3 * t + k];
                    if (nb < 0 || set.Contains(nb)) continue;
                    int hits = 0; for (int j = 0; j < 3; j++) { int n2 = c.Nb[3 * nb + j]; if (n2 >= 0 && set.Contains(n2)) hits++; }
                    if (hits >= 2) add.Add(nb);
                }
            if (add.Count == 0) break;
            foreach (int t in add) set.Add(t);
        }
        var seen = new HashSet<int>(); List<int> best = [];
        foreach (int s in set)
        {
            if (seen.Contains(s)) continue;
            var comp = new List<int> { s }; seen.Add(s);
            for (int i = 0; i < comp.Count; i++)
                for (int k = 0; k < 3; k++)
                {
                    int v = c.M.T[3 * comp[i] + k];
                    for (int j = c.VStart[v]; j < c.VStart[v + 1]; j++) { int nb = c.VTri[j]; if (set.Contains(nb) && seen.Add(nb)) comp.Add(nb); }
                }
            if (comp.Count > best.Count) best = comp;
        }
        return best;
    }

    // ---------------------------------------------------------------- least-squares refits
    static (PrimKind, Vec3, Vec3, double, double)? Refit(Ctx c, PrimKind kind, List<int> tris, (PrimKind kind, Vec3 o, Vec3 ax, double r, double half) cur)
    {
        double wsum = tris.Sum(t => c.A[t]);
        var cen = tris.Aggregate(Vec3.Zero, (s, t) => s + c.C[t] * c.A[t]) / wsum;
        switch (kind)
        {
            case PrimKind.Plane:
            {
                double xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
                foreach (int t in tris) { var d = c.C[t] - cen; double w = c.A[t]; xx += w * d.X * d.X; xy += w * d.X * d.Y; xz += w * d.X * d.Z; yy += w * d.Y * d.Y; yz += w * d.Y * d.Z; zz += w * d.Z * d.Z; }
                var (_, ev) = MeshAnalysis.EigenSym([xx, xy, xz, yy, yz, zz]);
                var n = ev[2]; if (Vec3.Dot(n, cur.ax) < 0) n = -n;
                return (PrimKind.Plane, cen, n, 0, 0);
            }
            case PrimKind.Sphere:
            {
                // |p|^2 = 2 c.p + k   (k = r^2 - |c|^2): linear in (cx, cy, cz, k)
                var ata = new double[4, 5];
                foreach (int t in tris)
                {
                    var p = c.C[t] - cen; double[] row = [2 * p.X, 2 * p.Y, 2 * p.Z, 1]; double rhs = Vec3.Dot(p, p), w = c.A[t];
                    for (int i = 0; i < 4; i++) { for (int j = 0; j < 4; j++) ata[i, j] += w * row[i] * row[j]; ata[i, 4] += w * row[i] * rhs; }
                }
                var x = SolveN(ata, 4); if (x == null) return null;
                var ctr = new Vec3(x[0], x[1], x[2]); double r2 = x[3] + ctr.X * ctr.X + ctr.Y * ctr.Y + ctr.Z * ctr.Z;
                if (r2 <= 0) return null;
                return (PrimKind.Sphere, ctr + cen, Vec3.UnitZ, Math.Sqrt(r2), 0);
            }
            case PrimKind.Cylinder:
            {
                // two ways to find the axis: the direction normals do not spread in (good for scans), and the direction the long thin
                // triangles of a CAD-style tessellation run in. Keep whichever gives the rounder circle.
                var axes = new List<Vec3>();
                {
                    double xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0; int used = 0;
                    var good = tris.Where(t => !c.Sliver[t]).ToList();
                    foreach (int t in good.Count >= 6 ? good : tris) { var n = c.N[t]; double w = c.A[t]; xx += w * n.X * n.X; xy += w * n.X * n.Y; xz += w * n.X * n.Z; yy += w * n.Y * n.Y; yz += w * n.Y * n.Z; zz += w * n.Z * n.Z; used++; }
                    if (used > 0) axes.Add(MeshAnalysis.EigenSym([xx, xy, xz, yy, yz, zz]).vectors[2]);
                }
                var slivers = tris.Where(t => c.Sliver[t]).ToList();
                if (slivers.Count >= 6)
                {
                    double xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
                    foreach (int t in slivers)
                    {
                        Vec3 best = Vec3.Zero; double bl = 0;
                        for (int k = 0; k < 3; k++) { var e = c.M.P(c.M.T[3 * t + (k + 1) % 3]) - c.M.P(c.M.T[3 * t + k]); if (e.Length > bl) { bl = e.Length; best = e; } }
                        if (bl < 1e-12) continue; var dd = best / bl;
                        xx += bl * dd.X * dd.X; xy += bl * dd.X * dd.Y; xz += bl * dd.X * dd.Z; yy += bl * dd.Y * dd.Y; yz += bl * dd.Y * dd.Z; zz += bl * dd.Z * dd.Z;
                    }
                    axes.Add(MeshAnalysis.EigenSym([xx, xy, xz, yy, yz, zz]).vectors[0]);
                }
                (PrimKind, Vec3, Vec3, double, double)? bestFit = null; double bestRes = double.MaxValue;
                foreach (var axis0 in axes)
                {
                    var ax = axis0; if (Vec3.Dot(ax, cur.ax) < 0) ax = -ax;
                    var u = Math.Abs(ax.X) < 0.9 ? Vec3.Cross(ax, Vec3.UnitX).Normalized() : Vec3.Cross(ax, Vec3.UnitY).Normalized(); var v = Vec3.Cross(ax, u);
                    var ata = new double[3, 4];
                    foreach (int t in tris)
                    {
                        var d = c.C[t] - cen; double px = Vec3.Dot(d, u), py = Vec3.Dot(d, v), w = c.A[t];
                        double[] row = [2 * px, 2 * py, 1]; double rhs = px * px + py * py;
                        for (int i = 0; i < 3; i++) { for (int j = 0; j < 3; j++) ata[i, j] += w * row[i] * row[j]; ata[i, 3] += w * row[i] * rhs; }
                    }
                    var x = SolveN(ata, 3); if (x == null) continue;
                    double r2 = x[2] + x[0] * x[0] + x[1] * x[1]; if (r2 <= 0) continue;
                    double r = Math.Sqrt(r2); var origin = cen + u * x[0] + v * x[1];
                    double res = 0, ws = 0;
                    foreach (int t in tris) { var d = c.C[t] - origin; d -= ax * Vec3.Dot(d, ax); double e = d.Length - r; res += c.A[t] * e * e; ws += c.A[t]; }
                    res = Math.Sqrt(res / Math.Max(ws, 1e-12));
                    if (res < bestRes) { bestRes = res; bestFit = (PrimKind.Cylinder, origin, ax, r, 0); }
                }
                return bestFit;
            }
            default:
            {
                // keep the RANSAC cone, polish only the apex-axis through a few Gauss-Newton-free passes: average axis from normals
                return (PrimKind.Cone, cur.o, cur.ax, 0, cur.half);
            }
        }
    }

    static double[]? SolveN(double[,] A, int n)
    {
        for (int c = 0; c < n; c++)
        {
            int p = c; for (int r = c + 1; r < n; r++) if (Math.Abs(A[r, c]) > Math.Abs(A[p, c])) p = r;
            if (Math.Abs(A[p, c]) < 1e-12) return null;
            if (p != c) for (int k = 0; k <= n; k++) (A[c, k], A[p, k]) = (A[p, k], A[c, k]);
            for (int r = c + 1; r < n; r++) { double f = A[r, c] / A[c, c]; for (int k = c; k <= n; k++) A[r, k] -= f * A[c, k]; }
        }
        var x = new double[n];
        for (int r = n - 1; r >= 0; r--) { double s = A[r, n]; for (int k = r + 1; k < n; k++) s -= A[r, k] * x[k]; x[r] = s / A[r, r]; }
        return x;
    }

    static FitRegion Describe(Ctx c, (PrimKind kind, Vec3 o, Vec3 ax, double r, double half) p, List<int> tris)
    {
        double area = tris.Sum(t => c.A[t]), se = 0;
        double t0 = double.MaxValue, t1 = double.MinValue; int outward = 0;
        var mn = new Vec3(double.MaxValue, double.MaxValue, double.MaxValue); var mx = -mn;
        foreach (int t in tris)
        {
            var pt = c.C[t]; var d = pt - p.o; double dist = p.kind switch
            {
                PrimKind.Plane => Math.Abs(Vec3.Dot(d, p.ax)),
                PrimKind.Sphere => Math.Abs(d.Length - p.r),
                PrimKind.Cylinder => Math.Abs((d - p.ax * Vec3.Dot(d, p.ax)).Length - p.r),
                _ => Math.Abs((d - p.ax * Vec3.Dot(d, p.ax)).Length - Vec3.Dot(d, p.ax) * Math.Tan(p.half * Math.PI / 180)) * Math.Cos(p.half * Math.PI / 180),
            };
            se += dist * dist * c.A[t];
            for (int k = 0; k < 3; k++)
            {
                var v = c.M.P(c.M.T[3 * t + k]);
                mn = new(Math.Min(mn.X, v.X), Math.Min(mn.Y, v.Y), Math.Min(mn.Z, v.Z)); mx = new(Math.Max(mx.X, v.X), Math.Max(mx.Y, v.Y), Math.Max(mx.Z, v.Z));
                if (p.kind is PrimKind.Cylinder or PrimKind.Cone) { double h = Vec3.Dot(v - p.o, p.ax); t0 = Math.Min(t0, h); t1 = Math.Max(t1, h); }
            }
            Vec3 radial = p.kind switch { PrimKind.Sphere => d, PrimKind.Cylinder or PrimKind.Cone => d - p.ax * Vec3.Dot(d, p.ax), _ => p.ax };
            if (Vec3.Dot(c.N[t], radial) > 0) outward++;
        }
        return new FitRegion
        {
            Kind = p.kind, Tris = [.. tris], Origin = p.o, Axis = p.ax, Radius = p.r, HalfAngle = p.half, T0 = t0 == double.MaxValue ? 0 : t0, T1 = t1 == double.MinValue ? 0 : t1,
            Rms = Math.Sqrt(se / area), Area = area, Convex = outward * 2 >= tris.Count, Min = mn, Max = mx,
        };
    }
}
