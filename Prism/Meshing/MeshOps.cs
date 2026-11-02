using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Kernel;

namespace Prism.Meshing;

/// <summary>Mesh repair and editing operations. Every method returns a new mesh.</summary>
public static class MeshOps
{
    // ================================================================= cleanup
    /// <summary>Merge vertices closer than tol (grid hashing), drop collapsed triangles and unused vertices.</summary>
    public static TriMesh Weld(TriMesh m, double tol)
    {
        if (tol <= 0) tol = 1e-9;
        var rep = new int[m.VertexCount];
        var cells = new Dictionary<(long, long, long), List<int>>();
        var keep = new List<int>();
        for (int i = 0; i < m.VertexCount; i++)
        {
            var p = m.P(i);
            long cx = (long)Math.Floor(p.X / tol), cy = (long)Math.Floor(p.Y / tol), cz = (long)Math.Floor(p.Z / tol);
            int found = -1;
            for (int dx = -1; dx <= 1 && found < 0; dx++)
                for (int dy = -1; dy <= 1 && found < 0; dy++)
                    for (int dz = -1; dz <= 1 && found < 0; dz++)
                        if (cells.TryGetValue((cx + dx, cy + dy, cz + dz), out var l))
                            foreach (int j in l) if ((m.P(j) - p).Length <= tol) { found = j; break; }
            if (found >= 0) rep[i] = rep[found];
            else
            {
                rep[i] = keep.Count; keep.Add(i);
                if (!cells.TryGetValue((cx, cy, cz), out var cl)) cells[(cx, cy, cz)] = cl = [];
                cl.Add(i);
            }
        }
        var v = new float[keep.Count * 3];
        for (int k = 0; k < keep.Count; k++) { v[3 * k] = m.V[3 * keep[k]]; v[3 * k + 1] = m.V[3 * keep[k] + 1]; v[3 * k + 2] = m.V[3 * keep[k] + 2]; }
        var vcol = m.VertexColors != null ? keep.Select(k => m.VertexColors[k]).ToArray() : null;
        var fcol = m.FaceColors != null ? new List<uint>() : null;
        var t = new List<int>(m.T.Length);
        for (int i = 0; i < m.TriCount; i++)
        {
            int a = rep[m.T[3 * i]], b = rep[m.T[3 * i + 1]], c = rep[m.T[3 * i + 2]];
            if (a == b || b == c || a == c) continue;
            t.Add(a); t.Add(b); t.Add(c); fcol?.Add(m.FaceColors![i]);
        }
        return new TriMesh(v, [.. t], vcol, fcol?.ToArray());
    }

    /// <summary>Remove zero-area and duplicate triangles and unreferenced vertices.</summary>
    public static (TriMesh mesh, int degenerate, int duplicate) Clean(TriMesh m, double areaEps = 1e-12)
    {
        var seen = new HashSet<(int, int, int)>(); var t = new List<int>(m.T.Length);
        var fcol = m.FaceColors != null ? new List<uint>() : null;
        int deg = 0, dup = 0;
        for (int i = 0; i < m.TriCount; i++)
        {
            int a = m.T[3 * i], b = m.T[3 * i + 1], c = m.T[3 * i + 2];
            if (a == b || b == c || a == c || m.TriArea(i) < areaEps) { deg++; continue; }
            int x = Math.Min(a, Math.Min(b, c)), z = Math.Max(a, Math.Max(b, c)), y = a + b + c - x - z;
            if (!seen.Add((x, y, z))) { dup++; continue; }
            t.Add(a); t.Add(b); t.Add(c); fcol?.Add(m.FaceColors![i]);
        }
        return (Compact(m.V, [.. t], m.VertexColors, fcol?.ToArray()), deg, dup);
    }

    /// <summary>Re-index so only referenced vertices remain (colours, when given, are carried along).</summary>
    public static TriMesh Compact(float[] v, int[] t, uint[]? vertexColors = null, uint[]? faceColors = null)
    {
        var map = new int[v.Length / 3]; Array.Fill(map, -1);
        var nv = new List<float>(); var nt = new int[t.Length]; var vc = vertexColors != null && vertexColors.Length == v.Length / 3 ? new List<uint>() : null;
        for (int i = 0; i < t.Length; i++)
        {
            int o = t[i];
            if (map[o] < 0) { map[o] = nv.Count / 3; nv.Add(v[3 * o]); nv.Add(v[3 * o + 1]); nv.Add(v[3 * o + 2]); vc?.Add(vertexColors![o]); }
            nt[i] = map[o];
        }
        return new TriMesh([.. nv], nt, vc?.ToArray(), faceColors);
    }

