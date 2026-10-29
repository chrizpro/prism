using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Kernel;

namespace Prism.Meshing;

public sealed record MeshReport(
    int Vertices, int Triangles, int Shells, int BoundaryEdges, int HoleLoops, int NonManifoldEdges, int InconsistentEdges,
    int Degenerate, int Duplicate, double Volume, double Area, Vec3 Size, bool Closed)
{
    /// <summary>Printable = watertight, manifold, consistently oriented, positive volume.</summary>
    public bool Printable => Closed && NonManifoldEdges == 0 && InconsistentEdges == 0 && Volume > 0;
}

/// <summary>Measurements on meshes: health report, wall thickness, overhangs, deviation between meshes and printing orientation.</summary>
public static class MeshAnalysis
{
    public static MeshReport Report(TriMesh m)
    {
        var topo = m.Topology();
        int deg = 0; var seen = new HashSet<(int, int, int)>(); int dup = 0;
        for (int t = 0; t < m.TriCount; t++)
        {
            int a = m.T[3 * t], b = m.T[3 * t + 1], c = m.T[3 * t + 2];
            if (a == b || b == c || a == c || m.TriArea(t) < 1e-12) { deg++; continue; }
            int x = Math.Min(a, Math.Min(b, c)), z = Math.Max(a, Math.Max(b, c)), y = a + b + c - x - z;
            if (!seen.Add((x, y, z))) dup++;
        }
        MeshOps.Components(m, out int shells);
        return new MeshReport(m.VertexCount, m.TriCount, shells, topo.BoundaryEdges, topo.HoleLoops.Count, topo.NonManifoldEdges, topo.InconsistentEdges,
            deg, dup, m.Volume(), m.Area(), m.Size, topo.BoundaryEdges == 0 && m.TriCount > 0);
    }

    /// <summary>Hole outlines as polylines (for highlighting).</summary>
    public static List<List<Vec3>> HolePolylines(TriMesh m) =>
        m.Topology().HoleLoops.Select(l => l.Select(m.P).Append(m.P(l[0])).ToList()).ToList();

    /// <summary>Segments of edges shared by more than two triangles.</summary>
    public static List<(Vec3 a, Vec3 b)> NonManifoldSegments(TriMesh m)
    {
        var cnt = new Dictionary<long, (int n, int a, int b)>(); int nv = Math.Max(m.VertexCount, 1);
        for (int t = 0; t < m.TriCount; t++)
            for (int k = 0; k < 3; k++)
            {
                int a = m.T[3 * t + k], b = m.T[3 * t + (k + 1) % 3]; var key = TriMesh.EdgeKey(a, b, nv);
                cnt[key] = cnt.TryGetValue(key, out var c) ? (c.n + 1, a, b) : (1, a, b);
            }
        return cnt.Values.Where(c => c.n > 2).Select(c => (m.P(c.a), m.P(c.b))).ToList();
    }

    // ---------------------------------------------------------------- wall thickness
    /// <summary>Distance from each vertex straight through the part (ray along the inward normal). Rays that escape give maxDist.</summary>
    public static float[] Thickness(TriMesh m, double maxDist)
    {
        var n = m.VertexNormals(); var bvh = m.Bvh; var res = new float[m.VertexCount];
        double eps = Math.Max(m.Size.Length * 1e-5, 1e-6);
        System.Threading.Tasks.Parallel.For(0, m.VertexCount, i =>
        {
            var nn = new Vec3(n[3 * i], n[3 * i + 1], n[3 * i + 2]);
            if (nn.Length < 0.5) { res[i] = (float)maxDist; return; }
            // five rays in a narrow cone, median distance: a single ray grazing an edge or a noisy sliver would report zero
            var u = Math.Abs(nn.X) < 0.9 ? Vec3.Cross(nn, Vec3.UnitX).Normalized() : Vec3.Cross(nn, Vec3.UnitY).Normalized(); var w = Vec3.Cross(nn, u);
            Span<double> d = stackalloc double[5];
            var o = m.P(i) - nn * eps;
            for (int k = 0; k < 5; k++)
            {
                var dir = -nn;
                if (k > 0) { double a = (k - 1) * Math.PI / 2; dir = (-nn + (u * Math.Cos(a) + w * Math.Sin(a)) * 0.25).Normalized(); }
                var hit = bvh.Raycast(o, dir, maxDist);
                d[k] = hit is { } h ? h.t * Math.Abs(Vec3.Dot(dir, -nn)) + eps : maxDist;     // project to the wall normal
            }
            d.Sort();
            res[i] = (float)d[2];
        });
        return res;
    }

