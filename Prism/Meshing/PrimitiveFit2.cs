using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Kernel;

namespace Prism.Meshing;

// =====================================================================================================================
// Extended primitive detection for reverse engineering: torus, extrusion (constant cross-section), surface of revolution
// (axis + profile polyline) and a free-form bicubic B-spline patch with a fit-error report. Pure C#, no kernel calls.
// PrimitiveFit.Detect calls PrimitiveFit2.Extras when Options.Extended is set; every fit is also public for direct use.
// =====================================================================================================================

/// <summary>A bicubic (or lower degree) clamped uniform B-spline patch over a planar parameter rectangle.
/// Poles are stored [iu * NV + iv], the layout Occ.NurbsSurface(grid, rows = NU, cols = NV) expects.</summary>
public sealed class NurbsPatch
{
    public int DegU { get; init; } = 3;
    public int DegV { get; init; } = 3;
    public int NU { get; init; }
    public int NV { get; init; }
    public Vec3[] Poles { get; init; } = [];
    /// <summary>Plane the parameters were measured in: p = Origin + AxisU * u + AxisV * v (+ height).</summary>
    public Vec3 Origin { get; init; }
    public Vec3 AxisU { get; init; }
    public Vec3 AxisV { get; init; }
    public double U0 { get; init; }
    public double U1 { get; init; }
    public double V0 { get; init; }
    public double V1 { get; init; }
    /// <summary>Fit error report.</summary>
    public double Rms { get; init; }
    public double MaxError { get; init; }
    public double PercentInTolerance { get; init; }
    /// <summary>Share of the parameter rectangle that has scan data under it (low = the patch is extrapolated over gaps).</summary>
    public double Coverage { get; init; }
    public int Samples { get; init; }

    internal double[] KnotsU => Knots(NU, DegU);
    internal double[] KnotsV => Knots(NV, DegV);

    public static double[] Knots(int n, int p)
    {
        var k = new double[n + p + 1];
        for (int i = 0; i <= p; i++) { k[i] = 0; k[n + i] = 1; }
        for (int j = 1; j <= n - p - 1; j++) k[p + j] = (double)j / (n - p);
        return k;
    }

    /// <summary>Surface point at normalised parameters s, t in [0,1].</summary>
    public Vec3 EvalNorm(double s, double t)
    {
        s = Math.Clamp(s, 0, 1); t = Math.Clamp(t, 0, 1);
        var ku = KnotsU; var kv = KnotsV;
        int su = BSpline.FindSpan(NU - 1, DegU, s, ku), sv = BSpline.FindSpan(NV - 1, DegV, t, kv);
        var bu = new double[DegU + 1]; var bv = new double[DegV + 1];
        BSpline.Basis(su, s, DegU, ku, bu); BSpline.Basis(sv, t, DegV, kv, bv);
        double x = 0, y = 0, z = 0;
        for (int a = 0; a <= DegU; a++)
            for (int b = 0; b <= DegV; b++)
            {
                var p = Poles[(su - DegU + a) * NV + (sv - DegV + b)]; double w = bu[a] * bv[b];
                x += w * p.X; y += w * p.Y; z += w * p.Z;
            }
        return new Vec3(x, y, z);
    }

    /// <summary>Surface point at plane coordinates (u, v).</summary>
    public Vec3 Eval(double u, double v) => EvalNorm((u - U0) / Math.Max(U1 - U0, 1e-12), (v - V0) / Math.Max(V1 - V0, 1e-12));

    Vec3[]? _grid;
    const int G = 20;

    /// <summary>Closest point on the patch (coarse grid search + Newton polish).</summary>
    public Vec3 Closest(Vec3 q)
    {
        if (_grid == null)
        {
            var g = new Vec3[(G + 1) * (G + 1)];
            for (int i = 0; i <= G; i++) for (int j = 0; j <= G; j++) g[i * (G + 1) + j] = EvalNorm((double)i / G, (double)j / G);
            _grid = g;
        }
        int bi = 0, bj = 0; double bd = double.MaxValue;
        for (int i = 0; i <= G; i++) for (int j = 0; j <= G; j++) { double d = (q - _grid[i * (G + 1) + j]).Length; if (d < bd) { bd = d; bi = i; bj = j; } }
        double s = (double)bi / G, t = (double)bj / G;
        for (int it = 0; it < 8; it++) { if (!NewtonStep(this, q, ref s, ref t)) break; }
        return EvalNorm(s, t);
    }

    internal static bool NewtonStep(NurbsPatch f, Vec3 q, ref double s, ref double t)
    {
        const double h = 1e-5;
        var p = f.EvalNorm(s, t);
        var su = (f.EvalNorm(Math.Min(s + h, 1), t) - f.EvalNorm(Math.Max(s - h, 0), t)) / (Math.Min(s + h, 1) - Math.Max(s - h, 0));
        var sv = (f.EvalNorm(s, Math.Min(t + h, 1)) - f.EvalNorm(s, Math.Max(t - h, 0))) / (Math.Min(t + h, 1) - Math.Max(t - h, 0));
        var d = p - q;
        double a = Vec3.Dot(su, su), b = Vec3.Dot(su, sv), c = Vec3.Dot(sv, sv), e = -Vec3.Dot(d, su), g = -Vec3.Dot(d, sv);
        double det = a * c - b * b; double lam = 1e-9 * (a + c);
        det = (a + lam) * (c + lam) - b * b;
        if (Math.Abs(det) < 1e-18) return false;
        double ds = ((c + lam) * e - b * g) / det, dt = ((a + lam) * g - b * e) / det;
        double ns = Math.Clamp(s + ds, 0, 1), nt = Math.Clamp(t + dt, 0, 1);
        bool moved = Math.Abs(ns - s) + Math.Abs(nt - t) > 1e-9;
        s = ns; t = nt; return moved;
    }
}

static class BSpline
{
    public static int FindSpan(int n, int p, double u, double[] U)
    {
        if (u >= U[n + 1]) return n;
        if (u <= U[p]) return p;
        int lo = p, hi = n + 1, mid = (lo + hi) / 2;
        while (u < U[mid] || u >= U[mid + 1]) { if (u < U[mid]) hi = mid; else lo = mid; mid = (lo + hi) / 2; }
        return mid;
    }

    public static void Basis(int span, double u, int p, double[] U, double[] N)
    {
        var left = new double[p + 1]; var right = new double[p + 1];
        N[0] = 1;
        for (int j = 1; j <= p; j++)
        {
            left[j] = u - U[span + 1 - j]; right[j] = U[span + j] - u; double saved = 0;
            for (int r = 0; r < j; r++)
            {
                double den = right[r + 1] + left[j - r], tmp = den == 0 ? 0 : N[r] / den;
                N[r] = saved + right[r + 1] * tmp; saved = left[j - r] * tmp;
            }
            N[j] = saved;
        }
    }
}

/// <summary>Distance / closest point / normal for every kind of fitted region.</summary>
public static class SurfaceMath
{
    internal static (Vec3 u, Vec3 v) Basis(Vec3 a)
    {
        var u = (Math.Abs(a.X) < 0.9 ? Vec3.Cross(a, Vec3.UnitX) : Vec3.Cross(a, Vec3.UnitY)).Normalized();
        return (u, Vec3.Cross(a, u));
    }

