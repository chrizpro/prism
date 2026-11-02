using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Kernel;

namespace Prism.Meshing;

/// <summary>
/// Isotropic remeshing (Botsch and Kobbelt): repeatedly split long edges, collapse short ones, flip edges to even out vertex valence and
/// smooth vertices tangentially, projecting them back onto the original surface so the shape (and volume) are kept.
/// Boundary edges and sharp creases can be pinned so they survive the process.
/// </summary>
public static class MeshRemesh
{
    public sealed record Options(double TargetEdge, int Iterations = 5, bool KeepSharp = true, double SharpAngleDeg = 35, bool KeepBoundary = true);

    /// <summary>Average edge length of a mesh (each undirected edge counted once).</summary>
    public static double MeanEdge(TriMesh m)
    {
        var seen = new HashSet<long>(); double sum = 0; int n = 0;
        for (int t = 0; t < m.TriCount; t++)
            for (int k = 0; k < 3; k++)
            {
                int a = m.T[3 * t + k], b = m.T[3 * t + (k + 1) % 3];
                if (seen.Add(TriMesh.EdgeKey(a, b, Math.Max(1, m.VertexCount)))) { sum += (m.P(a) - m.P(b)).Length; n++; }
            }
        return n == 0 ? 0 : sum / n;
    }

    /// <summary>Smallest triangle corner angle in degrees (a quality measure: higher is better, 60 is perfect).</summary>
    public static double MinAngleDeg(TriMesh m)
    {
        double best = 180;
        for (int t = 0; t < m.TriCount; t++) best = Math.Min(best, MinAngle(m.P(m.T[3 * t]), m.P(m.T[3 * t + 1]), m.P(m.T[3 * t + 2])));
        return m.TriCount == 0 ? 0 : best * 180 / Math.PI;
    }

    /// <summary>Fraction of edges whose length is within the given ratio band around the target (a uniformity measure).</summary>
    public static double EdgeUniformity(TriMesh m, double target, double lo = 0.6, double hi = 1.5)
    {
        var seen = new HashSet<long>(); int ok = 0, n = 0;
        for (int t = 0; t < m.TriCount; t++)
            for (int k = 0; k < 3; k++)
            {
                int a = m.T[3 * t + k], b = m.T[3 * t + (k + 1) % 3];
                if (!seen.Add(TriMesh.EdgeKey(a, b, Math.Max(1, m.VertexCount)))) continue;
                double l = (m.P(a) - m.P(b)).Length / target; n++;
                if (l >= lo && l <= hi) ok++;
            }
        return n == 0 ? 0 : (double)ok / n;
    }

    static double MinAngle(Vec3 a, Vec3 b, Vec3 c)
    {
        double Ang(Vec3 p, Vec3 q, Vec3 r)
        {
            var u = q - p; var v = r - p; double l = u.Length * v.Length;
            return l < 1e-30 ? 0 : Math.Acos(Math.Clamp(Vec3.Dot(u, v) / l, -1, 1));
        }
        return Math.Min(Ang(a, b, c), Math.Min(Ang(b, c, a), Ang(c, a, b)));
    }

    public static TriMesh Remesh(TriMesh input, Options opt)
    {
        if (input.TriCount == 0 || opt.TargetEdge <= 0) return input;
        var work = new Work(input, opt);
        work.Run();
        return work.ToMesh();
    }

    // ================================================================= working mesh
    sealed class Work
    {
        readonly Options _o;
        readonly TriMesh _orig;
        readonly Bvh _bvh;
        readonly double _low, _high, _len;
        readonly double _cosSharp;
        readonly List<Vec3> _p = [];
        readonly List<int> _tv = [];                            // 3 vertex ids per triangle, -1 = dead
        readonly List<HashSet<int>> _vt = [];                   // vertex -> triangles
        readonly List<HashSet<int>> _vn = [];                   // vertex -> neighbour vertices
        readonly Dictionary<long, List<int>> _edges = [];       // edge -> triangles
        readonly HashSet<long> _fixedEdges = [];                // boundary / crease / non-manifold edges that must survive
        int _liveTris;
        // 0 free, 1 slides along a chain of fixed edges, 2 corner (immovable)
        int[] _cls = [];

