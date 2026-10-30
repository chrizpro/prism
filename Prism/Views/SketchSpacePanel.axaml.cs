using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Prism.ViewModels;

namespace Prism.Views;

public partial class SketchSpacePanel : UserControl
{
    void OnGizmoPanel(object? sender,RoutedEventArgs e)=>(DataContext as MainViewModel)?.OpenGizmoPanel();
    public SketchSpacePanel() => InitializeComponent();
    void OnTransformKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox { Tag: string action } && DataContext is MainViewModel vm)
        { vm.SketchSpaceAction(action); e.Handled = true; }
    }
    void OnAction(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string action } && DataContext is MainViewModel vm) vm.SketchSpaceAction(action);
    }
}
