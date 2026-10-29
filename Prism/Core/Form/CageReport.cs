using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Kernel;

namespace Prism.Core.Form;

/// <summary>Cage integrity checks. Does not certify absence of geometric self-intersections.</summary>
public sealed record CageReport(int InvalidFaces, int DegenerateFaces, int NonFiniteVertices, int IsolatedVertices,
    int BoundaryEdges, int NonManifoldEdges, int WindingConflicts, int DisconnectedVertexFans, int Components,
    int ExtraordinaryVertices, IReadOnlyList<long> ProblemEdges)
{
    public bool Valid => Components > 0 && InvalidFaces + DegenerateFaces + NonFiniteVertices + NonManifoldEdges + WindingConflicts + DisconnectedVertexFans == 0;
    public static CageReport Inspect(SubdMesh mesh)
    {
        int invalid = 0, degenerate = 0, conflicts = 0, fans = 0, extraordinary = 0;
        var edges = new Dictionary<long, List<(int face, bool forward)>>();
        var incident = Enumerable.Range(0, mesh.V.Count).Select(_ => new HashSet<int>()).ToArray();
        var neighbors = Enumerable.Range(0, mesh.V.Count).Select(_ => new HashSet<int>()).ToArray();
        foreach (var (f, fi) in mesh.F.Select((f, fi) => (f, fi)))
        {
            if (f.Length < 3 || f.Distinct().Count() != f.Length || f.Any(v => v < 0 || v >= mesh.V.Count)) { invalid++; continue; }
            Vec3 area = Vec3.Zero;
            for (int i = 1; i + 1 < f.Length; i++) area += Vec3.Cross(mesh.V[f[i]] - mesh.V[f[0]], mesh.V[f[i + 1]] - mesh.V[f[0]]);
            if (area.Length < 1e-12) degenerate++;
            for (int i = 0; i < f.Length; i++)
            {
                int a = f[i], b = f[(i + 1) % f.Length]; long key = SubdMesh.Key(a, b);
                if (!edges.TryGetValue(key, out var use)) edges[key] = use = [];
                use.Add((fi, a < b)); incident[a].Add(fi); neighbors[a].Add(b); neighbors[b].Add(a);
            }
        }
        var problem = new HashSet<long>();
        foreach (var (edge, use) in edges)
        {
            if (use.Count > 2) problem.Add(edge);
            if (use.Count == 2 && use[0].forward == use[1].forward) { conflicts++; problem.Add(edge); }
        }
        for (int v = 0; v < mesh.V.Count; v++)
        {
            if (incident[v].Count == 0) continue;
            var boundary = neighbors[v].Count(n => edges[SubdMesh.Key(v, n)].Count == 1);
            if (boundary == 0 && neighbors[v].Count != 4) extraordinary++;
            var remainingFaces = incident[v].ToHashSet(); int count = 0;
            while (remainingFaces.Count > 0)
            {
                count++; var queue = new Queue<int>(); queue.Enqueue(remainingFaces.First());
                while (queue.TryDequeue(out int fi)) if (remainingFaces.Remove(fi))
                    foreach (int n in neighbors[v])
                    {
                        var use = edges[SubdMesh.Key(v, n)];
                        if (use.Any(u => u.face == fi)) foreach (var u in use) queue.Enqueue(u.face);
                    }
            }
            if (count > 1) { fans++; foreach (int n in neighbors[v]) problem.Add(SubdMesh.Key(v, n)); }
        }
        var remaining = Enumerable.Range(0, mesh.V.Count).Where(v => incident[v].Count > 0).ToHashSet(); int components = 0;
        while (remaining.Count > 0)
        {
            components++; var queue = new Queue<int>(); queue.Enqueue(remaining.First());
            while (queue.TryDequeue(out int v)) if (remaining.Remove(v)) foreach (int n in neighbors[v]) queue.Enqueue(n);
        }
        int nonFinite = mesh.V.Count(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y) || !double.IsFinite(p.Z));
        return new(invalid, degenerate, nonFinite, incident.Count(f => f.Count == 0), edges.Count(e => e.Value.Count == 1),
            edges.Count(e => e.Value.Count > 2), conflicts, fans, components, extraordinary, problem.ToArray());
    }
}
