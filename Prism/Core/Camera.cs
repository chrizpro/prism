using System;
using System.Numerics;
using Prism.Kernel;

namespace Prism.Core;

/// <summary>Orbit camera, Z-up. Yaw 0 / pitch 0 looks from -Y toward +Y (the "Front" view).</summary>
public sealed class Camera
{
    public Vec3 Target = Vec3.Zero;
    public double Distance = 220;
    public double Yaw = Rad(-35);
    public double Pitch = Rad(28);
    /// <summary>Turn of the picture about the line of sight (the view cube's rotate arrows); 0 keeps Z up.</summary>
    public double Roll;
    public double FovY = Rad(35);
    public bool Orthographic;
    /// <summary>Standard orthographic views in a multi-view layout cannot be orbited (that would stop them being Top/Front/Right).</summary>
    public bool Locked;
    public string Label = "";
    public double Width = 1000, Height = 700;   // pixels

    static double Rad(double d) => d * Math.PI / 180;

    public Vec3 EyeDirection => new(Math.Sin(Yaw) * Math.Cos(Pitch), -Math.Cos(Yaw) * Math.Cos(Pitch), Math.Sin(Pitch));
    Vec3 UprightUp => new(-Math.Sin(Yaw) * Math.Sin(Pitch), Math.Cos(Yaw) * Math.Sin(Pitch), Math.Cos(Pitch));
    public Vec3 Up
    {
        get
        {
            var u = UprightUp;
            if (Roll == 0) return u;
            var r = Vec3.Cross(Forward, u).Normalized();
            return u * Math.Cos(Roll) + r * Math.Sin(Roll);
        }
    }
    public Vec3 Eye => Target + EyeDirection * Distance;
    public Vec3 Forward => -EyeDirection;
    public Vec3 Right => Vec3.Cross(Forward, Up).Normalized();

    public double NearPlane => Math.Max(Distance * 0.01, 0.01);
    public double FarPlane => Distance * 400 + 1e4;

    /// <summary>World units covered by one screen pixel at the target distance.</summary>
    public double PixelSize => 2 * Distance * Math.Tan(FovY / 2) / Math.Max(Height, 1);

    public double PixelSizeAt(Vec3 p)
    {
        if (Orthographic) return PixelSize;
        double depth = Math.Max(Vec3.Dot(p - Eye, Forward), NearPlane);
        return 2 * depth * Math.Tan(FovY / 2) / Math.Max(Height, 1);
    }

    public Matrix4x4 View => Matrix4x4.CreateLookAt(F(Eye), F(Target), F(Up));

    public Matrix4x4 Projection
    {
        get
        {
            float aspect = (float)(Width / Math.Max(Height, 1));
            if (Orthographic)
            {
                float h = (float)(2 * Distance * Math.Tan(FovY / 2));
                return Matrix4x4.CreateOrthographic(h * aspect, h, (float)-FarPlane * 0.5f, (float)FarPlane);
            }
            return Matrix4x4.CreatePerspectiveFieldOfView((float)FovY, aspect, (float)NearPlane, (float)FarPlane);
        }
    }
    public Matrix4x4 ViewProjection => View * Projection;

    static Vector3 F(Vec3 v) => new((float)v.X, (float)v.Y, (float)v.Z);

    /// <summary>Projects a world point to pixel coordinates (origin top-left). Depth is NDC z; null when behind the eye.</summary>
    public (double x, double y, double depth)? Project(Vec3 p, Matrix4x4? vp = null)
    {
        var m = vp ?? ViewProjection;
        var c = Vector4.Transform(new Vector4(F(p), 1), m);
        if (c.W <= 1e-6f) return null;
        double nx = c.X / c.W, ny = c.Y / c.W;
        return ((nx * 0.5 + 0.5) * Width, (1 - (ny * 0.5 + 0.5)) * Height, c.Z / c.W);
    }

    /// <summary>World-space ray through a pixel.</summary>
    public (Vec3 origin, Vec3 dir) Ray(double px, double py)
    {
        double nx = px / Width * 2 - 1, ny = 1 - py / Height * 2;
        double halfH = Math.Tan(FovY / 2), aspect = Width / Math.Max(Height, 1);
        var f = Forward; var r = Right; var u = Up;
        if (Orthographic)
        {
            double h = Distance * halfH;
            return (Eye + r * (nx * h * aspect) + u * (ny * h), f);
        }
        var dir = (f + r * (nx * halfH * aspect) + u * (ny * halfH)).Normalized();
        return (Eye, dir);
    }

    public void Orbit(double dxPx, double dyPx)
    {
        Yaw -= dxPx * 0.0065;
        Pitch = Math.Clamp(Pitch + dyPx * 0.0065, -Math.PI / 2, Math.PI / 2);
    }

    /// <summary>Rotate the view with the cube, including across the poles, without forcing the scene upright.</summary>
    public void OrbitFree(double dxPx, double dyPx)
    {
        static Vec3 Rotate(Vec3 v, Vec3 axis, double angle) =>
            v * Math.Cos(angle) + Vec3.Cross(axis, v) * Math.Sin(angle) + axis * (Vec3.Dot(axis, v) * (1 - Math.Cos(angle)));
        var eye = EyeDirection;
        var up = Up;
        var right = Right;
        eye = Rotate(eye, up, -dxPx * 0.0065);
        right = Rotate(right, up, -dxPx * 0.0065);
        eye = Rotate(eye, right, -dyPx * 0.0065);
        up = Rotate(up, right, -dyPx * 0.0065);
        SetOrientation(eye, up);
    }

    public void SetOrientation(Vec3 eyeDirection, Vec3 up)
    {
        var eye = eyeDirection.Normalized();
        Pitch = Math.Asin(Math.Clamp(eye.Z, -1, 1));
        if (Math.Abs(eye.Z) < 0.999999999) Yaw = Math.Atan2(eye.X, -eye.Y);
        var upright = UprightUp;
        var right = Vec3.Cross(-eye, upright).Normalized();
        Roll = Math.Atan2(Vec3.Dot(up, right), Vec3.Dot(up, upright));
    }

    public void Pan(double dxPx, double dyPx)
    {
        double k = PixelSize;
        Target += (-Right * dxPx + Up * dyPx) * k;
    }

    /// <summary>Zooms toward the point under the cursor so it stays put on screen.</summary>
    public void ZoomAt(double px, double py, double factor)
    {
        var (o, d) = Ray(px, py);
        var before = Plane3Through(Target).Intersect(o, d) ?? Target;
        Distance = Math.Clamp(Distance * factor, 0.05, 1e7);
        var (o2, d2) = Ray(px, py);
        var after = Plane3Through(Target).Intersect(o2, d2) ?? Target;
        Target += before - after;
    }

    Plane3 Plane3Through(Vec3 p) => new(p, Right, Up);

    public void SetView(double yawDeg, double pitchDeg) { Yaw = Rad(yawDeg); Pitch = Rad(pitchDeg); Roll = 0; }
    public void LookAlong(Vec3 normal)
    {
        var n = normal.Normalized();
        Roll = 0;
        Pitch = Math.Asin(Math.Clamp(n.Z, -1, 1));
        Yaw = Math.Abs(n.Z) < 0.9999 ? Math.Atan2(n.X, -n.Y) : 0;
    }

    public void Fit(Vec3 min, Vec3 max)
    {
        var c = (min + max) * 0.5;
        double r = Math.Max((max - min).Length * 0.5, 1e-3);
        Target = c;
        Distance = r / Math.Sin(Math.Min(FovY, FovY * Math.Max(Width, 1) / Math.Max(Height, 1)) / 2) * 1.05;
    }
}
