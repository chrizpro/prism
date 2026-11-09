using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Kernel;

namespace Prism.Core;

/// <summary>
/// The triangle surfaces of one or more objects welded into a single graph, with shortest paths over it (geodesics).
/// Triangles of different faces and objects join wherever their corners coincide, so a path can run over the edge
/// between two faces, or from one body onto another that touches it, but never across empty space.
/// A path is found over Steiner points on the triangle edges (A*), then every point is slid along its edge until the
/// path cannot get shorter, which is the straight line you would get by unfolding the triangles it crosses.
/// </summary>
public sealed class SurfaceGraph
{
    /// <summary>Extra nodes on every triangle edge. More is more accurate before straightening and slower to search.</summary>
    const int Steiner = 3;

    readonly Vec3[] _v;
    readonly int[] _tri;                 // 3 vertex ids per triangle
    readonly int[] _owner;               // part index per triangle
    readonly int[] _triEdge;             // 3 edge ids per triangle
    readonly int[][] _vertTris;
    readonly int[] _edgeA, _edgeB;
    readonly int[][] _edgeTris;

    public int TriangleCount => _owner.Length;

    /// <summary>Weld the parts (positions as x,y,z floats, indices three per triangle) into one graph.</summary>
    public SurfaceGraph(IReadOnlyList<(float[] Positions, int[] Indices)> parts)
    {
        double lo = double.MaxValue, hi = double.MinValue;
        foreach (var (pos, _) in parts)
            for (int i = 0; i < pos.Length; i++) { lo = Math.Min(lo, pos[i]); hi = Math.Max(hi, pos[i]); }
        double size = hi > lo ? hi - lo : 1;
        double cell = Math.Max(1e-5, size * 1e-6);

        var verts = new List<Vec3>();
        var weld = new Dictionary<(long, long, long), int>();
        var tris = new List<int>(); var owners = new List<int>();
        for (int part = 0; part < parts.Count; part++)
        {
            var (pos, idx) = parts[part];
            var map = new int[pos.Length / 3];
            for (int i = 0; i < map.Length; i++)
            {
                double x = pos[3 * i], y = pos[3 * i + 1], z = pos[3 * i + 2];
                var key = ((long)Math.Round(x / cell), (long)Math.Round(y / cell), (long)Math.Round(z / cell));
                if (!weld.TryGetValue(key, out int id)) { id = verts.Count; verts.Add(new Vec3(x, y, z)); weld[key] = id; }
                map[i] = id;
            }
            for (int t = 0; t + 2 < idx.Length; t += 3)
            {
                int a = map[idx[t]], b = map[idx[t + 1]], c = map[idx[t + 2]];
                if (a == b || b == c || a == c) continue;     // collapsed by the weld
                tris.Add(a); tris.Add(b); tris.Add(c); owners.Add(part);
            }
        }
        _v = verts.ToArray(); _tri = tris.ToArray(); _owner = owners.ToArray();

        var vt = new List<int>[_v.Length];
        var edgeIds = new Dictionary<long, int>();
        var ea = new List<int>(); var eb = new List<int>(); var et = new List<List<int>>();
        _triEdge = new int[_tri.Length];
        for (int t = 0; t < _owner.Length; t++)
        {
            for (int k = 0; k < 3; k++)
            {
                int a = _tri[3 * t + k], b = _tri[3 * t + (k + 1) % 3];
                (vt[a] ??= []).Add(t);
                long key = (long)Math.Min(a, b) * _v.Length + Math.Max(a, b);
                if (!edgeIds.TryGetValue(key, out int e)) { e = ea.Count; edgeIds[key] = e; ea.Add(Math.Min(a, b)); eb.Add(Math.Max(a, b)); et.Add([]); }
                et[e].Add(t); _triEdge[3 * t + k] = e;
            }
        }
        _vertTris = vt.Select(l => l?.ToArray() ?? []).ToArray();
        _edgeA = ea.ToArray(); _edgeB = eb.ToArray(); _edgeTris = et.Select(l => l.ToArray()).ToArray();
    }

    /// <summary>The triangle of <paramref name="part"/> (or of any part when negative) nearest the point, and the point moved onto it.</summary>
    public int FindTriangle(Vec3 p, int part, out Vec3 onSurface)
    {
        int best = -1; double bestD = double.MaxValue; onSurface = p;
        for (int t = 0; t < _owner.Length; t++)
        {
            if (part >= 0 && _owner[t] != part) continue;
            var q = ClosestOnTriangle(p, _v[_tri[3 * t]], _v[_tri[3 * t + 1]], _v[_tri[3 * t + 2]]);
            double d = (q - p).Length;
            if (d < bestD) { bestD = d; best = t; onSurface = q; }
        }
        return best;
    }

