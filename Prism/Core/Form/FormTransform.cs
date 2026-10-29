using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Commands;
using Prism.Kernel;

namespace Prism.Core.Form;

public enum SymMode { Off, MirrorX, MirrorY, MirrorZ, Radial }

/// <summary>Live symmetry for freeform editing: moving a point also moves its mirror image (or its copies around the Z axis).</summary>
public sealed class Symmetry(SymMode mode, int count = 4)
{
    public SymMode Mode { get; } = mode;
    public int Count { get; } = Math.Max(2, count);
    public bool Active => Mode != SymMode.Off;
    /// <summary>How many partners each point has (1 for a mirror, Count - 1 for radial).</summary>
    public int Maps => Mode == SymMode.Radial ? Count - 1 : Mode == SymMode.Off ? 0 : 1;

    /// <summary>"x", "y", "z" (mirror across the plane with that normal), "radial:6" (six copies round Z) or "off".</summary>
    public static Symmetry Parse(string? s)
    {
        s = (s ?? "off").Trim().ToLowerInvariant();
        if (s.StartsWith("radial")) { int n = 4; var i = s.IndexOf(':'); if (i > 0) int.TryParse(s[(i + 1)..], out n); return new Symmetry(SymMode.Radial, n < 2 ? 4 : n); }
        return s switch { "x" => new Symmetry(SymMode.MirrorX), "y" => new Symmetry(SymMode.MirrorY), "z" => new Symmetry(SymMode.MirrorZ), _ => new Symmetry(SymMode.Off) };
    }

    public Vec3 Map(int k, Vec3 p) => Mode switch
    {
        SymMode.MirrorX => new Vec3(-p.X, p.Y, p.Z),
        SymMode.MirrorY => new Vec3(p.X, -p.Y, p.Z),
        SymMode.MirrorZ => new Vec3(p.X, p.Y, -p.Z),
        SymMode.Radial => Rotate(p, 2 * Math.PI * (k + 1) / Count),
        _ => p,
    };

    static Vec3 Rotate(Vec3 p, double a) { double c = Math.Cos(a), s = Math.Sin(a); return new Vec3(p.X * c - p.Y * s, p.X * s + p.Y * c, p.Z); }

    /// <summary>A point that sits on the mirror plane (or the radial axis) has to stay there.</summary>
    public Vec3 Constrain(Vec3 p) => Mode switch
    {
        SymMode.MirrorX => new Vec3(0, p.Y, p.Z),
        SymMode.MirrorY => new Vec3(p.X, 0, p.Z),
        SymMode.MirrorZ => new Vec3(p.X, p.Y, 0),
        SymMode.Radial => new Vec3(0, 0, p.Z),
        _ => p,
    };

    /// <summary>For every point, the points that mirror it (index and which copy), found by position.</summary>
    public List<(int p, int k)>[] Partners(SubdMesh m, double? tolerance = null)
    {
        var res = new List<(int, int)>[m.V.Count];
        for (int i = 0; i < res.Length; i++) res[i] = [];
        if (!Active) return res;
        var box = m.V.Count == 0 ? 1 : (m.V.Aggregate((a, b) => new Vec3(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z))) - m.V.Aggregate((a, b) => new Vec3(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z)))).Length;
        double tol = tolerance ?? Math.Max(0.03, box * 2e-4), cell = tol * 2;
        var grid = new Dictionary<(long, long, long), List<int>>();
        (long, long, long) Cell(Vec3 p) => ((long)Math.Floor(p.X / cell), (long)Math.Floor(p.Y / cell), (long)Math.Floor(p.Z / cell));
        for (int i = 0; i < m.V.Count; i++) { var c = Cell(m.V[i]); if (!grid.TryGetValue(c, out var l)) grid[c] = l = []; l.Add(i); }
        for (int i = 0; i < m.V.Count; i++)
            for (int k = 0; k < Maps; k++)
            {
                var q = Map(k, m.V[i]); var (cx, cy, cz) = Cell(q); int found = -1; double best = tol;
                for (long x = cx - 1; x <= cx + 1; x++) for (long y = cy - 1; y <= cy + 1; y++) for (long z = cz - 1; z <= cz + 1; z++)
                    if (grid.TryGetValue((x, y, z), out var l)) foreach (int j in l) { double d = (m.V[j] - q).Length; if (d < best) { best = d; found = j; } }
                if (found >= 0) res[i].Add((found, k));
            }
        return res;
    }
}

/// <summary>Moving, turning and scaling parts of a freeform cage, with soft (proportional) falloff and live symmetry.</summary>
public static class FormTransform
{
    public static Func<Vec3, Vec3> MoveBy(Vec3 d) => p => p + d;

    public static Func<Vec3, Vec3> RotateAbout(Vec3 pivot, Vec3 axis, double radians)
    {
        var k = axis.Normalized(); double c = Math.Cos(radians), s = Math.Sin(radians);
        return p => { var v = p - pivot; return pivot + v * c + Vec3.Cross(k, v) * s + k * (Vec3.Dot(k, v) * (1 - c)); };
    }

    public static Func<Vec3, Vec3> ScaleAbout(Vec3 pivot, double sx, double sy, double sz) =>
        p => { var v = p - pivot; return pivot + new Vec3(v.X * sx, v.Y * sy, v.Z * sz); };

    /// <summary>How strongly each point follows the selection: 1 for the selection itself, then falling off with distance from it (0 = untouched).</summary>
    public static double[] Weights(SubdMesh m, Vec3[] basePos, ISet<int> sel, double radius, Falloff fall)
    {
        var w = new double[m.V.Count];
        foreach (int v in sel) if (v < w.Length && !m.IsFrozen(v)) w[v] = 1;
        if (radius <= 1e-9 || sel.Count == 0) return w;
        var selPts = sel.Where(v => v < w.Length).Select(v => basePos[v]).ToList();
        for (int i = 0; i < w.Length; i++)
        {
            if (w[i] > 0 || m.IsFrozen(i)) continue;
            double d = double.MaxValue;
            foreach (var p in selPts) { double dd = (basePos[i] - p).Length; if (dd < d) d = dd; if (d < 1e-9) break; }
            w[i] = FalloffMath.Weight(fall, d, radius);
        }
        return w;
    }

    /// <summary>Apply a transform to the selection (and its soft neighbourhood) starting from the positions in <paramref name="basePos"/>, then mirror the result.</summary>
    public static void Apply(SubdMesh m, Vec3[] basePos, ISet<int> sel, Func<Vec3, Vec3> xf, double softRadius, Falloff fall, Symmetry? sym, List<(int p, int k)>[]? partners)
    {
        var w = Weights(m, basePos, sel, softRadius, fall);
        var pos = (Vec3[])basePos.Clone();
        for (int i = 0; i < w.Length; i++) if (w[i] > 0) pos[i] = basePos[i] + (xf(basePos[i]) - basePos[i]) * w[i];
        if (sym is { Active: true } && partners != null)
        {
            var authored = w.Select(x => x > 0).ToArray();
            for (int i = 0; i < w.Length; i++)
            {
                if (!authored[i]) continue;
                foreach (var (p, k) in partners[i])
                {
                    if (p == i) { pos[i] = sym.Constrain(pos[i]); continue; }       // on the plane: stays on it
                    if (!authored[p] && !m.IsFrozen(p)) pos[p] = sym.Map(k, pos[i]);
                }
            }
        }
        for (int i = 0; i < pos.Length; i++) if (!m.IsFrozen(i)) m.V[i] = pos[i];
    }
}
