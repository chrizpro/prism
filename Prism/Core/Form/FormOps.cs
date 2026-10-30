using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Kernel;

namespace Prism.Core.Form;

/// <summary>Form primitives (control cages) and topology-editing operations.</summary>
public static class FormOps
{
    // ================================================================= primitives
    public static SubdMesh Plane(double w, double h, int nx, int ny)
    {
        var m = new SubdMesh();
        for (int j = 0; j <= ny; j++) for (int i = 0; i <= nx; i++) m.V.Add(new(-w / 2 + w * i / nx, -h / 2 + h * j / ny, 0));
        int Id(int i, int j) => j * (nx + 1) + i;
        for (int j = 0; j < ny; j++) for (int i = 0; i < nx; i++) m.F.Add([Id(i, j), Id(i + 1, j), Id(i + 1, j + 1), Id(i, j + 1)]);
        return m;
    }

    static void AddGrid(SubdMesh m, Vec3 origin, Vec3 u, Vec3 v, int nu, int nv)
    {
        int b = m.V.Count;
        for (int j = 0; j <= nv; j++) for (int i = 0; i <= nu; i++) m.V.Add(origin + u * ((double)i / nu) + v * ((double)j / nv));
        int Id(int i, int j) => b + j * (nu + 1) + i;
        for (int j = 0; j < nv; j++) for (int i = 0; i < nu; i++) m.F.Add([Id(i, j), Id(i + 1, j), Id(i + 1, j + 1), Id(i, j + 1)]);
    }

    /// <summary>Box with its base on Z = 0, centred in X/Y.</summary>
    public static SubdMesh Box(double w, double d, double h, int nx, int ny, int nz)
    {
        var m = new SubdMesh();
        double x0 = -w / 2, y0 = -d / 2;
        var X = new Vec3(w, 0, 0); var Y = new Vec3(0, d, 0); var Z = new Vec3(0, 0, h);
        AddGrid(m, new(x0, y0, h), X, Y, nx, ny);             // +Z
        AddGrid(m, new(x0, y0, 0), Y, X, ny, nx);             // -Z
        AddGrid(m, new(x0 + w, y0, 0), Y, Z, ny, nz);         // +X
        AddGrid(m, new(x0, y0, 0), Z, Y, nz, ny);             // -X
        AddGrid(m, new(x0, y0 + d, 0), Z, X, nz, nx);         // +Y
        AddGrid(m, new(x0, y0, 0), X, Z, nx, nz);             // -Y
        m.WeldByDistance(Math.Min(Math.Min(w / nx, d / ny), h / nz) * 1e-3 + 1e-9);
        return m;
    }

    /// <summary>"Quadball": a cube projected onto a sphere. All faces are quads.</summary>
    public static SubdMesh Sphere(double r, int n)
    {
        var m = new SubdMesh();
        var e = 2.0;
        void Face(Vec3 o, Vec3 u, Vec3 v) => AddGrid(m, o, u * e, v * e, n, n);
        Face(new(-1, -1, 1), Vec3.UnitX, Vec3.UnitY); Face(new(-1, -1, -1), Vec3.UnitY, Vec3.UnitX);
        Face(new(1, -1, -1), Vec3.UnitY, Vec3.UnitZ); Face(new(-1, -1, -1), Vec3.UnitZ, Vec3.UnitY);
        Face(new(-1, 1, -1), Vec3.UnitZ, Vec3.UnitX); Face(new(-1, -1, -1), Vec3.UnitX, Vec3.UnitZ);
        m.WeldByDistance(1e-6);
        // spherify with a gentle correction so cells stay evenly sized
        for (int i = 0; i < m.V.Count; i++)
        {
            var p = m.V[i];
            double x2 = p.X * p.X, y2 = p.Y * p.Y, z2 = p.Z * p.Z;
            var q = new Vec3(p.X * Math.Sqrt(1 - y2 / 2 - z2 / 2 + y2 * z2 / 3), p.Y * Math.Sqrt(1 - x2 / 2 - z2 / 2 + x2 * z2 / 3), p.Z * Math.Sqrt(1 - x2 / 2 - y2 / 2 + x2 * y2 / 3));
            m.V[i] = q * r;
        }
        return m;
    }

    public static SubdMesh Cylinder(double r, double h, int seg, int rings, bool caps = true)
    {
        var m = new SubdMesh();
        for (int j = 0; j <= rings; j++)
            for (int i = 0; i < seg; i++)
            {
                double a = 2 * Math.PI * i / seg;
                m.V.Add(new(r * Math.Cos(a), r * Math.Sin(a), h * j / rings));
            }
        int Id(int i, int j) => j * seg + (i % seg);
        for (int j = 0; j < rings; j++)
            for (int i = 0; i < seg; i++) m.F.Add([Id(i, j), Id(i + 1, j), Id(i + 1, j + 1), Id(i, j + 1)]);
        if (caps)
        {
            m.F.Add(Enumerable.Range(0, seg).Select(i => Id(i, rings)).ToArray());          // top (CCW from +Z)
            m.F.Add(Enumerable.Range(0, seg).Select(i => Id(seg - 1 - i, 0)).ToArray());   // bottom
        }
        return m;
    }

    public static SubdMesh Torus(double R, double r, int nMajor, int nMinor)
    {
        var m = new SubdMesh();
        for (int i = 0; i < nMajor; i++)
            for (int j = 0; j < nMinor; j++)
            {
                double a = 2 * Math.PI * i / nMajor, b = 2 * Math.PI * j / nMinor;
                double rr = R + r * Math.Cos(b);
                m.V.Add(new(rr * Math.Cos(a), rr * Math.Sin(a), r * Math.Sin(b)));
            }
        int Id(int i, int j) => (i % nMajor) * nMinor + (j % nMinor);
        for (int i = 0; i < nMajor; i++) for (int j = 0; j < nMinor; j++) m.F.Add([Id(i, j), Id(i + 1, j), Id(i + 1, j + 1), Id(i, j + 1)]);
        return m;
    }

