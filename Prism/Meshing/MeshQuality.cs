using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Prism.Kernel;

namespace Prism.Meshing;

/// <summary>Triangle shape classes. Anything other than Good counts as a "sliver" for defect display.</summary>
public enum TriClass { Good, Needle, Cap, Degenerate }

/// <summary>Quality metric used for the heat map.</summary>
public enum QualityMetric { AspectRatio, SmallestAngle, LargestAngle, AreaOutlier }

public readonly record struct TriMetrics(double MinEdge, double MaxEdge, double Area, double AspectRatio, double MinAngle, double MaxAngle, TriClass Class);

public readonly record struct HistBucket(string Label, double Lo, double Hi, int Count);

public sealed record QualitySummary(
    int Triangles, int Good, int Needles, int Caps, int Degenerate,
    double AspectMean, double AspectMax, double MinAngleMin, double MinAngleMean, double MaxAngleMax,
    double EdgeMin, double EdgeMax, double EdgeMean, double AreaMedian, int SmallAreaOutliers, int LargeAreaOutliers,
    int OpenEdges, int NonManifoldEdges,
    IReadOnlyList<HistBucket> AspectHistogram, IReadOnlyList<HistBucket> MinAngleHistogram, IReadOnlyList<HistBucket> MaxAngleHistogram)
{
    public int Slivers => Needles + Caps + Degenerate;
}

/// <summary>Geometry of the defects to highlight (kept independent of any drawing code).</summary>
public sealed record DefectSet(
    IReadOnlyList<(Vec3 a, Vec3 b)> OpenEdges, IReadOnlyList<(Vec3 a, Vec3 b)> NonManifoldEdges,
    IReadOnlyList<(Vec3 a, Vec3 b, Vec3 c)> SelfIntersecting, IReadOnlyList<(Vec3 a, Vec3 b, Vec3 c)> Slivers);

public sealed record SelfIntersectionResult(int[] Triangles, List<(int a, int b)> Pairs);

public static class MeshQuality
{
    public const double DefaultNeedleAngle = 10, DefaultCapAngle = 160;
    public static readonly double[] AspectEdges = [1, 1.5, 2, 3, 5, 10, double.PositiveInfinity];
    public static readonly double[] MinAngleEdges = [0, 10, 20, 30, 40, 50, 60.0001];
    public static readonly double[] MaxAngleEdges = [60, 90, 120, 150, 180.0001];

    // ------------------------------------------------------------ per-triangle metrics
    public static TriMetrics Triangle(Vec3 a, Vec3 b, Vec3 c, double needleAngle = DefaultNeedleAngle, double capAngle = DefaultCapAngle)
    {
        double la = (c - b).Length, lb = (a - c).Length, lc = (b - a).Length;   // la is opposite a
        double emax = Math.Max(la, Math.Max(lb, lc)), emin = Math.Min(la, Math.Min(lb, lc));
        double area = Vec3.Cross(b - a, c - a).Length * 0.5;
        if (emax < 1e-300 || area <= 1e-12 * emax * emax)
            return new TriMetrics(emin, emax, area, double.PositiveInfinity, 0, 180, TriClass.Degenerate);
        double ar = emax * (la + lb + lc) / (4 * Math.Sqrt(3) * area);
        static double Ang(double opp, double s1, double s2) => Math.Acos(Math.Clamp((s1 * s1 + s2 * s2 - opp * opp) / (2 * s1 * s2), -1, 1)) * 180 / Math.PI;
        double A = Ang(la, lb, lc), B = Ang(lb, la, lc), C = 180 - A - B;
        double amin = Math.Min(A, Math.Min(B, C)), amax = Math.Max(A, Math.Max(B, C));
        var cls = amax >= capAngle ? TriClass.Cap : amin < needleAngle ? TriClass.Needle : TriClass.Good;
        return new TriMetrics(emin, emax, area, ar, amin, amax, cls);
    }

    public static TriMetrics[] All(TriMesh m, double needleAngle = DefaultNeedleAngle, double capAngle = DefaultCapAngle)
    {
        var r = new TriMetrics[m.TriCount];
        for (int t = 0; t < r.Length; t++) { var (a, b, c) = m.Tri(t); r[t] = Triangle(m.P(a), m.P(b), m.P(c), needleAngle, capAngle); }
        return r;
    }

    public static double MedianArea(TriMetrics[] mt)
    {
        var a = mt.Select(x => x.Area).Where(x => x > 0).OrderBy(x => x).ToArray();
        return a.Length == 0 ? 0 : a[a.Length / 2];
    }

