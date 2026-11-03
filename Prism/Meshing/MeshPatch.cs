using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Kernel;

namespace Prism.Meshing;

/// <summary>
/// Removes triangles and closes only the openings that leaves. A smooth patch gets new points at the size of the mesh around it,
/// then fairing so it carries on the curvature of the surface; a flat patch only triangulates the opening.
/// </summary>
public static class MeshPatch
{
    public sealed record Result(TriMesh Mesh, int Openings, int NewTriangles);

    const int MaxRefinePasses = 8;

    public static Result DeleteAndPatch(TriMesh m, IReadOnlySet<int> remove, bool smooth)
    {
        var keep = new List<int>(m.T.Length);
        var touched = new HashSet<int>();
        for (int t = 0; t < m.TriCount; t++)
            for (int k = 0; k < 3; k++)
            {
                if (remove.Contains(t)) touched.Add(m.T[3 * t + k]); else keep.Add(m.T[3 * t + k]);
            }
        var open = new TriMesh(m.V, [.. keep]);
        var verts = new List<Vec3>(m.VertexCount);
        for (int i = 0; i < m.VertexCount; i++) verts.Add(m.P(i));
        var tris = new List<int>(keep);
        int openings = 0, added = 0;
        var neighbours = Neighbours(keep);
        foreach (var hole in open.Topology().HoleLoops.Where(l => l.Any(touched.Contains)))
        {
            var loop = hole.Reverse().ToArray();             // new triangles traverse each boundary edge backwards
            if (loop.Length < 3) continue;
            var patch = Cap(loop, verts);
            if (smooth)
            {
                var border = loop.ToHashSet();
                double size = Enumerable.Range(0, loop.Length).Average(i => (verts[loop[i]] - verts[loop[(i + 1) % loop.Length]]).Length);
                Refine(patch, verts, size);
                Fair(patch, border, neighbours, verts);
            }
            tris.AddRange(patch); added += patch.Count / 3; openings++;
        }
        var v = new float[verts.Count * 3];
        for (int i = 0; i < verts.Count; i++) { v[3 * i] = (float)verts[i].X; v[3 * i + 1] = (float)verts[i].Y; v[3 * i + 2] = (float)verts[i].Z; }
        return new Result(MeshOps.Compact(v, [.. tris]), openings, added);
    }

    static Dictionary<int, HashSet<int>> Neighbours(List<int> t)
    {
        var res = new Dictionary<int, HashSet<int>>();
        void Link(int a, int b) { if (!res.TryGetValue(a, out var s)) res[a] = s = []; s.Add(b); }
        for (int i = 0; i < t.Count; i += 3)
            for (int k = 0; k < 3; k++) { Link(t[i + k], t[i + (k + 1) % 3]); Link(t[i + (k + 1) % 3], t[i + k]); }
        return res;
    }

    /// <summary>Triangles across the opening. An outline that folds over itself in its best-fit plane is fanned from its centre.</summary>
    static List<int> Cap(int[] loop, List<Vec3> verts)
    {
        var P = loop.Select(i => verts[i]).ToArray();
        Vec3 n = Vec3.Zero;
        for (int i = 0; i < P.Length; i++) { var a = P[i]; var b = P[(i + 1) % P.Length]; n += new Vec3((a.Y - b.Y) * (a.Z + b.Z), (a.Z - b.Z) * (a.X + b.X), (a.X - b.X) * (a.Y + b.Y)); }
        var res = new List<int>();
        if (n.Length > 1e-12)
        {
            n = n.Normalized();
            var u = Vec3.Cross(n, Math.Abs(n.X) < 0.9 ? Vec3.UnitX : Vec3.UnitY).Normalized(); var w = Vec3.Cross(n, u);
            var poly = P.Select(p => (Vec3.Dot(p, u), Vec3.Dot(p, w))).ToList();
            var flat = Triangulator.Polygon(poly);
            if (flat.Count == loop.Length - 2) { foreach (var (a, b, c) in flat) res.AddRange([loop[a], loop[b], loop[c]]); return res; }
        }
        int centre = verts.Count; verts.Add(P.Aggregate(Vec3.Zero, (s, p) => s + p) / P.Length);
        for (int i = 0; i < loop.Length; i++) res.AddRange([loop[i], loop[(i + 1) % loop.Length], centre]);
        return res;
    }

    static (int, int) Key(int a, int b) => a < b ? (a, b) : (b, a);

