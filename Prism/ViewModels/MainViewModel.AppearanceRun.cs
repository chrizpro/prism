using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Prism.Core;

namespace Prism.ViewModels;

/// <summary>
/// The Appearance studio's Cancel, Apply and OK (owner decision 2026-11-24), matching tool panels: changes show live; Apply keeps the changes
/// since the last Apply as one undo step; OK does that and closes; Cancel removes every appearance change since the studio opened.
/// Closing the studio any other way keeps the changes. See docs/appearance-studio/README.md, "Cancel, Apply and OK".
/// </summary>
public sealed partial class MainViewModel
{
    CadSession? _lookRunSession;
    HistoryNode? _lookAnchor, _lookMark;       // where history stood when the studio opened, and at the last Apply

    /// <summary>Something changed since the studio opened or since the last Apply, so Apply has work to do.</summary>
    [ObservableProperty] public partial bool AppearanceHasChanges { get; set; }
    /// <summary>The line above the studio's buttons saying what Apply, OK and Cancel will do.</summary>
    [ObservableProperty] public partial string AppearanceNote { get; set; } = "";

    void BeginAppearanceRun()
    {
        if (_lookRunSession == Session && _lookAnchor != null) return;
        _lookRunSession = Session; _lookAnchor = _lookMark = Session.Current;
        RefreshAppearanceRun();
    }

    void ClearAppearanceRun() { _lookRunSession = null; _lookAnchor = _lookMark = null; AppearanceHasChanges = false; AppearanceNote = ""; }

    /// <summary>Keep the run in step with history: undo past where the studio opened, or another document, starts it again from here.</summary>
    public void RefreshAppearanceRun()
    {
        if (_lookAnchor == null) return;
        if (_lookRunSession != Session) { _lookRunSession = Session; _lookAnchor = _lookMark = Session.Current; }
        if (!CadSession.IsAncestorOrSelf(_lookAnchor, Session.Current)) _lookAnchor = _lookMark = Session.Current;
        else if (!CadSession.IsAncestorOrSelf(_lookMark!, Session.Current)) _lookMark = _lookAnchor;
        AppearanceHasChanges = Session.Current != _lookMark;
        bool sinceOpen = Session.Current != _lookAnchor;
        AppearanceNote = !sinceOpen ? "Changes show live. Apply keeps them; OK keeps them and closes; Cancel undoes them."
            : AppearanceHasChanges ? "Changed since the studio opened. Apply keeps the changes as one undo step; Cancel removes them all."
            : "Applied. OK closes the studio; Cancel still removes everything changed since it opened.";
    }

    /// <summary>The objects whose look changed since <paramref name="since"/>, for the history label.</summary>
    string ChangedNames(HistoryNode since)
    {
        var then = since.After.ToDictionary(o => o.Id);
        var names = Session.Objects.Where(o => then.TryGetValue(o.Id, out var b) && !ReferenceEquals(b, o) && (b.Color != o.Color || b.Look != o.Look || !ReferenceEquals(b.FaceStyles, o.FaceStyles)))
            .Select(o => o.Name).Distinct().ToList();
        return names.Count switch { 0 => "", 1 => names[0], _ => $"{names[0]} and {names.Count - 1} more" };
    }

    /// <summary>Apply: the changes since the last Apply become one undo step (when only looks changed); the studio stays open.</summary>
    public bool ApplyAppearance(bool quiet = false)
    {
        if (_lookMark == null) return false;
        RefreshAppearanceRun();
        if (!AppearanceHasChanges) { if (!quiet) Session.Say("Nothing has changed since the last Apply."); return false; }
        string names = ChangedNames(_lookMark);
        if (Session.OnlyLooksChangedSince(_lookMark)) Session.FoldHistorySince(_lookMark, names.Length > 0 ? "Appearance: " + names : "Appearance", "materials");
        _lookMark = Session.Current;
        RefreshAppearanceRun();
        if (!quiet) Session.Say("Appearance applied as one undo step. OK closes the studio; Cancel would still remove everything changed since it opened.");
        return true;
    }

    /// <summary>OK: keep everything and close the studio.</summary>
    public void OkAppearance()
    {
        bool changed = ApplyAppearance(quiet: true);
        CloseMaterials();
        Session.Say(changed ? "Appearance kept." : "Appearance studio closed.");
    }

    /// <summary>Cancel: remove every appearance and material change made since the studio opened, then close it.</summary>
    public void CancelAppearance()
    {
        RefreshAppearanceRun();
        var anchor = _lookAnchor;
        ClearAppearanceRun();                       // closing below must not keep anything
        CloseMaterials();
        if (anchor == null || anchor == Session.Current) { Session.Say("Appearance studio closed; nothing had changed."); return; }
        string redo = Hotkeys.Display(Hotkeys.Effective(Hotkeys.Find("edit.redo")!).Gesture), undo = Hotkeys.Display(Hotkeys.Effective(Hotkeys.Find("edit.undo")!).Gesture);
        if (Session.OnlyLooksChangedSince(anchor))
        {
            Session.GoBackTo(anchor);
            Session.Say($"Appearance changes cancelled. Redo ({redo}) brings them back.");
        }
        else
        {
            int n = Session.RestoreLooksFrom(anchor, "Cancel appearance changes");
            Session.Say(n == 0 ? "Appearance studio closed; no appearance had changed."
                : $"Appearance changes cancelled on {n} object{(n == 1 ? "" : "s")}. Other work done while the studio was open is kept. Undo ({undo}) brings the appearance back.");
        }
    }

    /// <summary>The studio closed some other way (its ×, the ribbon, a panel switch): keep the changes, as OK does.</summary>
    void KeepAppearanceRun()
    {
        if (_lookAnchor == null) return;
        ApplyAppearance(quiet: true);
        ClearAppearanceRun();
    }
}