    /// <summary>Closest point on the (unbounded) fitted surface.</summary>
    public static Vec3 Closest(FitRegion r, Vec3 p)
    {
        var d = p - r.Origin;
        switch (r.Kind)
        {
            case PrimKind.Plane: return p - r.Axis * Vec3.Dot(d, r.Axis);
            case PrimKind.Sphere: { double l = d.Length; return l < 1e-12 ? r.Origin + Vec3.UnitX * r.Radius : r.Origin + d * (r.Radius / l); }
            case PrimKind.Cylinder: { double h = Vec3.Dot(d, r.Axis); var rad = d - r.Axis * h; double l = rad.Length; var rh = l < 1e-12 ? Basis(r.Axis).u : rad / l; return r.Origin + r.Axis * h + rh * r.Radius; }
            case PrimKind.Cone:
            {
                double h = Vec3.Dot(d, r.Axis); var rad = d - r.Axis * h; double l = rad.Length; var rh = l < 1e-12 ? Basis(r.Axis).u : rad / l;
                double al = r.HalfAngle * Math.PI / 180, s = Math.Max(h * Math.Cos(al) + l * Math.Sin(al), 0);
                return r.Origin + r.Axis * (s * Math.Cos(al)) + rh * (s * Math.Sin(al));
            }
            case PrimKind.Torus:
            {
                double h = Vec3.Dot(d, r.Axis); var rad = d - r.Axis * h; double l = rad.Length; var rh = l < 1e-12 ? Basis(r.Axis).u : rad / l;
                var q = r.Origin + rh * r.Radius; var v = p - q; double vl = v.Length;
                return vl < 1e-12 ? q + rh * r.MinorRadius : q + v * (r.MinorRadius / vl);
            }
            case PrimKind.Revolution:
            {
                if (r.Profile == null || r.Profile.Length < 2) return r.Origin;
                double h = Vec3.Dot(d, r.Axis); var rad = d - r.Axis * h; double l = rad.Length; var rh = l < 1e-12 ? Basis(r.Axis).u : rad / l;
                var prof = ProfileHR(r);
                var (_, ch, cr) = Poly2.Closest(prof, r.ProfileClosed, h, l);
                return r.Origin + r.Axis * ch + rh * cr;
            }
            case PrimKind.Extrusion:
            {
                if (r.Profile == null || r.Profile.Length < 2) return r.Origin;
                var (u, v) = Basis(r.Axis); double h = Vec3.Dot(d, r.Axis);
                var prof = r.Profile.Select(q => (x: Vec3.Dot(q - r.Origin, u), y: Vec3.Dot(q - r.Origin, v))).ToList();
                var (_, cx, cy) = Poly2.Closest(prof, r.ProfileClosed, Vec3.Dot(d, u), Vec3.Dot(d, v));
                return r.Origin + u * cx + v * cy + r.Axis * h;
            }
            default: return r.Patch != null ? r.Patch.Closest(p) : p;
        }
    }

    internal static List<(double x, double y)> ProfileHR(FitRegion r) =>
        r.Profile!.Select(q => { var d = q - r.Origin; double h = Vec3.Dot(d, r.Axis); return (x: h, y: (d - r.Axis * h).Length); }).ToList();

    public static double Distance(FitRegion r, Vec3 p)
    {
        switch (r.Kind)
        {
            case PrimKind.Plane: return Math.Abs(Vec3.Dot(p - r.Origin, r.Axis));
            case PrimKind.Sphere: return Math.Abs((p - r.Origin).Length - r.Radius);
            case PrimKind.Cylinder: { var d = p - r.Origin; return Math.Abs((d - r.Axis * Vec3.Dot(d, r.Axis)).Length - r.Radius); }
            default: return (p - Closest(r, p)).Length;
        }
    }

    /// <summary>Unit normal of the surface at the point nearest to p, pointing away from the axis / centre (planes: Axis). Null for extrusion and free-form.</summary>
    public static Vec3? Normal(FitRegion r, Vec3 p)
    {
        var d = p - r.Origin;
        switch (r.Kind)
        {
            case PrimKind.Plane: return r.Axis;
            case PrimKind.Sphere: return d.Length < 1e-12 ? null : d.Normalized();
            case PrimKind.Cylinder: { var rad = d - r.Axis * Vec3.Dot(d, r.Axis); return rad.Length < 1e-12 ? null : rad.Normalized(); }
            case PrimKind.Cone:
            {
                double h = Vec3.Dot(d, r.Axis); var rad = d - r.Axis * h; if (rad.Length < 1e-12) return null;
                double al = r.HalfAngle * Math.PI / 180; return (rad.Normalized() * Math.Cos(al) - r.Axis * Math.Sin(al)).Normalized();
            }
            case PrimKind.Torus:
            {
                double h = Vec3.Dot(d, r.Axis); var rad = d - r.Axis * h; if (rad.Length < 1e-12) return null;
                var q = r.Origin + rad.Normalized() * r.Radius; var v = p - q; return v.Length < 1e-12 ? null : v.Normalized();
            }
            default: return null;
        }
    }
}

static class Poly2
{
    public static (double d, double x, double y) Closest(IReadOnlyList<(double x, double y)> p, bool closed, double qx, double qy)
    {
        double best = double.MaxValue, bx = 0, by = 0; int n = p.Count, segs = closed ? n : n - 1;
        for (int i = 0; i < segs; i++)
        {
            var a = p[i]; var b = p[(i + 1) % n]; double dx = b.x - a.x, dy = b.y - a.y, l2 = dx * dx + dy * dy;
            double t = l2 < 1e-24 ? 0 : Math.Clamp(((qx - a.x) * dx + (qy - a.y) * dy) / l2, 0, 1);
            double px = a.x + dx * t, py = a.y + dy * t, d = Math.Sqrt((qx - px) * (qx - px) + (qy - py) * (qy - py));
            if (d < best) { best = d; bx = px; by = py; }
        }
        if (n == 1) return (Math.Sqrt((qx - p[0].x) * (qx - p[0].x) + (qy - p[0].y) * (qy - p[0].y)), p[0].x, p[0].y);
        return (best, bx, by);
    }

    public static double Length(IReadOnlyList<(double x, double y)> p, bool closed)
    {
        double s = 0; int segs = closed ? p.Count : p.Count - 1;
        for (int i = 0; i < segs; i++) { var a = p[i]; var b = p[(i + 1) % p.Count]; s += Math.Sqrt((b.x - a.x) * (b.x - a.x) + (b.y - a.y) * (b.y - a.y)); }
        return s;
    }

