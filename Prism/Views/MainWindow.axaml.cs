using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Prism.ViewModels;
using Prism.Core;

namespace Prism.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (DataContext is not MainViewModel vm || e.Handled) return;
        var s = vm.Session;
        bool typing = e.Source is TextBox;
        if (typing) { if (e.Key == Key.Escape) { Focus(); e.Handled = true; } return; }

        // shortcuts: the most specific scope first (a command that is open, then sketching, then the workspace, then everywhere)
        if (HotkeyInput.Gesture(e) is { } gesture)
            foreach (var action in Hotkeys.Resolve(gesture, Hotkeys.ActiveScopes(s.ActiveCommand != null, s.Sketch != null, vm.WorkspaceId)))
                if (Run(action.Id, vm)) { e.Handled = true; return; }

        // keys that feed whatever is being typed: these are not shortcuts and stay as they are
        bool alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        string? key = e.Key switch
        {
            Key.Back => "Back", Key.Tab => "Tab", Key.OemPlus or Key.Add => "Plus", Key.OemMinus or Key.Subtract => "Minus", _ => null,
        };
        if (key == null && (s.Sketch != null || s.ActiveCommand is Prism.Commands.Sketch3DCommand or Prism.Commands.EditFormCommand || s.ActiveCommand?.Handles.Count > 0) && !alt)
        {
            bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            char? ch = e.Key switch
            {
                >= Key.D0 and <= Key.D9 when !shift => (char)('0' + (e.Key - Key.D0)),
                >= Key.NumPad0 and <= Key.NumPad9 => (char)('0' + (e.Key - Key.NumPad0)),
                Key.OemPeriod or Key.Decimal => '.', Key.OemComma => ',',
                Key.L => 'L', Key.D => 'D', Key.X => 'X', Key.C => 'C', Key.H => 'H', Key.V => 'V', Key.A => 'A', Key.S => 'S', Key.R => 'R', Key.Y => 'Y', Key.Z => 'Z', Key.W => 'W', Key.E => 'E', _ => null,
            };
            // a sketch tool whose key the user moved no longer answers to its old letter
            if (ch != null && char.IsLetter(ch.Value) && s.Sketch != null && LetterMoved(ch.Value)) ch = null;
            if (ch != null) key = "Char:" + ch;
        }
        if (key != null && s.Key(key)) e.Handled = true;
    }

    static bool LetterMoved(char letter) => Hotkeys.Actions.Any(a => a.Scope == Hotkeys.Sketch && a.Default == letter.ToString() && Hotkeys.IsChanged(a));

    /// <summary>Do what a shortcut action means right now. False means it does not apply here, and the next candidate is tried.</summary>
    bool Run(string id, MainViewModel vm)
    {
        var s = vm.Session;
        bool free = s.Sketch != null && s.ActiveCommand == null;
        bool gizmoTarget = s.Sketch != null && (s.Sketch.GizmoEditing || (s.Sketch.Is3DEnabled || s.Sketch.PlanarGizmoActive) && s.Sketch.GeometryEditing && s.Sketch.HasGeometrySelection);
        switch (id)
        {
            case "file.new": vm.NewFile(); return true;
            case "file.open": vm.OpenFile(); return true;
            case "file.save": vm.SaveFile(false); return true;
            case "file.saveas": vm.SaveFile(true); return true;
            case "edit.undo": s.Undo(); return true;
            case "edit.redo": s.Redo(); return true;
            case "edit.duplicate": s.Duplicate(); return true;
            case "edit.delete": return s.Key("Delete");
            case "edit.cancel": return s.Key("Escape");
            case "edit.accept": return s.Key("Enter");
            case "view.lookat": return s.Key("LookAt");
            case "view.extents": s.ZoomExtents(); return true;
            case "app.search": (Content as MainView)?.FocusSearch(); return true;
            case "sketch.gizmo.move": return free && gizmoTarget && s.Key("GizmoMove");
            case "sketch.gizmo.rotate": return free && gizmoTarget && s.Key("GizmoRotate");
            case "sketch.gizmo.size": return free && gizmoTarget && s.Key("GizmoSize");
            case "sketch.gizmo": return free && s.Key(SketchShortcuts.GizmoCommand);
            case "sketch.edit": return free && s.Key(SketchShortcuts.EditCommand);
            case "sketch.plane.yz": return free && s.Key("PlaneYZ");
            case "sketch.plane.xz": return free && s.Key("PlaneXZ");
            case "sketch.plane.xy": return free && s.Key("PlaneXY");
            case "sketch.line": return s.Key("Char:L");
            case "sketch.rect": return s.Key("Char:R");
            case "sketch.ellipse": return s.Key("Char:C");
            case "sketch.spline": return s.Key("Char:S");
            case "sketch.arc": return s.Key("Char:A");
            case "sketch.dimension": return s.Key("Char:D");
            case "sketch.construction": return s.Key("Char:X");
            case "animation.play": if (vm.AnimationEditor == null) return false; vm.TimelineTogglePlay(); return true;
            case "animation.start": if (vm.AnimationEditor == null) return false; vm.TimelineToStart(); return true;
        }
        return false;
    }
}