    // ---------------------------------------------------------------- overhangs
    public sealed record OverhangResult(int[] Label, double TotalArea, double SupportArea, double BedArea, double MarginalArea, double SupportVolumeUpper, double Height);

    /// <summary>Classifies every triangle for a print with build direction +Z: 0 fine, 1 marginal, 2 needs support, 3 touches the bed.</summary>
    public static OverhangResult Overhang(TriMesh m, double maxAngleDeg, double bedTol = 0.05)
    {
        var fn = m.FaceNormals; var label = new int[m.TriCount];
        double sinMax = Math.Sin(maxAngleDeg * Math.PI / 180), sinMarg = Math.Sin(Math.Max(maxAngleDeg - 10, 0) * Math.PI / 180);
        double minZ = m.Min.Z, total = 0, support = 0, bed = 0, marg = 0, vol = 0;
        for (int t = 0; t < m.TriCount; t++)
        {
            double a = m.TriArea(t); total += a;
            double down = -fn[3 * t + 2];     // 1 = ceiling, 0 = vertical wall
            double zc = (m.V[3 * m.T[3 * t] + 2] + m.V[3 * m.T[3 * t + 1] + 2] + m.V[3 * m.T[3 * t + 2] + 2]) / 3;
            if (down > 0.9 && zc - minZ < Math.Max(bedTol, m.Size.Z * 0.01)) { label[t] = 3; bed += a; }
            else if (down > sinMax) { label[t] = 2; support += a; vol += a * Math.Max(down, 0) * Math.Max(zc - minZ, 0); }
            else if (down > sinMarg) { label[t] = 1; marg += a; }
        }
        return new OverhangResult(label, total, support, bed, marg, vol, m.Size.Z);
    }

    /// <summary>Tries lying the part on each of its big flat faces and each axis; returns candidates best first.</summary>
    public static List<(Vec3 down, double support, double height, double contact)> OrientCandidates(TriMesh m, double maxAngleDeg, int max = 8)
    {
        var fn = m.FaceNormals; var dirs = new List<Vec3> { Vec3.UnitX, -Vec3.UnitX, Vec3.UnitY, -Vec3.UnitY, Vec3.UnitZ, -Vec3.UnitZ };
        // big flat faces: bin face normals by ~6 degrees and weight by area
        var bins = new Dictionary<(int, int, int), (Vec3 n, double a)>();
        for (int t = 0; t < m.TriCount; t++)
        {
            var n = new Vec3(fn[3 * t], fn[3 * t + 1], fn[3 * t + 2]); if (n.Length < 0.5) continue;
            var key = ((int)Math.Round(n.X * 10), (int)Math.Round(n.Y * 10), (int)Math.Round(n.Z * 10));
            double a = m.TriArea(t);
            bins[key] = bins.TryGetValue(key, out var b) ? (b.n + n * a, b.a + a) : (n * a, a);
        }
        foreach (var b in bins.Values.OrderByDescending(b => b.a).Take(24)) dirs.Add(b.n.Normalized());
        var result = new List<(Vec3 down, double support, double height, double contact)>();
        foreach (var d in dirs)
        {
            bool dup = false; foreach (var r in result) if (Vec3.Dot(r.down, d) > 0.995) { dup = true; break; }
            if (dup) continue;
            // "down" is the outward normal of the face resting on the bed: the part's up is -d
            double sinMax = Math.Sin(maxAngleDeg * Math.PI / 180), support = 0, contact = 0, minH = double.MaxValue, maxH = double.MinValue;
            var up = -d;
            for (int i = 0; i < m.VertexCount; i++) { double h = Vec3.Dot(m.P(i), up); minH = Math.Min(minH, h); maxH = Math.Max(maxH, h); }
            for (int t = 0; t < m.TriCount; t++)
            {
                var n = new Vec3(fn[3 * t], fn[3 * t + 1], fn[3 * t + 2]); double a = m.TriArea(t);
                double downness = -Vec3.Dot(n, up);
                double hc = Vec3.Dot((m.P(m.T[3 * t]) + m.P(m.T[3 * t + 1]) + m.P(m.T[3 * t + 2])) / 3, up);
                if (downness > 0.9 && hc - minH < Math.Max(0.05, (maxH - minH) * 0.01)) contact += a;
                else if (downness > sinMax) support += a;
            }
            result.Add((d, support, maxH - minH, contact));
        }
        // less support first, then more contact, then lower height
        return result.OrderBy(r => r.support).ThenByDescending(r => r.contact).ThenBy(r => r.height).Take(max).ToList();
    }

