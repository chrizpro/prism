using Avalonia.Controls;
using Avalonia.Interactivity;
using Prism.ViewModels;

namespace Prism.Views;

/// <summary>The start page: workspace folder, new / open / import, parts in the workspace and recent files.</summary>
public partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
        var documents = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);
        var workspace = MainViewModel.DefaultWorkspacePath;
        // The browser has no Documents folder (empty path), and String.Replace rejects an empty pattern.
        DefaultLabel.Text = "Use " + (documents.Length > 0 ? workspace.Replace(documents, "Documents") : workspace);
        AttachedToVisualTree += async (_, _) => { if (Vm is { } vm && vm.HomeOpen) await vm.LoadGallery(); };
        PropertyChanged += async (_, e) => { if (e.Property == IsVisibleProperty && IsVisible && Vm is { } vm) await vm.LoadGallery(); };
    }

    MainViewModel? Vm => DataContext as MainViewModel;

    void OnNew(object? s, RoutedEventArgs e) => Vm?.NewPart();
    void OnOpen(object? s, RoutedEventArgs e) => Vm?.OpenFile();
    void OnImportStep(object? s, RoutedEventArgs e) { Vm?.CloseHome(); Vm?.ImportStep(); }
    void OnImportMesh(object? s, RoutedEventArgs e) { Vm?.CloseHome(); Vm?.ImportMesh(); }
    void OnUseDefault(object? s, RoutedEventArgs e) => Vm?.UseDefaultWorkspace();
    void OnChoose(object? s, RoutedEventArgs e) => Vm?.ChooseWorkspaceFolder();
    void OnExplorer(object? s, RoutedEventArgs e) => Vm?.ShowWorkspaceInExplorer();
    void OnSettings(object? s, RoutedEventArgs e) => Vm?.RequestSettings?.Invoke();
    void OnClose(object? s, RoutedEventArgs e) => Vm?.CloseHome();
    void OnOpenFile(object? s, RoutedEventArgs e) { if (s is Button { Tag: string path }) Vm?.OpenPath(path); }
    async void OnGalleryRefresh(object? s, RoutedEventArgs e) { if (Vm is { } vm) await vm.LoadGallery(refresh: true); }
    async void OnGalleryOpen(object? s, RoutedEventArgs e) { if (s is Button { Tag: GalleryCard card } && Vm is { } vm) await vm.OpenGalleryCard(card); }
}
