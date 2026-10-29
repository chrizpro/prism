using System;
using System.Collections.Generic;
using System.Linq;

namespace Prism.Core.Sketching;

/// <summary>Finds the closed profiles of a sketch and fills them with triangles (so the user can see which shapes are closed).</summary>
public static class SketchFill
{
    public readonly record struct P2(double X, double Y);

    /// <summary>Join open curve pieces end to end into closed loops; curves that close on their own (circles, ellipses) are loops already.</summary>
    public static List<List<P2>> Loops(IEnumerable<List<P2>> curves, double tol)
    {
        var loops = new List<List<P2>>(); var open = new List<List<P2>>();
        foreach (var c in curves)
        {
            if (c.Count < 2) continue;
            if (Near(c[0], c[^1], tol) && c.Count > 3) loops.Add(c[..^1]); else open.Add(new List<P2>(c));
        }
        while (open.Count > 0)
        {
            var chain = open[^1]; open.RemoveAt(open.Count - 1);
            bool grew = true;
            while (grew && !Near(chain[0], chain[^1], tol))
            {
                grew = false;
                for (int i = 0; i < open.Count; i++)
                {
                    var o = open[i];
                    if (Near(chain[^1], o[0], tol)) { chain.AddRange(o.Skip(1)); }
                    else if (Near(chain[^1], o[^1], tol)) { var r = new List<P2>(o); r.Reverse(); chain.AddRange(r.Skip(1)); }
                    else if (Near(chain[0], o[^1], tol)) { chain.InsertRange(0, o.Take(o.Count - 1)); }
                    else if (Near(chain[0], o[0], tol)) { var r = new List<P2>(o); r.Reverse(); chain.InsertRange(0, r.Take(r.Count - 1)); }
                    else continue;
                    open.RemoveAt(i); grew = true; break;
                }
            }
            if (chain.Count >= 4 && Near(chain[0], chain[^1], tol)) loops.Add(chain[..^1]);
        }
        return loops;
    }

    static bool Near(P2 a, P2 b, double tol) => Math.Abs(a.X - b.X) <= tol && Math.Abs(a.Y - b.Y) <= tol;

    static double Area(List<P2> p) { double a = 0; for (int i = 0; i < p.Count; i++) { var q = p[(i + 1) % p.Count]; a += p[i].X * q.Y - q.X * p[i].Y; } return a / 2; }

    static bool Inside(List<P2> poly, P2 pt)
    {
        bool c = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            if ((poly[i].Y > pt.Y) != (poly[j].Y > pt.Y) && pt.X < (poly[j].X - poly[i].X) * (pt.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X) c = !c;
        return c;
    }

    /// <summary>Triangles that fill the loops: a loop inside another one is a hole.</summary>
    public static List<(P2 a, P2 b, P2 c)> Triangulate(List<List<P2>> loops)
    {
        var res = new List<(P2, P2, P2)>();
        loops = loops.Where(l => l.Count >= 3 && Math.Abs(Area(l)) > 1e-9).ToList();
        var depth = new int[loops.Count]; var parent = new int[loops.Count];
        for (int i = 0; i < loops.Count; i++)
        {
            parent[i] = -1; double best = double.MaxValue;
            for (int j = 0; j < loops.Count; j++)
            {
                if (i == j || !Inside(loops[j], loops[i][0])) continue;
                depth[i]++; double a = Math.Abs(Area(loops[j])); if (a < best) { best = a; parent[i] = j; }
            }
        }
        for (int i = 0; i < loops.Count; i++)
        {
            if (depth[i] % 2 != 0) continue;
            var outer = new List<P2>(loops[i]); if (Area(outer) < 0) outer.Reverse();
            var holes = Enumerable.Range(0, loops.Count).Where(j => parent[j] == i && depth[j] % 2 == 1).Select(j => { var h = new List<P2>(loops[j]); if (Area(h) > 0) h.Reverse(); return h; })
                .OrderByDescending(h => h.Max(p => p.X)).ToList();
            foreach (var h in holes) outer = Bridge(outer, h, holes);
            EarClip(outer, res);
        }
        return res;
    }

    static bool Cross(P2 a, P2 b, P2 c, P2 d)
    {
        double o(P2 p, P2 q, P2 r) => (q.X - p.X) * (r.Y - p.Y) - (q.Y - p.Y) * (r.X - p.X);
        double d1 = o(a, b, c), d2 = o(a, b, d), d3 = o(c, d, a), d4 = o(c, d, b);
        return d1 * d2 < -1e-12 && d3 * d4 < -1e-12;
    }

    static List<P2> Bridge(List<P2> outer, List<P2> hole, List<List<P2>> allHoles)
    {
        int hi = 0; for (int i = 1; i < hole.Count; i++) if (hole[i].X > hole[hi].X) hi = i;
        var m = hole[hi];
        foreach (var vi in Enumerable.Range(0, outer.Count).OrderBy(i => Math.Pow(outer[i].X - m.X, 2) + Math.Pow(outer[i].Y - m.Y, 2)))
        {
            var v = outer[vi]; bool ok = true;
            for (int k = 0; k < outer.Count && ok; k++) if (Cross(m, v, outer[k], outer[(k + 1) % outer.Count])) ok = false;
            foreach (var h in allHoles) for (int k = 0; k < h.Count && ok; k++) if (Cross(m, v, h[k], h[(k + 1) % h.Count])) ok = false;
            if (!ok) continue;
            var r = new List<P2>(outer.Take(vi + 1));
            for (int k = 0; k <= hole.Count; k++) r.Add(hole[(hi + k) % hole.Count]);
            r.Add(v); r.AddRange(outer.Skip(vi + 1));
            return r;
        }
        return outer;
    }

    static void EarClip(List<P2> poly, List<(P2, P2, P2)> res)
    {
        var idx = Enumerable.Range(0, poly.Count).ToList(); int guard = poly.Count * poly.Count + 10;
        while (idx.Count > 3 && guard-- > 0)
        {
            bool cut = false;
            for (int k = 0; k < idx.Count; k++)
            {
                var a = poly[idx[(k + idx.Count - 1) % idx.Count]]; var b = poly[idx[k]]; var c = poly[idx[(k + 1) % idx.Count]];
                if ((b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X) <= 1e-12) continue;
                bool empty = true;
                foreach (var j in idx)
                {
                    var p = poly[j]; if (p == a || p == b || p == c) continue;
                    double d1 = (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X), d2 = (c.X - b.X) * (p.Y - b.Y) - (c.Y - b.Y) * (p.X - b.X), d3 = (a.X - c.X) * (p.Y - c.Y) - (a.Y - c.Y) * (p.X - c.X);
                    if (d1 >= -1e-12 && d2 >= -1e-12 && d3 >= -1e-12) { empty = false; break; }
                }
                if (!empty) continue;
                res.Add((a, b, c)); idx.RemoveAt(k); cut = true; break;
            }
            if (!cut) { idx.RemoveAt(0); }
        }
        if (idx.Count == 3) res.Add((poly[idx[0]], poly[idx[1]], poly[idx[2]]));
    }
}