    /// <summary>Triangles whose area differs from the median by more than <paramref name="factor"/> (small, large).</summary>
    public static (List<int> small, List<int> large) AreaOutliers(TriMetrics[] mt, double factor = 50)
    {
        double med = MedianArea(mt); var s = new List<int>(); var l = new List<int>();
        if (med <= 0) return (s, l);
        for (int t = 0; t < mt.Length; t++) { if (mt[t].Area < med / factor) s.Add(t); else if (mt[t].Area > med * factor) l.Add(t); }
        return (s, l);
    }

    /// <summary>Severity 0 (fine) .. 1 (worst) per triangle for a heat map metric.</summary>
    public static float[] Severity(TriMetrics[] mt, QualityMetric metric)
    {
        var r = new float[mt.Length]; double med = MedianArea(mt);
        for (int t = 0; t < r.Length; t++)
        {
            var x = mt[t]; double v = metric switch
            {
                QualityMetric.AspectRatio => double.IsInfinity(x.AspectRatio) ? 1 : (x.AspectRatio - 1) / 7,
                QualityMetric.SmallestAngle => (60 - x.MinAngle) / 60,
                QualityMetric.LargestAngle => (x.MaxAngle - 60) / 120,
                _ => med <= 0 || x.Area <= 0 ? 1 : Math.Abs(Math.Log10(x.Area / med)) / 2,
            };
            r[t] = (float)Math.Clamp(v, 0, 1);
        }
        return r;
    }

    /// <summary>Worst (highest) severity of the triangles around each vertex.</summary>
    public static float[] ToVertexMax(TriMesh m, float[] perTri)
    {
        var v = new float[m.VertexCount];
        for (int t = 0; t < perTri.Length; t++) for (int k = 0; k < 3; k++) { int i = m.T[3 * t + k]; if (perTri[t] > v[i]) v[i] = perTri[t]; }
        return v;
    }

    // ------------------------------------------------------------ edges
    /// <summary>Edges used by exactly one triangle, and edges used by more than two.</summary>
    public static (List<(Vec3 a, Vec3 b)> open, List<(Vec3 a, Vec3 b)> nonManifold) EdgeDefects(TriMesh m)
    {
        var cnt = new Dictionary<long, (int n, int a, int b)>(); int nv = Math.Max(m.VertexCount, 1);
        for (int t = 0; t < m.TriCount; t++)
            for (int k = 0; k < 3; k++)
            {
                int a = m.T[3 * t + k], b = m.T[3 * t + (k + 1) % 3]; var key = TriMesh.EdgeKey(a, b, nv);
                cnt[key] = cnt.TryGetValue(key, out var c) ? (c.n + 1, a, b) : (1, a, b);
            }
        return (cnt.Values.Where(c => c.n == 1).Select(c => (m.P(c.a), m.P(c.b))).ToList(), cnt.Values.Where(c => c.n > 2).Select(c => (m.P(c.a), m.P(c.b))).ToList());
    }

    // ------------------------------------------------------------ summary
    public static QualitySummary Summarize(TriMesh m, double needleAngle = DefaultNeedleAngle, double capAngle = DefaultCapAngle)
    {
        var mt = All(m, needleAngle, capAngle); int n = mt.Length;
        var (open, nm) = EdgeDefects(m);
        var (small, large) = AreaOutliers(mt);
        var fin = mt.Where(x => !double.IsInfinity(x.AspectRatio)).ToArray();
        double edgeMean = 0;
        for (int t = 0; t < n; t++) { var (a, b, c) = m.Tri(t); edgeMean += (m.P(a).DistanceTo(m.P(b)) + m.P(b).DistanceTo(m.P(c)) + m.P(c).DistanceTo(m.P(a))) / 3; }
        if (n > 0) edgeMean /= n;
        return new QualitySummary(n, mt.Count(x => x.Class == TriClass.Good), mt.Count(x => x.Class == TriClass.Needle), mt.Count(x => x.Class == TriClass.Cap), mt.Count(x => x.Class == TriClass.Degenerate),
            fin.Length == 0 ? 0 : fin.Average(x => x.AspectRatio), fin.Length == 0 ? 0 : fin.Max(x => x.AspectRatio),
            n == 0 ? 0 : mt.Min(x => x.MinAngle), n == 0 ? 0 : mt.Average(x => x.MinAngle), n == 0 ? 0 : mt.Max(x => x.MaxAngle),
            n == 0 ? 0 : mt.Min(x => x.MinEdge), n == 0 ? 0 : mt.Max(x => x.MaxEdge), edgeMean, MedianArea(mt), small.Count, large.Count, open.Count, nm.Count,
            Histogram(mt.Select(x => x.AspectRatio), AspectEdges, "0.#"), Histogram(mt.Select(x => x.MinAngle), MinAngleEdges, "0", "°"), Histogram(mt.Select(x => x.MaxAngle), MaxAngleEdges, "0", "°"));
    }