    // ================================================================= components
    public static int[] Components(TriMesh m, out int count)
    {
        var parent = Enumerable.Range(0, m.VertexCount).ToArray();
        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
        for (int t = 0; t < m.TriCount; t++)
        {
            int a = Find(m.T[3 * t]), b = Find(m.T[3 * t + 1]), c = Find(m.T[3 * t + 2]);
            parent[b] = a; parent[Find(c)] = Find(a);
        }
        var id = new Dictionary<int, int>(); var label = new int[m.TriCount];
        for (int t = 0; t < m.TriCount; t++)
        {
            int r = Find(m.T[3 * t]);
            if (!id.TryGetValue(r, out int k)) id[r] = k = id.Count;
            label[t] = k;
        }
        count = id.Count;
        return label;
    }

    /// <summary>Drop shells smaller than minTris triangles or minFraction of the largest shell.</summary>
    public static (TriMesh mesh, int removed, int kept) RemoveSmallShells(TriMesh m, int minTris, double minFraction)
    {
        var label = Components(m, out int n);
        var sizes = new int[n]; foreach (int l in label) sizes[l]++;
        int largest = sizes.Length == 0 ? 0 : sizes.Max();
        var keepComp = sizes.Select(s => s >= minTris && s >= largest * minFraction).ToArray();
        var tris = Enumerable.Range(0, m.TriCount).Where(t => keepComp[label[t]]);
        var kept = tris.ToList();
        var res = Compact(m.V, kept.SelectMany(t => new[] { m.T[3 * t], m.T[3 * t + 1], m.T[3 * t + 2] }).ToArray(), m.VertexColors, m.FaceColors != null ? kept.Select(t => m.FaceColors[t]).ToArray() : null);
        return (res, keepComp.Count(k => !k), keepComp.Count(k => k));
    }

    // ================================================================= orientation
    /// <summary>Make triangle winding consistent across every shell, then point closed shells outward.</summary>
    public static (TriMesh mesh, int flipped) FixOrientation(TriMesh m)
    {
        var t = (int[])m.T.Clone(); int flipped = 0;
        int nt = m.TriCount;
        var topo = new TriMesh(m.V, t).Topology();
        var visited = new bool[nt]; var compOf = new int[nt]; int comps = 0;
        bool HasEdge(int tri, int a, int b) { for (int k = 0; k < 3; k++) if (t[3 * tri + k] == a && t[3 * tri + (k + 1) % 3] == b) return true; return false; }
        // swapping two vertices reverses every edge, so neighbour slots move with them (slot 0<->2)
        void Flip(int tri)
        {
            (t[3 * tri + 1], t[3 * tri + 2]) = (t[3 * tri + 2], t[3 * tri + 1]);
            (topo.Neighbors[3 * tri], topo.Neighbors[3 * tri + 2]) = (topo.Neighbors[3 * tri + 2], topo.Neighbors[3 * tri]);
        }
        var q = new Queue<int>();
        for (int s = 0; s < nt; s++)
        {
            if (visited[s]) continue;
            visited[s] = true; compOf[s] = comps; q.Enqueue(s);
            while (q.Count > 0)
            {
                int u = q.Dequeue();
                for (int k = 0; k < 3; k++)
                {
                    int nb = topo.Neighbors[3 * u + k];
                    if (nb < 0 || visited[nb]) continue;
                    int a = t[3 * u + k], b = t[3 * u + (k + 1) % 3];
                    if (HasEdge(nb, a, b)) { Flip(nb); flipped++; }
                    visited[nb] = true; compOf[nb] = comps; q.Enqueue(nb);
                }
            }
            comps++;
        }
        // outward: signed volume per shell
        var vol = new double[comps];
        for (int i = 0; i < nt; i++)
        {
            var a = m.P(t[3 * i]); var b = m.P(t[3 * i + 1]); var c = m.P(t[3 * i + 2]);
            vol[compOf[i]] += Vec3.Dot(a, Vec3.Cross(b, c)) / 6;
        }
        for (int i = 0; i < nt; i++) if (vol[compOf[i]] < 0) { (t[3 * i + 1], t[3 * i + 2]) = (t[3 * i + 2], t[3 * i + 1]); flipped++; }
        return (new TriMesh(m.V, t, m.VertexColors, m.FaceColors), flipped);
    }

