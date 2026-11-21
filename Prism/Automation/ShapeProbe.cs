using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Prism.Automation;

/// <summary>Numbers and text pictures of a triangle mesh, for readers that cannot look at the viewport: cross-sections along an axis and ASCII silhouettes.
/// Works on any tessellated object (Freeform, solid, surface, mesh). Pure geometry, no application state.</summary>
public static class ShapeProbe
{
    public sealed record Section(double At, bool Empty, double MinU, double MaxU, double MinV, double MaxV, double Area, double CentroidU, double CentroidV, int Loops);

    /// <summary>The two world axes that span a section perpendicular to <paramref name="axis"/> (0 = X, 1 = Y, 2 = Z), in right-handed order.</summary>
    public static (int u, int v) PlaneAxes(int axis) => axis switch { 0 => (1, 2), 1 => (2, 0), _ => (0, 1) };

    static double C(float[] p, int vertex, int axis) => p[vertex * 3 + axis];

    /// <summary>Slice the mesh with the plane axis = at. Extents come from the cut outline; area and centroid are signed sums over the cut
    /// segments, so they are the true section area for a closed surface (holes subtract) and only indicative for an open one.</summary>
    public static Section Slice(float[] pos, int[] tri, int axis, double at)
    {
        var (au, av) = PlaneAxes(axis);
        double minU = double.MaxValue, maxU = double.MinValue, minV = double.MaxValue, maxV = double.MinValue;
        double twiceArea = 0, cu = 0, cv = 0; int segments = 0;
        var pts = new (double u, double v)[2]; var ends = new List<(double u, double v)>(3);
        for (int t = 0; t + 2 < tri.Length; t += 3)
        {
            int i0 = tri[t], i1 = tri[t + 1], i2 = tri[t + 2];
            double h0 = C(pos, i0, axis) - at, h1 = C(pos, i1, axis) - at, h2 = C(pos, i2, axis) - at;
            // a vertex exactly on the plane counts as above it, so a cut through a vertex neither doubles nor loses a segment
            bool s0 = h0 >= 0, s1 = h1 >= 0, s2 = h2 >= 0;
            if (s0 == s1 && s1 == s2) continue;
            ends.Clear();
            void Cross(int a, int b, double ha, double hb)
            {
                if ((ha >= 0) == (hb >= 0)) return;
                double k = ha / (ha - hb);
                ends.Add((C(pos, a, au) + (C(pos, b, au) - C(pos, a, au)) * k, C(pos, a, av) + (C(pos, b, av) - C(pos, a, av)) * k));
            }
            Cross(i0, i1, h0, h1); Cross(i1, i2, h1, h2); Cross(i2, i0, h2, h0);
            if (ends.Count != 2) continue;
            pts[0] = ends[0]; pts[1] = ends[1];
            // orient the segment with the triangle interior on its left, seen from +axis: direction = planeNormal x triangleNormal
            var n = TriNormal(pos, i0, i1, i2);
            double nu = n[au], nv = n[av];
            // (0,0,1) x (nx,ny,nz) in the (u,v) plane is (-nv, nu)
            double du = pts[1].u - pts[0].u, dv = pts[1].v - pts[0].v;
            if (du * -nv + dv * nu < 0) (pts[0], pts[1]) = (pts[1], pts[0]);
            double cross = pts[0].u * pts[1].v - pts[1].u * pts[0].v;
            twiceArea += cross; cu += (pts[0].u + pts[1].u) * cross; cv += (pts[0].v + pts[1].v) * cross; segments++;
            foreach (var p in pts) { minU = Math.Min(minU, p.u); maxU = Math.Max(maxU, p.u); minV = Math.Min(minV, p.v); maxV = Math.Max(maxV, p.v); }
        }
        if (segments == 0) return new Section(at, true, 0, 0, 0, 0, 0, 0, 0, 0);
        double area = twiceArea / 2;
        double ccu = Math.Abs(twiceArea) > 1e-12 ? cu / (3 * twiceArea) : (minU + maxU) / 2, ccv = Math.Abs(twiceArea) > 1e-12 ? cv / (3 * twiceArea) : (minV + maxV) / 2;
        return new Section(at, false, minU, maxU, minV, maxV, area, ccu, ccv, CountLoops(pos, tri, axis, at));
    }

    static double[] TriNormal(float[] p, int a, int b, int c)
    {
        double ux = C(p, b, 0) - C(p, a, 0), uy = C(p, b, 1) - C(p, a, 1), uz = C(p, b, 2) - C(p, a, 2);
        double vx = C(p, c, 0) - C(p, a, 0), vy = C(p, c, 1) - C(p, a, 1), vz = C(p, c, 2) - C(p, a, 2);
        return [uy * vz - uz * vy, uz * vx - ux * vz, ux * vy - uy * vx];
    }

