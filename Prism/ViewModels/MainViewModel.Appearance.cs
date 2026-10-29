using System.Collections.Generic;
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
    partial void OnAppearanceColorChanged(uint value) => OnPropertyChanged(nameof(ActiveFinishPreset));
    public FinishPreset? ActiveFinishPreset => System.Linq.Enumerable.FirstOrDefault(FinishPresets, p =>
        p.Color == AppearanceColor && System.Math.Abs(p.Metal - LookMetal) < .001 &&
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
                    Pattern = preset.Pattern, PatternScale = 12, PatternAngle = 0,
                }), label: "Finish: " + preset.Name);
        }
        finally { Session.EndGroup("Finish: " + preset.Name); }
        SyncLook(); Session.Say(preset.Name + " finish applied.");
    }

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
