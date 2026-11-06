using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Prism.Core;

namespace Prism.Views;

public partial class MainWindow
{
    readonly FeedbackSession feedbackSession = new(AppSettings.Current.Feedback ??= new());
    bool reportingBusy, closeApproved, normalClosure = true;
    // Integration launches opt in to automation. Ordinary interactive launches have no flag.
    public bool ReportingEnabled { get; set; } = Environment.GetEnvironmentVariable("PRISM_AUTOMATION") != "1"
        && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PRISM_AUTOMATION_DISCOVERY"));

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (ReportingEnabled && PendingCrashStore.Local.Read() is { } diagnostic)
            _ = ReviewCrash(diagnostic);
    }

    async Task ReviewCrash(CrashDiagnostic diagnostic)
    {
        reportingBusy = true;
        try { await new CrashReviewWindow(diagnostic).ShowDialog(this); }
        catch (Exception) { /* reporting must never stop the app from starting */ }
        finally { PendingCrashStore.Local.Dismiss(); reportingBusy = false; }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e); // Existing cancellation (for example a save prompt) wins.
        if (e.Cancel || !ReportingEnabled) return;
        normalClosure = e.CloseReason != WindowCloseReason.OSShutdown;
        if (!normalClosure) return;
        if (reportingBusy) { e.Cancel = true; return; }
        if (closeApproved || !feedbackSession.BeginPrompt(e.Cancel, true)) return;
        e.Cancel = true;
        // Defer until this closing event has returned before opening a modal or retrying Close.
        Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = AskForFeedback());
        reportingBusy = true;
    }

    async Task AskForFeedback()
    {
        var dialog = new FeedbackWindow();
        try { await dialog.ShowDialog(this); }
        catch (Exception) { }
        finally
        {
            AppSettings.Current.Feedback.Disabled |= dialog.Disabled;
            AppSettings.Save(false);
            reportingBusy = false;
            closeApproved = true;
            Close();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (ReportingEnabled)
        {
            feedbackSession.Complete(normalClosure);
            AppSettings.Save(false);
        }
        base.OnClosed(e);
    }
}
