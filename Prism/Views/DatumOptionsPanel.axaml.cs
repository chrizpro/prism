using Avalonia.Controls;

namespace Prism.Views;

public partial class DatumOptionsPanel : UserControl
{
    public DatumOptionsPanel() => InitializeComponent();
    void OnGizmoSettings(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => (DataContext as Prism.ViewModels.MainViewModel)?.EditDatumWithGizmoPanel();
}