    /// <summary>Douglas-Peucker. For a closed polyline it is split at the point farthest from the first one.</summary>
    public static List<(double x, double y)> Simplify(List<(double x, double y)> p, double tol, bool closed)
    {
        if (p.Count < 4 || tol <= 0) return p;
        List<(double x, double y)> Open(List<(double x, double y)> q)
        {
            var keep = new bool[q.Count]; keep[0] = keep[^1] = true;
            var stack = new Stack<(int a, int b)>(); stack.Push((0, q.Count - 1));
            while (stack.Count > 0)
            {
                var (a, b) = stack.Pop(); double best = 0; int idx = -1;
                for (int i = a + 1; i < b; i++) { double d = SegDist(q[i], q[a], q[b]); if (d > best) { best = d; idx = i; } }
                if (idx >= 0 && best > tol) { keep[idx] = true; stack.Push((a, idx)); stack.Push((idx, b)); }
            }
            return q.Where((_, i) => keep[i]).ToList();
        }
        if (!closed) return Open(p);
        int far = 0; double fd = -1;
        for (int i = 1; i < p.Count; i++) { double d = (p[i].x - p[0].x) * (p[i].x - p[0].x) + (p[i].y - p[0].y) * (p[i].y - p[0].y); if (d > fd) { fd = d; far = i; } }
        var first = Open(p.GetRange(0, far + 1));
        var second = Open(p.GetRange(far, p.Count - far).Append(p[0]).ToList());
        first.AddRange(second.Skip(1).Take(second.Count - 2));
        return first;
    }

    static double SegDist((double x, double y) q, (double x, double y) a, (double x, double y) b)
    {
        double dx = b.x - a.x, dy = b.y - a.y, l2 = dx * dx + dy * dy;
        double t = l2 < 1e-24 ? 0 : Math.Clamp(((q.x - a.x) * dx + (q.y - a.y) * dy) / l2, 0, 1);
        double px = a.x + dx * t - q.x, py = a.y + dy * t - q.y; return Math.Sqrt(px * px + py * py);
    }
}

/// <summary>Torus, extrusion, surface-of-revolution and free-form patch fitting.</summary>
public static class PrimitiveFit2
{
    // ------------------------------------------------------------------------------------------ sampling
    sealed class Samples
    {
        public Vec3[] P = [], N = []; public double[] W = []; public Vec3[] Verts = [];
        public double TotalW;
    }

    static Samples Sample(TriMesh m, IReadOnlyList<int> tris, int maxTris = 700, int maxVerts = 1200)
    {
        int stride = Math.Max(1, tris.Count / maxTris);
        var p = new List<Vec3>(); var n = new List<Vec3>(); var w = new List<double>();
        for (int i = 0; i < tris.Count; i += stride)
        {
            int t = tris[i]; double a = m.TriArea(t); if (a < 1e-14) continue;
            p.Add((m.P(m.T[3 * t]) + m.P(m.T[3 * t + 1]) + m.P(m.T[3 * t + 2])) / 3); n.Add(m.FaceNormal(t)); w.Add(a * stride);
        }
        var vs = new HashSet<int>(); foreach (int t in tris) { vs.Add(m.T[3 * t]); vs.Add(m.T[3 * t + 1]); vs.Add(m.T[3 * t + 2]); }
        int vstride = Math.Max(1, vs.Count / maxVerts); var verts = new List<Vec3>(); int k = 0;
        foreach (int v in vs.OrderBy(x => x)) { if (k++ % vstride == 0) verts.Add(m.P(v)); }
        return new Samples { P = [.. p], N = [.. n], W = [.. w], Verts = [.. verts], TotalW = w.Sum() };
    }

    internal static TriMesh SubMesh(TriMesh m, IReadOnlyList<int> tris)
    {
        var map = new Dictionary<int, int>(); var v = new List<float>(); var t = new int[tris.Count * 3];
        for (int i = 0; i < tris.Count; i++)
            for (int k = 0; k < 3; k++)
            {
                int old = m.T[3 * tris[i] + k];
                if (!map.TryGetValue(old, out int id)) { id = map.Count; map[old] = id; v.Add(m.V[3 * old]); v.Add(m.V[3 * old + 1]); v.Add(m.V[3 * old + 2]); }
                t[3 * i + k] = id;
            }
        return new TriMesh([.. v], t);
    }

    // ------------------------------------------------------------------------------------------ linear algebra helpers
    static double[]? SolveLinear(double[,] A, double[] b)
    {
        int n = b.Length; var M = new double[n, n + 1];
        for (int i = 0; i < n; i++) { for (int j = 0; j < n; j++) M[i, j] = A[i, j]; M[i, n] = b[i]; }
        for (int c = 0; c < n; c++)
        {
            int p = c; for (int r = c + 1; r < n; r++) if (Math.Abs(M[r, c]) > Math.Abs(M[p, c])) p = r;
            if (Math.Abs(M[p, c]) < 1e-14) return null;
            if (p != c) for (int k = 0; k <= n; k++) (M[c, k], M[p, k]) = (M[p, k], M[c, k]);
            for (int r = c + 1; r < n; r++) { double f = M[r, c] / M[c, c]; for (int k = c; k <= n; k++) M[r, k] -= f * M[c, k]; }
        }
        var x = new double[n];
        for (int r = n - 1; r >= 0; r--) { double s = M[r, n]; for (int k = r + 1; k < n; k++) s -= M[r, k] * x[k]; x[r] = s / M[r, r]; }
        return x;
    }

    /// <summary>Levenberg-Marquardt with a numeric Jacobian.</summary>
    static double[] LM(double[] x0, Func<double[], double[]> f, int iters = 25)
    {
        var x = (double[])x0.Clone(); var r = f(x); double cost = r.Sum(v => v * v), lambda = 1e-3; int n = x.Length, m = r.Length;
        for (int it = 0; it < iters; it++)
        {
            var J = new double[m, n];
            for (int j = 0; j < n; j++)
            {
                double h = 1e-6 * (1 + Math.Abs(x[j])); var xp = (double[])x.Clone(); xp[j] += h; var rp = f(xp);
                for (int i = 0; i < m; i++) J[i, j] = (rp[i] - r[i]) / h;
            }
            var JTJ = new double[n, n]; var g = new double[n];
            for (int a = 0; a < n; a++)
            {
                for (int i = 0; i < m; i++) g[a] += J[i, a] * r[i];
                for (int b = a; b < n; b++) { double s = 0; for (int i = 0; i < m; i++) s += J[i, a] * J[i, b]; JTJ[a, b] = JTJ[b, a] = s; }
            }
            bool ok = false;
            for (int tries = 0; tries < 8 && !ok; tries++)
            {
                var A = (double[,])JTJ.Clone(); for (int a = 0; a < n; a++) A[a, a] += lambda * (JTJ[a, a] + 1e-12);
                var dx = SolveLinear(A, g.Select(v => -v).ToArray());
                if (dx == null) { lambda *= 10; continue; }
                var xn = new double[n]; for (int a = 0; a < n; a++) xn[a] = x[a] + dx[a];
                var rn = f(xn); double cn = rn.Sum(v => v * v);
                if (cn < cost) { bool tiny = cost - cn < 1e-14 * (1 + cost); x = xn; r = rn; cost = cn; lambda = Math.Max(lambda / 3, 1e-9); ok = true; if (tiny) return x; }
                else lambda *= 4;
            }
            if (!ok) break;
        }
        return x;
    }

