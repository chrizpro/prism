using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Prism.Core;
using Prism.Rendering;
using Prism.ViewModels;
using Prism.Views;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Solid;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/appearance-studio/README.md">Appearance studio specification and limitations.</seealso>
[Trait("Feature", "Appearance")]
[Trait("Layer", "Headless")]
public class AppearanceStudioTests
{
    [AvaloniaFact(DisplayName = "APP-H001 | Appearance layers preserve engineering properties and undo together across selected bodies")]
    [Trait("TestId", "APP-H001")]
    public void LayersAndSliderGestureUndoTogether()
    {
        var vm = new MainViewModel(); var s = vm.Session;
        var a = CreateBoxBody(s, new(0, 0, 0), 10, 12, 20); var b = CreateBoxBody(s, new(20, 0, 0), 10, 12, 20);
        s.SelectItems([SelItem.Of(a), SelItem.Of(b)]); vm.ApplyMaterial(Materials.Find("6061")!);
        var previous = s.Current; var material = s.Find(a.Id)!.Material;
        vm.BeginAppearanceEdit();
        vm.LookRoughness = .4; vm.LookRoughness = .6; vm.WeatheringMode = (int)WeatheringKind.Rust; vm.WeatherCoverage = .7;
        vm.EndAppearanceEdit();
        Assert.All(s.Objects, o => { Assert.Equal(material, o.Material); Assert.Equal(.4f, o.Look.Gloss, 5); Assert.Equal(WeatheringKind.Rust, o.Look.Surface!.Weathering); });
        s.Undo(); Assert.Same(previous, s.Current); Assert.All(s.Objects, o => Assert.Null(o.Look.Surface));
        s.Redo(); Assert.All(s.Objects, o => Assert.Equal(.7f, o.Look.Surface!.Coverage, 5));
    }
    [AvaloniaFact(DisplayName = "APP-H002 | Changing engineering material preserves an override and reset restores the new material finish")]
    [Trait("TestId", "APP-H002")]
    public void MaterialChangesPreserveOverrideUntilReset()
    {
        var vm = new MainViewModel(); var s = vm.Session; var body = CreateBoxBody(s, new(0, 0, 0), 10, 10, 10);
        s.SelectItems([SelItem.Of(body)]); vm.ApplyHex("rgb(20,80,220)"); vm.GradientMode = 1; vm.CoatAmount = .5;
        var custom = s.Find(body.Id)!; var material = Materials.Find("304")!;
        vm.ApplyMaterial(material); var assigned = s.Find(body.Id)!;
        Assert.Equal(custom.Color, assigned.Color); Assert.Equal(custom.Look.Surface, assigned.Look.Surface); Assert.Equal(material, assigned.Material);
        var previous = s.Current; vm.ResetAppearance();
        Assert.Equal(material.Color, s.Find(body.Id)!.Color); Assert.Equal(material.Look, s.Find(body.Id)!.Look);
        s.Undo(); Assert.Same(previous, s.Current); Assert.Equal(custom.Look.Surface, s.Find(body.Id)!.Look.Surface);
    }
    [AvaloniaFact(DisplayName = "APP-H003 | Gradients, weathering, coat and render backplate settings survive native document save and reopen")]
    [Trait("TestId", "APP-H003")]
    public void LayersRoundtrip()
    {
        var vm = new MainViewModel(); var s = vm.Session; var body = CreateBoxBody(s, new(0, 0, 0), 10, 10, 10);
        s.SelectItems([SelItem.Of(body)]); vm.ApplyMaterial(Materials.Find("6061")!); vm.GradientMode = 4; vm.GradientSize = 24;
        vm.GradientColor2 = "#FF7700"; vm.WeatheringMode = 1; vm.WeatherBottom = true; vm.SurfaceBump = .35; vm.CoatAmount = .5;
        var expected = s.Find(body.Id)!;
        s.RenderSetup = new() { Background = RenderBg.Image, BackgroundImage = "embedded-fixture", ImageFit = BackgroundFit.Fit, ImageOffsetX = .25, ImageOffsetY = -.1 };
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".prism");
        try
        {
            s.Save(path); var loaded = new CadSession(); loaded.Open(path); var restored = Assert.Single(loaded.Objects);
            Assert.Equal(expected.Color, restored.Color); Assert.Equal(expected.Look, restored.Look); Assert.Equal(expected.Material, restored.Material);
            Assert.Equal(s.RenderSetup, loaded.RenderSetup);
            // Old documents without the additive surface field still load with the original finish.
            var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
            foreach (var obj in json["Objects"]!.AsArray()) obj!.AsObject().Remove("Surface");
            File.WriteAllText(path, json.ToJsonString()); loaded.Open(path); Assert.Null(Assert.Single(loaded.Objects).Look.Surface);
        }
        finally { File.Delete(path); }
    }
    [AvaloniaFact(DisplayName = "APP-H004 | Gradient changes alter CPU render pixels without changing body geometry or physical data")]
    [Trait("TestId", "APP-H004")]
    public void CpuRendererUsesLayers()
    {
        var s = new CadSession(); var body = CreateBoxBody(s, new(0, 0, 0), 20, 20, 20);
        s.Update(body.Id, o => { var m = o.WithMaterial(Materials.Find("6061")!); return m.WithColor(0xFFFF0000).WithLook(m.Look with { Metal = 0, Gloss = .1f }); });
        var settings = new RenderSettings { Ground = false, Width = 24, Height = 24, Samples = 8, Bounces = 2, Background = RenderBg.White };
        s.Camera.Fit(new(0, 0, 0), new(20, 20, 20)); s.Camera.Width = s.Camera.Height = 24;
        byte[] Render()
        {
            var tracer = new PathTracer(settings); Assert.True(tracer.LoadScene(s)); byte[] image = [];
            tracer.Render(s.Camera, (p, _, _, _, _) => image = p.ToArray(), CancellationToken.None); return image;
        }
        var red = Render(); var before = s.Find(body.Id)!;
        s.Update(body.Id, o => o.WithLook(o.Look with { Surface = new() { Gradient = ColorGradient.Linear, Color2 = 0xFF0000FF, GradientOffset = -100, GradientSize = 1 } }));
        var blue = Render(); Assert.NotEqual(red, blue);
        Assert.Same(before.Shape, s.Find(body.Id)!.Shape); Assert.Equal(before.Material, s.Find(body.Id)!.Material);
        Assert.Contains(Enumerable.Range(0, blue.Length / 4), i => blue[i * 4] > blue[i * 4 + 2] + 20);
    }
    [AvaloniaFact(DisplayName = "APP-H005 | Appearance panel starts compact and disables edits without a supported selection")]
    [Trait("TestId", "APP-H005")]
    public void PanelKeepsAdvancedOptionsCollapsed()
    {
        var vm = new MainViewModel(); var panel = new MaterialsPanel { DataContext = vm };
        Assert.False(vm.CanApplyAppearance); Assert.False(panel.FindControl<Button>("ResetFinish")!.IsEnabled);
        foreach (string name in new[] { "GradientOptions", "WeatherOptions", "ImageOptions", "ReflectionOptions" }) Assert.False(panel.FindControl<Expander>(name)!.IsExpanded);
        Assert.NotNull(panel.FindControl<Button>("ChooseColor")!.Flyout);
        Assert.Equal(2, panel.FindControl<TabControl>("StudioTabs")!.Items.Count);
    }

    [AvaloniaFact(DisplayName = "APP-H006 | Material defaults follow reassignment and recognizable legacy overrides remain visual overrides")]
    [Trait("TestId", "APP-H006")]
    public void DefaultsAndLegacyOverridesHaveDifferentPrecedence()
    {
        var vm = new MainViewModel(); var s = vm.Session; var body = CreateBoxBody(s, new(0, 0, 0), 10, 10, 10);
        s.SelectItems([SelItem.Of(body)]); vm.ApplyMaterial(Materials.Find("6061")!); vm.ApplyMaterial(Materials.Find("304")!);
        Assert.Equal(Materials.Find("304")!.Color, s.Find(body.Id)!.Color);
        Assert.False(s.Find(body.Id)!.HasAppearanceOverride);
        s.Update(body.Id, o => o.WithColor(0xFF2244CC)); // Legacy color edit with no additive Surface record.
        vm.ApplyMaterial(Materials.Find("6061")!);
        Assert.Equal(0xFF2244CCu, s.Find(body.Id)!.Color); Assert.Equal("6061", s.Find(body.Id)!.Look.MaterialId);
    }

    [AvaloniaFact(DisplayName = "APP-H007 | Family chips cover the whole library and selecting one narrows the list to that family's grades")]
    [Trait("TestId", "APP-H007")]
    public void FamilyChipsNarrowTheList()
    {
        var vm = new MainViewModel();
        Assert.Equal(1 + Materials.Categories.Count(), vm.MaterialFamilies.Count);
        Assert.Equal(Materials.All.Count, vm.MaterialFamilies.Sum(f => f.Index == 0 ? 0 : f.Count));
        Assert.Equal(Materials.All.Count, vm.MaterialList.Count);

        var steel = vm.MaterialFamilies.Single(f => f.Name == "Steel");
        vm.SelectFamily(steel.Index);
        Assert.True(steel.IsActive);
        Assert.False(vm.MaterialFamilies.First(f => f.Index == 0).IsActive);
        Assert.All(vm.MaterialList, r => Assert.Equal("Steel", r.Material.Category));
        Assert.Equal(Materials.All.Count(m => m.Category == "Steel"), vm.MaterialList.Count);

        vm.MaterialSearch = "mild";
        var row = Assert.Single(vm.MaterialList);
        Assert.Equal("a36", row.Material.Id);

        vm.MaterialSearch = ""; vm.SelectFamily(0);   // back to the whole library
        Assert.Equal(Materials.All.Count, vm.MaterialList.Count);
    }
}
