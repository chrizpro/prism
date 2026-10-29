using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Prism.ViewModels;

namespace Prism.Views;

public partial class SketchPanel : UserControl
{
    public SketchPanel() => InitializeComponent();
    MainViewModel? Vm => DataContext as MainViewModel;

    void OnFinish(object? sender, RoutedEventArgs e) => Vm?.Session.ExitSketch(true);
    void OnToolOption(object? sender, RoutedEventArgs e) { if(sender is Control { DataContext:ToolOptionItem option })Vm?.PickToolOption(option.Index); }
    void OnCancel(object? sender, RoutedEventArgs e) => Vm?.Session.ExitSketch(false);

    void OnDeleteRow(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is ConstraintRow row) Vm?.DeleteConstraint(row);
        e.Handled = true;
    }

    void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((e.Source as Control)?.DataContext is not ConstraintRow row) return;
        if (row.IsHeader) Vm?.ToggleSketchGroup(row.GroupName); else Vm?.EditConstraintValue(row);
    }

    void CommitNumber(TextBox tb)
    {
        if (tb.DataContext is ToolControlRow row && double.TryParse(tb.Text?.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) && (v > 0 || row.Id == "orientation" || row.Id.StartsWith("draw-"))) Vm?.SetToolControl(row.Id, v);
    }
    void OnToolNumberKey(object? sender, KeyEventArgs e) { if (e.Key == Key.Enter && sender is TextBox tb) { CommitNumber(tb); e.Handled = true; } }
    void OnToolNumberLost(object? sender, RoutedEventArgs e) { if (sender is TextBox tb) CommitNumber(tb); }
    void OnToolButton(object? sender, RoutedEventArgs e) { if ((sender as Control)?.DataContext is ToolControlRow row) Vm?.SetToolControl(row.Id, null); }

    void OnToggleGroup(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string g }) Vm?.ToggleSketchGroup(g);
        e.Handled = true;
    }
}