    public static TriMesh FlipAll(TriMesh m) => m.Transformed(p => p, flipWinding: true);

    // ================================================================= holes
    /// <summary>Close boundary loops of up to maxEdges edges by triangulating them.</summary>
    public static (TriMesh mesh, int filled, int skipped) FillHoles(TriMesh m, int maxEdges)
    {
        var topo = m.Topology();
        var tris = new List<int>(m.T); int filled = 0, skipped = 0;
        foreach (var loop in topo.HoleLoops)
        {
            if (loop.Length > maxEdges) { skipped++; continue; }
            var order = loop.Reverse().ToArray();                      // new triangles traverse each boundary edge backwards
            var P = order.Select(i => m.P(i)).ToArray();
            Vec3 n = Vec3.Zero;
            for (int i = 0; i < P.Length; i++) { var a = P[i]; var b = P[(i + 1) % P.Length]; n += new Vec3((a.Y - b.Y) * (a.Z + b.Z), (a.Z - b.Z) * (a.X + b.X), (a.X - b.X) * (a.Y + b.Y)); }
            n = n.Normalized();
            if (n.Length < 0.5) { skipped++; continue; }
            var href = Math.Abs(n.X) < 0.9 ? Vec3.UnitX : Vec3.UnitY;
            var u = Vec3.Cross(n, href).Normalized(); var w = Vec3.Cross(n, u);
            var poly = P.Select(p => (Vec3.Dot(p, u), Vec3.Dot(p, w))).ToList();
            foreach (var (a, b, c) in Triangulator.Polygon(poly)) { tris.Add(order[a]); tris.Add(order[b]); tris.Add(order[c]); }
            filled++;
        }
        uint[]? fcol = null;
        if (m.FaceColors != null) { fcol = new uint[tris.Count / 3]; m.FaceColors.CopyTo(fcol, 0); Array.Fill(fcol, 0xFFC8D0DAu, m.TriCount, fcol.Length - m.TriCount); }
        return (new TriMesh(m.V, [.. tris], m.VertexColors, fcol), filled, skipped);
    }

    // ================================================================= smoothing, subdivision
    static int[][] Adjacency(TriMesh m)
    {
        var sets = new HashSet<int>[m.VertexCount];
        for (int i = 0; i < sets.Length; i++) sets[i] = [];
        for (int t = 0; t < m.TriCount; t++)
            for (int k = 0; k < 3; k++) { int a = m.T[3 * t + k], b = m.T[3 * t + (k + 1) % 3]; sets[a].Add(b); sets[b].Add(a); }
        return sets.Select(s => s.ToArray()).ToArray();
    }

    /// <summary>Taubin smoothing (shrink then inflate), so noise goes away without the model shrinking.</summary>
    public static TriMesh Smooth(TriMesh m, int iterations, double lambda = 0.5, double mu = -0.53, bool keepBoundary = true, double[]? weights = null)
    {
        var adj = Adjacency(m); int nv = m.VertexCount;
        var fixedV = new bool[nv];
        if (keepBoundary)
        {
            var topo = m.Topology();
            var cnt = new Dictionary<long, int>();
            for (int t = 0; t < m.TriCount; t++) for (int k = 0; k < 3; k++) { var key = TriMesh.EdgeKey(m.T[3 * t + k], m.T[3 * t + (k + 1) % 3], nv); cnt[key] = cnt.GetValueOrDefault(key) + 1; }
            for (int t = 0; t < m.TriCount; t++) for (int k = 0; k < 3; k++) { int a = m.T[3 * t + k], b = m.T[3 * t + (k + 1) % 3]; if (cnt[TriMesh.EdgeKey(a, b, nv)] != 2) fixedV[a] = fixedV[b] = true; }
        }
        var p = new double[nv * 3]; for (int i = 0; i < p.Length; i++) p[i] = m.V[i];
        var q = new double[p.Length];
        void Step(double f)
        {
            for (int i = 0; i < nv; i++)
            {
                if (fixedV[i] || adj[i].Length == 0) { q[3 * i] = p[3 * i]; q[3 * i + 1] = p[3 * i + 1]; q[3 * i + 2] = p[3 * i + 2]; continue; }
                double sx = 0, sy = 0, sz = 0;
                foreach (int j in adj[i]) { sx += p[3 * j]; sy += p[3 * j + 1]; sz += p[3 * j + 2]; }
                double inv = 1.0 / adj[i].Length, ff = f * (weights?[i] ?? 1);
                q[3 * i] = p[3 * i] + ff * (sx * inv - p[3 * i]); q[3 * i + 1] = p[3 * i + 1] + ff * (sy * inv - p[3 * i + 1]); q[3 * i + 2] = p[3 * i + 2] + ff * (sz * inv - p[3 * i + 2]);
            }
            (p, q) = (q, p);
        }
        for (int it = 0; it < iterations; it++) { Step(lambda); Step(mu); }
        var v = new float[p.Length]; for (int i = 0; i < p.Length; i++) v[i] = (float)p[i];
        return new TriMesh(v, (int[])m.T.Clone(), m.VertexColors, m.FaceColors);
    }

