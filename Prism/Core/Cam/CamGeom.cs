using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Kernel;

namespace Prism.Core.Cam;

/// <summary>Planar geometry helpers for machining: outlines as polylines, offsets, regions.</summary>
public static class CamGeom
{
    /// <summary>The edges of a shape as closed (or open) polylines, joined end to end.</summary>
    public static List<(List<Vec3> pts, bool closed)> Loops(Shape s, double deflection = 0.02)
    {
        var m = s.Tessellate(deflection);
        var chains = new List<List<Vec3>>();
        for (int e = 0; e + 1 < m.EdgeSegStart.Length; e++)
        {
            var pts = new List<Vec3>();
            for (int sg = m.EdgeSegStart[e]; sg < m.EdgeSegStart[e + 1]; sg++)
            {
                var a = new Vec3(m.EdgePoints[6 * sg], m.EdgePoints[6 * sg + 1], m.EdgePoints[6 * sg + 2]); var b = new Vec3(m.EdgePoints[6 * sg + 3], m.EdgePoints[6 * sg + 4], m.EdgePoints[6 * sg + 5]);
                if (pts.Count == 0 || pts[^1].DistanceTo(a) > 1e-6) pts.Add(a);
                pts.Add(b);
            }
            if (pts.Count > 1) chains.Add(pts);
        }
        var res = new List<(List<Vec3>, bool)>();
        const double tol = 1e-3;
        while (chains.Count > 0)
        {
            var cur = chains[0]; chains.RemoveAt(0);
            bool grew = true;
            while (grew && cur[0].DistanceTo(cur[^1]) > tol)
            {
                grew = false;
                for (int i = 0; i < chains.Count; i++)
                {
                    var c = chains[i];
                    if (cur[^1].DistanceTo(c[0]) < tol) { cur.AddRange(c.Skip(1)); chains.RemoveAt(i); grew = true; break; }
                    if (cur[^1].DistanceTo(c[^1]) < tol) { c.Reverse(); cur.AddRange(c.Skip(1)); chains.RemoveAt(i); grew = true; break; }
                    if (cur[0].DistanceTo(c[^1]) < tol) { c.AddRange(cur.Skip(1)); cur = c; chains.RemoveAt(i); grew = true; break; }
                    if (cur[0].DistanceTo(c[0]) < tol) { c.Reverse(); c.AddRange(cur.Skip(1)); cur = c; chains.RemoveAt(i); grew = true; break; }
                }
            }
            bool closed = cur.Count > 2 && cur[0].DistanceTo(cur[^1]) < tol;
            if (closed) cur.RemoveAt(cur.Count - 1);
            res.Add((cur, closed));
        }
        return res;
    }

    public static double SignedArea(IReadOnlyList<Vec3> p)
    {
        double a = 0; for (int i = 0; i < p.Count; i++) { var q = p[(i + 1) % p.Count]; a += p[i].X * q.Y - q.X * p[i].Y; }
        return a / 2;
    }

    public static double Length(IReadOnlyList<Vec3> p, bool closed)
    {
        double l = 0; for (int i = 0; i + 1 < p.Count; i++) l += p[i].DistanceTo(p[i + 1]);
        if (closed && p.Count > 1) l += p[^1].DistanceTo(p[0]);
        return l;
    }

    /// <summary>Drop points of a closed loop that lie within tol of the line between their neighbours (Douglas-Peucker).</summary>
    public static List<Vec3> SimplifyClosed(List<Vec3> loop, double tol)
    {
        if (loop.Count < 8 || tol <= 0) return loop;
        int a = 0, b = 0; double best = -1;
        for (int i = 0; i < loop.Count; i += Math.Max(1, loop.Count / 100)) for (int j = i + 1; j < loop.Count; j += Math.Max(1, loop.Count / 100)) { double d = loop[i].DistanceTo(loop[j]); if (d > best) { best = d; a = i; b = j; } }
        var keep = new bool[loop.Count]; keep[a] = keep[b] = true;
        void Rec(int i, int j)
        {
            int n = (j - i + loop.Count) % loop.Count; if (n < 2) return;
            double md = -1; int mi = -1; var p0 = loop[i]; var p1 = loop[j]; var d01 = p1 - p0; double l2 = Math.Max(d01.X * d01.X + d01.Y * d01.Y, 1e-18);
            for (int k = 1; k < n; k++)
            {
                int idx = (i + k) % loop.Count; var p = loop[idx];
                double t = Math.Clamp(((p.X - p0.X) * d01.X + (p.Y - p0.Y) * d01.Y) / l2, 0, 1);
                double dx = p.X - (p0.X + d01.X * t), dy = p.Y - (p0.Y + d01.Y * t), d = Math.Sqrt(dx * dx + dy * dy);
                if (d > md) { md = d; mi = idx; }
            }
            if (md > tol) { keep[mi] = true; Rec(i, mi); Rec(mi, j); }
        }
        Rec(a, b); Rec(b, a);
        return loop.Where((_, i) => keep[i]).ToList();
    }

    /// <summary>A planar face from closed loops (nesting decided by the kernel, so holes and islands work).</summary>
    public static Shape? FaceFromLoops(IEnumerable<List<Vec3>> loops)
    {
        var wires = loops.Where(l => l.Count >= 3).Select(l => Occ.Polyline(l, true)).ToList();
        if (wires.Count == 0) return null;
        try { return Occ.Profile(wires, true); } catch (KernelException) { return null; }
    }

    /// <summary>Offset a region inside its plane. Returns the resulting closed loops (empty when it has shrunk away).</summary>
    public static List<List<Vec3>> Offset(Shape face, double dist, double z, bool sharp = false)
    {
        var res = new List<List<Vec3>>();
        int nf = face.Count(SubKind.Face);
        for (int f = 0; f < Math.Max(nf, 1); f++)
        {
            Shape one = nf == 0 ? face : face.Sub(SubKind.Face, f);
            try
            {
                var off = Occ.OffsetPlanar(one, dist, sharp);
                foreach (var (pts, closed) in Loops(off)) if (closed && pts.Count >= 3) res.Add(pts.Select(p => new Vec3(p.X, p.Y, z)).ToList());
            }
            catch (KernelException) { }
        }
        return res;
    }

    /// <summary>Point-in-polygon in XY (even-odd).</summary>
    public static bool Inside(IReadOnlyList<Vec3> poly, double x, double y)
    {
        bool c = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            if ((poly[i].Y > y) != (poly[j].Y > y) && x < (poly[j].X - poly[i].X) * (y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X) c = !c;
        return c;
    }

    /// <summary>Loops from picked curves, sketches, edges or flat faces, with the Z they lie at. All must be horizontal.</summary>
    public static List<List<Vec3>> LoopsFromItems(CadSession s, IEnumerable<SelItem> items, out double z, bool closedOnly = true)
    {
        var res = new List<List<Vec3>>(); double zz = double.NaN;
        foreach (var it in items)
        {
            Shape? sh = it.Kind == SelKind.Object ? s.Find(it.Obj)?.Shape : s.ShapeOf(it);
            if (sh == null) continue;
            foreach (var (pts, closed) in Loops(sh))
            {
                if (closedOnly && !closed) continue;
                double mn = pts.Min(p => p.Z), mx = pts.Max(p => p.Z);
                if (mx - mn > 1e-3) throw new FormatException("The outline must lie flat (in a horizontal plane). Sketch it on the XY plane or a plane parallel to it.");
                if (double.IsNaN(zz)) zz = pts[0].Z;
                res.Add(pts);
            }
        }
        z = double.IsNaN(zz) ? 0 : zz;
        return res;
    }
}
