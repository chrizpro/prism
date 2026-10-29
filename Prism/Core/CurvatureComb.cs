using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Kernel;

namespace Prism.Core;

/// <summary>
/// The curvature comb: a short hair at each point of a curve, as long as the curve is bent there and pointing away from the centre of the bend.
/// Smooth, even combs mean a fair curve; sudden jumps in the envelope mean the curvature is not continuous there.
/// </summary>
public static class CurvatureComb
{
    public static void Draw(Overlay o, CadSession s, List<(Vec3 p, Vec3 k)[]> edges, double hairPx = 46, int stride = 2)
    {
        var mags = new List<double>();
        foreach (var e in edges) foreach (var (_, k) in e) mags.Add(k.Length);
        if (mags.Count == 0) return;
        double maxK = mags.Max(); if (maxK < 1e-7) return;
        mags.Sort(); double robust = Math.Max(mags[(int)(mags.Count * 0.97)], maxK * 0.05);       // one sharp spot must not flatten the rest of the comb
        Vec3 first = edges[0][0].p; double ps = s.Camera.PixelSizeAt(first);
        double scale = hairPx * ps / robust, cap = hairPx * ps * 3;
        Vec3 atMax = first; double best = 0;
        foreach (var e in edges)
        {
            var tips = new List<Vec3>(e.Length);
            for (int i = 0; i < e.Length; i++)
            {
                var (p, k) = e[i]; double m = k.Length;
                if (m > best) { best = m; atMax = p; }
                var hair = k * -scale; if (hair.Length > cap) hair = hair.Normalized() * cap;
                var tip = p + hair; tips.Add(tip);
                if (i % stride == 0 && m > robust * 0.01) o.AddLine(p, tip, 0x90E8B03A, 1f);
            }
            if (tips.Zip(e, (t, q) => t.DistanceTo(q.p)).Any(d => d > ps * 2)) o.AddPolyline(tips, 0xFFFFC53D, 1.5f);
        }
        o.AddLabel(atMax, $"tightest bend: radius {1 / best:0.##} mm", true, 0);
    }
}
