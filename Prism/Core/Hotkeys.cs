using System;
using System.Collections.Generic;
using System.Linq;

namespace Prism.Core;

/// <summary>
/// One thing a key can do. A shortcut lives in a scope: it works everywhere, only while sketching, only while a command is open,
/// or only inside one workspace. The same key can mean different things in different scopes; the most specific scope wins.
/// </summary>
public sealed record HotkeyAction(string Id, string Name, string Category, string Scope, string Default, string Note = "", bool Custom = false, string Target = "");

/// <summary>A shortcut the user added: a key that runs one ribbon command ("Solid|Fillet"), in the context they chose.</summary>
public sealed class CustomHotkey
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "Custom";
    public string Scope { get; set; } = Hotkeys.Global;
    public string Gesture { get; set; } = "";
    /// <summary>The ribbon command to run, written "Tab|Label" (the label with line breaks turned into spaces).</summary>
    public string Target { get; set; } = "";
}

/// <summary>The shortcut catalogue, the user's changes to it, and the lookup from a pressed key to the actions it may run.</summary>
public static class Hotkeys
{
    public const string Global = "global", Sketch = "sketch", Command = "command", Freeform = "freeform";
    public static string Workspace(string id) => "workspace:" + id;

    // order matters twice: it is the order shown in Preferences, and when two actions in the same scope share a key, the earlier one is tried first
    public static readonly IReadOnlyList<HotkeyAction> Actions =
    [
        new("sketch.start", "Start or resume sketch", "Sketch", Global, "K"),
        new("file.new", "New part", "File", Global, "Ctrl+N"),
        new("file.open", "Open…", "File", Global, "Ctrl+O"),
        new("file.save", "Save", "File", Global, "Ctrl+S"),
        new("file.saveas", "Save as…", "File", Global, "Ctrl+Shift+S"),

        new("edit.undo", "Undo", "Edit", Global, "Ctrl+Z"),
        new("edit.redo", "Redo", "Edit", Global, "Ctrl+Y"),
        new("edit.duplicate", "Duplicate selection", "Edit", Global, "Ctrl+D"),
        new("edit.delete", "Delete selection", "Edit", Global, "Delete"),
        new("edit.cancel", "Cancel / end the current step", "Edit", Global, "Escape"),
        new("edit.accept", "Accept / finish", "Edit", Global, "Enter"),

        new("view.lookat", "Look at the selected plane, face or sketch", "View", Global, "N"),
        new("view.extents", "Zoom to fit everything (Zoom Extents)", "View", Global, "F", "F for Fit; the same key fits the view in the drawing editor"),
        new("view.extents.home", "Zoom to fit everything (Home key)", "View", Global, "Home", "A second key for Zoom Extents; while the animation timeline is open Home goes to the start instead"),
        new("view.quad.maximize", "Maximize or restore hovered view (Four Views)", "View", Global, "Space", "Hover a pane in Four Views and press Space to maximize it, or press Space again to return; while the animation timeline is open Space plays or pauses"),
        new("app.search", "Search commands", "View", Global, "Ctrl+K"),
        new("model.extrude", "Extrude", "Modeling", Global, "Alt+X", "Outside sketching and active editors"),
        new("datum.gizmo", "Edit Gizmo", "Modeling", Global, "G", "At the viewport cursor (origin if the pointer is outside it)"),
        new("form.edit", "Edit selected Freeform (Modify)", "Freeform", Global, "M"),
        new("form.move", "Translate cage selection", "Freeform", Freeform, "T"),
        new("form.rotate", "Rotate cage selection", "Freeform", Freeform, "R"),
        new("form.scale", "Scale cage selection", "Freeform", Freeform, "S"),
        new("form.extrude", "Extrude cage selection", "Freeform", Freeform, "E"),
        new("form.crease", "Crease sharpness…", "Freeform", Freeform, "C"),
        new("form.vertex", "Select vertices", "Freeform", Freeform, "D1"),
        new("form.edge", "Select edges", "Freeform", Freeform, "D2"),
        new("form.face", "Select faces", "Freeform", Freeform, "D3"),
        new("form.object", "Select whole form", "Freeform", Freeform, "D4"),
        new("form.selectall", "Select all cage components", "Freeform", Freeform, "Ctrl+A"),
        new("form.loop", "Select edge loop", "Freeform", Freeform, "Alt+L"),
        new("form.ring", "Select edge ring", "Freeform", Freeform, "Alt+R"),

        new("sketch.gizmo.move", "Gizmo: move", "Sketch", Sketch, "W", "Needs a gizmo target"),
        new("sketch.gizmo.rotate", "Gizmo: rotate", "Sketch", Sketch, "R", "Needs a gizmo target"),
        new("sketch.gizmo.size", "Gizmo: scale", "Sketch", Sketch, "S", "Needs a gizmo target"),
        new("sketch.edit", "Toggle geometry editing (3D)", "Sketch", Sketch, "E"),
        new("sketch.plane.yz", "Draw on the YZ plane", "Sketch", Sketch, "Shift+X"),
        new("sketch.plane.xz", "Draw on the XZ plane", "Sketch", Sketch, "Shift+Y"),
        new("sketch.plane.xy", "Draw on the XY plane", "Sketch", Sketch, "Shift+Z"),
        new("sketch.line", "Line", "Sketch tools", Sketch, "L"),
        new("sketch.rect", "Rectangle", "Sketch tools", Sketch, "R", "Also rotates a rectangle while you place it"),
        new("sketch.ellipse", "Ellipse / circle", "Sketch tools", Sketch, "C"),
        new("sketch.spline", "Spline", "Sketch tools", Sketch, "S"),
        new("sketch.arc", "Arc", "Sketch tools", Sketch, "A"),
        new("sketch.dimension", "Dimension", "Sketch tools", Sketch, "D"),
        new("sketch.construction", "Toggle construction geometry", "Sketch tools", Sketch, "X"),

        new("animation.play", "Play / stop the animation", "Animation", Command, "Space", "While the animation timeline is open"),
        new("animation.start", "Animation back to the start", "Animation", Command, "Home", "While the animation timeline is open"),
    ];

