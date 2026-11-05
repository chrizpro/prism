using System;
using System.Collections.Generic;
using Prism.Core;

namespace Prism.ViewModels;

public sealed partial class MainViewModel
{
    public IReadOnlyList<string> TextureProjections { get; } = ["Blended box", "Planar", "Cylindrical"];
    public int TextureProjectionMode { get => (int)SelectedSurface.Projection; set { if (TextureProjectionMode != value) EditSurface(s => s with { Projection = (TextureProjection)value }); } }
    public int TextureProjectionAxis { get => (int)SelectedSurface.TextureAxis; set { if (TextureProjectionAxis != value) EditSurface(s => s with { TextureAxis = (GradientAxis)value }); } }
    public double TextureOffsetX { get => SelectedSurface.TextureOffsetX; set { if (TextureOffsetX != value) EditSurface(s => s with { TextureOffsetX = (float)value }); } }
    public double TextureOffsetY { get => SelectedSurface.TextureOffsetY; set { if (TextureOffsetY != value) EditSurface(s => s with { TextureOffsetY = (float)value }); } }
    public double TextureOffsetZ { get => SelectedSurface.TextureOffsetZ; set { if (TextureOffsetZ != value) EditSurface(s => s with { TextureOffsetZ = (float)value }); } }
    public double TextureAspect { get => SelectedSurface.TextureAspect; set { if (TextureAspect != value) EditSurface(s => s with { TextureAspect = (float)value }); } }
    public bool TextureRepeat { get => SelectedSurface.TextureRepeat; set { if (TextureRepeat != value) EditSurface(s => s with { TextureRepeat = value }); } }
    public double SurfaceTransmission { get => SelectedSurface.Transmission; set { if (SurfaceTransmission != value) EditSurface(s => s with { Transmission = (float)value }); } }
    public double SurfaceIor { get => SelectedSurface.Ior; set { if (SurfaceIor != value) EditSurface(s => s with { Ior = (float)value }); } }
    public double AbsorptionDistance { get => SelectedSurface.AbsorptionDistance; set { if (AbsorptionDistance != value) EditSurface(s => s with { AbsorptionDistance = (float)value }); } }
    public double SurfaceAnisotropy { get => SelectedSurface.Anisotropy; set { if (SurfaceAnisotropy != value) EditSurface(s => s with { Anisotropy = (float)value }); } }
    public double AnisotropyAngle { get => SelectedSurface.AnisotropyAngle; set { if (AnisotropyAngle != value) EditSurface(s => s with { AnisotropyAngle = (float)value }); } }
    public string AbsorptionColor => "#" + (SelectedSurface.AbsorptionColor & 0xFFFFFF).ToString("X6");
    public void ApplyAbsorptionColor(string text)
    {
        if (ColorCode.TryParse(text, out uint color)) EditSurface(s => s with { AbsorptionColor = color });
        else Session.Say("Use a HEX, RGB, HSL or HSV color code for glass tint.", true);
    }
    void NotifyRenderingAppearance()
    {
        foreach (string name in new[] { nameof(TextureProjectionMode), nameof(TextureProjectionAxis), nameof(TextureOffsetX), nameof(TextureOffsetY), nameof(TextureOffsetZ), nameof(TextureAspect), nameof(TextureRepeat), nameof(SurfaceTransmission), nameof(SurfaceIor), nameof(AbsorptionColor), nameof(AbsorptionDistance), nameof(SurfaceAnisotropy), nameof(AnisotropyAngle), nameof(ActiveFinishPreset), nameof(HasColorGradient), nameof(HasFreeformGradient), nameof(HasWeathering), nameof(WeatherHeight) }) OnPropertyChanged(name);
    }
    public void CenterTextureProjection() => EditAppearance(o =>
    {
        var m = o.Mesh;
        var style = (o.Look.Surface ?? new()) with { TextureOffsetX = (float)(m.Max.X - m.Min.X) / 2, TextureOffsetY = (float)(m.Max.Y - m.Min.Y) / 2, TextureOffsetZ = (float)(m.Max.Z - m.Min.Z) / 2, Override = true };
        return o.WithLook(o.Look with { Surface = style });
    }, "Center texture projection");
    public void ApplyTransparentPreset(bool frosted) => EditAppearance(o => o.WithColor(0xFFEEF5FA).WithLook(o.Look with
    {
        Metal = 0, Gloss = frosted ? .65f : 1, Pattern = SurfacePattern.Plain,
        Surface = new() { Transmission = 1, Ior = 1.5f, Override = true }
    }), frosted ? "Frosted glass appearance" : "Clear glass appearance");
}
