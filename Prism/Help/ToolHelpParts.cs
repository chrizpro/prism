using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Kernel;
using Prism.Rendering;

namespace Prism.Help;

/// <summary>Building blocks for the before/after examples: result-colored boxes, toolpath lines and face lookups.</summary>
public static partial class ToolHelp
{
    const uint Blue = 0xFF3988E8, Green = 0xFF2EA043, Red = 0xFFE5484D, Orange = 0xFFF76B15, Purple = 0xFF8E4EC6, Amber = 0xFFE5A336, Copper = 0xFFC77D3A;

    static ToolHelpTopic Topic(string key, string title, string purpose, string before, string after, string steps, string tip)
        => new(key, title, purpose, before, after, steps, tip);

    static StudioPreviewSource Src(MeshData mesh, string label, int[]? edges = null, int[]? faces = null, uint? color = null, bool error = false, bool mono = false)
        => new(mesh, edges ?? [], faces ?? [], error, label, color, mono);

    /// <summary>The same shape the kernel's Box makes (centered on X and Y, standing on Z=0), cut into a grid so a result field can vary across it.</summary>
    static MeshData GridBox(double dx, double dy, double dz, int nx, int ny, int nz, bool wire = false)
    {
        // Face ids: 0 +Z, 1 -Z, 2 +X, 3 -X, 4 +Y, 5 -Y.
        var faces = new (Vec3 o, Vec3 u, Vec3 v, Vec3 n, int nu, int nv)[]
        {
            (new(-dx / 2, -dy / 2, dz), new(dx, 0, 0), new(0, dy, 0), new(0, 0, 1), nx, ny),
            (new(-dx / 2, -dy / 2, 0), new(dx, 0, 0), new(0, dy, 0), new(0, 0, -1), nx, ny),
            (new(dx / 2, -dy / 2, 0), new(0, dy, 0), new(0, 0, dz), new(1, 0, 0), ny, nz),
            (new(-dx / 2, -dy / 2, 0), new(0, dy, 0), new(0, 0, dz), new(-1, 0, 0), ny, nz),
            (new(-dx / 2, dy / 2, 0), new(dx, 0, 0), new(0, 0, dz), new(0, 1, 0), nx, nz),
            (new(-dx / 2, -dy / 2, 0), new(dx, 0, 0), new(0, 0, dz), new(0, -1, 0), nx, nz),
        };
        int vertices = faces.Sum(f => (f.nu + 1) * (f.nv + 1)), triangles = faces.Sum(f => f.nu * f.nv * 2);
        var lines = new List<(Vec3 a, Vec3 b)>();
        if (wire)
            foreach (var f in faces)
            {
                for (int i = 0; i <= f.nu; i++) for (int j = 0; j < f.nv; j++)
                    lines.Add((f.o + f.u * ((double)i / f.nu) + f.v * ((double)j / f.nv), f.o + f.u * ((double)i / f.nu) + f.v * ((double)(j + 1) / f.nv)));
                for (int j = 0; j <= f.nv; j++) for (int i = 0; i < f.nu; i++)
                    lines.Add((f.o + f.u * ((double)i / f.nu) + f.v * ((double)j / f.nv), f.o + f.u * ((double)(i + 1) / f.nu) + f.v * ((double)j / f.nv)));
            }
        else
        {
            var c = new Vec3[8];
            for (int i = 0; i < 8; i++) c[i] = new((i & 1) == 0 ? -dx / 2 : dx / 2, (i & 2) == 0 ? -dy / 2 : dy / 2, (i & 4) == 0 ? 0 : dz);
            foreach (var (a, b) in new[] { (0, 1), (2, 3), (4, 5), (6, 7), (0, 2), (1, 3), (4, 6), (5, 7), (0, 4), (1, 5), (2, 6), (3, 7) }) lines.Add((c[a], c[b]));
        }
        var mesh = new MeshData(vertices, triangles, lines.Count) { FaceCount = 6, EdgeCount = wire ? 1 : 12, Min = new(-dx / 2, -dy / 2, 0), Max = new(dx / 2, dy / 2, dz) };
        int vertex = 0, triangle = 0;
        for (int face = 0; face < faces.Length; face++)
        {
            var f = faces[face]; int first = vertex;
            for (int j = 0; j <= f.nv; j++) for (int i = 0; i <= f.nu; i++)
            {
                var p = f.o + f.u * ((double)i / f.nu) + f.v * ((double)j / f.nv);
                mesh.Positions[vertex * 3] = (float)p.X; mesh.Positions[vertex * 3 + 1] = (float)p.Y; mesh.Positions[vertex * 3 + 2] = (float)p.Z;
                mesh.Normals[vertex * 3] = (float)f.n.X; mesh.Normals[vertex * 3 + 1] = (float)f.n.Y; mesh.Normals[vertex * 3 + 2] = (float)f.n.Z;
                vertex++;
            }
            for (int j = 0; j < f.nv; j++) for (int i = 0; i < f.nu; i++)
            {
                int a = first + j * (f.nu + 1) + i, b = a + 1, d = a + f.nu + 1, e = d + 1;
                mesh.Indices[triangle * 3] = a; mesh.Indices[triangle * 3 + 1] = b; mesh.Indices[triangle * 3 + 2] = e; mesh.TriFace[triangle++] = face;
                mesh.Indices[triangle * 3] = a; mesh.Indices[triangle * 3 + 1] = e; mesh.Indices[triangle * 3 + 2] = d; mesh.TriFace[triangle++] = face;
            }
        }
        for (int i = 0; i < lines.Count; i++)
        {
            var (a, b) = lines[i];
            mesh.EdgePoints[i * 6] = (float)a.X; mesh.EdgePoints[i * 6 + 1] = (float)a.Y; mesh.EdgePoints[i * 6 + 2] = (float)a.Z;
            mesh.EdgePoints[i * 6 + 3] = (float)b.X; mesh.EdgePoints[i * 6 + 4] = (float)b.Y; mesh.EdgePoints[i * 6 + 5] = (float)b.Z;
            mesh.SegEdge[i] = wire ? 0 : i;
        }
        return mesh;
    }

