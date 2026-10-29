using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
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
using Prism.Rendering;
using Prism.ViewModels;
using Prism.Views;

NativeSearch.Initialize();
AppBuilder.Configure<TestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia().SetupWithoutStarting();
int checks = 0;
void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
void PumpPreviews(Control control)
{
    var stop = DateTime.UtcNow.AddSeconds(20);
    do
    {
        Dispatcher.UIThread.RunJobs();
        if (control.GetVisualDescendants().OfType<GeometryPreview>().All(p => p.PendingRender.IsCompleted)) break;
        Thread.Sleep(10);
    } while (DateTime.UtcNow < stop);
    Dispatcher.UIThread.RunJobs();
    Check(control.GetVisualDescendants().OfType<GeometryPreview>().All(p => p.PendingRender.IsCompleted), "preview jobs finish without blocking the UI dispatcher");
}
string output = Path.GetFullPath("docs/studio-polish"); Directory.CreateDirectory(output);
void Capture(Control panel, string name, ThemeVariant theme)
{
    Application.Current!.RequestedThemeVariant = theme; Dispatcher.UIThread.RunJobs(); PumpPreviews(panel);
    using var bitmap = new RenderTargetBitmap(new PixelSize(394, 900)); bitmap.Render(panel);
    bitmap.Save(Path.Combine(output, name + "-" + theme.Key.ToString()!.ToLowerInvariant() + ".png"));
}
var vm = new MainViewModel(); var s = vm.Session;
var model = new SketchModel();
var coords = new[] { (0.0, 0.0), (40.0, 0.0), (40.0, 6.0), (6.0, 6.0), (6.0, 36.0), (0.0, 36.0) };
var pointIds = coords.Select(p => model.AddPoint(p.Item1, p.Item2)).ToArray();
for (int i = 0; i < pointIds.Length; i++) model.AddLine(pointIds[i], pointIds[(i + 1) % pointIds.Length]);
s.EnterSketch(Plane3.XY, model); s.ExitSketch(true); var sketch = s.Objects.Single();
s.SelectItems([SelItem.Of(sketch)]); s.StartCommand(() => new ExtrudeCommand());
s.ActiveCommand!.Params.OfType<NumberParam>().Single(p => p.Label == "Distance").Value = 24; s.CommitCommand();
var body = s.Objects.Single(o => o.Kind == ObjKind.Solid);
int edge = Enumerable.Range(0, body.Shape.Count(SubKind.Edge)).First(i =>
{
    var info = Occ.EdgeInfoOf(body.Shape.Sub(SubKind.Edge, i));
    return Math.Abs(info.Start.X - 40) < .001 && Math.Abs(info.End.X - 40) < .001 && Math.Abs(info.Start.Y - 6) < .001 && Math.Abs(info.End.Y - 6) < .001;
});
s.SelectItems([new SelItem(body.Id, SelKind.Edge, edge)]); s.StartCommand(() => new FilletCommand());
s.ActiveCommand!.Params.OfType<NumberParam>().Single(p => p.Label == "Radius").Value = 2; s.CommitCommand();
Check(!s.StatusIsError, "L-section fixture has a valid initial fillet");
s.SelectItems([SelItem.Of(s.Objects.Single(o => o.Kind == ObjKind.Solid))]);
s.StartCommand(() => new TransformCommand(TransformCommand.Kind.Move)); s.CommitCommand();
var original = s.Current;
s.EditSketch(s.Find(sketch.Id)!);
s.Sketch!.Model.P(pointIds[2]).Y = .7; s.Sketch.Model.P(pointIds[3]).X = .7; s.Sketch.Model.P(pointIds[3]).Y = .7; s.Sketch.Model.P(pointIds[4]).X = .7;
s.ExitSketch(true);
Check(s.RebuildIssue != null && s.StatusIsError, "thinner bracket produces a real downstream fillet failure");
vm.OpenStudio("diagnostics"); vm.RunStudioChecks();
Check(vm.SelectedStudioIssue?.IsError == true && vm.IssuePreview != null, "failed feature is selected with actual input geometry");
var preview = vm.IssuePreview!;
Check(ReferenceEquals(preview.Mesh, s.RebuildIssue!.At.After.Single(o => o.Kind == ObjKind.Solid).Mesh), "preview uses the failed attempt's changed input, not the old successful body");
Check(preview.Edges.Contains(edge), "rebuild records remapped problem-edge selections");
var cameraBefore = (s.Camera.Target, s.Camera.Distance, s.Camera.Yaw, s.Camera.Pitch);
var rendered = StudioPreview.Render(preview, false);
Check(rendered.HighlightPixels > 5 && rendered.Bgra.Length == rendered.Width * rendered.Height * 4, "cached render includes visible problem-edge pixels");
Check(cameraBefore == (s.Camera.Target, s.Camera.Distance, s.Camera.Yaw, s.Camera.Pitch), "preview framing does not alter the working camera");
using (var canceled = new CancellationTokenSource())
{
    canceled.Cancel();
    try { StudioPreview.Render(preview, false, canceled.Token); throw new Exception("Expected cancellation"); }
    catch (OperationCanceledException) { Check(true, "stale preview render work is cancellable"); }
}
var panel = new StudiosPanel { DataContext = vm }; var host = new Window { Width = 394, Height = 900, Content = panel };
host.Show(); Dispatcher.UIThread.RunJobs();
foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
{
    Capture(panel, "diagnostics", theme);
    Check(panel.FindControl<GeometryPreview>("IssueGeometryPreview")!.HasRenderedImage, "Diagnostics displays cached geometry in " + theme.Key);
}
var failedState = s.Current;
vm.EditStudioIssue(vm.SelectedStudioIssue!);
Check(s.Current == failedState && s.ActiveCommand is FilletCommand, "repair opens the failed operation on its changed input state");
s.CancelCommand(); Check(s.Current == failedState && s.RebuildIssue != null, "canceling repair preserves the failure and its input snapshot");
vm.EditStudioIssue(vm.SelectedStudioIssue!);
s.ActiveCommand!.Params.OfType<NumberParam>().Single(p => p.Label == "Radius").Value = .2; s.CommitCommand();
Check(s.RebuildIssue == null && !s.StatusIsError && s.HistoryPath().Past.Last(n => n.Recipe != null).Recipe!.Title == "Move", "repair resumes downstream operations with the preserved object map");
Check(s.Find(sketch.Id)!.SketchData!.P(pointIds[2]).Y == .7 && Math.Abs(s.Objects.Single(o => o.Kind == ObjKind.Solid).Mesh.Min.X - 10) < .001, "repair retains the modified sketch and applies the later placement");
vm.DiagnosticSearch = "not a real issue"; Check(vm.VisibleDiagnosticIssues.Count == 0 && vm.IssuePreview == null, "issue search clears a nonmatching preview");
vm.RunStudioChecks(); Check(vm.SelectedStudioIssue == null && vm.IssuePreview == null, "rerunning checks respects the issue search rather than restoring a filtered-out preview");
vm.DiagnosticSearch = "";
vm.OpenStudio("references"); vm.SelectedStudioReference = vm.StudioReferences.First(r => r.OriginIndex == 0);
Check(vm.ReferencePreview != null && vm.ReferenceFields.Count > 0, "reference inspector previews the selected plane and its real frame");
foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light }) Capture(panel, "references", theme);
s.GoTo(original);
vm.OpenStudio("diagnostics"); vm.RunStudioChecks(); var previousSketchPreview = vm.IssuePreview!.Mesh;
s.EditSketch(s.Find(sketch.Id)!); s.Sketch!.Model.P(pointIds[1]).X = 42; s.Sketch.Model.P(pointIds[2]).X = 42; s.ExitSketch(true);
vm.RunStudioChecks();
Check(vm.IssuePreview != null && !ReferenceEquals(previousSketchPreview, vm.IssuePreview.Mesh) && ReferenceEquals(vm.IssuePreview.Mesh, s.Find(sketch.Id)!.Mesh), "rerunning checks refreshes geometry even when an issue's text and identity are unchanged");
s.ApplyDocumentParameters([new("width", "120 mm"), new("height", "80 mm"), new("wall", "3 mm", true), new("rib", "wall * 0.75"), new("hole_diameter", "6 mm", true)]);
vm.OpenStudio("parameters"); vm.SelectedParameterDraft = vm.DocumentParameterRows.Single(p => p.Name == "rib");
vm.SelectedStudioFeature = vm.StudioFeatures.Last(f => f.Node.Recipe?.Title == "Fillet");
vm.FeatureValueRows.Single(p => p.Label == "Radius").Formula = "rib"; vm.ApplyStudioFeatureValues();
vm.SelectedParameterDraft = vm.DocumentParameterRows.Single(p => p.Name == "rib");
Check(vm.PinnedParameterRows.Count == 2 && vm.VisibleParameterRows.Count == 5, "parameter table separates pinned values and document expressions");
foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light }) Capture(panel, "parameters", theme);
var keep = vm.SelectedParameterDraft; keep.Formula = "wall * 0.8"; vm.PreviewDocumentParameters();
Check(ReferenceEquals(vm.SelectedParameterDraft, keep) && keep.Result == "2.4", "editing expressions preserves the selected row and recalculates its result");
vm.ParameterSearch = "wall"; Check(vm.VisibleParameterRows.All(p => (p.Name + p.Formula).Contains("wall")), "parameter search filters the expression table");
vm.ParameterSearch = "";
vm.NewDocumentTab(); vm.OpenStudio("diagnostics");
Check(vm.IssuePreview == null && vm.ReferencePreview == null, "switching documents clears all old geometry preview sources");
PumpPreviews(panel); host.Close();

