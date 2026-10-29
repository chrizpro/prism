using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Prism.ViewModels;

namespace Prism.Views;

public partial class LibraryPanel : UserControl
{
    public LibraryPanel() { InitializeComponent(); }
    MainViewModel? Vm => DataContext as MainViewModel;

    void OnClose(object? sender, RoutedEventArgs e) => Vm?.CloseRightPanel("library");
    void OnRescan(object? sender, RoutedEventArgs e) => Vm?.RescanLibrary();
    void OnInsert(object? sender, RoutedEventArgs e) => Vm?.InsertLibraryItem(null);

    async void OnChooseFolder(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || TopLevel.GetTopLevel(this) is not { } top) return;
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Parts library folder", AllowMultiple = false });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is { } path) vm.SetLibraryFolder(path);
    }

    void OnTilePressed(object? sender, PointerPressedEventArgs e)
    {
        if (Vm is not { } vm || sender is not Control { Tag: LibraryItem item } || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        vm.SelectedLibraryItem = item;
        if (e.ClickCount >= 2) vm.InsertLibraryItem(item);
    }
}
