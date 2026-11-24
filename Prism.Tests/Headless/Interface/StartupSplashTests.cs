using Avalonia.Headless.XUnit;
using Prism.Core;
using Prism.Views;
using Xunit;

namespace Prism.HeadlessTests.Interface;

/// <summary>The v0.5 start-up card and start-up timing log (owner request 2026-11-23).</summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/interface/startup.md">Start-up guide.</seealso>
[Trait("Feature", "Interface")]
[Trait("Layer", "Headless")]
public class StartupSplashTests
{
    [AvaloniaFact(DisplayName = "STU-H001 | The start-up card is a borderless 560 x 320 window showing the stage, the version and progress clamped to 0..1")]
    [Trait("TestId", "STU-H001")]
    public void CardShowsStageAndProgress()
    {
        var card = new SplashWindow();
        Assert.Equal(560, card.Width);
        Assert.Equal(320, card.Height);
        Assert.Equal(Avalonia.Controls.SystemDecorations.None, card.SystemDecorations);
        Assert.False(card.CanResize);
        card.Show();
        card.SetStage("Building the window…", 0.35);
        Assert.Equal("Building the window…", card.Stage);
        Assert.Equal(0.35, card.Fraction, 6);
        card.SetStage("Opening Prism…", 3);
        Assert.Equal(1, card.Fraction, 6);
        card.SetStage("Starting…", -1);
        Assert.Equal(0, card.Fraction, 6);
        card.Close();
    }

    [AvaloniaFact(DisplayName = "STU-H002 | The start-up log lists each stage with its time and a total")]
    [Trait("TestId", "STU-H002")]
    public void LogListsStages()
    {
        StartupLog.Mark("Test stage A");
        StartupLog.Mark("Test stage B");
        var report = StartupLog.Report();
        Assert.Contains("Test stage A", report);
        Assert.Contains("Test stage B", report);
        Assert.Contains("total", report);
        Assert.Contains("start-up", report);
    }
}