    /// <summary>Bucket values by edge list; every value lands in exactly one bucket so the counts add up to the input size.</summary>
    public static List<HistBucket> Histogram(IEnumerable<double> values, double[] edges, string fmt = "0.#", string unit = "")
    {
        var counts = new int[edges.Length - 1];
        foreach (var v in values)
        {
            int i = counts.Length - 1;
            for (int k = 0; k < counts.Length; k++) if (v < edges[k + 1]) { i = k; break; }
            counts[i]++;
        }
        string F(double x) => Math.Round(x, 1).ToString(fmt, CultureInfo.InvariantCulture) + unit;
        var r = new List<HistBucket>();
        for (int k = 0; k < counts.Length; k++)
        {
            double lo = edges[k], hi = edges[k + 1];
            r.Add(new HistBucket(double.IsInfinity(hi) ? F(lo) + "+" : $"{F(lo)} - {F(Math.Min(Math.Round(hi), 180))}", lo, hi, counts[k]));
        }
        return r;
    }

    // ------------------------------------------------------------ self intersection
    /// <summary>Triangles that cross other triangles. Pairs that share a vertex (by index or position) are skipped, as are pairs that only touch.</summary>
    public static SelfIntersectionResult SelfIntersections(TriMesh m, int maxPairs = 100000)
    {
        var pairs = new List<(int a, int b)>(); var set = new SortedSet<int>();
        if (m.TriCount < 2) return new([], pairs);
        double eps = 1e-7 * Math.Max(m.Size.Length, 1e-9);
        var bvh = m.Bvh; var cand = new List<int>();
        for (int i = 0; i < m.TriCount && pairs.Count < maxPairs; i++)
        {
            var (a0, a1, a2) = m.Tri(i); Vec3 p0 = m.P(a0), p1 = m.P(a1), p2 = m.P(a2);
            var lo = new Vec3(Math.Min(p0.X, Math.Min(p1.X, p2.X)) - eps, Math.Min(p0.Y, Math.Min(p1.Y, p2.Y)) - eps, Math.Min(p0.Z, Math.Min(p1.Z, p2.Z)) - eps);
            var hi = new Vec3(Math.Max(p0.X, Math.Max(p1.X, p2.X)) + eps, Math.Max(p0.Y, Math.Max(p1.Y, p2.Y)) + eps, Math.Max(p0.Z, Math.Max(p1.Z, p2.Z)) + eps);
            cand.Clear(); bvh.QueryBox(lo, hi, cand);
            foreach (int j in cand)
            {
                if (j <= i) continue;
                var (b0, b1, b2) = m.Tri(j);
                if (SharesVertex(m, [a0, a1, a2], [b0, b1, b2], eps)) continue;
                if (TrianglesIntersect(p0, p1, p2, m.P(b0), m.P(b1), m.P(b2), eps)) { pairs.Add((i, j)); set.Add(i); set.Add(j); }
            }
        }
        return new(set.ToArray(), pairs);
    }

    static bool SharesVertex(TriMesh m, int[] A, int[] B, double eps)
    {
        foreach (int a in A) foreach (int b in B) if (a == b || m.P(a).DistanceTo(m.P(b)) <= eps) return true;
        return false;
    }

    /// <summary>Triangle/triangle test: true when the triangles cross with a positive-length overlap (touching does not count).</summary>
    public static bool TrianglesIntersect(Vec3 a0, Vec3 a1, Vec3 a2, Vec3 b0, Vec3 b1, Vec3 b2, double eps = 1e-9)
    {
        var n1 = Vec3.Cross(a1 - a0, a2 - a0); var n2 = Vec3.Cross(b1 - b0, b2 - b0);
        if (n1.Length < 1e-300 || n2.Length < 1e-300) return false;
        var u1 = n1.Normalized(); var u2 = n2.Normalized();
        double[] db = [Vec3.Dot(u1, b0 - a0), Vec3.Dot(u1, b1 - a0), Vec3.Dot(u1, b2 - a0)];
        if (db.All(d => d > eps) || db.All(d => d < -eps)) return false;
        double[] da = [Vec3.Dot(u2, a0 - b0), Vec3.Dot(u2, a1 - b0), Vec3.Dot(u2, a2 - b0)];
        if (da.All(d => d > eps) || da.All(d => d < -eps)) return false;
        for (int i = 0; i < 3; i++) { if (Math.Abs(db[i]) <= eps) db[i] = 0; if (Math.Abs(da[i]) <= eps) da[i] = 0; }
        if (db.All(d => d == 0) && da.All(d => d == 0)) return Coplanar(u1, a0, a1, a2, b0, b1, b2, eps);
        var dir = Vec3.Cross(u1, u2);
        if (dir.Length < 1e-12) return false;
        dir = dir.Normalized();
        if (!Interval(a0, a1, a2, da, dir, out double amin, out double amax)) return false;
        if (!Interval(b0, b1, b2, db, dir, out double bmin, out double bmax)) return false;
        return Math.Min(amax, bmax) - Math.Max(amin, bmin) > eps;
    }

