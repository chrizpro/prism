using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using Prism.Core;
using Prism.Kernel;

namespace Prism.Rendering;

/// <summary>A snapshot of actual viewport mesh data. No live session or native handles are used by the worker.</summary>
public sealed record StudioPreviewSource(MeshData Mesh, int[] Edges, int[] Faces, bool Error, string Caption, uint? HighlightColor = null, bool MonoScalars = false, bool Perspective = false);
public sealed record StudioPreviewPixels(byte[] Bgra, int Width, int Height, int HighlightPixels);

/// <summary>Small cached diagnostic renders, using the application's tessellation and projection camera.</summary>
public static class StudioPreview
{
    public static StudioPreviewPixels Render(StudioPreviewSource source, bool light, CancellationToken cancel = default)
    {
        const int width = 560, height = 320;
        var mesh = source.Mesh;
        var pixels = new byte[width * height * 4];
        var depth = new double[width * height]; Array.Fill(depth, double.PositiveInfinity);
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            int i = (y * width + x) * 4;
            byte shade = (byte)((light ? 222 : 36) + (light ? -12 : 9) * y / height);
            pixels[i] = (byte)Math.Min(255, shade + (light ? 7 : 12)); pixels[i + 1] = (byte)Math.Min(255, shade + 5); pixels[i + 2] = shade; pixels[i + 3] = 255;
        }
        var selectedEdges = source.Edges.ToHashSet(); var selectedFaces = source.Faces.ToHashSet();
        var focusPoints = new List<Vec3>();
        for (int i = 0; i < mesh.SegmentCount; i++) if (selectedEdges.Contains(mesh.SegEdge[i]))
        { focusPoints.Add(P(mesh.EdgePoints, i * 6)); focusPoints.Add(P(mesh.EdgePoints, i * 6 + 3)); }
        for (int t = 0; t < mesh.TriangleCount; t++) if (selectedFaces.Contains(mesh.TriFace[t]))
            for (int j = 0; j < 3; j++) focusPoints.Add(P(mesh.Positions, mesh.Indices[t * 3 + j] * 3));
        var min = mesh.Min; var max = mesh.Max;
        bool focused = focusPoints.Count > 0;
        if (focused)
        {
            var lo = new Vec3(focusPoints.Min(p => p.X), focusPoints.Min(p => p.Y), focusPoints.Min(p => p.Z));
            var hi = new Vec3(focusPoints.Max(p => p.X), focusPoints.Max(p => p.Y), focusPoints.Max(p => p.Z));
            if ((hi - lo).Length < (max - min).Length * .9)
            {
                var center = (lo + hi) * .5;
                double span = Math.Max((hi - lo).Length * .45, (max - min).Length * .12);
                min = center - new Vec3(span, span, span); max = center + new Vec3(span, span, span);
            }
            else focused = false;
        }
        var camera = new Camera { Width = width, Height = height, Orthographic = !source.Perspective };
        if (focused)
        {
            var outward = (min + max - mesh.Min - mesh.Max) * .5;
            if (focusPoints.Count >= 2)
            {
                // Aim between the faces adjacent to the selected edge, rather than
                // assuming the body's bounding-box center identifies its outside.
                var origin = focusPoints[0]; var axis = focusPoints[1] - origin;
                double length = axis.Length;
                if (length > 1e-8)
                {
                    axis /= length; var sum = Vec3.Zero; var faces = new HashSet<int>();
                    double tolerance = Math.Max((mesh.Max - mesh.Min).Length * 1e-5, 1e-6);
                    for (int t = 0; t < mesh.TriangleCount; t++)
                    {
                        if ((t & 255) == 0) cancel.ThrowIfCancellationRequested();
                        if (faces.Contains(mesh.TriFace[t])) continue;
                        int near = 0;
                        for (int j = 0; j < 3; j++)
                        {
                            var delta = P(mesh.Positions, mesh.Indices[t * 3 + j] * 3) - origin;
                            double along = Vec3.Dot(delta, axis);
                            if (along >= -tolerance && along <= length + tolerance && (delta - axis * along).Length < tolerance) near++;
                        }
                        if (near >= 2)
                        {
                            faces.Add(mesh.TriFace[t]); var n = P(mesh.Normals, mesh.Indices[t * 3] * 3);
                            if (n.Length > .1) sum += n.Normalized();
                        }
                    }
                    if (sum.Length > .1) outward = sum;
                }
            }
            if (outward.Length > (mesh.Max - mesh.Min).Length * .06)
                camera.LookAlong((outward.Normalized() + new Vec3(.1, -.3, .25)).Normalized());
        }
        camera.Fit(min, max);
        if (focused) camera.Distance *= .72;
        var vp = camera.ViewProjection;
        var projected = new (double x, double y, double depth)?[mesh.VertexCount];
        for (int i = 0; i < projected.Length; i++) projected[i] = camera.Project(P(mesh.Positions, i * 3), vp);
        var view = V(camera.EyeDirection);
        var lightPosition = V(camera.Target) + V(camera.EyeDirection) * (float)camera.Distance * .45f + V(camera.Up) * (float)camera.Distance * .5f - V(camera.Right) * (float)camera.Distance * .4f;
        int highlighted = 0;
        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            if ((t & 255) == 0) cancel.ThrowIfCancellationRequested();
            int ia = mesh.Indices[t * 3], ib = mesh.Indices[t * 3 + 1], ic = mesh.Indices[t * 3 + 2];
            if (projected[ia] is not { } a || projected[ib] is not { } b || projected[ic] is not { } c) continue;
            double area = Cross(b.x - a.x, b.y - a.y, c.x - a.x, c.y - a.y);
            if (Math.Abs(area) < .0001) continue;
            int x0 = Math.Max(0, (int)Math.Floor(Math.Min(a.x, Math.Min(b.x, c.x)))), x1 = Math.Min(width - 1, (int)Math.Ceiling(Math.Max(a.x, Math.Max(b.x, c.x))));
            int y0 = Math.Max(0, (int)Math.Floor(Math.Min(a.y, Math.Min(b.y, c.y)))), y1 = Math.Min(height - 1, (int)Math.Ceiling(Math.Max(a.y, Math.Max(b.y, c.y))));
            var na = V(P(mesh.Normals, ia * 3)); var nb = V(P(mesh.Normals, ib * 3)); var nc = V(P(mesh.Normals, ic * 3));
            var pa = V(P(mesh.Positions, ia * 3)); var pb = V(P(mesh.Positions, ib * 3)); var pc = V(P(mesh.Positions, ic * 3));
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
            {
                double u = Cross(b.x - x - .5, b.y - y - .5, c.x - x - .5, c.y - y - .5) / area;
                double v = Cross(c.x - x - .5, c.y - y - .5, a.x - x - .5, a.y - y - .5) / area;
                double w = 1 - u - v; if (u < 0 || v < 0 || w < 0) continue;
                double z = u * a.depth + v * b.depth + w * c.depth; int index = y * width + x;
                if (z >= depth[index]) continue; depth[index] = z;
                var normal = Vector3.Normalize(na * (float)u + nb * (float)v + nc * (float)w);
                if (Vector3.Dot(normal, view) < 0) normal = -normal;
                var localLight = Vector3.Normalize(lightPosition - (pa * (float)u + pb * (float)v + pc * (float)w));
                float diffuse = Math.Max(0, Vector3.Dot(normal, localLight));
                var localHalf = Vector3.Normalize(localLight + view);
                float spec = (float)Math.Pow(Math.Max(0, Vector3.Dot(normal, localHalf)), 42);
                double intensity = .38 + diffuse * .48 + spec * .4;
                byte grey = (byte)Math.Clamp(intensity * 230, 0, 255);
                Put(index, grey, (byte)Math.Min(255, grey + 5), (byte)Math.Min(255, grey + 12));
                if (mesh.Scalars is { } scalars && scalars.Length >= mesh.VertexCount)
                    Heat(index, scalars[ia] * u + scalars[ib] * v + scalars[ic] * w, intensity);
                if (selectedFaces.Contains(mesh.TriFace[t])) { Tint(index, .34); highlighted++; }
            }
        }
        for (int segment = 0; segment < mesh.SegmentCount; segment++)
        {
            if ((segment & 255) == 0) cancel.ThrowIfCancellationRequested();
            var a = camera.Project(P(mesh.EdgePoints, segment * 6), vp); var b = camera.Project(P(mesh.EdgePoints, segment * 6 + 3), vp);
            if (a is not { } from || b is not { } to) continue;
            bool selected = selectedEdges.Contains(mesh.SegEdge[segment]);
            int steps = (int)Math.Min(2000, Math.Max(Math.Abs(to.x - from.x), Math.Abs(to.y - from.y))) + 1;
            for (int j = 0; j <= steps; j++)
            {
                double f = (double)j / steps; int x = (int)Math.Round(from.x + (to.x - from.x) * f), y = (int)Math.Round(from.y + (to.y - from.y) * f);
                if (x < 0 || y < 0 || x >= width || y >= height) continue;
                int index = y * width + x; double z = from.depth + (to.depth - from.depth) * f;
                bool visible = z <= depth[index] + .00003;
                if (!visible) continue;
                if (selected)
                {
                    for (int dy = -2; dy <= 2; dy++) for (int dx = -2; dx <= 2; dx++)
                        if (x + dx >= 0 && x + dx < width && y + dy >= 0 && y + dy < height) { Tint((y + dy) * width + x + dx, 1); highlighted++; }
                }
                else if (mesh.TriangleCount == 0) Put(index, 113, 175, 207);
                else
                {
                    int p = index * 4; pixels[p] = (byte)(pixels[p] * .64); pixels[p + 1] = (byte)(pixels[p + 1] * .64); pixels[p + 2] = (byte)(pixels[p + 2] * .64);
                }
            }
        }
        return new(pixels, width, height, highlighted);
        void Put(int i, byte r, byte g, byte b) { int p = i * 4; pixels[p] = b; pixels[p + 1] = g; pixels[p + 2] = r; }
        // Result colors: blue (low) through green and yellow to red (high), or black to white for stripe patterns.
        void Heat(int i, double value, double intensity)
        {
            value = Math.Clamp(value, 0, 1);
            double shade = Math.Clamp(.55 + intensity * .6, 0, 1.25);
            if (source.MonoScalars) { byte g = (byte)Math.Clamp((value < .5 ? 28 : 238) * Math.Min(1, shade), 0, 255); Put(i, g, g, g); return; }
            (double r, double g, double b)[] stops = [(49, 84, 198), (52, 170, 214), (94, 190, 100), (240, 210, 60), (220, 60, 50)];
            double position = value * (stops.Length - 1); int k = Math.Min(stops.Length - 2, (int)position); double f = position - k;
            double R = stops[k].r + (stops[k + 1].r - stops[k].r) * f, G = stops[k].g + (stops[k + 1].g - stops[k].g) * f, B = stops[k].b + (stops[k + 1].b - stops[k].b) * f;
            Put(i, (byte)Math.Clamp(R * shade, 0, 255), (byte)Math.Clamp(G * shade, 0, 255), (byte)Math.Clamp(B * shade, 0, 255));
        }
        void Tint(int i, double amount)
        {
            int p = i * 4; uint color = source.HighlightColor ?? (source.Error ? 0xFFF75F64u : 0xFFEEB75Au);
            byte r = (byte)(color >> 16), g = (byte)(color >> 8), b = (byte)color;
            Put(i, (byte)(pixels[p + 2] * (1 - amount) + r * amount), (byte)(pixels[p + 1] * (1 - amount) + g * amount), (byte)(pixels[p] * (1 - amount) + b * amount));
        }
    }
    static double Cross(double ax, double ay, double bx, double by) => ax * by - ay * bx;
    static Vec3 P(float[] data, int index) => new(data[index], data[index + 1], data[index + 2]);
    static Vector3 V(Vec3 value) => new((float)value.X, (float)value.Y, (float)value.Z);
}
