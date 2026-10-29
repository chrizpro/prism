using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Prism.ViewModels;

namespace Prism.Views;

public partial class AssistantPanel : UserControl
{
    public AssistantPanel()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainViewModel vm) vm.ChatMessages.CollectionChanged += OnMessages;
        };
    }

    MainViewModel? Vm => DataContext as MainViewModel;

    void OnMessages(object? s, NotifyCollectionChangedEventArgs e) => Dispatcher.UIThread.Post(() => Scroller.ScrollToEnd(), DispatcherPriority.Background);
    void OnSettings(object? s, RoutedEventArgs e) { if (Vm != null) Vm.AssistantSettingsOpen = !Vm.AssistantSettingsOpen; }
    void OnNewChat(object? s, RoutedEventArgs e) => Vm?.NewAssistantChat();
    async void OnSend(object? s, RoutedEventArgs e) { if (Vm != null) await Vm.SendAssistant(); }
    void OnStop(object? s, RoutedEventArgs e) => Vm?.StopAssistant();
    async void OnInputKey(object? s, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Vm != null) { e.Handled = true; await Vm.SendAssistant(); }
    }
}
