using System;

namespace Prism.Core;

public enum ColorGradient { None, Linear, Radial, Spherical, Freeform }
public enum WeatheringKind { None, Dirt, Rust }
public enum GradientAxis { X, Y, Z }
public enum TextureProjection { Triplanar, Planar, Cylindrical }

/// <summary>Immutable visual layers. Coordinates are millimetres relative to the body's bounding-box minimum.
/// Embedded images travel with the document; none of these values affect engineering properties.</summary>
public sealed record SurfaceStyle
{
    public ColorGradient Gradient { get; init; }
    public GradientAxis Axis { get; init; } = GradientAxis.Z;
    public uint Color2 { get; init; } = 0xFF3478DB;
    public uint Color3 { get; init; } = 0xFFE9B26B;
    public uint Color4 { get; init; } = 0xFFB777CB;
    public float GradientSize { get; init; } = 50;
    public float GradientOffset { get; init; }
    public float GradientAngle { get; init; }
    public WeatheringKind Weathering { get; init; }
    public float Coverage { get; init; } = .3f;
    public float PatchSize { get; init; } = 8;
    public float WeatheringRoughness { get; init; } = .85f;
    public float WeatherHeight { get; init; } = 50;
    public bool BottomOnly { get; init; }
    public int Seed { get; init; }
    public float Bump { get; init; }
    public float Coat { get; init; }
    public float CoatRoughness { get; init; } = .15f;
    public float TextureSize { get; init; } = 25;
    public float TextureAngle { get; init; }
    public TextureProjection Projection { get; init; }
    public GradientAxis TextureAxis { get; init; } = GradientAxis.Z;
    public float TextureOffsetX { get; init; }
    public float TextureOffsetY { get; init; }
    public float TextureOffsetZ { get; init; }
    public float TextureAspect { get; init; } = 1;
    public bool TextureRepeat { get; init; } = true;
    public float Transmission { get; init; }
    public float Ior { get; init; } = 1.5f;
    public uint AbsorptionColor { get; init; } = 0xFFFFFFFF;
    public float AbsorptionDistance { get; init; } = 100;
    public float Anisotropy { get; init; }
    public float AnisotropyAngle { get; init; }
    public string? ColorMap { get; init; }
    public string? RoughnessMap { get; init; }
    public string? BumpMap { get; init; }
    /// <summary>Base64 image drawn on top of the colour with its own alpha, planar-projected by the texture placement (outside the image the surface shows through when Repeat is off).</summary>
    public string? DecalMap { get; init; }
    public bool Override { get; init; }

    public SurfaceStyle Validated() => this with
    {
        Gradient = Enum.IsDefined(Gradient) ? Gradient : ColorGradient.None,
        Axis = Enum.IsDefined(Axis) ? Axis : GradientAxis.Z,
        Projection = Enum.IsDefined(Projection) ? Projection : TextureProjection.Triplanar,
        TextureAxis = Enum.IsDefined(TextureAxis) ? TextureAxis : GradientAxis.Z,
        Weathering = Enum.IsDefined(Weathering) ? Weathering : WeatheringKind.None,
        GradientSize = Safe(GradientSize, 50, .1f, 10000),
        GradientOffset = Safe(GradientOffset, 0, -10000, 10000),
        GradientAngle = Safe(GradientAngle, 0, -360, 360),
        Coverage = Safe(Coverage, .3f, 0, 1), PatchSize = Safe(PatchSize, 8, .1f, 10000),
        WeatheringRoughness = Safe(WeatheringRoughness, .85f, .06f, 1),
        WeatherHeight = Safe(WeatherHeight, 50, .1f, 10000),
        Bump = Safe(Bump, 0, 0, 1), Coat = Safe(Coat, 0, 0, 1),
        CoatRoughness = Safe(CoatRoughness, .15f, .06f, 1),
        TextureSize = Safe(TextureSize, 25, .1f, 10000), TextureAngle = Safe(TextureAngle, 0, -360, 360),
        TextureOffsetX = Safe(TextureOffsetX, 0, -100000, 100000), TextureOffsetY = Safe(TextureOffsetY, 0, -100000, 100000), TextureOffsetZ = Safe(TextureOffsetZ, 0, -100000, 100000),
        TextureAspect = Safe(TextureAspect, 1, .01f, 100), Transmission = Safe(Transmission, 0, 0, 1),
        Ior = Safe(Ior, 1.5f, 1, 3), AbsorptionDistance = Safe(AbsorptionDistance, 100, .01f, 100000),
        Anisotropy = Safe(Anisotropy, 0, 0, .95f), AnisotropyAngle = Safe(AnisotropyAngle, 0, -360, 360)
    };
    static float Safe(float v, float fallback, float min, float max) => float.IsFinite(v) ? Math.Clamp(v, min, max) : fallback;
}
