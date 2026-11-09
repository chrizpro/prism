using System;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Prism.Automation;
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
    void OnCodexLogin(object? s, RoutedEventArgs e) => Vm?.CodexLogin();
    void OnFixKey(object? s, RoutedEventArgs e) => Vm?.FixAssistantKey();
    async void OnSend(object? s, RoutedEventArgs e) { if (Vm != null) await Vm.SendAssistant(); }
    void OnStop(object? s, RoutedEventArgs e) => Vm?.StopAssistant();
    void OnModelsRefresh(object? s, RoutedEventArgs e) { if (Vm != null) _ = Vm.RefreshAssistantModels(force: true); }
    void OnRemoveAttachment(object? s, RoutedEventArgs e) { if (s is Control { Tag: AssistantImage img }) Vm?.RemoveAssistantAttachment(img); }

    void OnAttach(object? s, RoutedEventArgs e)
    {
        if (s is not Control anchor || Vm is not { } vm) return;
        MenuItem Item(string header, Action run) { var item = new MenuItem { Header = header }; item.Click += (_, _) => run(); return item; }
        new MenuFlyout
        {
            Placement = PlacementMode.TopEdgeAlignedLeft,
            ItemsSource = new object[] { Item("Image file…", () => _ = AttachFile(vm)), Item("Current 3D view", () => AttachView(vm)) },
        }.ShowAt(anchor);
    }

    async System.Threading.Tasks.Task AttachFile(MainViewModel vm)
    {
        var path = vm.PickOpenFile == null ? null : await vm.PickOpenFile("Attach an image", ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.gif", "*.bmp"]);
        if (path == null) return;
        var err = vm.AddAssistantAttachment(Path.GetFileName(path), File.ReadAllBytes(path));
        if (err != null) vm.AssistantNote(err);
    }

    void AttachView(MainViewModel vm)
    {
        var top = TopLevel.GetTopLevel(this);
        var view = top?.GetVisualDescendants().OfType<Rendering.GlViewport>().FirstOrDefault();
        if (top is not Window window || view == null || Avalonia.VisualExtensions.TranslatePoint(view, new Avalonia.Point(0, 0), window) is not { } origin) { vm.AssistantNote("There is no 3D view to capture."); return; }
        double scale = window.RenderScaling;
        if (WindowCapture.CaptureThumbnail(window, (int)(origin.X * scale), (int)(origin.Y * scale), (int)(view.Bounds.Width * scale), (int)(view.Bounds.Height * scale), int.MaxValue) is not { } shot) { vm.AssistantNote("Could not capture the view."); return; }
        var err = vm.AddAssistantAttachment("view.png", Rendering.PngWriter.Encode(shot.pixels, shot.width, shot.height));
        if (err != null) vm.AssistantNote(err);
    }

    static byte[] Encode(Avalonia.Media.Imaging.Bitmap bmp) { using var ms = new MemoryStream(); bmp.Save(ms); return ms.ToArray(); }

    async void OnInputKey(object? s, KeyEventArgs e)
    {
        if (Vm is not { } vm) return;
        if (e.Key == Key.Enter) { e.Handled = true; await vm.SendAssistant(); return; }
        if (e.Key == Key.V && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            try
            {
                var cb = TopLevel.GetTopLevel(this)?.Clipboard;
                var data = cb == null ? null : await cb.GetDataAsync("Bitmap") ?? await cb.GetDataAsync("PNG");
                byte[]? png = data switch { Avalonia.Media.Imaging.Bitmap bmp => Encode(bmp), byte[] raw => raw, _ => null };
                if (png != null)
                {
                    var err = vm.AddAssistantAttachment("pasted.png", png);
                    if (err != null) vm.AssistantNote(err);
                    e.Handled = true;
                }
            }
            catch { }
        }
    }
}