    static Vec3 Tilt(Vec3 a0, Vec3 b1, Vec3 b2, double t1, double t2) => (a0 + b1 * t1 + b2 * t2).Normalized();

    // ------------------------------------------------------------------------------------------ revolution axis
    /// <summary>
    /// The line that every surface normal meets: the axis of a torus or any surface of revolution (cylinders, cones and spheres too).
    /// Searches a spread of directions; for each, the point that best fits all normal lines projected along it, then hill-climbs the direction.
    /// </summary>
    internal static (Vec3 point, Vec3 axis, double resid)? FindRevolutionAxis(Vec3[] P, Vec3[] N)
    {
        int stride = Math.Max(1, P.Length / 500); var pp = new List<Vec3>(); var nn = new List<Vec3>();
        for (int i = 0; i < P.Length; i += stride) { pp.Add(P[i]); nn.Add(N[i]); }
        var cen = pp.Aggregate(Vec3.Zero, (s, v) => s + v) / pp.Count;
        for (int i = 0; i < pp.Count; i++) pp[i] -= cen;

        (Vec3 c, double res) Eval(Vec3 dir)
        {
            var (u, v) = SurfaceMath.Basis(dir);
            double sxx = 0, sxy = 0, syy = 0, bx = 0, by = 0; int used = 0;
            var rows = new List<(double ax, double ay, double rhs)>();
            for (int i = 0; i < pp.Count; i++)
            {
                double nx = Vec3.Dot(nn[i], u), ny = Vec3.Dot(nn[i], v), l = Math.Sqrt(nx * nx + ny * ny);
                if (l < 0.3) continue;
                double px = Vec3.Dot(pp[i], u), py = Vec3.Dot(pp[i], v);
                double ax = -ny / l, ay = nx / l, rhs = (nx * py - ny * px) / l;
                rows.Add((ax, ay, rhs)); sxx += ax * ax; sxy += ax * ay; syy += ay * ay; bx += ax * rhs; by += ay * rhs; used++;
            }
            if (used < Math.Max(8, pp.Count * 0.6)) return (Vec3.Zero, double.MaxValue);
            double tr = sxx + syy, disc = Math.Sqrt(Math.Max(tr * tr / 4 - (sxx * syy - sxy * sxy), 0)), lmin = tr / 2 - disc;
            if (lmin / used < 0.01) return (Vec3.Zero, double.MaxValue);       // normals (nearly) parallel in this view: no axis information
            double det = sxx * syy - sxy * sxy; double cx = (bx * syy - by * sxy) / det, cy = (sxx * by - sxy * bx) / det;
            double e = 0; foreach (var (ax, ay, rhs) in rows) { double d = ax * cx + ay * cy - rhs; e += d * d; }
            return (u * cx + v * cy, Math.Sqrt(e / used));
        }

        var cands = new List<(double res, Vec3 d, Vec3 c)>();
        const int count = 500; double golden = Math.PI * (3 - Math.Sqrt(5));
        for (int i = 0; i < count; i++)
        {
            double z = (i + 0.5) / count, rr = Math.Sqrt(1 - z * z), ph = i * golden;
            var d = new Vec3(rr * Math.Cos(ph), rr * Math.Sin(ph), z);
            var (c, res) = Eval(d); if (res < double.MaxValue) cands.Add((res, d, c));
        }
        if (cands.Count == 0) return null;
        // polish several well separated starting directions: a coarse grid can rank a wrong basin first
        var starts = new List<(double res, Vec3 d, Vec3 c)>();
        foreach (var cd in cands.OrderBy(x => x.res))
        {
            if (starts.All(s => Math.Abs(Vec3.Dot(s.d, cd.d)) < 0.985)) starts.Add(cd);
            if (starts.Count >= 8) break;
        }
        Vec3 best = Vec3.UnitZ; double bestRes = double.MaxValue; Vec3 bestC = Vec3.Zero;
        foreach (var st in starts)
        {
            var cur = st.d; double curRes = st.res; var curC = st.c; double step = 0.1;
            for (int it = 0; it < 120 && step > 1e-6; it++)
            {
                var (b1, b2) = SurfaceMath.Basis(cur); bool improved = false;
                foreach (var (s1, s2) in new[] { (step, 0.0), (-step, 0.0), (0.0, step), (0.0, -step) })
                {
                    var d = Tilt(cur, b1, b2, s1, s2); var (c, res) = Eval(d);
                    if (res < curRes) { curRes = res; cur = d; curC = c; improved = true; break; }
                }
                if (!improved) step *= 0.5;
            }
            if (curRes < bestRes) { bestRes = curRes; best = cur; bestC = curC; }
        }
        return (bestC + cen, best, bestRes);
    }

    // ------------------------------------------------------------------------------------------ torus
    /// <summary>Fits a torus (ring radius R, tube radius r) to the triangles; null when the surface is not one.</summary>
    public static FitRegion? FitTorus(TriMesh m, IReadOnlyList<int> tris, double tol)
    {
        var s = Sample(m, tris); if (s.P.Length < 12 || s.Verts.Length < 12) return null;
        var found = FindRevolutionAxis(s.P, s.N); if (found == null) return null;
        var (c0, a, _) = found.Value;
        // meridian circle in (rho, h)
        var xy = s.Verts.Select(v => { var d = v - c0; double h = Vec3.Dot(d, a); return (x: (d - a * h).Length, y: h); }).ToList();
        var circ = FitCircle2D(xy); if (circ == null) return null;
        var (cr, ch, r) = circ.Value;
        if (cr < 0.2 * r || r < 1e-6) return null;             // a ring that tight is a sphere, not a torus
        var centre = c0 + a * ch;
        var (u0, v0) = SurfaceMath.Basis(a);
        double[] Res(double[] x)
        {
            var ax = Tilt(a, u0, v0, x[3], x[4]); var res = new double[s.Verts.Length]; var c = new Vec3(x[0], x[1], x[2]);
            for (int i = 0; i < res.Length; i++) { var d = s.Verts[i] - c; double h = Vec3.Dot(d, ax), rho = (d - ax * h).Length; res[i] = Math.Sqrt((rho - x[5]) * (rho - x[5]) + h * h) - x[6]; }
            return res;
        }
        var x0 = new[] { centre.X, centre.Y, centre.Z, 0, 0, cr, r };
        var xr = LM(x0, Res);
        double R = xr[5], rr = xr[6];
        if (R < 0.2 * rr || rr < 1e-6) return null;
        var axis = Tilt(a, u0, v0, xr[3], xr[4]); var ctr = new Vec3(xr[0], xr[1], xr[2]);
        var proto = new FitRegion { Kind = PrimKind.Torus, Origin = ctr, Axis = axis, Radius = R, MinorRadius = rr };
        var reg = Finish(m, proto, tris);
        return reg.Rms <= tol ? reg : null;
    }

