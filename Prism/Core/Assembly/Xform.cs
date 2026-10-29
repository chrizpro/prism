using System;
using Prism.Kernel;

namespace Prism.Core.Assembly;

/// <summary>A rigid placement: a rotation (row-major 3x3) followed by a translation.</summary>
public readonly struct Xform
{
    public readonly double[] R;      // 9 values, row-major
    public readonly Vec3 T;

    public Xform(double[] r, Vec3 t) { R = r; T = t; }
    public static Xform Identity => new([1, 0, 0, 0, 1, 0, 0, 0, 1], Vec3.Zero);

    public static Xform Translation(Vec3 t) => new([1, 0, 0, 0, 1, 0, 0, 0, 1], t);

    /// <summary>A frame with the given origin whose columns are x, y = z × x and z.</summary>
    public static Xform FromFrame(Vec3 origin, Vec3 x, Vec3 z)
    {
        z = z.Normalized(); x = (x - z * Vec3.Dot(x, z)).Normalized(); var y = Vec3.Cross(z, x);
        return new([x.X, y.X, z.X, x.Y, y.Y, z.Y, x.Z, y.Z, z.Z], origin);
    }

    /// <summary>A stable x direction for a frame that only has a z direction.</summary>
    public static Vec3 AnyPerpendicular(Vec3 z)
    {
        z = z.Normalized();
        return (Math.Abs(z.Z) < 0.99 ? Vec3.Cross(Vec3.UnitZ, z) : Vec3.Cross(Vec3.UnitY, z)).Normalized();
    }

    public static Xform RotZ(double rad)
    {
        double c = Math.Cos(rad), s = Math.Sin(rad);
        return new([c, -s, 0, s, c, 0, 0, 0, 1], Vec3.Zero);
    }
    public static Xform RotX180 => new([1, 0, 0, 0, -1, 0, 0, 0, -1], Vec3.Zero);
    public static Xform TransZ(double d) => Translation(new Vec3(0, 0, d));

    public Vec3 Apply(Vec3 p) => new(
        R[0] * p.X + R[1] * p.Y + R[2] * p.Z + T.X,
        R[3] * p.X + R[4] * p.Y + R[5] * p.Z + T.Y,
        R[6] * p.X + R[7] * p.Y + R[8] * p.Z + T.Z);

    public Vec3 Rotate(Vec3 v) => new(
        R[0] * v.X + R[1] * v.Y + R[2] * v.Z,
        R[3] * v.X + R[4] * v.Y + R[5] * v.Z,
        R[6] * v.X + R[7] * v.Y + R[8] * v.Z);

    public Vec3 AxisX => new(R[0], R[3], R[6]);
    public Vec3 AxisY => new(R[1], R[4], R[7]);
    public Vec3 AxisZ => new(R[2], R[5], R[8]);

    public static Xform operator *(Xform a, Xform b)
    {
        var r = new double[9];
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                r[3 * i + j] = a.R[3 * i] * b.R[j] + a.R[3 * i + 1] * b.R[3 + j] + a.R[3 * i + 2] * b.R[6 + j];
        return new(r, a.Rotate(b.T) + a.T);
    }

    public Xform Inverse()
    {
        var rt = new[] { R[0], R[3], R[6], R[1], R[4], R[7], R[2], R[5], R[8] };
        var inv = new Xform(rt, Vec3.Zero);
        return new(rt, -inv.Rotate(T));
    }

    public Shape Apply(Shape s) => Occ.Transform(s, R, T);

    public bool IsIdentity => Math.Abs(R[0] - 1) + Math.Abs(R[4] - 1) + Math.Abs(R[8] - 1) + Math.Abs(R[1]) + Math.Abs(R[2]) + Math.Abs(R[3]) + Math.Abs(R[5]) + Math.Abs(R[6]) + Math.Abs(R[7]) + T.Length < 1e-12;

    public double[] ToArray() => [.. R, T.X, T.Y, T.Z];
    public static Xform FromArray(double[] a) => new([.. a.AsSpan(0, 9).ToArray()], new Vec3(a[9], a[10], a[11]));

    /// <summary>Rotation angle about Z of a transform that only turns about Z (radians).</summary>
    public double AngleAboutZ() => Math.Atan2(R[3], R[0]);

    public double DistanceTo(Xform o) => (T - o.T).Length + Math.Abs(R[0] - o.R[0]) + Math.Abs(R[1] - o.R[1]) + Math.Abs(R[3] - o.R[3]) + Math.Abs(R[4] - o.R[4]) + Math.Abs(R[8] - o.R[8]);
}
