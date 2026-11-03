using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Kernel;

namespace Prism.Meshing;

public enum BooleanOp { Union, Subtract, Intersect }

/// <summary>Outcome of a mesh boolean: either a mesh, or an error the user can act on. Warning is set when the result is usable but not perfectly watertight.</summary>
public sealed record BooleanResult(TriMesh? Mesh, string? Error, string? Warning = null, int CutSegments = 0, int PiecesKept = 0, int PiecesDropped = 0);

/// <summary>
/// Union / subtract / intersect between two closed, manifold triangle meshes.
/// Method: find intersecting triangle pairs with bounding-volume trees, compute the intersection segments, retriangulate every cut triangle so
/// the segments become mesh edges (constrained triangulation by edge flipping), flood-fill the surface into patches that are not crossed by the
/// other mesh, classify each patch inside/outside the other mesh by ray parity, and stitch the wanted patches together.
/// The second mesh is nudged by a few millionths of the model size so coplanar faces never produce degenerate cases; flush (face-touching)
/// meshes therefore need a slight overlap to be merged.
/// </summary>
public static class MeshBoolean
{
    /// <summary>Null when the mesh is usable, otherwise a message telling the user what is wrong.</summary>
    public static string? CheckInput(TriMesh m, string name)
    {
        if (m.TriCount == 0) return $"{name} is empty.";
        var r = MeshAnalysis.Report(m);
        if (r.BoundaryEdges > 0) return $"{name} is not closed ({r.BoundaryEdges} open edges). Run Repair (fill holes) first.";
        if (r.NonManifoldEdges > 0) return $"{name} has {r.NonManifoldEdges} non-manifold edges. Run Repair first.";
        if (r.InconsistentEdges > 0) return $"{name} has inconsistent triangle orientation. Run Repair first.";
        if (r.Volume <= 0) return $"{name} is inside out (negative volume). Run Repair to fix the normals first.";
        return null;
    }

