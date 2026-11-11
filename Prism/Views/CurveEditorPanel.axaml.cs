using Avalonia.Controls;
using Avalonia.Interactivity;
using Prism.ViewModels;

namespace Prism.Views;

public partial class CurveEditorPanel : UserControl
{
    public CurveEditorPanel() => InitializeComponent();
    MainViewModel? Vm => DataContext as MainViewModel;
    void OnClose(object? sender, RoutedEventArgs e) => Vm?.CloseRightPanel("curve");
    void OnApply(object? sender, RoutedEventArgs e) => Vm?.ApplyCurveEditor();
}