    /// <summary>0 for vertices on sharp edges or corners (so smoothing keeps them), 1 elsewhere.</summary>
    public static double[] SharpWeights(TriMesh m, double angleDeg)
    {
        double cosA = Math.Cos(angleDeg * Math.PI / 180);
        var w = new double[m.VertexCount]; Array.Fill(w, 1.0);
        var fn = m.FaceNormals;
        var firstNormal = new int[m.VertexCount]; Array.Fill(firstNormal, -1);
        for (int t = 0; t < m.TriCount; t++)
            for (int k = 0; k < 3; k++)
            {
                int v = m.T[3 * t + k];
                if (firstNormal[v] < 0) { firstNormal[v] = t; continue; }
                int u = firstNormal[v];
                if (fn[3 * t] * fn[3 * u] + fn[3 * t + 1] * fn[3 * u + 1] + fn[3 * t + 2] * fn[3 * u + 2] < cosA) w[v] = 0;
            }
        return w;
    }

    /// <summary>Scale a closed mesh about its centre so it keeps the volume it had (cancels smoothing shrinkage).</summary>
    public static TriMesh RestoreVolume(TriMesh m, double targetVolume)
    {
        double v = m.Volume();
        if (targetVolume <= 0 || v <= 0) return m;
        double f = Math.Pow(targetVolume / v, 1.0 / 3.0);
        var c = m.CenterOfMass();
        return m.Transformed(p => c + (p - c) * f);
    }

    /// <summary>Split every triangle into four (midpoint subdivision), optionally smoothing the result afterwards.</summary>
    public static TriMesh Subdivide(TriMesh m, int levels = 1)
    {
        for (int lv = 0; lv < levels; lv++)
        {
            var v = new List<float>(m.V); var mid = new Dictionary<long, int>(); var t = new List<int>(m.T.Length * 4);
            int Mid(int a, int b)
            {
                long key = TriMesh.EdgeKey(a, b, m.VertexCount);
                if (mid.TryGetValue(key, out int id)) return id;
                id = v.Count / 3; mid[key] = id;
                v.Add((m.V[3 * a] + m.V[3 * b]) / 2); v.Add((m.V[3 * a + 1] + m.V[3 * b + 1]) / 2); v.Add((m.V[3 * a + 2] + m.V[3 * b + 2]) / 2);
                return id;
            }
            for (int i = 0; i < m.TriCount; i++)
            {
                int a = m.T[3 * i], b = m.T[3 * i + 1], c = m.T[3 * i + 2];
                int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                t.AddRange([a, ab, ca, ab, b, bc, ca, bc, c, ab, bc, ca]);
            }
            m = new TriMesh([.. v], [.. t]);
        }
        return m;
    }

