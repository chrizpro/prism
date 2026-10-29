using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using Prism.Core;

namespace Prism.ViewModels;

/// <summary>History: a branching timeline bar plus a full history tree panel, with hover-to-highlight and selective restore.</summary>
public sealed partial class MainViewModel
{
    /// <summary>What the bottom bar shows: start -> ... -> now -> (steps Redo would bring back, dimmed).</summary>
    public ObservableCollection<HistoryItem> TimelineBar { get; } = [];
    /// <summary>Every state, including side branches, indented by branch.</summary>
    public ObservableCollection<HistoryItem> HistoryRows { get; } = [];
    [ObservableProperty] public partial bool ShowHistoryPanel { get; set; }
    [ObservableProperty] public partial string HistorySummary { get; set; } = "";
    [ObservableProperty] public partial bool HistoryExpanded { get; set; } = AppSettings.Current.HistoryExpanded;
    [ObservableProperty] public partial double HistoryHeight { get; set; } = Math.Clamp(AppSettings.Current.HistoryHeight, 100, 360);
    public string HistoryToggleText => HistoryExpanded ? "Collapse" : "Expand";
    partial void OnHistoryExpandedChanged(bool value)
    {
        AppSettings.Current.HistoryExpanded = value; AppSettings.Save(false);
        OnPropertyChanged(nameof(HistoryToggleText));
    }
    partial void OnHistoryHeightChanged(double value) { AppSettings.Current.HistoryHeight = value; AppSettings.Save(false); }
    public event Action? HistoryRebuilt;

    HistoryItem MakeItem(HistoryNode n, int depth, bool future, bool current, bool onPath)
    {
        var (added, modified, removed) = n.IsRoot ? ([], [], []) : n.Diff();
        var chips = new List<string>();
        if (added.Count > 0) chips.Add($"+{added.Count}");
        if (modified.Count > 0) chips.Add($"~{modified.Count}");
        if (removed.Count > 0) chips.Add($"−{removed.Count}");
        var tip = new StringBuilder();
        tip.Append(n.IsRoot ? "Start of the document" : n.Label);
        if (n.Pin != null) tip.Append($"   ★ {n.Pin}");
        if (!n.IsRoot) tip.Append($"\n{n.Time:HH:mm:ss}");
        foreach (var o in added.Take(5)) tip.Append($"\n+ {o.Name}");
        foreach (var o in modified.Take(5)) tip.Append($"\n~ {o.Name}");
        foreach (var r in removed.Take(5)) tip.Append($"\n− {r}");
        if (n.Recipe != null) tip.Append("\n✎ Right-click or double-click to edit this feature");
        if (n.Parent != null && n.Parent.Children.Count > 1) tip.Append($"\n⑂ one of {n.Parent.Children.Count} branches from here");
        return new HistoryItem
        {
            Node = n, Label = n.Label, Icon = n.Icon, TimeText = n.IsRoot ? "" : n.Time.ToString("HH:mm"), Chips = string.Join(" ", chips), Pin = n.Pin,
            Depth = depth, BranchCount = n.Parent?.Children.Count ?? 1, IsStart = n.IsRoot, ToolTipText = tip.ToString(),
            IsCurrent = current, IsFuture = future, OnPath = onPath,
        };
    }

    public void RebuildHistory()
    {
        var s = Session;
        var (past, future) = s.HistoryPath();
        TimelineBar.Clear();
        foreach (var n in past) TimelineBar.Add(MakeItem(n, 0, false, n == s.Current, true));
        foreach (var n in future) TimelineBar.Add(MakeItem(n, 0, true, false, false));

        HistoryRows.Clear();
        var onPath = past.Concat(future).ToHashSet();
        var futureSet = future.ToHashSet();
        var pending = new Stack<(HistoryNode Node, int Depth)>();
        pending.Push((s.Root, 0));
        while (pending.TryPop(out var row))
        {
            var (n, depth) = row;
            HistoryRows.Add(MakeItem(n, depth, futureSet.Contains(n), n == s.Current, onPath.Contains(n)));
            // side branches first (indented), then the main continuation, so a fork reads where it happened
            if (n.Children.Count > 0) pending.Push((n.Children[0], depth));
            for (int i = n.Children.Count - 1; i >= 1; i--) pending.Push((n.Children[i], depth + 1));
        }
        int branches = s.AllNodes().Count(n => n.Children.Count > 1);
        int steps = s.AllNodes().Count() - 1; HistorySummary = $"{steps} step{(steps == 1 ? "" : "s")}" + (branches > 0 ? $" · {branches} branch point{(branches == 1 ? "" : "s")}" : "");
        RefreshRibbon();
        HistoryRebuilt?.Invoke();
    }

    public void GoToHistory(HistoryItem item) => Session.GoTo(item.Node);

    /// <summary>Light up the objects a step created/changed while the pointer rests on it.</summary>
    public void HoverHistory(HistoryItem? item)
    {
        var ids = Session.HighlightIds;
        ids.Clear();
        if (item != null && !item.IsStart)
        {
            var (added, modified, _) = item.Node.Diff();
            foreach (var o in added.Concat(modified)) ids.Add(o.Id);
        }
        Session.Invalidate();
    }

    public void PinStep(HistoryItem item, string name) { item.Node.Pin = string.IsNullOrWhiteSpace(name) ? null : name.Trim(); Session.RaiseTimeline(); }

    /// <summary>Objects that existed at this step, for "Restore from here" in the context menu.</summary>
    public IEnumerable<(Guid Id, string Name)> RestorableAt(HistoryItem item)
    {
        var nowIds = Session.Objects.ToDictionary(o => o.Id);
        foreach (var o in item.Node.After)
            if (!nowIds.TryGetValue(o.Id, out var cur) || !ReferenceEquals(cur, o)) yield return (o.Id, o.Name);
    }

    public void RestoreObjectFrom(HistoryItem item, Guid id)
    {
        try { Session.RestoreObject(item.Node, id); }
        catch (Exception e) { Session.Say(e.Message, true); }
    }
}
