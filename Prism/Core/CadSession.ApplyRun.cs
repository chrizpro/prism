using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Prism.Core;

/// <summary>
/// Cancel, Apply and OK (owner decision 2026-11-24): Apply adds a result as its own history step; OK keeps everything applied;
/// Cancel puts the model back as it was when the tool opened. Every other way out keeps what was applied.
/// See docs/interface/README.md, "Cancel, Apply and OK".
/// </summary>
public sealed partial class CadSession
{
    HistoryNode? _applyAnchor;          // where history stood when the first Apply of this run began
    int _appliedCount;
    bool _applyingNow, _reopeningAfterApply;

    /// <summary>How many results the open tool has applied since it was opened (0 when none, or no tool is open).</summary>
    public int AppliedCount => _appliedCount;

    /// <summary>Shown above the panel buttons while anything has been applied: how many, and what Cancel and OK will do with them.</summary>
    [ObservableProperty] public partial string AppliedNote { get; set; } = "";

    void ForgetApplied()
    {
        if (_reopeningAfterApply || _applyingNow) return;
        _applyAnchor = null; _appliedCount = 0;
        AppliedNote = "";
    }

    void UpdateAppliedNote(Commands.Command c)
    {
        AppliedNote = _appliedCount == 0 ? ""
            : $"{_appliedCount} applied. {c.CommitText} keeps {(_appliedCount == 1 ? "it" : "them")} (and adds this one if picked); Cancel removes {(_appliedCount == 1 ? "it" : "all " + _appliedCount)}.";
    }

    /// <summary>
    /// One Apply: <paramref name="commit"/> finishes the current result as its own history step, then <paramref name="reopen"/> opens the tool again.
    /// The first Apply of a run remembers where history stood, so Cancel can go back there.
    /// </summary>
    internal void RunApply(Commands.Command c, Action commit, Action reopen)
    {
        var anchor = _applyAnchor ?? Current; var before = Current; int count = _appliedCount;
        _applyingNow = true;
        try { commit(); } finally { _applyingNow = false; }
        if (ActiveCommand != null) return;                 // not built: the tool stays open with its picks and the run is unchanged
        _applyAnchor = anchor;
        _appliedCount = Current != before ? count + 1 : count;
        _reopeningAfterApply = true;
        try { reopen(); } finally { _reopeningAfterApply = false; }
        if (ActiveCommand == null) { ForgetApplied(); return; }   // it could not reopen: what was applied stays, as after OK
        UpdateAppliedNote(ActiveCommand);
    }

    static bool IsAncestorOrSelf(HistoryNode a, HistoryNode b)
    {
        for (var n = b; n != null; n = n.Parent) if (n == a) return true;
        return false;
    }

    /// <summary>The Cancel button or Esc: close the tool and remove every result it applied in this run. Redo brings them back.</summary>
    void RevertAppliedAfterCancel(HistoryNode? anchor, int count, string title)
    {
        if (anchor == null || count == 0 || anchor == Current || !IsAncestorOrSelf(anchor, Current)) return;
        GoTo(anchor);
        string redo = Hotkeys.Display(Hotkeys.Effective(Hotkeys.Find("edit.redo")!).Gesture);
        Say($"{title} cancelled: removed the {(count == 1 ? "result" : count + " results")} you applied. Redo ({redo}) brings {(count == 1 ? "it" : "them")} back{(count == 1 ? "" : ", one per press")}.");
    }
}
