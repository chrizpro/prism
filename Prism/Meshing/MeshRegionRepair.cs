using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Kernel;

namespace Prism.Meshing;

/// <summary>Repairs selected triangles while preserving the coordinates and winding of every unselected triangle.</summary>
public static class MeshRegionRepair
{
    public sealed record Options(bool Weld, double Distance, bool Clean, bool RemoveShells, int MinShell, bool Orient);

    public static (TriMesh Mesh, HashSet<int> Selected) Prepare(TriMesh mesh, ISet<int> selection, Options options)
    {
        if (selection.Count == 0 || selection.Any(t => t < 0 || t >= mesh.TriCount))
            throw new ArgumentException("Select a valid region before repairing it.");
        var m = mesh;
        var selected = Enumerable.Range(0, m.TriCount).Select(selection.Contains).ToArray();

        void Keep(Func<int, bool> keep)
        {
            var indices = Enumerable.Range(0, m.TriCount).Where(keep).ToArray();
            m = new TriMesh(m.V, indices.SelectMany(t => m.T.AsSpan(3 * t, 3).ToArray()).ToArray(),
                m.VertexColors, m.FaceColors == null ? null : indices.Select(t => m.FaceColors[t]).ToArray());
            selected = indices.Select(t => selected[t]).ToArray();
        }

        if (options.Weld)
        {
            var active = new bool[m.VertexCount];
            var locked = new bool[m.VertexCount];
            for (int t = 0; t < m.TriCount; t++)
                for (int k = 0; k < 3; k++)
                    if (selected[t]) active[m.T[3 * t + k]] = true;
                    else locked[m.T[3 * t + k]] = true;
            var representative = Enumerable.Range(0, m.VertexCount).ToArray();
            var cells = new Dictionary<(long, long, long), List<int>>();
            double tolerance = Math.Max(options.Distance, 1e-9);
            // Shared vertices anchor the region. Never merge two anchors or move an unselected face.
            foreach (int v in Enumerable.Range(0, m.VertexCount).Where(v => active[v]).OrderByDescending(v => locked[v]))
            {
                var p = m.P(v);
                long x = (long)Math.Floor(p.X / tolerance), y = (long)Math.Floor(p.Y / tolerance), z = (long)Math.Floor(p.Z / tolerance);
                int found = -1;
                if (!locked[v])
                    for (int dx = -1; dx <= 1 && found < 0; dx++)
                        for (int dy = -1; dy <= 1 && found < 0; dy++)
                            for (int dz = -1; dz <= 1 && found < 0; dz++)
                                if (cells.TryGetValue((x + dx, y + dy, z + dz), out var near))
                                    foreach (int candidate in near)
                                        if ((m.P(candidate) - p).Length <= tolerance) { found = candidate; break; }
                if (found >= 0) representative[v] = found;
                else
                {
                    if (!cells.TryGetValue((x, y, z), out var cell)) cells[(x, y, z)] = cell = [];
                    cell.Add(v);
                }
            }
            var triangles = (int[])m.T.Clone();
            for (int t = 0; t < m.TriCount; t++)
                if (selected[t]) for (int k = 0; k < 3; k++) triangles[3 * t + k] = representative[triangles[3 * t + k]];
            m = new TriMesh(m.V, triangles, m.VertexColors, m.FaceColors);
            Keep(t => !selected[t] || (m.T[3 * t] != m.T[3 * t + 1] && m.T[3 * t + 1] != m.T[3 * t + 2] && m.T[3 * t] != m.T[3 * t + 2]));
        }
        if (options.Clean)
        {
            (int, int, int) Key(int t)
            {
                int a = m.T[3 * t], b = m.T[3 * t + 1], c = m.T[3 * t + 2];
                int lo = Math.Min(a, Math.Min(b, c)), hi = Math.Max(a, Math.Max(b, c));
                return (lo, a + b + c - lo - hi, hi);
            }
            var seen = Enumerable.Range(0, m.TriCount).Where(t => !selected[t]).Select(Key).ToHashSet();
            Keep(t => !selected[t] || (m.TriArea(t) >= 1e-12 && seen.Add(Key(t))));
        }
        if (options.RemoveShells)
        {
            var labels = MeshOps.Components(m, out int count);
            var sizes = new int[count]; var protectedShell = new bool[count];
            for (int t = 0; t < m.TriCount; t++) { sizes[labels[t]]++; protectedShell[labels[t]] |= !selected[t]; }
            Keep(t => protectedShell[labels[t]] || sizes[labels[t]] >= options.MinShell);
        }
        if (options.Orient)
        {
            var triangles = (int[])m.T.Clone();
            var topology = m.Topology();
            var visited = selected.Select(value => !value).ToArray();
            var queue = new Queue<int>(Enumerable.Range(0, m.TriCount).Where(t => !selected[t]));
            void Visit()
            {
                while (queue.TryDequeue(out int t))
                    for (int k = 0; k < 3; k++)
                    {
                        int neighbor = topology.Neighbors[3 * t + k];
                        if (neighbor < 0 || visited[neighbor]) continue;
                        int a = triangles[3 * t + k], b = triangles[3 * t + (k + 1) % 3];
                        if (Enumerable.Range(0, 3).Any(j => triangles[3 * neighbor + j] == a && triangles[3 * neighbor + (j + 1) % 3] == b))
                        {
                            (triangles[3 * neighbor + 1], triangles[3 * neighbor + 2]) = (triangles[3 * neighbor + 2], triangles[3 * neighbor + 1]);
                            (topology.Neighbors[3 * neighbor], topology.Neighbors[3 * neighbor + 2]) = (topology.Neighbors[3 * neighbor + 2], topology.Neighbors[3 * neighbor]);
                        }
                        visited[neighbor] = true; queue.Enqueue(neighbor);
                    }
            }
            Visit();
            for (int t = 0; t < m.TriCount; t++)
                if (!visited[t]) { visited[t] = true; queue.Enqueue(t); Visit(); }
            m = new TriMesh(m.V, triangles, m.VertexColors, m.FaceColors);
            var labels = MeshOps.Components(m, out int count);
            var canFlip = Enumerable.Repeat(true, count).ToArray(); var volume = new double[count];
            topology = m.Topology();
            for (int t = 0; t < m.TriCount; t++)
            {
                canFlip[labels[t]] &= selected[t] && Enumerable.Range(0, 3).All(k => topology.Neighbors[3 * t + k] >= 0);
                volume[labels[t]] += Vec3.Dot(m.P(m.T[3 * t]), Vec3.Cross(m.P(m.T[3 * t + 1]), m.P(m.T[3 * t + 2]))) / 6;
            }
            for (int t = 0; t < m.TriCount; t++)
                if (canFlip[labels[t]] && volume[labels[t]] < 0)
                    (triangles[3 * t + 1], triangles[3 * t + 2]) = (triangles[3 * t + 2], triangles[3 * t + 1]);
            // TriMesh caches topology/normals, so do not modify its index array after construction.
            m = new TriMesh(m.V, triangles, m.VertexColors, m.FaceColors);
        }
        return (m, Enumerable.Range(0, m.TriCount).Where(t => selected[t]).ToHashSet());
    }

    /// <summary>A hole is selected only when the triangle on every boundary edge is selected.</summary>
    public static HashSet<long> SelectedBoundaryEdges(TriMesh mesh, ISet<int> selected)
    {
        var result = new HashSet<long>(); var topology = mesh.Topology();
        foreach (int t in selected)
            for (int k = 0; k < 3; k++)
                if (topology.Neighbors[3 * t + k] < 0)
                    result.Add(TriMesh.EdgeKey(mesh.T[3 * t + k], mesh.T[3 * t + (k + 1) % 3], mesh.VertexCount));
        return result;
    }

    public static bool ContainsRim(TriMesh mesh, HashSet<long> edges, int[] loop) =>
        Enumerable.Range(0, loop.Length).All(k => edges.Contains(TriMesh.EdgeKey(loop[k], loop[(k + 1) % loop.Length], mesh.VertexCount)));
}
