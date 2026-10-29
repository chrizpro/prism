using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Commands;
using Prism.Core;
using Prism.Ribbon;

namespace Prism.ViewModels;

public sealed partial class MainViewModel
{
    readonly Dictionary<string, RibbonButton> _catalog = [];
    readonly Dictionary<string, string> _catalogTab = [];
    readonly Dictionary<string, List<RibbonGroup>> _defaultGroups = [];

    RibbonButton ToolMenu(string label, string icon, string description, params RibbonButton[] variants) => new()
    {
        Label = label, Icon = icon, Description = description, Menu = [.. variants],
        CanExecute = () => { foreach (var v in variants) { v.Refresh(); if (v.IsEnabled) return true; } return false; },
    };

    void CompactModelingRibbon()
    {
        foreach (var tab in Tabs.Where(t => t.Title is "Home" or "Solid" or "Surface" or "NURBS" or "Freeform"))
        {
            var references = tab.Groups.Where(g => g.Title is "Sketch" or "Planes" or "Curves").ToList();
            if (references.Count > 0)
            {
                var tools = references.SelectMany(g => g.Items).OfType<RibbonButton>().ToArray();
                int at = tab.Groups.IndexOf(references[0]);
                foreach (var group in references) tab.Groups.Remove(group);
                tab.Groups.Insert(at, new RibbonGroup { Title = "References", Items =
                    [tools[0], ToolMenu("Curves & planes", "origin-plane", "Sketch editing, spatial curves and construction references.", tools.Skip(1).ToArray())] });
            }
            foreach (var group in tab.Groups)
            {
                if (group.Title == "Combine")
                {
                    var variants = group.Items.OfType<RibbonButton>().ToArray();
                    if (variants.Length > 1) { group.Items.Clear(); group.Items.Add(ToolMenu("Combine", "union", "Union, subtract, or intersect bodies.", variants)); }
                    continue;
                }
                if (tab.Title is "Solid" or "Home" && group.Title == "Pattern")
                {
                    var variants = group.Items.OfType<RibbonButton>().ToArray();
                    if (variants.Length == 5) { group.Items.Clear(); group.Items.AddRange(variants.Take(2)); group.Items.Add(ToolMenu("More patterns", "pattern-curve", "Pattern along a curve, fill a region, or use a table.", variants.Skip(2).ToArray())); }
                    continue;
                }
                int keep = group.Title is "Sketch" or "Modify" or "Inspect" ? 1 : 0;
                if (group.Title is not ("Curves" or "Planes" or "Finish" or "Fill" or "Loops" or "Lock and align" or "Components" or "Transform" or "Inspect" or "Sketch" or "Modify" or "Surfaces" or "Detail" or "Analyze" or "Shape" or "Create")) continue;
                // Existing dropdowns remain intact; menus stay one level deep for discovery and search.
                var buttons = group.Items.OfType<RibbonButton>().ToList();
                if (buttons.Count - keep < 2 || buttons.Any(b => b.HasMenu)) continue;
                if (group.Title == "Surfaces") keep = 1; // keep NURBS Patch directly available
                var nested = buttons.Skip(keep).ToArray();
                group.Items.Clear(); group.Items.AddRange(buttons.Take(keep));
                group.Items.Add(ToolMenu(group.Title == "Surfaces" ? "Build surface" : group.Title == "Sketch" ? "More sketches" : group.Title,
                    nested[0].Icon, "Open related tools. Individual commands remain available in command search.", nested));
            }
        }
    }

    /// <summary>A button that shows (and runs) the variant you used last and keeps the others in a list: one slot instead of five.</summary>
    RibbonButton Split(string id, string desc, params RibbonButton[] variants)
    {
        var picks = AppSettings.Current.LastPicks;
        var cur = picks.TryGetValue(id, out var last) ? variants.FirstOrDefault(v => v.Label.Replace("\n", " ") == last) ?? variants[0] : variants[0];
        return new RibbonButton
        {
            Id = id, Label = cur.Label.Replace("\n", " "), Icon = cur.Icon, Description = desc, Size = RibbonSize.Large, Menu = [.. variants], IsSplit = true, Current = cur,
            CanExecute = () => !IsSketching,
            Picked = v => { picks[id] = v.Label.Replace("\n", " "); AppSettings.Save(false); },
        };
    }

    RibbonButton PrimitiveSplit() => Split("split:primitive", "Create a box, cylinder, sphere, cone or torus. Click the icon to make the one shown; click the name (or right-click) to pick another.",
        Cmd("Box", "box", () => new BoxCommand(), "Create a rectangular solid."),
        Cmd("Cylinder", "cylinder", () => new CylinderCommand(), "Create a cylinder."),
        Cmd("Sphere", "sphere", () => new SphereCommand(), "Create a sphere."),
        Cmd("Cone", "cone", () => new ConeCommand(), "Create a cone or truncated cone."),
        Cmd("Torus", "torus", () => new TorusCommand(), "Create a torus."));

    /// <summary>Called once the shipped ribbon exists: names every command, remembers the shipped layout (for Reset) and applies the user's changes.</summary>
    void FinishRibbon()
    {
        foreach (var t in Tabs)
            foreach (var b in t.Buttons)
                if (b.Id.Length == 0) b.Id = b.Icon + "|" + b.Label.Replace("\n", " ");
        foreach (var t in Tabs.Where(t => !t.IsFile))
            foreach (var b in t.Buttons)
                if (_catalog.TryAdd(b.Id, b)) _catalogTab[b.Id] = t.Title;
        foreach (var t in Tabs.Where(t => !t.IsFile && !t.IsContextual)) _defaultGroups[t.Title] = [.. t.Groups];
        ApplyRibbonLayout(initial: true);
        RebuildQuickBar();
    }