    /// <summary>Rotation (axis, degrees) taking direction a onto direction b.</summary>
    public static (Vec3 axis, double deg) Align(Vec3 a, Vec3 b)
    {
        a = a.Normalized(); b = b.Normalized();
        var ax = Vec3.Cross(a, b); double s = ax.Length, c = Vec3.Dot(a, b);
        if (s < 1e-9) return c > 0 ? (Vec3.UnitZ, 0) : (Math.Abs(a.X) < 0.9 ? Vec3.Cross(a, Vec3.UnitX).Normalized() : Vec3.Cross(a, Vec3.UnitY).Normalized(), 180);
        return (ax / s, Math.Atan2(s, c) * 180 / Math.PI);
    }

    // ---------------------------------------------------------------- deviation
    public sealed record DeviationResult(float[] PerVertex, double Max, double Min, double Mean, double Rms, double AbsMax, double WithinTolerance, int Count);

    /// <summary>Signed distance from each vertex of m to the reference surface (positive = outside the reference, along its normals).</summary>
    public static DeviationResult Deviation(TriMesh m, TriMesh reference, double tolerance)
    {
        var res = new float[m.VertexCount]; var bvh = reference.Bvh; var rn = reference.FaceNormals;
        System.Threading.Tasks.Parallel.For(0, m.VertexCount, i =>
        {
            var p = m.P(i);
            if (bvh.ClosestPoint(p) is not { } c) { res[i] = 0; return; }
            var n = new Vec3(rn[3 * c.Tri], rn[3 * c.Tri + 1], rn[3 * c.Tri + 2]);
            double sign = Vec3.Dot(p - c.Point, n) >= 0 ? 1 : -1;
            res[i] = (float)(sign * c.Dist);
        });
        double max = double.MinValue, min = double.MaxValue, sum = 0, sq = 0, abs = 0; int within = 0;
        foreach (var d in res) { max = Math.Max(max, d); min = Math.Min(min, d); sum += d; sq += d * d; abs = Math.Max(abs, Math.Abs(d)); if (Math.Abs(d) <= tolerance) within++; }
        int n2 = Math.Max(res.Length, 1);
        return new DeviationResult(res, max, min, sum / n2, Math.Sqrt(sq / n2), abs, 100.0 * within / n2, res.Length);
    }

    // ---------------------------------------------------------------- alignment (ICP)
    public static double[] Identity() => [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];

    public static Vec3 Apply(double[] M, Vec3 p) => new(
        M[0] * p.X + M[1] * p.Y + M[2] * p.Z + M[3], M[4] * p.X + M[5] * p.Y + M[6] * p.Z + M[7], M[8] * p.X + M[9] * p.Y + M[10] * p.Z + M[11]);

    public static double[] Mul(double[] A, double[] B)
    {
        var C = new double[16];
        for (int i = 0; i < 4; i++) for (int j = 0; j < 4; j++) { double s = 0; for (int k = 0; k < 4; k++) s += A[4 * i + k] * B[4 * k + j]; C[4 * i + j] = s; }
        return C;
    }

    public sealed record IcpResult(double[] Transform, double RmsBefore, double RmsAfter, int Iterations);

