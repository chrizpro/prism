using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Kernel;

namespace Prism.Core.Cam;

/// <summary>
/// Adaptive clearing: the tool follows a path that keeps its load (how much of the cutter is buried in material) close to a chosen value,
/// instead of cutting full slots and sharp corners. It works on a grid that remembers which material is left.
/// </summary>
public static class AdaptiveClearing
{
    /// <summary>One piece of the path: a new start (the tool plunges there), a cutting move, or a link (travel through cleared ground).</summary>
    public enum Op : byte { Start, Cut, Link }

    public readonly record struct Step(Op Op, double X, double Y);

    /// <summary>Fraction of the cutter's circle that is buried when it takes a bite of this radial depth (a circular segment).</summary>
    public static double LoadFraction(double radius, double bite)
    {
        bite = Math.Clamp(bite, 1e-6, 2 * radius);
        double h = bite;
        double area = radius * radius * Math.Acos((radius - h) / radius) - (radius - h) * Math.Sqrt(Math.Max(2 * radius * h - h * h, 0));
        return area / (Math.PI * radius * radius);
    }

    static bool InsideAny(IReadOnlyList<List<Vec3>> loops, double x, double y)
    {
        int n = 0; foreach (var l in loops) if (CamGeom.Inside(l, x, y)) n++;
        return (n & 1) == 1;
    }

    /// <param name="region">The outlines of the area to clear (islands are loops inside loops).</param>
    /// <param name="reach">Where the tool centre may go: the region shrunk by the tool radius (islands grown).</param>
    /// <param name="bite">The sideways depth of cut to aim for, in mm.</param>
    public static List<Step> Plan(IReadOnlyList<List<Vec3>> region, IReadOnlyList<List<Vec3>> reach, double radius, double bite, int maxSteps = 600_000)
    {
        var path = new List<Step>();
        if (region.Count == 0 || reach.Count == 0) return path;
        double minX = region.SelectMany(l => l).Min(p => p.X), maxX = region.SelectMany(l => l).Max(p => p.X);
        double minY = region.SelectMany(l => l).Min(p => p.Y), maxY = region.SelectMany(l => l).Max(p => p.Y);
        double cell = Math.Max(radius / 5, Math.Max(maxX - minX, maxY - minY) / 700);
        int nx = (int)Math.Ceiling((maxX - minX) / cell) + 1, ny = (int)Math.Ceiling((maxY - minY) / cell) + 1;
        var left = new bool[nx * ny];                                              // material still standing
        int remaining = 0;
        for (int j = 0; j < ny; j++)
            for (int i = 0; i < nx; i++)
                if (InsideAny(region, minX + i * cell, minY + j * cell)) { left[j * nx + i] = true; remaining++; }
        // cells the tool can never touch (too far from anywhere its centre may go) are not waited for
        int rc = (int)Math.Ceiling(radius / cell);
        var disc = new List<(int di, int dj)>();
        for (int dj = -rc; dj <= rc; dj++) for (int di = -rc; di <= rc; di++) if ((di * di + dj * dj) * cell * cell <= radius * radius) disc.Add((di, dj));

        int Cut(double x, double y)
        {
            int ci = (int)Math.Round((x - minX) / cell), cj = (int)Math.Round((y - minY) / cell), removed = 0;
            foreach (var (di, dj) in disc)
            {
                int i = ci + di, j = cj + dj;
                if (i < 0 || j < 0 || i >= nx || j >= ny || !left[j * nx + i]) continue;
                left[j * nx + i] = false; removed++;
            }
            return removed;
        }
        double Load(double x, double y)
        {
            int ci = (int)Math.Round((x - minX) / cell), cj = (int)Math.Round((y - minY) / cell), n = 0;
            foreach (var (di, dj) in disc)
            {
                int i = ci + di, j = cj + dj;
                if (i >= 0 && j >= 0 && i < nx && j < ny && left[j * nx + i]) n++;
            }
            return n / (double)disc.Count;
        }
        bool Allowed(double x, double y) => InsideAny(reach, x, y);

        // the cells the tool can reach: within a radius of somewhere allowed. Mark the rest as done.
        {
            var reachable = new bool[nx * ny];
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    if (!left[j * nx + i]) continue;
                    double x = minX + i * cell, y = minY + j * cell;
                    // is there an allowed point within the radius? probe the cell and a ring around it
                    bool ok = Allowed(x, y);
                    for (int k = 0; k < 12 && !ok; k++) { double a = k * Math.PI / 6; ok = Allowed(x + radius * 0.95 * Math.Cos(a), y + radius * 0.95 * Math.Sin(a)); }
                    for (int k = 0; k < 8 && !ok; k++) { double a = k * Math.PI / 4 + 0.2; ok = Allowed(x + radius * 0.5 * Math.Cos(a), y + radius * 0.5 * Math.Sin(a)); }
                    reachable[j * nx + i] = ok;
                }
            for (int q = 0; q < left.Length; q++) if (left[q] && !reachable[q]) { left[q] = false; remaining--; }
        }