    /// <summary>Geometric circle fit: (centre x, centre y, radius).</summary>
    internal static (double cx, double cy, double r)? FitCircle2D(IReadOnlyList<(double x, double y)> pts)
    {
        if (pts.Count < 5) return null;
        double mx = pts.Average(p => p.x), my = pts.Average(p => p.y);
        var A = new double[3, 3]; var b = new double[3];
        foreach (var p in pts)
        {
            double x = p.x - mx, y = p.y - my; double[] row = [2 * x, 2 * y, 1]; double rhs = x * x + y * y;
            for (int i = 0; i < 3; i++) { for (int j = 0; j < 3; j++) A[i, j] += row[i] * row[j]; b[i] += row[i] * rhs; }
        }
        var sol = SolveLinear(A, b); if (sol == null) return null;
        double r2 = sol[2] + sol[0] * sol[0] + sol[1] * sol[1]; if (r2 <= 0) return null;
        double a0 = sol[0] + mx, b0 = sol[1] + my, r = Math.Sqrt(r2);
        // geometric polish
        for (int it = 0; it < 12; it++)
        {
            var N = new double[3, 3]; var g = new double[3];
            foreach (var p in pts)
            {
                double dx = p.x - a0, dy = p.y - b0, d = Math.Sqrt(dx * dx + dy * dy); if (d < 1e-12) continue;
                double[] J = [-dx / d, -dy / d, -1]; double res = d - r;
                for (int i = 0; i < 3; i++) { for (int j = 0; j < 3; j++) N[i, j] += J[i] * J[j]; g[i] += J[i] * res; }
            }
            for (int i = 0; i < 3; i++) N[i, i] += 1e-12;
            var dxv = SolveLinear(N, g.Select(v => -v).ToArray()); if (dxv == null) break;
            a0 += dxv[0]; b0 += dxv[1]; r += dxv[2];
            if (Math.Abs(dxv[0]) + Math.Abs(dxv[1]) + Math.Abs(dxv[2]) < 1e-10) break;
        }
        return r > 0 ? (a0, b0, r) : null;
    }

    // ------------------------------------------------------------------------------------------ extrusion
    /// <summary>Fits a prismatic surface: a constant cross-section swept along one axis. Null when the section changes along the axis.</summary>
    public static FitRegion? FitExtrusion(TriMesh m, IReadOnlyList<int> tris, double tol)
    {
        var s = Sample(m, tris); if (s.P.Length < 12) return null;
        double xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
        for (int i = 0; i < s.N.Length; i++) { var n = s.N[i]; double w = s.W[i]; xx += w * n.X * n.X; xy += w * n.X * n.Y; xz += w * n.X * n.Z; yy += w * n.Y * n.Y; yz += w * n.Y * n.Z; zz += w * n.Z * n.Z; }
        var (ev, vec) = MeshAnalysis.EigenSym([xx, xy, xz, yy, yz, zz]);
        if (Math.Sqrt(Math.Max(ev[2], 0) / s.TotalW) > 0.25) return null;      // normals do not all lie in one plane
        if (ev[1] / s.TotalW < 0.01) return null;                               // normals all alike: a plane
        var axis = vec[2];
        var o = s.Verts.Aggregate(Vec3.Zero, (acc, v) => acc + v) / s.Verts.Length;
        var sub = SubMesh(m, tris);

        // cross-section polyline at mid height, as plane coordinates
        (List<(double x, double y)> pts, bool closed, double mid)? Section(Vec3 ax)
        {
            var (u, v) = SurfaceMath.Basis(ax);
            double hmin = double.MaxValue, hmax = double.MinValue; foreach (var p in s.Verts) { double h = Vec3.Dot(p - o, ax); hmin = Math.Min(hmin, h); hmax = Math.Max(hmax, h); }
            if (hmax - hmin < 1e-6) return null;
            foreach (double f in new[] { 0.5137, 0.3137, 0.7137 })
            {
                double h = hmin + (hmax - hmin) * f;
                var loops = MeshOps.Section(sub, o + ax * h, ax);
                MeshOps.CutLoop? best = null; double bl = 0;
                foreach (var l in loops) { double len = 0; for (int i = 1; i < l.Points.Count; i++) len += (l.Points[i] - l.Points[i - 1]).Length; if (len > bl) { bl = len; best = l; } }
                if (best == null || best.Points.Count < 3) continue;
                var q = best.Points.Select(p => (x: Vec3.Dot(p - o, u), y: Vec3.Dot(p - o, v))).ToList();
                return (q, best.Closed, (hmin + hmax) / 2);
            }
            return null;
        }

        var sec = Section(axis); if (sec == null) return null;
        var profile0 = Poly2.Simplify(sec.Value.pts, tol * 0.25, sec.Value.closed);
        bool closed = sec.Value.closed;
        // polish the axis direction so the section is the same at every height
        var (b1, b2) = SurfaceMath.Basis(axis);
        double[] Res(double[] x, Vec3 ax)
        {
            var (u, v) = SurfaceMath.Basis(ax); var res = new double[s.Verts.Length];
            for (int i = 0; i < res.Length; i++) { var d = s.Verts[i] - o; res[i] = Poly2.Closest(profile0, closed, Vec3.Dot(d, u), Vec3.Dot(d, v)).d; }
            return res;
        }
        var xr = LM([0, 0], x => Res(x, Tilt(axis, b1, b2, x[0], x[1])), 12);
        var axis2 = Tilt(axis, b1, b2, xr[0], xr[1]);
        if (axis2.DistanceTo(axis) > 1e-9) { var sec2 = Section(axis2); if (sec2 != null) { axis = axis2; sec = sec2; } }

        var (uu, vv) = SurfaceMath.Basis(axis);
        double hlo = double.MaxValue, hhi = double.MinValue; foreach (var p in s.Verts) { double h = Vec3.Dot(p - o, axis); hlo = Math.Min(hlo, h); hhi = Math.Max(hhi, h); }
        double midH = (hlo + hhi) / 2; var origin = o + axis * midH;
        var prof2 = Poly2.Simplify(sec.Value.pts, tol * 0.25, sec.Value.closed);
        if (prof2.Count < 2) return null;
        // the section was cut at 'mid' of the earlier axis; recompute coordinates relative to the final origin
        var prof3 = prof2.Select(q => o + uu * q.x + vv * q.y + axis * midH).ToArray();
        var proto = new FitRegion { Kind = PrimKind.Extrusion, Origin = origin, Axis = axis, Profile = prof3, ProfileClosed = sec.Value.closed };
        var reg = Finish(m, proto, tris);
        return reg.Rms <= tol * 0.8 ? reg : null;
    }

