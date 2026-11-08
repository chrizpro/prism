using System.Text.Json;
using Prism.Core;
using Xunit;

namespace Prism.UnitTests.Appearance;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/appearance-studio/README.md">Reusable looks and user-defined engineering materials.</seealso>
[Trait("Feature", "Appearance")]
[Trait("Layer", "Unit")]
public class CustomAppearanceTests
{
    [Fact(DisplayName = "APP-U020 | Saved bitmap appearance roundtrips and applies without replacing engineering data")]
    [Trait("TestId", "APP-U020")]
    public void SavedLookRetainsImagesButNotSourceMaterial()
    {
        var source = Materials.Find("6061")!.Look with { Pattern = SurfacePattern.Wood, PatternScale = 8, PatternAngle = 31,
            Surface = new() { ColorMap = "embedded-color", RoughnessMap = "embedded-roughness", BumpMap = "embedded-height",
                Bump = .7f, TextureSize = 21, TextureOffsetX = 13, Projection = TextureProjection.Cylindrical, Coat = .6f } };
        var saved = SavedAppearance.Capture("  Wood finish  ", 0xFFFFFFFF, source);
        var json = JsonSerializer.Serialize(saved);
        Assert.DoesNotContain("PhysicalSnapshot", json);
        Assert.DoesNotContain("6061", json);
        var restored = JsonSerializer.Deserialize<SavedAppearance>(json)!;
        var target = Materials.Find("304")!.Look;
        var applied = restored.ApplyTo(target);
        Assert.Same(target.PhysicalSnapshot, applied.PhysicalSnapshot);
        Assert.Equal(target.MaterialId, applied.MaterialId);
        Assert.Equal("Wood finish · Custom", restored.DisplayName);
        Assert.Equal(source.Surface with { Override = true }, applied.Surface);
        Assert.Equal(source.Pattern, applied.Pattern);
        Assert.Equal(source.PatternScale, applied.PatternScale);
        Assert.Equal(source.PatternAngle, applied.PatternAngle);
        Assert.Equal(restored, JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(new AppSettings { SavedAppearances = [restored] }))!.SavedAppearances.Single());
    }

    [Fact(DisplayName = "APP-U021 | Custom materials discard source-check claims and preserve model limitations through serialization")]
    [Trait("TestId", "APP-U021")]
    public void CustomProvenanceTravelsWithSnapshot()
    {
        var source = Materials.Find("oak")!;
        var m = CustomMaterial.Create(source with { Density = .8 }, "My oak", "Supplier sheet");
        Assert.True(m.IsCustom); Assert.Equal("My oak · Custom", m.DisplayName);
        Assert.False(m.Evidence.IsotropicStructural); Assert.False(m.Evidence.IsotropicThermal);
        Assert.Equal(source.Evidence.StrengthBasis, m.Evidence.StrengthBasis);
        Assert.Equal("User-defined — not verified by Prism", m.Evidence.CheckedProperties);
        Assert.Equal("Supplier sheet", m.Evidence.Source);
        var restored = JsonSerializer.Deserialize<Prism.Core.Appearance>(JsonSerializer.Serialize(m.Look))!;
        Assert.Equal(m, restored.PhysicalSnapshot); Assert.True(restored.PhysicalSnapshot!.IsCustom);
        Assert.Equal(m, JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(new AppSettings { CustomMaterials = [m] }))!.CustomMaterials.Single());
        Assert.Equal(.70, source.Density); // creating a copy cannot edit the library entry
    }

    [Theory(DisplayName = "APP-U022 | Custom material input rejects invalid physical data")]
    [Trait("TestId", "APP-U022")]
    [InlineData(0, 200, .3, 40)] [InlineData(1, double.NaN, .3, 40)]
    [InlineData(1, 200, .5, 40)] [InlineData(1, 200, -1, 40)]
    [InlineData(1, 200, .3, double.PositiveInfinity)]
    public void InvalidEngineeringDataIsRejected(double density, double modulus, double poisson, double temperature)
    {
        var m = Materials.Default with { Density = density, Youngs = modulus, Poisson = poisson, MaxTemp = temperature };
        Assert.Throws<ArgumentException>(() => CustomMaterial.Create(m, "Invalid", ""));
    }
    [Fact(DisplayName = "APP-U023 | Custom copies preserve source glass and wood defaults without marking them as explicit overrides")]
    [Trait("TestId", "APP-U023")]
    public void CopiesKeepSourceVisualDefaults()
    {
        var glass = CustomMaterial.Create(Materials.Find("glass")!, "My glass", "");
        Assert.Equal(1, glass.Look.Surface!.Transmission);
        Assert.False(glass.Look.Surface.Override);
        var wood = CustomMaterial.Create(Materials.Find("oak")!, "My wood", "");
        Assert.Equal(SurfacePattern.Wood, wood.Look.Pattern);
        Assert.False(wood.Look.Surface!.Override);
    }
}
