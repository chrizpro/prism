using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Prism.ViewModels;
using Prism.Core;

namespace Prism.Views;

public partial class MainWindow : Window
{
    public MainWindow() { InitializeComponent(); InitializeDocumentSafety(); InitializeFileDrop(); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (DataContext is not MainViewModel vm || e.Handled) return;
        var s = vm.Session;
        bool typing = e.Source is TextBox;
        if (typing) { if (e.Key == Key.Escape) { Focus(); e.Handled = true; } return; }

        // typing text into the viewport: letters are text, not shortcuts. Ctrl and Alt combinations (undo...) still work as usual.
        if (s.Sketch?.Tool is Prism.Core.SketchTextTool { Editing: true })
        {
            bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
            string? textKey = e.Key switch { Key.Back => "Back", Key.Enter or Key.Return => ctrl ? "CtrlEnter" : "Enter", Key.Escape => "Escape", _ => null };
            if (textKey != null) { if (s.Key(textKey)) e.Handled = true; return; }
            if (!ctrl && !e.KeyModifiers.HasFlag(KeyModifiers.Alt)) return;
        }

        // shortcuts: the most specific scope first (a command that is open, then sketching, then the workspace, then everywhere)
        if (HotkeyInput.Gesture(e) is { } gesture)
            foreach (var action in Hotkeys.Resolve(gesture, Hotkeys.ActiveScopes(s.ActiveCommand != null, s.Sketch != null, vm.WorkspaceId, s.ActiveCommand is Prism.Commands.EditFormCommand)))
                if (Run(action.Id, vm)) { e.Handled = true; return; }

        // keys that feed whatever is being typed: these are not shortcuts and stay as they are
        bool alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        string? key = e.Key switch
        {
            Key.Back => "Back", Key.Tab => "Tab", Key.OemPlus or Key.Add => "Plus", Key.OemMinus or Key.Subtract => "Minus", _ => null,
        };
        if (key == null && (s.Sketch != null || s.ActiveCommand is Prism.Commands.Sketch3DCommand or Prism.Commands.EditFormCommand || s.ActiveTool is Prism.Commands.ImageGizmoTool || s.ActiveCommand?.Handles.Count > 0) && !alt && !e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            char? ch = e.Key switch
            {
                >= Key.D0 and <= Key.D9 when !shift => (char)('0' + (e.Key - Key.D0)),
                >= Key.NumPad0 and <= Key.NumPad9 => (char)('0' + (e.Key - Key.NumPad0)),
                Key.OemPeriod or Key.Decimal => '.', Key.OemComma => ',',
                Key.L => 'L', Key.D => 'D', Key.X => 'X', Key.C => 'C', Key.H => 'H', Key.V => 'V', Key.A => 'A', Key.S => 'S', Key.R => 'R', Key.T => 'T', Key.Y => 'Y', Key.Z => 'Z', Key.W => 'W', Key.E => 'E', _ => null,
            };
            // a sketch tool whose key the user moved no longer answers to its old letter
            if (ch != null && char.IsLetter(ch.Value) && s.Sketch != null && LetterMoved(ch.Value)) ch = null;
            if(ch!=null && s.ActiveCommand is Prism.Commands.EditFormCommand && Hotkeys.Actions.Any(a=>a.Scope==Hotkeys.Freeform && a.Default==ch.ToString()))ch=null;
            if (ch != null) key = "Char:" + ch;
        }
        if (key != null && s.Key(key)) e.Handled = true;
    }

    protected override void OnTextInput(Avalonia.Input.TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (e.Handled || e.Source is TextBox || string.IsNullOrEmpty(e.Text) || DataContext is not MainViewModel vm) return;
        if (vm.Session.Sketch?.Tool is Prism.Core.SketchTextTool { Editing: true } text) { text.TypeText(vm.Session, e.Text); e.Handled = true; }
    }

    static bool LetterMoved(char letter) => Hotkeys.Actions.Any(a => a.Scope == Hotkeys.Sketch && a.Default == letter.ToString() && Hotkeys.IsChanged(a));

