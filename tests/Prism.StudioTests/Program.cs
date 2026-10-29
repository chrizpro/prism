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
using Prism.Commands;
using Prism.Core;
using Prism.Core.Sketching;
using Prism.Kernel;
using Prism.ViewModels;
using Prism.Views;

NativeSearch.Initialize();
AppBuilder.Configure<TestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia().SetupWithoutStarting();
int checks = 0;
void Check(bool value, string text) { if (!value) throw new Exception(text); Console.WriteLine("PASS " + text); checks++; }
void Reject(Action action, string text) { try { action(); } catch (FormatException) { Check(true, text); return; } throw new Exception("Expected rejection: " + text); }
double Volume(CadSession session) => session.Objects.Where(o => o.Kind == ObjKind.Solid).Sum(o => Math.Abs(Occ.Mass(o.Shape).Volume));
var named = ParameterValues.Evaluate([new("wall", "3 mm"), new("rib", "wall * 0.75"), new("span", "2 cm + 1 inch")]);
Check(named["rib"] == 2.25 && Math.Abs(named["span"] - 45.4) < 1e-10, "named expressions support dependencies and length literals");
Check(Expression.Parse("sin(t) + 2t").Eval(30) == 60.5, "curve expression evaluation remains compatible");
Reject(() => ParameterValues.Evaluate([new("a", "b"), new("b", "a")]), "cycles rejected");
Reject(() => ParameterValues.Evaluate([new("width", "missing * 2")]), "missing names rejected");
Reject(() => ParameterValues.Evaluate([new("a", "1 / 0")]), "non-finite results rejected");
Reject(() => ParameterValues.Evaluate([new("a", "1"), new("A", "2")]), "case-insensitive duplicate names rejected");
Reject(() => ParameterValues.Evaluate([new("mm", "3")]), "unit identifiers cannot be shadowed");
Reject(() => ParameterValues.Evaluate([new("a", "t")]), "curve-only t is not silently accepted in document formulas");

var vm = new MainViewModel(); var s = vm.Session;
var model = new SketchModel(); model.AddCircle(model.AddPoint(0, 0), 10);
s.EnterSketch(Plane3.XY, model); s.ExitSketch(true);
var sketch = s.Objects.Single(o => o.Kind == ObjKind.Sketch);
s.SelectItems([SelItem.Of(sketch)]); s.StartCommand(() => new ExtrudeCommand()); s.CommitCommand();
Check(s.Current.Recipe?.Title == "Extrude", "fixture creates an editable extrusion");
vm.OpenStudio("parameters");
Check(vm.ShowParametersStudio && !vm.ShowProperties && !vm.ShowMaterialsPanel, "only requested studio occupies the sidebar");
vm.NewParameterName = "depth"; vm.NewParameterFormula = "12 mm"; vm.AddStudioParameter(); vm.ApplyStudioParameters();
Check(s.DocumentParameters.Single().Name == "depth", "document parameter draft applies to session");
vm.SelectedStudioFeature = vm.StudioFeatures.Single(f => f.Node.Recipe?.Title == "Extrude");
vm.FeatureValueRows.Single(r => r.Label == "Distance").Formula = "depth";
vm.ApplyStudioFeatureValues();
Check(!s.StatusIsError && Math.Abs(Volume(s) - Math.PI * 100 * 12) < .01, "feature expression updates exact geometry");
Check(s.HistoryPath().Past.Last(n => n.Recipe?.Title == "Extrude").Recipe!.Expressions!.Values.Contains("depth"), "feature retains its named expression binding");
var bound = s.Current; double oldVolume = Volume(s);
vm.DocumentParameterRows.Single().Formula = "24 mm"; vm.ApplyStudioParameters();
Check(!s.StatusIsError && Math.Abs(Volume(s) - oldVolume * 2) < .01, "changing named values rebuilds bound features");
var changed = s.Current;
s.GoTo(bound); Check(s.DocumentParameters.Single().Formula == "12 mm" && Math.Abs(Volume(s) - oldVolume) < .01, "history restores parameter definitions with geometry");
s.GoTo(changed);
var beforeInvalid = s.Current;
Check(!s.ApplyDocumentParameters([new("depth", "unknown")]) && s.Current == beforeInvalid, "invalid definitions leave geometry and history untouched");
Check(!s.ApplyDocumentParameters([]) && s.Current == beforeInvalid, "removing a bound named parameter is blocked");
var path = Path.Combine(Path.GetTempPath(), "prism-studio-" + Guid.NewGuid().ToString("N") + ".prism");
s.Save(path); var reopened = new CadSession(); reopened.Open(path);
Check(reopened.DocumentParameters.Single().Formula == "24 mm" && Math.Abs(Volume(reopened) - Volume(s)) < .01, "named parameters and final geometry survive save/reopen");
reopened.NewDocument(); Check(reopened.DocumentParameters.Count == 0, "new document clears named definitions");

