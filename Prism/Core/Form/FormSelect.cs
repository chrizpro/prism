using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Kernel;

namespace Prism.Core.Form;

/// <summary>Selection helpers for cages: grow, shrink, invert, flood by angle, boundary and creased edges.</summary>
public static class FormSelect
{
    // ---------------------------------------------------------------- faces
    public static HashSet<int> GrowFaces(SubdMesh m, ISet<int> faces)
    {
        var topo = m.Topo(); var result = new HashSet<int>(faces);
        foreach (int f in faces) foreach (int v in m.F[f]) foreach (int g in topo.VertFaces[v]) result.Add(g);
        return result;
    }

    /// <summary>Drop every selected face that touches an unselected one.</summary>
    public static HashSet<int> ShrinkFaces(SubdMesh m, ISet<int> faces)
    {
        var topo = m.Topo(); var result = new HashSet<int>();
        foreach (int f in faces)
            if (m.F[f].All(v => topo.VertFaces[v].All(faces.Contains))) result.Add(f);
        return result;
    }

    public static HashSet<int> InvertFaces(SubdMesh m, ISet<int> faces) => Enumerable.Range(0, m.F.Count).Where(f => !faces.Contains(f)).ToHashSet();

    /// <summary>Flood out from the seed faces across shared edges while the angle between neighbouring faces stays under the limit. Creased and open edges stop it.</summary>
    public static HashSet<int> FacesByAngle(SubdMesh m, IEnumerable<int> seeds, double degrees, bool stopAtCreases = true)
    {
        var topo = m.Topo(); var limit = Math.Cos(Math.Clamp(degrees, 0, 180) * Math.PI / 180);
        var result = new HashSet<int>(seeds); var queue = new Queue<int>(result);
        while (queue.Count > 0)
        {
            int f = queue.Dequeue(); var fn = m.FaceNormal(f); var fc = m.F[f];
            for (int i = 0; i < fc.Length; i++)
            {
                long key = SubdMesh.Key(fc[i], fc[(i + 1) % fc.Length]);
                if (stopAtCreases && m.Crease.TryGetValue(key, out var s) && s > 0) continue;
                if (!topo.EdgeFaces.TryGetValue(key, out var neighbours)) continue;
                foreach (int g in neighbours)
                    if (!result.Contains(g) && Vec3.Dot(fn, m.FaceNormal(g)) >= limit) { result.Add(g); queue.Enqueue(g); }
            }
        }
        return result;
    }

    // ---------------------------------------------------------------- vertices
    static List<int>[] Neighbours(SubdMesh m)
    {
        var n = new List<int>[m.V.Count]; for (int i = 0; i < n.Length; i++) n[i] = [];
        foreach (var (a, b) in m.Edges()) { n[a].Add(b); n[b].Add(a); }
        return n;
    }

    public static HashSet<int> GrowVertices(SubdMesh m, ISet<int> verts)
    {
        var n = Neighbours(m); var result = new HashSet<int>(verts);
        foreach (int v in verts) foreach (int o in n[v]) result.Add(o);
        return result;
    }

    public static HashSet<int> ShrinkVertices(SubdMesh m, ISet<int> verts)
    {
        var n = Neighbours(m);
        return verts.Where(v => n[v].All(verts.Contains)).ToHashSet();
    }

    public static HashSet<int> InvertVertices(SubdMesh m, ISet<int> verts) => Enumerable.Range(0, m.V.Count).Where(v => !verts.Contains(v)).ToHashSet();

    // ---------------------------------------------------------------- edges
    public static HashSet<long> GrowEdges(SubdMesh m, ISet<long> edges)
    {
        var points = new HashSet<int>(edges.SelectMany(k => { var (a, b) = SubdMesh.Unkey(k); return new[] { a, b }; }));
        var result = new HashSet<long>(edges);
        foreach (var (a, b) in m.Edges()) if (points.Contains(a) || points.Contains(b)) result.Add(SubdMesh.Key(a, b));
        return result;
    }

    public static HashSet<long> ShrinkEdges(SubdMesh m, ISet<long> edges)
    {
        var all = m.Edges().Select(e => (e.a, e.b, key: SubdMesh.Key(e.a, e.b))).ToList();
        var result = new HashSet<long>();
        foreach (var e in edges)
        {
            var (a, b) = SubdMesh.Unkey(e);
            // an edge stays when every edge meeting it at either end is selected too
            if (all.Where(x => x.a == a || x.b == a || x.a == b || x.b == b).All(x => edges.Contains(x.key))) result.Add(e);
        }
        return result;
    }

    public static HashSet<long> InvertEdges(SubdMesh m, ISet<long> edges) => m.Edges().Select(e => SubdMesh.Key(e.a, e.b)).Where(k => !edges.Contains(k)).ToHashSet();

    public static HashSet<long> BoundaryEdges(SubdMesh m) => m.Topo().EdgeFaces.Where(p => p.Value.Count == 1).Select(p => p.Key).ToHashSet();

    public static HashSet<long> CreasedEdges(SubdMesh m) => m.Crease.Where(c => c.Value > 0 && SubdMesh.Unkey(c.Key).a != SubdMesh.Unkey(c.Key).b).Select(c => c.Key).ToHashSet();

    // ---------------------------------------------------------------- lasso
    /// <summary>Even-odd point-in-polygon test in screen space.</summary>
    public static bool InPolygon(IReadOnlyList<(double x, double y)> path, double x, double y)
    {
        bool inside = false;
        for (int i = 0, j = path.Count - 1; i < path.Count; j = i++)
            if ((path[i].y > y) != (path[j].y > y) && x < (path[j].x - path[i].x) * (y - path[i].y) / (path[j].y - path[i].y) + path[i].x) inside = !inside;
        return inside;
    }
}