    List<RibbonGroup> BuildGroups(IEnumerable<GroupCfg> cfgs) =>
        [.. cfgs.Select(c => new RibbonGroup { Title = c.Title, Items = [.. c.Items.Where(_catalog.ContainsKey).Select(id => (RibbonItem)_catalog[id])] }).Where(g => g.Items.Count > 0)];

    public void ApplyRibbonLayout(bool initial = false)
    {
        var lay = AppSettings.Current.Ribbon;
        var was = SelectedTab?.Title;
        foreach (var c in Tabs.Where(t => t.IsCustom).ToList()) Tabs.Remove(c);
        foreach (var t in Tabs.Where(t => !t.IsFile && !t.IsContextual))
        {
            t.UserHidden = lay.Hidden.Contains(t.Title);
            var groups = lay.Edited.TryGetValue(t.Title, out var e) ? BuildGroups(e) : _defaultGroups[t.Title];
            t.Groups.Clear(); t.Groups.AddRange(groups);
        }
        int at = Tabs.IndexOf(Tabs.First(t => t.Title == "View"));
        foreach (var c in lay.Custom)
            Tabs.Insert(at++, new RibbonTab { Title = c.Title, IsCustom = true, Groups = BuildGroups(c.Groups) });
        if (initial) return;
        ApplyWorkspace();
        RefreshRibbon();
        var back = Tabs.FirstOrDefault(t => t.Title == was && t.IsVisible) ?? Tabs.FirstOrDefault(t => t.Title == "Home");
        SelectedTab = null; SelectedTab = back;                                // groups are plain lists: re-select the tab so the ribbon redraws
    }

    // ------------------------------------------------------------ what the Settings window edits
    public IReadOnlyList<string> EditableTabs => [.. Tabs.Where(t => !t.IsFile && !t.IsContextual).Select(t => t.Title)];
    public bool IsCustomTab(string title) => Tabs.Any(t => t.IsCustom && t.Title == title);
    public bool IsTabHidden(string title) => AppSettings.Current.Ribbon.Hidden.Contains(title);
    public bool IsTabChanged(string title) => AppSettings.Current.Ribbon.Edited.ContainsKey(title) || IsTabHidden(title);
    public IReadOnlyList<(string Id, string Label, string Tab)> CatalogEntries =>
        [.. _catalog.Select(kv => (kv.Key, Label: kv.Value.Label.Replace("\n", " "), Tab: _catalogTab[kv.Key])).OrderBy(x => x.Label, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Tab)];
    public string LabelOf(string id) => _catalog.TryGetValue(id, out var b) ? b.Label.Replace("\n", " ") : id.Split('|').Last() + " (not available)";
    public string IconOf(string id) => _catalog.TryGetValue(id, out var b) ? b.Icon : "";

    static List<GroupCfg> Copy(IEnumerable<GroupCfg> g) => [.. g.Select(x => new GroupCfg { Title = x.Title, Items = [.. x.Items] })];

    public List<GroupCfg> GetTabCfg(string title)
    {
        var lay = AppSettings.Current.Ribbon;
        if (lay.Custom.FirstOrDefault(c => c.Title == title) is { } cu) return Copy(cu.Groups);
        if (lay.Edited.TryGetValue(title, out var e)) return Copy(e);
        return [.. _defaultGroups[title].Select(g => new GroupCfg { Title = g.Title, Items = [.. g.Items.OfType<RibbonButton>().Select(b => b.Id)] })];
    }

    void CommitRibbon() { AppSettings.Save(false); ApplyRibbonLayout(); }

    public void SetTabCfg(string title, List<GroupCfg> groups)
    {
        var lay = AppSettings.Current.Ribbon;
        if (lay.Custom.FirstOrDefault(c => c.Title == title) is { } cu) cu.Groups = Copy(groups);
        else lay.Edited[title] = Copy(groups);
        CommitRibbon();
    }

    public string AddCustomTab(string name)
    {
        var lay = AppSettings.Current.Ribbon;
        name = name.Trim(); if (name.Length == 0) name = "My tab";
        string unique = name; int n = 2;
        while (Tabs.Any(t => t.Title.Equals(unique, StringComparison.OrdinalIgnoreCase))) unique = $"{name} {n++}";
        lay.Custom.Add(new CustomTabCfg { Title = unique, Groups = [new GroupCfg { Title = "Group 1" }] });
        CommitRibbon();
        return unique;
    }

    public void DeleteCustomTab(string title)
    {
        AppSettings.Current.Ribbon.Custom.RemoveAll(c => c.Title == title);
        CommitRibbon();
    }

    public void SetTabHidden(string title, bool hidden)
    {
        var h = AppSettings.Current.Ribbon.Hidden;
        h.Remove(title); if (hidden) h.Add(title);
        CommitRibbon();
    }

    /// <summary>Put one shipped tab back the way it came (its commands, and showing it again).</summary>
    public void ResetTab(string title)
    {
        var lay = AppSettings.Current.Ribbon;
        lay.Edited.Remove(title); lay.Hidden.Remove(title);
        CommitRibbon();
    }

    /// <summary>Forget every ribbon change: shipped tabs return, custom tabs go.</summary>
    public void ResetRibbon()
    {
        AppSettings.Current.Ribbon = new RibbonLayout();
        CommitRibbon();
    }
}