    Vec3 Pos(int n)
    {
        if (n < _v.Length) return _v[n];
        int s = n - _v.Length, e = s / Steiner, j = s % Steiner;
        return Vec3.Lerp(_v[_edgeA[e]], _v[_edgeB[e]], (j + 1.0) / (Steiner + 1));
    }
    int[] NodeTris(int n) => n < _v.Length ? _vertTris[n] : _edgeTris[(n - _v.Length) / Steiner];
    IEnumerable<int> TriNodes(int t)
    {
        for (int k = 0; k < 3; k++) yield return _tri[3 * t + k];
        for (int k = 0; k < 3; k++)
        {
            int baseId = _v.Length + _triEdge[3 * t + k] * Steiner;
            for (int j = 0; j < Steiner; j++) yield return baseId + j;
        }
    }

    /// <summary>
    /// The shortest path over the surface from one point to another, each given with the triangle it lies on.
    /// Null when the two are not joined by surface, or the search ran past <paramref name="maxNodes"/>.
    /// </summary>
    public List<Vec3>? Geodesic(Vec3 from, int fromTri, Vec3 to, int toTri, int maxNodes = 400_000)
    {
        if (fromTri < 0 || toTri < 0) return null;
        if (fromTri == toTri) return [from, to];

        var g = new Dictionary<int, double>(); var parent = new Dictionary<int, int>();
        var closed = new HashSet<int>(); var open = new PriorityQueue<int, double>();
        const int Goal = -1;
        foreach (int n in TriNodes(fromTri))
        {
            g[n] = (Pos(n) - from).Length; parent[n] = Goal;
            open.Enqueue(n, g[n] + (Pos(n) - to).Length);
        }
        double goalCost = double.MaxValue; int goalFrom = Goal;
        while (open.TryDequeue(out int cur, out double f))
        {
            if (f >= goalCost - 1e-12) break;               // nothing left can beat the best way found to the end point
            if (!closed.Add(cur)) continue;
            if (closed.Count > maxNodes) return null;
            var pc = Pos(cur); double gc = g[cur];
            var tris = NodeTris(cur);
            if (Array.IndexOf(tris, toTri) >= 0)
            {
                double total = gc + (pc - to).Length;
                if (total < goalCost) { goalCost = total; goalFrom = cur; }
            }
            foreach (int t in tris)
                foreach (int m in TriNodes(t))
                {
                    if (m == cur || closed.Contains(m)) continue;
                    var pm = Pos(m); double ng = gc + (pm - pc).Length;
                    if (g.TryGetValue(m, out double old) && ng >= old - 1e-12) continue;
                    g[m] = ng; parent[m] = cur;
                    open.Enqueue(m, ng + (pm - to).Length);
                }
        }
        if (goalFrom == Goal) return null;

        var chain = new List<int>();
        for (int n = goalFrom; n != Goal; n = parent[n]) chain.Add(n);
        chain.Reverse();
        return Straighten(from, to, chain);
    }

    /// <summary>Slide every point that sits on a triangle edge along it until the whole path is as short as it can be.</summary>
    List<Vec3> Straighten(Vec3 from, Vec3 to, List<int> chain)
    {
        var pts = new List<Vec3> { from }; var ends = new List<(Vec3 a, Vec3 b)?> { null };
        foreach (int n in chain)
        {
            pts.Add(Pos(n));
            ends.Add(n >= _v.Length ? (_v[_edgeA[(n - _v.Length) / Steiner]], _v[_edgeB[(n - _v.Length) / Steiner]]) : null);
        }
        pts.Add(to); ends.Add(null);

        for (int sweep = 0; sweep < 80; sweep++)
        {
            double moved = 0;
            for (int i = 1; i + 1 < pts.Count; i++)
            {
                if (ends[i] is not { } e) continue;
                Vec3 prev = pts[i - 1], next = pts[i + 1];
                double t0 = 0, t1 = 1;
                for (int k = 0; k < 40; k++)
                {
                    double m1 = t0 + (t1 - t0) / 3, m2 = t1 - (t1 - t0) / 3;
                    if (Cost(prev, next, e, m1) < Cost(prev, next, e, m2)) t1 = m2; else t0 = m1;
                }
                var q = Vec3.Lerp(e.a, e.b, (t0 + t1) / 2);
                moved = Math.Max(moved, (q - pts[i]).Length);
                pts[i] = q;
            }
            if (moved < 1e-9) break;
        }

        var result = new List<Vec3>();
        foreach (var p in pts) if (result.Count == 0 || (p - result[^1]).Length > 1e-9) result.Add(p);
        if (result.Count == 1) result.Add(result[0]);
        return result;
    }

    static double Cost(Vec3 prev, Vec3 next, (Vec3 a, Vec3 b) e, double t)
    {
        var p = Vec3.Lerp(e.a, e.b, t);
        return (p - prev).Length + (next - p).Length;
    }