    static Dictionary<string, string> Overrides => AppSettings.Current.Hotkeys;

    /// <summary>The built-in actions followed by the ones the user added.</summary>
    public static IEnumerable<HotkeyAction> All =>
        Actions.Concat(AppSettings.Current.CustomHotkeys.Select(c => new HotkeyAction(c.Id, c.Name, c.Category, c.Scope, c.Gesture, "Runs " + c.Target.Replace("|", " › "), Custom: true, Target: c.Target)));

    public const string CustomPrefix = "custom:";

    /// <summary>Add a shortcut that runs a ribbon command. Returns the new action.</summary>
    public static HotkeyAction AddCustom(string name, string target, string scope, string gesture)
    {
        var c = new CustomHotkey { Id = CustomPrefix + Guid.NewGuid().ToString("N")[..8], Name = name, Target = target, Scope = scope, Gesture = gesture };
        AppSettings.Current.CustomHotkeys.Add(c); AppSettings.Save();
        return All.First(a => a.Id == c.Id);
    }

    public static void RemoveCustom(string id) { if (AppSettings.Current.CustomHotkeys.RemoveAll(c => c.Id == id) > 0) AppSettings.Save(); }

    /// <summary>The contexts a shortcut can be tied to. Everyday actions choose between everywhere and one workspace; a custom shortcut can also be limited to sketching, an open command or Freeform editing.</summary>
    public static IReadOnlyList<(string Scope, string Name)> ScopeChoices(bool custom)
    {
        var list = new List<(string, string)> { (Global, ScopeName(Global)) };
        if (custom) list.AddRange([(Sketch, ScopeName(Sketch)), (Command, ScopeName(Command)), (Freeform, ScopeName(Freeform))]);
        list.AddRange(WorkspaceIds.Select(w => (Workspace(w), ScopeName(Workspace(w)))));
        return list;
    }

    /// <summary>The shortcut an action has right now, and the scope it works in, with the user's changes applied.</summary>
    public static (string Gesture, string Scope) Effective(HotkeyAction a)
    {
        if (Overrides.TryGetValue(a.Id, out var v))
        {
            int cut = v.IndexOf('|');
            string scope = cut >= 0 && v[(cut + 1)..].Length > 0 ? v[(cut + 1)..] : a.Scope;
            return (Canonical(cut >= 0 ? v[..cut] : v), scope);
        }
        return (a.Default, a.Scope);
    }
    static string Canonical(string gesture)=>string.Join("+",gesture.Split('+').Select(k=>k=="Return"?"Enter":k));

    public static bool IsChanged(HotkeyAction a) => Overrides.ContainsKey(a.Id);

    /// <summary>Only everyday actions can be limited to one workspace; sketch and command shortcuts are tied to what they act on.</summary>
    public static bool CanChangeScope(HotkeyAction a) => a.Scope == Global || a.Custom;

    public static HotkeyAction? Find(string id) => All.FirstOrDefault(a => a.Id == id);

    /// <summary>The actions bound to this key in the given scopes, most specific scope first. The caller tries each until one does something.</summary>
    public static IEnumerable<HotkeyAction> Resolve(string gesture, IReadOnlyList<string> activeScopes)
    {
        var all = All.ToList();
        for (int rank = 0; rank < activeScopes.Count; rank++)
            foreach (var a in all)
            {
                var (g, scope) = Effective(a);
                if (scope == activeScopes[rank] && string.Equals(g, gesture, StringComparison.OrdinalIgnoreCase)) yield return a;
            }
    }

    /// <summary>The scopes that apply right now, most specific first.</summary>
    public static List<string> ActiveScopes(bool commandOpen, bool sketching, string workspaceId, bool editingForm = false)
    {
        var scopes = new List<string>();
        if(editingForm)scopes.Add(Freeform);
        if (commandOpen) scopes.Add(Command);
        if (sketching) scopes.Add(Sketch);
        scopes.Add(Workspace(workspaceId));
        scopes.Add(Global);
        return scopes;
    }

