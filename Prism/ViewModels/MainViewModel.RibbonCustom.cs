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

    RibbonButton ToolMenu(string label, string icon, string description, params RibbonButton[] variants) =>
        Split("menu:" + label + ":" + string.Join(",", variants.Select(v => v.Icon + "|" + v.Label.Replace("\n", " "))), description, variants);

    void OrganizeRibbons(Func<RibbonGroup> transform, Func<RibbonGroup> inspect,
        Func<RibbonGroup> appearance, Func<RibbonGroup> select)
    {
        var modeling = new[] { "Home", "Solid", "Surface", "NURBS", "Freeform", "Direct Modeling", "Sheet Metal", "Mesh", "Reverse Engineer", "ECAD" };
        foreach (var tab in Tabs)
        {
            // Look At lives in the viewport toolbar and the View tab.
            if (tab.Title != "View")
                foreach (var group in tab.Groups)
                    group.Items.RemoveAll(i => i is RibbonButton { Icon: "look-at" });
            // Shared sections come from fresh factories, never from a different tab's mutable buttons.
            for (int i = 0; i < tab.Groups.Count; i++)
            {
                var group = tab.Groups[i];
                if (group.Title == "Transform") tab.Groups[i] = transform();
                else if (group.Title == "Appearance") tab.Groups[i] = appearance();
                else if (group.Title == "Select" && !tab.IsContextual) tab.Groups[i] = select();
                else if (group.Title == "Inspect")
                    tab.Groups[i] = tab.Title == "View" ? G("Section", group.Items.ToArray()) : inspect();
                else if (group.Title == "Components" && tab.Title == "Freeform")
                    tab.Groups[i] = G("Topology", group.Items.ToArray());
            }
            if (modeling.Contains(tab.Title))
            {
                if (tab.Groups.All(g => g.Title != "Inspect")) tab.Groups.Add(inspect());
                if (tab.Groups.All(g => g.Title != "Select")) tab.Groups.Add(select());
                // Reading a surface's smoothness matters where surfaces are shaped by eye; the Analyze tab has these for everything else.
                var inspectGroup = tab.Groups.Single(g => g.Title == "Inspect");
                if (tab.Title is "Surface" or "NURBS" or "Freeform")
                    inspectGroup.Items.Add(Btn("Zebra\nStripes", "zebra", () => Session.Zebra = !Session.Zebra, "Show reflection stripes: smooth, continuous stripes mean a smooth surface.", RibbonSize.Large, null, () => Session.Zebra));
                if (tab.Title is "Surface" or "NURBS")
                {
                    inspectGroup.Items.Add(Cmd("Curvature\nMap", "curvature", () => new CurvatureCommand(), "Color surfaces by curvature."));
                    inspectGroup.Items.Add(Cmd("Curvature\nGraph", "curvature", () => new CurveCurvatureCommand(), "Show a comb of hairs along a curve to judge how smooth it is."));
                }
                var references = tab.Groups.Where(g => g.Title is "Sketch" or "Planes").ToList();
                if (references.Count > 0)
                {
                    var tools = references.SelectMany(g => g.Items).OfType<RibbonButton>().ToArray();
                    foreach (var group in references) tab.Groups.Remove(group);
                    // one reference tool is a plain button; a dropdown of one is just an extra click
                    tab.Groups.Add(tools.Length == 2 ? G("References", tools) : G("References", tools[0], ToolMenu("Curves & planes", "origin-plane",
                        "Create spatial curves and work with construction references.", tools.Skip(1).ToArray())));
                }
            }
            if (tab.Title is "Home" or "Solid")
            {
                var primitives = tab.Groups.Single(g => g.Title is "Create" or "Primitives");
                var features = tab.Groups.Single(g => g.Title == "Features");
                tab.Groups.Remove(primitives); tab.Groups.Remove(features);
                tab.Groups.Add(G("Create", new[] { primitives.Items[0] }.Concat(features.Items).ToArray()));
            }
            if (tab.Title == "Solid") tab.Groups.RemoveAll(g => g.Title == "Direct");
            if(modeling.Contains(tab.Title))
                tab.Groups.Add(DatumGroup());
            if (tab.Title == "Solid")
            {
                var modify = tab.Groups.Single(g => g.Title == "Modify");
                var secondary = modify.Items.OfType<RibbonButton>().Where(b => b.Icon is "shell" or "draft" or "splitbody").ToArray();
                modify.Items.RemoveAll(i => i is RibbonButton { Icon: "shell" or "draft" or "splitbody" });
                modify.Items.Add(ToolMenu("More solid tools", "shell", "Hollow, draft or split a body.", secondary));
            }
            if (tab.Title == "Sketch")
            {
                var draw = tab.Groups.Single(g => g.Title == "Draw");
                var drawing = draw.Items.OfType<RibbonButton>().ToArray();
                var spline = drawing.Single(button => button.Label == "Spline");
                draw.Items.Clear();
                draw.Items.AddRange(drawing.Take(4));
                draw.Items.Add(spline);
                draw.Items.Add(new RibbonButton
                {
                    Id = "menu:More drawing tools:polygon|Polygon,slot|Slot,text|Text,spline|Spline",
                    Label = "More drawing tools",
                    Icon = "polygon",
                    Description = "Draw a polygon, slot or text.",
                    Menu = [.. drawing.Skip(4).Where(button => button != spline)]
                });

                var modify = tab.Groups.Single(g => g.Title == "Modify");
                var edits = modify.Items.OfType<RibbonButton>().ToArray();
                var promoted = new[] { "Trim", "Offset", "Transform" }.Select(label => edits.Single(button => button.Label == label)).ToArray();
                modify.Items.Clear();
                modify.Items.AddRange(promoted);
                modify.Items.Add(new RibbonButton
                {
                    Id = "menu:Sketch:Modify",
                    Label = "More edits",
                    Icon = "extend",
                    Description = "Insert points, extend curves, edit corners, mirror or pattern.",
                    Menu = [.. edits.Except(promoted)]
                });

                var reference = tab.Groups.Single(g => g.Title == "Reference");
                tab.Groups.Single(g => g.Title == "References").Items.AddRange(reference.Items);
                tab.Groups.Remove(reference);
            }
            if (tab.Title == "ECAD")
            {
                // Geometry placement uses the same transform commands as the modeling ribbons.
                tab.Groups.Single(g => g.Title == "Components").Items.RemoveAll(i => i is RibbonButton { HasMenu: true });
                tab.Groups.Add(transform());
            }
            if (tab.Title == "Analyze")
            {
                var section = tab.Groups.Single(g => g.Title == "Section");
                tab.Groups.Remove(section);
                tab.Groups.Add(G("Bounds", section.Items.OfType<RibbonButton>().Where(b => b.Icon == "bbox").ToArray()));
            }
            if (tab.Title == "Render")
            {
                var look = tab.Groups.Single(g => g.Title == "Look");
                tab.Groups.Remove(look);
                tab.Groups.Add(appearance());
            }
            if (tab.Title is "Thermal" or "Dynamics" or "Static" or "Motion")
            {
                foreach (var group in tab.Groups) group.Items.RemoveAll(i => i is RibbonButton { Icon: "materials" });
                tab.Groups.Add(appearance());
            }
            if (tab.Title is "Home" or "Solid")
            {
                var modify = tab.Groups.Single(g => g.Title == "Modify");
                var edgeTools = modify.Items.OfType<RibbonButton>().Where(b => b.Icon is "chamfer" or "fillet").OrderBy(b => b.Icon == "chamfer" ? 0 : 1).ToArray();
                modify.Items.RemoveAll(i => i is RibbonButton { Icon: "chamfer" or "fillet" });
                modify.Items.Insert(0, Split("split:solid-edge", "Chamfer or fillet edges. Click the icon to run the shown tool; click the name to choose another.", edgeTools));
            }
            foreach (var group in tab.Groups)
            {
                var buttons = group.Items.OfType<RibbonButton>().ToArray();
                if (group.Title == "Combine" && buttons.Length > 1)
                {
                    group.Items.Clear();
                    group.Items.Add(ToolMenu("Combine", "union", "Union, subtract, or intersect bodies.", buttons));
                    continue;
                }
                if (group.Title == "Pattern" && buttons.Length == 5)
                {
                    group.Items.Clear();
                    group.Items.Add(ToolMenu("Patterns", "pattern-linear", "Linear, circular, curve, fill and table patterns.", buttons));
                    continue;
                }
                if (group.Title == "Exchange" && tab.IsFile)
                {
                    group.Items.Clear();
                    group.Items.Add(ToolMenu("Import", "import", "Import CAD geometry or meshes.", buttons.Where(b => b.Label.StartsWith("Import", StringComparison.Ordinal)).ToArray()));
                    group.Items.Add(ToolMenu("Export", "export", "Export exact geometry, drawings or meshes.", buttons.Where(b => b.Label.StartsWith("Export", StringComparison.Ordinal)).ToArray()));
                    continue;
                }
                // A primary action plus related variants. Existing menus stay flat and retain every command.
                // Sketch constraints are used constantly and stack three to a column, so they stay one click away.
                bool compact = !(tab.Title == "Surface" && group.Title == "Create") && !(tab.Title == "Mesh" && group.Title == "Repair") && !(tab.Title == "NURBS" && group.Title == "Shape") && !(tab.Title == "Sketch" && group.Title == "Constrain") && !(tab.Title == "Freeform" && group.Title == "Loops") && (group.Title is "Transform" or "Inspect" or "Finish" or "Fill" or "Detail" or "Shape" or "Continuity" || buttons.Length > 2 && group.Title is not ("Document" or "Select" or "Results" or "Orientation" or "Display"));
                if (!compact || buttons.Length < 2 || buttons.Any(b => b.HasMenu)) continue;
                int keep = tab.Title == "Mesh" && group.Title == "Reshape" ? 3 : tab.Title == "Surface" && group.Title == "Modify" ? 2 : group.Title is "Inspect" or "Modify" or "Draw" or "Image" ? 1 : 0;
                group.Items.Clear(); group.Items.AddRange(buttons.Take(keep));
                string label = group.Title == "Inspect" ? "More inspection" : keep == 0 ? group.Title : "More";
                var menu = ToolMenu(label, buttons[keep].Icon,
                    "Related tools in this section. Every command is also available in command search.", buttons.Skip(keep).ToArray());
                menu.Id = "menu:" + tab.Title + ":" + group.Title;
                group.Items.Add(menu);
            }
            RibbonLayoutPolicy.Order(tab);
        }
    }

    /// <summary>A button that shows (and runs) the variant you used last and keeps the others in a list: one slot instead of five.</summary>
    RibbonButton Split(string id, string desc, params RibbonButton[] variants) => Split(RibbonSize.Large, id, desc, variants);
    RibbonButton Split(RibbonSize size, string id, string desc, params RibbonButton[] variants)
    {
        var picks = AppSettings.Current.LastPicks;
        var cur = picks.TryGetValue(id, out var last) ? variants.FirstOrDefault(v => v.Label.Replace("\n", " ") == last) ?? variants[0] : variants[0];
        return new RibbonButton
        {
            Id = id, Label = cur.Label.Replace("\n", " "), Icon = cur.Icon, Description = desc, Size = size, Menu = [.. variants], IsSplit = true, Current = cur,
            FollowChecked = true,      // a dropdown looks pressed, and shows the tool, while any tool inside it is running
            CanExecute = () => { foreach (var v in variants) { v.Refresh(); if (v.IsEnabled) return true; } return false; },
            Picked = v => { picks[id] = v.Label.Replace("\n", " "); AppSettings.Save(false); },
        };
    }

    static RibbonButton Followed(RibbonButton split) { split.FollowChecked = true; return split; }

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
        // Edited layouts may still refer to the former single curve dropdowns. Keep those
        // IDs as flat compatibility menus while new default layouts use the smaller families.
        foreach (var tab in Tabs.Where(t => !t.IsFile))
            foreach (var group in tab.Groups.Where(g => g.Title is "Curves" or "Edit curves"))
            {
                var leaves = group.Items.OfType<RibbonButton>().SelectMany(b => b.Menu ?? [b]).ToList();
                // Home used to combine creation and editing in its Curves dropdown.
                if (tab.Title == "Home" && group.Title == "Curves")
                    leaves.AddRange(tab.Groups.Single(g => g.Title == "Edit curves").Items.OfType<RibbonButton>().SelectMany(b => b.Menu ?? [b]));
                string id = "menu:" + tab.Title + ":" + group.Title;
                _catalog.TryAdd(id, new RibbonButton { Id = id, Label = group.Title, Icon = leaves[0].Icon, Menu = leaves,
                    Description = "Curve tools from your customized layout. Reset this tab in Settings to use the new grouped menus." });
                _catalogTab.TryAdd(id, tab.Title);
            }
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
