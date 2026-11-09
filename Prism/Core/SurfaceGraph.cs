using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Kernel;

namespace Prism.Core;

/// <summary>
/// The triangle surfaces of one or more objects welded into one mesh, so a curve can be laid on them. Triangles of
/// different faces and objects join wherever their corners coincide, which lets a curve run over the edge between two
/// faces, or from one body onto another that touches it, but never across empty space.
/// </summary>
public sealed class SurfaceGraph
{
    readonly Vec3[] _v;
    readonly int[] _tri;                 // 3 vertex ids per triangle
    readonly int[] _owner;               // part index per triangle
    readonly int[][] _vertTris;

    public int TriangleCount => _owner.Length;

    /// <summary>Weld the parts (positions as x,y,z floats, indices three per triangle) into one mesh.</summary>
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
        for (int t = 0; t < _owner.Length; t++)
            for (int k = 0; k < 3; k++) (vt[_tri[3 * t + k]] ??= []).Add(t);
        _vertTris = vt.Select(l => l?.ToArray() ?? []).ToArray();
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
    /// Lay a smooth curve through the picked points on the surface. A smooth curve through the points (centripetal Catmull-Rom,
    /// so it neither overshoots nor loops) is sampled <paramref name="perSegment"/> times between each pair, and every sample is moved
    /// onto the surface. Null when the curve cannot be laid on connected surface, such as a segment that would cross empty space.
    /// <paramref name="startTris"/> gives the triangle each pick lies on. Returns the points and which of them are the picks.
    /// </summary>
    public (List<Vec3> Points, List<bool> Pinned)? Trace(IReadOnlyList<Vec3> picks, IReadOnlyList<int> startTris, int perSegment = 24)
    {
        if (picks.Count < 2) return null;
        var pts = new List<Vec3> { picks[0] }; var pinned = new List<bool> { true };
        for (int i = 0; i + 1 < picks.Count; i++)
        {
            var p0 = i > 0 ? picks[i - 1] : picks[i] * 2 - picks[i + 1];
            var p3 = i + 2 < picks.Count ? picks[i + 2] : picks[i + 1] * 2 - picks[i];
            int tri = startTris[i];
            double tol = Math.Max(0.01 * (picks[i + 1] - picks[i]).Length, 1e-4);
            for (int k = 1; k <= perSegment; k++)
            {
                if (k == perSegment)
                {
                    // the segment must really arrive at the next pick over the surface, or something is in the way
                    NearestTriangle(picks[i + 1], tri, out var arrived);
                    if ((arrived - picks[i + 1]).Length > tol) return null;
                    pts.Add(picks[i + 1]); pinned.Add(true);
                    break;
                }
                var q = CatmullRom(p0, picks[i], picks[i + 1], p3, (double)k / perSegment);
                tri = NearestTriangle(q, tri, out var on);
                pts.Add(on); pinned.Add(false);
            }
        }
        return (pts, pinned);
    }

    static Vec3 CatmullRom(Vec3 p0, Vec3 p1, Vec3 p2, Vec3 p3, double u)
    {
        static double Knot(Vec3 a, Vec3 b) => Math.Max(Math.Sqrt((b - a).Length), 1e-6);
        double t0 = 0, t1 = t0 + Knot(p0, p1), t2 = t1 + Knot(p1, p2), t3 = t2 + Knot(p2, p3);
        double t = t1 + (t2 - t1) * u;
        var a1 = p0 * ((t1 - t) / (t1 - t0)) + p1 * ((t - t0) / (t1 - t0));
        var a2 = p1 * ((t2 - t) / (t2 - t1)) + p2 * ((t - t1) / (t2 - t1));
        var a3 = p2 * ((t3 - t) / (t3 - t2)) + p3 * ((t - t2) / (t3 - t2));
        var b1 = a1 * ((t2 - t) / (t2 - t0)) + a2 * ((t - t0) / (t2 - t0));
        var b2 = a2 * ((t3 - t) / (t3 - t1)) + a3 * ((t - t1) / (t3 - t1));
        return b1 * ((t2 - t) / (t2 - t1)) + b2 * ((t - t1) / (t2 - t1));
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