vm.OpenStudio("references");
Check(vm.StudioReferences.Count(r => r.OriginIndex >= 0) == 7 && vm.StudioReferences.Any(r => r.Id == sketch.Id), "references reuse document origins and sketch identities");
vm.SelectedStudioReference = vm.StudioReferences.Single(r => r.Id == sketch.Id);
Check(vm.ReferenceUsage.Any(u => u.Contains("Extrude")), "reference usage follows captured feature inputs");
bool visible = s.Find(sketch.Id)!.Visible;
vm.SelectedStudioReference.Visible = !visible;
Check(s.Find(sketch.Id)!.Visible != visible, "reference eye controls change actual document visibility");
vm.ReferenceSearch = "XY Plane"; Check(vm.StudioReferences.Count == 1, "reference search filters by name");
vm.SelectedStudioReference = vm.StudioReferences.Single(); vm.LocateStudioReference();
Check(s.Selection.Single().Kind == SelKind.Origin && s.OriginVisible[0], "locating an origin reference selects and reveals it");
vm.ReferenceSearch = "";

vm.OpenStudio("diagnostics"); vm.RunStudioChecks();
Check(vm.DiagnosticIssues.Any(i => i.ObjectId == sketch.Id && !i.IsError), "diagnostics identify underconstrained sketches");
Check(!vm.DiagnosticsStale && vm.DiagnosticPassed.Count == 2, "explicit validity checks produce an up-to-date result summary");
var issue = vm.DiagnosticIssues.Single(i => i.ObjectId == sketch.Id); vm.LocateStudioIssue(issue);
Check(s.Selection.Single().Obj == sketch.Id, "issue location selects its real geometry");
s.Say("Studio activity test", true);
Check(vm.StudioActivity.First().Contains("Studio activity test"), "activity includes the latest session error immediately");
s.Update(sketch.Id, o => o.WithName("Mounting profile")); Check(vm.DiagnosticsStale, "document changes invalidate old diagnostic results");
vm.EditStudioIssue(issue); Check(s.Sketch != null && !vm.AnyStudioVisible, "issue repair opens the actual sketch editor and yields the sidebar");
var activeEdit = s.Sketch;
vm.OpenStudio("parameters"); Check(vm.ShowParametersStudio && ReferenceEquals(s.Sketch,activeEdit), "opening another inspector preserves the active sketch edit");
s.ExitSketch(false); Check(vm.ShowParametersStudio, "the chosen inspector remains selected when the operation ends");

// Two separate dependent features must both use the newly evaluated definitions.
var solid = s.Objects.Single(o => o.Kind == ObjKind.Solid);
int circularEdge = Enumerable.Range(0, solid.Shape.Count(SubKind.Edge)).First(i => Occ.EdgeInfoOf(solid.Shape.Sub(SubKind.Edge, i)).Radius > 0);
s.SelectItems([new SelItem(solid.Id, SelKind.Edge, circularEdge)]);
s.StartCommand(() => new FilletCommand()); s.ActiveCommand!.Params.OfType<NumberParam>().Single(p => p.Label == "Radius").Value = 1; s.CommitCommand();
vm.OpenStudio("parameters");
vm.SelectedStudioFeature = vm.StudioFeatures.Last(f => f.Node.Recipe?.Title == "Fillet");
vm.FeatureValueRows.Single(r => r.Label == "Radius").Formula = "depth / 24"; vm.ApplyStudioFeatureValues();
Check(s.ApplyDocumentParameters([new("depth", "12 mm")]), "named update replays multiple dependent features");
var fillet = s.HistoryPath().Past.Last(n => n.Recipe?.Title == "Fillet").Recipe!;
Check(fillet.Values.OfType<double>().First() == .5 && s.Objects.Single(o => o.Kind == ObjKind.Solid).Shape.IsValid, "downstream fillet uses the new formula value");
var beforeRangeFailure = s.Current;
Check(!s.ApplyDocumentParameters([new("depth", "0.001 mm")]) && s.Current == beforeRangeFailure, "bound feature ranges are checked before changing history");
vm.SelectedStudioFeature = vm.StudioFeatures.Last(f => f.Node.Recipe?.Title == "Fillet");
vm.FeatureValueRows.First().Formula = "1"; vm.ApplyStudioFeatureValues();
var completeBranch = s.Current;
Check(!s.ApplyDocumentParameters([new("depth", "0.01 mm")]) && s.RebuildIssue?.FailedStep.Recipe?.Title == "Fillet", "native rebuild failures identify the exact dependent feature");
vm.OpenStudio("diagnostics"); vm.RunStudioChecks();
Check(vm.DiagnosticIssues.Any(i => i.IsError && i.Feature?.Recipe?.Title == "Fillet"), "diagnostics expose the structured rebuild failure");
s.GoTo(completeBranch); Check(s.Objects.Single(o => o.Kind == ObjKind.Solid).Shape.IsValid && s.DocumentParameters.Single().Formula == "12 mm", "failed parameter rebuild preserves the complete original branch");
var editable = s.HistoryPath().Past.Last(n => n.Recipe?.Title == "Extrude");
s.EditFeature(editable);
var distance = s.ActiveCommand!.Params.OfType<NumberParam>().Single(p => p.Label == "Distance");
distance.Value = 7;
Check(distance.Formula.Length == 0, "typing a literal into the command editor detaches its formula");
s.CancelCommand();
s.ApplyDocumentParameters([new("depth", "12 mm", true), new("wall", "3 mm"), new("rib", "wall * 0.75")]);