    // ------------------------------------------------------------------------------------------ revolution
    /// <summary>Fits a surface of revolution: an axis and a profile polyline (generatrix) in a half plane through it.</summary>
    public static FitRegion? FitRevolution(TriMesh m, IReadOnlyList<int> tris, double tol)
    {
        var s = Sample(m, tris); if (s.P.Length < 12 || s.Verts.Length < 12) return null;
        var found = FindRevolutionAxis(s.P, s.N); if (found == null) return null;
        var (c0, axis, _) = found.Value;
        var sub = SubMesh(m, tris);

        (List<(double x, double y)> hr, bool closed)? Profile(Vec3 c, Vec3 a)
        {
            var (e0, w0) = SurfaceMath.Basis(a);
            // angular range actually occupied by the patch: the middle of the biggest empty gap's complement
            var angs = s.Verts.Select(v => { var d = v - c; return Math.Atan2(Vec3.Dot(d, w0), Vec3.Dot(d, e0)); }).OrderBy(x => x).ToList();
            double gap = -1, start = angs[0];
            for (int i = 0; i < angs.Count; i++) { double nxt = i + 1 < angs.Count ? angs[i + 1] : angs[0] + 2 * Math.PI; if (nxt - angs[i] > gap) { gap = nxt - angs[i]; start = nxt; } }
            double span = 2 * Math.PI - gap, th = start + span / 2 + 0.0137;      // nudged off mesh vertices that sit exactly on the cut plane
            var e = e0 * Math.Cos(th) + w0 * Math.Sin(th);
            var loops = MeshOps.Section(sub, c, Vec3.Cross(a, e));
            List<Vec3>? bestRun = null; double bestLen = 0; bool bestClosed = false;
            foreach (var l in loops)
            {
                int n = l.Points.Count; if (n < 2) continue;
                var pos = l.Points.Select(p => Vec3.Dot(p - c, e) > 0).ToArray();
                if (pos.All(b => b))
                {
                    double len = 0; for (int i = 1; i < n; i++) len += (l.Points[i] - l.Points[i - 1]).Length;
                    if (len > bestLen) { bestLen = len; bestRun = l.Points; bestClosed = l.Closed; }
                    continue;
                }
                int begin = 0;
                if (l.Closed) { begin = Array.IndexOf(pos, false); }       // start the walk on a negative point so runs do not wrap
                var run = new List<Vec3>();
                void Flush() { if (run.Count >= 2) { double len = 0; for (int i = 1; i < run.Count; i++) len += (run[i] - run[i - 1]).Length; if (len > bestLen) { bestLen = len; bestRun = [.. run]; bestClosed = false; } } run.Clear(); }
                for (int k = 0; k < n; k++)
                {
                    int i = (begin + k) % n;
                    if (pos[i]) run.Add(l.Points[i]); else Flush();
                }
                Flush();
            }
            if (bestRun == null) return null;
            var hr = bestRun.Select(p => { var d = p - c; double h = Vec3.Dot(d, a); return (x: h, y: (d - a * h).Length); }).ToList();
            return (hr, bestClosed);
        }

        var prof = Profile(c0, axis); if (prof == null) return null;
        var simp = Poly2.Simplify(prof.Value.hr, tol * 0.25, prof.Value.closed);
        bool closed = prof.Value.closed;
        // polish the axis (shift + tilt) against the profile, then rebuild the profile with it
        var (b1, b2) = SurfaceMath.Basis(axis);
        double[] Res(double[] x)
        {
            var a = Tilt(axis, b1, b2, x[2], x[3]); var c = c0 + b1 * x[0] + b2 * x[1]; var res = new double[s.Verts.Length];
            for (int i = 0; i < res.Length; i++) { var d = s.Verts[i] - c; double h = Vec3.Dot(d, a); res[i] = Poly2.Closest(simp, closed, h, (d - a * h).Length).d; }
            return res;
        }
        var xr = LM([0, 0, 0, 0], Res, 12);
        var axis2 = Tilt(axis, b1, b2, xr[2], xr[3]); var c2 = c0 + b1 * xr[0] + b2 * xr[1];
        var prof2 = Profile(c2, axis2);
        if (prof2 != null) { axis = axis2; c0 = c2; prof = prof2; }
        var final = Poly2.Simplify(prof.Value.hr, tol * 0.25, prof.Value.closed);
        if (final.Count < 2) return null;
        // profile back to 3D in the half plane at the occupied mid angle: reuse x = h, y = rho in the basis e0
        var (eB, _) = SurfaceMath.Basis(axis);
        var prof3 = final.Select(q => c0 + axis * q.x + eB * q.y).ToArray();
        double h0 = final.Min(q => q.x), h1 = final.Max(q => q.x);
        var proto = new FitRegion { Kind = PrimKind.Revolution, Origin = c0, Axis = axis, Profile = prof3, ProfileClosed = prof.Value.closed, T0 = h0, T1 = h1, Radius = final.Max(q => q.y) };
        var reg = Finish(m, proto, tris);
        return reg.Rms <= tol * 0.8 ? reg : null;
    }

    // ------------------------------------------------------------------------------------------ free-form patch
    /// <summary>
    /// Least-squares bicubic B-spline through the region's vertices, parameterised by projection onto the best-fit plane (so the region must be a
    /// height field over it; wrapped or folded regions return null). The control grid grows until the error is within tolerance (or maxCtrl).
    /// The result always carries its error report in Patch (Rms, MaxError, PercentInTolerance, Coverage).
    /// </summary>
    public static FitRegion? FitFreeform(TriMesh m, IReadOnlyList<int> tris, double tol, int maxCtrl = 14)
    {
        if (tris.Count < 4) return null;
        // data points: vertices (plus centroids for tiny regions)
        var vs = new HashSet<int>(); foreach (int t in tris) { vs.Add(m.T[3 * t]); vs.Add(m.T[3 * t + 1]); vs.Add(m.T[3 * t + 2]); }
        var pts = vs.OrderBy(x => x).Select(m.P).ToList();
        if (pts.Count < 200) foreach (int t in tris) pts.Add((m.P(m.T[3 * t]) + m.P(m.T[3 * t + 1]) + m.P(m.T[3 * t + 2])) / 3);
        int stride = Math.Max(1, pts.Count / 6000); if (stride > 1) pts = pts.Where((_, i) => i % stride == 0).ToList();
        if (pts.Count < 16) return null;
        var cen = pts.Aggregate(Vec3.Zero, (a, p) => a + p) / pts.Count;
        double xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
        foreach (var p in pts) { var d = p - cen; xx += d.X * d.X; xy += d.X * d.Y; xz += d.X * d.Z; yy += d.Y * d.Y; yz += d.Y * d.Z; zz += d.Z * d.Z; }
        var (_, ev) = MeshAnalysis.EigenSym([xx, xy, xz, yy, yz, zz]);
        var eu = ev[0]; var evv = ev[1]; var en = ev[2];
        // height-field test (area weighted)
        double good = 0, all = 0; foreach (int t in tris) { double a = m.TriArea(t); all += a; if (Math.Abs(Vec3.Dot(m.FaceNormal(t), en)) > 0.3) good += a; }
        if (all < 1e-12 || good / all < 0.92) return null;
        var uv = pts.Select(p => (u: Vec3.Dot(p - cen, eu), v: Vec3.Dot(p - cen, evv))).ToArray();
        double u0 = uv.Min(q => q.u), u1 = uv.Max(q => q.u), v0 = uv.Min(q => q.v), v1 = uv.Max(q => q.v);
        if (u1 - u0 < 1e-9 || v1 - v0 < 1e-9) return null;
        double aspect = (u1 - u0) / (v1 - v0);
        var st = uv.Select(q => (s: (q.u - u0) / (u1 - u0), t: (q.v - v0) / (v1 - v0))).ToArray();

        NurbsPatch? best = null;
        foreach (int nc in new[] { 4, 5, 6, 8, 10, 12, 14, 16 })
        {
            if (nc > maxCtrl) break;
            int nu = Math.Clamp((int)Math.Round(nc * Math.Sqrt(aspect)), 4, 20), nv = Math.Clamp((int)Math.Round(nc / Math.Sqrt(aspect)), 4, 20);
            if (nu * nv > pts.Count / 3) break;
            var patch = FitPatch(pts, st, nu, nv, cen, eu, evv, u0, u1, v0, v1, tol);
            if (patch == null) continue;
            if (best == null || patch.Rms < best.Rms) best = patch;
            if (patch.Rms <= tol * 0.4 && patch.MaxError <= tol * 2) { best = patch; break; }
        }
        if (best == null) return null;
        var proto = new FitRegion { Kind = PrimKind.Freeform, Origin = cen, Axis = en, Patch = best };
        return Finish(m, proto, tris);
    }

