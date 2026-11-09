using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia;
using Avalonia.Input;
using Avalonia.Threading;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Prism.ViewModels;
using Prism.Views;
using Xunit;

namespace Prism.HeadlessTests.DocumentSafety;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/interface/document-safety/README.md">Document safety.</seealso>
[Trait("Feature", "DocumentSafety")]
[Trait("Layer", "Headless")]
public class DocumentSafetyTests : IDisposable
{
    readonly string folder = Path.Combine(Path.GetTempPath(), "prism-safety-" + Guid.NewGuid());
    readonly string? priorSettings = Environment.GetEnvironmentVariable("PRISM_SETTINGS_FILE");
    readonly List<string> recent = AppSettings.Current.RecentFiles.ToList();
    readonly bool enabled = AppSettings.Current.AutoRecoveryEnabled;
    readonly int interval = AppSettings.Current.AutoRecoveryIntervalMinutes;
    public DocumentSafetyTests()
    {
        Directory.CreateDirectory(folder);
        Environment.SetEnvironmentVariable("PRISM_SETTINGS_FILE", Path.Combine(folder, "settings.json"));
    }
    static void AddBody(CadSession session) => session.AddObject(Occ.Box(10, 20, 30), "Test part", ObjKind.Solid);
    // The window-close handlers are async and continue on later dispatcher turns, so one Background pass is sometimes not enough (SAV-H006 failed about one full run in five). Pass several times.
    static async Task Drain()
    {
        for (int i = 0; i < 6; i++) { await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background).GetTask(); await Task.Yield(); }
    }
    static DragEventArgs Drop(MainWindow window, string[] paths, bool over = false)
    {
        var data = new DataObject(); data.Set(DataFormats.FileNames, paths);
        var args = new DragEventArgs(over ? DragDrop.DragOverEvent : DragDrop.DropEvent, data, window, new Point(100, 100), KeyModifiers.None);
        window.RaiseEvent(args); return args;
    }

    [AvaloniaFact(DisplayName = "SAV-H010 | Routed file drops open native and mesh tabs without replacing unsaved work")]
    [Trait("TestId", "SAV-H010")]
    public async Task DroppedNativeAndMeshFilesPreserveExistingWork()
    {
        var source = new CadSession(); AddBody(source);
        string native = Path.Combine(folder, "part.prism"); source.Save(native);
        string mesh = Path.Combine(folder, "triangle.stl");
        File.WriteAllText(mesh, "solid test\nfacet normal 0 0 1\nouter loop\nvertex 0 0 0\nvertex 10 0 0\nvertex 0 10 0\nendloop\nendfacet\nendsolid test");
        var vm = new MainViewModel(); AddBody(vm.Session); var original = vm.ActiveDocument!; vm.HomeOpen = true;
        var window = new MainWindow { DataContext = vm };
        window.GetType().GetProperty("ReportingEnabled")?.SetValue(window, false); window.Show();
        try
        {
            Assert.Equal(DragDropEffects.Copy, Drop(window, [native], over: true).DragEffects);
            Assert.Equal(DragDropEffects.Copy, Drop(window, [native, mesh]).DragEffects);
            Assert.Equal(3, vm.Documents.Count); Assert.Contains(original, vm.Documents); Assert.True(original.Session.Dirty);
            Assert.Equal(native, vm.Documents[1].Session.FilePath);
            Assert.Equal(ObjKind.Mesh, Assert.Single(vm.Documents[2].Session.Objects).Kind);
            Assert.True(vm.Documents[2].Session.Dirty); Assert.Null(vm.Documents[2].Session.FilePath); Assert.False(vm.HomeOpen);
        }
        finally { window.AskDocumentClose = _ => Task.FromResult(DocumentCloseDecision.Discard); window.Close(); await Drain(); }
    }

    [AvaloniaFact(DisplayName = "SAV-H011 | Invalid, unsupported and pending-edit drops leave existing documents intact")]
    [Trait("TestId", "SAV-H011")]
    public async Task BadDropsAndPendingEditsCannotReplaceWork()
    {
        string broken = Path.Combine(folder, "broken.prism"); File.WriteAllText(broken, "invalid json");
        string text = Path.Combine(folder, "notes.txt"); File.WriteAllText(text, "a note");
        var vm = new MainViewModel(); AddBody(vm.Session); var original = vm.Session;
        var window = new MainWindow { DataContext = vm };
        window.GetType().GetProperty("ReportingEnabled")?.SetValue(window, false); window.Show();
        try
        {
            Assert.Equal(DragDropEffects.None, Drop(window, [text, folder], over: true).DragEffects);
            Drop(window, [broken, text, folder]); Assert.Single(vm.Documents); Assert.Same(original, vm.Session);
            Assert.True(original.Dirty); Assert.True(original.StatusIsError);
            original.StartCommand(() => new BoxCommand());
            Assert.Equal(DragDropEffects.None, Drop(window, [broken], over: true).DragEffects);
            Drop(window, [broken]); Assert.Single(vm.Documents); Assert.Same(original, vm.Session);
            original.CancelCommand();
        }
        finally { window.AskDocumentClose = _ => Task.FromResult(DocumentCloseDecision.Discard); window.Close(); await Drain(); }
    }

    [AvaloniaFact(DisplayName = "SAV-H001 | Exit reviews inactive documents; a later Cancel preserves the open session")]
    [Trait("TestId", "SAV-H001")]
    public async Task CloseChecksAllDocumentsBeforeExiting()
    {
        var vm = new MainViewModel(); var first = vm.ActiveDocument!; AddBody(first.Session);
        var second = vm.NewDocumentTab(); AddBody(second.Session);
        var pristine = vm.NewDocumentTab();
        vm.PickSaveFile = (_, _, _) => Task.FromResult<string?>(Path.Combine(folder, "first.prism"));
        var asked = new List<DocumentTab>();
        bool result = await vm.ConfirmApplicationCloseAsync(tab =>
        {
            asked.Add(tab); return Task.FromResult(tab == first ? DocumentCloseDecision.Save : DocumentCloseDecision.Cancel);
        });
        Assert.False(result); Assert.Equal(new[] { first, second }, asked);
        Assert.Equal(3, vm.Documents.Count); Assert.False(first.Session.Dirty); Assert.True(second.Session.Dirty);
        Assert.True(File.Exists(first.Session.FilePath));
        Assert.False(MainViewModel.NeedsCloseDecision(pristine));
    }

    [AvaloniaFact(DisplayName = "SAV-H012 | Nothing created means no close prompt: undo to the start, empty sketch or unused tool")]
    [Trait("TestId", "SAV-H012")]
    public void EmptyDocumentsDoNotPromptOnExit()
    {
        var vm = new MainViewModel(); var tab = vm.ActiveDocument!;
        Assert.False(MainViewModel.NeedsCloseDecision(tab));
        AddBody(tab.Session);
        Assert.True(MainViewModel.NeedsCloseDecision(tab));
        tab.Session.Undo();
        Assert.Empty(tab.Session.Objects); Assert.False(tab.Session.Dirty);
        Assert.False(MainViewModel.NeedsCloseDecision(tab));
        tab.Session.Redo();
        Assert.True(MainViewModel.NeedsCloseDecision(tab));
        tab.Session.Undo();
        tab.Session.StartCommand(() => new BoxCommand());
        try { Assert.False(MainViewModel.NeedsCloseDecision(tab)); }
        finally { tab.Session.CancelCommand(); }
        tab.Session.EnterSketch(Plane3.XY);
        try { Assert.False(MainViewModel.NeedsCloseDecision(tab)); }
        finally { tab.Session.ExitSketch(false); }
    }

    [AvaloniaFact(DisplayName = "SAV-H013 | A change outside the timeline keeps a document unsaved even after undo returns its geometry to the start")]
    [Trait("TestId", "SAV-H013")]
    public void OffTimelineChangesSurviveUndo()
    {
        var vm = new MainViewModel(); var tab = vm.ActiveDocument!;
        AddBody(tab.Session);                 // already dirty
        tab.Session.EditedLights();           // a lights change is not a history step
        tab.Session.Undo();
        Assert.Empty(tab.Session.Objects);
        Assert.True(tab.Session.Dirty);
        Assert.True(MainViewModel.NeedsCloseDecision(tab));
    }

    [AvaloniaFact(DisplayName = "SAV-H002 | Canceled Save As and a failed destination keep unsaved work open")]
    [Trait("TestId", "SAV-H002")]
    public async Task SaveCancellationAndFailureCancelClose()
    {
        var vm = new MainViewModel(); AddBody(vm.Session);
        vm.PickSaveFile = (_, _, _) => Task.FromResult<string?>(null);
        Assert.False(await vm.ConfirmApplicationCloseAsync(_ => Task.FromResult(DocumentCloseDecision.Save)));
        Assert.Null(vm.Session.FilePath); Assert.True(vm.Session.Dirty);
        vm.PickSaveFile = (_, _, _) => Task.FromResult<string?>(folder); // A directory cannot become the document file.
        Assert.False(await vm.ConfirmApplicationCloseAsync(_ => Task.FromResult(DocumentCloseDecision.Save)));
        Assert.True(vm.Session.Dirty); Assert.Single(vm.Session.Objects); Assert.Null(vm.Session.FilePath);
        Assert.True(vm.Session.StatusIsError);
        Assert.Empty(Directory.GetFiles(folder, "*.tmp"));
    }

    [AvaloniaFact(DisplayName = "SAV-H003 | Save retains its intended document across file-picker awaits")]
    [Trait("TestId", "SAV-H003")]
    public async Task SaveCapturesTheDocumentBeforeAwaitingPicker()
    {
        var vm = new MainViewModel(); var first = vm.ActiveDocument!; AddBody(first.Session);
        var picker = new TaskCompletionSource<string?>(); vm.PickSaveFile = (_, _, _) => picker.Task;
        var saving = vm.SaveDocumentAsync(first);
        var second = vm.NewDocumentTab(); AddBody(second.Session);
        string path = Path.Combine(folder, "first.prism"); picker.SetResult(path);
        Assert.True(await saving);
        Assert.Equal(path, first.Session.FilePath); Assert.False(first.Session.Dirty);
        Assert.Null(second.Session.FilePath); Assert.True(second.Session.Dirty);
        var reopened = new CadSession(); reopened.Open(path);
        Assert.Equal(6000, reopened.Objects.Single().Shape.Props().Volume, 5);
    }

    [AvaloniaFact(DisplayName = "SAV-H009 | A document created while Save As is open receives its own close decision")]
    [Trait("TestId", "SAV-H009")]
    public async Task CloseRechecksDocumentsAddedDuringSaveAs()
    {
        var vm = new MainViewModel(); var first = vm.ActiveDocument!; AddBody(vm.Session);
        var picker = new TaskCompletionSource<string?>(); vm.PickSaveFile = (_, _, _) => picker.Task;
        var asked = new List<DocumentTab>();
        var closing = vm.ConfirmApplicationCloseAsync(tab =>
        {
            asked.Add(tab); return Task.FromResult(tab == first ? DocumentCloseDecision.Save : DocumentCloseDecision.Cancel);
        });
        var added = vm.NewDocumentTab(); AddBody(added.Session);
        picker.SetResult(Path.Combine(folder, "first.prism"));
        Assert.False(await closing);
        Assert.Equal(new[] { first, added }, asked);
        Assert.False(first.Session.Dirty); Assert.True(added.Session.Dirty); Assert.Equal(2, vm.Documents.Count);
    }

    [AvaloniaFact(DisplayName = "SAV-H004 | Recovery preserves original bytes, dirty flag, path and history; restores an untitled copy")]
    [Trait("TestId", "SAV-H004")]
    public void RecoveryDoesNotActLikeSavingTheOriginal()
    {
        var vm = new MainViewModel(); AddBody(vm.Session);
        string original = Path.Combine(folder, "original.prism"); vm.Session.Save(original);
        byte[] before = File.ReadAllBytes(original);
        vm.Session.Update(vm.Session.Objects.Single().Id, o => o.WithColor(0xFF123456), label: "Color");
        var history = vm.Session.Current; string? pin = history.Pin; var objects = vm.Session.Objects;
        var store = new RecoveryStore(Path.Combine(folder, "recovery"));
        Assert.Equal(1, vm.WriteRecoveryCopies(store));
        Assert.Equal(before, File.ReadAllBytes(original)); Assert.Equal(original, vm.Session.FilePath);
        Assert.True(vm.Session.Dirty); Assert.Same(history, vm.Session.Current); Assert.Equal(pin, history.Pin);
        Assert.Same(objects, vm.Session.Objects);
        var entry = Assert.Single(store.List());
        Assert.True(vm.RestoreRecoveryCopy(entry.Path));
        Assert.Equal(2, vm.Documents.Count); Assert.Null(vm.Session.FilePath); Assert.True(vm.Session.Dirty);
        Assert.Equal(0xFF123456u, vm.Session.Objects.Single().Color);
        Assert.Equal(6000, vm.Session.Objects.Single().Shape.Props().Volume, 5);
        Assert.True(vm.Session.CanUndo);
    }

    [AvaloniaFact(DisplayName = "SAV-H005 | Untitled recovery works; incomplete commands defer and keep the prior copy")]
    [Trait("TestId", "SAV-H005")]
    public async Task PendingEditsDoNotReplaceTheRecoverySnapshot()
    {
        var vm = new MainViewModel(); AddBody(vm.Session);
        var store = new RecoveryStore(Path.Combine(folder, "recovery"));
        Assert.Equal(1, vm.WriteRecoveryCopies(store));
        var path = Assert.Single(store.List()).Path; byte[] before = File.ReadAllBytes(path);
        vm.Session.StartCommand(() => new BoxCommand());
        try
        {
            Assert.Equal(0, vm.WriteRecoveryCopies(store));
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.True(vm.Session.Dirty); Assert.Null(vm.Session.FilePath);
            Assert.True(MainViewModel.NeedsCloseDecision(vm.ActiveDocument!));
            Assert.False(await vm.SaveDocumentAsync(vm.ActiveDocument!));
            Assert.Contains("Finish or cancel", vm.Session.Status);
        }
        finally { vm.Session.CancelCommand(); }
    }

    [AvaloniaFact(DisplayName = "SAV-H006 | Repeated window closes show one review; Cancel stays open and Don't save exits")]
    [Trait("TestId", "SAV-H006")]
    public async Task ActualWindowCloseHonorsCancelAndDiscard()
    {
        var vm = new MainViewModel(); AddBody(vm.Session);
        var window = new MainWindow { DataContext = vm };
        // Reporting is a separate feature; don't show its optional feedback prompt in this scenario.
        window.GetType().GetProperty("ReportingEnabled")?.SetValue(window, false);
        int asks = 0; var answer = new TaskCompletionSource<DocumentCloseDecision>();
        window.AskDocumentClose = _ => { asks++; return answer.Task; };
        window.Show();
        try
        {
            window.Close(); await Drain();
            Assert.True(window.IsVisible); Assert.True(window.CloseReviewPending); Assert.Equal(1, asks);
            window.Close(); await Drain(); Assert.Equal(1, asks);
            answer.SetResult(DocumentCloseDecision.Cancel); await Drain();
            Assert.True(window.IsVisible); Assert.False(window.CloseReviewPending);
            window.AskDocumentClose = _ => Task.FromResult(DocumentCloseDecision.Discard);
            window.Close(); await Drain(); Assert.False(window.IsVisible);
        }
        finally { window.AskDocumentClose = _ => Task.FromResult(DocumentCloseDecision.Discard); window.Close(); await Drain(); }
    }

    [AvaloniaFact(DisplayName = "SAV-H007 | Save prompt buttons return Save, Don't save and Cancel including title-bar dismissal")]
    [Trait("TestId", "SAV-H007")]
    public async Task DialogChoicesAreExplicit()
    {
        var owner = new Window(); owner.Show();
        try
        {
            foreach (var item in new[] { ("Save", DocumentCloseDecision.Save), ("Don't save", DocumentCloseDecision.Discard), ("Cancel", DocumentCloseDecision.Cancel) })
            {
                var dialog = new SaveChangesWindow("Test part"); var task = dialog.ShowDialog<DocumentCloseDecision>(owner);
                var button = dialog.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Content, item.Item1));
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(item.Item2, await task);
            }
            var dismissed = new SaveChangesWindow("Test part"); var result = dismissed.ShowDialog<DocumentCloseDecision>(owner);
            dismissed.Close(); Assert.Equal(DocumentCloseDecision.Cancel, await result);
        }
        finally { owner.Close(); }
    }

    [AvaloniaFact(DisplayName = "SAV-H008 | Settings exposes disabled-by-default recovery with an editable saved interval")]
    [Trait("TestId", "SAV-H008")]
    public void WorkspaceSettingsRetainRecoveryPreferences()
    {
        AppSettings.Current.AutoRecoveryEnabled = false; AppSettings.Current.AutoRecoveryIntervalMinutes = 5;
        var settings = new SettingsWindow(new MainViewModel());
        try
        {
            var check = settings.FindControl<CheckBox>("RecoveryEnabled")!;
            var minutes = settings.FindControl<NumericUpDown>("RecoveryInterval")!;
            Assert.False(check.IsChecked); Assert.False(minutes.IsEnabled); Assert.Equal(5m, minutes.Value);
            check.IsChecked = true; check.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(AppSettings.Current.AutoRecoveryEnabled); Assert.True(minutes.IsEnabled);
            minutes.Value = 9; Assert.Equal(9, AppSettings.Current.AutoRecoveryIntervalMinutes);
        }
        finally { settings.Close(); }
    }

    public void Dispose()
    {
        AppSettings.Current.RecentFiles = recent;
        AppSettings.Current.AutoRecoveryEnabled = enabled; AppSettings.Current.AutoRecoveryIntervalMinutes = interval;
        Environment.SetEnvironmentVariable("PRISM_SETTINGS_FILE", priorSettings);
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
    }
}
