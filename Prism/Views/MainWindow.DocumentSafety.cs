using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Prism.Core;
using Prism.ViewModels;

namespace Prism.Views;

public partial class MainWindow
{
    readonly RecoverySchedule recoverySchedule = new();
    readonly DispatcherTimer recoveryTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    public bool CloseReviewPending { get; private set; }
    public Func<DocumentTab, Task<DocumentCloseDecision>>? AskDocumentClose { get; set; }
    CloseStamp[]? approvedDocuments;
    bool documentWindowClosed;
    sealed record CloseStamp(DocumentTab Tab, object Objects, object History, bool Dirty, string? Path, object? Sketch, object? Command);
    static CloseStamp[] Stamp(MainViewModel vm) => vm.Documents.Select(d => new CloseStamp(d, d.Session.Objects, d.Session.Current,
        d.Session.Dirty, d.Session.FilePath, d.Session.Sketch, d.Session.ActiveCommand)).ToArray();

    void InitializeDocumentSafety()
    {
        Closing += ReviewDocumentsOnClose;
        Opened += (_, _) =>
        {
            recoverySchedule.ShouldRun(DateTimeOffset.UtcNow, AppSettings.Current.AutoRecoveryEnabled, AppSettings.Current.AutoRecoveryIntervalMinutes);
            recoveryTimer.Start();
            UpdateRecoveryPreference();
        };
        recoveryTimer.Tick += (_, _) =>
        {
            if (CloseReviewPending || !IsEnabled || DataContext is not MainViewModel vm) return;
            if (recoverySchedule.ShouldRun(DateTimeOffset.UtcNow, AppSettings.Current.AutoRecoveryEnabled, AppSettings.Current.AutoRecoveryIntervalMinutes))
                vm.WriteRecoveryCopies(RecoveryStore.Local);
        };
        AppSettings.Changed += UpdateRecoveryPreference;
        Closed += (_, _) =>
        {
            documentWindowClosed = true;
            recoveryTimer.Stop();
            AppSettings.Changed -= UpdateRecoveryPreference;
        };
    }
    void UpdateRecoveryPreference()
    {
        if (DataContext is not MainViewModel vm) return;
        vm.RecoveryStatus = AppSettings.Current.AutoRecoveryEnabled
            ? $"Recovery copies are on, every {AppSettings.Current.AutoRecoveryIntervalMinutes} minute(s)."
            : "Recovery copies are off. Existing copies are kept.";
    }
    void ReviewDocumentsOnClose(object? sender, WindowClosingEventArgs e)
    {
        if (e.Cancel || e.CloseReason == WindowCloseReason.OSShutdown || DataContext is not MainViewModel vm) return;
        if (CloseReviewPending) { e.Cancel = true; return; }
        if (approvedDocuments != null && approvedDocuments.SequenceEqual(Stamp(vm))) return;
        if (!vm.Documents.Any(MainViewModel.NeedsCloseDecision)) return;
        e.Cancel = true;
        CloseReviewPending = true;
        // The event returns before showing the modal. Reporting's existing close handler sees cancellation first.
        Dispatcher.UIThread.Post(async () =>
        {
            bool approved = false;
            try
            {
                approved = await vm.ConfirmApplicationCloseAsync(AskDocumentClose ??
                    (tab => new SaveChangesWindow(tab.Title, tab.Session.FilePath, tab.Session.Sketch != null || tab.Session.ActiveCommand != null).ShowDialog<DocumentCloseDecision>(this)));
                approvedDocuments = approved ? Stamp(vm) : null;
            }
            catch (Exception ex) { vm.Session.Say("Could not complete the close review: " + ex.Message, true); }
            finally { CloseReviewPending = false; }
            if (approved && !documentWindowClosed) Close();
        });
    }
}