    // ================================================================= decimation
    /// <summary>Quadric edge-collapse simplification (Garland-Heckbert). Boundaries are preserved and folds are rejected.</summary>
    public static TriMesh Decimate(TriMesh m, int targetTris, bool keepBoundary = true)
    {
        int nv = m.VertexCount, nt = m.TriCount;
        if (targetTris >= nt) return m;
        var pos = new double[nv * 3]; for (int i = 0; i < pos.Length; i++) pos[i] = m.V[i];
        var tri = (int[])m.T.Clone(); var alive = new bool[nt]; Array.Fill(alive, true);
        var inc = new List<int>[nv]; for (int i = 0; i < nv; i++) inc[i] = [];
        for (int t = 0; t < nt; t++) for (int k = 0; k < 3; k++) inc[tri[3 * t + k]].Add(t);
        var Q = new double[nv * 10];
        var boundaryV = new bool[nv];

        double[] PlaneQ(double a, double b, double c, double d, double w) => [w * a * a, w * a * b, w * a * c, w * a * d, w * b * b, w * b * c, w * b * d, w * c * c, w * c * d, w * d * d];
        void AddQ(int v, double[] q) { for (int k = 0; k < 10; k++) Q[10 * v + k] += q[k]; }
        Vec3 Pt(int i) => new(pos[3 * i], pos[3 * i + 1], pos[3 * i + 2]);

        // boundary edges
        var cnt = new Dictionary<long, int>();
        for (int t = 0; t < nt; t++) for (int k = 0; k < 3; k++) { var key = TriMesh.EdgeKey(tri[3 * t + k], tri[3 * t + (k + 1) % 3], nv); cnt[key] = cnt.GetValueOrDefault(key) + 1; }
        for (int t = 0; t < nt; t++)
        {
            var a = Pt(tri[3 * t]); var b = Pt(tri[3 * t + 1]); var c = Pt(tri[3 * t + 2]);
            var cr = Vec3.Cross(b - a, c - a); double area2 = cr.Length;
            if (area2 < 1e-18) continue;
            var n = cr / area2; double d = -Vec3.Dot(n, a);
            var q = PlaneQ(n.X, n.Y, n.Z, d, area2 / 2);
            for (int k = 0; k < 3; k++)
            {
                int u = tri[3 * t + k], w = tri[3 * t + (k + 1) % 3];
                AddQ(u, q);
                if (cnt[TriMesh.EdgeKey(u, w, nv)] != 2)
                {
                    boundaryV[u] = boundaryV[w] = true;
                    var e = Pt(w) - Pt(u); var bn = Vec3.Cross(e, n).Normalized();
                    double bd = -Vec3.Dot(bn, Pt(u));
                    var bq = PlaneQ(bn.X, bn.Y, bn.Z, bd, 100 * e.Length * e.Length / 2 + 1e-9);
                    AddQ(u, bq); AddQ(w, bq);
                }
            }
        }

        var ver = new int[nv];
        var pq = new PriorityQueue<(int a, int b, int va, int vb, double x, double y, double z), double>();

        double Err(double[] q, double x, double y, double z) =>
            q[0] * x * x + 2 * q[1] * x * y + 2 * q[2] * x * z + 2 * q[3] * x + q[4] * y * y + 2 * q[5] * y * z + 2 * q[6] * y + q[7] * z * z + 2 * q[8] * z + q[9];

        void Push(int a, int b)
        {
            if (keepBoundary && boundaryV[a] && boundaryV[b] && cnt.GetValueOrDefault(TriMesh.EdgeKey(a, b, nv)) == 2) return;   // interior edge between two boundary vertices
            var q = new double[10]; for (int k = 0; k < 10; k++) q[k] = Q[10 * a + k] + Q[10 * b + k];
            double x, y, z;
            var pa = Pt(a); var pb = Pt(b);
            if (keepBoundary && boundaryV[a] != boundaryV[b]) { var keep = boundaryV[a] ? pa : pb; (x, y, z) = (keep.X, keep.Y, keep.Z); }
            else
            {
                // minimise the quadric: solve [q0 q1 q2; q1 q4 q5; q2 q5 q7] x = -[q3 q6 q8]
                double a11 = q[0], a12 = q[1], a13 = q[2], a22 = q[4], a23 = q[5], a33 = q[7];
                double det = a11 * (a22 * a33 - a23 * a23) - a12 * (a12 * a33 - a23 * a13) + a13 * (a12 * a23 - a22 * a13);
                if (Math.Abs(det) > 1e-14)
                {
                    double b1 = -q[3], b2 = -q[6], b3 = -q[8];
                    x = (b1 * (a22 * a33 - a23 * a23) - a12 * (b2 * a33 - a23 * b3) + a13 * (b2 * a23 - a22 * b3)) / det;
                    y = (a11 * (b2 * a33 - a23 * b3) - b1 * (a12 * a33 - a23 * a13) + a13 * (a12 * b3 - b2 * a13)) / det;
                    z = (a11 * (a22 * b3 - b2 * a23) - a12 * (a12 * b3 - b2 * a13) + b1 * (a12 * a23 - a22 * a13)) / det;
                    // keep the optimum near the edge, otherwise fall back (avoids spikes)
                    double edge = (pa - pb).Length;
                    if ((new Vec3(x, y, z) - (pa + pb) * 0.5).Length > 3 * edge) { (x, y, z) = ((pa.X + pb.X) / 2, (pa.Y + pb.Y) / 2, (pa.Z + pb.Z) / 2); }
                }
                else (x, y, z) = ((pa.X + pb.X) / 2, (pa.Y + pb.Y) / 2, (pa.Z + pb.Z) / 2);
            }
            double cost = Math.Max(0, Err(q, x, y, z));
            pq.Enqueue((a, b, ver[a], ver[b], x, y, z), cost);
        }

        var seenEdge = new HashSet<long>();
        for (int t = 0; t < nt; t++)
            for (int k = 0; k < 3; k++)
            {
                int a = tri[3 * t + k], b = tri[3 * t + (k + 1) % 3];
                if (seenEdge.Add(TriMesh.EdgeKey(a, b, nv))) Push(a, b);
            }

        int count = nt;
        var nbrs = new HashSet<int>();
        while (count > targetTris && pq.Count > 0)
        {
            var e = pq.Dequeue();
            if (e.va != ver[e.a] || e.vb != ver[e.b]) continue;
            int a = e.a, b = e.b;
            // triangles on the edge
            var shared = new List<int>(); foreach (int t in inc[a]) if (alive[t] && TriHas(tri, t, b)) shared.Add(t);
            if (shared.Count == 0 || shared.Count > 2) continue;
            // link condition
            nbrs.Clear();
            var na = new HashSet<int>(); foreach (int t in inc[a]) if (alive[t]) for (int k = 0; k < 3; k++) na.Add(tri[3 * t + k]);
            var nb = new HashSet<int>(); foreach (int t in inc[b]) if (alive[t]) for (int k = 0; k < 3; k++) nb.Add(tri[3 * t + k]);
            na.Remove(a); na.Remove(b); nb.Remove(a); nb.Remove(b);
            na.IntersectWith(nb);
            if (na.Count != shared.Count) continue;
            // fold test
            var np = new Vec3(e.x, e.y, e.z); bool bad = false;
            foreach (var (vtx, other) in new[] { (a, b), (b, a) })
                foreach (int t in inc[vtx])
                {
                    if (!alive[t] || TriHas(tri, t, other)) continue;
                    var p = new Vec3[3]; for (int k = 0; k < 3; k++) p[k] = tri[3 * t + k] == vtx ? np : Pt(tri[3 * t + k]);
                    var oldN = Vec3.Cross(Pt(tri[3 * t + 1]) - Pt(tri[3 * t]), Pt(tri[3 * t + 2]) - Pt(tri[3 * t]));
                    var newN = Vec3.Cross(p[1] - p[0], p[2] - p[0]);
                    double lo = oldN.Length, ln = newN.Length;
                    if (ln < 1e-12 || lo < 1e-18 || Vec3.Dot(oldN, newN) / (lo * ln) < 0.3) { bad = true; break; }
                }
            if (bad) continue;
            // collapse b into a
            pos[3 * a] = e.x; pos[3 * a + 1] = e.y; pos[3 * a + 2] = e.z;
            foreach (int t in shared) { alive[t] = false; count--; }
            foreach (int t in inc[b]) { if (!alive[t]) continue; for (int k = 0; k < 3; k++) if (tri[3 * t + k] == b) tri[3 * t + k] = a; inc[a].Add(t); }
            inc[b].Clear();
            for (int k = 0; k < 10; k++) Q[10 * a + k] += Q[10 * b + k];
            boundaryV[a] |= boundaryV[b];
            ver[a]++; ver[b]++;
            nbrs.Clear();
            foreach (int t in inc[a]) if (alive[t]) for (int k = 0; k < 3; k++) if (tri[3 * t + k] != a) nbrs.Add(tri[3 * t + k]);
            foreach (int o in nbrs) Push(a, o);
            // edge keys of the survivors changed meaning: record boundary status for new neighbours conservatively
            foreach (int o in nbrs) { var key = TriMesh.EdgeKey(a, o, nv); if (!cnt.ContainsKey(key)) cnt[key] = 2; }
        }

        var v = new float[nv * 3]; for (int i = 0; i < v.Length; i++) v[i] = (float)pos[i];
        var tris = new List<int>();
        for (int t = 0; t < nt; t++) if (alive[t]) { int a = tri[3 * t], b = tri[3 * t + 1], c = tri[3 * t + 2]; if (a != b && b != c && a != c) { tris.Add(a); tris.Add(b); tris.Add(c); } }
        return Compact(v, [.. tris]);
    }