var panel = new StudiosPanel { DataContext = vm }; var host = new Window { Width = 340, Height = 820, Content = panel };
host.Show(); Dispatcher.UIThread.RunJobs();
var output = Path.GetFullPath("docs/studios"); Directory.CreateDirectory(output);
foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
{
    Application.Current!.RequestedThemeVariant = theme;
    foreach (var studio in new[] { "diagnostics", "references", "parameters" })
    {
        vm.OpenStudio(studio); if (studio == "diagnostics") vm.RunStudioChecks();
        if (studio == "references") vm.SelectedStudioReference = vm.StudioReferences.First(r => r.Id == sketch.Id);
        Dispatcher.UIThread.RunJobs();
        Check(panel.Bounds.Width == 340 && panel.Bounds.Height == 820, $"{studio} fits the sidebar in {theme.Key}");
        using var image = new RenderTargetBitmap(new PixelSize(340, 820)); image.Render(panel);
        image.Save(Path.Combine(output, $"{studio}-{theme.Key.ToString()!.ToLowerInvariant()}.png"));
        if (studio == "parameters")
        {
            panel.FindControl<TabControl>("ParametersTabs")!.SelectedIndex = 1; Dispatcher.UIThread.RunJobs();
            using var featureImage = new RenderTargetBitmap(new PixelSize(340, 820)); featureImage.Render(panel);
            featureImage.Save(Path.Combine(output, $"parameters-feature-{theme.Key.ToString()!.ToLowerInvariant()}.png"));
            panel.FindControl<TabControl>("ParametersTabs")!.SelectedIndex = 0;
        }
    }
}
host.Close();
var oldSession = vm.Session;
vm.NewDocumentTab(); vm.OpenStudio("parameters");
Check(vm.DocumentParameterRows.Count == 0 && vm.StudioFeatures.Count == 0, "document switches cannot leak parameters or feature references");
oldSession.Say("detached session message", true); vm.OpenStudio("diagnostics");
Check(vm.StudioActivity.All(x => !x.Contains("detached session message")), "old session activity is detached");
var mainView = new MainView { DataContext = vm }; var mainHost = new Window { Width = 1000, Height = 640, Content = mainView };
mainHost.Show(); Dispatcher.UIThread.RunJobs();
var picker = mainView.FindControl<Button>("PanelPicker")!;
picker.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
var panelMenu = (Avalonia.Controls.MenuFlyout)Avalonia.Controls.Primitives.FlyoutBase.GetAttachedFlyout(picker)!;
var parameterItem = panelMenu.ItemsSource!.Cast<MenuItem>().Single(b => b.Header!.ToString()!.EndsWith("Parameters"));
parameterItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
parameterItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); panelMenu.Hide(); Dispatcher.UIThread.RunJobs();
Check(vm.ShowParametersStudio && mainView.FindControl<Grid>("WorkGrid")!.ColumnDefinitions[4].Width.Value == 394, "header panel picker opens the full-width studio at the minimum window size");
vm.ShowScriptPanel = true; vm.OpenStudio("references");
Check(vm.ShowReferencesStudio && !vm.ShowScriptEditor, "script workspace cannot cover an explicitly opened studio");
vm.OpenStudio(null); Check(vm.ShowScriptEditor, "closing a studio restores the script editor when requested");
mainHost.Close();
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
