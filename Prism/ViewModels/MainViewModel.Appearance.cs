using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Prism.Core;
namespace Prism.ViewModels;
public sealed record FinishPreset(string Name, uint Color, double Metal, double Gloss, SurfacePattern Pattern);
public sealed partial class MainViewModel
{
    public IReadOnlyList<FinishPreset> FinishPresets { get; } =
    [
        new("Brushed aluminum", 0xFFB9C6D6, 1, .35, SurfacePattern.Brushed),
        new("Satin aluminum", 0xFFC0C7CF, 1, .2, SurfacePattern.Plain),
        new("Polished steel", 0xFFBBC3CC, 1, .92, SurfacePattern.Plain),
        new("Black anodized", 0xFF303740, .8, .25, SurfacePattern.Brushed),
        new("Brass", 0xFFD2A94A, 1, .55, SurfacePattern.Plain),
        new("Cast aluminum", 0xFF9CA5AD, .8, .12, SurfacePattern.Cast),
    ];
    [ObservableProperty] public partial bool CanApplyAppearance { get; set; }
    [ObservableProperty] public partial uint AppearanceColor { get; set; } = 0xFFB9C6D6;
    public SurfacePattern AppearancePattern => (SurfacePattern)LookPattern;
    partial void OnAppearanceColorChanged(uint value) { OnPropertyChanged(nameof(ActiveFinishPreset)); OnPropertyChanged(nameof(PickerColor)); OnPropertyChanged(nameof(ColorValue)); }
    public Avalonia.Media.Color PickerColor => Avalonia.Media.Color.FromUInt32(AppearanceColor);
    public FinishPreset? ActiveFinishPreset => System.Linq.Enumerable.FirstOrDefault(FinishPresets, p =>
        !HasAdvancedSurface && p.Color == AppearanceColor && System.Math.Abs(p.Metal - LookMetal) < .001 &&
        System.Math.Abs(p.Gloss - LookGloss) < .001 && (int)p.Pattern == LookPattern);
    public void ApplyFinishPreset(FinishPreset preset)
    {
        var targets = TargetsForLook();
        if (targets.Count == 0) return;
        Session.BeginGroup();
        try
        {
            foreach (var target in targets)
                Session.Update(target.Id, o => o.WithColor(preset.Color).WithLook(o.Look with
                {
                    Metal = (float)preset.Metal, Gloss = (float)preset.Gloss,
                    Pattern = preset.Pattern, PatternScale = 12, PatternAngle = 0, Surface = new SurfaceStyle { Override = true },
                }), label: "Finish: " + preset.Name);
        }
        finally { Session.EndGroup("Finish: " + preset.Name); }
        SyncLook(); Session.Say(preset.Name + " finish applied.");
    }


    public ObservableCollection<uint> RecentAppearanceColors { get; } = [];
    public ObservableCollection<uint> SavedAppearanceColors { get; } = new(AppSettings.Current.SavedAppearanceColors.Take(24));
    public void SaveAppearanceColor()
    {
        if (SavedAppearanceColors.Contains(AppearanceColor)) return;
        SavedAppearanceColors.Insert(0, AppearanceColor); if (SavedAppearanceColors.Count > 24) SavedAppearanceColors.RemoveAt(24);
        PersistSavedColors();
    }
    public void RemoveSavedAppearanceColor(uint color) { SavedAppearanceColors.Remove(color); PersistSavedColors(); }
    void PersistSavedColors() { AppSettings.Current.SavedAppearanceColors = SavedAppearanceColors.ToList(); AppSettings.Save(false); }
    void RememberAppearanceColor()
    {
        if (_appearanceGesture || !CanApplyAppearance) return;
        RecentAppearanceColors.Remove(AppearanceColor); RecentAppearanceColors.Insert(0, AppearanceColor);
        if (RecentAppearanceColors.Count > 8) RecentAppearanceColors.RemoveAt(8);
    }
    bool HasAdvancedSurface => SelectedSurface.Transmission > 0 || SelectedSurface.Anisotropy > 0 || SelectedSurface.Gradient != ColorGradient.None || SelectedSurface.Weathering != WeatheringKind.None || SelectedSurface.Coat > 0 || SelectedSurface.Bump > 0 || SelectedSurface.ColorMap != null || SelectedSurface.RoughnessMap != null || SelectedSurface.BumpMap != null;
    public double ColorValue { get => ColorCode.Hsv(AppearanceColor).V; set { var hsv = ColorCode.Hsv(AppearanceColor); ApplyColor(ColorCode.FromHsv(hsv.H, hsv.S, value)); } }
    public static Func<Task<uint?>>? PickScreenColor { get; set; }