    static bool TriHas(int[] tri, int t, int v) => tri[3 * t] == v || tri[3 * t + 1] == v || tri[3 * t + 2] == v;

    // ================================================================= cutting
    /// <summary>One closed (or open) polyline produced by cutting a mesh with a plane.</summary>
    public sealed record CutLoop(List<Vec3> Points, bool Closed);

    /// <summary>Result of slicing a mesh by a plane.</summary>
    public sealed record CutResult(TriMesh Above, TriMesh Below, List<CutLoop> Loops);

    /// <summary>Cut with the plane through origin with the given normal. Cap = fill the cut faces so both halves are closed.</summary>
    public static CutResult PlaneCut(TriMesh m, Vec3 origin, Vec3 normal, bool cap)
    {
        normal = normal.Normalized();
        double d0 = Vec3.Dot(normal, origin);
        var verts = new List<float>(m.V);
        int nv0 = m.VertexCount;
        var dist = new double[nv0];
        for (int i = 0; i < nv0; i++) { dist[i] = Vec3.Dot(normal, m.P(i)) - d0; if (Math.Abs(dist[i]) < 1e-9) dist[i] = 1e-9; }
        var cutVert = new Dictionary<long, int>();
        int Cut(int a, int b)
        {
            long key = TriMesh.EdgeKey(a, b, nv0);
            if (cutVert.TryGetValue(key, out int id)) return id;
            double t = dist[a] / (dist[a] - dist[b]);
            id = verts.Count / 3; cutVert[key] = id;
            for (int k = 0; k < 3; k++) verts.Add((float)(m.V[3 * a + k] + t * (m.V[3 * b + k] - m.V[3 * a + k])));
            return id;
        }
        var above = new List<int>(); var below = new List<int>(); var segs = new List<(int a, int b)>();
        for (int t = 0; t < m.TriCount; t++)
        {
            int[] v = [m.T[3 * t], m.T[3 * t + 1], m.T[3 * t + 2]];
            bool[] pos = [dist[v[0]] > 0, dist[v[1]] > 0, dist[v[2]] > 0];
            int np = pos.Count(x => x);
            if (np == 3) { above.AddRange(v); continue; }
            if (np == 0) { below.AddRange(v); continue; }
            // rotate so the lone vertex (the one alone on its side) is v[0]
            int lone = np == 1 ? Array.IndexOf(pos, true) : Array.IndexOf(pos, false);
            int a = v[lone], b = v[(lone + 1) % 3], c = v[(lone + 2) % 3];
            int ab = Cut(a, b), ca = Cut(c, a);
            var loneSide = np == 1 ? above : below; var otherSide = np == 1 ? below : above;
            loneSide.AddRange([a, ab, ca]);
            otherSide.AddRange([ab, b, c]); otherSide.AddRange([ab, c, ca]);
            // directed cut segment: along the lone-side triangle, from ca to ab keeps the lone side on the left when it is "above"
            segs.Add(np == 1 ? (ca, ab) : (ab, ca));
        }

        var loops = ChainLoops(segs, verts);
        var cutLoops = loops.Select(l => new CutLoop(l.ids.Select(i => new Vec3(verts[3 * i], verts[3 * i + 1], verts[3 * i + 2])).ToList(), l.closed)).ToList();

        if (cap && loops.Count > 0)
        {
            var href = Math.Abs(normal.X) < 0.9 ? Vec3.UnitX : Vec3.UnitY;
            var u = Vec3.Cross(normal, href).Normalized(); var w = Vec3.Cross(normal, u);
            var closed = loops.Where(l => l.closed && l.ids.Count >= 3).ToList();
            var P2 = closed.Select(l => l.ids.Select(i => (x: Vec3.Dot(new Vec3(verts[3 * i], verts[3 * i + 1], verts[3 * i + 2]), u), y: Vec3.Dot(new Vec3(verts[3 * i], verts[3 * i + 1], verts[3 * i + 2]), w))).ToList()).ToList();
            var area = P2.Select(p => { double a = 0; for (int i = 0; i < p.Count; i++) { var s = p[i]; var e = p[(i + 1) % p.Count]; a += s.x * e.y - e.x * s.y; } return a / 2; }).ToList();
            // nesting depth by containment of one vertex
            int n = closed.Count; var parent = new int[n]; var depth = new int[n];
            for (int i = 0; i < n; i++)
            {
                parent[i] = -1; double best = double.MaxValue;
                for (int j = 0; j < n; j++)
                    if (i != j && Math.Abs(area[j]) > Math.Abs(area[i]) && Inside(P2[j], P2[i][0]) && Math.Abs(area[j]) < best) { best = Math.Abs(area[j]); parent[i] = j; }
            }
            for (int i = 0; i < n; i++) { int d = 0, p = parent[i]; while (p >= 0) { d++; p = parent[p]; } depth[i] = d; }
            for (int i = 0; i < n; i++)
            {
                if (depth[i] % 2 != 0) continue;
                var holes = Enumerable.Range(0, n).Where(j => parent[j] == i && depth[j] % 2 == 1).ToList();
                var outerPts = P2[i]; var holePts = holes.Select(j => (IReadOnlyList<(double x, double y)>)P2[j]).ToList();
                var ids = new List<int>(closed[i].ids); foreach (int j in holes) ids.AddRange(closed[j].ids);
                foreach (var (a, b, c) in Triangulator.Polygon(outerPts, holePts))
                {
                    // triangulator output is CCW about +normal; the "above" half needs the cap facing -normal
                    below.AddRange([ids[a], ids[b], ids[c]]);
                    above.AddRange([ids[a], ids[c], ids[b]]);
                }
            }
        }
        var vv = verts.ToArray();
        return new CutResult(Compact(vv, [.. above]), Compact(vv, [.. below]), cutLoops);
    }