    static NurbsPatch? FitPatch(List<Vec3> pts, (double s, double t)[] st0, int nu, int nv, Vec3 cen, Vec3 eu, Vec3 ev, double u0, double u1, double v0, double v1, double tol)
    {
        const int p = 3; int K = nu * nv, M = pts.Count;
        var ku = NurbsPatch.Knots(nu, p); var kv = NurbsPatch.Knots(nv, p);
        var st = ((double s, double t)[])st0.Clone();
        Vec3[]? poles = null;
        for (int pass = 0; pass < 3; pass++)
        {
            var ata = new double[K, K]; var rhs = new double[K, 3];
            var bu = new double[p + 1]; var bv = new double[p + 1];
            for (int i = 0; i < M; i++)
            {
                int su = BSpline.FindSpan(nu - 1, p, st[i].s, ku), sv = BSpline.FindSpan(nv - 1, p, st[i].t, kv);
                BSpline.Basis(su, st[i].s, p, ku, bu); BSpline.Basis(sv, st[i].t, p, kv, bv);
                for (int a = 0; a <= p; a++)
                    for (int b = 0; b <= p; b++)
                    {
                        int ia = (su - p + a) * nv + (sv - p + b); double wa = bu[a] * bv[b];
                        rhs[ia, 0] += wa * pts[i].X; rhs[ia, 1] += wa * pts[i].Y; rhs[ia, 2] += wa * pts[i].Z;
                        for (int c = 0; c <= p; c++)
                            for (int d = 0; d <= p; d++) ata[ia, (su - p + c) * nv + (sv - p + d)] += wa * bu[c] * bv[d];
                    }
            }
            // second-difference smoothing keeps empty parts of the grid well defined
            double lam = 5e-4 * M / K;
            void Smooth(int i0, int i1, int i2)
            {
                int[] idx = [i0, i1, i2]; double[] w = [1, -2, 1];
                for (int a = 0; a < 3; a++) for (int b = 0; b < 3; b++) ata[idx[a], idx[b]] += lam * w[a] * w[b];
            }
            for (int i = 0; i < nu; i++) for (int j = 0; j < nv; j++)
                {
                    if (i + 2 < nu) Smooth(i * nv + j, (i + 1) * nv + j, (i + 2) * nv + j);
                    if (j + 2 < nv) Smooth(i * nv + j, i * nv + j + 1, i * nv + j + 2);
                }
            double tr = 0; for (int i = 0; i < K; i++) tr += ata[i, i];
            for (int i = 0; i < K; i++) ata[i, i] += 1e-9 * tr / K + 1e-12;
            // Cholesky
            var L = new double[K, K];
            for (int i = 0; i < K; i++)
                for (int j = 0; j <= i; j++)
                {
                    double sum = ata[i, j]; for (int k = 0; k < j; k++) sum -= L[i, k] * L[j, k];
                    if (i == j) { if (sum <= 0) return null; L[i, i] = Math.Sqrt(sum); } else L[i, j] = sum / L[j, j];
                }
            poles = new Vec3[K]; var sol = new double[K, 3];
            for (int c = 0; c < 3; c++)
            {
                var y = new double[K];
                for (int i = 0; i < K; i++) { double sum = rhs[i, c]; for (int k = 0; k < i; k++) sum -= L[i, k] * y[k]; y[i] = sum / L[i, i]; }
                for (int i = K - 1; i >= 0; i--) { double sum = y[i]; for (int k = i + 1; k < K; k++) sum -= L[k, i] * sol[k, c]; sol[i, c] = sum / L[i, i]; }
            }
            for (int i = 0; i < K; i++) poles[i] = new Vec3(sol[i, 0], sol[i, 1], sol[i, 2]);
            if (pass == 2) break;
            // parameter correction: move each point's (s, t) toward its foot point on the current surface
            var cur = new NurbsPatch { NU = nu, NV = nv, Poles = poles, DegU = p, DegV = p };
            for (int i = 0; i < M; i++) { double s = st[i].s, t = st[i].t; NurbsPatch.NewtonStep(cur, pts[i], ref s, ref t); st[i] = (s, t); }
        }
        var final = new NurbsPatch { NU = nu, NV = nv, Poles = poles!, DegU = p, DegV = p };
        double se = 0, mx = 0; int within = 0; var errs = new double[M];
        for (int i = 0; i < M; i++)
        {
            double s = st[i].s, t = st[i].t; NurbsPatch.NewtonStep(final, pts[i], ref s, ref t);
            double e = (final.EvalNorm(s, t) - pts[i]).Length; errs[i] = e; se += e * e; mx = Math.Max(mx, e); if (e <= tol) within++;
        }
        var occupied = new bool[10, 10]; foreach (var q in st0) occupied[Math.Min((int)(q.s * 10), 9), Math.Min((int)(q.t * 10), 9)] = true;
        int cells = 0; foreach (bool o in occupied) if (o) cells++;
        return new NurbsPatch
        {
            NU = nu, NV = nv, Poles = poles!, DegU = p, DegV = p, Origin = cen, AxisU = eu, AxisV = ev, U0 = u0, U1 = u1, V0 = v0, V1 = v1,
            Rms = Math.Sqrt(se / M), MaxError = mx, PercentInTolerance = 100.0 * within / M, Coverage = cells / 100.0, Samples = M,
        };
    }

    // ------------------------------------------------------------------------------------------ driver
    /// <summary>Tries torus, extrusion, revolution (in that order) and finally a free-form patch. Null when nothing fits.</summary>
    public static FitRegion? FitBest(TriMesh m, IReadOnlyList<int> tris, double tol, bool freeform = true)
        => FitTorus(m, tris, tol) ?? FitExtrusion(m, tris, tol) ?? FitRevolution(m, tris, tol) ?? (freeform ? FitFreeform(m, tris, tol) : null);