    // ================================================================= editing
    /// <summary>Extrudes the faces as one region. New side walls are added; the moved faces keep their indices.</summary>
    public static void ExtrudeFaces(SubdMesh m, ISet<int> faces, double dist)
    {
        if (faces.Count == 0) return;
        var region = new HashSet<int>(faces.SelectMany(f => m.F[f]));
        var directed = new HashSet<(int, int)>();
        foreach (var fi in faces) { var f = m.F[fi]; for (int i = 0; i < f.Length; i++) directed.Add((f[i], f[(i + 1) % f.Length])); }
        var boundary = directed.Where(e => !directed.Contains((e.Item2, e.Item1))).ToList();

        var newOf = new Dictionary<int, int>();
        var normal = new Dictionary<int, Vec3>();
        foreach (var fi in faces) { var n = m.FaceNormal(fi); foreach (var v in m.F[fi]) normal[v] = normal.GetValueOrDefault(v) + n; }
        var onBoundary = new HashSet<int>(boundary.SelectMany(e => new[] { e.Item1, e.Item2 }));
        foreach (var v in region)
        {
            // points on the outline stay behind to carry the side walls; points inside the region simply move with it (copying them would leave stray, unconnected points)
            if (onBoundary.Contains(v)) { newOf[v] = m.V.Count; m.V.Add(m.V[v] + normal[v].Normalized() * dist); }
            else { newOf[v] = v; m.V[v] += normal[v].Normalized() * dist; }
        }
        foreach (var fi in faces) m.F[fi] = m.F[fi].Select(v => newOf[v]).ToArray();
        foreach (var (a, b) in boundary) m.F.Add([a, b, newOf[b], newOf[a]]);
    }

    /// <summary>Extrudes boundary edges outward as a new strip of quads; returns the new (top) edges.</summary>
    public static List<(int, int)> ExtrudeEdges(SubdMesh m, IEnumerable<(int a, int b)> edges)
    {
        var topo = m.Topo();
        var result = new List<(int, int)>();
        var copies = new Dictionary<int, int>();
        int Copy(int v) { if (!copies.TryGetValue(v, out int c)) { c = m.V.Count; m.V.Add(m.V[v]); copies[v] = c; } return c; }
        foreach (var (a0, b0) in edges)
        {
            if (!topo.EdgeFaces.TryGetValue(SubdMesh.Key(a0, b0), out var fs) || fs.Count != 1) continue; // boundary edges only
            var f = m.F[fs[0]]; int a = a0, b = b0;
            for (int i = 0; i < f.Length; i++) if (f[i] == b0 && f[(i + 1) % f.Length] == a0) { a = b0; b = a0; }  // orient as in the face
            // face has a->b; neighbour across the edge runs b->a
            int a2 = Copy(a), b2 = Copy(b);
            m.F.Add([b, a, a2, b2]);
            result.Add((a2, b2));
        }
        return result;
    }

    /// <summary>Adds an edge loop through the given edge (splits the ring of quads around it). <paramref name="t"/> is how far along the edge
    /// from <paramref name="ea"/> to <paramref name="eb"/> the loop goes (0.5 = the middle; the loop stays parallel all the way round). Returns the new vertices.</summary>
    public static List<int> InsertEdgeLoop(SubdMesh m, int ea, int eb, double t = 0.5) => InsertEdgeLoop(m, ea, eb, t, out _);

    public static List<int> InsertEdgeLoop(SubdMesh m, int ea, int eb, double t, out int startMid)
    {
        startMid = -1;
        t = Math.Clamp(t, 0.02, 0.98);
        var topo = m.Topo();
        var ring = new HashSet<long>(); var queue = new Queue<long>();
        var start = SubdMesh.Key(ea, eb);
        if (!topo.EdgeFaces.ContainsKey(start)) return [];
        ring.Add(start); queue.Enqueue(start);
        var fromOf = new Dictionary<long, int> { [start] = ea };       // which end of each ring edge the fraction is measured from
        var ringFaces = new Dictionary<int, (long e1, long e2)>();
        while (queue.Count > 0)
        {
            var e = queue.Dequeue();
            foreach (var fi in topo.EdgeFaces[e])
            {
                var f = m.F[fi]; if (f.Length != 4) continue;
                int i = 0;
                for (; i < 4; i++) if (SubdMesh.Key(f[i], f[(i + 1) % 4]) == e) break;
                int v0 = f[i], v1 = f[(i + 1) % 4], v2 = f[(i + 2) % 4], v3 = f[(i + 3) % 4];
                var opp = SubdMesh.Key(v2, v3);
                ringFaces[fi] = (e, opp);
                if (ring.Add(opp)) { queue.Enqueue(opp); fromOf[opp] = fromOf[e] == v0 ? v3 : v2; }     // v0->v1 runs the same way as v3->v2
            }
        }
        var mid = new Dictionary<long, int>();
        foreach (var e in ring)
        {
            var (a, b) = SubdMesh.Unkey(e); int from = fromOf.GetValueOrDefault(e, a), other = from == a ? b : a;
            mid[e] = m.V.Count; m.V.Add(m.V[from] + (m.V[other] - m.V[from]) * t);
        }
        startMid = mid[start];
        // a creased edge keeps its crease on both halves
        foreach (var e in ring)
            if (m.Crease.TryGetValue(e, out var c) && c > 0)
            {
                var (a, b) = SubdMesh.Unkey(e); m.Crease.Remove(e);
                m.Crease[SubdMesh.Key(a, mid[e])] = c; m.Crease[SubdMesh.Key(mid[e], b)] = c;
            }

        var newFaces = new List<int[]>();
        for (int fi = 0; fi < m.F.Count; fi++)
        {
            var f = m.F[fi];
            if (ringFaces.TryGetValue(fi, out var pair) && ring.Contains(pair.e1) && ring.Contains(pair.e2))
            {
                int i = 0;
                for (; i < 4; i++) if (SubdMesh.Key(f[i], f[(i + 1) % 4]) == pair.e1) break;
                int v0 = f[i], v1 = f[(i + 1) % 4], v2 = f[(i + 2) % 4], v3 = f[(i + 3) % 4];
                int m01 = mid[SubdMesh.Key(v0, v1)], m23 = mid[SubdMesh.Key(v2, v3)];
                newFaces.Add([v0, m01, m23, v3]);
                newFaces.Add([m01, v1, v2, m23]);
                continue;
            }
            // faces that only touch the ring get the midpoint inserted (becoming n+1-gons)
            var g = new List<int>();
            for (int i = 0; i < f.Length; i++)
            {
                g.Add(f[i]);
                if (mid.TryGetValue(SubdMesh.Key(f[i], f[(i + 1) % f.Length]), out int mv)) g.Add(mv);
            }
            newFaces.Add(g.ToArray());
        }
        m.F.Clear(); m.F.AddRange(newFaces);
        return mid.Values.ToList();
    }

