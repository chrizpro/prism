using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Prism.ViewModels;

namespace Prism.Views;

public partial class SimulationPanel : UserControl
{
    public SimulationPanel() { InitializeComponent(); }
    MainViewModel? Vm => DataContext as MainViewModel;
    void OnRun(object? s, RoutedEventArgs e) => Vm?.RunStudy();
    void OnMesh(object? s, RoutedEventArgs e) => Vm?.MeshStudy();
    void OnClear(object? s, RoutedEventArgs e) => Vm?.ClearResults();
    void OnRemoveRow(object? s, RoutedEventArgs e) { if (s is Button { Tag: Guid id }) Vm?.RemoveSimRow(id); }
}