    /// <summary>
    /// Called by PrimitiveFit.Detect: (1) smooth chains of curved regions plus left-over triangles are tested as one torus, replacing the fragments
    /// a local cylinder/sphere fit made of it; (2) smooth left-over patches are tested as extrusion, revolution, then free-form.
    /// </summary>
    internal static void Extras(PrimitiveFit.Ctx cx, PrimitiveFit.Options o, List<FitRegion> regions, bool[] assigned)
    {
        var m = cx.M; int nt = m.TriCount;
        double totalArea = 0; for (int t = 0; t < nt; t++) totalArea += cx.A[t];
        double cosSmooth = Math.Cos(35 * Math.PI / 180);
        var owner = new int[nt]; Array.Fill(owner, -1);
        for (int i = 0; i < regions.Count; i++) foreach (int t in regions[i].Tris) owner[t] = i;
        var slots = regions.Select(r => (FitRegion?)r).ToList();

        List<List<int>> Components(Func<int, bool> eligible)
        {
            var seen = new bool[nt]; var res = new List<List<int>>();
            for (int s = 0; s < nt; s++)
            {
                if (seen[s] || cx.A[s] < 1e-12 || !eligible(s)) continue;
                var comp = new List<int> { s }; seen[s] = true;
                for (int i = 0; i < comp.Count; i++)
                    for (int k = 0; k < 3; k++)
                    {
                        int nb = cx.Nb[3 * comp[i] + k];
                        if (nb >= 0 && !seen[nb] && cx.A[nb] > 1e-12 && eligible(nb) && Vec3.Dot(cx.SN[comp[i]], cx.SN[nb]) >= cosSmooth) { seen[nb] = true; comp.Add(nb); }
                    }
                res.Add(comp);
            }
            return res;
        }
        bool Curved(int t) => owner[t] < 0 || regions[owner[t]].Kind is PrimKind.Cylinder or PrimKind.Sphere or PrimKind.Cone;

        // (1) torus over fragments
        foreach (var comp in Components(t => Curved(t) && (owner[t] < 0 || slots[owner[t]] != null)).OrderByDescending(c => c.Sum(t => cx.A[t])))
        {
            var members = comp.Select(t => owner[t]).Where(i => i >= 0).Distinct().ToList();
            int left = comp.Count(t => owner[t] < 0);
            if (members.Count + (left >= o.MinTris ? 1 : 0) < 2 && !(members.Count == 0 && left >= o.MinTris)) continue;
            double area = comp.Sum(t => cx.A[t]); if (area < totalArea * 0.004) continue;
            var tor = FitTorus(m, comp, o.Tolerance * 0.6);
            // three or more fragments of one smooth surface may be a wavy extrusion or a vase-like revolved surface
            if (tor == null && members.Count + (left >= o.MinTris ? 1 : 0) >= 3)
                tor = FitExtrusion(m, comp, o.Tolerance * 0.75) ?? FitRevolution(m, comp, o.Tolerance * 0.75);
            if (tor == null) continue;
            foreach (int i in members) slots[i] = null;
            slots.Add(tor); foreach (int t in comp) { owner[t] = slots.Count - 1; assigned[t] = true; }
        }

        // (2) left-over smooth patches
        var more = new List<FitRegion>();
        foreach (var comp in Components(t => owner[t] < 0).OrderByDescending(c => c.Sum(t => cx.A[t])))
        {
            double area = comp.Sum(t => cx.A[t]);
            if (comp.Count < Math.Max(o.MinTris, 12) || area < totalArea * 0.005) continue;
            var reg = FitExtrusion(m, comp, o.Tolerance) ?? FitRevolution(m, comp, o.Tolerance) ?? FitFreeform(m, comp, o.Tolerance);
            if (reg == null) continue;
            more.Add(reg); foreach (int t in comp) assigned[t] = true;
        }
        regions.Clear();
        regions.AddRange(slots.Where(r => r != null)!);
        regions.AddRange(more);
    }

    // ------------------------------------------------------------------------------------------ region statistics
    static Vec3 OutwardRef(FitRegion r, Vec3 p)
    {
        switch (r.Kind)
        {
            case PrimKind.Plane: case PrimKind.Freeform: return r.Axis;
            case PrimKind.Extrusion: { var d = p - r.Origin; var rad = d - r.Axis * Vec3.Dot(d, r.Axis); return rad; }
            case PrimKind.Revolution: { var d = p - r.Origin; return d - r.Axis * Vec3.Dot(d, r.Axis); }
            default: return SurfaceMath.Normal(r, p) ?? r.Axis;
        }
    }

    /// <summary>Copies the surface parameters of proto, attaches the triangles and computes area, bounds, axial extent, RMS / max deviation and convexity.</summary>
    public static FitRegion Finish(TriMesh m, FitRegion proto, IReadOnlyList<int> tris)
    {
        double area = 0, se = 0, sw = 0, maxDev = 0, t0 = double.MaxValue, t1 = double.MinValue; int outward = 0, counted = 0;
        var mn = new Vec3(double.MaxValue, double.MaxValue, double.MaxValue); var mx = -mn;
        int stride = Math.Max(1, tris.Count / 600); bool axial = proto.Kind is PrimKind.Cylinder or PrimKind.Cone or PrimKind.Extrusion or PrimKind.Revolution;
        for (int i = 0; i < tris.Count; i++)
        {
            int t = tris[i]; double a = m.TriArea(t); area += a;
            for (int k = 0; k < 3; k++)
            {
                var v = m.P(m.T[3 * t + k]);
                mn = new(Math.Min(mn.X, v.X), Math.Min(mn.Y, v.Y), Math.Min(mn.Z, v.Z)); mx = new(Math.Max(mx.X, v.X), Math.Max(mx.Y, v.Y), Math.Max(mx.Z, v.Z));
                if (axial) { double h = Vec3.Dot(v - proto.Origin, proto.Axis); t0 = Math.Min(t0, h); t1 = Math.Max(t1, h); }
            }
            if (i % stride != 0 || a < 1e-14) continue;
            var c = (m.P(m.T[3 * t]) + m.P(m.T[3 * t + 1]) + m.P(m.T[3 * t + 2])) / 3;
            double d = SurfaceMath.Distance(proto, c); se += d * d * a; sw += a; maxDev = Math.Max(maxDev, d);
            counted++; if (Vec3.Dot(m.FaceNormal(t), OutwardRef(proto, c)) > 0) outward++;
        }
        if (proto.Kind is PrimKind.Extrusion or PrimKind.Revolution && proto.Kind == PrimKind.Revolution && proto.T1 > proto.T0) { t0 = proto.T0; t1 = proto.T1; }
        double rms = proto.Patch != null ? proto.Patch.Rms : Math.Sqrt(se / Math.Max(sw, 1e-12));
        return new FitRegion
        {
            Kind = proto.Kind, Tris = [.. tris], Origin = proto.Origin, Axis = proto.Axis, Radius = proto.Radius, HalfAngle = proto.HalfAngle, MinorRadius = proto.MinorRadius,
            Profile = proto.Profile, ProfileClosed = proto.ProfileClosed, Patch = proto.Patch, T0 = t0 == double.MaxValue ? 0 : t0, T1 = t1 == double.MinValue ? 0 : t1,
            Rms = rms, MaxDev = proto.Patch != null ? proto.Patch.MaxError : maxDev, Area = area, Convex = counted == 0 || outward * 2 >= counted, Min = mn, Max = mx,
        };
    }
}
