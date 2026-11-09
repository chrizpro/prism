using System;
using System.Numerics;
using Prism.Core;

namespace Prism.Rendering;

public readonly record struct SurfaceSample(Vector3 Color, float Metal, float Roughness, Vector3 Normal);

/// <summary>Procedural layer evaluator. Its equations are mirrored in the viewport shader.</summary>
public sealed class SurfaceLayers
{
    public SurfaceStyle Style { get; }
    readonly SurfaceImage? _color, _rough, _bump, _decal;
    public SurfaceLayers(SurfaceStyle? style)
    {
        Style = (style ?? new()).Validated();
        _color = Style.ColorMap is { } c ? SurfaceImage.Decode(c) : null;
        _rough = Style.RoughnessMap is { } r ? SurfaceImage.Decode(r) : null;
        _bump = Style.BumpMap is { } b ? SurfaceImage.Decode(b) : null;
        _decal = Style.DecalMap is { } d ? SurfaceImage.Decode(d, keepAlpha: true) : null;
    }
    public static Vector3 Srgb(uint c) => new((c >> 16 & 255) / 255f, (c >> 8 & 255) / 255f, (c & 255) / 255f);
    public static Vector3 Rotate(Vector3 p, float degrees)
    {
        float a = degrees * MathF.PI / 180, c = MathF.Cos(a), s = MathF.Sin(a);
        return new(c * p.X + s * p.Y, -s * p.X + c * p.Y, p.Z);
    }
    public static Vector3 GradientColor(SurfaceStyle s, Vector3 p, Vector3 color)
    {
        if (s.Gradient == ColorGradient.None) return color;
        p = Rotate(p, s.GradientAngle);
        p = s.Axis switch { GradientAxis.X => new(p.Y, p.Z, p.X), GradientAxis.Y => new(p.X, p.Z, p.Y), _ => p };
        float size = Math.Max(s.GradientSize, .1f);
        float t = s.Gradient switch
        {
            ColorGradient.Linear => (p.Z - s.GradientOffset) / size,
            ColorGradient.Radial => new Vector2(p.X, p.Y).Length() / size - s.GradientOffset / size,
            _ => p.Length() / size - s.GradientOffset / size
        };
        t = Math.Clamp(t, 0, 1); t = t * t * (3 - 2 * t);
        if (s.Gradient != ColorGradient.Freeform) return Vector3.Lerp(color, Srgb(s.Color2), t);
        // Four smooth corner colors in the plane perpendicular to the chosen axis.
        float u = Math.Clamp((p.X - s.GradientOffset) / size, 0, 1), v = Math.Clamp((p.Y - s.GradientOffset) / size, 0, 1);
        u = u * u * (3 - 2 * u); v = v * v * (3 - 2 * v);
        return Vector3.Lerp(Vector3.Lerp(color, Srgb(s.Color2), u), Vector3.Lerp(Srgb(s.Color3), Srgb(s.Color4), u), v);
    }
    static float Hash(Vector3 p)
    {
        p = new(p.X - MathF.Floor(p.X / 289) * 289, p.Y - MathF.Floor(p.Y / 289) * 289, p.Z - MathF.Floor(p.Z / 289) * 289);
        float n = (p.X * 34 + 1) * p.X; n -= MathF.Floor(n / 289) * 289; n += p.Y;
        n = (n * 34 + 1) * n; n -= MathF.Floor(n / 289) * 289; n += p.Z;
        n = (n * 34 + 1) * n;
        n = n - MathF.Floor(n / 289) * 289; return n / 289;
    }
    public static float Noise(Vector3 p)
    {
        var i = new Vector3(MathF.Floor(p.X), MathF.Floor(p.Y), MathF.Floor(p.Z)); var f = p - i;
        f = f * f * (new Vector3(3) - 2 * f);
        float L(float a, float b, float t) => a + (b - a) * t;
        return L(L(L(Hash(i), Hash(i + Vector3.UnitX), f.X), L(Hash(i + Vector3.UnitY), Hash(i + new Vector3(1, 1, 0)), f.X), f.Y),
            L(L(Hash(i + Vector3.UnitZ), Hash(i + new Vector3(1, 0, 1)), f.X), L(Hash(i + new Vector3(0, 1, 1)), Hash(i + Vector3.One), f.X), f.Y), f.Z);
    }
    public static float WeatherMask(SurfaceStyle s, Vector3 local)
    {
        if (s.Weathering == WeatheringKind.None || s.Coverage <= 0) return 0;
        var p = local / Math.Max(s.PatchSize, .1f) + new Vector3((s.Seed % 997) * .173f, .37f, 1.13f);
        float n = .65f * Noise(p) + .25f * Noise(p * 2.03f) + .1f * Noise(p * 5.17f);
        float t = Math.Clamp((s.Coverage * 1.2f - .1f - n) / .12f + .5f, 0, 1);
        t = t * t * (3 - 2 * t);
        return t * (s.BottomOnly ? Math.Clamp(1 - local.Z / Math.Max(s.WeatherHeight, .1f), 0, 1) : 1);
    }
    static Vector3 ProjectionAxis(Vector3 p, GradientAxis axis) => axis switch { GradientAxis.X => new(p.Y, p.Z, p.X), GradientAxis.Y => new(p.X, p.Z, p.Y), _ => p };
    public static Vector3 MapPosition(SurfaceStyle s, Vector3 local) => ProjectionAxis(Rotate((local - new Vector3(s.TextureOffsetX, s.TextureOffsetY, s.TextureOffsetZ)) / s.TextureSize, s.TextureAngle), s.TextureAxis);
    public static Vector3 MapNormal(SurfaceStyle s, Vector3 normal) => ProjectionAxis(Rotate(normal, s.TextureAngle), s.TextureAxis);
    public static Vector3 SampleImage(SurfaceImage image, SurfaceStyle s, Vector3 local, Vector3 normal)
    {
        var p = MapPosition(s, local); var n = MapNormal(s, normal);
        Vector3 Sample(float u, float v) => image.Sample(u, v / s.TextureAspect, s.TextureRepeat);
        if (s.Projection == TextureProjection.Planar) return Sample(p.X, p.Y);
        if (s.Projection == TextureProjection.Cylindrical) return Sample(MathF.Atan2(p.Y, p.X) / (2 * MathF.PI) + .5f, p.Z);
        var w = Vector3.Abs(n); w *= w; w *= w; w /= Math.Max(w.X + w.Y + w.Z, 1e-6f);
        return Sample(p.Y, p.Z) * w.X + Sample(p.X, p.Z) * w.Y + Sample(p.X, p.Y) * w.Z;
    }
    /// <summary>Planar-projected decal sample (rgb premultiplied by its alpha). Outside the image or fully transparent pixels return black, so the base shows through; Repeat tiles the artwork.</summary>
    public static Vector4 SampleDecal(SurfaceImage image, SurfaceStyle s, Vector3 local)
    {
        var p = MapPosition(s, local); float u = p.X, v = p.Y / s.TextureAspect;
        if (!s.TextureRepeat && (u < 0 || u > 1 || v < 0 || v > 1)) return Vector4.Zero;
        var d = image.Sample4(u, v, s.TextureRepeat);
        return new Vector4(d.X * d.W, d.Y * d.W, d.Z * d.W, d.W);
    }
    float Height(Vector3 local, Vector3 normal)
    {
        if (_bump != null) return Vector3.Dot(SampleImage(_bump, Style, local, normal), new Vector3(.2126f, .7152f, .0722f));
        return Noise(local / Style.PatchSize * 8 + new Vector3(Style.Seed % 997));
    }
    public SurfaceSample Evaluate(Vector3 local, Vector3 normal, Vector3 color, float metal, float roughness)
    {
        var s = Style;
        color = GradientColor(s, local, color);
        if (_color != null) color *= SampleImage(_color, s, local, normal);
        if (_decal != null) { var d = SampleDecal(_decal, s, local); color = d.W >= 1 ? new Vector3(d.X, d.Y, d.Z) : color * (1 - d.W) + new Vector3(d.X, d.Y, d.Z); }
        if (_rough != null) roughness = Vector3.Dot(SampleImage(_rough, s, local, normal), new Vector3(.2126f, .7152f, .0722f));
        float mask = WeatherMask(s, local), detail = Noise(local / s.PatchSize * 3.1f + new Vector3(s.Seed % 997));
        var weather = s.Weathering == WeatheringKind.Rust ? Vector3.Lerp(new(.18f, .055f, .018f), new(.65f, .28f, .075f), detail) : Vector3.Lerp(new(.12f, .095f, .065f), new(.37f, .30f, .20f), detail);
        color = Vector3.Lerp(color, weather, mask); metal *= 1 - mask;
        roughness += (s.WeatheringRoughness - roughness) * mask;
        if (s.Bump > 0)
        {
            float e = Math.Max((_bump == null ? s.PatchSize / 8 : s.TextureSize / Math.Max(_bump.Width, _bump.Height)) * .25f, .001f);
            var g = new Vector3(Height(local + Vector3.UnitX * e, normal) - Height(local - Vector3.UnitX * e, normal), Height(local + Vector3.UnitY * e, normal) - Height(local - Vector3.UnitY * e, normal), Height(local + Vector3.UnitZ * e, normal) - Height(local - Vector3.UnitZ * e, normal)) / (2 * e);
            normal = Vector3.Normalize(normal - (g - normal * Vector3.Dot(g, normal)) * s.Bump * .2f);
        }
        return new(Vector3.Clamp(color, Vector3.Zero, Vector3.One), Math.Clamp(metal, 0, 1), Math.Clamp(roughness, .06f, 1), normal);
    }
}