vm.Session.AddObject(Occ.Box(20, 20, 10), "Appearance sample", ObjKind.Solid);
vm.Session.SelectItems([SelItem.Of(vm.Session.Objects.Single())]); vm.ApplyMaterial(Materials.Find("6061")!); vm.ApplyFinishPreset(vm.FinishPresets[0]); vm.OpenStudio("appearance");
var appearance = new MaterialsPanel { DataContext = vm }; var appearanceHost = new Window { Width = 394, Height = 900, Content = appearance };
appearanceHost.Show(); Dispatcher.UIThread.RunJobs();
foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light }) Capture(appearance, "appearance", theme);
Check(appearance.FindControl<ListBox>("FinishLibrary")!.Items.Count == 6, "polished finish library retains all six working presets");
vm.ShowAssignedMaterial(); appearance.FindControl<TabControl>("StudioTabs")!.SelectedIndex = 1; Dispatcher.UIThread.RunJobs();
var expander = appearance.GetVisualDescendants().OfType<Expander>().First();
var disclosure = expander.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>().First();
disclosure.IsChecked = true; Dispatcher.UIThread.RunJobs();
Check(expander.IsExpanded, "restyled material disclosures still expand through their native toggle controls");
disclosure.IsChecked = false; Dispatcher.UIThread.RunJobs();
foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light }) Capture(appearance, "material-library", theme);
appearanceHost.Close(); Console.WriteLine($"SUCCESS: {checks} checks");
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
