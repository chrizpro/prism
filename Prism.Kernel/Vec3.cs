namespace Prism.Kernel;

/// <summary>Double-precision 3D vector/point (millimetres, Z up).</summary>
public readonly record struct Vec3(double X, double Y, double Z)
{
    public static readonly Vec3 Zero = new(0, 0, 0);
    public static readonly Vec3 UnitX = new(1, 0, 0);
    public static readonly Vec3 UnitY = new(0, 1, 0);
    public static readonly Vec3 UnitZ = new(0, 0, 1);

    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
    public Vec3 Normalized() { var l = Length; return l < 1e-12 ? this : new(X / l, Y / l, Z / l); }
    public static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    public static Vec3 Cross(Vec3 a, Vec3 b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    public double DistanceTo(Vec3 o) => (this - o).Length;

    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator -(Vec3 a) => new(-a.X, -a.Y, -a.Z);
    public static Vec3 operator *(Vec3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    public static Vec3 operator *(double s, Vec3 a) => a * s;
    public static Vec3 operator /(Vec3 a, double s) => new(a.X / s, a.Y / s, a.Z / s);

    public static Vec3 Lerp(Vec3 a, Vec3 b, double t) => a + (b - a) * t;
    public override string ToString() => $"({X:0.###}, {Y:0.###}, {Z:0.###})";
}

/// <summary>A work plane: origin plus an orthonormal frame (U, V, Normal).</summary>
public readonly record struct Plane3(Vec3 Origin, Vec3 U, Vec3 V)
{
    public Vec3 Normal => Vec3.Cross(U, V).Normalized();
    public static readonly Plane3 XY = new(Vec3.Zero, Vec3.UnitX, Vec3.UnitY);
    public static readonly Plane3 XZ = new(Vec3.Zero, Vec3.UnitX, Vec3.UnitZ);
    public static readonly Plane3 YZ = new(Vec3.Zero, Vec3.UnitY, Vec3.UnitZ);

    public Vec3 PointAt(double u, double v) => Origin + U * u + V * v;
    public (double u, double v) Project(Vec3 p) { var d = p - Origin; return (Vec3.Dot(d, U), Vec3.Dot(d, V)); }

    /// <summary>Intersects a ray with the plane; null when parallel or behind the origin.</summary>
    public Vec3? Intersect(Vec3 rayOrigin, Vec3 rayDir)
    {
        var n = Normal;
        double den = Vec3.Dot(n, rayDir);
        if (Math.Abs(den) < 1e-9) return null;
        double t = Vec3.Dot(n, Origin - rayOrigin) / den;
        return t < 0 ? null : rayOrigin + rayDir * t;
    }
}