    /// <summary>
    /// Which ribbon button a built-in action runs, so the search results can show its key. No tabs means the label is the same tool on
    /// every ribbon; otherwise only those ribbons have it (Extrude on Surface and NURBS is the surface extrude, which Alt+X does not start).
    /// </summary>
    static readonly Dictionary<string, (string[] Tabs, string Label)> ButtonOf = new()
    {
        ["sketch.start"] = (["NURBS"], "Create Sketch"), ["file.new"] = ([], "New"), ["file.open"] = ([], "Open"), ["file.save"] = ([], "Save"), ["file.saveas"] = ([], "Save As"),
        ["edit.duplicate"] = ([], "Duplicate"), ["view.lookat"] = ([], "Look At"), ["view.extents"] = ([], "Zoom Extents"), ["view.extents.home"] = ([], "Zoom Extents"),
        ["model.extrude"] = (["Home", "Solid", "Sheet Metal"], "Extrude"), ["datum.gizmo"] = ([], "Gizmo"), ["form.edit"] = ([], "Edit Freeform"),
        ["sketch.line"] = (["Sketch"], "Line"), ["sketch.rect"] = (["Sketch"], "Rectangle"), ["sketch.ellipse"] = (["Sketch"], "Ellipse"),
        ["sketch.spline"] = (["Sketch"], "Spline"), ["sketch.arc"] = (["Sketch"], "Arc"),
    };

    /// <summary>The key(s) the user has for a ribbon button right now ("F / Home"), with their changes and custom shortcuts applied; empty when it has none.</summary>
    public static string ShortcutFor(string tab, string label)
    {
        var keys = new List<string>();
        foreach (var a in All)
        {
            var (gesture, _) = Effective(a);
            if (gesture.Length == 0) continue;
            bool match = a.Custom ? a.Target == tab + "|" + label
                : ButtonOf.TryGetValue(a.Id, out var b) && b.Label == label && (b.Tabs.Length == 0 || b.Tabs.Contains(tab));
            if (match) keys.Add(Display(gesture));
        }
        return string.Join(" / ", keys.Distinct());
    }

    /// <summary>Another action that already uses this key in exactly this scope, if any.</summary>
    public static HotkeyAction? FindConflict(string id, string gesture, string scope)
    {
        if (gesture.Length == 0) return null;
        foreach (var a in All)
        {
            if (a.Id == id) continue;
            var (g, s) = Effective(a);
            if (s == scope && string.Equals(g, gesture, StringComparison.OrdinalIgnoreCase)) return a;
        }
        return null;
    }

    /// <summary>Give an action a new key (empty clears it) and, for everyday actions, optionally limit it to one workspace.</summary>
    public static void Set(string id, string gesture, string? scope = null)
    {
        var a = Find(id) ?? throw new ArgumentException("Unknown shortcut action " + id);
        if (a.Custom)
        {
            var c = AppSettings.Current.CustomHotkeys.First(x => x.Id == id);
            c.Gesture = gesture; if (scope != null) c.Scope = scope;
            AppSettings.Save(); return;
        }
        string sc = scope != null && CanChangeScope(a) ? scope : Effective(a).Scope;
        if (gesture == a.Default && sc == a.Scope) Overrides.Remove(id); else Overrides[id] = gesture + "|" + sc;
        AppSettings.Save();
    }

    public static void Reset(string id) { if (Overrides.Remove(id)) AppSettings.Save(); }
    public static void ResetAll() { if (Overrides.Count == 0) return; Overrides.Clear(); AppSettings.Save(); }

    public static string ScopeName(string scope) => scope switch
    {
        Global => "Everywhere",
        Sketch => "While sketching",
        Command => "While a command is open",
        Freeform => "While editing Freeform",
        _ when scope.StartsWith("workspace:", StringComparison.Ordinal) => "Only in " + WorkspaceName(scope[10..]),
        _ => scope,
    };

    static string WorkspaceName(string id) => id switch
    {
        "design" => "Design", "ecad" => "ECAD", "scan" => "Scan", "simulate" => "CAE", "assemble" => "Assemble", "render" => "Render", "make" => "CAM", "script" => "Script", "all" => "All tabs", _ => id,
    };

    /// <summary>The workspaces a shortcut can be limited to.</summary>
    public static readonly string[] WorkspaceIds = ["design", "ecad", "scan", "simulate", "assemble", "render", "make", "script"];
    public static string NameOfWorkspace(string id) => WorkspaceName(id);

    /// <summary>A gesture as people write it: "Ctrl+Shift+S", "D1" shown as "1".</summary>
    public static string Display(string gesture)
    {
        if (gesture.Length == 0) return "Not set";
        return string.Join("+", gesture.Split('+').Select(p => p switch
        {
            "OemPlus" => "+", "OemMinus" => "-", "OemPeriod" => ".", "OemComma" => ",", "Back" => "Backspace", "Return" => "Enter",
            _ when p.Length == 2 && p[0] == 'D' && char.IsDigit(p[1]) => p[1].ToString(),
            _ => p,
        }));
    }
}
