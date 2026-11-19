using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Kernel;

namespace Prism.Meshing;

/// <summary>Local face construction and explicit boundary welding on a triangle mesh.</summary>
public static class MeshLocalEdit
{
    public readonly record struct Edge(int A, int B)
    {
        public (int, int) Key => A < B ? (A, B) : (B, A);
    }

    public readonly record struct Corner(Vec3 Point, int Vertex = -1, int EdgeEnd = -1, double Fraction = 0);
    public sealed record Patch(TriMesh Mesh, int FirstNewTriangle, Edge[] Stitched, int Remaining, string? Error);

    public static Edge[] Boundary(TriMesh mesh, IReadOnlySet<int>? removed = null)
    {
        var counts = new Dictionary<(int, int), (Edge Edge, int Count)>();
        var removedEdges = new HashSet<(int, int)>();
        for (int t = 0; t < mesh.TriCount; t++)
            for (int k = 0; k < 3; k++)
            {
                var edge = new Edge(mesh.T[3 * t + k], mesh.T[3 * t + (k + 1) % 3]);
                if (removed?.Contains(t) == true) removedEdges.Add(edge.Key);
                else counts[edge.Key] = (edge, counts.GetValueOrDefault(edge.Key).Count + 1);
            }
        return counts.Values.Where(x => x.Count == 1 &&
            (removed == null || removedEdges.Contains(x.Edge.Key))).Select(x => x.Edge).ToArray();
    }

