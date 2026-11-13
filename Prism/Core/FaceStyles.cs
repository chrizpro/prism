using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Kernel;

namespace Prism.Core;

/// <summary>
/// What one face carries beyond the object it belongs to: its own color and finish, and whether it is hidden.
/// A null <see cref="Color"/> or <see cref="Look"/> means "as the object". See the surface guide, "Faces as first-class things".
/// </summary>
public sealed record FaceStyle(uint? Color = null, Appearance? Look = null, bool Hidden = false)
{
    public bool IsEmpty => Color == null && Look == null && !Hidden;
    /// <summary>The same style without the hidden flag, so two faces "look the same" whether or not one is hidden.</summary>
    public FaceStyle Visible => this with { Hidden = false };
}

/// <summary>
/// Face styles live in an array indexed by face number. Face numbers change when a shape is edited, so an edit carries the styles over by
/// geometry: a new face takes the style of the old face it lies on. A face that is split gives every piece its style; a face that no old face
/// lies under (a new fillet far from anything) has none. No kernel history is needed.
/// </summary>
public static class FaceStyleMap
{
    /// <summary>
    /// For every face of <paramref name="next"/> the number of the face of <paramref name="old"/> it lies on, or -1 when none does. When both meshes have the
    /// same faces (every area equal) the faces are taken to be the same ones in the same order, which covers moving a body.
    /// </summary>
    public static int[] Origins(MeshData old, MeshData next)
    {
        var origin = new int[next.FaceCount];
        if (next.FaceCount == old.FaceCount && SameFaces(old, next)) { for (int f = 0; f < origin.Length; f++) origin[f] = f; return origin; }
        double diag = Math.Max((next.Max - next.Min).Length, (old.Max - old.Min).Length);
        double tol = Math.Max(diag * 4e-3, 1e-6);
        var boxes = FaceBoxes(old, tol);
        for (int f = 0; f < next.FaceCount; f++)
        {
            var votes = new Dictionary<int, double>();
            foreach (var (c, n, w) in Samples(next, f))
            {
                int best = Nearest(old, boxes, c, n, tol, out _);
                if (best >= 0) votes[best] = votes.GetValueOrDefault(best) + w;
            }
            origin[f] = votes.Count == 0 ? -1 : votes.MaxBy(kv => kv.Value).Key;
        }
        return origin;
    }

    /// <summary>Styles for the faces of <paramref name="next"/>, carried over from <paramref name="old"/>. Null when no face has a style.</summary>
    public static FaceStyle?[]? Remap(MeshData old, FaceStyle?[] styles, MeshData next, out int lost)
    {
        var origin = Origins(old, next);
        var result = new FaceStyle?[next.FaceCount];
        var carried = new HashSet<int>();
        for (int f = 0; f < origin.Length; f++)
            if (origin[f] >= 0 && origin[f] < styles.Length && styles[origin[f]] is { IsEmpty: false } s) { result[f] = s; carried.Add(origin[f]); }
        lost = 0;
        for (int f = 0; f < styles.Length; f++) if (styles[f] is { IsEmpty: false } && !carried.Contains(f)) lost++;
        return Compact(result);
    }

    static FaceStyle?[]? Compact(FaceStyle?[] a) => a.Any(s => s is { IsEmpty: false }) ? a : null;

    /// <summary>The same faces in the same order, possibly moved: every face has the same area.</summary>
    static bool SameFaces(MeshData a, MeshData b)
    {
        for (int f = 0; f < a.FaceCount; f++)
        {
            double x = Area(a, f), y = Area(b, f);
            if (Math.Abs(x - y) > 1e-3 * Math.Max(Math.Max(x, y), 1e-9)) return false;
        }
        return true;
    }

    public static double Area(MeshData m, int face)
    {
        double sum = 0;
        for (int t = m.FaceTriStart[face]; t < m.FaceTriStart[face + 1]; t++) sum += TriArea(m, t);
        return sum;
    }

    static double TriArea(MeshData m, int t)
    {
        var (a, b, c) = Corners(m, t);
        return 0.5 * Vec3.Cross(b - a, c - a).Length;
    }

    static (Vec3, Vec3, Vec3) Corners(MeshData m, int t)
    {
        Vec3 P(int i) => new(m.Positions[i * 3], m.Positions[i * 3 + 1], m.Positions[i * 3 + 2]);
        return (P(m.Indices[3 * t]), P(m.Indices[3 * t + 1]), P(m.Indices[3 * t + 2]));
    }

