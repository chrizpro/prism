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
using Prism.ViewModels;
using Prism.Views;

NativeSearch.Initialize();
AppBuilder.Configure<TestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia().SetupWithoutStarting();
int checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    Console.WriteLine("PASS " + message); checks++;
}
double Volume(CadSession session) => session.Objects.Where(o => o.Kind == ObjKind.Solid).Sum(o => Math.Abs(Occ.Mass(o.Shape).Volume));
SceneObject CircleSketch(CadSession session, double radius)
{
    var model = new SketchModel(); model.AddCircle(model.AddPoint(0, 0), radius);
    session.EnterSketch(Plane3.XY, model); session.ExitSketch(true);
    return session.Objects.Last(o => o.Kind == ObjKind.Sketch);
}
void Extrude(CadSession session, SceneObject sketch, double distance)
{
    session.SelectItems([SelItem.Of(sketch)]);
    session.StartCommand(() => new ExtrudeCommand());
    session.ActiveCommand!.Params.OfType<NumberParam>().Single(p => p.Label == "Distance").Value = distance;
    session.CommitCommand();
    Check(session.ActiveCommand == null && !session.StatusIsError, "extrusion commits with an editable recipe");
}

var s = new CadSession();
var sketch = CircleSketch(s, 10); var sketchNode = s.Current;
Extrude(s, sketch, 12); var extrudeNode = s.Current;
var cylinder = s.Objects.Single(o => o.Kind == ObjKind.Solid);
int roundEdge = Enumerable.Range(0, cylinder.Shape.Count(SubKind.Edge)).First(i => Occ.EdgeInfoOf(cylinder.Shape.Sub(SubKind.Edge, i)).Radius > 0);
s.SelectItems([new SelItem(cylinder.Id, SelKind.Edge, roundEdge)]);
s.StartCommand(() => new FilletCommand());
s.ActiveCommand!.Params.OfType<NumberParam>().Single(p => p.Label == "Radius").Value = 1;
s.CommitCommand();
Check(s.ActiveCommand == null && !s.StatusIsError && s.Current.Recipe?.Title == "Fillet", "downstream fillet is editable: " + s.Status);
var originalTip = s.Current; double originalVolume = Volume(s);
s.PinCurrent("Approved base");

Check(s.EditSketch(s.Find(sketch.Id)!), "existing sketch opens for editing");
Check(s.Objects.All(o => o.Kind != ObjKind.Solid), "sketch editing rolls back downstream solids");
s.Sketch!.Model.Entities.Single(e => e.Type == EntType.Circle).R = 20;
s.ExitSketch(true);
Check(!s.StatusIsError && s.Current.Recipe?.Title == "Fillet", "sketch change rebuilds extrusion and downstream fillet");
Check(Volume(s) > originalVolume * 3.8, "rebuilt solid volume reflects the larger sketch");
Check(s.Current.Pin == "Approved base", "rebuild retains named checkpoints");
Check(originalTip.After.Any(o => ReferenceEquals(o, cylinder)) == false, "original fillet snapshot is distinct from its input");
var newTip = s.Current; double enlargedVolume = Volume(s);
s.GoTo(originalTip);
Check(Math.Abs(Volume(s) - originalVolume) < 1e-6, "original geometry remains accessible on its branch");
s.GoTo(newTip);

var activeExtrude = s.HistoryPath().Past.Single(n => n.Recipe?.Title == "Extrude");
Check(s.EditFeature(activeExtrude), "earlier feature opens with captured inputs");
Check(s.ActiveCommand!.CommitText == "Update", "feature editor exposes Update action");
s.ActiveCommand.Params.OfType<NumberParam>().Single(p => p.Label == "Distance").Value = 24;
s.CommitCommand();
Check(!s.StatusIsError && s.Current.Recipe?.Title == "Fillet" && Volume(s) > enlargedVolume * 1.9, "editing extrusion depth rebuilds the following fillet");
var updatedTip = s.Current;
var updatedExtrude = s.HistoryPath().Past.Single(n => n.Recipe?.Title == "Extrude");
Check(s.EditFeature(updatedExtrude), "updated feature can be edited again");
s.CancelCommand();
Check(s.Current == updatedTip, "canceling feature editing restores the exact previous state");
Check(s.EditSketch(s.Find(sketch.Id)!), "rebuilt sketch can be reopened");
s.Sketch!.Model.Entities.Single(e => e.Type == EntType.Circle).R = 50;
s.GoTo(originalTip);
Check(s.Sketch != null, "history navigation is blocked during a sketch edit");
s.ExitSketch(false);
Check(s.Current == updatedTip && s.Find(sketch.Id)!.SketchData!.Entities.Single().R == 20, "canceling sketch editing restores geometry and history position");

