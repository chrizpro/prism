using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Kernel;

namespace Prism.Core.Form;

/// <summary>
/// Turns a subdivision cage into real CAD geometry.
/// Regular quad regions of a Catmull-Clark surface are exactly bicubic B-splines, so each regular face becomes one exact
/// NURBS patch built from its 4x4 control neighbourhood (C2-continuous across seams). Faces next to extraordinary vertices,
/// boundaries or creases are approximated: the face is subdivided and each piece fitted through a 5x5 grid of limit-ish points.
/// The patches are sewn into a shell, or a solid when the cage is closed.
/// </summary>
/// <summary>How closely a conversion follows the smooth surface: Exact uses four fitted patches per irregular face, Fewer uses one larger fitted patch (about a quarter of the faces in irregular areas).</summary>
public enum ConvertDetail { Exact, Fewer }

public static class FormToBrep
{
    // ---------------------------------------------------------------- regular patches
    static List<int>? OrderedNeighbors(SubdMesh m, SubdMesh.Topology t, int x)
    {
        var faces = t.VertFaces[x];
        if (faces.Count != 4) return null;
        foreach (var fi in faces) if (m.F[fi].Length != 4) return null;
        int Next(int fi) { var f = m.F[fi]; return f[(Array.IndexOf(f, x) + 1) % 4]; }
        int Prev(int fi) { var f = m.F[fi]; return f[(Array.IndexOf(f, x) + 3) % 4]; }
        var order = new List<int>(); int cur = faces[0];
        for (int k = 0; k < 4; k++)
        {
            int n = Next(cur); order.Add(n);
            int g = faces.FirstOrDefault(h => h != cur && Prev(h) == n, -1);
            if (g < 0) return k == 3 && order.Distinct().Count() == 4 ? order : null;
            cur = g;
        }
        return order.Distinct().Count() == 4 ? order : null;
    }

    static int Opposite(SubdMesh m, SubdMesh.Topology t, int x, int y)
    {
        var o = OrderedNeighbors(m, t, x);
        if (o == null) return -1;
        int i = o.IndexOf(y);
        return i < 0 ? -1 : o[(i + 2) % 4];
    }

    static int Diagonal(SubdMesh m, SubdMesh.Topology t, int a, int p, int q)
    {
        foreach (var fi in t.VertFaces[a])
        {
            var f = m.F[fi];
            if (f.Length == 4 && f.Contains(p) && f.Contains(q))
                return f[(Array.IndexOf(f, a) + 2) % 4];
        }
        return -1;
    }

    static bool IsInteriorRegular(SubdMesh m, SubdMesh.Topology t, int v) => OrderedNeighbors(m, t, v) is { } n && n.All(o => t.EdgeFaces[SubdMesh.Key(v, o)].Count == 2);

    /// <summary>The 4x4 control neighbourhood of a regular quad face, or null if the face is not in a regular region.</summary>
    static Vec3[,]? Stencil(SubdMesh m, SubdMesh.Topology t, int fi, HashSet<int> creased)
    {
        var f = m.F[fi];
        if (f.Length != 4) return null;
        int a = f[0], b = f[1], c = f[2], d = f[3];
        if (!(IsInteriorRegular(m, t, a) && IsInteriorRegular(m, t, b) && IsInteriorRegular(m, t, c) && IsInteriorRegular(m, t, d))) return null;
        var id = new int[4, 4];
        id[1, 1] = a; id[2, 1] = b; id[2, 2] = c; id[1, 2] = d;
        id[0, 1] = Opposite(m, t, a, b); id[3, 1] = Opposite(m, t, b, a);
        id[0, 2] = Opposite(m, t, d, c); id[3, 2] = Opposite(m, t, c, d);
        id[1, 0] = Opposite(m, t, a, d); id[1, 3] = Opposite(m, t, d, a);
        id[2, 0] = Opposite(m, t, b, c); id[2, 3] = Opposite(m, t, c, b);
        foreach (var x in new[] { id[0, 1], id[3, 1], id[0, 2], id[3, 2], id[1, 0], id[1, 3], id[2, 0], id[2, 3] }) if (x < 0) return null;
        id[0, 0] = Diagonal(m, t, a, id[0, 1], id[1, 0]);
        id[3, 0] = Diagonal(m, t, b, id[3, 1], id[2, 0]);
        id[0, 3] = Diagonal(m, t, d, id[0, 2], id[1, 3]);
        id[3, 3] = Diagonal(m, t, c, id[3, 2], id[2, 3]);
        var p = new Vec3[4, 4];
        for (int i = 0; i < 4; i++)
            for (int j = 0; j < 4; j++)
            {
                if (id[i, j] < 0 || creased.Contains(id[i, j])) return null;
                p[i, j] = m.V[id[i, j]];
            }
        return p;
    }

