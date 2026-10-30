using System;
using System.Collections.Generic;
using System.Numerics;
using Prism.Kernel;

namespace Prism.Rendering;

public enum LightKind { Sun, Point, Spot }

/// <summary>
/// A light the user dropped into the scene. Sun: a distant light, <see cref="Position"/> only marks the direction it comes from
/// (<c>Position - Aim</c>). Point and Spot: a small spherical lamp at <see cref="Position"/>; a spot also points at <see cref="Aim"/>.
/// Intensity is <b>lux</b> (illuminance on a surface facing the sun) for a sun and <b>lumens</b> (luminous power) for a lamp.
/// Model units are millimetres.
/// </summary>
public sealed class RenderLight
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Light";
    public LightKind Kind { get; set; } = LightKind.Point;
    public bool Enabled { get; set; } = true;
    public Vec3 Position { get; set; }
    public Vec3 Aim { get; set; }
    /// <summary>Lux for a sun, lumens for a point or spot lamp.</summary>
    public double Intensity { get; set; } = 100000;
    public double Kelvin { get; set; } = 5600;
    /// <summary>Spot: full opening angle of the cone, degrees.</summary>
    public double ConeAngle { get; set; } = 50;
    /// <summary>Spot: fraction of the cone (0 hard edge … 1) over which the beam fades out.</summary>
    public double Softness { get; set; } = 0.25;
    /// <summary>Lamp: radius of the glowing sphere in mm (bigger means softer shadows). Sun: apparent diameter in degrees (the real sun is 0.53°).</summary>
    public double Size { get; set; } = 0.53;

    public Vec3 TowardLight => (Position - Aim).Normalized();
    public Vec3 Axis => (Aim - Position).Normalized();

    public RenderLight Clone() => (RenderLight)MemberwiseClone();

    public static string KindName(LightKind k) => k switch { LightKind.Sun => "Sun", LightKind.Spot => "Spot", _ => "Lamp" };
}

/// <summary>The picture-wide choices (not the lights): they are stored in the document so the next render looks the same.</summary>
public sealed class RenderSetup
{
    public RenderEnv Env { get; set; } = RenderEnv.Studio;
    public RenderBg Background { get; set; } = RenderBg.Environment;
    public bool Ground { get; set; } = true;
    public double Exposure { get; set; } = 1;
    public override bool Equals(object? o) => o is RenderSetup r && r.Env == Env && r.Background == Background && r.Ground == Ground && r.Exposure == Exposure;
    public override int GetHashCode() => HashCode.Combine(Env, Background, Ground, Exposure);
}

/// <summary>
/// The photometry the tracer uses. Colours are linear RGB; the tracer's irradiance unit is <see cref="LuxPerUnit"/> lux, so a clear-sky
/// sun (100 000 lx) lands at 4.0 and a white diffuse surface facing it is rendered at reflectance × 4/π ≈ 1.3 (a "sunny" exposure).
/// </summary>
public static class Photometry
{
    public const double LuxPerUnit = 25000;
    public const double MetresPerModelUnit = 0.001;
    public const double SunDiameterDegrees = 0.53;

    /// <summary>Linear sRGB of a black body at <paramref name="kelvin"/>, scaled so that its luminance is exactly 1 (colour temperature changes the hue, not the brightness).</summary>
    public static Vector3 KelvinToRgb(double kelvin)
    {
        double k = Math.Clamp(kelvin, 1667, 25000), k2 = k * k, k3 = k2 * k;
        // Kim et al. 2002 approximation of the Planckian locus in CIE xy
        double x = k <= 4000 ? -0.2661239e9 / k3 - 0.2343589e6 / k2 + 0.8776956e3 / k + 0.179910
                             : -3.0258469e9 / k3 + 2.1070379e6 / k2 + 0.2226347e3 / k + 0.240390;
        double y = k <= 2222 ? -1.1063814 * x * x * x - 1.34811020 * x * x + 2.18555832 * x - 0.20219683
                 : k <= 4000 ? -0.9549476 * x * x * x - 1.37418593 * x * x + 2.09137015 * x - 0.16748867
                             : 3.0817580 * x * x * x - 5.87338670 * x * x + 3.75112997 * x - 0.37001483;
        double X = x / y, Z = (1 - x - y) / y;
        double r = Math.Max(0, 3.2406 * X - 1.5372 - 0.4986 * Z);
        double g = Math.Max(0, -0.9689 * X + 1.8758 + 0.0415 * Z);
        double b = Math.Max(0, 0.0557 * X - 0.2040 + 1.0570 * Z);
        double luminance = 0.2126 * r + 0.7152 * g + 0.0722 * b;
        return new Vector3((float)(r / luminance), (float)(g / luminance), (float)(b / luminance));
    }

    /// <summary>Luminous intensity (candela) of a lamp that emits <paramref name="lumens"/> in every direction.</summary>
    public static double PointCandela(double lumens) => lumens / (4 * Math.PI);

    /// <summary>Luminous intensity on the axis of a spot whose beam fills the cone of full angle <paramref name="coneDegrees"/>.
    /// Simplification: the quoted power is spread evenly over the cone; the soft edge does not take any of it.</summary>
    public static double SpotCandela(double lumens, double coneDegrees)
        => lumens / (2 * Math.PI * (1 - Math.Cos(Math.Clamp(coneDegrees, 1, 175) * Math.PI / 360)));

    /// <summary>Illuminance (lux) on a surface facing a lamp of the given candela at <paramref name="distanceMillimetres"/> (inverse-square law).</summary>
    public static double IlluminanceLux(double candela, double distanceMillimetres)
    {
        double metres = Math.Max(distanceMillimetres * MetresPerModelUnit, 1e-6);
        return candela / (metres * metres);
    }

    /// <summary>1 inside the inner cone, 0 outside the outer cone, smooth in between (angles are measured from the spot axis).</summary>
    public static float SpotFalloff(float cosAngle, float cosOuter, float cosInner)
    {
        if (cosAngle <= cosOuter) return 0;
        if (cosAngle >= cosInner) return 1;
        float t = (cosAngle - cosOuter) / (cosInner - cosOuter);
        return t * t * (3 - 2 * t);
    }

    /// <summary>The lumen figure that lights a subject at <paramref name="distanceMillimetres"/> to <paramref name="units"/> of tracer irradiance.</summary>
    public static double LumensFor(LightKind kind, double units, double distanceMillimetres, double coneDegrees)
    {
        double lux = units * LuxPerUnit, metres = Math.Max(distanceMillimetres * MetresPerModelUnit, 1e-6);
        double candela = lux * metres * metres;
        return kind == LightKind.Spot ? candela * 2 * Math.PI * (1 - Math.Cos(Math.Clamp(coneDegrees, 1, 175) * Math.PI / 360)) : candela * 4 * Math.PI;
    }
}
