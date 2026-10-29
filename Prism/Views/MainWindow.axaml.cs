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
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool typing = e.Source is TextBox;

        if (ctrl && !typing)
        {
            switch (e.Key)
            {
                case Key.Z: s.Undo(); e.Handled = true; return;
                case Key.Y: s.Redo(); e.Handled = true; return;
                case Key.S: vm.SaveFile(e.KeyModifiers.HasFlag(KeyModifiers.Shift)); e.Handled = true; return;
                case Key.O: vm.OpenFile(); e.Handled = true; return;
                case Key.N: vm.NewFile(); e.Handled = true; return;
                case Key.D: s.Duplicate(); e.Handled = true; return;
                case Key.K: (Content as MainView)?.FocusSearch(); e.Handled = true; return;
            }
        }
        if (typing) { if (e.Key == Key.Escape) { Focus(); e.Handled = true; } return; }

        if (s.Sketch != null && s.ActiveCommand == null && !ctrl && !e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            string? sketchKey = e.Key switch
            {
                Key.X when shift => "PlaneYZ", Key.Y when shift => "PlaneXZ", Key.Z when shift => "PlaneXY",
                Key.G => SketchShortcuts.GizmoCommand, Key.E => SketchShortcuts.EditCommand,
                Key.W when s.Sketch.GizmoEditing || (s.Sketch.Is3DEnabled || s.Sketch.PlanarGizmoActive) && s.Sketch.GeometryEditing && s.Sketch.HasGeometrySelection => "GizmoMove",
                Key.R when s.Sketch.GizmoEditing || (s.Sketch.Is3DEnabled || s.Sketch.PlanarGizmoActive) && s.Sketch.GeometryEditing && s.Sketch.HasGeometrySelection => "GizmoRotate",
                Key.S when s.Sketch.GizmoEditing || (s.Sketch.Is3DEnabled || s.Sketch.PlanarGizmoActive) && s.Sketch.GeometryEditing && s.Sketch.HasGeometrySelection => "GizmoSize", _ => null,
            };
            if (sketchKey != null && s.Key(sketchKey)) { e.Handled = true; return; }
        }

        string? key = e.Key switch
        {
            Key.Escape => "Escape", Key.Enter => "Enter", Key.Delete => "Delete", Key.Back => "Back",
            Key.N => "LookAt", Key.Tab => "Tab", Key.OemPlus or Key.Add => "Plus", Key.OemMinus or Key.Subtract => "Minus", _ => null,
        };
        if (key == null && (s.Sketch != null || s.ActiveCommand is Prism.Commands.Sketch3DCommand or Prism.Commands.EditFormCommand || s.ActiveCommand?.Handles.Count > 0) && !e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            char? ch = e.Key switch
            {
                >= Key.D0 and <= Key.D9 when !shift => (char)('0' + (e.Key - Key.D0)),
                >= Key.NumPad0 and <= Key.NumPad9 => (char)('0' + (e.Key - Key.NumPad0)),
                Key.OemPeriod or Key.Decimal => '.', Key.OemComma => ',',
                Key.L => 'L', Key.D => 'D', Key.X => 'X', Key.C => 'C', Key.H => 'H', Key.V => 'V', Key.A => 'A', Key.S => 'S', Key.R => 'R', Key.Y => 'Y', Key.Z => 'Z', Key.W => 'W', Key.E => 'E', _ => null,
            };
            if (ch != null) key = "Char:" + ch;
        }
        if (key != null && s.Key(key)) e.Handled = true;
        else if (e.Key == Key.Home) { s.ZoomExtents(); e.Handled = true; }
    }
}