// A rollback can have a preferred future. Rebuilding must stop at the displayed tip.
s.GoTo(updatedExtrude);
Check(s.EditSketch(s.Find(sketch.Id)!), "sketch edit works while rolled back");
s.Sketch!.Model.Entities.Single().R = 15;
s.ExitSketch(true);
Check(s.Current.Recipe?.Title == "Extrude", "sketch rebuild does not resurrect the dimmed future after rollback");
var rolledTip = s.Current;
s.GoTo(sketchNode);
Check(s.EditSketch(s.Find(sketch.Id)!), "sketch can be edited directly at its history step");
s.Sketch!.Model.Entities.Single().R = 18;
s.ExitSketch(true);
Check(!s.StatusIsError && s.Current != sketchNode && s.Objects.Any(o => o.Kind == ObjKind.Solid), "editing the historical sketch rebuilds its preferred continuation");

// Invalid downstream geometry must report the failed step and preserve the old branch.
s.GoTo(updatedTip);
var beforeFailure = s.Current;
Check(s.EditSketch(s.Find(sketch.Id)!), "start a sketch edit that invalidates a later feature");
s.Sketch!.Model.Entities.Single().R = 0.05;
s.ExitSketch(true);
Check(s.StatusIsError && s.Status.Contains("could not be repeated"), "invalid downstream fillet reports rebuild failure: " + s.Status);
s.GoTo(beforeFailure);
Check(s.Objects.Any(o => o.Kind == ObjKind.Solid), "failed rebuild preserves the complete original branch");

var graph = HistoryGraphLayout.Build(s.Root);
Check(graph.Entries.Count == s.AllNodes().Count(), "graph includes every alternative exactly once");
Check(graph.Entries.Select(e => (e.Lane, e.Column)).Distinct().Count() == graph.Entries.Count, "history nodes never overlap");
var layoutBefore = graph.Entries.ToDictionary(e => e.Node.Id, e => (e.Lane, e.Column));
s.GoTo(originalTip);
Check(HistoryGraphLayout.Build(s.Root).Entries.All(e => layoutBefore[e.Node.Id] == (e.Lane, e.Column)), "navigation does not rearrange branch lanes");
var deepRoot = new HistoryNode(); var leaf = deepRoot;
for (int i = 0; i < 12000; i++) { var next = new HistoryNode { Parent = leaf }; leaf.Children.Add(next); leaf = next; }
Check(HistoryGraphLayout.Build(deepRoot).Entries.Count == 12001, "graph handles deep history without recursive traversal");

var assembly = new CadSession();
var firstSketch = CircleSketch(assembly, 8); Extrude(assembly, firstSketch, 10);
var secondModel = new SketchModel(); secondModel.AddCircle(secondModel.AddPoint(100, 0), 3);
assembly.EnterSketch(Plane3.XY, secondModel); assembly.ExitSketch(true);
var secondSketch = assembly.Objects.Last(o => o.Kind == ObjKind.Sketch);
Extrude(assembly, secondSketch, 5);
var secondBody = assembly.Objects.Last(o => o.Kind == ObjKind.Solid);
assembly.Update(secondBody.Id, o => o.WithName("Independent pin"));
Check(assembly.EditSketch(assembly.Find(firstSketch.Id)!), "edit a sketch before an unrelated sketch and body");
assembly.Sketch!.Model.Entities.Single().R = 12; assembly.ExitSketch(true);
Check(!assembly.StatusIsError && assembly.Objects.Count(o => o.Kind == ObjKind.Solid) == 2, "rebuild retains unrelated later sketch and body");
Check(Math.Abs(Volume(assembly) - (Math.PI * 144 * 10 + Math.PI * 9 * 5)) < 0.01, "multiple feature recipes remap their inputs to the correct sketches");
Check(assembly.Objects.Any(o => o.Name == "Independent pin"), "property changes replay without overwriting rebuilt geometry");
var branchPoint = assembly.Current;
assembly.BranchHistory("Lightweight");
Check(assembly.Current.Parent == branchPoint && assembly.Current.Pin == "Lightweight", "explicit branches create named history snapshots");
assembly.GoTo(branchPoint); assembly.BranchHistory("Alternate mount");
Check(branchPoint.Children.Count == 2 && branchPoint.Children[0].Pin == "Lightweight", "starting an alternative preserves its sibling branch");

