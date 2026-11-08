using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Prism.ViewModels;

namespace Prism.Views;

public partial class MaterialsPanel : UserControl
{
    public MaterialsPanel()
    {
        InitializeComponent();
        PickerWheel.AddHandler(PointerPressedEvent, (_, _) => Vm?.BeginAppearanceEdit(), RoutingStrategies.Tunnel);
        PickerWheel.AddHandler(PointerReleasedEvent, (_, _) => Vm?.EndAppearanceEdit(), RoutingStrategies.Bubble, true);
        PickerWheel.AddHandler(PointerCaptureLostEvent, (_, _) => Vm?.EndAppearanceEdit(), RoutingStrategies.Bubble, true);
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (e.Source is Avalonia.Visual v && Avalonia.VisualTree.VisualExtensions.FindAncestorOfType<Slider>(v, true) != null) Vm?.BeginAppearanceEdit();
        }, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, (_, _) => Vm?.EndAppearanceEdit(), RoutingStrategies.Bubble, true);
        AddHandler(PointerCaptureLostEvent, (_, _) => Vm?.EndAppearanceEdit(), RoutingStrategies.Bubble, true);
    }

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
    void OnGenericAppearance(object? s, RoutedEventArgs e) => Vm?.ApplyGenericAppearance();
    void OnSaveAppearance(object? s, RoutedEventArgs e) => Vm?.SaveCustomAppearance();
    void OnApplySavedAppearance(object? s, RoutedEventArgs e) => Vm?.ApplySavedAppearance();
    void OnRemoveSavedAppearance(object? s, RoutedEventArgs e) => Vm?.RemoveSavedAppearance();
    void OnResetFinish(object? s, RoutedEventArgs e) => Vm?.ResetAppearance();
    void OnFinishPicked(object? s, SelectionChangedEventArgs e)
    {
        if (Vm is { } vm && FinishLibrary.SelectedItem is FinishPreset preset && preset != vm.ActiveFinishPreset)
            vm.ApplyFinishPreset(preset);
    }
    void OnBeginCustomMaterial(object? s, RoutedEventArgs e) => Vm?.BeginCustomMaterial();
    void OnSaveCustomMaterial(object? s, RoutedEventArgs e) => Vm?.SaveCustomMaterial();
    void OnRemoveCustomMaterial(object? s, RoutedEventArgs e) => Vm?.RemoveCustomMaterial();
    void OnApply(object? s, RoutedEventArgs e) => Vm?.ApplyMaterial();
    void OnFamily(object? s, RoutedEventArgs e) { if (s is Button { Tag: int index }) Vm?.SelectFamily(index); }
    void OnClose(object? s, RoutedEventArgs e) => Vm?.CloseMaterials();
    void OnCompare(object? s, RoutedEventArgs e) => Vm?.ToggleCompare();
    void OnListDoubleTapped(object? s, TappedEventArgs e) => Vm?.ApplyMaterial();
    void OnHexKey(object? s, KeyEventArgs e) { if (e.Key == Key.Enter) { Vm?.ApplyHex(HexBox.Text ?? ""); e.Handled = true; } }
    void OnColorCodeLostFocus(object? s, RoutedEventArgs e) { if (Vm is { } vm && ColorCodeDiffers(vm)) vm.ApplyHex(HexBox.Text ?? ""); }
    bool ColorCodeDiffers(MainViewModel vm) => !Prism.Core.ColorCode.TryParse(HexBox.Text, out var color) || color != vm.AppearanceColor;
    void OnRememberedColor(object? s, RoutedEventArgs e) { if (s is Button { Tag: uint color }) Vm?.ApplyColor(color); }
    void OnSaveColor(object? s, RoutedEventArgs e) => Vm?.SaveAppearanceColor();
    void OnColorWheelPicked(object? s, System.EventArgs e) { if (s is Controls.ColorWheel wheel) Vm?.ApplyColor(wheel.Color); }
    async void OnScreenPick(object? s, RoutedEventArgs e)
    {
        ChooseColor.Flyout?.Hide();
        if (MainViewModel.PickScreenColor == null) { Vm?.Session.Say("Screen color sampling is not available on this platform. Use color codes or the wheel.", true); return; }
        try { var color = await MainViewModel.PickScreenColor(); if (color.HasValue) Vm?.ApplyColor(color.Value); }
        catch (System.Exception ex) { Vm?.Session.Say("Could not sample the screen: " + ex.Message, true); }
    }
    void SetGradientColor(object? source)
    {
        if (Vm is not { } vm || source is not TextBox box) return;
        switch (box.Tag?.ToString()) { case "2": if (box.Text != vm.GradientColor2) vm.GradientColor2 = box.Text ?? ""; break; case "3": if (box.Text != vm.GradientColor3) vm.GradientColor3 = box.Text ?? ""; break; case "4": if (box.Text != vm.GradientColor4) vm.GradientColor4 = box.Text ?? ""; break; }
    }
    void OnGradientColorKey(object? s, KeyEventArgs e) { if (e.Key == Key.Enter) { SetGradientColor(s); e.Handled = true; } }
    void OnGradientColorLostFocus(object? s, RoutedEventArgs e) => SetGradientColor(s);
    async void OnImportMap(object? s, RoutedEventArgs e) { if (Vm is { } vm && s is Button { Tag: string kind }) await vm.ImportSurfaceMap(kind); }
    void OnClearMap(object? s, RoutedEventArgs e) { if (s is Button { Tag: string kind }) Vm?.ClearSurfaceMap(kind); }
    void OnCenterProjection(object? s, RoutedEventArgs e) => Vm?.CenterTextureProjection();
    void OnClearGlass(object? s, RoutedEventArgs e) => Vm?.ApplyTransparentPreset(false);
    void OnFrostedGlass(object? s, RoutedEventArgs e) => Vm?.ApplyTransparentPreset(true);
    void OnAbsorptionKey(object? s, KeyEventArgs e) { if (e.Key == Key.Enter) { Vm?.ApplyAbsorptionColor(AbsorptionTint.Text ?? ""); e.Handled = true; } }
    void OnAbsorptionLostFocus(object? s, RoutedEventArgs e) { if (Vm is {} vm && AbsorptionTint.Text != vm.AbsorptionColor) vm.ApplyAbsorptionColor(AbsorptionTint.Text ?? ""); }
}
