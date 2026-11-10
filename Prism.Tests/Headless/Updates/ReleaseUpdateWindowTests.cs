using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using System.Text.Json;
using Prism.Core;
using Prism.ViewModels;
using Prism.Views;
using Xunit;

namespace Prism.HeadlessTests.Updates;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/packaging/README.md#checking-for-updates">Desktop update behavior.</seealso>
[Trait("Feature", "Updates")]
[Trait("Layer", "Headless")]
public sealed class ReleaseUpdateWindowTests
{
    [AvaloniaFact(DisplayName = "UPD-H002 | Disabling startup checks persists and Workspace settings can restore them")]
    [Trait("TestId", "UPD-H002")]
    public async Task StartupChecksCanBeDisabledAndRestored()
    {
        string? oldPath = Environment.GetEnvironmentVariable("PRISM_SETTINGS_FILE");
        bool oldValue = AppSettings.Current.CheckForUpdatesOnStartup;
        string path = Path.Combine(Path.GetTempPath(), $"prism-update-{Guid.NewGuid():N}.json");
        Environment.SetEnvironmentVariable("PRISM_SETTINGS_FILE", path);
        var owner = new Window();
        var update = new ReleaseUpdates.Update("v0.3.0-alpha.1", "Prism-windows-x64-Setup.exe");
        var dialog = new ReleaseUpdateWindow(update.Asset, _ => throw new InvalidOperationException("Unexpected download"), update);
        try
        {
            Assert.True(JsonSerializer.Deserialize<AppSettings>("{}")!.CheckForUpdatesOnStartup);
            AppSettings.Current.CheckForUpdatesOnStartup = true;
            owner.Show();
            var closed = dialog.ShowDialog(owner);
            var disable = dialog.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Content, "Turn off startup update checks"));
            disable.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await closed;
            Assert.False(JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path))!.CheckForUpdatesOnStartup);

            var settings = new SettingsWindow(new MainViewModel());
            var startup = settings.FindControl<CheckBox>("StartupUpdates")!;
            Assert.False(startup.IsChecked);
            startup.IsChecked = true;
            startup.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path))!.CheckForUpdatesOnStartup);
        }
        finally
        {
            dialog.Close();
            owner.Close();
            AppSettings.Current.CheckForUpdatesOnStartup = oldValue;
            Environment.SetEnvironmentVariable("PRISM_SETTINGS_FILE", oldPath);
            File.Delete(path);
        }
    }

    [AvaloniaTheory(DisplayName = "UPD-H001 | The startup prompt opens the installer only when the user chooses Download update")]
    [InlineData("Download update", true)]
    [InlineData("Not now", false)]
    [Trait("TestId", "UPD-H001")]
    public async Task StartupPromptRequiresAnExplicitChoice(string choice, bool shouldDownload)
    {
        var owner = new Window();
        owner.Show();
        var update = new ReleaseUpdates.Update("v0.3.0-alpha.1", "Prism-windows-x64-Setup.exe");
        string? opened = null;
        var dialog = new ReleaseUpdateWindow(update.Asset, url => opened = url, update);
        try
        {
            var closed = dialog.ShowDialog(owner);
            Assert.Null(opened);
            Assert.Contains(dialog.GetLogicalDescendants().OfType<TextBlock>(), block => block.Text?.Contains("0.3.0-alpha.1") == true);
            var button = dialog.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Content, choice));
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await closed;
            Assert.Equal(shouldDownload ? update.DownloadUrl : null, opened);
        }
        finally
        {
            dialog.Close();
            owner.Close();
        }
    }
}