    public static BooleanResult Run(TriMesh a, TriMesh b, BooleanOp op)
    {
        var err = CheckInput(a, "The first mesh") ?? CheckInput(b, "The second mesh");
        if (err != null) return new BooleanResult(null, err);
        BooleanResult? last = null;
        for (int attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                var r = Attempt(a, b, op, attempt);
                if (r.Mesh == null || r.Warning == null) return r;
                last = r;
            }
            catch (InvalidOperationException) { }
        }
        return last ?? new BooleanResult(null, "The boolean could not be completed because the surfaces touch in a degenerate way. Try moving one mesh slightly, or Repair both first.");
    }

    // nudge directions for the retry attempts (generic, irrational-ish)
    static readonly Vec3[] Nudge = [new(0.5773, 0.3411, 0.7391), new(-0.2718, 0.8123, 0.4547), new(0.1414, -0.6180, 0.9269), new(0.7071, 0.1234, -0.3333)];

    static BooleanResult Attempt(TriMesh a, TriMesh b, BooleanOp op, int attempt)
    {
        var lo = new Vec3(Math.Min(a.Min.X, b.Min.X), Math.Min(a.Min.Y, b.Min.Y), Math.Min(a.Min.Z, b.Min.Z));
        var hi = new Vec3(Math.Max(a.Max.X, b.Max.X), Math.Max(a.Max.Y, b.Max.Y), Math.Max(a.Max.Z, b.Max.Z));
        double diag = Math.Max((hi - lo).Length, 1e-9);
        var shift = Nudge[attempt % Nudge.Length].Normalized() * (diag * 2e-5 * (1 + attempt));
        var bv = new float[b.V.Length];
        for (int i = 0; i < b.VertexCount; i++) { bv[3 * i] = (float)(b.V[3 * i] + shift.X); bv[3 * i + 1] = (float)(b.V[3 * i + 1] + shift.Y); bv[3 * i + 2] = (float)(b.V[3 * i + 2] + shift.Z); }
        var bp = new TriMesh(bv, b.T);

        var pool = new Pool(diag * 1e-8);
        for (int i = 0; i < a.VertexCount; i++) pool.AddRaw(a.P(i));
        int offB = a.VertexCount;
        for (int i = 0; i < bp.VertexCount; i++) pool.AddRaw(bp.P(i));

        // ---- intersection segments
        var segA = new Dictionary<int, List<(int, int)>>(); var segB = new Dictionary<int, List<(int, int)>>();
        var cutEdges = new HashSet<long>(); int segCount = 0;
        var ta = new BoxTree(a, 0); var tb = new BoxTree(bp, 0);
        BoxTree.Pairs(ta, tb, (i, j) =>
        {
            var (a0, a1, a2) = (a.P(a.T[3 * i]), a.P(a.T[3 * i + 1]), a.P(a.T[3 * i + 2]));
            var (b0, b1, b2) = (bp.P(bp.T[3 * j]), bp.P(bp.T[3 * j + 1]), bp.P(bp.T[3 * j + 2]));
            if (!TriTri(a0, a1, a2, b0, b1, b2, out var p, out var q)) return;
            int ip = pool.Add(p), iq = pool.Add(q);
            if (ip == iq) return;
            if (!cutEdges.Add(EdgeKey(ip, iq))) return;
            segCount++;
            if (!segA.TryGetValue(i, out var la)) segA[i] = la = []; la.Add((ip, iq));
            if (!segB.TryGetValue(j, out var lb)) segB[j] = lb = []; lb.Add((ip, iq));
        });

        if (segCount == 0)
        {
            // no surface crossing: disjoint or one inside the other
            bool aInB = Inside(a.P(a.T[0]), bp), bInA = Inside(bp.P(bp.T[0]), a);
            return Disjoint(a, b, bp, op, aInB, bInA);
        }

        // ---- retriangulate
        var trisA = Split(pool, a, 0, segA); var trisB = Split(pool, bp, offB, segB);

        // ---- patches and classification
        var keepA = new List<(int, int, int)>(); var keepB = new List<(int, int, int)>();
        int kept = 0, dropped = 0;
        bool wantAOut = op != BooleanOp.Intersect, wantBOut = op == BooleanOp.Union;
        foreach (var (tris, other, isA) in new[] { (trisA, bp, true), (trisB, a, false) })
        {
            var (patch, count) = Patches(tris, cutEdges);
            var rep = new int[count]; var repArea = new double[count]; Array.Fill(rep, -1);
            for (int t = 0; t < tris.Count; t++)
            {
                var (x, y, z) = tris[t];
                double ar = Vec3.Cross(pool.Pts[y] - pool.Pts[x], pool.Pts[z] - pool.Pts[x]).Length;
                if (rep[patch[t]] < 0 || ar > repArea[patch[t]]) { rep[patch[t]] = t; repArea[patch[t]] = ar; }
            }
            var inside = new bool[count];
            for (int k = 0; k < count; k++)
            {
                var (x, y, z) = tris[rep[k]];
                inside[k] = Inside((pool.Pts[x] + pool.Pts[y] + pool.Pts[z]) / 3.0, other);
            }
            for (int t = 0; t < tris.Count; t++)
            {
                bool ins = inside[patch[t]];
                var (x, y, z) = tris[t];
                if (x == y || y == z || x == z) continue;
                if (isA)
                {
                    if (ins ? !wantAOut : wantAOut) { keepA.Add((x, y, z)); } // inside kept for intersect, outside kept otherwise
                }
                else
                {
                    switch (op)
                    {
                        case BooleanOp.Union: if (!ins) keepB.Add((x, y, z)); break;
                        case BooleanOp.Intersect: if (ins) keepB.Add((x, y, z)); break;
                        case BooleanOp.Subtract: if (ins) keepB.Add((x, z, y)); break;
                    }
                }
            }
            for (int k = 0; k < count; k++) { bool want = isA ? (inside[k] ? !wantAOut : wantAOut) : (op == BooleanOp.Union ? !inside[k] : inside[k]); if (want) kept++; else dropped++; }
        }
        var all = keepA.Concat(keepB).ToList();
        if (all.Count == 0) return new BooleanResult(null, op == BooleanOp.Intersect ? "The meshes do not overlap, so the intersection is empty." : "The result is empty.");
        var result = Build(pool, all);
        var topo = result.Topology();
        string? warn = null;
        if (topo.BoundaryEdges + topo.NonManifoldEdges + topo.InconsistentEdges > 0)
            warn = $"The result has {topo.BoundaryEdges} open and {topo.NonManifoldEdges} non-manifold edges where the surfaces meet. Run Repair on it, or nudge one mesh slightly and try again.";
        return new BooleanResult(result, null, warn, segCount, kept, dropped);
    }

    static BooleanResult Disjoint(TriMesh a, TriMesh b, TriMesh bp, BooleanOp op, bool aInB, bool bInA)
    {
        TriMesh Merge(TriMesh x, TriMesh y) => TriMesh.Merge([x, y]);
        TriMesh Flip(TriMesh x) => x.Transformed(p => p, true);
        switch (op)
        {
            case BooleanOp.Union:
                if (aInB) return new(b, null); if (bInA) return new(a, null); return new(Merge(a, b), null);
            case BooleanOp.Intersect:
                if (aInB) return new(a, null); if (bInA) return new(b, null);
                return new(null, "The meshes do not overlap, so the intersection is empty.");
            default:
                if (aInB) return new(null, "The second mesh completely contains the first, so nothing is left.");
                if (bInA) return new(Merge(a, Flip(b)), null, "The second mesh is completely inside the first: the result is a hollow part with an internal cavity.");
                return new(a, null, "The meshes do not touch, so the first mesh is unchanged.");
        }
    }

    static TriMesh Build(Pool pool, List<(int, int, int)> tris)
    {
        var v = new float[pool.Pts.Count * 3];
        for (int i = 0; i < pool.Pts.Count; i++) { v[3 * i] = (float)pool.Pts[i].X; v[3 * i + 1] = (float)pool.Pts[i].Y; v[3 * i + 2] = (float)pool.Pts[i].Z; }
        var t = new int[tris.Count * 3];
        for (int i = 0; i < tris.Count; i++) { t[3 * i] = tris[i].Item1; t[3 * i + 1] = tris[i].Item2; t[3 * i + 2] = tris[i].Item3; }
        return MeshOps.Compact(v, t);
    }

    static long EdgeKey(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

    // ================================================================= point pool
    sealed class Pool
    {
        public readonly List<Vec3> Pts = [];
        readonly double _eps;
        readonly Dictionary<(long, long, long), List<int>> _grid = [];
        public Pool(double eps) { _eps = Math.Max(eps, 1e-12); }
        (long, long, long) Cell(Vec3 p) => ((long)Math.Floor(p.X / _eps), (long)Math.Floor(p.Y / _eps), (long)Math.Floor(p.Z / _eps));
        public int AddRaw(Vec3 p)
        {
            Pts.Add(p);
            var c = Cell(p); if (!_grid.TryGetValue(c, out var l)) _grid[c] = l = []; l.Add(Pts.Count - 1);
            return Pts.Count - 1;
        }
        public int Add(Vec3 p)
        {
            var (cx, cy, cz) = Cell(p);
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                        if (_grid.TryGetValue((cx + dx, cy + dy, cz + dz), out var l))
                            foreach (int j in l) if ((Pts[j] - p).Length <= _eps) return j;
            return AddRaw(p);
        }
    }

    // ================================================================= bounding-box tree and pair traversal
    sealed class BoxTree
    {
        readonly double[] _bb; readonly int[] _l, _r, _first, _count; readonly int[] _tris; int _n;
        readonly double[] _cen; readonly TriMesh _m;
        public BoxTree(TriMesh m, int _)
        {
            _m = m; int nt = m.TriCount;
            _tris = Enumerable.Range(0, nt).ToArray();
            int cap = Math.Max(4, nt * 2);
            _bb = new double[cap * 6]; _l = new int[cap]; _r = new int[cap]; _first = new int[cap]; _count = new int[cap];
            _cen = new double[nt * 3];
            for (int t = 0; t < nt; t++)
            {
                var c = (m.P(m.T[3 * t]) + m.P(m.T[3 * t + 1]) + m.P(m.T[3 * t + 2])) / 3.0;
                _cen[3 * t] = c.X; _cen[3 * t + 1] = c.Y; _cen[3 * t + 2] = c.Z;
            }
            if (nt > 0) Build(0, nt);
        }
        int Build(int lo, int hi)
        {
            int id = _n++;
            double x0 = double.MaxValue, y0 = x0, z0 = x0, x1 = double.MinValue, y1 = x1, z1 = x1;
            for (int i = lo; i < hi; i++)
                for (int k = 0; k < 3; k++)
                {
                    var p = _m.P(_m.T[3 * _tris[i] + k]);
                    x0 = Math.Min(x0, p.X); x1 = Math.Max(x1, p.X); y0 = Math.Min(y0, p.Y); y1 = Math.Max(y1, p.Y); z0 = Math.Min(z0, p.Z); z1 = Math.Max(z1, p.Z);
                }
            // tiny padding so touching boxes overlap despite rounding
            double pad = 1e-9 * Math.Max(1, Math.Max(x1 - x0, Math.Max(y1 - y0, z1 - z0)));
            _bb[6 * id] = x0 - pad; _bb[6 * id + 1] = y0 - pad; _bb[6 * id + 2] = z0 - pad; _bb[6 * id + 3] = x1 + pad; _bb[6 * id + 4] = y1 + pad; _bb[6 * id + 5] = z1 + pad;
            if (hi - lo <= 4) { _first[id] = lo; _count[id] = hi - lo; _l[id] = -1; return id; }
            int axis = (x1 - x0) >= (y1 - y0) && (x1 - x0) >= (z1 - z0) ? 0 : (y1 - y0) >= (z1 - z0) ? 1 : 2;
            var seg = new ArraySegment<int>(_tris, lo, hi - lo);
            var sorted = seg.OrderBy(t => _cen[3 * t + axis]).ToArray();
            Array.Copy(sorted, 0, _tris, lo, sorted.Length);
            int mid = (lo + hi) / 2;
            _l[id] = Build(lo, mid); _r[id] = Build(mid, hi);
            return id;
        }
        bool Overlap(int n, BoxTree o, int m)
        {
            for (int k = 0; k < 3; k++)
                if (_bb[6 * n + k] > o._bb[6 * m + 3 + k] || _bb[6 * n + 3 + k] < o._bb[6 * m + k]) return false;
            return true;
        }
        public static void Pairs(BoxTree a, BoxTree b, Action<int, int> visit)
        {
            if (a._m.TriCount == 0 || b._m.TriCount == 0) return;
            var stack = new Stack<(int, int)>(); stack.Push((0, 0));
            while (stack.Count > 0)
            {
                var (n, m) = stack.Pop();
                if (!a.Overlap(n, b, m)) continue;
                bool la = a._l[n] < 0, lb = b._l[m] < 0;
                if (la && lb)
                {
                    for (int i = 0; i < a._count[n]; i++)
                        for (int j = 0; j < b._count[m]; j++) visit(a._tris[a._first[n] + i], b._tris[b._first[m] + j]);
                }
                else if (la) { stack.Push((n, b._l[m])); stack.Push((n, b._r[m])); }
                else if (lb) { stack.Push((a._l[n], m)); stack.Push((a._r[n], m)); }
                else
                {
                    stack.Push((a._l[n], b._l[m])); stack.Push((a._l[n], b._r[m])); stack.Push((a._r[n], b._l[m])); stack.Push((a._r[n], b._r[m]));
                }
            }
        }
    }

    // ================================================================= triangle-triangle intersection
    static bool Lex(Vec3 a, Vec3 b) => a.X != b.X ? a.X < b.X : a.Y != b.Y ? a.Y < b.Y : a.Z < b.Z;

    static bool Crossings(Vec3 v0, Vec3 v1, Vec3 v2, double d0, double d1, double d2, Vec3 dir, out Vec3 pLo, out double tLo, out Vec3 pHi, out double tHi)
    {
        Span<Vec3> pts = stackalloc Vec3[2]; int n = 0;
        Vec3[] v = [v0, v1, v2]; double[] d = [d0, d1, d2];
        for (int i = 0; i < 3 && n < 2; i++)
        {
            int j = (i + 1) % 3;
            if ((d[i] >= 0) == (d[j] >= 0)) continue;
            int p = i, q = j;
            if (!Lex(v[p], v[q])) (p, q) = (q, p);       // canonical direction so neighbouring triangles compute bit-identical points
            pts[n++] = v[p] + (v[q] - v[p]) * (d[p] / (d[p] - d[q]));
        }
        pLo = pHi = default; tLo = tHi = 0;
        if (n != 2) return false;
        double t0 = Vec3.Dot(dir, pts[0]), t1 = Vec3.Dot(dir, pts[1]);
        if (t0 <= t1) { pLo = pts[0]; tLo = t0; pHi = pts[1]; tHi = t1; } else { pLo = pts[1]; tLo = t1; pHi = pts[0]; tHi = t0; }
        return true;
    }

    static bool TriTri(Vec3 a0, Vec3 a1, Vec3 a2, Vec3 b0, Vec3 b1, Vec3 b2, out Vec3 p, out Vec3 q)
    {
        p = q = default;
        var n1 = Vec3.Cross(a1 - a0, a2 - a0); var n2 = Vec3.Cross(b1 - b0, b2 - b0);
        double da0 = Vec3.Dot(n2, a0 - b0), da1 = Vec3.Dot(n2, a1 - b0), da2 = Vec3.Dot(n2, a2 - b0);
        if ((da0 >= 0) == (da1 >= 0) && (da1 >= 0) == (da2 >= 0)) return false;
        double db0 = Vec3.Dot(n1, b0 - a0), db1 = Vec3.Dot(n1, b1 - a0), db2 = Vec3.Dot(n1, b2 - a0);
        if ((db0 >= 0) == (db1 >= 0) && (db1 >= 0) == (db2 >= 0)) return false;
        var dir = Vec3.Cross(n1, n2);
        if (dir.Length <= 1e-14 * n1.Length * n2.Length) return false;
        if (!Crossings(a0, a1, a2, da0, da1, da2, dir, out var alo, out var talo, out var ahi, out var tahi)) return false;
        if (!Crossings(b0, b1, b2, db0, db1, db2, dir, out var blo, out var tblo, out var bhi, out var tbhi)) return false;
        var lo = talo >= tblo ? alo : blo; var hi = tahi <= tbhi ? ahi : bhi;
        double tl = Math.Max(talo, tblo), th = Math.Min(tahi, tbhi);
        if (!(th > tl)) return false;
        p = lo; q = hi;
        return true;
    }

    // ================================================================= retriangulation of cut triangles
    static List<(int, int, int)> Split(Pool pool, TriMesh m, int vertexOffset, Dictionary<int, List<(int, int)>> segs)
    {
        var res = new List<(int, int, int)>(m.TriCount + segs.Count * 4);
        for (int t = 0; t < m.TriCount; t++)
        {
            int a = m.T[3 * t] + vertexOffset, b = m.T[3 * t + 1] + vertexOffset, c = m.T[3 * t + 2] + vertexOffset;
            if (segs.TryGetValue(t, out var s)) res.AddRange(CutTriangle(pool.Pts, a, b, c, s));
            else res.Add((a, b, c));
        }
        return res;
    }

    static List<(int, int, int)> CutTriangle(List<Vec3> pts, int i0, int i1, int i2, List<(int, int)> segs)
    {
        var p0 = pts[i0]; var nrm = Vec3.Cross(pts[i1] - p0, pts[i2] - p0); double nl = nrm.Length;
        if (nl < 1e-30) return [(i0, i1, i2)];
        nrm /= nl; var u = (pts[i1] - p0).Normalized(); var w = Vec3.Cross(nrm, u);
        double scale = Math.Max((pts[i1] - p0).Length, Math.Max((pts[i2] - p0).Length, (pts[i2] - pts[i1]).Length));
        double eps = 1e-9 * scale;

        var ids = new List<int> { i0, i1, i2 };
        var xy = new List<(double x, double y)>();
        foreach (int id in ids) { var d = pts[id] - p0; xy.Add((Vec3.Dot(d, u), Vec3.Dot(d, w))); }
        var tris = new List<int[]> { new[] { 0, 1, 2 } };
        var local = new Dictionary<int, int> { [i0] = 0, [i1] = 1, [i2] = 2 };

        double Orient(int a, int b, int c) => (xy[b].x - xy[a].x) * (xy[c].y - xy[a].y) - (xy[b].y - xy[a].y) * (xy[c].x - xy[a].x);

        int Insert(int pid)
        {
            if (local.TryGetValue(pid, out int ex)) return ex;
            var d = pts[pid] - p0; var q = (x: Vec3.Dot(d, u), y: Vec3.Dot(d, w));
            int li = xy.Count; xy.Add(q); ids.Add(pid); local[pid] = li;
            int best = -1; double bestMin = double.MinValue; int bestEdge = 0; double bestEdgeD = 0;
            for (int k = 0; k < tris.Count; k++)
            {
                var tr = tris[k]; double mn = double.MaxValue; int me = 0;
                for (int e = 0; e < 3; e++)
                {
                    int a = tr[e], b = tr[(e + 1) % 3];
                    double len = Math.Sqrt((xy[b].x - xy[a].x) * (xy[b].x - xy[a].x) + (xy[b].y - xy[a].y) * (xy[b].y - xy[a].y));
                    double dist = len < 1e-300 ? 0 : ((xy[b].x - xy[a].x) * (q.y - xy[a].y) - (xy[b].y - xy[a].y) * (q.x - xy[a].x)) / len;
                    if (dist < mn) { mn = dist; me = e; }
                }
                if (mn > bestMin) { bestMin = mn; best = k; bestEdge = me; bestEdgeD = mn; }
            }
            var T = tris[best];
            if (bestEdgeD < eps)
            {
                int a = T[bestEdge], b = T[(bestEdge + 1) % 3], c = T[(bestEdge + 2) % 3];
                tris[best] = [a, li, c]; tris.Add([li, b, c]);
                // neighbour across the edge (it holds b->a)
                for (int k = 0; k < tris.Count - 1; k++)
                {
                    if (k == best) continue;
                    var n = tris[k];
                    for (int e = 0; e < 3; e++)
                        if (n[e] == b && n[(e + 1) % 3] == a)
                        {
                            int dd = n[(e + 2) % 3];
                            tris[k] = [b, li, dd]; tris.Add([li, a, dd]);
                            goto done;
                        }
                }
            done:;
            }
            else
            {
                int a = T[0], b = T[1], c = T[2];
                tris[best] = [a, b, li]; tris.Add([b, c, li]); tris.Add([c, a, li]);
            }
            return li;
        }

        var wanted = new List<(int, int)>();
        foreach (var (p, q) in segs) { int lp = Insert(p), lq = Insert(q); if (lp != lq) wanted.Add((lp, lq)); }

        bool HasEdge(int a, int b)
        {
            foreach (var t in tris) for (int e = 0; e < 3; e++) if ((t[e] == a && t[(e + 1) % 3] == b) || (t[e] == b && t[(e + 1) % 3] == a)) return true;
            return false;
        }

        foreach (var (lp, lq) in wanted)
        {
            for (int guard = 0; !HasEdge(lp, lq); guard++)
            {
                if (guard > 4000) throw new InvalidOperationException("constraint recovery failed");
                // edges that properly cross the segment lp-lq
                bool flipped = false;
                var seen = new HashSet<long>();
                foreach (var t in tris.ToArray())
                {
                    for (int e = 0; e < 3 && !flipped; e++)
                    {
                        int c = t[e], d = t[(e + 1) % 3];
                        if (c == lp || c == lq || d == lp || d == lq) continue;
                        if (!seen.Add(EdgeKey(c, d))) continue;
                        double o1 = Orient(lp, lq, c), o2 = Orient(lp, lq, d), o3 = Orient(c, d, lp), o4 = Orient(c, d, lq);
                        if (!(o1 * o2 < 0 && o3 * o4 < 0)) continue;
                        // triangles on either side of c-d
                        int k1 = -1, k2 = -1;
                        for (int k = 0; k < tris.Count; k++)
                            for (int ee = 0; ee < 3; ee++)
                            {
                                if (tris[k][ee] == c && tris[k][(ee + 1) % 3] == d) k1 = k;
                                if (tris[k][ee] == d && tris[k][(ee + 1) % 3] == c) k2 = k;
                            }
                        if (k1 < 0 || k2 < 0) throw new InvalidOperationException("cut crosses the triangle boundary");
                        int x = tris[k1].First(v => v != c && v != d), y = tris[k2].First(v => v != c && v != d);
                        // quad c,y,d,x must be strictly convex for the flip to be valid
                        if (Orient(x, c, y) <= 0 || Orient(y, d, x) <= 0) continue;
                        tris[k1] = [c, y, x]; tris[k2] = [y, d, x];
                        flipped = true;
                    }
                    if (flipped) break;
                }
                if (!flipped) throw new InvalidOperationException("constraint recovery stuck");
            }
        }
        return tris.Select(t => (ids[t[0]], ids[t[1]], ids[t[2]])).ToList();
    }

    // ================================================================= patches (connected through edges that are not cut)
    static (int[] patch, int count) Patches(List<(int, int, int)> tris, HashSet<long> cutEdges)
    {
        var parent = Enumerable.Range(0, tris.Count).ToArray();
        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
        var first = new Dictionary<long, int>();
        for (int t = 0; t < tris.Count; t++)
        {
            var (a, b, c) = tris[t];
            foreach (var (u, v) in new[] { (a, b), (b, c), (c, a) })
            {
                long k = EdgeKey(u, v);
                if (cutEdges.Contains(k)) continue;
                if (first.TryGetValue(k, out int o)) { int ra = Find(o), rb = Find(t); if (ra != rb) parent[ra] = rb; }
                else first[k] = t;
            }
        }
        var map = new Dictionary<int, int>(); var patch = new int[tris.Count];
        for (int t = 0; t < tris.Count; t++)
        {
            int r = Find(t);
            if (!map.TryGetValue(r, out int id)) map[r] = id = map.Count;
            patch[t] = id;
        }
        return (patch, map.Count);
    }

    // ================================================================= point in mesh (ray parity, majority over three directions)
    static readonly Vec3[] RayDirs = [new Vec3(0.5773, 0.3411, 0.7391).Normalized(), new Vec3(-0.2718, 0.8123, 0.4547).Normalized(), new Vec3(0.1414, -0.6180, 0.9269).Normalized()];

    public static bool Inside(Vec3 p, TriMesh m)
    {
        double step = 1e-9 * Math.Max(1, m.Size.Length);
        int votes = 0;
        foreach (var d in RayDirs)
        {
            int cnt = 0; double tMin = 1e-12;
            for (int guard = 0; guard < 100000; guard++)
            {
                var h = m.Bvh.Raycast(p, d, double.MaxValue, tMin);
                if (h == null) break;
                cnt++; tMin = h.Value.t + step;
            }
            if ((cnt & 1) == 1) votes++;
        }
        return votes >= 2;
    }
}
