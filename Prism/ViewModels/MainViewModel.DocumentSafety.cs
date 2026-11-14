using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Prism.Core;

namespace Prism.ViewModels;

public sealed partial class MainViewModel
{
    public Func<string, string, string[], Task<string?>>? PickNativeSaveFile { get; set; }
    [ObservableProperty] public partial string RecoveryStatus { get; set; } = "Recovery copies are off.";
    /// <summary>Changed documents, or a sketch with something drawn. An empty sketch or a tool opened but not used creates nothing, so it never prompts.</summary>
    public static bool NeedsCloseDecision(DocumentTab tab) => tab.Session.Dirty || tab.Session.Sketch is { HasContent: true };

    public async Task<bool> SaveDocumentAsync(DocumentTab tab, bool saveAs = false)
    {
        var session = tab.Session; // Keep the intended document across a file-picker await.
        if (session.Sketch != null || session.ActiveCommand != null)
        {
            session.Say("Finish or cancel the current edit before saving.", true);
            return false;
        }
        try
        {
            string? path = session.FilePath;
            if (path == null || saveAs || NativeDocumentFile.IsMold(path))
            {
                string suggested = path == null ? NextPartName() : System.IO.Path.GetFileName(path);
                string? typed = session.TypedDocumentExtension;
                string[] extensions = typed == null ? [".prism"] : [typed, ".prism"];
                if (session.CanExportMold) extensions = [.. extensions, NativeDocumentFile.Mold];
                if (PickNativeSaveFile != null)
                    path = await PickNativeSaveFile("Save " + tab.Title, System.IO.Path.ChangeExtension(suggested, extensions[0]), extensions);
                else if (PickSaveFile != null)
                    path = await PickSaveFile("Save " + tab.Title, System.IO.Path.ChangeExtension(suggested, extensions[0]), extensions[0][1..]);
                else return false;
                if (string.IsNullOrWhiteSpace(path)) return false;
            }
            session.Save(path);
            AddRecent(path);
            return !NativeDocumentFile.IsMold(path);
        }
        catch (Exception e) { session.Say("Could not save " + tab.Title + ": " + e.Message, true); return false; }
    }

    public async Task<bool> ConfirmApplicationCloseAsync(Func<DocumentTab, Task<DocumentCloseDecision>> ask)
    {
        // Recheck after awaits: scripting or other asynchronous work can create/change documents
        // while a save picker is open. A reviewed version cannot approve a later edit implicitly.
        static object State(DocumentTab t) => (t.Session.Current, t.Session.Objects, t.Session.Dirty,
            t.Session.FilePath, t.Session.Sketch, t.Session.ActiveCommand);
        var reviewed = new Dictionary<DocumentTab, object>();
        while (Documents.FirstOrDefault(t => NeedsCloseDecision(t) &&
            (!reviewed.TryGetValue(t, out var old) || !old.Equals(State(t)))) is { } tab)
        {
            ActivateDocument(tab);
            var decision = await ask(tab);
            if (decision == DocumentCloseDecision.Cancel) return false;
            if (decision == DocumentCloseDecision.Save && !await SaveDocumentAsync(tab)) return false;
            reviewed[tab] = State(tab);
        }
        return true;
    }

    /// <summary>Close selected tabs after reviewing each changed or unfinished document.</summary>
    bool _closingAllTabs;
    public async Task<bool> CloseAllTabsAsync(bool includePinned, Func<DocumentTab, Task<DocumentCloseDecision>> ask, DocumentTab? except = null)
    {
        if (_closingAllTabs) return false;
        _closingAllTabs = true;
        try
        {
        foreach (var tab in Documents.Where(t => !ReferenceEquals(t, except) && (includePinned || !t.IsPinned)).ToArray())
        {
            if (!Documents.Contains(tab) || !includePinned && tab.IsPinned) continue;
            if (NeedsCloseDecision(tab))
            {
                ActivateDocument(tab);
                var state = (tab.Session.Current, tab.Session.Objects, tab.Session.Dirty, tab.Session.FilePath, tab.Session.Sketch, tab.Session.ActiveCommand);
                var decision = await ask(tab);
                if (decision == DocumentCloseDecision.Cancel || !Documents.Contains(tab) || !includePinned && tab.IsPinned) return false;
                if (!state.Equals((tab.Session.Current, tab.Session.Objects, tab.Session.Dirty, tab.Session.FilePath, tab.Session.Sketch, tab.Session.ActiveCommand))) return false;
                if (decision == DocumentCloseDecision.Save && !await SaveDocumentAsync(tab)) return false;
            }
            // The review above is the explicit discard confirmation for this tab.
            tab.ConfirmClose = true;
            CloseDocument(tab);
        }
        return true;
        }
        finally { _closingAllTabs = false; }
    }

    public int WriteRecoveryCopies(RecoveryStore store)
    {
        int saved = 0, failed = 0, deferred = 0;
        foreach (var tab in Documents.ToArray())
        {
            if (!tab.Session.Dirty) continue;
            try
            {
                if (tab.Session.WriteRecoveryCopy(store.PathFor(tab.Session, tab.Title))) saved++;
                else deferred++;
            }
            catch (Exception e) { failed++; tab.Session.Say("Recovery copy failed for " + tab.Title + ": " + e.Message, true); }
        }
        RecoveryStatus = failed > 0 ? $"Recovery: {saved} saved; {failed} failed. Check the document status and recovery folder."
            : deferred > 0 ? $"Recovery: {saved} saved; {deferred} waiting for an edit or exploded view to finish."
            : saved > 0 ? $"Recovery copies saved at {DateTime.Now:t} ({saved} document(s))." : "Recovery is on. No changed documents to copy.";
        return saved;
    }

    public bool RestoreRecoveryCopy(string path)
    {
        try
        {
            // Parse before adding a tab so a damaged recovery file cannot replace current work.
            var recovered = new CadSession(); recovered.Open(path);
            recovered.FilePath = null; recovered.Dirty = true;
            var tab = new DocumentTab(recovered, ++_docCounter) { DisplayName = "Recovered " + System.IO.Path.GetFileNameWithoutExtension(path).Split("--")[0] };
            tab.Refresh(); Documents.Add(tab); ActiveDocument = tab; CloseHome();
            recovered.Say("Recovered a copy. Save it to choose a normal destination.");
            return true;
        }
        catch (Exception e) { RecoveryStatus = "Could not recover that file: " + e.Message; return false; }
    }
}
