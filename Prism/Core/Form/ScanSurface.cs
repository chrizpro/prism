using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Kernel;
using Prism.Meshing;

namespace Prism.Core.Form;

/// <summary>Lets form cages snap to and wrap onto scanned meshes in the scene.</summary>
public static class ScanSurface
{
    public static List<TriMesh> Scans(CadSession s) => s.Objects.Where(o => o.Tri != null && o.Visible).Select(o => o.Tri!).ToList();
    public static bool Any(CadSession s) => s.Objects.Any(o => o.Tri != null && o.Visible);

    /// <summary>Nearest hit of a ray on any visible scan, with the surface normal.</summary>
    public static (Vec3 point, Vec3 normal)? Raycast(CadSession s, Vec3 origin, Vec3 dir, double maxT = double.MaxValue)
    {
        (Vec3, Vec3)? best = null; double bt = maxT;
        foreach (var m in Scans(s))
            if (m.Bvh.Raycast(origin, dir, bt) is { } h && h.t < bt) { bt = h.t; best = (origin + dir * h.t, m.FaceNormal(h.tri)); }
        return best;
    }

    public static (Vec3 point, Vec3 normal)? Closest(CadSession s, Vec3 p, double maxDist = double.MaxValue)
    {
        (Vec3, Vec3)? best = null; double bd = maxDist;
        foreach (var m in Scans(s))
            if (m.Bvh.ClosestPoint(p, bd) is { } c && c.Dist < bd) { bd = c.Dist; best = (c.Point, m.FaceNormal(c.Tri)); }
        return best;
    }

    /// <summary>Move cage vertices toward their nearest point on the scan (strength 1 = all the way), optionally floating above it.</summary>
    public static int Wrap(CadSession s, SubdMesh cage, IEnumerable<int> verts, double strength = 1, double offset = 0)
    {
        int n = 0;
        foreach (int v in verts)
            if (Closest(s, cage.V[v]) is { } c)
            {
                var target = c.point + c.normal * offset;
                cage.V[v] = Vec3.Lerp(cage.V[v], target, Math.Clamp(strength, 0, 1)); n++;
            }
        return n;
    }

    /// <summary>Even out the quads: pull each vertex toward the average of its neighbours, then back onto the scan.</summary>
    public static void Relax(CadSession s, SubdMesh cage, IEnumerable<int> verts, int iterations, double offset = 0, bool wrap = true)
    {
        var nb = new List<int>[cage.V.Count];
        for (int i = 0; i < nb.Length; i++) nb[i] = [];
        foreach (var (a, b) in cage.Edges()) { nb[a].Add(b); nb[b].Add(a); }
        var set = verts.ToList();
        for (int it = 0; it < iterations; it++)
        {
            var next = new Dictionary<int, Vec3>();
            foreach (int v in set)
            {
                if (nb[v].Count == 0) continue;
                var avg = nb[v].Aggregate(Vec3.Zero, (acc, u) => acc + cage.V[u]) / nb[v].Count;
                next[v] = Vec3.Lerp(cage.V[v], avg, 0.5);
            }
            foreach (var (v, p) in next) cage.V[v] = p;
            if (wrap) Wrap(s, cage, set, 1, offset);
        }
    }

    /// <summary>Project cage vertices from outside toward a centre: the first scan surface they meet becomes their position. Misses fall back to the closest point.</summary>
    public static void ShrinkFrom(CadSession s, SubdMesh cage, Vec3 center, double offset = 0)
    {
        for (int v = 0; v < cage.V.Count; v++)
        {
            var dir = (center - cage.V[v]).Normalized();
            if (dir.Length < 0.5) continue;
            var hit = Raycast(s, cage.V[v], dir);
            if (hit is { } h) cage.V[v] = h.point + h.normal * offset;
            else if (Closest(s, cage.V[v]) is { } c) cage.V[v] = c.point + c.normal * offset;
        }
    }
}