    public static Patch Draw(TriMesh source, IReadOnlySet<int>? removed, IReadOnlyList<Edge> boundary, IReadOnlyList<Corner[]> faces)
    {
        var boundaryKeys = boundary.Select(e => e.Key).ToHashSet();
        var boundaryVertices = boundary.SelectMany(e => new[] { e.A, e.B }).ToHashSet();
        var points = Enumerable.Range(0, source.VertexCount).Select(source.P).ToList();
        var vertexColors = source.VertexColors?.ToList();
        var faceColors = source.FaceColors?.ToList();
        faceColors?.Clear();
        var corners = new Dictionary<Corner, int>();
        var splits = new Dictionary<(int, int), List<(double Fraction, int Vertex)>>();
        const uint neutral = 0xFFC8D0DA;

        int Index(Corner corner)
        {
            if (corner.Vertex >= 0 && corner.EdgeEnd < 0)
            {
                if (!boundaryVertices.Contains(corner.Vertex))
                    throw new InvalidOperationException("The snapped vertex is outside the repair boundary.");
                return corner.Vertex;
            }

            var point = corner.Point;
            if (corner.EdgeEnd >= 0)
            {
                var edge = new Edge(corner.Vertex, corner.EdgeEnd);
                if (!boundaryKeys.Contains(edge.Key) || corner.Fraction <= 0 || corner.Fraction >= 1)
                    throw new InvalidOperationException("The snapped edge is outside the repair boundary.");
                double t = edge.A < edge.B ? corner.Fraction : 1 - corner.Fraction;
                point = source.P(edge.Key.Item1) * (1 - t) + source.P(edge.Key.Item2) * t;
                corner = new Corner(point, edge.Key.Item1, edge.Key.Item2, t);
            }
            if (corners.TryGetValue(corner, out int index)) return index;
            index = points.Count;
            corners.Add(corner, index);
            points.Add(point);
            vertexColors?.Add(corner.Vertex >= 0 ? source.VertexColors![corner.Vertex] : neutral);
            if (corner.EdgeEnd >= 0)
            {
                var key = (corner.Vertex, corner.EdgeEnd);
                if (!splits.TryGetValue(key, out var list)) splits[key] = list = [];
                list.Add((corner.Fraction, index));
            }
            return index;
        }

        var polygons = faces.Select(face => face.Select(Index).ToArray()).ToArray();
        var triangles = new List<int>(source.T.Length);
        for (int t = 0; t < source.TriCount; t++)
        {
            if (removed?.Contains(t) == true) continue;
            var (a, b, c) = source.Tri(t);
            if (!splits.ContainsKey(new Edge(a, b).Key) && !splits.ContainsKey(new Edge(b, c).Key) &&
                !splits.ContainsKey(new Edge(c, a).Key))
            {
                triangles.AddRange([a, b, c]);
                if (faceColors != null) faceColors.Add(source.FaceColors![t]);
                continue;
            }
            var fragments = new List<(int A, int B, int C)> { (a, b, c) };
            Split(a, b); Split(b, c); Split(c, a);
            foreach (var (u, v, w) in fragments)
            {
                triangles.AddRange([u, v, w]);
                if (faceColors != null) faceColors.Add(source.FaceColors![t]);
            }

            void Split(int start, int end)
            {
                if (!splits.TryGetValue(new Edge(start, end).Key, out var vertices)) return;
                int i = fragments.FindIndex(f => f.A == start && f.B == end || f.B == start && f.C == end || f.C == start && f.A == end);
                var (p, q, r) = fragments[i];
                int opposite = p != start && p != end ? p : q != start && q != end ? q : r;
                fragments.RemoveAt(i);
                int last = start;
                foreach (var (_, vertex) in vertices.OrderBy(v => start < end ? v.Fraction : -v.Fraction))
                {
                    fragments.Add((last, vertex, opposite));
                    last = vertex;
                }
                fragments.Add((last, end, opposite));
            }
        }

        int firstNew = triangles.Count / 3;
        foreach (var polygon in polygons)
        {
            if (polygon.Length is < 3 or > 4 || polygon.Distinct().Count() != polygon.Length)
                throw new InvalidOperationException("Draw three or four distinct corners for each face.");
            var p = polygon.Select(i => points[i]).ToArray();
            var normal = Vec3.Cross(p[1] - p[0], p[2] - p[0]);
            if (p.Length == 4) normal += Vec3.Cross(p[2] - p[0], p[3] - p[0]);
            if (normal.Length < 1e-12) throw new InvalidOperationException("The face is folded or has no area.");
            normal = normal.Normalized();
            var u = (p[1] - p[0]).Normalized();
            var v = Vec3.Cross(normal, u).Normalized();
            var projected = p.Select(q => (Vec3.Dot(q - p[0], u), Vec3.Dot(q - p[0], v))).ToArray();
            var face = Triangulator.Polygon(projected);
            if (face.Count != polygon.Length - 2) throw new InvalidOperationException("The corners do not form a single face.");
            foreach (var (a, b, c) in face)
            {
                if (Vec3.Dot(Vec3.Cross(p[b] - p[a], p[c] - p[a]), normal) <= 1e-12)
                    throw new InvalidOperationException("The face is folded or has no area. Draw two triangles instead.");
                triangles.AddRange([polygon[a], polygon[b], polygon[c]]);
                faceColors?.Add(neutral);
            }
        }

        var coordinates = new float[points.Count * 3];
        for (int i = 0; i < points.Count; i++)
        {
            coordinates[3 * i] = (float)points[i].X;
            coordinates[3 * i + 1] = (float)points[i].Y;
            coordinates[3 * i + 2] = (float)points[i].Z;
        }
        var mesh = new TriMesh(coordinates, [.. triangles], vertexColors?.ToArray(), faceColors?.ToArray());
        var added = Enumerable.Range(firstNew, mesh.TriCount - firstNew).ToHashSet();
        mesh = MeshRegionRepair.Prepare(mesh, added, new(false, 0, false, false, 0, true)).Mesh;
        var topology = mesh.Topology();
        var stitched = new List<Edge>();
        var reached = new HashSet<int>();
        var queue = new Queue<int>();
        foreach (int t in added)
            for (int k = 0; k < 3; k++)
            {
                int neighbour = topology.Neighbors[3 * t + k];
                if (neighbour < 0 || neighbour >= firstNew) continue;
                stitched.Add(new Edge(mesh.T[3 * t + k], mesh.T[3 * t + (k + 1) % 3]));
                if (reached.Add(t)) queue.Enqueue(t);
            }
        while (queue.TryDequeue(out int t))
            for (int k = 0; k < 3; k++)
            {
                int neighbour = topology.Neighbors[3 * t + k];
                if (neighbour >= firstNew && reached.Add(neighbour)) queue.Enqueue(neighbour);
            }

        var seam = stitched.Select(e => e.Key).ToHashSet();
        int remaining = 0;
        foreach (var edge in boundary)
        {
            if (!splits.TryGetValue(edge.Key, out var inner))
            {
                if (!seam.Contains(edge.Key)) remaining++;
                continue;
            }
            int last = edge.Key.Item1;
            foreach (int next in inner.OrderBy(v => v.Fraction).Select(v => v.Vertex).Append(edge.Key.Item2))
            {
                if (!seam.Contains(new Edge(last, next).Key)) remaining++;
                last = next;
            }
        }
        string? error = Validate(mesh, added);
        if (reached.Count < added.Count) error ??= "Connect every drafted patch to the mesh along a boundary edge.";
        if (removed != null && remaining > 0) error ??= "Finish stitching the entire cut boundary before replacing the selection.";
        return new Patch(mesh, firstNew, [.. stitched], remaining, error);
    }

    public static TriMesh Stitch(TriMesh source, Edge fixedEdge, Edge moving, double tolerance)
    {
        if (fixedEdge.Key == moving.Key) throw new InvalidOperationException("Pick two different boundary edges.");
        if (source.P(fixedEdge.A).DistanceTo(source.P(moving.B)) > tolerance ||
            source.P(fixedEdge.B).DistanceTo(source.P(moving.A)) > tolerance)
            throw new InvalidOperationException("The endpoints are outside the maximum gap or their winding does not match.");
        if (fixedEdge.A == moving.A || fixedEdge.B == moving.B)
            throw new InvalidOperationException("The edges share an endpoint with incompatible winding.");

        var indices = (int[])source.T.Clone();
        var changed = new HashSet<int>();
        for (int i = 0; i < indices.Length; i++)
        {
            int vertex = indices[i];
            int replacement = vertex == moving.A ? fixedEdge.B : vertex == moving.B ? fixedEdge.A : vertex;
            if (replacement == vertex) continue;
            indices[i] = replacement;
            changed.Add(i / 3);
        }
        if (changed.Count == 0) throw new InvalidOperationException("The two edges are already joined.");
        var mesh = new TriMesh(source.V, indices, source.VertexColors, source.FaceColors);
        foreach (int t in changed)
            if (mesh.TriArea(t) <= 1e-12 || Vec3.Dot(source.FaceNormal(t), mesh.FaceNormal(t)) <= 0)
                throw new InvalidOperationException("Stitching would collapse or flip a triangle.");
        if (Validate(mesh, changed) is { } error) throw new InvalidOperationException(error);
        return MeshOps.Compact(mesh.V, mesh.T, mesh.VertexColors, mesh.FaceColors);
    }

