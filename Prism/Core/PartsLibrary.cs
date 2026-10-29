using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Prism.Core.Assembly;
using Prism.Kernel;

namespace Prism.Core;

/// <summary>One part offered in the library: a file in the library folder, or a built-in parametric part.</summary>
public sealed class LibraryPart
{
    public required string Name { get; init; }
    public required string Category { get; init; }
    /// <summary>The STEP / .prism file, or null for a built-in part.</summary>
    public string? Path { get; init; }
    public Func<Shape>? Builder { get; init; }
    public bool IsBuiltIn => Builder != null;
    public string Source => Path != null ? System.IO.Path.GetFileName(Path) : "Built-in";
}

/// <summary>
/// The parts library: the STEP / Prism files found in a chosen folder (sub-folders become categories) plus a small set of
/// built-in parametric common parts that need no files at all.
/// </summary>
public static class PartsLibrary
{
    public static readonly string[] Extensions = [".step", ".stp", ".prism", ".ourcad"];
    public const int MaxFiles = 600;

    public static string? Folder => AppSettings.Current.PartsLibraryPath;

    public static List<LibraryPart> Scan(string? folder)
    {
        var list = new List<LibraryPart>();
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return list;
        var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 4, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
        foreach (var file in Directory.EnumerateFiles(folder, "*.*", options))
        {
            if (!Extensions.Contains(System.IO.Path.GetExtension(file).ToLowerInvariant())) continue;
            var dir = System.IO.Path.GetDirectoryName(file) ?? folder;
            string category = System.IO.Path.GetRelativePath(folder, dir) is { } rel && rel != "." ? rel.Replace('\\', '/') : "Library";
            list.Add(new LibraryPart { Name = System.IO.Path.GetFileNameWithoutExtension(file), Category = category, Path = file });
            if (list.Count >= MaxFiles) break;
        }
        return [.. list.OrderBy(p => p.Category, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)];
    }

    // ------------------------------------------------------------------ built-in parametric parts
    static Shape Tube(double outer, double inner, double h) => Occ.Cut(Occ.Cylinder(outer, h), Occ.Translate(Occ.Cylinder(inner, h + 2), new Vec3(0, 0, -1)));

    static Shape Washer(double d, double outer, double t) => Tube(outer / 2, d / 2, t);

    static Shape HexPrism(double acrossFlats, double h)
    {
        double r = acrossFlats / Math.Sqrt(3);
        var corners = Enumerable.Range(0, 6).Select(i => new Vec3(r * Math.Cos(i * Math.PI / 3), r * Math.Sin(i * Math.PI / 3), 0)).ToList();
        corners.Add(corners[0]);
        return Occ.Extrude(Occ.Profile([Occ.Polyline(corners, true)]), new Vec3(0, 0, h));
    }

    static Shape HexNut(double d, double flats, double h) => Occ.Cut(HexPrism(flats, h), Occ.Translate(Occ.Cylinder(d / 2, h + 2), new Vec3(0, 0, -1)));

    static Shape HexBolt(double d, double length, double flats, double headHeight)
        => Occ.Fuse(HexPrism(flats, headHeight), Occ.Translate(Occ.Cylinder(d / 2, length), new Vec3(0, 0, headHeight)));

    static Shape SocketScrew(double d, double length)
    {
        double head = d * 1.5, hh = d;
        var body = Occ.Fuse(Occ.Cylinder(head / 2, hh), Occ.Translate(Occ.Cylinder(d / 2, length), new Vec3(0, 0, hh)));
        return Occ.Cut(body, Occ.Translate(HexPrism(d * 0.8, hh * 0.6), new Vec3(0, 0, hh * 0.4)));
    }

