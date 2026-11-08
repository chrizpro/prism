using System.Text.Json;
using Prism.Core;
using Xunit;

namespace Prism.UnitTests.Appearance;

[CollectionDefinition("Appearance preferences", DisableParallelization = true)]
public class AppearancePreferencesCollection { }

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/appearance-studio/README.md">Local custom libraries and persistence.</seealso>
[Collection("Appearance preferences")]
[Trait("Feature", "Appearance")]
[Trait("Layer", "Unit")]
public class AppearancePreferencesTests
{
    [Fact(DisplayName = "APP-U024 | Custom libraries persist and a failed save preserves the previous settings file")]
    [Trait("TestId", "APP-U024")]
    public void FailedSaveKeepsPreviousLibraries()
    {
        string folder = Path.Combine(Path.GetTempPath(), "prism-appearance-" + Guid.NewGuid());
        string path = Path.Combine(folder, "settings.json");
        var previousPath = Environment.GetEnvironmentVariable("PRISM_SETTINGS_FILE");
        var preferences = AppSettings.Current;
        var appearances = preferences.SavedAppearances;
        var materials = preferences.CustomMaterials;
        try
        {
            Environment.SetEnvironmentVariable("PRISM_SETTINGS_FILE", path);
            var m = CustomMaterial.Create(Materials.Find("glass")!, "My glass", "My source");
            var look = SavedAppearance.Capture("My bitmap", 0xFFFFFFFF, m.Look with { Surface = new() { ColorMap = "embedded-image", Bump = .4f } });
            preferences.CustomMaterials = [m]; preferences.SavedAppearances = [look];
            Assert.True(AppSettings.TrySave(false));
            var previous = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<AppSettings>(previous)!;
            Assert.Equal(m, Assert.Single(loaded.CustomMaterials));
            Assert.Equal(look, Assert.Single(loaded.SavedAppearances));
            preferences.CustomMaterials.Add(m with { Density = double.PositiveInfinity });
            Assert.False(AppSettings.TrySave(false));
            Assert.Equal(previous, File.ReadAllText(path));
            Assert.Empty(Directory.GetFiles(folder, "*.tmp"));
        }
        finally
        {
            preferences.SavedAppearances = appearances; preferences.CustomMaterials = materials;
            Environment.SetEnvironmentVariable("PRISM_SETTINGS_FILE", previousPath);
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }
}