    static Vec3 At(float[] data, int index) => new(data[index * 3], data[index * 3 + 1], data[index * 3 + 2]);

    /// <summary>Paint a result field (0 = blue, 1 = red) over every vertex; the field is rescaled to fill that range.</summary>
    static MeshData Colorize(MeshData mesh, Func<Vec3, Vec3, double> field)
    {
        var values = new float[mesh.VertexCount];
        for (int i = 0; i < values.Length; i++) values[i] = (float)field(At(mesh.Positions, i), At(mesh.Normals, i));
        float low = values.Min(), high = values.Max(); if (high - low < 1e-6f) high = low + 1e-6f;
        for (int i = 0; i < values.Length; i++) values[i] = (values[i] - low) / (high - low);
        mesh.Scalars = values;
        return mesh;
    }

    /// <summary>Move every vertex (and edge point) by the given function, as an exaggerated displacement.</summary>
    static MeshData Deform(MeshData mesh, Func<Vec3, Vec3> move)
    {
        for (int i = 0; i < mesh.VertexCount; i++) Put(mesh.Positions, i, move(At(mesh.Positions, i)));
        for (int i = 0; i < mesh.EdgePoints.Length / 3; i++) Put(mesh.EdgePoints, i, move(At(mesh.EdgePoints, i)));
        var lo = new Vec3(double.MaxValue, double.MaxValue, double.MaxValue); var hi = -lo;
        for (int i = 0; i < mesh.VertexCount; i++)
        { var p = At(mesh.Positions, i); lo = new(Math.Min(lo.X, p.X), Math.Min(lo.Y, p.Y), Math.Min(lo.Z, p.Z)); hi = new(Math.Max(hi.X, p.X), Math.Max(hi.Y, p.Y), Math.Max(hi.Z, p.Z)); }
        mesh.Min = lo; mesh.Max = hi;
        return mesh;
        static void Put(float[] data, int index, Vec3 p) { data[index * 3] = (float)p.X; data[index * 3 + 1] = (float)p.Y; data[index * 3 + 2] = (float)p.Z; }
    }

