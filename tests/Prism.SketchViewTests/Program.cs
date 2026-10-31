using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.Themes.Fluent;
using Prism.Commands;
using Prism.Core;
using Prism.Core.Sketching;
using Prism.Kernel;
using Prism.ViewModels;

Native.Initialize();
AppBuilder.Configure<TestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia().SetupWithoutStarting();
int checks = 0;
void Check(bool ok, string message) { if (!ok) throw new Exception("FAIL " + message); Console.WriteLine("PASS " + message); checks++; }
var vm = new MainViewModel(); var s = vm.Session;
void Settle() { var until = DateTime.UtcNow.AddSeconds(3); while (s.IsAnimating && DateTime.UtcNow < until) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); } Dispatcher.UIThread.RunJobs(); }
double Facing(Plane3 p) => Vec3.Dot(s.Cameras[0].EyeDirection, p.Normal);

// a flat sketch: orthographic, looking straight at its plane
s.Ortho = false;
s.EnterSketch(Plane3.XZ); Settle();
Check(s.Ortho && s.Cameras[0].Orthographic, "starting a sketch switches to orthographic");
Check(Math.Abs(Math.Abs(Facing(Plane3.XZ)) - 1) < 1e-3, "starting a sketch looks straight at its plane");
s.Key("LookAt"); Settle();
double first = Facing(Plane3.XZ);
Check(Math.Abs(Math.Abs(first) - 1) < 1e-3, "N keeps looking straight at the plane");
s.Key("LookAt"); Settle();
Check(Facing(Plane3.XZ) * first < 0, "N again looks from the other side");
s.ExitSketch(false);
Check(!s.Ortho, "finishing the sketch puts the perspective view back");

// turning 3D sketching on switches to perspective; turning it off returns to a straight-on orthographic view
s.EnterSketch(Plane3.XY); Settle();
Check(s.Ortho, "a flat sketch is orthographic");
s.Sketch!.Set3DEnabled(s, true, adjustView: true); Settle();
Check(!s.Ortho && !s.Cameras[0].Orthographic, "enabling 3D sketching switches to perspective");
s.Sketch!.Set3DEnabled(s, false, adjustView: true); Settle();
Check(s.Ortho && Math.Abs(Math.Abs(Facing(Plane3.XY)) - 1) < 1e-3, "back to 2D: orthographic and looking at the plane");
// picking a drawing plane turns 3D on as a side effect and must leave the camera alone
s.Sketch!.Set3DEnabled(s, true); Settle();
Check(s.Ortho, "enabling 3D as a side effect of picking a plane does not change the projection");
s.Sketch!.Set3DEnabled(s, false); Settle();
s.ExitSketch(false);

// the 3D Sketch command starts in 3D with perspective and does not turn the view to face a plane
s.Ortho = true; s.Cameras[0].Orthographic = true;
s.StartCommand(() => new Sketch3DCommand()); s.AcceptCommandPanel(); Settle();
Check(s.Sketch != null && s.Sketch.Is3DEnabled && !s.Ortho, "3D Sketch starts in perspective");
s.ExitSketch(false);
Check(s.Ortho, "leaving a 3D sketch restores the projection from before it");

// the Create Sketch command looks normal by default
s.StartCommand(() => new CreateSketchCommand()); s.AcceptCommandPanel(); Settle();
Check(s.Sketch != null && !s.Sketch.Is3DEnabled && s.Ortho, "Create Sketch starts a flat orthographic sketch");
s.ExitSketch(false);
// a drawing tool's panel takes over the dock, and the sketch panel returns unchanged
s.EnterSketch(Plane3.XY); Settle();
s.Sketch!.Edit(s, "Draw", () => { int p0 = s.Sketch.Model.AddPoint(0, 0), p1 = s.Sketch.Model.AddPoint(30, 0); int l = s.Sketch.Model.AddLine(p0, p1); s.Sketch.Model.Add(CType.Horizontal, 0, 0, false, l); });
int rowsBefore = vm.SketchConstraints.Count, curvesBefore = s.Sketch.Model.Entities.Count;
Check(!vm.ToolPanelFocused, "the idle Select tool keeps the sketch panel (overview and constraints) in front");
s.Sketch.SetTool(s, new RectTool(RectMode.TwoCorner));
Check(vm.ToolPanelFocused && vm.ToolPanelTitle.Length > 0, "choosing the Rectangle tool brings its panel forward on its own");
vm.FinishTool();
Check(!vm.ToolPanelFocused && s.Sketch.Tool is SelectTool, "Done goes back to the sketch panel");
Check(vm.SketchConstraints.Count == rowsBefore && s.Sketch.Model.Entities.Count == curvesBefore, "the sketch and its constraints are as they were: nothing lost, no warning");
s.Sketch.SetTool(s, new LineTool());
Check(vm.ToolPanelFocused, "other drawing tools also take over the panel");
s.Sketch.SetTool(s, new SelectTool());
Check(!vm.ToolPanelFocused, "picking Select again returns to the sketch panel");
// pictures of the panels for review (PRISM_REVIEW_OUTPUT names the folder)
if (Environment.GetEnvironmentVariable("PRISM_REVIEW_OUTPUT") is { Length: > 0 } review)
{
    Directory.CreateDirectory(review);
    s.Sketch!.Set3DEnabled(s, true, adjustView: true); Settle();
    var sketchPanel = new Prism.Views.SketchPanel { DataContext = vm }; var host = new Avalonia.Controls.Window { Width = 360, Height = 900, Content = sketchPanel };
    host.Show(); Dispatcher.UIThread.RunJobs();
    host.CaptureRenderedFrame()?.Save(Path.Combine(review, "sketch-panel-3d.png"));
    var gizmoPanel = new Prism.Views.GizmoOptionsPanel { DataContext = vm }; var host2 = new Avalonia.Controls.Window { Width = 360, Height = 1100, Content = new Avalonia.Controls.ScrollViewer { Content = gizmoPanel } };
    host2.Show(); Dispatcher.UIThread.RunJobs();
    host2.CaptureRenderedFrame()?.Save(Path.Combine(review, "gizmo-panel.png"));
}
s.ExitSketch(false);
Console.WriteLine($"SUCCESS: {checks} checks");

sealed class TestApp : Application { public override void Initialize() { Styles.Add(new FluentTheme()); Styles.Add(new StyleInclude(new Uri("avares://Prism/")) { Source = new Uri("avares://Prism/Styles/Theme.axaml") }); } }
static class Native { [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern bool SetDllDirectory(string path); public static void Initialize() => SetDllDirectory(Path.GetFullPath("native/bin")); }
