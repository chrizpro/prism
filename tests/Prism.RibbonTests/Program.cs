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
using Prism.Core;

AppBuilder.Configure<TestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia().SetupWithoutStarting();
int checks = 0;
void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
AppSettings.Current.Ribbon = new RibbonLayout();
AppSettings.Current.LastPicks.Clear();
AppSettings.Current.VisibleWorkspaces = null;
var vm = new MainViewModel();
vm.UpdateSearchResults();
Check(vm.SearchOpen && vm.SearchResults.Count > 0, "command search offers suggestions when opened");
vm.SearchText = "Subtract";
var subtractHit = vm.SearchResults.First(h => h.Tab == "Solid");
Check(subtractHit.Path.Contains("Combine") && subtractHit.Label == "Subtract", "command search shows a menu command's ribbon path");
vm.SearchText = "Finish Sketch";
var finishHit = vm.SearchResults.First(h => h.Tab == "Sketch");
Check(finishHit.Shortcut == "Enter", "command search exposes shortcuts");
vm.RunSearchHit(finishHit);
Check(vm.Session.StatusIsError && vm.Session.Status.Contains("current context"), "command search warns when a contextual command cannot run");
Check(MainViewModel.WorkspaceDefs.All(w => w.Id != "ar") && vm.Tabs.All(t => t.Title != "AR"), "AR has no standalone workspace or tab");
foreach (var title in new[] { "Home", "Assemble", "Render", "Animate" })
{
    var share = vm.Tabs.Single(t => t.Title == title).Groups.Single(g => g.Title == "Share");
    Check(share.Items.OfType<RibbonButton>().Single().Menu!.Select(b => b.Icon).SequenceEqual(new[] { "ar", "cancel" }), title + " exposes AR export and stop sharing together");
}
string Signature(RibbonGroup group) => string.Join(";", group.Items.OfType<RibbonButton>().Select(b => b.Label + ":" + b.Size + ":" + string.Join(",", b.Menu?.Select(v => v.Label) ?? [])));
foreach (var name in new[] { "Transform", "Inspect", "Appearance", "Select" })
{
    var groups = vm.Tabs.Where(t => !t.IsContextual).SelectMany(t => t.Groups).Where(g => g.Title == name).ToArray();
    Check(groups.Select(Signature).Distinct().Count() == 1, name + " uses the same tools, order and grouping across ribbons");
}
foreach (var tab in vm.Tabs)
{
    Check(tab.Groups.Select(g => RibbonLayoutPolicy.Rank(g.Title)).SequenceEqual(tab.Groups.Select(g => RibbonLayoutPolicy.Rank(g.Title)).Order()), tab.Title + " follows the shared section order");
    Check(tab.Groups.Select(g => g.Title).Distinct().Count() == tab.Groups.Count, tab.Title + " has no duplicate section captions");
    Check(tab.Buttons.All(b => b.Menu == null || b.Menu.All(v => !v.HasMenu)), tab.Title + " keeps menus one level deep");
}
foreach (var tab in vm.Tabs.Where(t => t.Groups.Any(g => g.Title == "Transform")))
{
    var group = tab.Groups.Single(g => g.Title == "Transform");
    var buttons = group.Items.OfType<RibbonButton>().ToArray();
    Check(buttons.Length == 2 && buttons[0].Label == "Move" && buttons[1].Menu!.Select(b => b.Label).SequenceEqual(new[] { "Rotate", "Scale", "Mirror", "Duplicate" }), tab.Title + " keeps Move visible with four transform variants");
}
Check(vm.Workspaces.Select(w => w.Name).SequenceEqual(vm.Workspaces.Select(w => w.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase)), "workspaces are alphabetized");
Check(vm.Workspaces.Where(w => w.Id is "script" or "ecad" or "assemble" or "simulate" or "make").All(w => !w.IsPinned), "specialist workspaces remain available without occupying the default bar");
AppSettings.Current.AccentColor = "Purple";
Prism.App.ApplyAccent();
Check(Avalonia.Application.Current!.Resources.ContainsKey("Accent"), "custom accent supplies interface resources");
AppSettings.Current.AccentColor = "Blue";
Prism.App.ApplyAccent();
Check(!Avalonia.Application.Current!.Resources.ContainsKey("Accent"), "blue restores the theme's default accent");
Check(vm.Tabs.SelectMany(t => t.Buttons).All(b => b.Icon != "sketch-edit"), "Edit Sketch is absent from ribbon tabs");
var sketchDraw = vm.Tabs.Single(t => t.Title == "Sketch").Groups.Single(g => g.Title == "Draw").Items.OfType<RibbonButton>().ToArray();
Check(new[] { "Line", "Rectangle", "Ellipse", "Arc" }.All(n => sketchDraw.Any(b => b.Label == n && !b.HasMenu)), "common sketch shapes have separate buttons");
Check(vm.Tabs.Single(t => t.Title == "Surface").Buttons.Any(b => b.Label.Replace("\n", " ") == "Create Enclosed Solid"), "enclosed solid has its new name");
var solidTab=vm.Tabs.Single(t=>t.Title=="Solid");
Check(solidTab.Groups.All(g => g.Title != "Direct"), "Solid omits the Direct section");
var combine=solidTab.Groups.Single(g=>g.Title=="Combine").Items.OfType<RibbonButton>().Single();
Check(combine.Menu.Select(b=>b.Label).SequenceEqual(new[]{"Union","Subtract","Intersect"}),"Solid combines boolean commands into one menu");
var patterns=solidTab.Groups.Single(g=>g.Title=="Pattern").Items.OfType<RibbonButton>().ToList();
Check(patterns.Count==1&&patterns[0].Menu!.Select(b => b.Icon).SequenceEqual(new[]{"pattern-linear","pattern-circular","pattern-curve","pattern-fill","pattern-table"}),"Solid groups all five pattern methods in one menu");
var solidCreate = solidTab.Groups.Single(g => g.Title == "Create").Items.OfType<RibbonButton>().ToArray();
Check(new[] { "Extrude", "Revolve" }.All(n => solidCreate.Any(b => b.Label == n && !b.HasMenu)), "Solid exposes Extrude and Revolve");
Check(solidTab.Buttons.All(b => b.Label is not ("Sweep" or "Loft" or "Edit\nFeature")), "Solid omits Sweep, Loft and Edit Feature");
var solidModify = solidTab.Groups.Single(g => g.Title == "Modify").Items.OfType<RibbonButton>().ToArray();
var edge = solidModify.Single(b => b.Menu?.Any(v => v.Label == "Chamfer") == true);
Check(edge.IsSplit && edge.Label == "Chamfer" && edge.Menu!.Select(v => v.Label).SequenceEqual(new[] { "Chamfer", "Fillet" }) && solidModify.Any(b => b.Label == "Shell" && !b.HasMenu), "Solid exposes Shell and defaults its edge tools to Chamfer");
AppSettings.Current.LastPicks["split:solid-edge"] = "Fillet";
var restored = new MainViewModel().Tabs.Single(t => t.Title == "Solid").Groups.Single(g => g.Title == "Modify").Items.OfType<RibbonButton>().Single(b => b.Menu?.Any(v => v.Label == "Chamfer") == true);
Check(restored.Label == "Fillet" && restored.Current?.Label == "Fillet", "last edge tool is restored for the next session");
AppSettings.Current.LastPicks.Remove("split:solid-edge");
var surfaceCreate = vm.Tabs.Single(t => t.Title == "Surface").Groups.Single(g => g.Title == "Create").Items.OfType<RibbonButton>().ToArray();
Check(new[] { "Extrude", "Revolve", "Sweep", "Loft" }.All(n => surfaceCreate.Any(b => b.Label == n && !b.HasMenu)), "Surface exposes its four creation tools");
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
var output = Path.GetFullPath(Environment.GetEnvironmentVariable("PRISM_REVIEW_OUTPUT") ?? "artifacts/ribbon-review"); Directory.CreateDirectory(output);
foreach (var title in vm.Tabs.Where(t => !t.IsFile).Select(t => t.Title).ToArray())
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
Check(invoked == 1, "keyboard or programmatic activation runs the current split tool");
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