// Exercise real controls and save light/dark layout captures for visual review.
// Use the geometry test session's real branching snapshots, without replacing VM session state.
List<HistoryItem> Items() => s.AllNodes().Select(n => new HistoryItem { Node = n, Label = n.Label, Icon = n.Icon, Pin = n.Pin, IsCurrent = n == s.Current, OnPath = s.HistoryPath().Past.Contains(n) }).ToList();
var view = new HistoryGraphView(); view.SetHistory(s, Items());
HistoryItem? invoked = null; view.Navigate += i => invoked = i;
var host = new Window { Width = 1200, Height = 310, Content = view, Background = Brushes.Transparent };
host.Show(); Dispatcher.UIThread.RunJobs();
Check(view.GetVisualDescendants().OfType<Button>().Count(b => b.DataContext is HistoryItem && b.Focusable) == graph.Entries.Count, "every graph node is a focusable native button");
var nodeButton = view.GetVisualDescendants().OfType<Button>().First(b => b.DataContext is HistoryItem { Node: var n } && n == originalTip);
nodeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Check(invoked?.Node == originalTip, "clicking a graph node invokes the correct history state");
string output = Path.GetFullPath("docs/history-timeline"); Directory.CreateDirectory(output);
foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
{
    Application.Current!.RequestedThemeVariant = theme; Dispatcher.UIThread.RunJobs();
    view.SetHistory(s, Items()); Dispatcher.UIThread.RunJobs();
    using var bitmap = new RenderTargetBitmap(new PixelSize(1200, 310)); bitmap.Render(view);
    bitmap.Save(Path.Combine(output, $"history-{theme.Key.ToString()!.ToLowerInvariant()}.png"));
}
host.Close();
var settings = AppSettings.Current;
bool expandedBefore = settings.HistoryExpanded; double heightBefore = settings.HistoryHeight;
string? homeBefore = Environment.GetEnvironmentVariable("PRISM_NO_HOME");
try
{
    Environment.SetEnvironmentVariable("PRISM_NO_HOME", "1");
    var workspaceVm = new MainViewModel { HistoryExpanded = true, HistoryHeight = 180 };
    var workspace = new MainView { DataContext = workspaceVm };
    var window = new Window { Width = 1000, Height = 640, Content = workspace };
    window.Show(); Dispatcher.UIThread.RunJobs();
    var graphView = workspace.FindControl<HistoryGraphView>("HistoryGraph")!;
    Check(graphView.IsVisible && graphView.Bounds.Height == 180, "timeline is expanded and sized correctly at the minimum window size");
    var collapse = workspace.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Collapse"));
    collapse.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
    Check(!graphView.IsVisible && !workspaceVm.HistoryExpanded, "collapse hides the graph while preserving its header");
    var expand = workspace.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Expand"));
    expand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
    Check(graphView.IsVisible && workspaceVm.HistoryHeight == 180, "expanding restores the previous timeline height");
    window.Close();
}
finally
{
    settings.HistoryExpanded = expandedBefore; settings.HistoryHeight = heightBefore; AppSettings.Save(false);
    Environment.SetEnvironmentVariable("PRISM_NO_HOME", homeBefore);
}
Console.WriteLine($"SUCCESS: {checks} checks");

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

