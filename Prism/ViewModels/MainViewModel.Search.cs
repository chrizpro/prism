using System;
using System.Collections.ObjectModel;
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

    partial void OnSearchTextChanged(string value)
    {
        SearchResults.Clear();
        var q = value.Trim();
        if (q.Length == 0) { SearchOpen = false; return; }
        var terms = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var hits = Tabs.Where(t => !t.IsFile && !t.UserHidden).SelectMany(t => t.Buttons.Where(b => !b.HasMenu).Select(b => (t, b)))
            .Select(x =>
            {
                x.b.Refresh();
                string label = x.b.Label.Replace("\n", " "), hay = $"{label} {x.b.Description} {x.t.Title}";
                if (!terms.All(term => hay.Contains(term, StringComparison.OrdinalIgnoreCase))) return (x.t, x.b, score: -1);
                int score = label.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? 3 : label.Contains(q, StringComparison.OrdinalIgnoreCase) ? 2 : 1;
                return (x.t, x.b, score);
            })
            .Where(x => x.score >= 0)
            .OrderByDescending(x => x.score).ThenBy(x => x.b.Label)
            .GroupBy(x => x.b.Label.Replace("\n", " ") + "|" + x.t.Title).Select(g => g.First())
            .Take(8);
        foreach (var h in hits) SearchResults.Add(new CommandHit { Button = h.b, Tab = h.t.Title });
        SearchIndex = 0;
        SearchOpen = SearchResults.Count > 0;
    }

    public void RunSearchHit(CommandHit? hit)
    {
        if (hit == null) return;
        SearchOpen = false;
        SearchText = "";
        hit.Button.Refresh();
        if (!hit.Button.IsEnabled) { Session.Say($"'{hit.Label}' is not available right now (select something or leave sketch mode).", true); return; }
        if (IsSketching == false && hit.Tab != SelectedTab?.Title && !hit.Tab.StartsWith("Sketch")) { var tab = Tabs.FirstOrDefault(t => t.Title == hit.Tab); if (tab != null && !tab.IsContextual) { EnsureTabVisible(tab); SelectedTab = tab; } }
        hit.Button.Invoke();
    }
}
