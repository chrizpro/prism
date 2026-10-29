using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Kernel;

namespace Prism.Core.Cam;

public sealed class SliceSettings
{
    public double LayerHeight = 0.2, FirstLayer = 0.3, LineWidth = 0.45;
    public int Walls = 3, TopLayers = 4, BottomLayers = 4, BrimLines = 0;
    public double InfillPercent = 15;
    public bool GridInfill = false;
    public double PrintSpeed = 50, FirstLayerSpeed = 20, TravelSpeed = 150, Retract = 1.0, RetractSpeed = 35;
    public int NozzleTemp = 205, BedTemp = 60, FanPercent = 100;
    public double FilamentDia = 1.75, Flow = 1.0;
    public double BedX = 220, BedY = 220;
    public bool CenterOnBed = true;
}

/// <summary>
/// Slices solids into printable layers: perimeters (exact offsets), solid top and bottom skin, and sparse infill (found on a fine grid, so
/// the skin lands exactly where a surface faces up or down). No supports yet.
/// </summary>
public static class Slicer
{
    public sealed record Result(ToolpathInfo Path, int Layers, double MinutesEstimate, double FilamentMm, Vec3 Min, Vec3 Max, List<string> Notes);

    sealed class Layer
    {
        public double Z;
        public List<List<Vec3>> Walls = [];          // innermost first
        public List<List<Vec3>> Brim = [];
        public List<List<Vec3>> InfillZone = [];     // loops bounding the area inside the walls
    }

