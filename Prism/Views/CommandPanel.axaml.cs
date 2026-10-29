using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Prism.Commands;
using Prism.ViewModels;
using Prism.Core;

namespace Prism.Views;

public partial class CommandPanel : UserControl
{
    MainViewModel? _observed;
    public CommandPanel()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach();
        AttachedToVisualTree += (_, _) => Attach();
        DetachedFromVisualTree += (_, _) => Detach();
    }
    MainViewModel? Vm => DataContext as MainViewModel;
    internal CadSession? Session => Vm?.Session;

    void Detach()
    {
        if (_observed != null) _observed.PropertyChanged -= OnViewModelChanged;
        _observed = null;
    }

    void Attach()
    {
        Detach(); _observed = Vm;
        if (_observed != null) _observed.PropertyChanged += OnViewModelChanged;
    }

    void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.ActiveCommand)) return;
        PanelScroll.Offset = default;
    }

    void OnOk(object? sender, RoutedEventArgs e) => Vm?.Session.CommitCommand();
    void OnCancel(object? sender, RoutedEventArgs e) => Vm?.Session.CancelCommand();
    void OnActionClick(object? sender, RoutedEventArgs e) { if ((sender as Control)?.DataContext is ActionParam a) a.Run(); }

}