    public static string? Validate(TriMesh mesh, IReadOnlySet<int> changed)
    {
        var edges = new Dictionary<(int, int), (int Count, int Winding)>();
        var fans = new Dictionary<int, List<Edge>>();
        foreach (int t in changed)
        {
            if (mesh.TriArea(t) <= 1e-12) return "The edit would create a collapsed triangle.";
            for (int i = 0; i < 3; i++)
            {
                int vertex = mesh.T[3 * t + i];
                edges.TryAdd(new Edge(vertex, mesh.T[3 * t + (i + 1) % 3]).Key, default);
                fans.TryAdd(vertex, []);
            }
        }
        for (int t = 0; t < mesh.TriCount; t++)
            for (int i = 0; i < 3; i++)
            {
                var edge = new Edge(mesh.T[3 * t + i], mesh.T[3 * t + (i + 1) % 3]);
                if (edges.TryGetValue(edge.Key, out var previous))
                    edges[edge.Key] = (previous.Count + 1, previous.Winding + (edge.A < edge.B ? 1 : -1));
                if (fans.TryGetValue(edge.A, out var fan))
                    fan.Add(new Edge(edge.B, mesh.T[3 * t + (i + 2) % 3]));
            }
        if (edges.Values.Any(e => e.Count > 2 || e.Count == 2 && e.Winding != 0))
            return "The edit would create incompatible face winding or a non-manifold edge.";

        foreach (var fan in fans.Values)
            if (!ConnectedFan(fan)) return "The edit would join separate surface sheets at a single vertex.";

        double epsilon = Math.Max(mesh.Size.Length * 1e-7, 1e-9);
        var nearby = new List<int>();
        var padding = new Vec3(epsilon, epsilon, epsilon);
        foreach (int t in changed)
        {
            var (a, b, c) = mesh.Tri(t);
            var vertices = new[] { a, b, c };
            var p = vertices.Select(mesh.P).ToArray();
            var lo = new Vec3(p.Min(x => x.X), p.Min(x => x.Y), p.Min(x => x.Z)) - padding;
            var hi = new Vec3(p.Max(x => x.X), p.Max(x => x.Y), p.Max(x => x.Z)) + padding;
            nearby.Clear();
            mesh.Bvh.QueryBox(lo, hi, nearby);
            foreach (int other in nearby)
            {
                if (other == t || other < t && changed.Contains(other)) continue;
                var (x, y, z) = mesh.Tri(other);
                var opposite = new[] { x, y, z };
                var shared = vertices.Intersect(opposite).ToArray();
                if (shared.Length == 3) return "The edit would add a duplicate face.";
                var q = opposite.Select(mesh.P).ToArray();
                if (shared.Length == 2)
                {
                    var origin = mesh.P(shared[0]);
                    var along = mesh.P(shared[1]) - origin;
                    var u = Vec3.Cross(along, mesh.P(vertices.Except(shared).Single()) - origin);
                    var v = Vec3.Cross(along, mesh.P(opposite.Except(shared).Single()) - origin);
                    bool coplanar = Math.Abs(Vec3.Dot(u.Normalized(), mesh.P(opposite.Except(shared).Single()) - origin)) <= epsilon;
                    if (coplanar && Vec3.Dot(u, v) > 0) return "The edit would overlap a face along a shared edge.";
                    continue;
                }
                if (MeshQuality.TrianglesIntersect(p[0], p[1], p[2], q[0], q[1], q[2], epsilon))
                    return "The edit would create intersecting faces.";
            }
        }
        return null;
    }

    static bool ConnectedFan(List<Edge> edges)
    {
        var adjacency = new Dictionary<int, List<int>>();
        foreach (var e in edges)
        {
            Add(e.A, e.B);
            Add(e.B, e.A);
        }
        void Add(int a, int b)
        {
            if (!adjacency.TryGetValue(a, out var neighbors)) adjacency[a] = neighbors = [];
            neighbors.Add(b);
        }
        var reached = new HashSet<int> { edges[0].A };
        var queue = new Queue<int>(reached);
        while (queue.TryDequeue(out int v))
            foreach (int n in adjacency[v])
                if (reached.Add(n)) queue.Enqueue(n);
        return reached.Count == adjacency.Count;
    }
}
