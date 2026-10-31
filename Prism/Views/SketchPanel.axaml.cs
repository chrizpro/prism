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

    void OnToggleRow(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is ConstraintRow row) Vm?.ToggleConstraint(row);
        e.Handled = true;
    }

    void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((e.Source as Control)?.DataContext is not ConstraintRow row) return;
        if (row.IsHeader) Vm?.ToggleSketchGroup(row.GroupName); else Vm?.EditConstraintValue(row);
    }

    void CommitNumber(TextBox box)
    {
        if (box.DataContext is ToolControlRow row) Vm?.CommitToolNumber(row);
    }
    void OnToolNumberFocus(object? sender, GotFocusEventArgs e) { if (sender is TextBox { DataContext: ToolControlRow row }) row.IsEditing = true; }
    void OnToolNumberKey(object? sender, KeyEventArgs e) { if (e.Key == Key.Enter && sender is TextBox box) { CommitNumber(box); e.Handled = true; } }
    void OnToolNumberLost(object? sender, RoutedEventArgs e)
    {
        if (sender is not TextBox { DataContext: ToolControlRow row } box) return;
        CommitNumber(box); row.IsEditing = false;
    }
    void OnToolButton(object? sender, RoutedEventArgs e) { if ((sender as Control)?.DataContext is ToolControlRow row && ReferenceEquals(row.Owner, Vm?.Session.Sketch?.Tool)) Vm?.SetToolControl(row.Id, null); }

    void OnToggleGroup(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string g }) Vm?.ToggleSketchGroup(g);
        e.Handled = true;
    }
}
