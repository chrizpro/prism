using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using Prism.Core;
using Prism.Core.Sketching;
using Prism.Kernel;
using Prism.ViewModels;

Environment.SetEnvironmentVariable("PRISM_SETTINGS_FILE", Path.Combine(Path.GetTempPath(), "prism-constraint-test-" + Guid.NewGuid().ToString("N") + ".json"));
Environment.SetEnvironmentVariable("PRISM_NO_HOME", "1");
Native.Initialize();
AppBuilder.Configure<TestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia().SetupWithoutStarting();
int checks = 0;
void Check(bool ok, string message) { if (!ok) throw new Exception("FAIL " + message); Console.WriteLine("PASS " + message); checks++; }
double Length(SketchModel m, int a, int b) { var p = m.P(a); var q = m.P(b); return Math.Sqrt((p.X - q.X) * (p.X - q.X) + (p.Y - q.Y) * (p.Y - q.Y)); }

// ---- the solver ignores a switched-off constraint
var model = new SketchModel();
int a = model.AddPoint(0, 0), b = model.AddPoint(30, 4); int line = model.AddLine(a, b);
model.Add(CType.Fixed, 0, 0, false, a);
var horizontal = model.Add(CType.Horizontal, 0, 0, false, line);
var length = model.Add(CType.LineLength, 50, 0, true, line);
var solved = SketchSolver.Solve(model);
Check(solved.Converged && Math.Abs(Length(model, a, b) - 50) < 1e-6 && Math.Abs(model.P(b).Y) < 1e-6, "horizontal and a 50 mm length both hold");

length.Disabled = true;
model.P(b).X = 80;
SketchSolver.Solve(model);
Check(Math.Abs(Length(model, a, b) - 80) < 1e-6 && Math.Abs(model.P(b).Y) < 1e-6, "with the length switched off the line keeps its new length and stays horizontal");
length.Disabled = false;
SketchSolver.Solve(model);
Check(Math.Abs(Length(model, a, b) - 50) < 1e-6, "switching the length back on pulls the line back to 50 mm");

// ---- it stays in the sketch: copies and saved files keep the state
length.Disabled = true;
Check(model.Clone().Constraints.Single(c => c.Type == CType.LineLength).Disabled, "a copy of the sketch keeps the switched-off state");
var json = System.Text.Json.JsonSerializer.Serialize(model);
Check(System.Text.Json.JsonSerializer.Deserialize<SketchModel>(json)!.Constraints.Single(c => c.Type == CType.LineLength).Disabled, "the switched-off state is saved with the sketch");
Check(!model.SameDefinition(SketchModelWithLengthOn()), "switching a constraint off counts as a change to the sketch");
SketchModel SketchModelWithLengthOn() { var m = model.Clone(); m.Constraints.Single(c => c.Type == CType.LineLength).Disabled = false; return m; }

// ---- the panel row and the toggle, with undo
var vm = new MainViewModel(); var s = vm.Session;
s.EnterSketch(Plane3.XY);
var sk = s.Sketch!;
sk.Edit(s, "Draw", () =>
{
    int p0 = sk.Model.AddPoint(0, 0), p1 = sk.Model.AddPoint(40, 0); int l = sk.Model.AddLine(p0, p1);
    sk.Model.Add(CType.Horizontal, 0, 0, false, l); sk.Model.Add(CType.LineLength, 40, 0, true, l);
});
var rows = vm.SketchConstraints.Where(r => r.IsDetail).ToList();
var dimension = rows.First(r => r.IsDimension);
Check(dimension.Active && dimension.RowOpacity == 1, "a constraint row starts switched on");
vm.ToggleConstraint(dimension);
var after = vm.SketchConstraints.First(r => r.Id == dimension.Id);
Check(!after.Active && after.RowOpacity < 1 && sk.Model.Constraints.Single(c => c.Id == dimension.Id).Disabled, "toggling a row switches the constraint off and greys the row");
Check(vm.SketchConstraints.Count(r => r.IsDetail) == rows.Count, "a switched-off constraint stays in the list");
s.Undo(); 
Check(!sk.Model.Constraints.Single(c => c.Id == dimension.Id).Disabled, "undo switches it back on");
vm.ToggleConstraint(vm.SketchConstraints.First(r => r.Id == dimension.Id));
vm.ToggleConstraint(vm.SketchConstraints.First(r => r.Id == dimension.Id));
Check(!sk.Model.Constraints.Single(c => c.Id == dimension.Id).Disabled, "toggling off and on again leaves it switched on");
if (Environment.GetEnvironmentVariable("PRISM_REVIEW_OUTPUT") is { Length: > 0 } review)
{
    Directory.CreateDirectory(review);
    var panel = new Prism.Views.SketchPanel { DataContext = vm }; var host = new Avalonia.Controls.Window { Width = 360, Height = 1250, Content = panel };
    host.Show(); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    vm.ToggleConstraint(vm.SketchConstraints.First(r => r.IsDetail && !r.IsDimension)); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    host.CaptureRenderedFrame()?.Save(Path.Combine(review, "constraint-toggle.png"));
}
s.ExitSketch(false);
Console.WriteLine($"SUCCESS: {checks} checks");

sealed class TestApp : Application { public override void Initialize() { Styles.Add(new FluentTheme()); Styles.Add(new StyleInclude(new Uri("avares://Prism/")) { Source = new Uri("avares://Prism/Styles/Theme.axaml") }); } }
static class Native { [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern bool SetDllDirectory(string path); public static void Initialize() => SetDllDirectory(Path.GetFullPath("native/bin")); }