    /// <summary>
    /// The triangle nearest a point, found by stepping from <paramref name="start"/> to whichever neighbouring triangle is closer
    /// until none is. Meant for points that move a little at a time, so it does not walk the whole mesh.
    /// </summary>
    public int NearestTriangle(Vec3 p, int start, out Vec3 onSurface)
    {
        int cur = start;
        onSurface = ClosestOnTriangle(p, _v[_tri[3 * cur]], _v[_tri[3 * cur + 1]], _v[_tri[3 * cur + 2]]);
        double best = (onSurface - p).Length;
        for (int step = 0; step < 256; step++)
        {
            int next = -1; var nextPoint = onSurface;
            for (int k = 0; k < 3; k++)
                foreach (int t in _vertTris[_tri[3 * cur + k]])
                {
                    if (t == cur) continue;
                    var q = ClosestOnTriangle(p, _v[_tri[3 * t]], _v[_tri[3 * t + 1]], _v[_tri[3 * t + 2]]);
                    double d = (q - p).Length;
                    if (d < best - 1e-12) { best = d; next = t; nextPoint = q; }
                }
            if (next < 0) break;
            cur = next; onSurface = nextPoint;
        }
        return cur;
    }

    /// <summary>
    /// A smooth curve over the surface through the ends of the given legs (each a path from <see cref="Geodesic"/>).
    /// Every leg is resampled evenly, then the points between the leg ends are faired (Taubin smoothing, which evens out
    /// the kinks without shrinking the curve) and put back on the surface after each pass. The leg ends stay exactly where they are.
    /// <paramref name="startTris"/> is the triangle each leg starts on. Returns the points and which of them are leg ends.
    /// </summary>
    public (List<Vec3> Points, List<bool> Pinned) SmoothPath(IReadOnlyList<IReadOnlyList<Vec3>> legs, IReadOnlyList<int> startTris, double spacing, int sweeps = 40)
    {
        var pts = new List<Vec3>(); var pinned = new List<bool>(); var tris = new List<int>();
        spacing = Math.Max(spacing, 1e-6);
        for (int li = 0; li < legs.Count; li++)
        {
            var leg = legs[li];
            double total = Length(leg);
            int n = Math.Max(2, (int)Math.Ceiling(total / spacing) + 1);
            int seg = 0; double segStart = 0;
            for (int k = 0; k < n; k++)
            {
                double s = total * k / (n - 1);
                while (seg + 2 < leg.Count && segStart + (leg[seg + 1] - leg[seg]).Length < s - 1e-12) { segStart += (leg[seg + 1] - leg[seg]).Length; seg++; }
                double segLen = (leg[seg + 1] - leg[seg]).Length;
                var p = segLen < 1e-12 ? leg[seg] : Vec3.Lerp(leg[seg], leg[seg + 1], Math.Clamp((s - segStart) / segLen, 0, 1));
                if (k == 0 && li > 0) continue;                                  // the previous leg already ended here
                bool end = k == 0 || k == n - 1;
                int tri = k == 0 ? startTris[li] : NearestTriangle(p, tris[^1], out _);
                if (end && k == n - 1) p = leg[^1];
                pts.Add(p); pinned.Add(end); tris.Add(tri);
            }
        }
        const double Lambda = 0.5, Mu = -0.53;
        for (int sweep = 0; sweep < sweeps; sweep++)
            foreach (double factor in new[] { Lambda, Mu })
            {
                var next = pts.ToArray();
                for (int i = 1; i + 1 < pts.Count; i++)
                {
                    if (pinned[i]) continue;
                    var laplacian = (pts[i - 1] + pts[i + 1]) * 0.5 - pts[i];
                    next[i] = pts[i] + laplacian * factor;
                }
                for (int i = 0; i < next.Length; i++)
                {
                    if (pinned[i]) continue;
                    tris[i] = NearestTriangle(next[i], tris[i], out var on);
                    next[i] = on;
                }
                for (int i = 0; i < next.Length; i++) pts[i] = next[i];
            }
        return (pts, pinned);
    }

    public static double Length(IReadOnlyList<Vec3> path)
    {
        double len = 0;
        for (int i = 1; i < path.Count; i++) len += (path[i] - path[i - 1]).Length;
        return len;
    }

    static Vec3 ClosestOnTriangle(Vec3 p, Vec3 a, Vec3 b, Vec3 c)
    {
        var ab = b - a; var ac = c - a; var ap = p - a;
        double d1 = Vec3.Dot(ab, ap), d2 = Vec3.Dot(ac, ap);
        if (d1 <= 0 && d2 <= 0) return a;
        var bp = p - b; double d3 = Vec3.Dot(ab, bp), d4 = Vec3.Dot(ac, bp);
        if (d3 >= 0 && d4 <= d3) return b;
        double vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0) return a + ab * (d1 / (d1 - d3));
        var cp = p - c; double d5 = Vec3.Dot(ab, cp), d6 = Vec3.Dot(ac, cp);
        if (d6 >= 0 && d5 <= d6) return c;
        double vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0) return a + ac * (d2 / (d2 - d6));
        double va = d3 * d6 - d5 * d4;
        if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0) return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
        double denom = 1 / (va + vb + vc);
        return a + ab * (vb * denom) + ac * (vc * denom);
    }
}