    public static Result Slice(IReadOnlyList<MeshData> meshes, SliceSettings s, Action<string>? progress = null)
    {
        var notes = new List<string>();
        // gather triangles
        var tris = new List<float[]>(); Vec3 mn = new(double.MaxValue, double.MaxValue, double.MaxValue), mx = -mn;
        foreach (var m in meshes)
        {
            for (int t = 0; t + 2 < m.Indices.Length; t += 3)
            {
                var tri = new float[9];
                for (int c = 0; c < 3; c++) { int vi = m.Indices[t + c] * 3; tri[3 * c] = m.Positions[vi]; tri[3 * c + 1] = m.Positions[vi + 1]; tri[3 * c + 2] = m.Positions[vi + 2]; }
                tris.Add(tri);
            }
            mn = new(Math.Min(mn.X, m.Min.X), Math.Min(mn.Y, m.Min.Y), Math.Min(mn.Z, m.Min.Z)); mx = new(Math.Max(mx.X, m.Max.X), Math.Max(mx.Y, m.Max.Y), Math.Max(mx.Z, m.Max.Z));
        }
        if (tris.Count == 0) throw new InvalidOperationException("There is nothing to slice: pick solid bodies.");
        double height = mx.Z - mn.Z;
        if (height < s.FirstLayer) throw new InvalidOperationException("The model is thinner than the first layer.");

        // layer heights
        var zs = new List<double>(); double zTop = mn.Z + s.FirstLayer; zs.Add(zTop);
        while (zTop + s.LayerHeight <= mx.Z + 1e-6) { zTop += s.LayerHeight; zs.Add(zTop); }
        if (mx.Z - (zs[^1]) > s.LayerHeight * 0.5) zs.Add(mx.Z);
        int nl = zs.Count;

        // triangles sorted by lowest z for quick lookup
        var zmin = tris.Select(t => Math.Min(t[2], Math.Min(t[5], t[8]))).ToArray();
        var zmax = tris.Select(t => Math.Max(t[2], Math.Max(t[5], t[8]))).ToArray();

        // raster grid for infill and skin decisions
        double cell = Math.Max(s.LineWidth / 2, Math.Max(mx.X - mn.X, mx.Y - mn.Y) / 1200.0);
        double gx0 = mn.X - 2, gy0 = mn.Y - 2;
        int nx = (int)Math.Ceiling((mx.X - mn.X + 4) / cell) + 1, ny = (int)Math.Ceiling((mx.Y - mn.Y + 4) / cell) + 1;
        var zones = new bool[nl][];
        var layers = new Layer[nl];

        for (int li = 0; li < nl; li++)
        {
            double zSlice = li == 0 ? mn.Z + s.FirstLayer / 2 : (zs[li] + (li > 0 ? zs[li - 1] : mn.Z)) / 2;
            var layer = layers[li] = new Layer { Z = zs[li] };
            var loops = SliceLoops(tris, zmin, zmax, zSlice);
            zones[li] = new bool[nx * ny];
            if (loops.Count == 0) continue;
            loops = loops.Select(l => CamGeom.SimplifyClosed(l, 0.02)).ToList();
            var face = CamGeom.FaceFromLoops(loops);
            if (face == null) { notes.Add($"Layer {li + 1}: the outline could not be built (the model may not be watertight)."); continue; }
            // walls: first (outer) wall is half a line width in; each next one a full line width further in
            for (int w = 0; w < s.Walls; w++)
            {
                var offs = CamGeom.Offset(face, -(s.LineWidth / 2 + w * s.LineWidth), zs[li]);
                if (offs.Count == 0) break;
                layer.Walls.AddRange(offs.Select(l => Tag(l, w)));
                if (w == s.Walls - 1) layer.InfillZone.AddRange(CamGeom.Offset(face, -(s.LineWidth / 2 + (s.Walls - 1) * s.LineWidth + s.LineWidth / 2), zs[li]));
            }
            if (s.Walls == 0) layer.InfillZone.AddRange(loops);
            if (li == 0 && s.BrimLines > 0)
            {
                var outer = OuterLoops(loops);
                var oface = CamGeom.FaceFromLoops(outer);
                if (oface != null) for (int b = 0; b < s.BrimLines; b++) layer.Brim.AddRange(CamGeom.Offset(oface, s.LineWidth / 2 + b * s.LineWidth, zs[li]));
            }
            Fill(zones[li], layer.InfillZone, gx0, gy0, cell, nx, ny);
            if (li % 10 == 0) progress?.Invoke($"Slicing layer {li + 1} of {nl}");
        }

        // build the toolpath
        var moves = new List<double>(); var starts = new List<int>();
        double px = double.NaN, py = double.NaN;
        void Go(double x, double y, double z, MoveKind k) { moves.Add(x); moves.Add(y); moves.Add(z); moves.Add((int)k); px = x; py = y; }
        void Travel(double x, double y, double z) { if (double.IsNaN(px) || Math.Abs(px - x) > 1e-6 || Math.Abs(py - y) > 1e-6 || moves.Count == 0) Go(x, y, z, MoveKind.Rapid); }
        void Loop(List<Vec3> l, double z, MoveKind k)
        {
            // begin at the vertex nearest to where the nozzle is, so travel stays short
            int best = 0; if (!double.IsNaN(px)) { double bd = double.MaxValue; for (int i = 0; i < l.Count; i++) { double d = (l[i].X - px) * (l[i].X - px) + (l[i].Y - py) * (l[i].Y - py); if (d < bd) { bd = d; best = i; } } }
            Travel(l[best].X, l[best].Y, z);
            for (int i = 1; i <= l.Count; i++) { var q = l[(best + i) % l.Count]; Go(q.X, q.Y, z, k); }
        }

        int zoneTop = Math.Max(s.TopLayers, 0), zoneBottom = Math.Max(s.BottomLayers, 0);
        double spacingSparse = s.InfillPercent > 0.5 ? s.LineWidth / (s.InfillPercent / 100.0) * (s.GridInfill ? 2 : 1) : 0;
        for (int li = 0; li < nl; li++)
        {
            var layer = layers[li]; starts.Add(moves.Count / 4);
            double z = layer.Z;
            if (moves.Count == 0) Go(layer.Walls.FirstOrDefault()?[0].X ?? mn.X, layer.Walls.FirstOrDefault()?[0].Y ?? mn.Y, z, MoveKind.Rapid);
            foreach (var b in layer.Brim.AsEnumerable().Reverse()) Loop(b, z, MoveKind.Brim);
            // inner walls first, outer wall last
            foreach (var w in WallsInnerToOuter(layer, s)) Loop(w.loop, z, w.outer ? MoveKind.OuterWall : MoveKind.InnerWall);

            // skin: cells in this layer that are not covered by the next / previous layers
            var skin = new bool[nx * ny]; var sparse = new bool[nx * ny];
            var cur = zones[li];
            for (int c = 0; c < cur.Length; c++)
            {
                if (!cur[c]) continue;
                bool top = false, bottom = false;
                for (int j = 1; j <= zoneTop && !top; j++) top = li + j >= nl || !zones[li + j][c];
                for (int j = 1; j <= zoneBottom && !bottom; j++) bottom = li - j < 0 || !zones[li - j][c];
                if (top || bottom) skin[c] = true; else sparse[c] = true;
            }
            bool alt = li % 2 == 0;
            Lines(skin, gx0, gy0, cell, nx, ny, s.LineWidth * 0.95, alt, z, MoveKind.Skin, Go, Travel);
            if (spacingSparse > 0)
            {
                Lines(sparse, gx0, gy0, cell, nx, ny, spacingSparse, alt, z, MoveKind.Infill, Go, Travel);
                if (s.GridInfill) Lines(sparse, gx0, gy0, cell, nx, ny, spacingSparse, !alt, z, MoveKind.Infill, Go, Travel);
            }
            if (li % 10 == 0) progress?.Invoke($"Planning layer {li + 1} of {nl}");
        }

        double offX = 0, offY = 0, offZ = -mn.Z;
        if (s.CenterOnBed) { offX = s.BedX / 2 - (mn.X + mx.X) / 2; offY = s.BedY / 2 - (mn.Y + mx.Y) / 2; }
        if (mx.X - mn.X > s.BedX || mx.Y - mn.Y > s.BedY) notes.Add($"The model is {mx.X - mn.X:0.#} × {mx.Y - mn.Y:0.#} mm, bigger than the {s.BedX:0.#} × {s.BedY:0.#} mm bed.");
        var pp = new PrintParams(s.LayerHeight, s.FirstLayer, s.LineWidth, s.FilamentDia, s.Flow, s.PrintSpeed, s.FirstLayerSpeed, s.TravelSpeed, s.Retract, s.RetractSpeed, s.NozzleTemp, s.BedTemp, s.FanPercent, s.BedX, s.BedY, offX, offY, offZ);
        var tp = new ToolpathInfo("3D print", "Nozzle", s.LineWidth, s.PrintSpeed * 60, s.PrintSpeed * 60, 0, mx.Z + 5, [.. moves], [.. starts], true, true, pp);
        return new Result(tp, nl, tp.EstimateMinutes(), Post.FilamentLength(tp, pp), mn, mx, notes);
    }