    [ObservableProperty] public partial SurfaceStyle SelectedSurface { get; set; } = new();

    public IReadOnlyList<string> GradientModes { get; } = ["Solid color", "Linear", "Radial", "Spherical", "Freeform (four colors)"];

    public IReadOnlyList<string> GradientAxes { get; } = ["X", "Y", "Z"];

    public IReadOnlyList<string> WeatheringModes { get; } = ["None", "Dirt", "Rust"];

    public bool HasColorGradient => GradientMode != 0;
    public bool HasFreeformGradient => GradientMode == 4;
    public bool HasWeathering => WeatheringMode != 0;
    public string AppearanceStatus => TargetsForLook().FirstOrDefault()?.HasAppearanceOverride == true ? "Custom appearance · visual override" : "Material / default appearance";

    public string ColorMapStatus => SelectedSurface.ColorMap == null ? "No color image" : "Color image embedded";

    public string RoughnessMapStatus => SelectedSurface.RoughnessMap == null ? "No roughness image" : "Roughness image embedded";

    public string BumpMapStatus => SelectedSurface.BumpMap == null ? "No bump image" : "Bump image embedded";

    bool _editingAppearance, _appearanceGesture;

    public void BeginAppearanceEdit() { if (_appearanceGesture) return; _appearanceGesture = true; Session.BeginGroup(); }

    public void EndAppearanceEdit() { if (!_appearanceGesture) return; _appearanceGesture = false; Session.EndGroup("Appearance"); RememberAppearanceColor(); }

    void EditAppearance(Func<SceneObject, SceneObject> edit, string label)

    {

        var targets = TargetsForLook(); if (targets.Count == 0) return;

        _editingAppearance = true; Session.BeginGroup();

        try { foreach (var o in targets) Session.Update(o.Id, edit, label: label); }

        finally { Session.EndGroup(label); _editingAppearance = false; }

        SyncLook();

    }

    void EditSurface(Func<SurfaceStyle, SurfaceStyle> edit) => EditAppearance(o => o.WithLook(o.Look with { Surface = (edit(o.Look.Surface ?? new()) with { Override = true }).Validated() }), "Appearance layers");

    public int GradientMode { get => (int)SelectedSurface.Gradient; set { if (GradientMode != value) EditSurface(s => s with { Gradient = (ColorGradient)value }); } }

    public int GradientDirection { get => (int)SelectedSurface.Axis; set { if (GradientDirection != value) EditSurface(s => s with { Axis = (GradientAxis)value }); } }

    public double GradientSize { get => SelectedSurface.GradientSize; set { if (GradientSize != value) EditSurface(s => s with { GradientSize = (float)value }); } }

    public double GradientOffset { get => SelectedSurface.GradientOffset; set { if (GradientOffset != value) EditSurface(s => s with { GradientOffset = (float)value }); } }

    public double GradientAngle { get => SelectedSurface.GradientAngle; set { if (GradientAngle != value) EditSurface(s => s with { GradientAngle = (float)value }); } }

    public int WeatheringMode { get => (int)SelectedSurface.Weathering; set { if (WeatheringMode != value) EditSurface(s => s with { Weathering = (WeatheringKind)value }); } }

    public double WeatherCoverage { get => SelectedSurface.Coverage; set { if (WeatherCoverage != value) EditSurface(s => s with { Coverage = (float)value }); } }

    public double WeatherSize { get => SelectedSurface.PatchSize; set { if (WeatherSize != value) EditSurface(s => s with { PatchSize = (float)value }); } }

    public double WeatherHeight { get => SelectedSurface.WeatherHeight; set { if (WeatherHeight != value) EditSurface(s => s with { WeatherHeight = (float)value }); } }
    public double WeatherRoughness { get => SelectedSurface.WeatheringRoughness; set { if (WeatherRoughness != value) EditSurface(s => s with { WeatheringRoughness = (float)value }); } }

    public bool WeatherBottom { get => SelectedSurface.BottomOnly; set { if (WeatherBottom != value) EditSurface(s => s with { BottomOnly = value }); } }

    public int WeatherSeed { get => SelectedSurface.Seed; set { if (WeatherSeed != value) EditSurface(s => s with { Seed = value }); } }

    public double SurfaceBump { get => SelectedSurface.Bump; set { if (SurfaceBump != value) EditSurface(s => s with { Bump = (float)value }); } }