    static Dictionary<(int, int), List<int>> EdgeTris(List<int> t)
    {
        var res = new Dictionary<(int, int), List<int>>();
        for (int i = 0; i < t.Count / 3; i++)
            for (int k = 0; k < 3; k++)
            {
                var e = Key(t[3 * i + k], t[3 * i + (k + 1) % 3]);
                if (!res.TryGetValue(e, out var l)) res[e] = l = [];
                l.Add(i);
            }
        return res;
    }

    /// <summary>Split inner edges until they are about the size of the mesh around the patch. The outline is never touched, so the patch stays joined to it.</summary>
    static void Refine(List<int> t, List<Vec3> verts, double size)
    {
        for (int pass = 0; pass < MaxRefinePasses; pass++)
        {
            Flip(t, verts);
            var mids = new Dictionary<(int, int), int>();
            foreach (var (e, on) in EdgeTris(t))
                if (on.Count == 2 && (verts[e.Item1] - verts[e.Item2]).Length > size * 1.4) { mids[e] = verts.Count; verts.Add((verts[e.Item1] + verts[e.Item2]) * 0.5); }
            if (mids.Count == 0) break;
            var next = new List<int>(t.Count * 2);
            for (int i = 0; i < t.Count; i += 3)
            {
                // the corners with any new points between them, in order
                var ring = new List<int>(6); int firstMid = -1;
                for (int k = 0; k < 3; k++)
                {
                    int a = t[i + k], b = t[i + (k + 1) % 3];
                    ring.Add(a);
                    if (mids.TryGetValue(Key(a, b), out int m)) { if (firstMid < 0) firstMid = ring.Count; ring.Add(m); }
                }
                if (firstMid < 0) { next.AddRange([ring[0], ring[1], ring[2]]); continue; }
                if (ring.Count == 6) { next.AddRange([ring[0], ring[1], ring[5], ring[1], ring[2], ring[3], ring[3], ring[4], ring[5], ring[1], ring[3], ring[5]]); continue; }
                for (int k = 1; k + 1 < ring.Count; k++) next.AddRange([ring[firstMid], ring[(firstMid + k) % ring.Count], ring[(firstMid + k + 1) % ring.Count]]);
            }
            t.Clear(); t.AddRange(next);
        }
        Flip(t, verts);
    }

    /// <summary>Swap the diagonal of two triangles where that gives better shapes (the Delaunay rule), so long thin triangles do not survive into the fairing.</summary>
    static void Flip(List<int> t, List<Vec3> verts)
    {
        double Angle(Vec3 at, Vec3 a, Vec3 b) { var x = (a - at).Normalized(); var y = (b - at).Normalized(); return Math.Acos(Math.Clamp(Vec3.Dot(x, y), -1, 1)); }
        for (int sweep = 0; sweep < 6; sweep++)
        {
            bool any = false;
            var map = EdgeTris(t); var used = new HashSet<int>();
            foreach (var (e, on) in map)
            {
                if (on.Count != 2 || !used.Add(on[0]) || !used.Add(on[1])) continue;
                int A = on[0], B = on[1];
                int Other(int tri) { for (int k = 0; k < 3; k++) { int v = t[3 * tri + k]; if (v != e.Item1 && v != e.Item2) return v; } return -1; }
                int c = Other(A), d = Other(B);
                if (c == d || map.ContainsKey(Key(c, d))) continue;
                if (Angle(verts[c], verts[e.Item1], verts[e.Item2]) + Angle(verts[d], verts[e.Item1], verts[e.Item2]) <= Math.PI + 1e-6) continue;
                // keep triangle A's winding: find the direction it runs along the shared edge
                int from = e.Item1, to = e.Item2;
                for (int k = 0; k < 3; k++) if (t[3 * A + k] == e.Item2 && t[3 * A + (k + 1) % 3] == e.Item1) { from = e.Item2; to = e.Item1; }
                t[3 * A] = from; t[3 * A + 1] = d; t[3 * A + 2] = c;
                t[3 * B] = to; t[3 * B + 1] = c; t[3 * B + 2] = d;
                any = true;
            }
            if (!any) break;
        }
    }