    // The walls were collected loop by loop; remember which wall number each loop came from (0 = outermost).
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<List<Vec3>, object> Depth = new();
    static List<Vec3> Tag(List<Vec3> l, int depth) { Depth.Remove(l); Depth.Add(l, depth); return l; }
    static IEnumerable<(List<Vec3> loop, bool outer)> WallsInnerToOuter(Layer layer, SliceSettings s)
    {
        var items = layer.Walls.Select(l => (l, d: Depth.TryGetValue(l, out var o) ? (int)o : 0)).ToList();
        foreach (var (l, d) in items.OrderByDescending(x => x.d)) yield return (l, d == 0);
    }

    // ------------------------------------------------------------------ slicing the mesh
    static List<List<Vec3>> SliceLoops(List<float[]> tris, float[] zmin, float[] zmax, double z)
    {
        var segs = new List<(Vec3 a, Vec3 b)>();
        for (int t = 0; t < tris.Count; t++)
        {
            if (zmin[t] > z || zmax[t] < z) continue;
            var tri = tris[t]; var pts = new List<Vec3>();
            for (int e = 0; e < 3; e++)
            {
                int i = e, j = (e + 1) % 3;
                double zi = tri[3 * i + 2], zj = tri[3 * j + 2];
                if ((zi < z) == (zj < z)) continue;
                double u = (z - zi) / (zj - zi);
                pts.Add(new Vec3(tri[3 * i] + u * (tri[3 * j] - tri[3 * i]), tri[3 * i + 1] + u * (tri[3 * j + 1] - tri[3 * i + 1]), z));
            }
            if (pts.Count == 2 && pts[0].DistanceTo(pts[1]) > 1e-9) segs.Add((pts[0], pts[1]));
        }
        // join the segments into loops through their end points
        const double q = 1e-4;
        (long, long) Key(Vec3 p) => ((long)Math.Round(p.X / q), (long)Math.Round(p.Y / q));
        var byStart = new Dictionary<(long, long), List<int>>();
        for (int i = 0; i < segs.Count; i++) { (byStart.TryGetValue(Key(segs[i].a), out var l) ? l : byStart[Key(segs[i].a)] = []).Add(i); (byStart.TryGetValue(Key(segs[i].b), out var l2) ? l2 : byStart[Key(segs[i].b)] = []).Add(i); }
        var used = new bool[segs.Count]; var loops = new List<List<Vec3>>();
        for (int i = 0; i < segs.Count; i++)
        {
            if (used[i]) continue;
            used[i] = true; var loop = new List<Vec3> { segs[i].a, segs[i].b }; var end = segs[i].b;
            while (true)
            {
                if (!byStart.TryGetValue(Key(end), out var cand)) break;
                int next = cand.FirstOrDefault(c => !used[c], -1); if (next < 0) break;
                used[next] = true;
                var (a, b) = segs[next]; var other = Key(a) == Key(end) ? b : a;
                if (Key(other) == Key(loop[0])) { end = other; break; }
                loop.Add(other); end = other;
            }
            if (loop.Count >= 3 && Key(end) == Key(loop[0])) loops.Add(loop);
        }
        return loops;
    }