    /// <summary>Do what a shortcut action means right now. False means it does not apply here, and the next candidate is tried.</summary>
    bool Run(string id, MainViewModel vm)
    {
        if (id.StartsWith(Hotkeys.CustomPrefix, System.StringComparison.Ordinal)) return Hotkeys.Find(id) is { } custom && vm.RunCommandShortcut(custom.Target);
        var s = vm.Session;
        bool free = s.Sketch != null && s.ActiveCommand == null;
        bool gizmoTarget = s.Sketch != null && (s.Sketch.GizmoEditing || (s.Sketch.Is3DEnabled || s.Sketch.PlanarGizmoActive) && s.Sketch.GeometryEditing && s.Sketch.HasGeometrySelection);
        if(id.StartsWith("form.") && s.ActiveCommand is Prism.Commands.EditFormCommand form)
        {
            if(form.HasPanelStep)return true;
            if(form.Tool is Prism.Commands.FormEditTool {Binding.Positioning:true} && id is not ("form.move" or "form.rotate" or "form.vertex" or "form.edge" or "form.face" or "form.object"))return true;
            switch(id)
            {
                case "form.move":form.ForceMode(0);break;
                case "form.rotate":form.ForceMode(1);break;
                case "form.scale":form.ForceMode(2);break;
                case "form.vertex":form.SetSelectionMode(Prism.Commands.FormMode.Vertex);break;
                case "form.edge":form.SetSelectionMode(Prism.Commands.FormMode.Edge);break;
                case "form.face":form.SetSelectionMode(Prism.Commands.FormMode.Face);break;
                case "form.object":form.SetSelectionMode(Prism.Commands.FormMode.Object);break;
                case "form.crease":form.OpenCrease();break;
                case "form.selectall":form.SelectOp("all");break;
                case "form.extrude":form.Params.OfType<Prism.Commands.ActionParam>().Single(a=>a.Label=="Extrude selection").Run();break;
                case "form.loop":form.SelectEdgeLoopKind(false);break;
                case "form.ring":form.SelectEdgeLoopKind(true);break;
                default:return false;
            }
            return true;
        }
        switch (id)
        {
            case "sketch.start": if (s.ActiveCommand != null) return false; vm.StartFluidSketch(); return true;
            case "model.extrude":if(s.Sketch!=null || s.ActiveCommand?.Tool!=null)return false;s.StartCommand(()=>new Prism.Commands.ExtrudeCommand());return true;
            case "form.edit":if(s.Sketch!=null || s.ActiveCommand!=null || !s.SelectedObjects.Any(o=>o.Form!=null))return false;s.StartCommand(()=>new Prism.Commands.EditFormCommand());return true;
            case "file.new": vm.NewFile(); return true;
            case "file.open": vm.OpenFile(); return true;
            case "file.save": vm.SaveFile(false); return true;
            case "file.saveas": vm.SaveFile(true); return true;
            case "edit.undo":if(vm.ShowGizmoPanel&&vm.GizmoPositioning&&!vm.GizmoIsDatum){vm.CancelGizmoPlacement();return true;}s.Undo(); return true;
            case "edit.redo": s.Redo(); return true;
            case "edit.duplicate": s.Duplicate(); return true;
            case "edit.delete": return s.Key("Delete");
            case "edit.cancel":if(s.PendingNotice==null&&vm.ShowGizmoPanel&&vm.GizmoPositioning&&!vm.GizmoIsDatum){vm.CancelGizmoPlacement();vm.CloseGizmoPanel();return true;}return s.Key("Escape");
            case "edit.accept":if(s.PendingNotice==null&&vm.ShowGizmoPanel&&vm.GizmoPositioning&&!vm.GizmoIsDatum){vm.BindGizmoSelection();return true;}return s.Key("Enter");
            case "view.lookat": return s.Key("LookAt");
            case "view.extents": case "view.extents.home": s.ZoomExtents(); return true;
            case "app.search": (Content as MainView)?.FocusSearch(); return true;
            case "sketch.gizmo.move": return free && gizmoTarget && s.Key("GizmoMove");
            case "sketch.gizmo.rotate": return free && gizmoTarget && s.Key("GizmoRotate");
            case "sketch.gizmo.size": return free && gizmoTarget && s.Key("GizmoSize");
            case "sketch.gizmo": if(s.Sketch!=null)return false;     // the sketch gizmo's own editing is retired: planes and gizmos come from the Datum tools
            if(free && vm.CanPlaceGizmo){ if(vm.ShowGizmoPanel){ if(vm.GizmoPositioning&&!vm.GizmoIsDatum){vm.FinishGizmoPanel();} if(vm.ShowGizmoPanel)vm.CloseGizmoPanel(); return true; } vm.OpenGizmoFromHotkey();return true;} return free && s.Key(SketchShortcuts.GizmoCommand);
            case "datum.gizmo":
                // In a sketch G takes the gizmo on the selected points and curves (again closes its panel); it places nothing.
                if(s.Sketch!=null){ if(!free||!gizmoTarget)return false; if(vm.ShowGizmoPanel){vm.CloseGizmoPanel();return true;} vm.TransformSketchSelection();return true; }
                if(s.ActiveCommand!=null&&!vm.CanPlaceGizmo)return false;vm.OpenGizmoFromHotkey();return true;
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
