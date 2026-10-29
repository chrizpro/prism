using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Prism.Core;
using Prism.Kernel;
using Prism.ViewModels;
using Prism.Views;

NativeSearch.Initialize();
AppBuilder.Configure<TestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia().SetupWithoutStarting();
int checks = 0;
void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
var vm = new MainViewModel(); var s = vm.Session;
var a = s.AddObject(Occ.Box(20, 20, 10), "Bracket", ObjKind.Solid);
var b = s.AddObject(Occ.Sphere(4), "Pin", ObjKind.Solid);
var material = Materials.Find("6061")!;
s.SelectItems([SelItem.Of(a), SelItem.Of(b)]); vm.ApplyMaterial(material);
var panel = new MaterialsPanel { DataContext = vm };
var window = new Window { Width = 312, Height = 1000, Content = panel };
window.Show(); Dispatcher.UIThread.RunJobs();
Check(panel.FindControl<TabControl>("StudioTabs")!.Items.Count == 2, "appearance and existing material library share one panel");
var gallery = panel.FindControl<ListBox>("FinishLibrary")!;
Check(gallery.Items.Count == 6, "six procedural finish swatches are available");
foreach (var preset in vm.FinishPresets)
{
    var previous = s.Current;
    gallery.SelectedItem = preset; Dispatcher.UIThread.RunJobs();
    Check(s.Objects.All(o => o.Material == material && o.Look.PhysicalSnapshot == material), preset.Name + " preserves assigned engineering data");
    Check(s.Objects.All(o => o.Color == preset.Color && o.Look.Pattern == preset.Pattern && Math.Abs(o.Look.Gloss - preset.Gloss) < .001), preset.Name + " updates both selected bodies");
    s.Undo(); Check(s.Current == previous, "one undo restores the entire multi-body finish change");
}
vm.ApplyFinishPreset(vm.FinishPresets[3]); vm.ResetAppearance();
Check(s.Objects.All(o => o.Material == material && o.Color == material.Color && o.Look.Gloss == material.Gloss), "reset restores the assigned material appearance");
s.SelectItems([]); Dispatcher.UIThread.RunJobs();
Check(!gallery.IsEnabled && !panel.FindControl<Button>("ResetFinish")!.IsEnabled, "appearance controls disable with no selection");
s.SelectItems([SelItem.Of(s.Find(a.Id)!)]); vm.ApplyFinishPreset(vm.FinishPresets[0]); Dispatcher.UIThread.RunJobs();
Check(Equals(gallery.SelectedItem, vm.FinishPresets[0]), "selected swatch tracks the current body finish");
var change = panel.GetVisualDescendants().OfType<Button>().Single(c => Equals(c.Content, "Change material…"));
change.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
Check(panel.FindControl<TabControl>("StudioTabs")!.SelectedIndex == 1, "Change material opens the existing library");
panel.FindControl<TabControl>("StudioTabs")!.SelectedIndex = 0; Dispatcher.UIThread.RunJobs();
var output = Path.GetFullPath("docs/appearance-studio"); Directory.CreateDirectory(output);
foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
{
    Application.Current!.RequestedThemeVariant = theme; Dispatcher.UIThread.RunJobs();
    Check(panel.Bounds.Width == 312 && gallery.Bounds.Width <= 312, theme.Key + " panel fits the existing 312px sidebar");
    using var bitmap = new RenderTargetBitmap(new PixelSize(312, 1000)); bitmap.Render(panel);
    bitmap.Save(Path.Combine(output, $"appearance-{theme.Key.ToString()!.ToLowerInvariant()}.png"));
}
window.Close(); Console.WriteLine($"SUCCESS: {checks} checks");
sealed class TestApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://Prism/")) { Source = new Uri("avares://Prism/Styles/Theme.axaml") });
    }
}
static class NativeSearch
{
    [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern bool SetDllDirectory(string path);
    public static void Initialize() => SetDllDirectory(Path.GetFullPath("native/bin"));
}