    /// <summary>Uniform cubic B-spline control points -> Bezier control points (tensor product).</summary>
    static Vec3[] ToBezier(Vec3[,] p)
    {
        var tmp = new Vec3[4, 4];
        for (int j = 0; j < 4; j++)
        {
            tmp[0, j] = (p[0, j] + p[1, j] * 4 + p[2, j]) / 6;
            tmp[1, j] = (p[1, j] * 2 + p[2, j]) / 3;
            tmp[2, j] = (p[1, j] + p[2, j] * 2) / 3;
            tmp[3, j] = (p[1, j] + p[2, j] * 4 + p[3, j]) / 6;
        }
        var res = new Vec3[16];
        for (int i = 0; i < 4; i++)
        {
            res[i * 4 + 0] = (tmp[i, 0] + tmp[i, 1] * 4 + tmp[i, 2]) / 6;
            res[i * 4 + 1] = (tmp[i, 1] * 2 + tmp[i, 2]) / 3;
            res[i * 4 + 2] = (tmp[i, 1] + tmp[i, 2] * 2) / 3;
            res[i * 4 + 3] = (tmp[i, 1] + tmp[i, 2] * 4 + tmp[i, 3]) / 6;
        }
        return res;
    }

    // ---------------------------------------------------------------- fitted patches for irregular faces
    static (double u, double v)[] Child((double u, double v)[] p, int k)
    {
        var c = ((p[0].u + p[1].u + p[2].u + p[3].u) / 4, (p[0].v + p[1].v + p[2].v + p[3].v) / 4);
        (double, double) Mid((double u, double v) a, (double u, double v) b) => ((a.u + b.u) / 2, (a.v + b.v) / 2);
        return [p[k], Mid(p[k], p[(k + 1) % 4]), c, Mid(p[(k + 3) % 4], p[k])];
    }

    static Shape Fitted(SubdMesh m3, int root)
    {
        var grid = new Vec3[25];
        (double u, double v)[] unit = [(0, 0), (1, 0), (1, 1), (0, 1)];
        for (int k1 = 0; k1 < 4; k1++)
        {
            var c1 = Child(unit, k1);
            for (int k2 = 0; k2 < 4; k2++)
            {
                var c2 = Child(c1, k2);
                var face = m3.F[16 * root + 4 * k1 + k2];
                for (int c = 0; c < 4; c++)
                    grid[(int)Math.Round(c2[c].u * 4) * 5 + (int)Math.Round(c2[c].v * 4)] = m3.V[face[c]];
            }
        }
        return Occ.FitSurface(grid, 5, 5);
    }

    /// <summary>One fitted patch for a whole quad face: a 9x9 grid of points of the face after three subdivision steps.</summary>
    static Shape FittedWhole(SubdMesh m3, IReadOnlyList<int> children)
    {
        var grid = new Vec3[81];
        (double u, double v)[] unit = [(0, 0), (1, 0), (1, 1), (0, 1)];
        for (int k0 = 0; k0 < 4; k0++)
        {
            var c0 = Child(unit, k0); int root = children[k0];
            for (int k1 = 0; k1 < 4; k1++)
            {
                var c1 = Child(c0, k1);
                for (int k2 = 0; k2 < 4; k2++)
                {
                    var c2 = Child(c1, k2); var face = m3.F[16 * root + 4 * k1 + k2];
                    for (int c = 0; c < 4; c++) grid[(int)Math.Round(c2[c].u * 8) * 9 + (int)Math.Round(c2[c].v * 8)] = m3.V[face[c]];
                }
            }
        }
        return Occ.FitSurface(grid, 9, 9);
    }

    public static Shape ToShape(SubdMesh cage, ConvertDetail detail = ConvertDetail.Exact)
    {
        var report = CageReport.Inspect(cage);
        if (!report.Valid) throw new KernelException("Repair the freeform cage before conversion. Run Check Cage to locate degenerate faces, winding conflicts or non-manifold edges and vertices.");
        if (cage.IsEmpty) throw new KernelException("The form has no faces.");
        bool closed = cage.IsClosed();
        var (mn, mx) = cage.Bounds();
        double diag = (mx - mn).Length;
        var topo = cage.Topo();

        // vertices touched by creases use the approximate path so the sharp feature is honoured
        var creased = new HashSet<int>();
        foreach (var (k, s) in cage.Crease) if (s > 0) { var (a, b) = SubdMesh.Unkey(k); creased.Add(a); creased.Add(b); }
        // a crease also affects every vertex that shares a face with it: widen by one ring
        if (creased.Count > 0)
            foreach (var v in creased.ToList()) foreach (var fi in topo.VertFaces[v]) foreach (var w in cage.F[fi]) creased.Add(w);

        var patches = new List<Shape>();
        List<int>? irregular = null;
        for (int fi = 0; fi < cage.F.Count; fi++)
        {
            var st = Stencil(cage, topo, fi, creased);
            if (st != null) patches.Add(Occ.NurbsSurface(ToBezier(st), 4, 4, 3, 3));
            else (irregular ??= []).Add(fi);
        }

        if (irregular != null)
        {
            var m1 = cage.Refine(out var parent1);
            var m2 = m1.Refine(out _);
            var m3 = m2.Refine(out _);
            var irr = irregular.ToHashSet();
            var done = new HashSet<int>();
            for (int r = 0; r < m1.F.Count; r++)
            {
                int face = parent1[r];
                if (!irr.Contains(face)) continue;
                if (detail == ConvertDetail.Fewer && cage.F[face].Length == 4)
                {
                    if (!done.Add(face)) continue;                       // the other three children are part of the same patch
                    var kids = Enumerable.Range(0, m1.F.Count).Where(x => parent1[x] == face).ToList();
                    patches.Add(FittedWhole(m3, kids));
                }
                else patches.Add(Fitted(m3, r));
            }
        }
        return Occ.Sew(patches, Math.Max(diag * 0.002, 0.005), closed);
    }
}
