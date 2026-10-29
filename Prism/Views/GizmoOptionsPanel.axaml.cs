using Avalonia.Controls;
using Avalonia.Interactivity;
using Prism.ViewModels;

namespace Prism.Views;

public partial class GizmoOptionsPanel : UserControl
{
    public GizmoOptionsPanel() => InitializeComponent();
    void OnReset(object? sender, RoutedEventArgs e) => (DataContext as MainViewModel)?.ResetGizmoDisplay();
}