    public static List<LibraryPart> BuiltIn()
    {
        var list = new List<LibraryPart>();
        void Add(string category, string name, Func<Shape> build) => list.Add(new LibraryPart { Name = name, Category = category, Builder = build });
        // ISO metric coarse series: nominal diameter, across flats, nut height, washer outer diameter and thickness
        foreach (var (d, flats, nut, wOuter, wThick) in new (double, double, double, double, double)[] { (3, 5.5, 2.4, 7, 0.5), (4, 7, 3.2, 9, 0.8), (5, 8, 4.7, 10, 1), (6, 10, 5.2, 12, 1.6), (8, 13, 6.8, 16, 1.6), (10, 16, 8.4, 20, 2), (12, 18, 10.8, 24, 2.5) })
        {
            double dd = d, ff = flats, nn = nut, wo = wOuter, wt = wThick;
            Add("Fasteners", $"Hex nut M{d}", () => HexNut(dd, ff, nn));
            Add("Fasteners", $"Flat washer M{d}", () => Washer(dd + 0.4 + dd * 0.05, wo, wt));
            Add("Fasteners", $"Hex bolt M{d}x{d * 4}", () => HexBolt(dd, dd * 4, ff, dd * 0.65));
            Add("Fasteners", $"Socket head screw M{d}x{d * 3}", () => SocketScrew(dd, dd * 3));
        }
        foreach (double d in new[] { 3.0, 4, 5, 6, 8 })
        {
            double dd = d;
            Add("Pins and spacers", $"Dowel pin {d}x{d * 4}", () => Occ.Cylinder(dd / 2, dd * 4));
            Add("Pins and spacers", $"Round spacer {d}x{d * 2}", () => Tube(dd, dd / 2, dd * 2));
        }
        foreach (var (od, id, w) in new (double, double, double)[] { (22, 8, 7), (26, 10, 8), (30, 12, 8), (32, 15, 9) })
        {
            double o = od, i = id, ww = w;
            Add("Bearings", $"Ball bearing {id}x{od}x{w}", () => Occ.Fuse(Tube(o / 2, o / 2 - 2.2, ww), Tube(i / 2 + 2.2, i / 2, ww)));
        }
        foreach (double size in new[] { 20.0, 30, 40 })
        {
            double s = size;
            Add("Structure", $"Square tube {size}x{size}x{size * 5}", () => Occ.Cut(Occ.Box(s, s, s * 5), Occ.Translate(Occ.Box(s - 4, s - 4, s * 5 + 2), new Vec3(2, 2, -1))));
        }
        Add("Primitives", "Ball 20 mm", () => Occ.Sphere(10));
        Add("Primitives", "Block 20x20x20", () => Occ.Box(20, 20, 20));
        return list;
    }

