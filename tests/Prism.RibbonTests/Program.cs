using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Prism.Ribbon;
using Prism.ViewModels;

AppBuilder.Configure<TestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia().SetupWithoutStarting();
int checks = 0;
void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
var vm = new MainViewModel();
var solidTab=vm.Tabs.Single(t=>t.Title=="Solid");
var combine=solidTab.Groups.Single(g=>g.Title=="Combine").Items.OfType<RibbonButton>().Single();
Check(combine.Menu.Select(b=>b.Label).SequenceEqual(new[]{"Union","Subtract","Intersect"}),"Solid combines boolean commands into one menu");
var patterns=solidTab.Groups.Single(g=>g.Title=="Pattern").Items.OfType<RibbonButton>().ToList();
Check(patterns.Count==3&&patterns[2].Menu.Count==3,"Solid exposes linear, circular and a menu of three other pattern methods");
Check(solidTab.Buttons.Any(b=>b.Label=="Sweep")&&solidTab.Buttons.Any(b=>b.Label=="Loft"),"Solid retains sweep and loft creation");
Check(vm.Tabs.Any(t=>t.Title=="Direct Modeling")&&MainViewModel.WorkspaceDefs.Single(w=>w.Id=="design").Tabs.Contains("Direct Modeling"),"Direct Modeling is available in the Design workspace");
Check(vm.Tabs.Any(t => t.Title == "CAM") && vm.Tabs.All(t => t.Title is not ("CNC" or "3D Print")), "CNC and print tools share one CAM tab");
var cam = vm.Tabs.Single(t => t.Title == "CAM");
foreach (var name in new[] { "Profile", "Pocket", "Drill", "Face", "Post G-code", "CAM setup", "Playback", "Setup sheet" })
    Check(cam.Buttons.Any(b => b.Label.Replace("\n", " ") == name), "CAM retains " + name);
Check(MainViewModel.WorkspaceDefs.Single(w=>w.Id=="make").Name=="CAM","CAM has a dedicated named workspace");
var additive=vm.Tabs.Single(t=>t.Title=="Additive");
Check(additive.Buttons.Any(b=>b.Label=="Import nozzle path")&&additive.Buttons.All(b=>!b.Label.Contains("slice")),"additive workflow imports external slicer paths without promoting built-in slicing");
foreach (var title in new[] { "Surface", "NURBS", "Freeform" })
{
    var tab = vm.Tabs.Single(t => t.Title == title);
    Check(tab.Groups.SelectMany(g => g.Items).OfType<RibbonButton>().Any(b => b.HasMenu), title + " groups related tools");
    Check(tab.Buttons.Where(b => b.Menu == null).All(b => b.Id.Length > 0), title + " nested tools retain command identities");
}
var output = Path.GetFullPath("docs/surfacing-enhancements"); Directory.CreateDirectory(output);
foreach (var title in new[] { "Solid", "Direct Modeling", "NURBS", "Freeform", "CAM", "Additive", "Tooling", "ECAD", "Thermal", "Dynamics", "Fluids" })
{
    var tab = vm.Tabs.Single(t => t.Title == title); tab.IsVisible = true; vm.SelectTab(tab);
    var view = new RibbonView { DataContext = vm };
    var window = new Window { Width = 1024, Height = 200, Content = view }; window.Show();
    Dispatcher.UIThread.RunJobs();
    using (var shot = window.CaptureRenderedFrame()) shot?.Save(Path.Combine(output, "ribbon-" + title.ToLowerInvariant() + "-1024.png"));
    var scroll = view.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "GroupScroll");
    Check(scroll.Viewport.Width > 0 && scroll.Bounds.Right <= 1024, title + " ribbon stays within a 1024px window");
    Console.WriteLine($"WIDTH {title}: tools={scroll.Extent.Width:0}, viewport={scroll.Viewport.Width:0}");
    if (scroll.Extent.Width > scroll.Viewport.Width + 1)
    {
        var right = view.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ScrollRight");
        Check(right.IsVisible, title + " shows an overflow arrow");
        for (int i = 0; i < 5; i++) { right.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
        Check(Math.Abs(scroll.Offset.X - (scroll.Extent.Width - scroll.Viewport.Width)) < 1, title + " overflow arrow reaches the final tools");
    }
    window.Close();
}
var ecad = vm.Tabs.Single(t => t.Title == "ECAD");
var cae=MainViewModel.WorkspaceDefs.Single(w=>w.Id=="simulate");
Check(cae.Name=="CAE"&&new[]{"Static","Thermal","Dynamics","Fluids"}.All(cae.Tabs.Contains),"CAE workspace includes the four analysis areas");
foreach (var label in new[] { "Rectangular board", "Board from outline", "Mounting Hole", "Component Envelope", "Interference", "Import STEP", "Export STEP" })
    Check(ecad.Buttons.Any(b => b.Label.Replace("\n", " ") == label), "ECAD exposes " + label);
// A menu click must open its choices without executing a modeling command first.
int invoked = 0;
var variant = new RibbonButton { Label = "Variant", Execute = () => invoked++ };
var split = new RibbonButton { Label = "Split", Menu = [variant], IsSplit = true, Current = variant };
var probe = new RibbonTab { Title = "Probe", Groups = [new RibbonGroup { Title = "Probe", Items = [split] }] };
vm.Tabs.Add(probe); vm.SelectTab(probe);
var ribbon = new RibbonView { DataContext = vm }; var host = new Window { Width = 600, Height = 200, Content = ribbon }; host.Show(); Dispatcher.UIThread.RunJobs();
var button = ribbon.GetVisualDescendants().OfType<Button>().Single(b => ReferenceEquals(b.DataContext, split));
button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
Check(invoked == 0, "opening a split menu does not execute its current tool");
host.Close();
Console.WriteLine($"SUCCESS: {checks} checks");

class TestApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://Prism/")) { Source = new Uri("avares://Prism/Styles/Theme.axaml") });
        RequestedThemeVariant = ThemeVariant.Dark;
    }
}
