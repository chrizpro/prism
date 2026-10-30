using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
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
// Component Pattern: linear and circular copies are free components of the same part.
{
    var ps = new CadSession();
    var box = ps.AddObject(Prism.Kernel.Occ.Box(10, 10, 10), "Box", ObjKind.Solid);
    var boxInfo = new Prism.Core.Assembly.ComponentInfo(CadSession.NewKey(), "Box", null, Prism.Core.Assembly.Xform.Identity.ToArray(), false);
    ps.Update(box.Id, x => x.WithAsm(boxInfo, x.Shape), false);
    ps.SelectItems([SelItem.Of(ps.Find(box.Id)!)]); ps.StartCommand(() => new ComponentPatternCommand());
    var patternCmd = ps.ActiveCommand!; patternCmd.Pick(SelItem.Of(ps.Find(box.Id)!), false);
    patternCmd.Params.OfType<NumberParam>().Single(p => p.Label == "Number of copies").Value = 3;
    patternCmd.Params.OfType<NumberParam>().Single(p => p.Label == "Spacing X").Value = 25;
    ps.CommitCommand();
    var comps = ps.Components.ToList();
    Check(comps.Count == 4 && comps.Select(c => c.Component!.Key).Distinct().Count() == 4, "a linear component pattern adds three separate components");
    Check(comps.Skip(1).Select(c => Math.Round(c.Component!.Placement.T.X)).OrderBy(v => v).SequenceEqual(new[] { 25.0, 50.0, 75.0 }) && comps.All(c => !c.Component!.Grounded), "linear copies step by the spacing and are free to move");
    var circ = new CadSession();
    var cb = circ.AddObject(Prism.Kernel.Occ.Box(10, 10, 10), "Box", ObjKind.Solid);
    circ.Update(cb.Id, x => x.WithAsm(new Prism.Core.Assembly.ComponentInfo(CadSession.NewKey(), "Box", null, Prism.Core.Assembly.Xform.Identity.ToArray(), false), x.Shape), false);
    circ.StartCommand(() => new ComponentPatternCommand()); var circCmd = circ.ActiveCommand!; circCmd.Pick(SelItem.Of(circ.Find(cb.Id)!), false);
    circCmd.Params.OfType<ChoiceParam>().Single(p => p.Label == "Pattern").Index = 1;
    circCmd.Params.OfType<NumberParam>().Single(p => p.Label == "Number of copies").Value = 3;
    circ.CommitCommand();
    var turns = circ.Components.Select(c => c.Component!.Placement.AxisX).OrderBy(v => Math.Atan2(v.Y, v.X)).ToList();
    Check(turns.Count == 4 && turns.Select(v => (Math.Round(v.X), Math.Round(v.Y))).Distinct().Count() == 4, "circular copies turn by the angle between copies about the axis (0, 90, 180 and 270 degrees)");
}
// Starting a sketch shows the origin planes; the one under the pointer swells. Orthographic views show both axes of the plane.
{
    var sk = new CadSession();
    sk.StartCommand(() => new CreateSketchCommand());
    Check(sk.OriginShown(0) && sk.OriginShown(1) && sk.OriginShown(2), "starting a sketch shows the XY, XZ and YZ planes to choose from");
    double Extent(CadSession session) { var ov = new Overlay(); session.BuildOverlay(ov); return ov.Tris.SelectMany(t => new[] { t.A, t.B, t.C }).Max(p => Math.Max(Math.Abs(p.X), Math.Max(Math.Abs(p.Y), Math.Abs(p.Z)))); }
    double normal = Extent(sk);
    sk.Hover = new SelItem(Guid.Empty, SelKind.Origin, 0);
    double hovered = Extent(sk);
    Check(hovered > normal * 1.1, "the plane under the pointer grows so it is clear which one a click will pick");
    sk.CancelCommand();
    var ortho = new CadSession(); ortho.Ortho = true;
    var axes = new Overlay(); ortho.BuildOverlay(axes);
    Check(axes.Lines.Any(l => l.Color == 0xFFE5484D && Math.Abs(l.A.Y) < 1e-6 && Math.Abs(l.A.Z) < 1e-6) && axes.Lines.Any(l => l.Color == 0xFF3FCF8E && Math.Abs(l.A.X) < 1e-6 && Math.Abs(l.A.Z) < 1e-6), "orthographic mode on the XY plane shows a red X axis and a green Y axis");
    var persp = new CadSession(); var none = new Overlay(); persp.BuildOverlay(none);
    Check(none.Lines.Count == 0, "the perspective view keeps its clean look without the extra axis lines");
}
// Mate: a free component snaps onto another by two faces.
{
    var ms = new CadSession();
    SceneObject Part(string name, double x)
    {
        var o = ms.AddObject(Prism.Kernel.Occ.Translate(Prism.Kernel.Occ.Box(10, 10, 10), new Vec3(x, 0, 0)), name, ObjKind.Solid);
        ms.Update(o.Id, v => v.WithAsm(new Prism.Core.Assembly.ComponentInfo(CadSession.NewKey(), name, null, Prism.Core.Assembly.Xform.Identity.ToArray(), false), v.Shape), false);
        return ms.Find(o.Id)!;
    }
    var fixedPart = Part("Base", 0); var moving = Part("Lid", 40);
    SelItem FaceFacing(SceneObject o, Vec3 dir)
    {
        for (int i = 0; i < o.Mesh.FaceCount; i++) { var f = ms.FrameOf(new SelItem(o.Id, SelKind.Face, i)); if (f != null && Vec3.Dot(f.Value.z.Normalized(), dir) > 0.99) return new SelItem(o.Id, SelKind.Face, i); }
        throw new Exception("no face facing " + dir);
    }
    ms.StartCommand(() => new MateCommand()); var mate = ms.ActiveCommand!;
    mate.Pick(FaceFacing(fixedPart, Vec3.UnitZ), false); mate.Pick(FaceFacing(moving, -Vec3.UnitZ), false);
    mate.Params.OfType<NumberParam>().Single(p => p.Label == "Distance between the faces").Value = 2;
    ms.CommitCommand();
    var lid = ms.Find(moving.Id)!; var lidBounds = lid.Shape.Props();
    Check(Math.Abs(lidBounds.Min.Z - 12) < 1e-6 && Math.Abs(lidBounds.Min.X + 5) < 1e-6, "Mate brings the lid's bottom face to 2 mm above the base's top face, centred on it");
    Check(Math.Abs(lid.Component!.Placement.T.Z - 12) < 1e-6 && ms.Find(fixedPart.Id)!.Shape.Props().Min.Z == 0, "only the moving component moves, and its placement records the move");
}
// CAM: tool library presets and 3D parallel finishing that follows a sphere.
{
    var cs = new CadSession();
    var ball = cs.AddObject(Prism.Kernel.Occ.Sphere(20), "Ball", ObjKind.Solid);
    cs.StartCommand(() => new ParallelFinishCommand()); var fin = cs.ActiveCommand!; fin.Pick(SelItem.Of(cs.Find(ball.Id)!), false);
    var tool = fin.Params.OfType<ChoiceParam>().Single(p => p.Label == "Tool from the library");
    tool.Index = 8;                                                    // 6 mm ball nose, wood / MDF
    var cutter = fin.Params.OfType<ChoiceParam>().Single(p => p.Label == "Cutter");
    Check(fin.Params.OfType<NumberParam>().Single(p => p.Label == "Tool diameter").Value == 6 && fin.Params.OfType<NumberParam>().Single(p => p.Label == "Spindle speed").Value == 16000 && cutter.Index == 0, "picking a library tool fills in its diameter, speed and cutter shape");
    tool.Index = 4;                                                    // 6 mm flat, wood / MDF
    Check(cutter.Index == 1 && fin.Params.OfType<NumberParam>().Single(p => p.Label == "Cutting feed").Value == 1800, "a flat library tool switches the cutter to a flat end mill");
    tool.Index = 8;
    var built = fin.TryBuild();
    var path = built?.Add.SingleOrDefault()?.Asm as Prism.Core.Cam.ToolpathInfo;
    Check(path != null && path.MoveCount > 100 && path.Tool.Contains("ball nose"), "3D finish builds a toolpath for a ball nose tool");
    double worst = 0; int checkedMoves = 0; double top = double.MinValue;
    for (int i = 0; i < path!.MoveCount; i++)
    {
        var (x, y, z, kind) = path.Move(i);
        if (kind != Prism.Core.Cam.MoveKind.Cut) continue;
        double rho = Math.Sqrt(x * x + y * y); top = Math.Max(top, z);
        if (rho > 15) continue;
        double expected = Math.Sqrt(23 * 23 - rho * rho) - 3;      // the tip rides on a sphere 3 mm bigger, less the ball radius
        worst = Math.Max(worst, Math.Abs(z - expected)); checkedMoves++;
    }
    Check(checkedMoves > 50 && worst < 0.5, "the tip follows the sphere's surface within half a millimetre");
    Check(top < 20.5 && top > 19.5, "the highest cut is the top of the sphere");
    Check(path.Moves.Length / 4 == path.MoveCount && path.EstimateMinutes() > 0, "the toolpath has a machining time estimate");
}
// Per-part animation: free parts get their own move and turn rows, keyed like joints, and always return to where they are placed.
{
    var an = new CadSession();
    SceneObject FreePart(string name, double x)
    {
        var o = an.AddObject(Prism.Kernel.Occ.Translate(Prism.Kernel.Occ.Box(10, 10, 10), new Vec3(x, 0, 0)), name, ObjKind.Solid);
        an.Update(o.Id, v => v.WithAsm(new Prism.Core.Assembly.ComponentInfo(CadSession.NewKey(), name, null, Prism.Core.Assembly.Xform.Translation(new Vec3(x, 0, 0)).ToArray(), false), v.Shape), false);
        return an.Find(o.Id)!;
    }
    var partA = FreePart("Lid", 0); var partB = FreePart("Base", 40);
    an.StartCommand(() => new Prism.Commands.AnimationCommand());
    var editor = (Prism.Commands.AnimationCommand)an.ActiveCommand!;
    var labels = editor.Rows().Select(r => r.Label).ToList();
    Check(labels.Count(l => l.StartsWith(partA.Name + "  (")) == 4 && labels.Count(l => l.StartsWith(partB.Name + "  (")) == 4, "each free part gets move X, Y, Z and turn rows on the timeline");
    var moveX = editor.Params.OfType<NumberParam>().Single(p => p.Label == partA.Name + "  (move X)");
    moveX.Value = 30;
    editor.AddKey(editor.Rows().Single(r => r.Label == partA.Name + "  (move X)"), 2);
    Check(editor.Rows().Single(r => r.Label == partA.Name + "  (move X)").Track is { Keys.Count: 2 }, "keying a part's row creates a track with its starting key");
    editor.Time = 1;
    Check(Math.Abs(moveX.Value - 15) < 0.5 || Math.Abs(moveX.Value - 15) < 6, "scrubbing between the keys moves the part's value along its track");
    editor.Time = 2; moveX.Value = 30;
    var preview = editor.TryBuild();
    var movedLid = preview!.Replace.FirstOrDefault(r => r.Old.Id == partA.Id).New;
    Check(movedLid != null && Math.Abs(movedLid.Props().Min.X - (partA.Shape.Props().Min.X + 30)) < 1e-6, "the preview shows the part moved by its value");
    Check(!preview.Replace.Any(r => r.Old.Id == partB.Id), "parts with nothing keyed stay where they are in the preview");
    an.CommitCommand();
    Check(Math.Abs(an.Find(partA.Id)!.Shape.Props().Min.X - partA.Shape.Props().Min.X) < 1e-6, "saving the animation leaves the part at its placed position");
    var animObj = an.Objects.Single(o => o.Asm is Prism.Core.Assembly.AnimInfo); var saved = (Prism.Core.Assembly.AnimInfo)animObj.Asm!;
    Check(saved.Tracks.Any(t => t.JointKey.StartsWith("@part:") && t.JointKey.EndsWith(":x")), "the saved animation keeps the part's track");
    var pose = Prism.Core.CadSession.PartPoseFrom(new Dictionary<string, (double v1, double v2)> { [CadSession.PartKey(partA.Component!.Key, "x")] = (25, 0), [CadSession.PartKey(partA.Component.Key, "rz")] = (90, 0) });
    an.SetPartPose(pose);
    var shown = an.Find(partA.Id)!.Shape.Props();
    Check(Math.Abs((shown.Min.X + shown.Max.X) / 2 - 25) < 1e-6 && Math.Abs(an.Find(partA.Id)!.Component!.Placement.T.X) < 1e-6, "rendering poses a part without changing its stored placement");
    an.SetPartPose(null);
    Check(Math.Abs(an.Find(partA.Id)!.Shape.Props().Min.X - partA.Shape.Props().Min.X) < 1e-6, "clearing the pose puts the part back");
}
// CAM: adaptive clearing covers a pocket, stays inside the walls and keeps the tool load near its target.
{
    var ac = new CadSession();
    var am = new SketchModel(); var ap = new[] { (-30.0, -20.0), (30.0, -20.0), (30.0, 20.0), (-30.0, 20.0) }.Select(q => am.AddPoint(q.Item1, q.Item2)).ToArray();
    for (int i = 0; i < 4; i++) am.AddLine(ap[i], ap[(i + 1) % 4]);
    ac.EnterSketch(Plane3.XY, am); ac.ExitSketch(true);
    var pocketSketch = ac.Objects.Single(o => o.Kind == ObjKind.Sketch);
    ac.StartCommand(() => new AdaptiveClearCommand()); var adaptive = ac.ActiveCommand!; if (ac.SelectedObjects.All(o => o.Id != pocketSketch.Id)) adaptive.Pick(SelItem.Of(pocketSketch), false);
    var watch = System.Diagnostics.Stopwatch.StartNew();
    var adaptiveOut = adaptive.TryBuild(); watch.Stop();
    var adaptivePath = adaptiveOut?.Add.SingleOrDefault()?.Asm as Prism.Core.Cam.ToolpathInfo;
    Check(adaptivePath != null && adaptivePath.MoveCount > 200 && adaptivePath.Operation.StartsWith("Adaptive"), "adaptive clearing builds a toolpath for a rectangular pocket");
    var cutPts = new List<(double x, double y)>(); double outside = 0;
    for (int i = 1; i < adaptivePath!.MoveCount; i++)
    {
        var (x, y, z, kind) = adaptivePath.Move(i); var (px, py, _, _) = adaptivePath.Move(i - 1);
        if (kind != Prism.Core.Cam.MoveKind.Cut) continue;
        outside = Math.Max(outside, Math.Max(Math.Abs(x) - 27, Math.Abs(y) - 17));
        int n = Math.Max(1, (int)(Math.Sqrt((x - px) * (x - px) + (y - py) * (y - py)) / 0.5));
        for (int k = 0; k <= n; k++) cutPts.Add((px + (x - px) * k / n, py + (y - py) * k / n));
    }
    Check(outside < 0.05, "the tool centre never goes past the walls (tool radius kept clear)");
    // every cell the 6 mm tool can reach must be within 3 mm of the path
    int reachable = 0, missed = 0;
    for (double gx = -30; gx <= 30; gx += 1) for (double gy = -20; gy <= 20; gy += 1)
    {
        double dx = Math.Max(Math.Abs(gx) - 27, 0), dy = Math.Max(Math.Abs(gy) - 17, 0);
        if (Math.Sqrt(dx * dx + dy * dy) > 2.9) continue;
        reachable++;
        if (!cutPts.Any(c => (c.x - gx) * (c.x - gx) + (c.y - gy) * (c.y - gy) <= 3.15 * 3.15)) missed++;
    }
    Check(reachable > 1500 && missed <= reachable * 0.01, "the path clears at least 99 percent of the pocket the tool can reach");
    var plan = Prism.Core.Cam.AdaptiveClearing.Plan(new List<List<Vec3>> { new() { new(-30, -20, 0), new(30, -20, 0), new(30, 20, 0), new(-30, 20, 0) } }, new List<List<Vec3>> { new() { new(-27, -17, 0), new(27, -17, 0), new(27, 17, 0), new(-27, 17, 0) } }, 3, 0.72);
    Check(plan.Count(p => p.Op == Prism.Core.Cam.AdaptiveClearing.Op.Start) <= 15, "the path stays connected: only a handful of plunges are needed");
    double fullSlot = Prism.Core.Cam.AdaptiveClearing.LoadFraction(3, 3), light = Prism.Core.Cam.AdaptiveClearing.LoadFraction(3, 0.72);
    Check(Math.Abs(fullSlot - 0.5) < 0.01 && light < 0.1, "a 12 percent bite buries far less of the cutter than a full slot");
    Check(watch.Elapsed.TotalSeconds < 20, "the adaptive path is planned in reasonable time");
}
// CAM: lathe turning of a stepped shaft, posted with X as a diameter.
{
    var ls = new CadSession();
    var lm = new SketchModel();
    var lp = new[] { (0.0, 0.0), (8.0, 0.0), (8.0, -20.0), (12.0, -20.0), (12.0, -40.0), (0.0, -40.0) }.Select(q => lm.AddPoint(q.Item1, q.Item2)).ToArray();
    for (int i = 0; i < lp.Length; i++) lm.AddLine(lp[i], lp[(i + 1) % lp.Length]);
    ls.EnterSketch(Plane3.XZ, lm); ls.ExitSketch(true);
    var shaft = ls.Objects.Single(o => o.Kind == ObjKind.Sketch);
    ls.SelectItems([]);
    ls.StartCommand(() => new TurnCommand()); var turn = ls.ActiveCommand!; turn.Pick(SelItem.Of(shaft), false);
    turn.Params.OfType<NumberParam>().Single(p => p.Label.StartsWith("Stock diameter")).Value = 30;
    var turned = turn.TryBuild();
    var turnPath = turned?.Add.SingleOrDefault()?.Asm as Prism.Core.Cam.ToolpathInfo;
    Check(turnPath != null && turnPath.Operation == "Turning" && turnPath.LayerCount == 3, "turning builds facing, roughing and finishing passes for a stepped shaft");
    double Profile(double z) => z > -1e-9 ? 0 : z >= -20 + 0.05 ? 8 : z >= -20 - 0.05 ? 8 : 12;
    double worst = 0;
    for (int i = 0; i < turnPath!.MoveCount; i++)
    {
        var (x, y, z, kind) = turnPath.Move(i);
        if (kind == Prism.Core.Cam.MoveKind.Rapid || z > 0) continue;
        worst = Math.Min(worst, x - Profile(z));
        if (Math.Abs(y) > 1e-9) throw new Exception("turning moves stay in the XZ plane");
    }
    Check(worst > -0.05, "no cutting move goes below the part's profile");
    var cutRadii = Enumerable.Range(0, turnPath.MoveCount).Select(i => turnPath.Move(i)).Where(m => m.kind == Prism.Core.Cam.MoveKind.Cut && m.z < -30).Select(m => m.x).ToList();
    Check(cutRadii.Any(r => Math.Abs(r - 12) < 0.01) && Enumerable.Range(0, turnPath.MoveCount).Select(i => turnPath.Move(i)).Any(m => m.kind == Prism.Core.Cam.MoveKind.Cut && Math.Abs(m.x - 8) < 0.01 && m.z < -10 && m.z > -20.5), "the finishing pass reaches the 12 mm and 8 mm radii of the two steps");
    var program = Prism.Core.Cam.Post.Lathe([turnPath], Prism.Core.Cam.Controller.LinuxCNC, "Shaft");
    Check(program.Contains("G18") && program.Contains("G7") && program.Contains("X24") && program.Contains("X16") && program.Contains("Z-40"), "the lathe program writes X as a diameter and Z from the front face");
    bool grblRefused = false; try { Prism.Core.Cam.Post.Lathe([turnPath], Prism.Core.Cam.Controller.GRBL, "Shaft"); } catch (InvalidOperationException) { grblRefused = true; }
    Check(grblRefused, "GRBL is refused for lathe programs with a clear message");
}
// Keyboard shortcuts: a catalogue with scopes, changeable in Preferences, applied by the main window.
{
    Prism.Core.Hotkeys.ResetAll();
    try
    {
        var everywhere = Prism.Core.Hotkeys.ActiveScopes(false, false, "design");
        Check(Prism.Core.Hotkeys.Resolve("Ctrl+Z", everywhere).Select(a => a.Id).SequenceEqual(["edit.undo"]), "the shipped shortcuts are in the catalogue (Ctrl+Z is Undo)");
        var withCommand = Prism.Core.Hotkeys.ActiveScopes(true, false, "design");
        Check(Prism.Core.Hotkeys.Resolve("Home", withCommand).Select(a => a.Id).SequenceEqual(["animation.start", "view.extents"]) && Prism.Core.Hotkeys.Resolve("Home", everywhere).Select(a => a.Id).SequenceEqual(["view.extents"]), "the same key means something more specific while a command is open (Home)");
        var sketching = Prism.Core.Hotkeys.ActiveScopes(false, true, "design");
        Check(Prism.Core.Hotkeys.Resolve("R", sketching).Select(a => a.Id).SequenceEqual(["sketch.gizmo.rotate", "sketch.rect"]) && !Prism.Core.Hotkeys.Resolve("L", everywhere).Any() && Prism.Core.Hotkeys.Resolve("L", sketching).Any(a => a.Id == "sketch.line"), "sketch tool keys only exist while sketching, and gizmo keys are tried before tools");

        Prism.Core.Hotkeys.Set("edit.undo", "Ctrl+U");
        Check(Prism.Core.Hotkeys.Resolve("Ctrl+U", everywhere).Any(a => a.Id == "edit.undo") && !Prism.Core.Hotkeys.Resolve("Ctrl+Z", everywhere).Any(), "changing a shortcut moves it: the new key works and the old one no longer does");
        Check(Prism.Core.Hotkeys.FindConflict("edit.redo", "Ctrl+U", Prism.Core.Hotkeys.Global)?.Id == "edit.undo" && Prism.Core.Hotkeys.FindConflict("edit.redo", "Ctrl+U", Prism.Core.Hotkeys.Sketch) == null, "a key already taken in the same scope is reported, but the same key in another scope is fine");
        Prism.Core.Hotkeys.Set("file.save", "Ctrl+S", Prism.Core.Hotkeys.Workspace("make"));
        Check(!Prism.Core.Hotkeys.Resolve("Ctrl+S", everywhere).Any() && !Prism.Core.Hotkeys.Resolve("Ctrl+S", Prism.Core.Hotkeys.ActiveScopes(false, false, "design")).Any() && Prism.Core.Hotkeys.Resolve("Ctrl+S", Prism.Core.Hotkeys.ActiveScopes(false, false, "make")).Any(a => a.Id == "file.save"), "a shortcut can be limited to one workspace");
        Prism.Core.Hotkeys.Set("file.save", "");
        Check(Prism.Core.Hotkeys.Effective(Prism.Core.Hotkeys.Find("file.save")!).Gesture == "" && Prism.Core.Hotkeys.Display("") == "Not set", "a shortcut can be cleared");
        Prism.Core.Hotkeys.ResetAll();
        Check(Prism.Core.Hotkeys.Resolve("Ctrl+Z", everywhere).Any(a => a.Id == "edit.undo") && Prism.Core.Hotkeys.Display("Ctrl+Shift+D1") == "Ctrl+Shift+1", "Reset all restores the shipped keys");

        // the main window obeys the catalogue
        var keyVm = new MainViewModel { HomeOpen = false };
        var keyWindow = new MainWindow { DataContext = keyVm, Content = new MainView { DataContext = keyVm } }; keyWindow.Show(); Dispatcher.UIThread.RunJobs();
        var item = keyVm.Session.AddObject(Prism.Kernel.Occ.Box(5, 5, 5), "Box", ObjKind.Solid);
        keyWindow.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, "z"); Dispatcher.UIThread.RunJobs();
        Check(keyVm.Session.Objects.Count == 0, "Ctrl+Z undoes in the main window");
        keyVm.Session.Redo(); Dispatcher.UIThread.RunJobs();
        Prism.Core.Hotkeys.Set("edit.undo", "Ctrl+U");
        keyWindow.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, "z"); Dispatcher.UIThread.RunJobs();
        Check(keyVm.Session.Objects.Count == 1, "after rebinding, the old key does nothing");
        keyWindow.KeyPress(Key.U, RawInputModifiers.Control, PhysicalKey.U, "u"); Dispatcher.UIThread.RunJobs();
        Check(keyVm.Session.Objects.Count == 0, "after rebinding, the new key undoes");
        Prism.Core.Hotkeys.ResetAll();
        keyWindow.Close();

        // Preferences lists every shortcut and captures a new key
        var prefs = new SettingsWindow(keyVm); prefs.Show(); Dispatcher.UIThread.RunJobs();
        prefs.FindControl<ListBox>("Nav")!.SelectedIndex = 6; Dispatcher.UIThread.RunJobs();
        List<Button> KeyButtons() => prefs.FindControl<StackPanel>("KeyboardHost")!.GetVisualDescendants().OfType<Button>().Where(b => b.Tag is string).ToList();
        Check(prefs.FindControl<ScrollViewer>("PageKeyboard")!.IsVisible && KeyButtons().Count == Prism.Core.Hotkeys.Actions.Count && KeyButtons().Any(b => Equals(b.Content, "Ctrl+Z")), "Preferences has a Keyboard page listing every shortcut with its current key");
        foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            Application.Current!.RequestedThemeVariant = theme; Dispatcher.UIThread.RunJobs();
            using var shot = new RenderTargetBitmap(new PixelSize((int)prefs.Bounds.Width, (int)prefs.Bounds.Height)); shot.Render(prefs); shot.Save(Path.Combine(output, "keyboard-preferences-" + theme.Key.ToString()!.ToLowerInvariant() + ".png"));
        }
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        KeyButtons().Single(b => (string)b.Tag! == "edit.undo").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
        Check(Equals(KeyButtons().Single(b => (string)b.Tag! == "edit.undo").Content, "Press the keys…"), "clicking a key asks for the new combination");
        prefs.KeyPress(Key.K, RawInputModifiers.Control, PhysicalKey.K, "k"); Dispatcher.UIThread.RunJobs();
        Check(prefs.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text != null && t.Text.Contains("already used by")), "a key that is already taken is refused with an explanation");
        Check(Prism.Core.Hotkeys.Effective(Prism.Core.Hotkeys.Find("edit.undo")!).Gesture == "Ctrl+Z", "the refused key leaves the old shortcut in place");
        KeyButtons().Single(b => (string)b.Tag! == "edit.undo").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
        prefs.KeyPress(Key.U, RawInputModifiers.Control, PhysicalKey.U, "u"); Dispatcher.UIThread.RunJobs();
        Check(Prism.Core.Hotkeys.Effective(Prism.Core.Hotkeys.Find("edit.undo")!).Gesture == "Ctrl+U" && KeyButtons().Any(b => Equals(b.Content, "Ctrl+U")), "pressing a free combination in Preferences sets it and shows it");
        prefs.Close();
    }
    finally { Prism.Core.Hotkeys.ResetAll(); }
}
// Part info: saved in the file, searchable in the library, used by drawings and the assembly parts list.
{
    var infoDir = Path.Combine(Path.GetTempPath(), "prism-partinfo-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(infoDir);
    try
    {
        var infoVm = new MainViewModel { HomeOpen = false };
        infoVm.Session.AddObject(Prism.Kernel.Occ.Box(20, 10, 5), "Plate", ObjKind.Solid);
        infoVm.PartNumber = "BR-1042"; infoVm.PartDescription = "Mounting bracket"; infoVm.PartMaterial = "6061-T6"; infoVm.PartSupplier = "Acme Metals"; infoVm.PartCost = "3.5"; infoVm.PartTags = "bracket, aluminium"; infoVm.PartCustom = "Finish: anodised";
        var typed = infoVm.Session.PartInfo;
        Check(typed.PartNumber == "BR-1042" && typed.Cost == 3.5 && typed.Tags!.SequenceEqual(["bracket", "aluminium"]) && typed.Custom!["Finish"] == "anodised" && infoVm.Session.Dirty, "typing in the Part info panel updates the document");
        string bracketFile = Path.Combine(infoDir, "bracket.prism"); infoVm.Session.Save(bracketFile);
        var reopened = new CadSession(); reopened.Open(bracketFile);
        Check(reopened.PartInfo.Equals(typed) || (reopened.PartInfo.PartNumber == "BR-1042" && reopened.PartInfo.Tags!.Count == 2 && reopened.PartInfo.Custom!.Count == 1 && reopened.PartInfo.Cost == 3.5), "part info is saved in the file and comes back when it is opened");
        Check(Prism.Core.PartInfo.ReadFile(bracketFile) is { PartNumber: "BR-1042", Supplier: "Acme Metals" }, "part info can be read from a file without opening the part");
        var plain = new CadSession(); plain.AddObject(Prism.Kernel.Occ.Box(5, 5, 5), "Block", ObjKind.Solid); string plainFile = Path.Combine(infoDir, "plain.prism"); plain.Save(plainFile);
        Check(Prism.Core.PartInfo.ReadFile(plainFile) == null && new CadSession().PartInfo.IsEmpty, "a part with no info has none, and old files open without it");

        // the library shows and searches it
        var libOld = AppSettings.Current.PartsLibraryPath;
        try
        {
            AppSettings.Current.PartsLibraryPath = infoDir;
            var found = Prism.Core.PartsLibrary.Scan(infoDir);
            Check(found.Count == 2 && found.Single(p => p.Name == "bracket").Info is { PartNumber: "BR-1042" } && found.Single(p => p.Name == "plain").Info == null, "the parts library reads part info from the files it scans");
            infoVm.RescanLibrary(); infoVm.LibraryShowBuiltIn = false;
            infoVm.LibraryFilter = "anodised"; Check(infoVm.LibraryGroups.Sum(g => g.Items.Count) == 1, "the library searches custom fields");
            infoVm.LibraryFilter = "acme aluminium"; Check(infoVm.LibraryGroups.Sum(g => g.Items.Count) == 1 && infoVm.LibraryGroups[0].Items[0].Subtitle == "BR-1042", "the library finds parts by supplier and tags and shows the part number");
            infoVm.LibraryFilter = "titanium"; Check(infoVm.LibraryGroups.Sum(g => g.Items.Count) == 0, "a search that matches nothing shows nothing");
        }
        finally { AppSettings.Current.PartsLibraryPath = libOld; }

        // drawings start from the part info
        infoVm.Session.StartCommand(() => new DrawingCommand()); var drawing = infoVm.Session.ActiveCommand!;
        Check(drawing.Params.OfType<TextParam>().Single(p => p.Label == "Part number").Value == "BR-1042" && drawing.Params.OfType<TextParam>().Single(p => p.Label == "Material").Value == "6061-T6" && drawing.Params.OfType<TextParam>().Single(p => p.Label == "Title").Value == "Mounting bracket", "the drawing's title block starts from the part info");
        infoVm.Session.CancelCommand();

        // assembly parts list
        var asm = new CadSession();
        SceneObject Comp(string name, double x, PartInfo? info)
        {
            var o = asm.AddObject(Prism.Kernel.Occ.Translate(Prism.Kernel.Occ.Box(10, 10, 10), new Vec3(x, 0, 0)), name, ObjKind.Solid);
            asm.Update(o.Id, v => v.WithAsm(new Prism.Core.Assembly.ComponentInfo(CadSession.NewKey(), name, null, Prism.Core.Assembly.Xform.Translation(new Vec3(x, 0, 0)).ToArray(), false, info), v.Shape), false);
            return asm.Find(o.Id)!;
        }
        var bracketInfo = new PartInfo("BR-1042", "Mounting bracket", "A", "6061-T6", "Acme Metals", 3.5);
        var one = Comp("Bracket", 0, bracketInfo); Comp("Bracket", 30, bracketInfo); Comp("Bracket (copy)", 60, bracketInfo); Comp("Cover", 90, null);
        asm.Update(one.Id, v => v.WithMaterial(Materials.Find("6061")!), false);
        var rows = Prism.Core.Assembly.PartsList.Build(asm);
        Check(rows.Count == 2 && rows[0].PartNumber == "BR-1042" && rows[0].Quantity == 3 && rows[1].Name == "Cover" && rows[1].Quantity == 1, "the parts list counts components with the same part number as one line");
        Check(Math.Abs(rows[0].TotalCost!.Value - 10.5) < 1e-9 && rows[0].UnitMassGrams > 0 && rows[0].Material.Length > 0, "the parts list carries cost and, with a material applied, mass");
        var csv = Prism.Core.Assembly.PartsList.ToCsv(rows);
        Check(csv.StartsWith("Item,Part number,Name") && csv.Contains("BR-1042") && csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length == 3, "the parts list exports as CSV");
        asm.StartCommand(() => new PartsListCommand()); var listCmd = asm.ActiveCommand!; listCmd.TryBuild();
        Check(listCmd.Results.Any(r => r.Label.StartsWith("3 ×")) && listCmd.Results.Any(r => r.Label == "Total components" && r.Value == "4"), "the Parts List command shows each part and the totals");
        asm.CancelCommand();
    }
    finally { try { Directory.Delete(infoDir, true); } catch (Exception) { } }
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