    /// <summary>Number of separate closed outlines in the cut: counts connected groups of cut segments by their shared mesh edges.</summary>
    static int CountLoops(float[] pos, int[] tri, int axis, double at)
    {
        // display meshes repeat a vertex for every face that uses it, so neighbours are found by position, not by index
        var (lo, hi) = Bounds(pos);
        double cell = Math.Max(Math.Max(hi[0] - lo[0], Math.Max(hi[1] - lo[1], hi[2] - lo[2])) * 1e-6, 1e-9);
        var weld = new Dictionary<(long, long, long), int>();
        int Id(int vertex)
        {
            var key = ((long)Math.Round(C(pos, vertex, 0) / cell), (long)Math.Round(C(pos, vertex, 1) / cell), (long)Math.Round(C(pos, vertex, 2) / cell));
            if (!weld.TryGetValue(key, out var id)) weld[key] = id = weld.Count;
            return id;
        }
        var parent = new Dictionary<long, long>();
        long Find(long x) { while (parent.TryGetValue(x, out var p) && p != x) { parent[x] = parent[p]; x = parent[x]; } return x; }
        void Union(long a, long b) { parent.TryAdd(a, a); parent.TryAdd(b, b); var ra = Find(a); var rb = Find(b); if (ra != rb) parent[ra] = rb; }
        static long Key(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
        for (int t = 0; t + 2 < tri.Length; t += 3)
        {
            var v = new[] { tri[t], tri[t + 1], tri[t + 2] };
            var cut = new List<long>();
            for (int k = 0; k < 3; k++)
            {
                int a = v[k], b = v[(k + 1) % 3];
                if ((C(pos, a, axis) - at >= 0) != (C(pos, b, axis) - at >= 0)) cut.Add(Key(Id(a), Id(b)));
            }
            if (cut.Count == 2) Union(cut[0], cut[1]);
        }
        var roots = new HashSet<long>();
        foreach (var k in parent.Keys.ToList()) roots.Add(Find(k));
        return roots.Count;
    }

    public static (double[] min, double[] max) Bounds(float[] pos)
    {
        double[] lo = [double.MaxValue, double.MaxValue, double.MaxValue], hi = [double.MinValue, double.MinValue, double.MinValue];
        for (int i = 0; i < pos.Length; i += 3) for (int k = 0; k < 3; k++) { lo[k] = Math.Min(lo[k], pos[i + k]); hi[k] = Math.Max(hi[k], pos[i + k]); }
        return (lo, hi);
    }

    /// <summary>Evenly spaced cut positions strictly inside the extent of the axis (the end caps are not cut: they are flat and add nothing).</summary>
    public static double[] EvenOffsets(double lo, double hi, int count) =>
        Enumerable.Range(0, Math.Max(count, 0)).Select(i => lo + (hi - lo) * (i + 0.5) / count).ToArray();

    public sealed record Picture(string View, string Axes, double MmPerColumn, double MmPerRow, double Left, double Top, List<string> Rows);

    /// <summary>An ASCII silhouette seen along an axis ("front" along +Y: x right, z up; "right": y right, z up; "top": x right, y up).
    /// Cells are twice as tall as wide so the picture keeps its proportions. With <paramref name="shade"/> the character shows how near the surface is.</summary>
    public static Picture Silhouette(float[] pos, int[] tri, string view, int columns, bool shade, int maxRows = 26)
    {
        var (uAxis, vAxis, dAxis, axes) = view switch
        {
            "front" => (0, 2, 1, "x right, z up, looking along +y"),
            "right" => (1, 2, 0, "y right, z up, looking along -x"),
            "top" => (0, 1, 2, "x right, y up, looking down"),
            _ => throw new ArgumentException($"Unknown view '{view}'. Use front, right or top."),
        };
        var (lo, hi) = Bounds(pos);
        // a flat shape seen edge-on has no extent along one axis: give every extent at least 2% of the largest so the picture stays finite
        double biggest = Math.Max(Math.Max(hi[0] - lo[0], hi[1] - lo[1]), Math.Max(hi[2] - lo[2], 1e-6));
        double w = Math.Max(hi[uAxis] - lo[uAxis], biggest * 0.02), h = Math.Max(hi[vAxis] - lo[vAxis], biggest * 0.02);
        columns = Math.Clamp(columns, 12, 200); maxRows = Math.Clamp(maxRows, 6, 120);
        // fit both limits: no wider than `columns`, no taller than `maxRows` (a tall thin shape gets a coarser grid, not a long print-out)
        double mmPerCol = Math.Max(w / columns, h / (2.0 * maxRows)), mmPerRow = mmPerCol * 2;
        columns = Math.Max(1, (int)Math.Ceiling(w / mmPerCol - 1e-9));
        int rows = Math.Max(1, (int)Math.Ceiling(h / mmPerRow - 1e-9));
        var depth = new double[rows * columns]; Array.Fill(depth, double.NaN);
        // depth axis: for front the camera sits at -y (small y is near); right sits at +x (large x near); top sits at +z
        bool nearIsLow = view == "front";
        // allocated once: a stackalloc inside this loop is not released until the method returns and overflows the stack on big meshes
        Span<double> pu = stackalloc double[3], pv = stackalloc double[3], pd = stackalloc double[3];
        for (int t = 0; t + 2 < tri.Length; t += 3)
        {
            for (int k = 0; k < 3; k++) { pu[k] = (C(pos, tri[t + k], uAxis) - lo[uAxis]) / mmPerCol; pv[k] = (hi[vAxis] - C(pos, tri[t + k], vAxis)) / mmPerRow; pd[k] = C(pos, tri[t + k], dAxis); }
            int x0 = Math.Max(0, (int)Math.Floor(Math.Min(pu[0], Math.Min(pu[1], pu[2])) - 0.5)), x1 = Math.Min(columns - 1, (int)Math.Ceiling(Math.Max(pu[0], Math.Max(pu[1], pu[2])) - 0.5));
            int y0 = Math.Max(0, (int)Math.Floor(Math.Min(pv[0], Math.Min(pv[1], pv[2])) - 0.5)), y1 = Math.Min(rows - 1, (int)Math.Ceiling(Math.Max(pv[0], Math.Max(pv[1], pv[2])) - 0.5));
            double den = (pv[1] - pv[2]) * (pu[0] - pu[2]) + (pu[2] - pu[1]) * (pv[0] - pv[2]);
            if (Math.Abs(den) < 1e-12)
            {
                // an edge-on triangle still blocks light: mark the cells it passes through so thin walls do not vanish
                for (int s = 0; s <= 8; s++)
                {
                    int cx = (int)Math.Floor(pu[0] + (pu[1] - pu[0]) * s / 8.0 + (pu[2] - pu[0]) * s / 16.0), cy = (int)Math.Floor(pv[0] + (pv[1] - pv[0]) * s / 8.0 + (pv[2] - pv[0]) * s / 16.0);
                    if (cx >= 0 && cx < columns && cy >= 0 && cy < rows && double.IsNaN(depth[cy * columns + cx])) depth[cy * columns + cx] = (pd[0] + pd[1] + pd[2]) / 3;
                }
                continue;
            }
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    double px = x + 0.5, py = y + 0.5;
                    double a = ((pv[1] - pv[2]) * (px - pu[2]) + (pu[2] - pu[1]) * (py - pv[2])) / den;
                    double b = ((pv[2] - pv[0]) * (px - pu[2]) + (pu[0] - pu[2]) * (py - pv[2])) / den;
                    double c = 1 - a - b;
                    if (a < -0.02 || b < -0.02 || c < -0.02) continue;
                    double d = a * pd[0] + b * pd[1] + c * pd[2], old = depth[y * columns + x];
                    if (double.IsNaN(old) || (nearIsLow ? d < old : d > old)) depth[y * columns + x] = d;
                }
        }
        double dMin = hi[dAxis], dMax = lo[dAxis];
        foreach (var d in depth) if (!double.IsNaN(d)) { dMin = Math.Min(dMin, d); dMax = Math.Max(dMax, d); }
        const string ramp = "@%#*+=-:."; // nearest to farthest
        var lines = new List<string>(rows);
        for (int y = 0; y < rows; y++)
        {
            var sb = new StringBuilder(columns);
            for (int x = 0; x < columns; x++)
            {
                double d = depth[y * columns + x];
                if (double.IsNaN(d)) { sb.Append(' '); continue; }
                if (!shade) { sb.Append('#'); continue; }
                double f = dMax - dMin < 1e-9 ? 0 : (nearIsLow ? d - dMin : dMax - d) / (dMax - dMin);
                sb.Append(ramp[Math.Clamp((int)(f * (ramp.Length - 1) + 0.5), 0, ramp.Length - 1)]);
            }
            lines.Add(sb.ToString().TrimEnd());
        }
        return new Picture(view, axes, mmPerCol, mmPerRow, lo[uAxis], hi[vAxis], lines);
    }
}