    public double CoatAmount { get => SelectedSurface.Coat; set { if (CoatAmount != value) EditSurface(s => s with { Coat = (float)value }); } }

    public double CoatRoughness { get => SelectedSurface.CoatRoughness; set { if (CoatRoughness != value) EditSurface(s => s with { CoatRoughness = (float)value }); } }

    public double TextureSize { get => SelectedSurface.TextureSize; set { if (TextureSize != value) EditSurface(s => s with { TextureSize = (float)value }); } }

    public double TextureAngle { get => SelectedSurface.TextureAngle; set { if (TextureAngle != value) EditSurface(s => s with { TextureAngle = (float)value }); } }

    public string GradientColor2 { get => "#" + (SelectedSurface.Color2 & 0xFFFFFF).ToString("X6"); set { if (ColorCode.TryParse(value, out var c)) EditSurface(s => s with { Color2 = c }); else Session.Say("Use HEX, rgb(...), hsl(...) or hsv(...).", true); } }

    public string GradientColor3 { get => "#" + (SelectedSurface.Color3 & 0xFFFFFF).ToString("X6"); set { if (ColorCode.TryParse(value, out var c)) EditSurface(s => s with { Color3 = c }); else Session.Say("Use HEX, rgb(...), hsl(...) or hsv(...).", true); } }

    public string GradientColor4 { get => "#" + (SelectedSurface.Color4 & 0xFFFFFF).ToString("X6"); set { if (ColorCode.TryParse(value, out var c)) EditSurface(s => s with { Color4 = c }); else Session.Say("Use HEX, rgb(...), hsl(...) or hsv(...).", true); } }

    public double LookRoughness { get => 1 - LookGloss; set => LookGloss = 1 - Math.Clamp(value, 0, 1); }

    partial void OnSelectedSurfaceChanged(SurfaceStyle value)

    {
        NotifyRenderingAppearance();

        OnPropertyChanged(nameof(GradientMode));

        OnPropertyChanged(nameof(GradientDirection));

        OnPropertyChanged(nameof(GradientSize));

        OnPropertyChanged(nameof(GradientOffset));

        OnPropertyChanged(nameof(GradientAngle));

        OnPropertyChanged(nameof(WeatheringMode));

        OnPropertyChanged(nameof(WeatherCoverage));

        OnPropertyChanged(nameof(WeatherSize));

        OnPropertyChanged(nameof(WeatherRoughness));

        OnPropertyChanged(nameof(WeatherBottom));

        OnPropertyChanged(nameof(WeatherSeed));

        OnPropertyChanged(nameof(SurfaceBump));

        OnPropertyChanged(nameof(CoatAmount));

        OnPropertyChanged(nameof(CoatRoughness));

        OnPropertyChanged(nameof(TextureSize));

        OnPropertyChanged(nameof(TextureAngle));

        OnPropertyChanged(nameof(GradientColor2));

        OnPropertyChanged(nameof(GradientColor3));

        OnPropertyChanged(nameof(GradientColor4));

        OnPropertyChanged(nameof(AppearanceStatus));

        OnPropertyChanged(nameof(ColorMapStatus));

        OnPropertyChanged(nameof(RoughnessMapStatus));

        OnPropertyChanged(nameof(BumpMapStatus));

    }

    public async Task ImportSurfaceMap(string kind)

    {

        if (PickOpenFile == null) return;

        var path = await PickOpenFile("Choose " + kind + " image", ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.webp"]);

        if (path == null) return;

        try

        {

            var data = Rendering.SurfaceImage.Import(path);

            EditSurface(s => kind switch { "color" => s with { ColorMap = data }, "roughness" => s with { RoughnessMap = data }, _ => s with { BumpMap = data, Bump = Math.Max(s.Bump, .3f) } });

        }

        catch (Exception e) { Session.Say("Could not import image: " + e.Message, true); }

    }

    public void ClearSurfaceMap(string kind) => EditSurface(s => kind switch { "color" => s with { ColorMap = null }, "roughness" => s with { RoughnessMap = null }, _ => s with { BumpMap = null } });

    public void ResetAppearance()

    {

        var targets = TargetsForLook();

        if (targets.Count == 0) return;

        Session.BeginGroup();

        try

        {

            foreach (var target in targets)

                Session.Update(target.Id, o => o.WithColor(o.Material?.Color ?? Palette.DefaultFor(o.Kind, 0))

                    .WithLook(o.Material?.Look ?? Appearance.Default), label: "Reset finish");

        }

        finally { Session.EndGroup("Reset finish"); }

        SyncLook();

    }

}