    // ------------------------------------------------------------------ loading
    /// <summary>The part's geometry and colour, ready to place as a component.</summary>
    public static (Shape shape, uint color, Appearance? look) Load(LibraryPart part)
    {
        if (part.Builder != null) return (part.Builder(), 0xFFB4BCC8, null);
        string path = part.Path ?? throw new InvalidOperationException("That library entry has no file.");
        string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".prism" or ".ourcad")
        {
            var doc = new CadSession(); doc.Open(path);
            return CadSession.PartGeometry(doc) is { } g ? (g.shape, g.color, g.look) : throw new InvalidOperationException($"{part.Name} has no solid bodies.");
        }
        return (Occ.ImportStep(path), 0xFFB4BCC8, null);
    }

    // ------------------------------------------------------------------ thumbnails
    /// <summary>A small shaded picture of a shape from a three-quarter view, as BGRA pixels (software rasteriser, orthographic).</summary>
    public static byte[] RenderThumbnail(Shape shape, int size, uint background = 0xFF20252E, uint color = 0xFFB4BCC8)
    {
        var mesh = shape.Tessellate();
        var pixels = new byte[size * size * 4];
        for (int i = 0; i < size * size; i++) { pixels[i * 4] = (byte)background; pixels[i * 4 + 1] = (byte)(background >> 8); pixels[i * 4 + 2] = (byte)(background >> 16); pixels[i * 4 + 3] = 255; }
        if (mesh.TriangleCount == 0) return pixels;
        var forward = new Vec3(-0.55, 0.75, -0.45).Normalized();
        var right = Vec3.Cross(forward, Vec3.UnitZ).Normalized();
        var up = Vec3.Cross(right, forward).Normalized();
        var centre = (mesh.Min + mesh.Max) * 0.5;
        int vn = mesh.VertexCount;
        var sx = new double[vn]; var sy = new double[vn]; var sz = new double[vn];
        double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
        for (int i = 0; i < vn; i++)
        {
            var p = new Vec3(mesh.Positions[i * 3], mesh.Positions[i * 3 + 1], mesh.Positions[i * 3 + 2]) - centre;
            sx[i] = Vec3.Dot(p, right); sy[i] = Vec3.Dot(p, up); sz[i] = Vec3.Dot(p, forward);
            minX = Math.Min(minX, sx[i]); maxX = Math.Max(maxX, sx[i]); minY = Math.Min(minY, sy[i]); maxY = Math.Max(maxY, sy[i]);
        }
        double span = Math.Max(Math.Max(maxX - minX, maxY - minY), 1e-9), scale = size * 0.84 / span;
        double cx = (minX + maxX) / 2, cy = (minY + maxY) / 2;
        var depth = new double[size * size]; Array.Fill(depth, double.MaxValue);
        var light = new Vec3(-0.35, 0.45, -0.82).Normalized();     // roughly from the viewer's upper left
        double r0 = (color >> 16 & 255), g0 = (color >> 8 & 255), b0 = (color & 255);
        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            int a = mesh.Indices[t * 3], b = mesh.Indices[t * 3 + 1], c = mesh.Indices[t * 3 + 2];
            double ax = size / 2 + (sx[a] - cx) * scale, ay = size / 2 - (sy[a] - cy) * scale;
            double bx = size / 2 + (sx[b] - cx) * scale, by = size / 2 - (sy[b] - cy) * scale;
            double qx = size / 2 + (sx[c] - cx) * scale, qy = size / 2 - (sy[c] - cy) * scale;
            double area = (bx - ax) * (qy - ay) - (by - ay) * (qx - ax);
            if (Math.Abs(area) < 1e-9) continue;
            // smooth shading from the vertex normals
            double Shade(int v)
            {
                var n = new Vec3(mesh.Normals[v * 3], mesh.Normals[v * 3 + 1], mesh.Normals[v * 3 + 2]);
                double lambert = Math.Max(0, -Vec3.Dot(n.Normalized(), light));
                double rim = Math.Abs(Vec3.Dot(n.Normalized(), forward));
                return 0.28 + 0.62 * lambert + 0.12 * rim;
            }
            double sa = Shade(a), sb = Shade(b), sc = Shade(c);
            int x0 = Math.Max(0, (int)Math.Floor(Math.Min(ax, Math.Min(bx, qx)))), x1 = Math.Min(size - 1, (int)Math.Ceiling(Math.Max(ax, Math.Max(bx, qx))));
            int y0 = Math.Max(0, (int)Math.Floor(Math.Min(ay, Math.Min(by, qy)))), y1 = Math.Min(size - 1, (int)Math.Ceiling(Math.Max(ay, Math.Max(by, qy))));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    double px = x + 0.5, py = y + 0.5;
                    double w0 = ((bx - px) * (qy - py) - (by - py) * (qx - px)) / area;
                    double w1 = ((qx - px) * (ay - py) - (qy - py) * (ax - px)) / area;
                    double w2 = 1 - w0 - w1;
                    if (w0 < -0.001 || w1 < -0.001 || w2 < -0.001) continue;
                    double z = w0 * sz[a] + w1 * sz[b] + w2 * sz[c];
                    int di = y * size + x;
                    if (z >= depth[di]) continue;
                    depth[di] = z;
                    double k = Math.Clamp(w0 * sa + w1 * sb + w2 * sc, 0, 1.2);
                    int o = di * 4;
                    pixels[o] = (byte)Math.Min(255, b0 * k); pixels[o + 1] = (byte)Math.Min(255, g0 * k); pixels[o + 2] = (byte)Math.Min(255, r0 * k); pixels[o + 3] = 255;
                }
        }
        return pixels;
    }
}