    static bool Inside(List<(double x, double y)> poly, (double x, double y) p)
    {
        bool inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            if ((poly[i].y > p.y) != (poly[j].y > p.y) && p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x) inside = !inside;
        return inside;
    }

    static List<(List<int> ids, bool closed)> ChainLoops(List<(int a, int b)> segs, List<float> verts)
    {
        var next = new Dictionary<int, List<int>>();
        for (int i = 0; i < segs.Count; i++) { if (!next.TryGetValue(segs[i].a, out var l)) next[segs[i].a] = l = []; l.Add(i); }
        var used = new bool[segs.Count]; var res = new List<(List<int>, bool)>();
        for (int s = 0; s < segs.Count; s++)
        {
            if (used[s]) continue;
            var ids = new List<int>(); int cur = s; bool closed = false;
            while (true)
            {
                used[cur] = true; ids.Add(segs[cur].a);
                int b = segs[cur].b;
                if (b == segs[s].a) { closed = true; break; }
                if (!next.TryGetValue(b, out var cand)) { ids.Add(b); break; }
                int nx = cand.FirstOrDefault(c => !used[c], -1);
                if (nx < 0) { ids.Add(b); break; }
                cur = nx;
            }
            res.Add((ids, closed));
        }
        return res;
    }

    /// <summary>Section outline of a mesh at a plane, without building the halves.</summary>
    public static List<CutLoop> Section(TriMesh m, Vec3 origin, Vec3 normal) => PlaneCut(m, origin, normal, false).Loops;

