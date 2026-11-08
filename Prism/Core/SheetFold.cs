using System;
using Prism.Kernel;

namespace Prism.Core;

/// <summary>A rigid motion p' = M·p + T, with M a row-major 3×3 rotation (the layout <see cref="Occ.Transform"/> takes).</summary>
public readonly record struct Rigid(double[] M, Vec3 T)
{
    public static readonly Rigid Identity = new([1, 0, 0, 0, 1, 0, 0, 0, 1], Vec3.Zero);
    public Vec3 Rotate(Vec3 v) => new(M[0] * v.X + M[1] * v.Y + M[2] * v.Z, M[3] * v.X + M[4] * v.Y + M[5] * v.Z, M[6] * v.X + M[7] * v.Y + M[8] * v.Z);
    public Vec3 Apply(Vec3 p) => Rotate(p) + T;
}

/// <summary>
/// The arithmetic of folding a flat sheet along straight bend lines (Fold). The flat sketch is the true blank. A bend line is the middle of a bend zone of
/// width <see cref="BendAllowance"/>, which becomes a cylindrical strip whose neutral surface keeps its length. The regions between zones stay rigid.
/// It uses the bend allowance θ·(r + K·t), with K measured from the inside surface, that Flat Pattern uses, so a folded part unfolds to the same blank.
/// </summary>
public static class SheetFold
{
    /// <summary>Length of the neutral surface across a bend of <paramref name="theta"/> radians: the width of the flat bend zone.</summary>
    public static double BendAllowance(double theta, double insideRadius, double k, double thickness) => theta * (insideRadius + k * thickness);

    /// <summary>Radius of the neutral surface of a bend.</summary>
    public static double NeutralRadius(double insideRadius, double k, double thickness) => insideRadius + k * thickness;

    /// <summary>
    /// Where a region folds to when it is joined to its parent across a bend.
    /// <paramref name="p"/> is a point on the bend line and <paramref name="d"/> its direction, both in the flat sheet; <paramref name="n"/> is the sheet normal and
    /// <paramref name="u"/> = n × d points from the parent into the child. A positive <paramref name="s"/> folds the child toward +n (the inside of the bend is on the +n face),
    /// a negative one toward −n. The metal occupies z ∈ [z0, z0 + t] along n.
    /// </summary>
    public static Rigid Child(Rigid parent, Vec3 p, Vec3 u, Vec3 d, Vec3 n, double theta, int s,
        double insideRadius, double k, double thickness, double z0)
    {
        double ba = BendAllowance(theta, insideRadius, k, thickness), half = ba / 2, rho = NeutralRadius(insideRadius, k, thickness);
        double zn = z0 + (s > 0 ? thickness - k * thickness : k * thickness);            // neutral surface, measured from the sketch plane
        var start = parent.Apply(p - u * half + n * zn);                                  // where the parent's flat metal ends
        Vec3 uw = parent.Rotate(u), dw = parent.Rotate(d), zw = parent.Rotate(n);
        double c = Math.Cos(theta), si = Math.Sin(theta);
        var centre = start + zw * (s * rho);                                              // axis of the bend, on the inside
        var end = centre + zw * (-s * rho * c) + uw * (rho * si);                         // where the child's flat metal starts
        Vec3 u2 = uw * c + zw * (s * si), z2 = zw * c - uw * (s * si);
        // M = u2·uᵀ + dw·dᵀ + z2·nᵀ : the flat basis (u, d, n) goes to the folded basis (u2, dw, z2)
        var m = new double[9];
        void Outer(Vec3 a, Vec3 b) { m[0] += a.X * b.X; m[1] += a.X * b.Y; m[2] += a.X * b.Z; m[3] += a.Y * b.X; m[4] += a.Y * b.Y; m[5] += a.Y * b.Z; m[6] += a.Z * b.X; m[7] += a.Z * b.Y; m[8] += a.Z * b.Z; }
        Outer(u2, u); Outer(dw, d); Outer(z2, n);
        var rot = new Rigid(m, Vec3.Zero);
        var flatStart = p + u * half + n * zn;                                            // the child's first flat point on the neutral surface
        return new Rigid(m, end - rot.Rotate(flatStart));
    }
}
