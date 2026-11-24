using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Prism.ViewModels;

/// <summary>"Tell me what you want to do": search every ribbon command by name, description or tab.</summary>
public sealed partial class MainViewModel
{
    public ObservableCollection<CommandHit> SearchResults { get; } = [];
    [ObservableProperty] public partial string SearchText { get; set; } = "";
    [ObservableProperty] public partial int SearchIndex { get; set; }
    [ObservableProperty] public partial bool SearchOpen { get; set; }

    partial void OnSearchTextChanged(string value) => UpdateSearchResults();

    public void UpdateSearchResults()
    {
        SearchResults.Clear();
        var q = SearchText.Trim();
        var terms = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var hits = Tabs.Where(t => !t.IsFile && !t.UserHidden)
            .SelectMany(t => t.Groups.SelectMany(g => g.Items.OfType<Prism.Ribbon.RibbonButton>()
                .SelectMany(parent => parent.Menu == null
                    ? new[] { (t, g, b: parent, menu: "") }
                    : parent.Menu.Select(b => (t, g, b, menu: parent.Label.Replace("\n", " "))))))
            .Select(x =>
            {
                x.b.Refresh();
                string label = x.b.Label.Replace("\n", " "), hay = $"{label} {x.b.Description} {x.t.Title} {x.g.Title} {x.menu} {x.b.Shortcut}";
                int score = !terms.All(term => hay.Contains(term, StringComparison.OrdinalIgnoreCase)) ? -1 : label.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? 3 : label.Contains(q, StringComparison.OrdinalIgnoreCase) ? 2 : 1;
                return (x.t, x.g, x.b, x.menu, score);
            })
            .Where(x => x.score >= 0)
            .OrderByDescending(x => x.score).ThenBy(x => x.b.Label)
            .GroupBy(x => x.b.Label.Replace("\n", " ") + "|" + x.t.Title).Select(g => g.First())
            .Take(8);
        if (RepeatHit(q, terms) is { } repeat) SearchResults.Add(repeat);
        foreach (var h in hits.Take(8 - SearchResults.Count)) SearchResults.Add(new CommandHit { Button = h.b, Tab = h.t.Title, Path = string.Join(" › ", new[] { h.t.Title, h.g.Title, h.menu }.Where(s => !string.IsNullOrWhiteSpace(s))) });
        SearchIndex = 0;
        SearchOpen = SearchResults.Count > 0;
    }

    /// <summary>
    /// "Repeat <last tool>" heads the results when the search matches it (by "repeat" or the tool's name) and no tool or sketch is open. It runs
    /// Repeat last tool, so it stays on the current tab.
    /// </summary>
    CommandHit? RepeatHit(string q, string[] terms)
    {
        var s = Session;
        if (q.Length == 0 || s.LastTool is not { } last || s.ActiveCommand != null || s.Sketch != null) return null;
        string label = "Repeat " + last.Title;
        if (!terms.All(term => $"{label} last tool again".Contains(term, StringComparison.OrdinalIgnoreCase))) return null;
        var key = Core.Hotkeys.Effective(Core.Hotkeys.Actions.First(a => a.Id == "edit.repeat")).Gesture;
        var button = new Prism.Ribbon.RibbonButton
        {
            Label = label, Icon = last.Icon, Shortcut = key, Execute = () => s.RepeatLastTool(),
            Description = $"Open {last.Title} again with the options it was last finished with and nothing picked.",
        };
        return new CommandHit { Button = button, Tab = SelectedTab?.Title ?? "", Path = "Edit › Repeat last tool" };
    }

    /// <summary>Every ribbon command once (the same list the search box draws from), for choosing what a custom shortcut runs.</summary>
    public IEnumerable<CommandHit> AllCommandHits() =>
        Tabs.Where(t => !t.IsFile && !t.UserHidden)
            .SelectMany(t => t.Groups.SelectMany(g => g.Items.OfType<Prism.Ribbon.RibbonButton>()
                .SelectMany(parent => parent.Menu == null
                    ? new[] { (t, g, b: parent, menu: "") }
                    : parent.Menu.Select(b => (t, g, b, menu: parent.Label.Replace("\n", " "))))))
            .Select(x => new CommandHit { Button = x.b, Tab = x.t.Title, Path = string.Join(" › ", new[] { x.t.Title, x.g.Title, x.menu }.Where(s => !string.IsNullOrWhiteSpace(s))) })
            .GroupBy(h => h.Label + "|" + h.Tab).Select(g => g.First());

    public static string CommandTarget(CommandHit hit) => hit.Tab + "|" + hit.Label;

    /// <summary>Run the ribbon command a custom shortcut points at ("Tab|Label"). False when it cannot run right now, so another shortcut on the same key may.</summary>
    public bool RunCommandShortcut(string target)
    {
        int cut = target.IndexOf('|'); if (cut < 0) return false;
        string tab = target[..cut], label = target[(cut + 1)..];
        label = label switch { "Divide" => "Points Along Curve", "Duplicate Edge" => "Extract Edges", _ => label };
        var hit = AllCommandHits().FirstOrDefault(h => h.Tab == tab && h.Label == label);
        if (hit == null) { Session.Say($"The shortcut's command '{label}' is no longer in the ribbon.", true); return true; }
        hit.Button.Refresh();
        if (!hit.Button.IsEnabled) return false;
        hit.Button.Invoke();
        return true;
    }

    public void RunSearchHit(CommandHit? hit)
    {
        if (hit == null) return;
        SearchText = "";
        SearchOpen = false;
        hit.Button.Refresh();
        var targetTab = Tabs.FirstOrDefault(t => t.Title == hit.Tab);
        if (targetTab is { IsContextual: true, IsVisible: false }) { Session.Say($"'{hit.Label}' is not available in the current context; open {hit.Tab} first.", true); return; }
        if (!hit.Button.IsEnabled) { Session.Say($"'{hit.Label}' is not available in the current context. Check the selection and active mode.", true); return; }
        if (IsSketching == false && hit.Tab != SelectedTab?.Title && !hit.Tab.StartsWith("Sketch")) { var tab = Tabs.FirstOrDefault(t => t.Title == hit.Tab); if (tab != null && !tab.IsContextual) { EnsureTabVisible(tab); SelectedTab = tab; } }
        hit.Button.Invoke();
    }
}
