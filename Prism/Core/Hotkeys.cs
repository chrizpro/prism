using System;
using System.Collections.Generic;
using System.Linq;

namespace Prism.Core;

/// <summary>
/// One thing a key can do. A shortcut lives in a scope: it works everywhere, only while sketching, only while a command is open,
/// or only inside one workspace. The same key can mean different things in different scopes; the most specific scope wins.
/// </summary>
public sealed record HotkeyAction(string Id, string Name, string Category, string Scope, string Default, string Note = "");

/// <summary>The shortcut catalogue, the user's changes to it, and the lookup from a pressed key to the actions it may run.</summary>
public static class Hotkeys
{
    public const string Global = "global", Sketch = "sketch", Command = "command";
    public static string Workspace(string id) => "workspace:" + id;

    // order matters twice: it is the order shown in Preferences, and when two actions in the same scope share a key, the earlier one is tried first
    public static readonly IReadOnlyList<HotkeyAction> Actions =
    [
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
        new("view.extents", "Zoom to fit everything", "View", Global, "Home"),
        new("app.search", "Search commands", "View", Global, "Ctrl+K"),

        new("sketch.gizmo.move", "Gizmo: move", "Sketch", Sketch, "W", "Needs a gizmo target"),
        new("sketch.gizmo.rotate", "Gizmo: rotate", "Sketch", Sketch, "R", "Needs a gizmo target"),
        new("sketch.gizmo.size", "Gizmo: scale", "Sketch", Sketch, "S", "Needs a gizmo target"),
        new("sketch.gizmo", "Toggle gizmo setup", "Sketch", Sketch, "G"),
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

    /// <summary>The shortcut an action has right now, and the scope it works in, with the user's changes applied.</summary>
    public static (string Gesture, string Scope) Effective(HotkeyAction a)
    {
        if (Overrides.TryGetValue(a.Id, out var v))
        {
            int cut = v.IndexOf('|');
            string scope = cut >= 0 && v[(cut + 1)..].Length > 0 ? v[(cut + 1)..] : a.Scope;
            return (cut >= 0 ? v[..cut] : v, scope);
        }
        return (a.Default, a.Scope);
    }

    public static bool IsChanged(HotkeyAction a) => Overrides.ContainsKey(a.Id);

    /// <summary>Only everyday actions can be limited to one workspace; sketch and command shortcuts are tied to what they act on.</summary>
    public static bool CanChangeScope(HotkeyAction a) => a.Scope == Global;

    public static HotkeyAction? Find(string id) => Actions.FirstOrDefault(a => a.Id == id);

    /// <summary>The actions bound to this key in the given scopes, most specific scope first. The caller tries each until one does something.</summary>
    public static IEnumerable<HotkeyAction> Resolve(string gesture, IReadOnlyList<string> activeScopes)
    {
        for (int rank = 0; rank < activeScopes.Count; rank++)
            foreach (var a in Actions)
            {
                var (g, scope) = Effective(a);
                if (scope == activeScopes[rank] && string.Equals(g, gesture, StringComparison.OrdinalIgnoreCase)) yield return a;
            }
    }

    /// <summary>The scopes that apply right now, most specific first.</summary>
    public static List<string> ActiveScopes(bool commandOpen, bool sketching, string workspaceId)
    {
        var scopes = new List<string>();
        if (commandOpen) scopes.Add(Command);
        if (sketching) scopes.Add(Sketch);
        scopes.Add(Workspace(workspaceId));
        scopes.Add(Global);
        return scopes;
    }

    /// <summary>Another action that already uses this key in exactly this scope, if any.</summary>
    public static HotkeyAction? FindConflict(string id, string gesture, string scope)
    {
        if (gesture.Length == 0) return null;
        foreach (var a in Actions)
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
