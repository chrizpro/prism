using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Prism.Commands;
using Prism.Controls;
using Prism.Core;
using Prism.Core.Sketching;
using Prism.Kernel;
using Prism.Ribbon;
using Prism.ViewModels;
using Prism.Views;

NativeSearch.Initialize();
Environment.SetEnvironmentVariable("PRISM_NO_HOME", "1");
AppBuilder.Configure<TestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia().SetupWithoutStarting();
int checks = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS " + message); checks++; }
var orbitCamera = new Camera { Yaw = 0, Pitch = 0, Roll = 0 };
orbitCamera.OrbitFree(0, 300);
Check(orbitCamera.EyeDirection.Z > 0.8 && orbitCamera.Up.Z < 0, "cube free orbit crosses the top pole without stopping");
Check(Math.Abs(Vec3.Dot(orbitCamera.EyeDirection, orbitCamera.Up)) < 1e-9, "cube orbit keeps the camera basis perpendicular");
orbitCamera.OrbitFree(0, -300);
Check(orbitCamera.EyeDirection.DistanceTo(new Vec3(0, -1, 0)) < 1e-8 && orbitCamera.Up.DistanceTo(Vec3.UnitZ) < 1e-8, "reversing a cube drag restores its view");
string output = Path.GetFullPath("docs/panel-workflows"); Directory.CreateDirectory(output);
void Capture(Control control, string name)
{
    var deadline = DateTime.UtcNow.AddSeconds(10);
    do { Dispatcher.UIThread.RunJobs(); if (control.GetVisualDescendants().OfType<GeometryPreview>().All(p => p.PendingRender.IsCompleted)) break; Thread.Sleep(10); } while (DateTime.UtcNow < deadline);
    Dispatcher.UIThread.RunJobs();
    using var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(control.Bounds.Width), (int)Math.Ceiling(control.Bounds.Height)));
    bitmap.Render(control); bitmap.Save(Path.Combine(output, name + ".png"));
    Console.WriteLine("SCREENSHOT " + name);
}
void PanelShot(Control control, string name, int height = 960)
{
    var host = new Window { Width = 394, Height = height, Content = control }; host.Show(); Dispatcher.UIThread.RunJobs();
    foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
    {
        Application.Current!.RequestedThemeVariant = theme; Dispatcher.UIThread.RunJobs();
        Capture(control, name + "-" + theme.Key.ToString()!.ToLowerInvariant());
    }
    host.Close(); Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
}
var vm = new MainViewModel(); var s = vm.Session;
var model = new SketchModel();
var points = new[] { (10.0, 0.0), (50.0, 0.0), (50.0, 30.0), (10.0, 30.0) }.Select(p => model.AddPoint(p.Item1, p.Item2)).ToArray();
for (int i = 0; i < 4; i++) model.AddLine(points[i], points[(i + 1) % 4]);
s.EnterSketch(Plane3.XY, model); s.ExitSketch(true);
var sketch = s.Objects.Single(); s.Update(sketch.Id, o => o.WithName("Mounting profile")); sketch = s.Find(sketch.Id)!;
SelItem Edge(int index) => new(sketch.Id, SelKind.Edge, index);
Check(SelectionChains.Expand(s, Edge(0), false).Count == 4, "connected chain includes all four rectangle edges");
Check(SelectionChains.Expand(s, Edge(0), true).Count == 1, "tangent chain stops at rectangle corners");
s.ClearSelection();
s.StartCommand(() => new ExtrudeCommand(true));
var surface = s.ActiveCommand!; var profile = surface.Slots[0]; profile.SelectionRuleIndex = 1;
for (int i = 0; i < 3; i++) Check(surface.Pick(Edge(i), true), "single edge is accepted for surface extrusion");
var surfaceOutput = surface.TryBuild();
Check(surfaceOutput?.Add.Single().Shape?.Count(SubKind.Face) == 3, "three selected sides extrude as three surface faces");
Check(s.ActiveFilter == SelFilter.Edge, "selection intent changes the viewport pick filter to edges");
var surfaceRecipe = surface.Capture()!;
var replay = new ExtrudeCommand(true); replay.Begin(s); replay.Restore(surfaceRecipe);
Check(replay.Slots[0].SelectionRuleIndex == 1 && replay.TryBuild()?.Add.Single().Shape?.Count(SubKind.Face) == 3, "feature replay preserves the selected three edges and restores edge-picking intent");
PanelShot(new CommandPanel { DataContext = vm }, "extrude-three-edges");
profile.SelectionRuleIndex = 4;
var first = Occ.EdgeInfoOf(sketch.Shape.Sub(SubKind.Edge, 0));
int opposite = Enumerable.Range(1, 3).First(i => { var e = Occ.EdgeInfoOf(sketch.Shape.Sub(SubKind.Edge, i)); return (e.Start - first.Start).Length > 1e-5 && (e.Start - first.End).Length > 1e-5 && (e.End - first.Start).Length > 1e-5 && (e.End - first.End).Length > 1e-5; });
surface.Pick(Edge(0), true); surface.Pick(Edge(opposite), true);
Check(profile.Items.Count == 3, "partial loop picks exactly three sides between opposite anchor edges");
var firstPath = profile.Items.ToArray(); profile.OtherPath = true;
Check(profile.Items.Count == 3 && !profile.Items.SequenceEqual(firstPath), "Other path selects the alternative three-sided chain");
PanelShot(new CommandPanel { DataContext = vm }, "partial-loop-selection", 780);
s.CancelCommand();
s.StartCommand(() => new ExtrudeCommand()); var extrude = s.ActiveCommand!;
extrude.Slots[0].SelectionRuleIndex = 1; for (int i = 0; i < 3; i++) extrude.Pick(Edge(i), true);
Check(extrude.TryBuild() == null && extrude.Error != null, "an open three-sided chain cannot silently create a solid");
extrude.Pick(Edge(3), true);
Check(extrude.TryBuild()?.Add.Single().Shape?.Count(SubKind.Solid) > 0, "adding the fourth edge creates a closed solid profile");
s.CommitCommand();
var body = s.Objects.Single(o => o.Kind == ObjKind.Solid); s.Update(body.Id, o => o.WithName("Bracket body"));
vm.RebuildBrowser();
var bodyNode = vm.AllBrowserNodes().Single(n => n.ObjectId == body.Id && n.IsObject);
Check(bodyNode.Children.Any(n => n.Title == "Sketches" && n.Children.Any(c => c.ObjectId == sketch.Id)), "source sketch is nested under its real owning body");
bodyNode.IsExpanded = false; vm.RebuildBrowser();
Check(!vm.AllBrowserNodes().Single(n => n.ObjectId == body.Id && n.IsObject).IsExpanded, "body disclosure survives browser rebuilds");
vm.BrowserFilter = "Mounting profile";
Check(vm.AllBrowserNodes().Any(n => n.ObjectId == body.Id), "filtering a nested sketch retains the ancestor body");
vm.BrowserFilter = ""; bodyNode = vm.AllBrowserNodes().Single(n => n.ObjectId == body.Id && n.IsObject); bodyNode.IsExpanded = true;
s.SelectItems([SelItem.Of(sketch)]); s.StartCommand(() => new RevolveCommand());
var revolve = s.ActiveCommand!;
Check(revolve.WorkflowSections.Where(x => x.IsVisible).Select(x => x.Title).SequenceEqual(new[] { "Profile", "Axis", "Extent", "Result" }), "revolve reads Profile, Axis, Extent, Result from top to bottom");
revolve.Params.OfType<ChoiceParam>().Single(p => p.Label == "Axis").Index = 3;
var axisStep = revolve.WorkflowSections.Single(g => g.Title == "Axis");
Check(axisStep.Items.OfType<SelectionSlot>().Single().Label == "Axis line", "picked axis stays inside step two after the axis-type selector");
revolve.Pick(new SelItem(Guid.Empty, SelKind.Origin, 4), false);
var commandPanel = new CommandPanel { DataContext = vm };
var commandHost = new Window { Width = 394, Height = 960, Content = commandPanel }; commandHost.Show(); Dispatcher.UIThread.RunJobs();
var profileExpander = commandPanel.GetVisualDescendants().OfType<Expander>().Single(e => e.DataContext is CommandSection { Title: "Profile" });
var disclosure = profileExpander.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>().First();
disclosure.IsChecked = false; Dispatcher.UIThread.RunJobs();
Check(!revolve.WorkflowSections.Single(g => g.Title == "Profile").IsExpanded && revolve.Slots[0].Items.Count == 1, "collapsing a command step preserves the selected profile");
disclosure.IsChecked = true; Dispatcher.UIThread.RunJobs();
Capture(commandPanel, "revolve-steps-dark");
Application.Current!.RequestedThemeVariant = ThemeVariant.Light; Dispatcher.UIThread.RunJobs(); Capture(commandPanel, "revolve-steps-light");
commandHost.Close(); Application.Current.RequestedThemeVariant = ThemeVariant.Dark;
s.CancelCommand();
s.SelectItems([SelItem.Of(sketch)]); s.StartCommand(() => new ExtrudeCommand());
PanelShot(new CommandPanel { DataContext = vm }, "extrude-steps");
var workspace = new MainView { DataContext = vm }; var workspaceHost = new Window { Width = 1360, Height = 940, Content = workspace };
workspaceHost.Show(); Dispatcher.UIThread.RunJobs();
var tree = workspace.FindControl<TreeView>("Tree")!; var treePanel = tree.GetVisualAncestors().OfType<Border>().First(b => b.Bounds.Width >= 270 && b.Bounds.Width < 300 && b.Background != null);
foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
{
    Application.Current!.RequestedThemeVariant = theme; Dispatcher.UIThread.RunJobs(); Capture(treePanel, "context-tree-" + theme.Key.ToString()!.ToLowerInvariant());
}
var lines = workspace.GetVisualDescendants().OfType<BrowserBranchLines>().Single();
var liveBodyNode = vm.AllBrowserNodes().Single(n => n.ObjectId == body.Id && n.IsObject);
liveBodyNode.IsExpanded = false; Dispatcher.UIThread.RunJobs();
Check(tree.GetVisualDescendants().OfType<TreeViewItem>().Where(row => row.DataContext is BrowserNode n && n.ObjectId == sketch.Id).All(row => !row.IsEffectivelyVisible), "native body disclosure hides all descendant sketch rows");
liveBodyNode.IsExpanded = true; Dispatcher.UIThread.RunJobs();
var originalAccent = AppSettings.Current.AccentColor;
AppSettings.Current.AccentColor = "Purple"; Prism.App.ApplyAccent(); Dispatcher.UIThread.RunJobs();
Check(lines.BranchBrush is SolidColorBrush brush && brush.Color == ((SolidColorBrush)Application.Current!.Resources["Accent"]!).Color, "branch lines follow the accent selected in Preferences");
Capture(treePanel, "context-tree-purple");
AppSettings.Current.AccentColor = originalAccent; Prism.App.ApplyAccent();
workspaceHost.Close(); s.CancelCommand();
s.SelectItems([SelItem.Of(s.Find(body.Id)!)]); vm.ApplyMaterial(Materials.Find("6061")!); vm.OpenStudio("appearance");
PanelShot(new MaterialsPanel { DataContext = vm }, "appearance");
PanelShot(new PropertiesPanel { DataContext = vm }, "properties", 760);
vm.NewStudy(); PanelShot(new SimulationPanel { DataContext = vm }, "simulation", 860);
vm.OpenRightPanel("library"); PanelShot(new LibraryPanel { DataContext = vm }, "parts-library");
s.ApplyDocumentParameters([new("width", "40 mm"), new("height", "30 mm"), new("wall", "3 mm", true), new("rib", "wall * 0.75")]);
foreach (string studio in new[] { "diagnostics", "references", "parameters" })
{
    vm.OpenStudio(studio); if (studio == "diagnostics") vm.RunStudioChecks();
    PanelShot(new StudiosPanel { DataContext = vm }, studio);
}
s.EditSketch(s.Find(sketch.Id)!); PanelShot(new SketchPanel { DataContext = vm }, "sketch", 860); s.ExitSketch(false);
// The right panel and the animation timeline must be reachable on a fresh document, with no joints.
{
    var animVm = new MainViewModel { HomeOpen = false };
    var animView = new MainView { DataContext = animVm }; var animHost = new Window { Width = 1360, Height = 940, Content = animView };
    animHost.Show(); Dispatcher.UIThread.RunJobs();
    var rightColumn = animView.FindControl<Grid>("WorkGrid")!.ColumnDefinitions[4];
    var propsPanel = animView.GetVisualDescendants().OfType<PropertiesPanel>().Single();
    Check(animVm.RightPanelTabs.Count > 0 && animVm.ShowProperties && propsPanel.IsEffectivelyVisible && propsPanel.Bounds.Width > 250 && rightColumn.ActualWidth > 250, "the right panel is shown with Properties on a fresh document");
    Check(animVm.Tabs.Single(t => t.Title == "Animate").IsVisible, "the Animate tab is shown in the default Design workspace");
    var editPlay = animVm.Tabs.Single(t => t.Title == "Animate").Buttons.Single(b => b.Label.StartsWith("Edit"));
    editPlay.Refresh();
    Check(editPlay.IsEnabled, "Edit / Play is available without joints (the exploded view can be animated)");
    var timelineDock = animView.FindControl<Border>("TimelineDock")!;
    Check(!timelineDock.IsEffectivelyVisible && !animVm.ShowAnimationTimeline, "the timeline stays out of the way until animation is opened");
    var openTimeline = propsPanel.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Open animation timeline"));
    openTimeline.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
    var timelineView = animView.GetVisualDescendants().OfType<AnimationTimelineView>().Single();
    Check(animVm.ShowAnimationTimeline && timelineDock.IsEffectivelyVisible && timelineView.Bounds.Width > 400 && timelineView.Bounds.Height > 20, "Open animation timeline from the right panel shows the timeline dock");
    var commandPanelView = animView.GetVisualDescendants().OfType<CommandPanel>().Single();
    Check(animVm.ShowCommandPanel && commandPanelView.IsEffectivelyVisible && rightColumn.ActualWidth > 300, "the right panel shows the animation options (length, time, loop, keys, play) while editing");
    foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
    {
        Application.Current!.RequestedThemeVariant = theme; Dispatcher.UIThread.RunJobs(); Capture(animView, "animation-workspace-" + theme.Key.ToString()!.ToLowerInvariant());
    }
    animVm.Session.CancelCommand(); Dispatcher.UIThread.RunJobs();
    Check(!animVm.ShowAnimationTimeline && propsPanel.IsEffectivelyVisible, "closing the animation returns the right panel to Properties");
    animVm.Session.AddAnimation(new Prism.Core.Assembly.AnimInfo(8, [], true), false); Dispatcher.UIThread.RunJobs();
    animVm.Session.SelectItems([SelItem.Of(animVm.Session.Objects.Single(o => o.Kind == ObjKind.Animation))]); Dispatcher.UIThread.RunJobs();
    Check(animVm.Properties.Any(p => p.Label == "Length" && p.Value == "8 s") && animVm.AnimationSummary.Contains("8 s"), "a selected animation lists its length, loop and keys in the right panel");
    animHost.Close(); Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
}
// Draft and Split Body are on the Solid tab; Hide / Isolate / Show All work from the View tab.
{
    var toolVm = new MainViewModel { HomeOpen = false };
    var solidLabels = toolVm.Tabs.Single(t => t.Title == "Solid").Buttons.Select(b => b.Label.Replace((char)10, (char)32)).ToList();
    Check(solidLabels.Contains("Draft") && solidLabels.Contains("Split Body"), "the Solid tab offers Draft and Split Body");
    var ts = toolVm.Session; var a = ts.AddAnimation(new Prism.Core.Assembly.AnimInfo(1, [], false), false); var b = ts.AddAnimation(new Prism.Core.Assembly.AnimInfo(2, [], false), false);
    var viewTab = toolVm.Tabs.Single(t => t.Title == "View");
    RibbonButton ViewBtn(string label) => viewTab.Buttons.First(x => x.Label == label);
    ts.SelectItems([SelItem.Of(ts.Find(a.Id)!)]); ViewBtn("Hide").Refresh(); ViewBtn("Hide").Invoke();
    Check(!ts.Find(a.Id)!.Visible && ts.Find(b.Id)!.Visible, "Hide hides only the selected object");
    ViewBtn("Show All").Refresh(); Check(ViewBtn("Show All").IsEnabled, "Show All is enabled while something is hidden");
    ViewBtn("Show All").Invoke(); Check(ts.Find(a.Id)!.Visible, "Show All brings hidden objects back");
    ts.SelectItems([SelItem.Of(ts.Find(b.Id)!)]); ViewBtn("Isolate").Invoke();
    Check(ts.Find(b.Id)!.Visible && !ts.Find(a.Id)!.Visible, "Isolate hides everything except the selection");
}
// Ball and Planar joints: two motion values each, limits respected, buttons on the Joints tab.
{
    var frame = Prism.Core.Assembly.JointInfo.Pack(Prism.Core.Assembly.Xform.Identity);
    Prism.Core.Assembly.JointInfo Joint(Prism.Core.Assembly.JointType t, bool limits = false) => new("k", "J", t, "a", "b", frame, frame, false, 0, 0, 0, limits, -10, 10, -20, 20);
    var planar = Joint(Prism.Core.Assembly.JointType.Planar); var planarMove = planar.Motion(3, 4);
    Check(planar.HasSecond && !planar.UsesAngle && Math.Abs(planarMove.T.X - 3) < 1e-9 && Math.Abs(planarMove.T.Y - 4) < 1e-9 && Math.Abs(planarMove.T.Z) < 1e-9, "a planar joint slides in two directions and stays flat");
    var ball = Joint(Prism.Core.Assembly.JointType.Ball); var tilt = ball.Motion(0, 90);
    Check(ball.HasSecond && ball.SecondIsAngle && Math.Abs(tilt.AxisZ.Y + 1) < 1e-9 && tilt.T.DistanceTo(Vec3.Zero) < 1e-9, "a ball joint tilts about a fixed point");
    Check(Joint(Prism.Core.Assembly.JointType.Planar, true).Clamped() == (0, 0) && Joint(Prism.Core.Assembly.JointType.Ball, true).Motion(50, 90).AxisZ.Y < -0.3, "limits apply to both values of a two-value joint");
    var jointLabels = new MainViewModel { HomeOpen = false }.Tabs.Single(t => t.Title == "Joints").Buttons.Select(b => b.Label).ToList();
    Check(jointLabels.Contains("Ball") && jointLabels.Contains("Planar"), "the Joints tab offers Ball and Planar joints");
}
// Sheet metal flange: an optional hem folds the end back over the metal.
{
    var hs = new CadSession();
    var hm = new SketchModel(); var hp = new[] { (0.0, 0.0), (40.0, 0.0), (40.0, 30.0) }.Select(q => hm.AddPoint(q.Item1, q.Item2)).ToArray();
    hm.AddLine(hp[0], hp[1]); hm.AddLine(hp[1], hp[2]);
    hs.EnterSketch(Plane3.XY, hm); hs.ExitSketch(true);
    var flangeProfile = hs.Objects.Single(o => o.Kind == ObjKind.Sketch);
    double FlangeVolume(double hem)
    {
        hs.SelectItems([SelItem.Of(flangeProfile)]); hs.StartCommand(() => new ContourFlangeCommand());
        var cmd = hs.ActiveCommand!; cmd.Pick(SelItem.Of(flangeProfile), false);
        cmd.Params.OfType<NumberParam>().Single(p => p.Label.StartsWith("Hem length")).Value = hem;
        var built = cmd.TryBuild(); Check(built != null && built.Add.Count == 1 && built.Add[0].Shape!.IsValid, hem > 0 ? "a flange with a hem builds a valid solid" : "a plain flange builds a valid solid");
        double v = built!.Add[0].Shape!.Props().Volume; hs.CancelCommand(); return v;
    }
    double plain = FlangeVolume(0), hemmed = FlangeVolume(8);
    Check(hemmed - plain > 600 && hemmed - plain < 800, "the hem adds a half-turn bend and an 8 mm return layer of metal");
}
Console.WriteLine($"SUCCESS: {checks} checks; screenshots in {output}");
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