    /// <summary>Several evenly spaced loops through an edge.</summary>
    public static List<int> InsertEdgeLoops(SubdMesh m, int ea, int eb, int count, double firstFraction = -1)
    {
        count = Math.Max(1, count);
        var all = new List<int>(); int from = ea; double done = 0;
        for (int k = 1; k <= count; k++)
        {
            double target = count == 1 && firstFraction > 0 ? firstFraction : (double)k / (count + 1);       // fraction of the original edge from ea
            double rel = (target - done) / (1 - done);
            var nv = InsertEdgeLoop(m, from, eb, rel, out int startMid);
            if (nv.Count == 0 || startMid < 0) break;
            all.AddRange(nv); from = startMid; done = target;
        }
        return all;
    }

    /// <summary>Tighten the selected edges: support loops go in either side of each edge at the given distance, so the smooth surface turns a
    /// sharper corner there (a rounded bevel of about that radius).</summary>
    public static int BevelEdges(SubdMesh m, IEnumerable<(int a, int b)> edges, double width)
    {
        int made = 0;
        foreach (var (a0, b0) in edges.Distinct())
        {
            var topo = m.Topo();
            if (!topo.EdgeFaces.TryGetValue(SubdMesh.Key(a0, b0), out var faces)) continue;
            var rungs = new List<(int from, int to)>();
            foreach (var fi in faces)
            {
                var f = m.F[fi]; if (f.Length != 4) continue;
                for (int i = 0; i < 4; i++)
                {
                    int p0 = f[i], p1 = f[(i + 1) % 4], p3 = f[(i + 3) % 4];
                    if (SubdMesh.Key(p0, p1) == SubdMesh.Key(a0, b0)) { rungs.Add((p0, p3)); break; }
                }
            }
            foreach (var (from, to) in rungs)
            {
                if (!m.Topo().EdgeFaces.ContainsKey(SubdMesh.Key(from, to))) continue;
                double len = (m.V[to] - m.V[from]).Length; if (len < 1e-9) continue;
                if (InsertEdgeLoop(m, from, to, Math.Min(width / len, 0.45)).Count > 0) made++;
            }
        }
        return made;
    }

    /// <summary>Shrink the selected faces inward by <paramref name="amount"/> (and optionally push them out by <paramref name="depth"/>), leaving a border ring of faces. A region keeps shared inner edges; <paramref name="individual"/> insets every face on its own.</summary>
    public static void InsetFaces(SubdMesh m, ISet<int> faces, double amount, double depth = 0, bool individual = false)
    {
        if (faces.Count == 0) return;
        if (individual) { foreach (var f in faces.ToList()) InsetRegion(m, new HashSet<int> { f }, amount, depth); }
        else InsetRegion(m, faces, amount, depth);
    }

    static void InsetRegion(SubdMesh m, ISet<int> faces, double amount, double depth)
    {
        ExtrudeFaces(m, faces, depth);                           // the faces keep their numbers and now stand on new points
        var directed = new HashSet<(int, int)>();
        foreach (var fi in faces) { var f = m.F[fi]; for (int i = 0; i < f.Length; i++) directed.Add((f[i], f[(i + 1) % f.Length])); }
        var boundary = directed.Where(e => !directed.Contains((e.Item2, e.Item1))).Select(e => e.Item1).Distinct().ToList();
        var inward = new Dictionary<int, Vec3>();
        foreach (var v in boundary)
        {
            Vec3 d = Vec3.Zero; int n = 0;
            foreach (var fi in faces) if (m.F[fi].Contains(v)) { d += m.FaceCenter(fi) - m.V[v]; n++; }
            if (n > 0) inward[v] = d / n;
        }
        foreach (var (v, d) in inward)
        {
            double len = d.Length; if (len < 1e-9) continue;
            m.V[v] += d / len * Math.Min(amount, len * 0.9);
        }
    }

    // ---------------------------------------------------------------- edge loops, rings and bridging
    /// <summary>The loop of edges that continues straight through the four-edge vertices from an edge (what artists call an edge loop).</summary>
    public static List<long> EdgeLoop(SubdMesh m, int a, int b)
    {
        var topo = m.Topo();
        var start = SubdMesh.Key(a, b);
        if (!topo.EdgeFaces.ContainsKey(start)) return [];
        var around = new Dictionary<int, List<int>>();
        foreach (var k in topo.EdgeFaces.Keys)
        {
            var (x, y) = SubdMesh.Unkey(k);
            (around.TryGetValue(x, out var lx) ? lx : around[x] = []).Add(y);
            (around.TryGetValue(y, out var ly) ? ly : around[y] = []).Add(x);
        }
        var loop = new List<long> { start }; var seen = new HashSet<long> { start };
        void Walk(int from, int to)
        {
            int prev = from, cur = to;
            while (true)
            {
                if (!around.TryGetValue(cur, out var nb)) break;
                var inFaces = topo.EdgeFaces[SubdMesh.Key(prev, cur)]; int next = -1;
                if(inFaces.Count==1)
                {
                    var boundary=nb.Where(w=>w!=prev && topo.EdgeFaces[SubdMesh.Key(cur,w)].Count==1).ToList();
                    if(boundary.Count==1)next=boundary[0];
                }
                else if (nb.Count == 4)
                    foreach (int w in nb)
                    {
                        if (w == prev) continue;
                        if (!topo.EdgeFaces[SubdMesh.Key(cur, w)].Any(inFaces.Contains)) { next = w; break; }   // the edge on the far side, sharing no face with the one we came by
                    }
                else if(nb.Count==3)
                {
                    // the rim of a cap or other many-sided face: carry on around that face
                    foreach (int fi in inFaces)
                    {
                        var f = m.F[fi]; if (f.Length == 4) continue;
                        int i = Array.IndexOf(f, cur); if (i < 0) continue;
                        int n1 = f[(i + 1) % f.Length], n2 = f[(i + f.Length - 1) % f.Length];
                        next = n1 == prev ? n2 : n1; break;
                    }
                }
                if (next < 0) break;
                long k = SubdMesh.Key(cur, next);
                if (!seen.Add(k)) break;
                loop.Add(k); prev = cur; cur = next;
            }
        }
        Walk(a, b); Walk(b, a);
        return loop;
    }