    /// <summary>Give the triangles whose center satisfies the test a face id of their own, so they can be highlighted as a patch.</summary>
    static int Patch(MeshData mesh, Func<Vec3, bool> inside)
    {
        int id = mesh.FaceCount;
        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            var center = (At(mesh.Positions, mesh.Indices[t * 3]) + At(mesh.Positions, mesh.Indices[t * 3 + 1]) + At(mesh.Positions, mesh.Indices[t * 3 + 2])) / 3;
            if (inside(center)) mesh.TriFace[t] = id;
        }
        return id;
    }

    /// <summary>Every face whose vertices all lie on one coordinate plane (axis 0 = X, 1 = Y, 2 = Z).</summary>
    static int[] FacesAt(MeshData mesh, int axis, double value)
    {
        var found = new List<int>();
        for (int face = 0; face < mesh.FaceCount; face++)
        {
            bool flat = mesh.FaceTriStart[face + 1] > mesh.FaceTriStart[face];
            for (int tri = mesh.FaceTriStart[face]; tri < mesh.FaceTriStart[face + 1] && flat; tri++)
                for (int k = 0; k < 3 && flat; k++)
                    flat = Math.Abs(mesh.Positions[mesh.Indices[tri * 3 + k] * 3 + axis] - value) < 1e-4;
            if (flat) found.Add(face);
        }
        return found.ToArray();
    }

    /// <summary>Faces whose surface normal passes the test (read at the face's first vertex).</summary>
    static int[] FacesWhere(MeshData mesh, Func<Vec3, bool> normalTest)
    {
        var found = new List<int>();
        for (int face = 0; face < mesh.FaceCount; face++)
            if (mesh.FaceTriStart[face + 1] > mesh.FaceTriStart[face] && normalTest(At(mesh.Normals, mesh.Indices[mesh.FaceTriStart[face] * 3]))) found.Add(face);
        return found.ToArray();
    }

    static int[] AllFaces(MeshData mesh) => Enumerable.Range(0, mesh.FaceCount).ToArray();
    static int[] AllEdges(MeshData mesh) => mesh.SegEdge.Distinct().ToArray();

    /// <summary>Move a shape and release the original.</summary>
    static Shape Mv(Shape shape, double x, double y, double z) { using (shape) return Occ.Translate(shape, new(x, y, z)); }

    static Shape Join(IEnumerable<Shape> shapes)
    {
        var list = shapes.ToList();
        try { return Occ.Compound(list); }
        finally { foreach (var shape in list) shape.Dispose(); }
    }

    /// <summary>A group of open polylines, as the toolpath examples draw them.</summary>
    static Shape Paths(params IEnumerable<Vec3>[] lines) => Join(lines.Select(l => Occ.Polyline(l.ToList(), false)));

    static IEnumerable<Vec3> Rect(double x0, double y0, double x1, double y1, double z) => [new(x0, y0, z), new(x1, y0, z), new(x1, y1, z), new(x0, y1, z), new(x0, y0, z)];

    /// <summary>Back-and-forth passes across a rectangle, one polyline.</summary>
    static IEnumerable<Vec3> Zigzag(double x0, double x1, double y0, double y1, double step, double z, Func<double, double, double>? height = null)
    {
        var points = new List<Vec3>(); bool forward = true;
        for (double y = y0; y <= y1 + 1e-9; y += step)
        {
            double a = forward ? x0 : x1, b = forward ? x1 : x0;
            for (int i = 0; i <= 12; i++) { double x = a + (b - a) * i / 12; points.Add(new(x, y, height?.Invoke(x, y) ?? z)); }
            forward = !forward;
        }
        return points;
    }

    static ToolHelpExample FromShapes(Shape before, Shape after, ToolHelpTopic topic, int[]? beforeFaces = null, uint? beforeColor = null, bool pathBefore = false, bool pathAfter = false)
    {
        var b = before.Tessellate(); var a = after.Tessellate();
        return new(Src(b, topic.BeforeLabel, pathBefore ? AllEdges(b) : null, beforeFaces, pathBefore ? Blue : beforeColor),
            Src(a, topic.AfterLabel, pathAfter ? AllEdges(a) : null, null, pathAfter ? Blue : null));
    }
}