        public Work(TriMesh m, Options o)
        {
            _o = o; _orig = m; _bvh = m.Bvh; _len = o.TargetEdge; _low = 4.0 / 5.0 * _len; _high = 4.0 / 3.0 * _len;
            _cosSharp = Math.Cos(o.SharpAngleDeg * Math.PI / 180);
            for (int i = 0; i < m.VertexCount; i++) NewVertex(m.P(i));
            for (int t = 0; t < m.TriCount; t++) AddTri(m.T[3 * t], m.T[3 * t + 1], m.T[3 * t + 2]);
            // classify the fixed edges
            foreach (var (key, tris) in _edges)
            {
                if (tris.Count == 1) { if (o.KeepBoundary) _fixedEdges.Add(key); }
                else if (tris.Count > 2) _fixedEdges.Add(key);
                else if (o.KeepSharp && Vec3.Dot(Normal(tris[0]).Normalized(), Normal(tris[1]).Normalized()) < _cosSharp) _fixedEdges.Add(key);
            }
        }

        static long Key(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
        static int KA(long k) => (int)(k >> 32);
        static int KB(long k) => (int)(k & 0xFFFFFFFF);

        int NewVertex(Vec3 p) { _p.Add(p); _vt.Add([]); _vn.Add([]); return _p.Count - 1; }

        int AddTri(int a, int b, int c)
        {
            int t = _tv.Count / 3; _tv.Add(a); _tv.Add(b); _tv.Add(c); _liveTris++;
            _vt[a].Add(t); _vt[b].Add(t); _vt[c].Add(t);
            AddEdge(a, b, t); AddEdge(b, c, t); AddEdge(c, a, t);
            return t;
        }

        void AddEdge(int a, int b, int t)
        {
            long k = Key(a, b);
            if (!_edges.TryGetValue(k, out var l)) { _edges[k] = l = []; _vn[a].Add(b); _vn[b].Add(a); }
            l.Add(t);
        }

        void RemoveTri(int t)
        {
            int a = _tv[3 * t], b = _tv[3 * t + 1], c = _tv[3 * t + 2];
            RemoveEdge(a, b, t); RemoveEdge(b, c, t); RemoveEdge(c, a, t);
            _vt[a].Remove(t); _vt[b].Remove(t); _vt[c].Remove(t);
            _tv[3 * t] = -1; _liveTris--;
        }

        void RemoveEdge(int a, int b, int t)
        {
            long k = Key(a, b);
            var l = _edges[k]; l.Remove(t);
            if (l.Count == 0) { _edges.Remove(k); _vn[a].Remove(b); _vn[b].Remove(a); }
        }

        Vec3 Normal(int t) => Vec3.Cross(_p[_tv[3 * t + 1]] - _p[_tv[3 * t]], _p[_tv[3 * t + 2]] - _p[_tv[3 * t]]);
        int Other(int t, int a, int b) { for (int k = 0; k < 3; k++) { int v = _tv[3 * t + k]; if (v != a && v != b) return v; } return -1; }
        bool HasDirected(int t, int a, int b) { for (int k = 0; k < 3; k++) if (_tv[3 * t + k] == a && _tv[3 * t + (k + 1) % 3] == b) return true; return false; }
        static double SegDist(Vec3 p, Vec3 a, Vec3 b)
        {
            var ab = b - a; double l2 = Vec3.Dot(ab, ab);
            double t = l2 < 1e-30 ? 0 : Math.Clamp(Vec3.Dot(p - a, ab) / l2, 0, 1);
            return (a + ab * t - p).Length;
        }
        double Len(int a, int b) => (_p[a] - _p[b]).Length;
        bool IsBoundaryVertex(int v) { foreach (int x in _vn[v]) if (_edges[Key(v, x)].Count == 1) return true; return false; }

        // ------------------------------------------------------------- driver
        public void Run()
        {
            for (int it = 0; it < Math.Max(1, _o.Iterations); it++)
            {
                for (int pass = 0; pass < 10; pass++) { if (SplitLong() == 0) break; }
                Classify(); Collapse();
                for (int r = 0; r < 2; r++) Flip();
                for (int r = 0; r < 3; r++) { Classify(); Smooth(); }
            }
            Classify(); Smooth();
        }

        public TriMesh ToMesh()
        {
            var tris = new List<int>(); int cnt = _p.Count;
            for (int t = 0; t < _tv.Count / 3; t++) if (_tv[3 * t] >= 0) { tris.Add(_tv[3 * t]); tris.Add(_tv[3 * t + 1]); tris.Add(_tv[3 * t + 2]); }
            var v = new float[cnt * 3];
            for (int i = 0; i < cnt; i++) { v[3 * i] = (float)_p[i].X; v[3 * i + 1] = (float)_p[i].Y; v[3 * i + 2] = (float)_p[i].Z; }
            return MeshOps.Compact(v, [.. tris]);
        }

        void Classify()
        {
            _cls = new int[_p.Count];
            var count = new int[_p.Count]; var nb = new List<int>[_p.Count];
            foreach (long k in _fixedEdges)
            {
                int a = KA(k), b = KB(k);
                count[a]++; count[b]++;
                (nb[a] ??= []).Add(b); (nb[b] ??= []).Add(a);
            }
            for (int v = 0; v < _p.Count; v++)
            {
                if (count[v] == 0) continue;
                if (count[v] != 2) { _cls[v] = 2; continue; }
                var u = (_p[nb[v]![0]] - _p[v]).Normalized(); var w = (_p[nb[v][1]] - _p[v]).Normalized();
                // straight through = cos(angle between the two edges) close to -1
                _cls[v] = Vec3.Dot(u, w) > -Math.Cos(_o.SharpAngleDeg * Math.PI / 180) ? 2 : 1;
            }
        }

        // ------------------------------------------------------------- split
        int SplitLong()
        {
            var cand = _edges.Keys.Where(k => Len(KA(k), KB(k)) > _high).OrderByDescending(k => Len(KA(k), KB(k))).ToList();
            int n = 0;
            foreach (long k in cand)
            {
                if (!_edges.TryGetValue(k, out var l) || l.Count > 2) continue;
                int a = KA(k), b = KB(k);
                if (Len(a, b) <= _high) continue;
                Split(a, b); n++;
            }
            return n;
        }

        void Split(int a, int b)
        {
            long k = Key(a, b);
            var tris = _edges[k].ToArray();
            int m = NewVertex((_p[a] + _p[b]) * 0.5);
            foreach (int t in tris)
            {
                int c = Other(t, a, b);
                bool fwd = HasDirected(t, a, b);
                RemoveTri(t);
                if (fwd) { AddTri(a, m, c); AddTri(m, b, c); } else { AddTri(b, m, c); AddTri(m, a, c); }
            }
            if (_fixedEdges.Remove(k)) { _fixedEdges.Add(Key(a, m)); _fixedEdges.Add(Key(m, b)); }
        }

        // ------------------------------------------------------------- collapse
        void Collapse()
        {
            var cand = _edges.Keys.Where(k => Len(KA(k), KB(k)) < _low).OrderBy(k => Len(KA(k), KB(k))).ToList();
            foreach (long k in cand)
            {
                if (_liveTris <= 4) break;
                if (!_edges.TryGetValue(k, out var l) || l.Count > 2) continue;
                int a = KA(k), b = KB(k);
                if (Len(a, b) >= _low) continue;
                if (!TryCollapse(a, b)) TryCollapse(b, a);
            }
        }

        /// <summary>Remove vertex a by merging it into b.</summary>
        bool TryCollapse(int a, int b)
        {
            long k = Key(a, b);
            if (!_edges.TryGetValue(k, out var onEdge) || onEdge.Count > 2) return false;
            bool fixedEdge = _fixedEdges.Contains(k);
            // topology (link condition)
            var opposite = new HashSet<int>(); foreach (int t in onEdge) opposite.Add(Other(t, a, b));
            int common = 0; foreach (int x in _vn[a]) if (_vn[b].Contains(x)) { if (!opposite.Contains(x)) return false; common++; }
            if (common != opposite.Count) return false;
            // constraints: chain / boundary vertices may only merge along their chain, and never across a corner
            bool aBoundary = IsBoundaryVertex(a), bBoundary = IsBoundaryVertex(b);
            bool aNon = _cls[a] != 0 || aBoundary, bNon = _cls[b] != 0 || bBoundary;
            if (aNon)
            {
                if (_cls[a] == 2) return false;
                if (!(fixedEdge || onEdge.Count == 1)) return false;
                int other = -1, chain = 0;
                foreach (int x in _vn[a])
                    if (x != b && (_fixedEdges.Contains(Key(a, x)) || _edges[Key(a, x)].Count == 1)) { other = x; chain++; }
                if (chain != 1) return false;
                if (SegDist(_p[a], _p[b], _p[other]) > 0.1 * _len) return false;
            }
            Vec3 np = (aNon || bNon) ? _p[b] : (_p[a] + _p[b]) * 0.5;
            // geometry: nothing may flip or grow too long
            var around = new HashSet<int>(_vt[a]); around.UnionWith(_vt[b]);
            foreach (int t in around)
            {
                bool hasA = false, hasB = false;
                for (int q = 0; q < 3; q++) { int v = _tv[3 * t + q]; if (v == a) hasA = true; if (v == b) hasB = true; }
                if (hasA && hasB) continue;
                Vec3 P(int i) => i == a || i == b ? np : _p[i];
                var n0 = Normal(t);
                var n1 = Vec3.Cross(P(_tv[3 * t + 1]) - P(_tv[3 * t]), P(_tv[3 * t + 2]) - P(_tv[3 * t]));
                double l0 = n0.Length, l1 = n1.Length;
                if (l1 < 1e-12 || l0 < 1e-18) { if (l1 < 1e-12) return false; continue; }
                if (Vec3.Dot(n0, n1) / (l0 * l1) < 0.4) return false;
                for (int q = 0; q < 3; q++)
                {
                    int v0 = _tv[3 * t + q], v1 = _tv[3 * t + (q + 1) % 3];
                    if ((v0 == a || v0 == b || v1 == a || v1 == b) && (P(v0) - P(v1)).Length > _high) return false;
                }
            }
            // do it
            foreach (int t in onEdge.ToArray()) RemoveTri(t);
            foreach (int t in _vt[a].ToArray())
            {
                int x = _tv[3 * t], y = _tv[3 * t + 1], z = _tv[3 * t + 2];
                RemoveTri(t);
                AddTri(x == a ? b : x, y == a ? b : y, z == a ? b : z);
            }
            foreach (long fk in _fixedEdges.Where(e => KA(e) == a || KB(e) == a).ToArray())
            {
                _fixedEdges.Remove(fk);
                int x = KA(fk) == a ? KB(fk) : KA(fk);
                if (x != b) _fixedEdges.Add(Key(b, x));
            }
            _p[b] = np;
            return true;
        }

        // ------------------------------------------------------------- flip
        void Flip()
        {
            foreach (long k in _edges.Keys.ToList())
            {
                if (!_edges.TryGetValue(k, out var l) || l.Count != 2 || _fixedEdges.Contains(k)) continue;
                int a = KA(k), b = KB(k);
                int t1 = l[0], t2 = l[1];
                if (!HasDirected(t1, a, b)) (a, b) = (b, a);
                int c = Other(t1, a, b), d = Other(t2, a, b);
                if (c == d || _vn[c].Contains(d)) continue;
                int Target(int v) => IsBoundaryVertex(v) ? 4 : 6;
                int Dev(int v, int delta) { int x = _vn[v].Count + delta - Target(v); return x * x; }
                int before = Dev(a, 0) + Dev(b, 0) + Dev(c, 0) + Dev(d, 0);
                int after = Dev(a, -1) + Dev(b, -1) + Dev(c, 1) + Dev(d, 1);
                if (after >= before) continue;
                // new triangles (a,d,c) and (d,b,c): must keep the surface orientation and not be slivers
                Vec3 pa = _p[a], pb = _p[b], pc = _p[c], pd = _p[d];
                var n1 = Vec3.Cross(pd - pa, pc - pa); var n2 = Vec3.Cross(pb - pd, pc - pd);
                var no = Normal(t1) + Normal(t2);
                if (n1.Length < 1e-12 || n2.Length < 1e-12 || no.Length < 1e-18) continue;
                double nl = no.Length;
                if (Vec3.Dot(n1, no) / (n1.Length * nl) < 0.5 || Vec3.Dot(n2, no) / (n2.Length * nl) < 0.5) continue;
                double oldMin = Math.Min(MinAngle(pa, pb, pc), MinAngle(pb, pa, pd)), newMin = Math.Min(MinAngle(pa, pd, pc), MinAngle(pd, pb, pc));
                if (newMin < 0.6 * oldMin) continue;
                RemoveTri(t1); RemoveTri(t2);
                AddTri(a, d, c); AddTri(d, b, c);
            }
        }

        // ------------------------------------------------------------- smooth + project
        Vec3 VertexNormal(int v)
        {
            Vec3 s = Vec3.Zero;
            foreach (int t in _vt[v]) s += Normal(t);
            return s.Length < 1e-18 ? Vec3.Zero : s.Normalized();
        }

        bool Valid(int v, Vec3 np)
        {
            var old = _p[v];
            foreach (int t in _vt[v])
            {
                var n0 = Normal(t);
                _p[v] = np; var n1 = Normal(t); _p[v] = old;
                double l0 = n0.Length, l1 = n1.Length;
                if (l1 < 1e-14) return false;
                if (l0 > 1e-18 && Vec3.Dot(n0, n1) / (l0 * l1) < 0.2) return false;
            }
            return true;
        }

        void Smooth()
        {
            for (int v = 0; v < _p.Count; v++)
            {
                if (_vt[v].Count == 0 || _cls[v] == 2) continue;
                Vec3 target;
                bool project = true;
                if (_cls[v] == 1)
                {
                    var ends = _vn[v].Where(x => _fixedEdges.Contains(Key(v, x))).ToList();
                    if (ends.Count != 2) continue;
                    var mid = (_p[ends[0]] + _p[ends[1]]) * 0.5;
                    // only slide along (nearly) straight chains, otherwise the curve would shrink
                    if (SegDist(_p[v], _p[ends[0]], _p[ends[1]]) > 0.05 * _len) continue;
                    target = _p[v] + (mid - _p[v]) * 0.5; project = false;
                }
                else if (IsBoundaryVertex(v))
                {
                    var ends = _vn[v].Where(x => _edges[Key(v, x)].Count == 1).ToList();
                    if (ends.Count != 2) continue;
                    target = _p[v] + ((_p[ends[0]] + _p[ends[1]]) * 0.5 - _p[v]) * 0.5;
                }
                else
                {
                    var n = VertexNormal(v); if (n.Length < 0.5) continue;
                    Vec3 c = Vec3.Zero; foreach (int x in _vn[v]) c += _p[x];
                    c /= _vn[v].Count;
                    var d = c - _p[v]; d -= n * Vec3.Dot(d, n);
                    target = _p[v] + d * 0.8;
                }
                if (project && _bvh.ClosestPoint(target) is { } cl) target = cl.Point;
                if (Valid(v, target)) _p[v] = target;
            }
        }
    }
}
