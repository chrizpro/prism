using System;
using System.Linq;
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

    public static bool IsAncestorOrSelf(HistoryNode a, HistoryNode b)
    {
        for (var n = b; n != null; n = n.Parent) if (n == a) return true;
        return false;
    }

    // ------------------------------------------------------------ the Appearance studio's Apply, OK and Cancel
    /// <summary>A history step that changed only how objects look: no object added, removed, reshaped, renamed or moved.</summary>
    static bool LooksOnly(HistoryNode n)
    {
        var before = n.Parent?.After; if (before == null || before.Count != n.After.Count) return false;
        var byId = before.ToDictionary(o => o.Id);
        foreach (var o in n.After)
            if (!byId.TryGetValue(o.Id, out var b) || !ReferenceEquals(b, o) && !b.SameGeometryAs(o)) return false;
        return true;
    }

    /// <summary>Whether every step from <paramref name="since"/> to now changed only looks (false when history moved off that branch).</summary>
    public bool OnlyLooksChangedSince(HistoryNode since)
    {
        if (!IsAncestorOrSelf(since, Current)) return false;
        for (var n = Current; n != since; n = n.Parent!) if (!LooksOnly(n)) return false;
        return true;
    }

    /// <summary>Fold the steps from <paramref name="since"/> to now into one step with this label. False when there is nothing to fold.</summary>
    public bool FoldHistorySince(HistoryNode since, string label, string icon)
    {
        if (Current == since || !IsAncestorOrSelf(since, Current)) return false;
        var first = Current; while (first.Parent != since) first = first.Parent!;
        if (first == Current) { Current.Label = label; TimelineChanged?.Invoke(); return true; }
        var node = new HistoryNode { Id = _nextNodeId++, Parent = since, Label = label, Icon = icon, After = Objects, Parameters = DocumentParameters, DrawingState = Drawings.Serialize() };
        since.Children.Remove(first); since.Children.Add(node); since.Preferred = node; Current = node;
        TimelineChanged?.Invoke();
        return true;
    }

    /// <summary>Put colors, finishes, materials and (where the shape is unchanged) face colors back as they were at <paramref name="since"/>, as one step. Geometry stays. Returns how many objects changed.</summary>
    public int RestoreLooksFrom(HistoryNode since, string label)
    {
        var then = since.After.ToDictionary(o => o.Id);
        var list = Objects; int changed = 0;
        for (int i = 0; i < list.Count; i++)
        {
            var o = list[i];
            if (!then.TryGetValue(o.Id, out var b) || ReferenceEquals(b, o)) continue;
            var r = o.WithColor(b.Color).WithLook(b.Look);
            if (b.SameGeometryAs(o)) r = r.WithFaceStyles(b.FaceStyles);
            if (r.Color == o.Color && r.Look == o.Look && ReferenceEquals(r.FaceStyles, o.FaceStyles)) continue;
            list = list.SetItem(i, r); changed++;
        }
        if (changed == 0) return 0;
        Checkpoint(label, "materials");
        Objects = list;
        RaiseScene();
        return changed;
    }

    /// <summary>Go back to a step on the current branch (the studio's Cancel when only looks changed). Redo walks forward again.</summary>
    public void GoBackTo(HistoryNode node) { if (IsAncestorOrSelf(node, Current)) GoTo(node); }

    /// <summary>The Cancel button or Esc: close the tool and remove every result it applied in this run. Redo brings them back.</summary>
    void RevertAppliedAfterCancel(HistoryNode? anchor, int count, string title)
    {
        if (anchor == null || count == 0 || anchor == Current || !IsAncestorOrSelf(anchor, Current)) return;
        GoTo(anchor);
        string redo = Hotkeys.Display(Hotkeys.Effective(Hotkeys.Find("edit.redo")!).Gesture);
        Say($"{title} cancelled: removed the {(count == 1 ? "result" : count + " results")} you applied. Redo ({redo}) brings {(count == 1 ? "it" : "them")} back{(count == 1 ? "" : ", one per press")}.");
    }
}