        double target = LoadFraction(radius, bite);
        double stepLen = Math.Max(cell, Math.Min(radius * 0.35, bite * 0.8));
        double minLoad = target * 0.2;
        double cx = 0, cy = 0, heading = 0; bool haveTool = false;

        // find the nearest standing material to (x, y) and a place the tool centre can stand to cut it
        (double x, double y)? NextStart(double x, double y)
        {
            double best = double.MaxValue; (double, double)? found = null;
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    if (!left[j * nx + i]) continue;
                    double px = minX + i * cell, py = minY + j * cell, d = (px - x) * (px - x) + (py - y) * (py - y);
                    if (d >= best) continue;
                    // stand as close to the material as the allowed zone permits
                    bool stood = false; double sx = 0, sy = 0;
                    for (int s = 0; s < 8 && !stood; s++)
                    {
                        double rad = s * radius * 0.7 / 7;
                        for (int k = 0; k < 16 && !stood; k++)
                        {
                            double a = k * Math.PI / 8, qx = px + rad * Math.Cos(a), qy = py + rad * Math.Sin(a);
                            if (Allowed(qx, qy)) { stood = true; sx = qx; sy = qy; }
                        }
                    }
                    if (stood) { best = d; found = (sx, sy); }
                    else { left[j * nx + i] = false; remaining--; }         // nowhere to stand: leave it
                }
            return found;
        }

        // is the straight way from the tool to (x, y) inside the allowed zone and through ground that is (nearly) clear?
        bool FreeRun(double x0, double y0, double x1, double y1)
        {
            double len = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0)); int n = Math.Max(1, (int)(len / (cell * 2)));
            for (int k = 1; k <= n; k++)
            {
                double t = k / (double)n, x = x0 + (x1 - x0) * t, y = y0 + (y1 - y0) * t;
                if (!Allowed(x, y) || Load(x, y) > target * 0.1) return false;      // a light brush with the edge of the cut is fine
            }
            return true;
        }

        int steps = 0;
        while (remaining > 0 && steps++ < maxSteps)
        {
            if (!haveTool)
            {
                var s = NextStart(cx, cy); if (s == null) break;
                (cx, cy) = s.Value; haveTool = true; heading = 0;
                path.Add(new(Op.Start, cx, cy)); remaining -= Cut(cx, cy);
                continue;
            }
            // try directions around the current heading and take the one whose load is closest to the target
            double bestScore = double.MaxValue, bestAngle = 0, bestX = 0, bestY = 0; bool any = false;
            for (int k = -9; k <= 9; k++)
            {
                double a = heading + k * Math.PI / 18, nx2 = cx + stepLen * Math.Cos(a), ny2 = cy + stepLen * Math.Sin(a);
                if (!Allowed(nx2, ny2)) continue;
                double load = Load(nx2, ny2);
                if (load < minLoad) continue;
                double score = Math.Abs(load - target) + Math.Abs(k) * 0.004;             // a slight preference for going straight on
                if (score < bestScore) { bestScore = score; bestAngle = a; bestX = nx2; bestY = ny2; any = true; }
            }
            if (!any)
            {
                // no good bite ahead: look all around once before giving up on this stretch
                for (int k = 0; k < 24; k++)
                {
                    double a = heading + k * Math.PI / 12, nx2 = cx + stepLen * Math.Cos(a), ny2 = cy + stepLen * Math.Sin(a);
                    if (!Allowed(nx2, ny2)) continue;
                    double load = Load(nx2, ny2);
                    if (load < minLoad) continue;
                    double score = Math.Abs(load - target);
                    if (score < bestScore) { bestScore = score; bestAngle = a; bestX = nx2; bestY = ny2; any = true; }
                }
            }
            if (any)
            {
                heading = bestAngle; cx = bestX; cy = bestY;
                path.Add(new(Op.Cut, cx, cy)); remaining -= Cut(cx, cy);
                continue;
            }
            var next = NextStart(cx, cy);
            if (next == null) break;
            var (sx, sy) = next.Value;
            if (FreeRun(cx, cy, sx, sy)) path.Add(new(Op.Link, sx, sy)); else path.Add(new(Op.Start, sx, sy));
            cx = sx; cy = sy; remaining -= Cut(cx, cy);
        }
        return path;
    }
}