    /// <summary>
    /// Place the new points on a curved surface (a quadric) fitted to two rings of the mesh around the opening, so the patch carries on its curvature and scan noise averages out.
    /// Whatever the fit misses along the outline is blended across the patch, so it still meets the outline exactly.
    /// </summary>
    static void Fair(List<int> patch, HashSet<int> border, Dictionary<int, HashSet<int>> outside, List<Vec3> verts)
    {
        var free = patch.Where(v => !border.Contains(v)).Distinct().ToList();
        if (free.Count == 0) return;
        var around = Neighbours(patch);
        var origin = border.Aggregate(Vec3.Zero, (s, v) => s + verts[v]) / border.Count;
        Vec3 n = Vec3.Zero;                                  // the patch's own triangles give the side the surface faces
        for (int i = 0; i < patch.Count; i += 3) n += Vec3.Cross(verts[patch[i + 1]] - verts[patch[i]], verts[patch[i + 2]] - verts[patch[i]]);
        n = n.Normalized();
        var u = Vec3.Cross(n, Math.Abs(n.X) < 0.9 ? Vec3.UnitX : Vec3.UnitY).Normalized(); var w = Vec3.Cross(n, u);

        // spread the new points evenly in the plane of the opening
        var pu = new Dictionary<int, double>(); var pv = new Dictionary<int, double>();
        foreach (int v in around.Keys) { pu[v] = Vec3.Dot(verts[v] - origin, u); pv[v] = Vec3.Dot(verts[v] - origin, w); }
        const double relax = 1.7;
        void Solve(Dictionary<int, double> f)
        {
            for (int it = 0; it < 4000; it++)
            {
                double moved = 0;
                foreach (int v in free)
                {
                    double sum = 0; foreach (int j in around[v]) sum += f[j];
                    double next = f[v] + (sum / around[v].Count - f[v]) * relax;
                    moved = Math.Max(moved, Math.Abs(next - f[v])); f[v] = next;
                }
                if (moved < 1e-7) break;
            }
        }
        Solve(pu); Solve(pv);

        var rings = new HashSet<int>(border);
        for (int ring = 0; ring < 2; ring++) foreach (int v in rings.ToList()) if (outside.TryGetValue(v, out var ns)) rings.UnionWith(ns);
        double size = Math.Max(rings.Max(v => (verts[v] - origin).Length), 1e-9);
        static double[] Terms(double x, double y) => [1, x, y, x * x, x * y, y * y];
        var A = new double[6, 6]; var b = new double[6];
        foreach (int v in rings)
        {
            var d = verts[v] - origin; var row = Terms(Vec3.Dot(d, u) / size, Vec3.Dot(d, w) / size); double h = Vec3.Dot(d, n);
            for (int i = 0; i < 6; i++) { b[i] += row[i] * h; for (int j = 0; j < 6; j++) A[i, j] += row[i] * row[j]; }
        }
        var c = SolveSmall(A, b);
        double Height(double x, double y) { if (c == null) return 0; var row = Terms(x / size, y / size); double h = 0; for (int i = 0; i < 6; i++) h += row[i] * c[i]; return h; }

        var miss = new Dictionary<int, double>();
        foreach (int v in around.Keys) miss[v] = border.Contains(v) ? Vec3.Dot(verts[v] - origin, n) - Height(pu[v], pv[v]) : 0;
        Solve(miss);
        foreach (int v in free) verts[v] = origin + u * pu[v] + w * pv[v] + n * (Height(pu[v], pv[v]) + miss[v]);
    }

    /// <summary>Gaussian elimination with pivoting; null when too few points were around to fix the surface.</summary>
    static double[]? SolveSmall(double[,] a, double[] b)
    {
        int n = b.Length; var x = (double[])b.Clone();
        for (int c = 0; c < n; c++)
        {
            int p = c; for (int r = c + 1; r < n; r++) if (Math.Abs(a[r, c]) > Math.Abs(a[p, c])) p = r;
            if (Math.Abs(a[p, c]) < 1e-12) return null;
            if (p != c) { for (int k = 0; k < n; k++) (a[c, k], a[p, k]) = (a[p, k], a[c, k]); (x[c], x[p]) = (x[p], x[c]); }
            for (int r = c + 1; r < n; r++) { double f = a[r, c] / a[c, c]; for (int k = c; k < n; k++) a[r, k] -= f * a[c, k]; x[r] -= f * x[c]; }
        }
        for (int r = n - 1; r >= 0; r--) { double s = x[r]; for (int k = r + 1; k < n; k++) s -= a[r, k] * x[k]; x[r] = s / a[r, r]; }
        return x;
    }
}