using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Prism.Core;
using Prism.Views;
using Xunit;

namespace Prism.HeadlessTests.Reporting;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/interface/reporting/README.md">Reporting specification.</seealso>
[Trait("Feature", "Reporting")]
[Trait("Layer", "Headless")]
public class ReportingDialogTests
{
    static Button Find(Window window, string caption) => window.GetLogicalDescendants().OfType<Button>().Single(x => Equals(x.Content, caption));
    static void Click(Window window, string caption) => Find(window, caption).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [AvaloniaFact(DisplayName = "RPT-H001 | Dismissing a crash review never opens a browser or copies a trace")]
    [Trait("TestId", "RPT-H001")]
    public async Task DismissDoesNotShareDiagnostic()
    {
        var owner = new Window(); owner.Show();
        int opens = 0;
        var dialog = new CrashReviewWindow(CrashDiagnostic.Capture(new Exception("private path")), _ => { opens++; return true; });
        try
        {
            var closed = dialog.ShowDialog(owner);
            Assert.Equal(0, opens);
            Assert.False(Find(dialog, "Copy approved trace").IsVisible);
            Click(dialog, "Dismiss");
            await closed;
            Assert.Equal(0, opens);
        }
        finally { dialog.Close(); owner.Close(); }
    }

    [AvaloniaFact(DisplayName = "RPT-H002 | Explicit crash approval passes exactly the previewed diagnostic to GitHub")]
    [Trait("TestId", "RPT-H002")]
    public async Task ApprovalSharesOnlyPreview()
    {
        var owner = new Window(); owner.Show();
        string? opened = null;
        var diagnostic = CrashDiagnostic.Capture(new InvalidOperationException(@"C:\secret\model.step"));
        var dialog = new CrashReviewWindow(diagnostic, url => { opened = url; return true; });
        try
        {
            var closed = dialog.ShowDialog(owner);
            Assert.Null(opened);
            Assert.Equal(diagnostic.Preview, dialog.GetLogicalDescendants().OfType<TextBox>().Single().Text);
            Click(dialog, "Continue to GitHub");
            await closed;
            Assert.Equal(diagnostic.Approve().GitHubUrl(), opened);
            Assert.DoesNotContain("secret", opened!);
        }
        finally { dialog.Close(); owner.Close(); }
    }

    [AvaloniaFact(DisplayName = "RPT-H003 | Browser failure retains crash review and reveals explicit approved-copy fallback")]
    [Trait("TestId", "RPT-H003")]
    public async Task FailedBrowserDoesNotDiscardReview()
    {
        var owner = new Window(); owner.Show();
        var dialog = new CrashReviewWindow(CrashDiagnostic.Capture(new Exception()), _ => false);
        try
        {
            var closed = dialog.ShowDialog(owner);
            Click(dialog, "Continue to GitHub");
            Assert.False(closed.IsCompleted);
            Assert.True(Find(dialog, "Copy approved trace").IsVisible);
            Click(dialog, "Dismiss");
            await closed;
        }
        finally { dialog.Close(); owner.Close(); }
    }

    [AvaloniaFact(DisplayName = "RPT-H004 | Don't show again is retained when feedback browser opening fails")]
    [Trait("TestId", "RPT-H004")]
    public async Task FeedbackFailureStillClosesAndRetainsOptOut()
    {
        var owner = new Window(); owner.Show();
        string? opened = null;
        var dialog = new FeedbackWindow(url => { opened = url; return false; });
        try
        {
            var closed = dialog.ShowDialog(owner);
            Assert.False(dialog.Disabled);
            dialog.GetLogicalDescendants().OfType<CheckBox>().Single().IsChecked = true;
            Click(dialog, "Give feedback");
            await closed;
            Assert.True(dialog.Disabled);
            Assert.Equal(AppInfo.FeedbackUrl(), opened);
        }
        finally { dialog.Close(); owner.Close(); }
    }
}