    /// <summary>Point-to-plane ICP of m onto reference. Optionally pre-aligns centroids and tries the four principal-axis flips.</summary>
    public static IcpResult Icp(TriMesh m, TriMesh reference, int iterations, double trim, bool prealign, int maxPoints = 15000)
    {
        int step = Math.Max(1, m.VertexCount / maxPoints);
        var pts = new List<Vec3>(); for (int i = 0; i < m.VertexCount; i += step) pts.Add(m.P(i));
        var rn = reference.FaceNormals; var bvh = reference.Bvh;
        double Rms(double[] M)
        {
            double s = 0; int n = 0;
            foreach (var p in pts) { var c = bvh.ClosestPoint(Apply(M, p)); if (c != null) { s += c.Value.Dist * c.Value.Dist; n++; } }
            return n == 0 ? 0 : Math.Sqrt(s / n);
        }
        double[] Refine(double[] M0, int iters)
        {
            var M = (double[])M0.Clone();
            for (int it = 0; it < iters; it++)
            {
                var pairs = new List<(Vec3 p, Vec3 q, Vec3 n, double d)>(pts.Count);
                foreach (var p0 in pts)
                {
                    var p = Apply(M, p0); var c = bvh.ClosestPoint(p);
                    if (c == null) continue;
                    pairs.Add((p, c.Value.Point, new Vec3(rn[3 * c.Value.Tri], rn[3 * c.Value.Tri + 1], rn[3 * c.Value.Tri + 2]), c.Value.Dist));
                }
                if (pairs.Count < 6) break;
                pairs.Sort((a, b) => a.d.CompareTo(b.d));
                int use = Math.Max(6, (int)(pairs.Count * (1 - Math.Clamp(trim, 0, 0.9))));
                var A = new double[6, 7];
                for (int k = 0; k < use; k++)
                {
                    var (p, q, n, _) = pairs[k]; var c = Vec3.Cross(p, n); double r = Vec3.Dot(p - q, n);
                    double[] row = [c.X, c.Y, c.Z, n.X, n.Y, n.Z];
                    for (int i = 0; i < 6; i++) { for (int j = 0; j < 6; j++) A[i, j] += row[i] * row[j]; A[i, 6] -= row[i] * r; }
                }
                var x = Solve6(A);
                if (x == null) break;
                double a = x[0], b = x[1], g = x[2];
                double[] dM = [1, -g, b, x[3], g, 1, -a, x[4], -b, a, 1, x[5], 0, 0, 0, 1];
                // orthonormalise the small rotation
                var rot = Rodrigues(new Vec3(a, b, g)); dM[0] = rot[0]; dM[1] = rot[1]; dM[2] = rot[2]; dM[4] = rot[3]; dM[5] = rot[4]; dM[6] = rot[5]; dM[8] = rot[6]; dM[9] = rot[7]; dM[10] = rot[8];
                M = Mul(dM, M);
                if (Math.Abs(a) + Math.Abs(b) + Math.Abs(g) + Math.Abs(x[3]) + Math.Abs(x[4]) + Math.Abs(x[5]) < 1e-9) break;
            }
            return M;
        }

        var start = Identity();
        double before = Rms(start);
        if (prealign)
        {
            var cm = m.CenterOfMass(); var cr = reference.CenterOfMass();
            var (em, vm) = Principal(m); var (er, vr) = Principal(reference);
            // the untouched position is a candidate too: already-close scans should not be thrown off by a symmetric look-alike
            var best = Refine(Identity(), 15); double bestRms = Rms(best);
            foreach (var (s1, s2) in new[] { (1, 1), (1, -1), (-1, 1), (-1, -1) })
            {
                // rotation taking mesh principal frame onto the reference frame (third axis keeps right-handedness)
                var a0 = vm[0] * s1; var a1 = vm[1] * s2; var a2 = Vec3.Cross(a0, a1);
                var b0 = vr[0]; var b1 = vr[1]; var b2 = Vec3.Cross(b0, b1);
                // R = B * A^T
                double[] R = new double[9]; Vec3[] A3 = [a0, a1, a2], B3 = [b0, b1, b2];
                static double At(Vec3 v, int i) => i == 0 ? v.X : i == 1 ? v.Y : v.Z;
                for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) R[3 * i + j] = At(B3[0], i) * At(A3[0], j) + At(B3[1], i) * At(A3[1], j) + At(B3[2], i) * At(A3[2], j);
                var rotated = Apply3(R, cm);
                double[] M = [R[0], R[1], R[2], cr.X - rotated.X, R[3], R[4], R[5], cr.Y - rotated.Y, R[6], R[7], R[8], cr.Z - rotated.Z, 0, 0, 0, 1];
                var refined = Refine(M, 15); double r = Rms(refined);
                if (r < bestRms) { bestRms = r; best = refined; }
            }
            start = best;
        }
        var final = Refine(start, iterations);
        return new IcpResult(final, before, Rms(final), iterations);
    }

    static Vec3 Apply3(double[] R, Vec3 p) => new(R[0] * p.X + R[1] * p.Y + R[2] * p.Z, R[3] * p.X + R[4] * p.Y + R[5] * p.Z, R[6] * p.X + R[7] * p.Y + R[8] * p.Z);

    static double[] Rodrigues(Vec3 w)
    {
        double th = w.Length;
        if (th < 1e-12) return [1, 0, 0, 0, 1, 0, 0, 0, 1];
        var k = w / th; double c = Math.Cos(th), s = Math.Sin(th), v = 1 - c;
        return [c + k.X * k.X * v, k.X * k.Y * v - k.Z * s, k.X * k.Z * v + k.Y * s,
                k.Y * k.X * v + k.Z * s, c + k.Y * k.Y * v, k.Y * k.Z * v - k.X * s,
                k.Z * k.X * v - k.Y * s, k.Z * k.Y * v + k.X * s, c + k.Z * k.Z * v];
    }

    static double[]? Solve6(double[,] A)
    {
        int n = 6;
        for (int c = 0; c < n; c++)
        {
            int p = c; for (int r = c + 1; r < n; r++) if (Math.Abs(A[r, c]) > Math.Abs(A[p, c])) p = r;
            if (Math.Abs(A[p, c]) < 1e-12) A[p, c] += 1e-9;     // regularise sliding directions
            if (p != c) for (int k = 0; k <= n; k++) (A[c, k], A[p, k]) = (A[p, k], A[c, k]);
            for (int r = c + 1; r < n; r++) { double f = A[r, c] / A[c, c]; for (int k = c; k <= n; k++) A[r, k] -= f * A[c, k]; }
        }
        var x = new double[n];
        for (int r = n - 1; r >= 0; r--) { double s = A[r, n]; for (int k = r + 1; k < n; k++) s -= A[r, k] * x[k]; x[r] = s / A[r, r]; }
        return x.Any(double.IsNaN) ? null : x;
    }

    /// <summary>Principal axes of the vertex cloud (largest spread first) and their variances.</summary>
    public static (double[] variance, Vec3[] axes) Principal(TriMesh m)
    {
        var c = m.Centroid(); double xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0; int n = m.VertexCount;
        for (int i = 0; i < n; i++) { var d = m.P(i) - c; xx += d.X * d.X; xy += d.X * d.Y; xz += d.X * d.Z; yy += d.Y * d.Y; yz += d.Y * d.Z; zz += d.Z * d.Z; }
        return EigenSym([xx / n, xy / n, xz / n, yy / n, yz / n, zz / n]);
    }

    /// <summary>Jacobi eigen decomposition of a symmetric 3x3 (xx, xy, xz, yy, yz, zz). Eigenvalues sorted descending.</summary>
    public static (double[] values, Vec3[] vectors) EigenSym(double[] s)
    {
        double[,] a = { { s[0], s[1], s[2] }, { s[1], s[3], s[4] }, { s[2], s[4], s[5] } };
        double[,] v = { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
        for (int sweep = 0; sweep < 50; sweep++)
        {
            double off = Math.Abs(a[0, 1]) + Math.Abs(a[0, 2]) + Math.Abs(a[1, 2]);
            if (off < 1e-15) break;
            for (int p = 0; p < 2; p++)
                for (int q = p + 1; q < 3; q++)
                {
                    if (Math.Abs(a[p, q]) < 1e-18) continue;
                    double th = (a[q, q] - a[p, p]) / (2 * a[p, q]);
                    double t = Math.Sign(th == 0 ? 1 : th) / (Math.Abs(th) + Math.Sqrt(th * th + 1));
                    double c = 1 / Math.Sqrt(t * t + 1), sn = t * c;
                    for (int k = 0; k < 3; k++) { double akp = a[k, p], akq = a[k, q]; a[k, p] = c * akp - sn * akq; a[k, q] = sn * akp + c * akq; }
                    for (int k = 0; k < 3; k++) { double apk = a[p, k], aqk = a[q, k]; a[p, k] = c * apk - sn * aqk; a[q, k] = sn * apk + c * aqk; }
                    for (int k = 0; k < 3; k++) { double vkp = v[k, p], vkq = v[k, q]; v[k, p] = c * vkp - sn * vkq; v[k, q] = sn * vkp + c * vkq; }
                }
        }
        var idx = new[] { 0, 1, 2 }.OrderByDescending(i => a[i, i]).ToArray();
        return (idx.Select(i => a[i, i]).ToArray(), idx.Select(i => new Vec3(v[0, i], v[1, i], v[2, i]).Normalized()).ToArray());
    }

    /// <summary>Build a TriMesh from the kernel's render mesh (e.g. to compare a scan with a CAD body).</summary>
    public static TriMesh FromMeshData(MeshData d)
    {
        var v = (float[])d.Positions.Clone(); var t = (int[])d.Indices.Clone();
        return MeshOps.Weld(new TriMesh(v, t), 1e-5);
    }
}