    /// <summary>A handful of triangles from the face, biggest first and spread through it: centroid, normal, weight (area).</summary>
    static IEnumerable<(Vec3 c, Vec3 n, double w)> Samples(MeshData m, int face)
    {
        int first = m.FaceTriStart[face], count = m.FaceTriStart[face + 1] - first;
        if (count <= 0) yield break;
        int take = Math.Min(count, 7);
        for (int k = 0; k < take; k++)
        {
            int t = first + (int)((k + 0.5) * count / take);
            var (a, b, c) = Corners(m, t);
            var cross = Vec3.Cross(b - a, c - a); double len = cross.Length;
            if (len < 1e-18) continue;
            yield return ((a + b + c) / 3.0, cross / len, len * 0.5);
        }
    }

    static (Vec3 min, Vec3 max)[] FaceBoxes(MeshData m, double pad)
    {
        var boxes = new (Vec3, Vec3)[m.FaceCount];
        for (int f = 0; f < m.FaceCount; f++)
        {
            Vec3 lo = new(double.MaxValue, double.MaxValue, double.MaxValue), hi = new(double.MinValue, double.MinValue, double.MinValue);
            for (int t = m.FaceTriStart[f]; t < m.FaceTriStart[f + 1]; t++)
            {
                var (a, b, c) = Corners(m, t);
                foreach (var p in new[] { a, b, c }) { lo = new(Math.Min(lo.X, p.X), Math.Min(lo.Y, p.Y), Math.Min(lo.Z, p.Z)); hi = new(Math.Max(hi.X, p.X), Math.Max(hi.Y, p.Y), Math.Max(hi.Z, p.Z)); }
            }
            boxes[f] = (new(lo.X - pad, lo.Y - pad, lo.Z - pad), new(hi.X + pad, hi.Y + pad, hi.Z + pad));
        }
        return boxes;
    }

    /// <summary>The old face that point <paramref name="p"/> lies on (within <paramref name="tol"/>, facing about the same way), or -1.</summary>
    static int Nearest(MeshData old, (Vec3 min, Vec3 max)[] boxes, Vec3 p, Vec3 n, double tol, out double distance)
    {
        int best = -1; distance = double.MaxValue;
        for (int f = 0; f < old.FaceCount; f++)
        {
            var (lo, hi) = boxes[f];
            if (p.X < lo.X || p.X > hi.X || p.Y < lo.Y || p.Y > hi.Y || p.Z < lo.Z || p.Z > hi.Z) continue;
            for (int t = old.FaceTriStart[f]; t < old.FaceTriStart[f + 1]; t++)
            {
                var (a, b, c) = Corners(old, t);
                var cross = Vec3.Cross(b - a, c - a); double len = cross.Length;
                if (len < 1e-18) continue;
                var tn = cross / len;
                if (Math.Abs(Vec3.Dot(tn, n)) < 0.6) continue;
                double d = (p - ClosestOnTriangle(p, a, b, c)).Length;
                if (d < distance && d <= tol) { distance = d; best = f; }
            }
        }
        return best;
    }

    static Vec3 ClosestOnTriangle(Vec3 p, Vec3 a, Vec3 b, Vec3 c)
    {
        var ab = b - a; var ac = c - a; var ap = p - a;
        double d1 = Vec3.Dot(ab, ap), d2 = Vec3.Dot(ac, ap);
        if (d1 <= 0 && d2 <= 0) return a;
        var bp = p - b; double d3 = Vec3.Dot(ab, bp), d4 = Vec3.Dot(ac, bp);
        if (d3 >= 0 && d4 <= d3) return b;
        double vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0) return a + ab * (d1 / (d1 - d3));
        var cp = p - c; double d5 = Vec3.Dot(ab, cp), d6 = Vec3.Dot(ac, cp);
        if (d6 >= 0 && d5 <= d6) return c;
        double vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0) return a + ac * (d2 / (d2 - d6));
        double va = d3 * d6 - d5 * d4;
        if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0) return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
        double denom = 1 / (va + vb + vc);
        return a + ab * (vb * denom) + ac * (vc * denom);
    }

    /// <summary>Styles after setting <paramref name="edit"/> on the given faces; faces whose style becomes empty drop it.</summary>
    public static FaceStyle?[]? With(FaceStyle?[]? current, int faceCount, IEnumerable<int> faces, Func<FaceStyle, FaceStyle> edit)
    {
        var next = new FaceStyle?[faceCount];
        if (current != null) Array.Copy(current, next, Math.Min(current.Length, faceCount));
        foreach (int f in faces)
        {
            if (f < 0 || f >= faceCount) continue;
            var s = edit(next[f] ?? new FaceStyle());
            next[f] = s.IsEmpty ? null : s;
        }
        return Compact(next);
    }
}
