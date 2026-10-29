using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Prism.ViewModels;

namespace Prism.Views;

public partial class MaterialsPanel : UserControl
{
    public MaterialsPanel() { InitializeComponent(); }

    protected override void OnDataContextChanged(System.EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is not MainViewModel vm || Swatches.Items.Count > 0) return;
        foreach (var c in vm.Swatches)
        {
            var b = new Button
            {
                Width = 26, Height = 26, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(0), CornerRadius = new CornerRadius(13),
                Background = new SolidColorBrush(Color.FromUInt32(c)), BorderBrush = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)), BorderThickness = new Thickness(1),
            };
            var col = c;
            b.Click += (_, _) => Vm?.ApplyColor(col);
            ToolTip.SetTip(b, "#" + (col & 0xFFFFFF).ToString("X6"));
            Avalonia.Automation.AutomationProperties.SetName(b, "Color #" + (col & 0xFFFFFF).ToString("X6"));
            Swatches.Items.Add(b);
        }
    }

    MainViewModel? Vm => DataContext as MainViewModel;
    void OnShowLibrary(object? s, RoutedEventArgs e) { Vm?.ShowAssignedMaterial(); StudioTabs.SelectedIndex = 1; }
    void OnResetFinish(object? s, RoutedEventArgs e) => Vm?.ResetAppearance();
    void OnTogglePalette(object? s, RoutedEventArgs e) => Swatches.IsVisible = !Swatches.IsVisible;
    void OnFinishPicked(object? s, SelectionChangedEventArgs e)
    {
        if (Vm is { } vm && FinishLibrary.SelectedItem is FinishPreset preset && preset != vm.ActiveFinishPreset)
            vm.ApplyFinishPreset(preset);
    }
    void OnApply(object? s, RoutedEventArgs e) => Vm?.ApplyMaterial();
    void OnClose(object? s, RoutedEventArgs e) => Vm?.CloseMaterials();
    void OnCompare(object? s, RoutedEventArgs e) => Vm?.ToggleCompare();
    void OnListDoubleTapped(object? s, TappedEventArgs e) => Vm?.ApplyMaterial();
    void OnHexKey(object? s, KeyEventArgs e) { if (e.Key == Key.Enter) { Vm?.ApplyHex(HexBox.Text ?? ""); e.Handled = true; } }
}
