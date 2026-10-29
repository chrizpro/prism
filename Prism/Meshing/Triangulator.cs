using System;
using System.Collections.Generic;
using System.Linq;

namespace Prism.Meshing;

/// <summary>Ear-clipping triangulation of a planar polygon, with optional holes (bridged into the outline). Works in 2D.</summary>
public static class Triangulator
{
    /// <summary>Returns triangles as index triples into the combined point list: outer points first, then every hole's points in order.</summary>
    public static List<(int a, int b, int c)> Polygon(IReadOnlyList<(double x, double y)> outer, IReadOnlyList<IReadOnlyList<(double x, double y)>>? holes = null)
    {
        var pts = new List<(double x, double y)>(outer);
        var ring = Enumerable.Range(0, outer.Count).ToList();
        if (Area(pts, ring) < 0) ring.Reverse();                        // outer CCW
        if (holes != null)
        {
            // bridge holes from the one with the rightmost vertex to the leftmost
            var hs = new List<List<int>>();
            foreach (var h in holes)
            {
                int start = pts.Count; pts.AddRange(h);
                var hr = Enumerable.Range(start, h.Count).ToList();
                if (Area(pts, hr) > 0) hr.Reverse();                      // holes CW
                hs.Add(hr);
            }
            foreach (var hr in hs.OrderByDescending(h => h.Max(i => pts[i].x)))
                ring = Bridge(pts, ring, hr);
        }
        return Clip(pts, ring);
    }

    static double Area(List<(double x, double y)> p, List<int> r)
    {
        double a = 0;
        for (int i = 0; i < r.Count; i++) { var u = p[r[i]]; var v = p[r[(i + 1) % r.Count]]; a += u.x * v.y - v.x * u.y; }
        return a / 2;
    }

    static List<int> Bridge(List<(double x, double y)> p, List<int> outer, List<int> hole)
    {
        // rightmost hole vertex
        int hi = 0; for (int i = 1; i < hole.Count; i++) if (p[hole[i]].x > p[hole[hi]].x) hi = i;
        var hp = p[hole[hi]];
        // connect to the nearest visible outer vertex (visibility = bridge crosses no outer edge)
        var order = Enumerable.Range(0, outer.Count).OrderBy(i => Dist2(p[outer[i]], hp)).ToList();
        int best = order[0];
        foreach (int oi in order)
        {
            bool ok = true;
            for (int k = 0; k < outer.Count && ok; k++)
            {
                int a = outer[k], b = outer[(k + 1) % outer.Count];
                if (a == outer[oi] || b == outer[oi]) continue;
                if (Intersect(p[outer[oi]], hp, p[a], p[b])) ok = false;
            }
            if (ok) { best = oi; break; }
        }
        var res = new List<int>();
        for (int i = 0; i <= best; i++) res.Add(outer[i]);
        for (int k = 0; k <= hole.Count; k++) res.Add(hole[(hi + k) % hole.Count]);
        res.Add(outer[best]);
        for (int i = best + 1; i < outer.Count; i++) res.Add(outer[i]);
        return res;
    }

    static double Dist2((double x, double y) a, (double x, double y) b) => (a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y);

    static bool Intersect((double x, double y) a, (double x, double y) b, (double x, double y) c, (double x, double y) d)
    {
        double Cr((double x, double y) o, (double x, double y) u, (double x, double y) v) => (u.x - o.x) * (v.y - o.y) - (u.y - o.y) * (v.x - o.x);
        double d1 = Cr(a, b, c), d2 = Cr(a, b, d), d3 = Cr(c, d, a), d4 = Cr(c, d, b);
        return d1 * d2 < -1e-12 && d3 * d4 < -1e-12;
    }

    static List<(int, int, int)> Clip(List<(double x, double y)> p, List<int> ring)
    {
        var res = new List<(int, int, int)>();
        var r = new List<int>(ring);
        int guard = r.Count * r.Count + 10;
        while (r.Count > 3 && guard-- > 0)
        {
            bool clipped = false;
            for (int i = 0; i < r.Count; i++)
            {
                int ia = r[(i + r.Count - 1) % r.Count], ib = r[i], ic = r[(i + 1) % r.Count];
                var a = p[ia]; var b = p[ib]; var c = p[ic];
                double cross = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
                if (cross <= 1e-14) continue;                              // reflex or degenerate
                bool ear = true;
                foreach (int j in r)
                {
                    if (j == ia || j == ib || j == ic) continue;
                    var q = p[j];
                    if (Same(q, a) || Same(q, b) || Same(q, c)) continue;  // bridge duplicates
                    if (InTri(q, a, b, c)) { ear = false; break; }
                }
                if (!ear) continue;
                res.Add((ia, ib, ic)); r.RemoveAt(i); clipped = true; break;
            }
            if (!clipped)
            {
                // numerically stuck: clip the most convex vertex anyway so we always terminate
                int bi = 0; double bc = double.MinValue;
                for (int i = 0; i < r.Count; i++)
                {
                    var a = p[r[(i + r.Count - 1) % r.Count]]; var b = p[r[i]]; var c = p[r[(i + 1) % r.Count]];
                    double cr = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
                    if (cr > bc) { bc = cr; bi = i; }
                }
                res.Add((r[(bi + r.Count - 1) % r.Count], r[bi], r[(bi + 1) % r.Count])); r.RemoveAt(bi);
            }
        }
        if (r.Count == 3) res.Add((r[0], r[1], r[2]));
        return res;
    }

    static bool Same((double x, double y) a, (double x, double y) b) => Math.Abs(a.x - b.x) < 1e-12 && Math.Abs(a.y - b.y) < 1e-12;

    static bool InTri((double x, double y) p, (double x, double y) a, (double x, double y) b, (double x, double y) c)
    {
        double s1 = (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
        double s2 = (c.x - b.x) * (p.y - b.y) - (c.y - b.y) * (p.x - b.x);
        double s3 = (a.x - c.x) * (p.y - c.y) - (a.y - c.y) * (p.x - c.x);
        return s1 >= -1e-12 && s2 >= -1e-12 && s3 >= -1e-12;
    }
}