    /// <summary>The ring of edges that sit side by side across the quads from an edge (the edges an inserted loop would cross).</summary>
    public static HashSet<long> EdgeRing(SubdMesh m, int ea, int eb)
    {
        var topo = m.Topo(); var ring = new HashSet<long>(); var queue = new Queue<long>();
        var start = SubdMesh.Key(ea, eb);
        if (!topo.EdgeFaces.ContainsKey(start)) return ring;
        ring.Add(start); queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var e = queue.Dequeue();
            foreach (var fi in topo.EdgeFaces[e])
            {
                var f = m.F[fi]; if (f.Length != 4) continue;
                int i = 0; for (; i < 4; i++) if (SubdMesh.Key(f[i], f[(i + 1) % 4]) == e) break;
                var opp = SubdMesh.Key(f[(i + 2) % 4], f[(i + 3) % 4]);
                if (ring.Add(opp)) queue.Enqueue(opp);
            }
        }
        return ring;
    }

    /// <summary>Order a set of edges into chains of vertices; closed is true for loops.</summary>
    public static List<(List<int> verts, bool closed)> Chains(IEnumerable<(int a, int b)> edges)
    {
        var adj = new Dictionary<int, List<int>>(); var left = new HashSet<long>();
        foreach (var (a, b) in edges) { if (!left.Add(SubdMesh.Key(a, b))) continue; (adj.TryGetValue(a, out var la) ? la : adj[a] = []).Add(b); (adj.TryGetValue(b, out var lb) ? lb : adj[b] = []).Add(a); }
        var res = new List<(List<int>, bool)>();
        while (left.Count > 0)
        {
            var k0 = left.First(); var (s, t) = SubdMesh.Unkey(k0); left.Remove(k0);
            var chain = new List<int> { s, t };
            bool Extend(bool atEnd)
            {
                int cur = atEnd ? chain[^1] : chain[0];
                foreach (int w in adj[cur]) { var k = SubdMesh.Key(cur, w); if (left.Remove(k)) { if (atEnd) chain.Add(w); else chain.Insert(0, w); return true; } }
                return false;
            }
            while (Extend(true)) { }
            while (Extend(false)) { }
            bool closed = chain.Count > 2 && chain[0] == chain[^1];
            if (closed) chain.RemoveAt(chain.Count - 1);
            res.Add((chain, closed));
        }
        return res;
    }

    /// <summary>Join two boundary chains with a strip of quads. Chains need the same number of vertices.</summary>
    public static void Bridge(SubdMesh m, List<int> A, bool closedA, List<int> B, bool closedB)
    {
        if (A.Count != B.Count) throw new InvalidOperationException($"The two edge groups have different numbers of edges ({A.Count} and {B.Count}). Select the same number on each side.");
        if (closedA != closedB) throw new InvalidOperationException("One selection is a closed loop and the other an open strip.");
        int n = A.Count; bool closed = closedA;
        if (n < (closed ? 3 : 2) || A.Distinct().Count() != n || B.Distinct().Count() != n || A.Intersect(B).Any())
            throw new InvalidOperationException("Select two separate boundary chains without repeated or shared vertices.");
        if (A.Concat(B).Any(v => v < 0 || v >= m.V.Count)) throw new ArgumentOutOfRangeException(nameof(A), "A bridge vertex is outside the cage.");
        var topo = m.Topo();
        bool Forward(int a, int b)
        {
            if (!topo.EdgeFaces.TryGetValue(SubdMesh.Key(a, b), out var fs) || fs.Count != 1)
                throw new InvalidOperationException("Bridge requires boundary edges. Remove the end faces first, or bridge two faces directly.");
            var face = m.F[fs[0]];
            return face[(Array.IndexOf(face, a) + 1) % face.Length] == b;
        }
        bool forwardA = Forward(A[0], A[1]);
        int segs = closed ? n : n - 1;
        for (int i = 0; i < segs; i++)
        {
            if (Forward(A[i], A[(i + 1) % n]) != forwardA)
                throw new InvalidOperationException("The first boundary has inconsistent face orientation.");
            Forward(B[i], B[(i + 1) % n]);
        }
        // best pairing: either direction, and (for loops) every rotation
        double best = double.MaxValue; List<int>? bestB = null;
        foreach (var dir in new[] { B, B.AsEnumerable().Reverse().ToList() })
            for (int r = 0; r < (closed ? n : 1); r++)
            {
                var cand = Enumerable.Range(0, n).Select(i => dir[(i + r) % n]).ToList();
                // Both seams must oppose their existing face, not just have the shortest geometric pairing.
                if (Enumerable.Range(0, segs).Any(i => Forward(cand[i], cand[(i + 1) % n]) == forwardA)) continue;
                double sum = 0; for (int i = 0; i < n; i++) sum += (m.V[A[i]] - m.V[cand[i]]).Length;
                if (sum < best) { best = sum; bestB = cand; }
            }
        if (bestB == null) throw new InvalidOperationException("The second boundary has inconsistent face orientation.");
        bool flip = !forwardA;
        for (int i = 0; i < segs; i++)
        {
            int a0 = A[i], a1 = A[(i + 1) % n], b0 = bestB[i], b1 = bestB[(i + 1) % n];
            // existing face runs a0->a1 means the bridge must run a1->a0
            m.F.Add(flip ? [a0, a1, b1, b0] : [a1, a0, b0, b1]);
        }
    }

    /// <summary>Remove two disjoint, equally sized cap faces and bridge their boundary loops atomically.</summary>
    public static void BridgeFaces(SubdMesh m, int first, int second)
    {
        if (first < 0 || second < 0 || first >= m.F.Count || second >= m.F.Count || first == second)
            throw new ArgumentException("Bridge requires two different valid face indices.");
        var a = m.F[first].ToList(); var b = m.F[second].ToList();
        if (a.Count != b.Count || a.Intersect(b).Any())
            throw new InvalidOperationException("Bridge faces must have the same number of corners and must not share vertices.");
        var work = m.Clone();
        work.F.RemoveAt(Math.Max(first, second)); work.F.RemoveAt(Math.Min(first, second));
        Bridge(work, a, true, b, true);
        m.ReplaceWith(work);
    }

    public static void DeleteFaces(SubdMesh m, ISet<int> faces)
    {
        var keep = new List<int[]>();
        for (int i = 0; i < m.F.Count; i++) if (!faces.Contains(i)) keep.Add(m.F[i]);
        m.F.Clear(); m.F.AddRange(keep);
        m.RemoveUnusedVertices();
    }

    public static void FlipNormals(SubdMesh m)
    {
        for (int i = 0; i < m.F.Count; i++) m.F[i] = m.F[i].Reverse().ToArray();
    }

    public static void SetCrease(SubdMesh m, IEnumerable<(int a, int b)> edges, double sharpness)
    {
        foreach (var (a, b) in edges)
        {
            var k = SubdMesh.Key(a, b);
            if (sharpness <= 0) m.Crease.Remove(k); else m.Crease[k] = sharpness;
        }
    }

    /// <summary>Mirrors the whole cage across a plane through the origin-plane point and welds the seam.</summary>
    public static void MirrorWeld(SubdMesh m, Vec3 origin, Vec3 normal)
    {
        var n = normal.Normalized();
        int nv = m.V.Count;
        var (mn, mx) = m.Bounds();
        double tol = Math.Max((mx - mn).Length * 1e-5, 1e-6);
        var faces = m.F.ToList();
        for (int i = 0; i < nv; i++) { var p = m.V[i]; m.V.Add(p - n * (2 * Vec3.Dot(p - origin, n))); }
        foreach (var f in faces) m.F.Add(f.Select(v => v + nv).Reverse().ToArray());
        foreach (var (k, s) in m.Crease.ToList()) { var (a, b) = SubdMesh.Unkey(k); m.Crease[SubdMesh.Key(a + nv, b + nv)] = s; }
        m.WeldByDistance(tol);
    }

    /// <summary>Adds a polygon from existing vertex ids; orientation is flipped if needed so it faces the viewer direction.</summary>
    public static int AddFace(SubdMesh m, IList<int> verts, Vec3? facing = null)
    {
        var f = verts.ToArray();
        int fi = m.F.Count; m.F.Add(f);
        if (facing is { } dir && Vec3.Dot(m.FaceNormal(fi), dir) < 0) m.F[fi] = f.Reverse().ToArray();
        return fi;
    }

    /// <summary>Caps every open boundary loop with one polygon (facing the same way as its neighbours). Returns how many holes were filled.</summary>
    public static int FillHoles(SubdMesh m)
    {
        var directed = new HashSet<(int, int)>();
        foreach (var f in m.F) for (int i = 0; i < f.Length; i++) directed.Add((f[i], f[(i + 1) % f.Length]));
        var next = new Dictionary<int, List<int>>();
        foreach (var (a, b) in directed) if (!directed.Contains((b, a))) { if (!next.TryGetValue(b, out var l)) next[b] = l = []; l.Add(a); }   // the new face runs b->a
        int filled = 0;
        while (next.Count > 0)
        {
            int start = next.Keys.First(), cur = start; var loop = new List<int>();
            while (true)
            {
                loop.Add(cur);
                if (!next.TryGetValue(cur, out var l) || l.Count == 0) { loop.Clear(); break; }
                int nx = l[0]; l.RemoveAt(0); if (l.Count == 0) next.Remove(cur);
                cur = nx;
                if (cur == start) break;
                if (loop.Count > 100000) { loop.Clear(); break; }
            }
            if (loop.Count >= 3) { m.F.Add(loop.ToArray()); filled++; }
            else foreach (var k in next.Keys.Where(k => next[k].Count == 0).ToList()) next.Remove(k);
            if (loop.Count == 0 && next.Count > 0 && next.Values.All(v => v.Count == 0)) break;
        }
        return filled;
    }

    /// <summary>Joins the selected faces into one polygon (they must form a single patch with one outline).</summary>
    public static bool MergeFaces(SubdMesh m, ISet<int> faces)
    {
        if (faces.Count < 2) return false;
        var directed = new HashSet<(int, int)>();
        foreach (var fi in faces) { var f = m.F[fi]; for (int i = 0; i < f.Length; i++) directed.Add((f[i], f[(i + 1) % f.Length])); }
        var outline = directed.Where(e => !directed.Contains((e.Item2, e.Item1))).ToList();
        if (outline.Count < 3 || outline.GroupBy(e => e.Item1).Any(g => g.Count() > 1)) return false;
        var nxt = outline.ToDictionary(e => e.Item1, e => e.Item2);
        var loop = new List<int>(); int s = outline[0].Item1, c = s;
        do { loop.Add(c); if (!nxt.TryGetValue(c, out c)) return false; } while (c != s && loop.Count <= outline.Count);
        if (loop.Count != outline.Count) return false;      // more than one outline: not a single patch
        // the merged face takes the place of the first merged one, so the other faces keep their order
        var keep = new List<int[]>(); int first = faces.Min();
        for (int i = 0; i < m.F.Count; i++)
        {
            if (i == first) keep.Add(loop.ToArray());
            else if (!faces.Contains(i)) keep.Add(m.F[i]);
        }
        m.F.Clear(); m.F.AddRange(keep);
        m.RemoveUnusedVertices();
        return true;
    }

    /// <summary>Collapses each edge to its midpoint (the two ends become one vertex; faces that shrink below a triangle disappear).</summary>
    public static int CollapseEdges(SubdMesh m, IEnumerable<(int a, int b)> edges)
    {
        var map = new Dictionary<int, int>();
        int Find(int v) { while (map.TryGetValue(v, out var w) && w != v) v = w; return v; }
        int n = 0;
        foreach (var (a0, b0) in edges)
        {
            int a = Find(a0), b = Find(b0); if (a == b) continue;
            m.V[a] = (m.V[a] + m.V[b]) * 0.5; map[b] = a; n++;
        }
        var faces = new List<int[]>();
        foreach (var f in m.F)
        {
            var g = new List<int>();
            foreach (var v in f) { int r = Find(v); if (g.Count == 0 || g[^1] != r) g.Add(r); }
            if (g.Count > 1 && g[0] == g[^1]) g.RemoveAt(g.Count - 1);
            if (g.Count >= 3) faces.Add(g.ToArray());
        }
        m.F.Clear(); m.F.AddRange(faces);
        m.RemoveUnusedVertices();
        return n;
    }

    /// <summary>Cuts a face in two along the line between two of its corners.</summary>
    public static bool SplitFace(SubdMesh m, int fi, int va, int vb)
    {
        var f = m.F[fi]; int ia = Array.IndexOf(f, va), ib = Array.IndexOf(f, vb);
        if (ia < 0 || ib < 0 || ia == ib || (ia + 1) % f.Length == ib || (ib + 1) % f.Length == ia) return false;
        var p = new List<int>(); for (int i = ia; ; i = (i + 1) % f.Length) { p.Add(f[i]); if (i == ib) break; }
        var q = new List<int>(); for (int i = ib; ; i = (i + 1) % f.Length) { q.Add(f[i]); if (i == ia) break; }
        m.F[fi] = p.ToArray(); m.F.Add(q.ToArray());
        return true;
    }

    /// <summary>Moves each vertex toward the average of its neighbours (open-border vertices stay put). The vertex set is relaxed for <paramref name="passes"/> rounds.</summary>
    public static void SmoothVertices(SubdMesh m, ISet<int> verts, double strength, int passes)
    {
        var topo = m.Topo(); var nb = new HashSet<int>[m.V.Count];
        var border = new HashSet<int>();
        foreach (var (k, fs) in topo.EdgeFaces)
        {
            var (a, b) = SubdMesh.Unkey(k); (nb[a] ??= []).Add(b); (nb[b] ??= []).Add(a);
            if (fs.Count == 1) { border.Add(a); border.Add(b); }
        }
        for (int p = 0; p < passes; p++)
        {
            var next = new Dictionary<int, Vec3>();
            foreach (var v in verts)
            {
                if (v >= nb.Length || nb[v] == null || border.Contains(v) || m.IsFrozen(v)) continue;
                var avg = nb[v].Aggregate(Vec3.Zero, (s, i) => s + m.V[i]) / nb[v].Count;
                next[v] = m.V[v] + (avg - m.V[v]) * strength;
            }
            foreach (var (v, pos) in next) m.V[v] = pos;
        }
    }

    /// <summary>Squashes the vertices onto their best-fit plane (normal = the average of the surrounding face normals).</summary>
    public static bool FlattenVertices(SubdMesh m, ISet<int> verts, double strength = 1)
    {
        if (verts.Count < 3) return false;
        var c = verts.Aggregate(Vec3.Zero, (s, v) => s + m.V[v]) / verts.Count;
        var topo = m.Topo(); var n = Vec3.Zero;
        foreach (var v in verts) foreach (var fi in topo.VertFaces[v]) n += m.FaceNormal(fi);
        if (n.Length < 1e-9) return false;
        n = n.Normalized();
        foreach (var v in verts) if (!m.IsFrozen(v)) m.V[v] -= n * (Vec3.Dot(m.V[v] - c, n) * strength);
        return true;
    }

    /// <summary>Gives the cage wall thickness: a second skin is added <paramref name="thickness"/> mm inside (negative = outside) and open borders are joined with a rim.</summary>
    public static void Solidify(SubdMesh m, double thickness)
    {
        int nv = m.V.Count; var n = new Vec3[nv];
        for (int f = 0; f < m.F.Count; f++) { var fn = m.FaceNormal(f); foreach (var v in m.F[f]) n[v] += fn; }
        for (int v = 0; v < nv; v++) m.V.Add(n[v].Length < 1e-9 ? m.V[v] : m.V[v] - n[v].Normalized() * thickness);
        var directed = new HashSet<(int, int)>();
        foreach (var f in m.F) for (int i = 0; i < f.Length; i++) directed.Add((f[i], f[(i + 1) % f.Length]));
        var inner = m.F.Select(f => f.Select(v => v + nv).Reverse().ToArray()).ToList();
        foreach (var (a, b) in directed) if (!directed.Contains((b, a))) inner.Add([b, a, a + nv, b + nv]);
        m.F.AddRange(inner);
        foreach (var (k, s) in m.Crease.ToList()) { var (a, b) = SubdMesh.Unkey(k); m.Crease[SubdMesh.Key(a + nv, b + nv)] = s; }
    }

    public static IEnumerable<int> VerticesOf(SubdMesh m, IEnumerable<int> faces) => faces.SelectMany(f => m.F[f]).Distinct();

    /// <summary>Pull: move the points onto the smooth surface they currently only control (Catmull-Clark limit positions). Returns how many moved.</summary>
    public static int PullToLimit(SubdMesh m, ISet<int> verts, double strength = 1)
    {
        var topo = m.Topo(); var result = new Dictionary<int, Vec3>();
        foreach (int v in verts)
        {
            if (v < 0 || v >= m.V.Count) continue;
            var faces = topo.VertFaces[v]; int n = faces.Count;
            if (n < 3 || faces.Any(f => m.F[f].Length != 4)) continue;                          // regular quad fans only
            bool closedFan = true; var around = new List<int>(); var diagonals = Vec3.Zero; var edgeSum = Vec3.Zero;
            foreach (int f in faces)
            {
                var fc = m.F[f]; int i = Array.IndexOf(fc, v);
                int next = fc[(i + 1) % 4], prev = fc[(i + 3) % 4], opposite = fc[(i + 2) % 4];
                diagonals += m.V[opposite]; around.Add(next); around.Add(prev);
            }
            foreach (var u in around.Distinct())
            {
                if (!topo.EdgeFaces.TryGetValue(SubdMesh.Key(v, u), out var ef) || ef.Count != 2) closedFan = false;
                edgeSum += m.V[u];
            }
            if (!closedFan || around.Distinct().Count() != n) continue;                         // boundary points stay put
            var limit = (m.V[v] * (n * n) + edgeSum * 4 + diagonals) / (n * (n + 5.0));
            result[v] = m.V[v] + (limit - m.V[v]) * Math.Clamp(strength, 0, 1);
        }
        foreach (var (v, p) in result) m.V[v] = p;
        return result.Count;
    }

    /// <summary>Merge Edge: remove each selected inner edge, joining the two faces it separates. Returns how many edges were removed.</summary>
    public static int MergeEdges(SubdMesh m, IEnumerable<(int a, int b)> edges)
    {
        int merged = 0;
        // merging can renumber points, so remember each edge by where its ends are
        var located = edges.Select(e => (pa: m.V[e.a], pb: m.V[e.b])).ToList();
        foreach (var (pa, pb) in located)
        {
            int a = m.V.FindIndex(p => p.DistanceTo(pa) < 1e-9), b = m.V.FindIndex(p => p.DistanceTo(pb) < 1e-9);
            if (a < 0 || b < 0) continue;
            var topo = m.Topo();
            if (!topo.EdgeFaces.TryGetValue(SubdMesh.Key(a, b), out var fs) || fs.Count != 2) continue;
            if (MergeFaces(m, new HashSet<int>(fs))) merged++;
        }
        return merged;
    }

    /// <summary>Unweld Edges: split the cage along the selected edges, giving each side its own points so the surface opens into a seam. Returns the number of points that were duplicated.</summary>
    public static int UnweldEdges(SubdMesh m, IEnumerable<(int a, int b)> edges)
    {
        var cut = new HashSet<long>(edges.Select(e => SubdMesh.Key(e.a, e.b)));
        if (cut.Count == 0) return 0;
        var topo = m.Topo(); int added = 0;
        var ends = cut.SelectMany(k => { var (x, y) = SubdMesh.Unkey(k); return new[] { x, y }; }).Distinct().ToList();
        foreach (int v in ends)
        {
            var faces = topo.VertFaces[v].ToList(); if (faces.Count < 2) continue;
            var group = faces.ToDictionary(f => f, f => f);
            int Find(int f) { while (group[f] != f) f = group[f] = group[group[f]]; return f; }
            // two faces around the point stay together when they share an edge at the point that is not cut
            foreach (var f1 in faces)
                foreach (var f2 in faces)
                {
                    if (f1 >= f2) continue;
                    var a = m.F[f1]; var b = m.F[f2];
                    foreach (int u in a.Where(u => u != v && b.Contains(u)))
                    {
                        if (!IsEdge(a, v, u) || !IsEdge(b, v, u) || cut.Contains(SubdMesh.Key(v, u))) continue;
                        group[Find(f1)] = Find(f2);
                    }
                }
            var fans = faces.GroupBy(Find).ToList(); if (fans.Count < 2) continue;
            foreach (var fan in fans.Skip(1))
            {
                int copy = m.V.Count; m.V.Add(m.V[v]); added++;
                foreach (int f in fan) m.F[f] = m.F[f].Select(x => x == v ? copy : x).ToArray();
            }
        }
        // creases that sat on a cut edge are no longer shared by two faces; drop them rather than leave them on one side by accident
        foreach (var k in cut) m.Crease.Remove(k);
        return added;
    }

    static bool IsEdge(int[] face, int a, int b)
    {
        for (int i = 0; i < face.Length; i++) { int p = face[i], q = face[(i + 1) % face.Length]; if (p == a && q == b || p == b && q == a) return true; }
        return false;
    }

    // ================================================================= corner creases, chamfer, slide
    /// <summary>Corner crease: the point keeps its place instead of being pulled into the smooth surface (1 = fully sharp, 0 clears). Stored as a crease on (v, v).</summary>
    public static int SetVertexCrease(SubdMesh m, IEnumerable<int> verts, double sharpness)
    {
        int n = 0;
        foreach (int v in verts.Distinct())
        {
            if (v < 0 || v >= m.V.Count) continue;
            long k = SubdMesh.Key(v, v);
            if (sharpness <= 0) m.Crease.Remove(k); else m.Crease[k] = sharpness;
            n++;
        }
        return n;
    }

    /// <summary>The faces around a point in rotation order. For an open fan (boundary point) the list starts at the boundary and <paramref name="open"/> is true.</summary>
    static List<int> Fan(SubdMesh m, SubdMesh.Topology topo, int v, out bool open)
    {
        var faces = topo.VertFaces[v]; open = false;
        if (faces.Count == 0) return [];
        int Next(int f) => m.F[f][(Array.IndexOf(m.F[f], v) + 1) % m.F[f].Length];
        int Prev(int f) => m.F[f][(Array.IndexOf(m.F[f], v) + m.F[f].Length - 1) % m.F[f].Length];
        int Across(int f, int u) { var l = topo.EdgeFaces[SubdMesh.Key(v, u)]; return l.Count == 2 ? (l[0] == f ? l[1] : l[0]) : -1; }
        // start at a face whose previous edge is a border, if there is one
        int start = faces[0];
        foreach (int f in faces) if (topo.EdgeFaces[SubdMesh.Key(v, Prev(f))].Count != 2) { start = f; open = true; break; }
        var order = new List<int> { start }; var seen = new HashSet<int> { start }; int cur = start;
        while (true)
        {
            int g = Across(cur, Next(cur));
            if (g < 0 || !seen.Add(g)) break;
            order.Add(g); cur = g;
        }
        return order.Count == faces.Count ? order : [];
    }

    /// <summary>
    /// Chamfer (edge bevel): every selected inner edge is replaced by a narrow strip of faces of the given width, and points where
    /// three or more selected edges meet get a corner face. Returns the number of new faces.
    /// </summary>
    public static int ChamferEdges(SubdMesh m, IEnumerable<(int a, int b)> edges, double width)
    {
        var topo = m.Topo();
        var cut = new HashSet<long>();
        foreach (var (a, b) in edges) { var k = SubdMesh.Key(a, b); if (topo.EdgeFaces.TryGetValue(k, out var l) && l.Count == 2 && a != b) cut.Add(k); }
        if (cut.Count == 0) return 0;
        var points = cut.SelectMany(k => { var (a, b) = SubdMesh.Unkey(k); return new[] { a, b }; }).Distinct().ToList();

        // per point: which copy of it each surrounding face ends up with
        var copyOf = new Dictionary<(int v, int face), int>();
        var cornerLoops = new Dictionary<int, List<int>>();
        var newFaces = new List<int[]>();
        foreach (int v in points)
        {
            var fan = Fan(m, topo, v, out bool open);
            if (fan.Count == 0) continue;
            int n = fan.Count;
            bool IsCut(int i)                       // the edge between fan[i] and fan[i+1]
            {
                int f = fan[i]; var fc = m.F[f]; int u = fc[(Array.IndexOf(fc, v) + 1) % fc.Length];
                return cut.Contains(SubdMesh.Key(v, u));
            }
            // sectors: runs of faces between cut edges (and, for an open fan, the borders)
            var sectors = new List<List<int>>(); var current = new List<int>();
            int limit = open ? n - 1 : n;
            var cutAfter = Enumerable.Range(0, limit).Select(IsCut).ToList();
            if (!open)
            {
                int first = cutAfter.IndexOf(true); if (first < 0) continue;
                for (int step = 1; step <= n; step++)
                {
                    int i = (first + step) % n; current.Add(fan[i]);
                    if (cutAfter[i]) { sectors.Add(current); current = []; }
                }
            }
            else
            {
                for (int i = 0; i < n; i++)
                {
                    current.Add(fan[i]);
                    if (i == n - 1 || cutAfter[i]) { sectors.Add(current); current = []; }
                }
                if (sectors.Count < 2) continue;
            }
            var loop = new List<int>();
            foreach (var sector in sectors)
            {
                var centre = sector.Aggregate(Vec3.Zero, (acc, f) => acc + m.FaceCenter(f)) / sector.Count;
                var dir = (centre - m.V[v]).Normalized();
                int copy = m.V.Count; m.V.Add(m.V[v] + dir * width);
                foreach (int f in sector) copyOf[(v, f)] = copy;
                loop.Add(copy);
            }
            cornerLoops[v] = loop;
        }
        // which face runs along each cut edge a->b (found before the faces are rewritten)
        var runsForward = new Dictionary<long, int>();
        foreach (var k in cut)
        {
            var (a, b) = SubdMesh.Unkey(k);
            foreach (int f in topo.EdgeFaces[k])
            {
                var fc = m.F[f];
                for (int i = 0; i < fc.Length; i++) if (fc[i] == a && fc[(i + 1) % fc.Length] == b) runsForward[k] = f;
            }
        }
        // rewrite the faces onto the copies
        for (int f = 0; f < m.F.Count; f++)
        {
            var fc = m.F[f];
            for (int i = 0; i < fc.Length; i++) if (copyOf.TryGetValue((fc[i], f), out int c)) fc[i] = c;
        }
        // strips along the cut edges
        foreach (var k in cut)
        {
            var (a, b) = SubdMesh.Unkey(k); var faces = topo.EdgeFaces[k];
            int fl = runsForward.TryGetValue(k, out int forwardFace) ? forwardFace : faces[0];
            int fr = faces[0] == fl ? faces[1] : faces[0];
            int aL = copyOf.TryGetValue((a, fl), out var x1) ? x1 : a, bL = copyOf.TryGetValue((b, fl), out var x2) ? x2 : b;
            int aR = copyOf.TryGetValue((a, fr), out var x3) ? x3 : a, bR = copyOf.TryGetValue((b, fr), out var x4) ? x4 : b;
            var quad = new[] { bL, aL, aR, bR }.Aggregate(new List<int>(), (acc, v) => { if (acc.Count == 0 || acc[^1] != v) acc.Add(v); return acc; });
            if (quad.Count > 1 && quad[0] == quad[^1]) quad.RemoveAt(quad.Count - 1);
            if (quad.Count >= 3) newFaces.Add([.. quad]);
        }        // corner faces where three or more strips meet
        foreach (var (v, loop) in cornerLoops)
            if (loop.Count >= 3) newFaces.Add([.. loop.AsEnumerable().Reverse()]);
        m.F.AddRange(newFaces);
        foreach (var k in cut) m.Crease.Remove(k);
        m.RemoveUnusedVertices();
        return newFaces.Count;
    }

    /// <summary>
    /// Slide edges: move the selected edge loop along the rails of the quads beside it. A positive amount slides towards one side
    /// (the same side all along the loop), a negative amount to the other; 1 reaches the next loop. Returns how many points moved.
    /// </summary>
    public static int SlideEdges(SubdMesh m, IEnumerable<(int a, int b)> edges, double amount)
    {
        var selected = new HashSet<long>(edges.Select(e => SubdMesh.Key(e.a, e.b)));
        if (selected.Count == 0) return 0;
        var topo = m.Topo();
        // for every selected point, the rail neighbours on each side (found in quads that contain a selected edge)
        var rails = new Dictionary<int, List<int>>();
        foreach (var k in selected)
        {
            var (a, b) = SubdMesh.Unkey(k);
            if (!topo.EdgeFaces.TryGetValue(k, out var faces)) continue;
            foreach (int f in faces)
            {
                var fc = m.F[f]; if (fc.Length != 4) continue;
                foreach (int end in new[] { a, b })
                {
                    int i = Array.IndexOf(fc, end), other = end == a ? b : a;
                    int p = fc[(i + 1) % 4], q = fc[(i + 3) % 4];
                    int rail = p == other ? q : q == other ? p : -1;
                    if (rail < 0 || selected.Contains(SubdMesh.Key(end, rail))) continue;
                    if (!rails.TryGetValue(end, out var list)) rails[end] = list = [];
                    if (!list.Contains(rail)) list.Add(rail);
                }
            }
        }
        if (rails.Count == 0) return 0;
        // choose one side consistently: follow the direction of the first point's first rail
        Vec3? side = null; var moved = new Dictionary<int, Vec3>();
        foreach (var (v, list) in rails.OrderBy(r => r.Key))
        {
            var dirs = list.Select(u => m.V[u] - m.V[v]).ToList();
            side ??= dirs[0].Normalized();
            int best = 0; double bestDot = double.NegativeInfinity;
            for (int i = 0; i < dirs.Count; i++) { double d = Vec3.Dot(dirs[i].Normalized(), side.Value); if (d > bestDot) { bestDot = d; best = i; } }
            var chosen = list[best]; int opposite = list.Count > 1 ? list[1 - best] : -1;
            if (amount >= 0) moved[v] = m.V[v] + (m.V[chosen] - m.V[v]) * Math.Min(amount, 1);
            else if (opposite >= 0) moved[v] = m.V[v] + (m.V[opposite] - m.V[v]) * Math.Min(-amount, 1);
        }
        foreach (var (v, p) in moved) m.V[v] = p;
        return moved.Count;
    }
}