    static List<List<Vec3>> OuterLoops(List<List<Vec3>> loops) =>
        loops.Where(l => !loops.Any(o => !ReferenceEquals(o, l) && Math.Abs(CamGeom.SignedArea(o)) > Math.Abs(CamGeom.SignedArea(l)) && CamGeom.Inside(o, l[0].X, l[0].Y))).ToList();

    // ------------------------------------------------------------------ raster fill and line generation
    static void Fill(bool[] mask, List<List<Vec3>> loops, double x0, double y0, double cell, int nx, int ny)
    {
        for (int iy = 0; iy < ny; iy++)
        {
            double y = y0 + (iy + 0.5) * cell; var xs = new List<double>();
            foreach (var l in loops)
                for (int i = 0, j = l.Count - 1; i < l.Count; j = i++)
                    if ((l[i].Y > y) != (l[j].Y > y)) xs.Add(l[i].X + (y - l[i].Y) / (l[j].Y - l[i].Y) * (l[j].X - l[i].X));
            xs.Sort();
            for (int k = 0; k + 1 < xs.Count; k += 2)
            {
                int a = Math.Max(0, (int)Math.Ceiling((xs[k] - x0) / cell - 0.5)), b = Math.Min(nx - 1, (int)Math.Floor((xs[k + 1] - x0) / cell - 0.5));
                for (int ix = a; ix <= b; ix++) mask[iy * nx + ix] = true;
            }
        }
    }

    /// <summary>Parallel lines through the true cells of a mask, as a zigzag. alongX: lines run in X (rows), otherwise in Y (columns).</summary>
    static void Lines(bool[] mask, double x0, double y0, double cell, int nx, int ny, double spacing, bool alongX, double z, MoveKind kind,
        Action<double, double, double, MoveKind> go, Action<double, double, double> travel)
    {
        int step = Math.Max(1, (int)Math.Round(spacing / cell));
        int outer = alongX ? ny : nx, inner = alongX ? nx : ny; bool forward = true;
        for (int o = step / 2; o < outer; o += step)
        {
            var runs = new List<(int a, int b)>(); int start = -1;
            for (int i = 0; i <= inner; i++)
            {
                bool on = i < inner && (alongX ? mask[o * nx + i] : mask[i * nx + o]);
                if (on && start < 0) start = i; else if (!on && start >= 0) { if (i - 1 - start >= 1) runs.Add((start, i - 1)); start = -1; }
            }
            if (!forward) runs.Reverse();
            foreach (var (a, b) in runs)
            {
                double p0 = (forward ? a : b) + 0.5, p1 = (forward ? b : a) + 0.5;
                double c = (o + 0.5) * cell;
                double xa = alongX ? x0 + p0 * cell : x0 + c, ya = alongX ? y0 + c : y0 + p0 * cell, xb = alongX ? x0 + p1 * cell : x0 + c, yb = alongX ? y0 + c : y0 + p1 * cell;
                travel(xa, ya, z); go(xb, yb, z, kind);
            }
            if (runs.Count > 0) forward = !forward;
        }
    }
}
