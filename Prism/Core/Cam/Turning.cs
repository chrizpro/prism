using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Kernel;

namespace Prism.Core.Cam;

/// <summary>
/// The outline of a turned part: the outer radius at every position along its axis. A sketch of the half section (X is the radius, Z runs along the axis)
/// is turned into this, because a lathe can only ever make shapes that are round about the spindle.
/// </summary>
public sealed class TurningProfile
{
    public readonly double ZMin, ZMax, Step;
    readonly double[] _r;                       // NaN where the part has no material
    public int Count => _r.Length;
    public double Z(int i) => ZMax - i * Step;  // sample 0 is the front (highest Z): the tool works toward the chuck
    public double R(int i) => _r[i];
    public double MaxR => _r.Where(v => !double.IsNaN(v)).DefaultIfEmpty(0).Max();

    TurningProfile(double zMin, double zMax, double step, double[] r) { ZMin = zMin; ZMax = zMax; Step = step; _r = r; }

    public static TurningProfile FromLoops(IReadOnlyList<List<Vec3>> loops, bool[] closed)
    {
        var pts = loops.SelectMany(l => l).ToList();
        if (pts.Count < 2) throw new InvalidOperationException("The profile needs at least two points.");
        double zMin = pts.Min(p => p.Z), zMax = pts.Max(p => p.Z);
        if (zMax - zMin < 1e-6) throw new InvalidOperationException("The profile has no length along the axis (Z). Draw it on the XZ plane.");
        double step = Math.Max(0.02, (zMax - zMin) / 20000);
        int n = (int)Math.Floor((zMax - zMin) / step) + 1;
        var r = new double[n];
        for (int i = 0; i < n; i++)
        {
            double z = zMax - i * step, best = double.NaN;
            for (int li = 0; li < loops.Count; li++)
            {
                var l = loops[li]; int count = closed[li] ? l.Count : l.Count - 1;
                for (int k = 0; k < count; k++)
                {
                    var a = l[k]; var b = l[(k + 1) % l.Count];
                    double lo = Math.Min(a.Z, b.Z) - 1e-9, hi = Math.Max(a.Z, b.Z) + 1e-9;
                    if (z < lo || z > hi) continue;
                    double x = Math.Abs(b.Z - a.Z) < 1e-12 ? Math.Max(Math.Abs(a.X), Math.Abs(b.X)) : Math.Abs(a.X + (b.X - a.X) * (z - a.Z) / (b.Z - a.Z));
                    if (double.IsNaN(best) || x > best) best = x;
                }
            }
            r[i] = best;
        }
        return new TurningProfile(zMin, zMax, step, r);
    }

    /// <summary>The outline as a polyline from the front toward the chuck, with straight stretches merged.</summary>
    public List<(double r, double z)> Outline(double tolerance)
    {
        var raw = new List<(double r, double z)>();
        for (int i = 0; i < Count; i++) if (!double.IsNaN(_r[i])) raw.Add((_r[i], Z(i)));
        return raw.Count < 3 ? raw : Simplify(raw, tolerance);
    }

    static List<(double r, double z)> Simplify(List<(double r, double z)> p, double tol)
    {
        var keep = new bool[p.Count]; keep[0] = keep[^1] = true;
        var stack = new Stack<(int a, int b)>(); stack.Push((0, p.Count - 1));
        while (stack.Count > 0)
        {
            var (a, b) = stack.Pop(); double worst = 0; int at = -1;
            for (int i = a + 1; i < b; i++)
            {
                double dz = p[b].z - p[a].z, dr = p[b].r - p[a].r, len = Math.Sqrt(dz * dz + dr * dr);
                double d = len < 1e-12 ? Math.Sqrt(Math.Pow(p[i].z - p[a].z, 2) + Math.Pow(p[i].r - p[a].r, 2)) : Math.Abs(dr * (p[i].z - p[a].z) - dz * (p[i].r - p[a].r)) / len;
                if (d > worst) { worst = d; at = i; }
            }
            if (at >= 0 && worst > tol) { keep[at] = true; stack.Push((a, at)); stack.Push((at, b)); }
        }
        return p.Where((_, i) => keep[i]).ToList();
    }

    /// <summary>The stretches (front Z, back Z) where the part is smaller than this radius, so the tool has material to remove.</summary>
    public List<(double front, double back)> StretchesBelow(double radius)
    {
        var res = new List<(double, double)>(); int start = -1;
        for (int i = 0; i <= Count; i++)
        {
            bool cut = i < Count && !double.IsNaN(_r[i]) && _r[i] < radius;
            if (cut && start < 0) start = i;
            if (!cut && start >= 0) { res.Add((Z(start), Z(i - 1))); start = -1; }
        }
        return res;
    }
}