    static bool Interval(Vec3 p0, Vec3 p1, Vec3 p2, double[] d, Vec3 dir, out double lo, out double hi)
    {
        Vec3[] p = [p0, p1, p2]; double l = double.MaxValue, h = double.MinValue; int n = 0;
        for (int i = 0; i < 3; i++)
        {
            int j = (i + 1) % 3;
            if (d[i] == 0) { double t = Vec3.Dot(dir, p[i]); l = Math.Min(l, t); h = Math.Max(h, t); n++; }
            if (d[i] * d[j] < 0) { double t = Vec3.Dot(dir, p[i] + (p[j] - p[i]) * (d[i] / (d[i] - d[j]))); l = Math.Min(l, t); h = Math.Max(h, t); n++; }
        }
        lo = l; hi = h;
        return n >= 2;
    }

    static bool Coplanar(Vec3 n, Vec3 a0, Vec3 a1, Vec3 a2, Vec3 b0, Vec3 b1, Vec3 b2, double eps)
    {
        int drop = Math.Abs(n.X) >= Math.Abs(n.Y) && Math.Abs(n.X) >= Math.Abs(n.Z) ? 0 : Math.Abs(n.Y) >= Math.Abs(n.Z) ? 1 : 2;
        (double x, double y) P(Vec3 v) => drop == 0 ? (v.Y, v.Z) : drop == 1 ? (v.X, v.Z) : (v.X, v.Y);
        var A = new[] { P(a0), P(a1), P(a2) }; var B = new[] { P(b0), P(b1), P(b2) };
        static double Orient((double x, double y) p, (double x, double y) q, (double x, double y) r) => (q.x - p.x) * (r.y - p.y) - (q.y - p.y) * (r.x - p.x);
        double e2 = eps * Math.Max(1, (a1 - a0).Length + (b1 - b0).Length);
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
            {
                var p = A[i]; var q = A[(i + 1) % 3]; var r = B[j]; var s = B[(j + 1) % 3];
                double o1 = Orient(p, q, r), o2 = Orient(p, q, s), o3 = Orient(r, s, p), o4 = Orient(r, s, q);
                if (((o1 > e2 && o2 < -e2) || (o1 < -e2 && o2 > e2)) && ((o3 > e2 && o4 < -e2) || (o3 < -e2 && o4 > e2))) return true;
            }
        bool Inside((double x, double y)[] T, (double x, double y) p)
        {
            double s0 = Orient(T[0], T[1], p), s1 = Orient(T[1], T[2], p), s2 = Orient(T[2], T[0], p);
            return (s0 > e2 && s1 > e2 && s2 > e2) || (s0 < -e2 && s1 < -e2 && s2 < -e2);
        }
        return B.Any(p => Inside(A, p)) || A.Any(p => Inside(B, p));
    }

    // ------------------------------------------------------------ defects bundle
    public static DefectSet Defects(TriMesh m, TriMetrics[] mt, int[] selfIntersecting, bool open = true, bool nonManifold = true, bool slivers = true, int maxTris = 20000)
    {
        var (o, nm) = open || nonManifold ? EdgeDefects(m) : ([], []);
        (Vec3, Vec3, Vec3) Tri(int t) { var (a, b, c) = m.Tri(t); return (m.P(a), m.P(b), m.P(c)); }
        return new DefectSet(open ? o : [], nonManifold ? nm : [], selfIntersecting.Take(maxTris).Select(Tri).ToList(),
            slivers ? Enumerable.Range(0, mt.Length).Where(t => mt[t].Class != TriClass.Good).Take(maxTris).Select(Tri).ToList() : []);
    }
}
