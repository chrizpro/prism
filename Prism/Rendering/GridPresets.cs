using System.Collections.Generic;
using System.Linq;
using Prism.Core;

namespace Prism.Rendering;

/// <summary>How the 3D view's background and grid look. The shaders read these numbers; Settings shows them as swatches.</summary>
public sealed record GridPreset(
    string Id, string Name,
    (float r, float g, float b) Top, (float r, float g, float b) Bottom,
    (float r, float g, float b) Minor, (float r, float g, float b) Major,
    float MinorAlpha, float MajorAlpha,
    bool Dots = false, float Glow = 0f, float Fade = 1f)
{
    /// <summary>The classic look swaps to a pale version when the app is in the light theme.</summary>
    public GridPreset? LightVariant { get; init; }

    public static readonly GridPreset Classic = new("classic", "Classic", (0.20f, 0.235f, 0.29f), (0.075f, 0.09f, 0.115f), (0.62f, 0.70f, 0.82f), (0.62f, 0.70f, 0.82f), 0.10f, 0.22f)
    {
        LightVariant = new("classic", "Classic", (0.94f, 0.955f, 0.975f), (0.78f, 0.81f, 0.86f), (0.28f, 0.36f, 0.48f), (0.28f, 0.36f, 0.48f), 0.26f, 0.57f),
    };

    public static readonly IReadOnlyList<GridPreset> All =
    [
        Classic,
        new("blueprint", "Blueprint", (0.10f, 0.33f, 0.62f), (0.03f, 0.12f, 0.30f), (0.85f, 0.93f, 1.00f), (0.95f, 0.98f, 1.00f), 0.16f, 0.46f),
        new("neon", "Neon", (0.12f, 0.03f, 0.24f), (0.015f, 0.01f, 0.05f), (0.95f, 0.25f, 0.85f), (0.25f, 0.95f, 1.00f), 0.32f, 0.80f, Glow: 1f, Fade: 1.4f),
        new("dusk", "Dusk", (0.33f, 0.17f, 0.33f), (0.06f, 0.04f, 0.13f), (1.00f, 0.60f, 0.40f), (1.00f, 0.78f, 0.52f), 0.15f, 0.38f, Glow: 0.35f, Fade: 1.3f),
        new("terminal", "Terminal", (0.03f, 0.09f, 0.06f), (0.0f, 0.02f, 0.01f), (0.20f, 1.00f, 0.50f), (0.35f, 1.00f, 0.65f), 0.15f, 0.42f, Glow: 0.30f),
        new("dots", "Dots", (0.17f, 0.19f, 0.23f), (0.06f, 0.07f, 0.09f), (0.80f, 0.85f, 0.95f), (0.90f, 0.93f, 1.00f), 0.38f, 0.85f, Dots: true),
        new("paper", "Graph paper", (0.97f, 0.97f, 0.93f), (0.89f, 0.90f, 0.85f), (0.35f, 0.55f, 0.75f), (0.20f, 0.40f, 0.65f), 0.30f, 0.62f),
        new("studio", "Studio", (0.66f, 0.68f, 0.72f), (0.30f, 0.31f, 0.35f), (1.00f, 1.00f, 1.00f), (1.00f, 1.00f, 1.00f), 0.06f, 0.16f, Fade: 1.6f),
    ];

    /// <summary>The preset in Settings, with the light-theme variant picked when it has one.</summary>
    public static GridPreset Current(bool lightTheme)
    {
        var p = All.FirstOrDefault(x => x.Id == AppSettings.Current.GridStyle) ?? Classic;
        return lightTheme && p.LightVariant != null ? p.LightVariant : p;
    }
}
