using System;

namespace Prism.Core;

public enum ColorGradient { None, Linear, Radial, Spherical, Freeform }
public enum WeatheringKind { None, Dirt, Rust }
public enum GradientAxis { X, Y, Z }

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
    public string? ColorMap { get; init; }
    public string? RoughnessMap { get; init; }
    public string? BumpMap { get; init; }
    public bool Override { get; init; }

    public SurfaceStyle Validated() => this with
    {
        Gradient = Enum.IsDefined(Gradient) ? Gradient : ColorGradient.None,
        Axis = Enum.IsDefined(Axis) ? Axis : GradientAxis.Z,
        Weathering = Enum.IsDefined(Weathering) ? Weathering : WeatheringKind.None,
        GradientSize = Safe(GradientSize, 50, .1f, 10000),
        GradientOffset = Safe(GradientOffset, 0, -10000, 10000),
        GradientAngle = Safe(GradientAngle, 0, -360, 360),
        Coverage = Safe(Coverage, .3f, 0, 1), PatchSize = Safe(PatchSize, 8, .1f, 10000),
        WeatheringRoughness = Safe(WeatheringRoughness, .85f, .06f, 1),
        WeatherHeight = Safe(WeatherHeight, 50, .1f, 10000),
        Bump = Safe(Bump, 0, 0, 1), Coat = Safe(Coat, 0, 0, 1),
        CoatRoughness = Safe(CoatRoughness, .15f, .06f, 1),
        TextureSize = Safe(TextureSize, 25, .1f, 10000), TextureAngle = Safe(TextureAngle, 0, -360, 360)
    };
    static float Safe(float v, float fallback, float min, float max) => float.IsFinite(v) ? Math.Clamp(v, min, max) : fallback;
}