    // ================================================================= transforms
    public static TriMesh Move(TriMesh m, Vec3 d) => m.Transformed(p => p + d);

    public static TriMesh Rotate(TriMesh m, Vec3 origin, Vec3 axis, double deg) =>
        m.Transformed(p => Rodrigues(p, origin, axis, deg));

    public static Vec3 Rodrigues(Vec3 p, Vec3 origin, Vec3 axis, double deg)
    {
        var k = axis.Normalized(); double a = deg * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a); var v = p - origin;
        return origin + v * c + Vec3.Cross(k, v) * s + k * (Vec3.Dot(k, v) * (1 - c));
    }

    public static TriMesh Scale(TriMesh m, Vec3 origin, double f) => m.Transformed(p => origin + (p - origin) * f, flipWinding: f < 0);

    public static TriMesh Mirror(TriMesh m, Vec3 origin, Vec3 normal)
    {
        var n = normal.Normalized();
        return m.Transformed(p => p - n * (2 * Vec3.Dot(p - origin, n)), flipWinding: true);
    }

    /// <summary>Move vertices along their normals (positive = grow). Useful for tolerance/offset experiments; large offsets can self-intersect.</summary>
    public static TriMesh Offset(TriMesh m, double d)
    {
        var n = m.VertexNormals(); var v = new float[m.V.Length];
        for (int i = 0; i < m.VertexCount; i++) { v[3 * i] = m.V[3 * i] + (float)(n[3 * i] * d); v[3 * i + 1] = m.V[3 * i + 1] + (float)(n[3 * i + 1] * d); v[3 * i + 2] = m.V[3 * i + 2] + (float)(n[3 * i + 2] * d); }
        return new TriMesh(v, (int[])m.T.Clone(), m.VertexColors, m.FaceColors);
    }
}
