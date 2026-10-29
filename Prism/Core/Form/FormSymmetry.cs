using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Kernel;

namespace Prism.Core.Form;

/// <summary>
/// Persistent symmetry for a cage: the mode lives on the form (see <see cref="SubdMesh.Sym"/>), so every edit of the form,
/// not only a drag, is repeated on the mirror images of what was selected.
/// </summary>
public static class FormSymmetry
{
    public static Symmetry Of(SubdMesh m) => Symmetry.Parse(m.Sym);

    /// <summary>Add the mirror images of the selected points, faces and edges to the selections. Returns false when symmetry is off.</summary>
    public static bool Expand(SubdMesh m, Symmetry sym, ISet<int> verts, ISet<int> faces, List<(int a, int b)> edges)
    {
        if (!sym.Active) return false;
        var partners = sym.Partners(m);
        foreach (int v in verts.ToList()) if (v < partners.Length) foreach (var (p, _) in partners[v]) verts.Add(p);

        var byCorners = new Dictionary<string, int>();
        for (int f = 0; f < m.F.Count; f++) byCorners[CornerKey(m.F[f])] = f;
        foreach (int f in faces.ToList())
            for (int k = 0; k < sym.Maps; k++)
            {
                var image = Image(m.F[f], partners, k); if (image == null) continue;
                if (byCorners.TryGetValue(CornerKey(image), out int g)) faces.Add(g);
            }

        var topo = m.Topo();
        foreach (var (a, b) in edges.ToList())
            for (int k = 0; k < sym.Maps; k++)
            {
                int? pa = PartnerOf(partners[a], k), pb = PartnerOf(partners[b], k);
                if (pa == null || pb == null) continue;
                if (topo.EdgeFaces.ContainsKey(SubdMesh.Key(pa.Value, pb.Value)) && !edges.Contains((pa.Value, pb.Value)) && !edges.Contains((pb.Value, pa.Value))) edges.Add((pa.Value, pb.Value));
            }
        return true;
    }

    static int? PartnerOf(List<(int p, int k)> list, int k) { foreach (var (p, kk) in list) if (kk == k) return p; return null; }

    static int[]? Image(int[] face, List<(int p, int k)>[] partners, int k)
    {
        var image = new int[face.Length];
        for (int i = 0; i < face.Length; i++)
        {
            var hit = partners[face[i]].Where(x => x.k == k).Select(x => (int?)x.p).FirstOrDefault();
            if (hit == null) return null;
            image[i] = hit.Value;
        }
        return image;
    }

    static string CornerKey(int[] face) => string.Join(",", face.OrderBy(v => v));

    /// <summary>The faces that mirror each of the given faces (not including the faces themselves), before anything is changed.</summary>
    public static List<int> ImagesOf(SubdMesh m, Symmetry sym, IEnumerable<int> faces)
    {
        var set = new HashSet<int>(faces); var all = new HashSet<int>(faces);
        Expand(m, sym, new HashSet<int>(), all, []);
        all.ExceptWith(set);
        return all.ToList();
    }

    /// <summary>Make the cage exactly symmetric: every point on the chosen side is copied to its partners and points on the plane are put back on it.</summary>
    public static int Enforce(SubdMesh m, Symmetry sym, bool keepPositiveSide = true)
    {
        if (!sym.Active) return 0;
        // a cage that has drifted out of symmetry has no exact partners, so match loosely: within a quarter of the shortest typical edge
        var lengths = m.Edges().Select(e => m.V[e.a].DistanceTo(m.V[e.b])).OrderBy(x => x).ToList();
        double loose = lengths.Count == 0 ? 0.03 : Math.Max(0.03, lengths[lengths.Count / 4] * 0.5);
        var partners = sym.Partners(m, loose); int changed = 0;
        bool Authoritative(Vec3 p) => sym.Mode switch
        {
            SymMode.MirrorX => keepPositiveSide ? p.X >= 0 : p.X <= 0,
            SymMode.MirrorY => keepPositiveSide ? p.Y >= 0 : p.Y <= 0,
            SymMode.MirrorZ => keepPositiveSide ? p.Z >= 0 : p.Z <= 0,
            _ => true,
        };
        var original = m.V.ToArray();
        for (int i = 0; i < original.Length; i++)
        {
            if (!Authoritative(original[i])) continue;
            foreach (var (p, k) in partners[i])
            {
                if (p == i) { var onPlane = sym.Constrain(original[i]); if (onPlane.DistanceTo(m.V[i]) > 1e-9) { m.V[i] = onPlane; changed++; } continue; }
                if (sym.Mode == SymMode.Radial && p < i) continue;
                var target = sym.Map(k, original[i]);
                if (target.DistanceTo(m.V[p]) > 1e-9) { m.V[p] = target; changed++; }
            }
        }
        return changed;
    }

    /// <summary>How far each point is from the position its partner implies (0 means the cage is symmetric).</summary>
    public static double Asymmetry(SubdMesh m, Symmetry sym)
    {
        if (!sym.Active) return 0;
        var partners = sym.Partners(m); double worst = 0; int unmatched = 0;
        for (int i = 0; i < m.V.Count; i++)
        {
            if (partners[i].Count < sym.Maps) { unmatched++; continue; }
            foreach (var (p, k) in partners[i]) worst = Math.Max(worst, sym.Map(k, m.V[i]).DistanceTo(m.V[p]));
        }
        return unmatched > 0 ? double.PositiveInfinity : worst;
    }
}
