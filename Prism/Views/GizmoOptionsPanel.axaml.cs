using Avalonia.Controls;
using Avalonia.Interactivity;
using Prism.ViewModels;

namespace Prism.Views;

public partial class GizmoOptionsPanel : UserControl
{
    public GizmoOptionsPanel() => InitializeComponent();
    void OnReset(object? sender, RoutedEventArgs e) => (DataContext as MainViewModel)?.ResetGizmoDisplay();
    void OnPlace(object? sender,RoutedEventArgs e)=>(DataContext as MainViewModel)?.BeginGizmoPlacement();
    void OnBind(object? sender,RoutedEventArgs e)=>(DataContext as MainViewModel)?.BindGizmoSelection();
    void OnCancelPlacement(object? sender,RoutedEventArgs e)=>(DataContext as MainViewModel)?.CancelGizmoPlacement();
    void OnResetPivot(object? sender,RoutedEventArgs e)=>(DataContext as MainViewModel)?.ResetGizmoBinding();
    void OnReturn(object? sender,RoutedEventArgs e)=>(DataContext as MainViewModel)?.FinishGizmoPanel();
    void OnSketchAction(object? sender,RoutedEventArgs e){if(sender is Button{Tag:string action}&&DataContext is MainViewModel vm)vm.SketchSpaceAction(action);}
}
