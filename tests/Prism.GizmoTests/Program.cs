using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
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
using Prism.Core.Form;
using Prism.Kernel;
using Prism.ViewModels;
using Prism.Views;

NativeSearch.Initialize();
AppBuilder.Configure<TestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia().SetupWithoutStarting();
Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
var previous = AppSettings.Current.Gizmo;
int checks = 0;
void Check(bool valid, string description) { if (!valid) throw new Exception(description); checks++; Console.WriteLine("PASS " + description); }
bool Near(double a, double b) => Math.Abs(a - b) < 1e-5;
string output = Path.GetFullPath("docs/gizmo-workflows"); Directory.CreateDirectory(output);
void Capture(Control view, string name, int width, int height)
{
    var window = new Window { Width = width, Height = height, Content = view }; window.Show(); Dispatcher.UIThread.RunJobs();
    foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
    {
        Application.Current!.RequestedThemeVariant = theme; Dispatcher.UIThread.RunJobs();
        Check(GizmoVisual.Ink == (theme == ThemeVariant.Light ? 0xFF1B2432u : 0xFFE8EDF5u), "labels follow the active theme contrast");
        if (view is Border border) border.Background = Application.Current.TryFindResource("PanelBg", theme, out var resource) && resource is IBrush brush ? brush : Brushes.Transparent;
        using var image = new RenderTargetBitmap(new PixelSize(width, height)); image.Render(view);
        image.Save(Path.Combine(output, name + "-" + theme.Key.ToString()!.ToLowerInvariant() + ".png"));
    }
    window.Close(); Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
}
try
{
    AppSettings.Current.Gizmo = new(); var preferences = GizmoSettings.Current;
    Application.Current!.Resources["Accent"] = new SolidColorBrush(Color.Parse("#8556C8"));
    Check(GizmoVisual.Accent == 0xFF8556C8u, "handle highlights follow the selected preference accent");
    Application.Current.Resources.Remove("Accent");
    Check(preferences.Allows(0, 0) && !preferences.Allows(1, 0) && !preferences.Allows(2, 0), "default move mode hides rotation and scale");
    Check(JsonSerializer.Deserialize<AppSettings>("{}")!.Gizmo!.DisplayMode == 0, "older preferences receive the uncluttered default");
    var cameraSession = new CadSession();
    for (int mode = 0; mode < 3; mode++)
    {
        var overlay = new Overlay();
        for (int operation = 0; operation < 3; operation++) GizmoVisual.DrawOperation(overlay, cameraSession, Vec3.Zero, GizmoVisual.WorldAxes, operation, mode);
        Check(mode == 1 ? overlay.Tris.Count == 0 && overlay.Lines.Count == 216 : mode == 0 ? overlay.Lines.Count == 3 && overlay.Tris.Count == 24 : overlay.Lines.Count == 3 && overlay.Tris.Count == 8, "active operation " + mode + " uses the shared, distinct handle geometry");
        Capture(new Border { Background = Brushes.Transparent, Child = new GizmoPreview { Operation = mode } }, new[] { "move", "rotate", "scale" }[mode], 660, 440);
    }
    preferences.DisplayMode = 1;
    Check(Enumerable.Range(0, 3).All(op => preferences.Allows(op, 0)), "combined mode exposes all three operations");
    Capture(new Border { Background = Brushes.Transparent, Child = new GizmoPreview { Operation = 0 } }, "combined", 660, 440);
    preferences.DisplayMode = 2; preferences.Rotate = preferences.Scale = false;
    Check(preferences.Allows(0, 2) && !preferences.Allows(1, 2) && !preferences.Allows(2, 2), "custom visibility overrides the active operation");
    var roundTrip = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(AppSettings.Current))!.Gizmo!;
    Check(roundTrip.DisplayMode == 2 && roundTrip.Move && !roundTrip.Rotate && !roundTrip.Scale, "custom visibility serializes with application preferences");
    preferences.ScreenSize = double.NaN; preferences.HitTolerance = double.PositiveInfinity;
    Check(Near(preferences.Pixels, 96) && Near(preferences.Tolerance, 9), "invalid persisted display values use safe fallbacks");
    preferences.ScreenSize = 96; preferences.HitTolerance = 9;

    var vm = new MainViewModel(); var s = vm.Session; s.EnterSketch(Plane3.XY, alignView: false);
    var sketch = s.Sketch!; sketch.Set3DEnabled(s, true); sketch.GizmoFollowsSketch = false; sketch.SetGizmoEditing(s, true);
    preferences.DisplayMode = 0; preferences.Rotate = preferences.Scale = true;
    double length = GizmoVisual.Length(s, sketch.GizmoFrame.Origin);
    var center = s.Camera.Project(sketch.GizmoFrame.Origin)!.Value;
    var tip = s.Camera.Project(sketch.GizmoFrame.Origin + Vec3.UnitX * length)!.Value;
    Check(sketch.GizmoHit(s, tip.x, tip.y, true) == 1, "visible move arrow is pickable");
    double screenLength = Math.Sqrt(Math.Pow(tip.x - center.x, 2) + Math.Pow(tip.y - center.y, 2));
    s.Camera.Distance *= 2;
    var zoomTip = s.Camera.Project(Vec3.UnitX * GizmoVisual.Length(s, Vec3.Zero))!.Value; var zoomCenter = s.Camera.Project(Vec3.Zero)!.Value;
    Check(Near(screenLength, Math.Sqrt(Math.Pow(zoomTip.x - zoomCenter.x, 2) + Math.Pow(zoomTip.y - zoomCenter.y, 2))), "gizmo keeps its screen size while zooming");
    s.Camera.Distance /= 2;
    var uniform = s.Camera.Project(GizmoVisual.UniformPoint(s, Vec3.Zero, length))!.Value;
    Check(sketch.GizmoHit(s, uniform.x, uniform.y, true) != 10, "hidden scale marker cannot intercept a click");
    sketch.GizmoOperation = SketchGizmoOperation.Size;
    Check(sketch.GizmoHit(s, uniform.x, uniform.y, true) == 10, "scale mode exposes its visible uniform handle");
    sketch.GizmoOperation = SketchGizmoOperation.Rotate;
    var ringPoint = GizmoVisual.Ring(Vec3.Zero, Vec3.UnitX, Vec3.UnitY, length)
        .Select(p => s.Camera.Project(p)!.Value).First(p => sketch.GizmoHit(s, p.x, p.y, true) is >= 7 and <= 9);
    Check(sketch.GizmoHit(s, ringPoint.x, ringPoint.y, true) is >= 7 and <= 9, "rotation rings are pickable in rotate mode");
    sketch.GizmoOperation = SketchGizmoOperation.Move;
    Check(sketch.GizmoHit(s, ringPoint.x, ringPoint.y, true) is not (>= 7 and <= 9), "hidden rotation rings do not intercept move-mode clicks");
    preferences.DisplayMode = 2; preferences.Move = preferences.Rotate = preferences.Scale = false;
    Check(sketch.GizmoHit(s, center.x, center.y, true) == -1 && sketch.GizmoHit(s, tip.x, tip.y, true) == -1 && sketch.GizmoHit(s, uniform.x, uniform.y, true) == -1, "custom-hidden handles have no active hit targets");
    var hidden = GizmoVisual.Hit(s, tip.x, tip.y, _ => Vec3.Zero, GizmoVisual.WorldAxes, 0);
    Check(hidden.axis == -1 && !hidden.center, "freeform and control-point picking obey the same visibility mask");
    preferences.Move = preferences.Rotate = preferences.Scale = true; preferences.DisplayMode = 0;
    preferences.PlaneHandles = false;
    var planePoint = s.Camera.Project((Vec3.UnitX + Vec3.UnitY) * length * .28)!.Value;
    Check(sketch.GizmoHit(s, planePoint.x, planePoint.y, true) != 4, "hidden plane handles cannot intercept a click");
    preferences.PlaneHandles = true;

    preferences.Snap = true; preferences.MoveStep = 2; preferences.AngleStep = 15; preferences.ScaleStep = 10;
    Check(GizmoVisual.SnapMove(new Vec3(3.1, -2.9, .2)) == new Vec3(4, -2, 0), "translation snaps to the configured increment");
    Check(Near(GizmoVisual.SnapAngle(17 * Math.PI / 180), 15 * Math.PI / 180), "rotation snaps to the configured angle");
    Check(Near(GizmoVisual.SnapScale(1.24), 1.2), "scale snaps relative to 100 percent");
    Check(Near(GizmoVisual.SnapAngle(.31, true), .31) && Near(GizmoVisual.SnapScale(1.24, true), 1.24), "Alt bypasses angle and scale snapping");
    s.PointerDown(new(center.x, center.y, true, false, false, false));
    s.PointerMove(new(center.x + 40, center.y + 15, true, false, false, false));
    s.PointerUp(new(center.x + 40, center.y + 15, false, false, false, false));
    var moved = sketch.GizmoFrame.Origin;
    Check(moved.Length > 1 && new[] { moved.X, moved.Y, moved.Z }.All(v => Near(v / 2, Math.Round(v / 2))), "a real sketch gizmo drag uses snapped local coordinates");
    Check(sketch.TryUndo(s) && sketch.GizmoFrame == Plane3.XY, "snapped sketch drag is one undoable operation");
    sketch.GizmoOperation = SketchGizmoOperation.Rotate;
    s.PointerDown(new(ringPoint.x, ringPoint.y, true, false, false, false));
    s.PointerMove(new(ringPoint.x + 40, ringPoint.y, true, false, false, false));
    s.PointerUp(new(ringPoint.x + 40, ringPoint.y, false, false, false, false));
    var frame = sketch.GizmoFrame;
    double rotation = Math.Acos(Math.Clamp((Vec3.Dot(frame.U, Vec3.UnitX) + Vec3.Dot(frame.V, Vec3.UnitY) + Vec3.Dot(frame.Normal, Vec3.UnitZ) - 1) / 2, -1, 1)) * 180 / Math.PI;
    Check(Near(rotation, 30), "a real ring drag snaps to thirty degrees");
    Check(sketch.TryUndo(s) && sketch.GizmoFrame == Plane3.XY, "rotation drag restores cleanly with undo");
    sketch.GizmoOperation = SketchGizmoOperation.Size;
    s.PointerDown(new(uniform.x, uniform.y, true, false, false, false));
    s.PointerMove(new(uniform.x + 40, uniform.y - 10, true, false, false, false));
    s.PointerUp(new(uniform.x + 40, uniform.y - 10, false, false, false, false));
    Check(Near(sketch.GizmoSize, 1.3), "uniform scale drag snaps in ten-percent increments");
    Check(sketch.TryUndo(s) && Near(sketch.GizmoSize, 1), "uniform scale remains undoable");
    preferences.Snap = false;

    // Exercise the actual editor event paths, including mode switches from a combined handle.
    s.ExitSketch(false);
    var cage = s.AddObject(null, "Cage", ObjKind.Form, form: FormOps.Box(30, 30, 30, 1, 1, 1));
    s.SelectItems([SelItem.Of(cage)]); s.StartCommand(() => new EditFormCommand());
    var form = (EditFormCommand)s.ActiveCommand!; form.Mode.Index = 0; form.Verts.Add(0); form.Verts.Add(1); form.SelectionNotify();
    preferences.DisplayMode = 1;
    var pivot = form.PivotPoint(); double cageLength = GizmoVisual.Length(s, pivot);
    var formRing = GizmoVisual.Ring(pivot, Vec3.UnitX, Vec3.UnitY, cageLength).Select(p => s.Camera.Project(p)!.Value)
        .First(p => GizmoVisual.Hit(s, p.x, p.y, _ => pivot, GizmoVisual.WorldAxes, 0).operation == 1);
    form.Tool!.PointerDown(s, new(formRing.x, formRing.y, true, false, false, false));
    Check(form.TransformMode.Index == 1, "clicking a combined cage ring selects Rotate before dragging");
    form.Tool.PointerUp(s, new(formRing.x, formRing.y, false, false, false, false));
    Check(form.HasGizmoOptions, "freeform commands expose the shared options panel"); s.CancelCommand();
    var curve = s.AddObject(Occ.NurbsCurve([new(0, 0, 0), new(15, 0, 5), new(25, 10, 0), new(40, 0, 0)]), "Curve", ObjKind.Curve);
    s.SelectItems([SelItem.Of(curve)]); s.StartCommand(() => new EditPointsCommand());
    var edit = (EditPointsCommand)s.ActiveCommand!; var points = (EditPointsTool)edit.Tool!; points.Selected.Add(0); points.Selected.Add(1); edit.SelectionChanged();
    var pointCenter = points.GizmoCentre()!.Value; double pointLength = GizmoVisual.Length(s, pointCenter);
    var pointRing = GizmoVisual.Ring(pointCenter, Vec3.UnitX, Vec3.UnitY, pointLength).Select(p => s.Camera.Project(p)!.Value)
        .First(p => GizmoVisual.Hit(s, p.x, p.y, _ => pointCenter, GizmoVisual.WorldAxes, 0).operation == 1);
    points.PointerDown(s, new(pointRing.x, pointRing.y, true, false, false, false));
    Check(edit.TransformMode.Index == 1, "control-point editing uses the same combined-ring behavior");
    points.PointerUp(s, new(pointRing.x, pointRing.y, false, false, false, false)); s.CancelCommand();

    preferences.DisplayMode = 2; preferences.Scale = false; preferences.Snap = true;
    var options = new GizmoOptionsPanel { DataContext = vm };
    options.FindControl<Expander>("GizmoOptionsExpander")!.IsExpanded = true;
    Capture(new Border { Background = Brushes.Transparent, Child = options }, "advanced-options", 394, 1120);
    Check(options.GetVisualDescendants().OfType<GizmoPreview>().Count() == 1, "advanced options include the live shared-renderer preview");
    var panel = new CommandPanel { DataContext = vm }; s.SelectItems([SelItem.Of(curve)]); s.StartCommand(() => new EditPointsCommand());
    var host = new Window { Width = 394, Height = 900, Content = panel }; host.Show(); Dispatcher.UIThread.RunJobs();
    Check(panel.GetVisualDescendants().OfType<GizmoOptionsPanel>().Any(p => p.IsEffectivelyVisible), "control-point command embeds the common preference controls"); host.Close(); s.CancelCommand();
    Console.WriteLine($"SUCCESS: {checks} checks; screenshots in {output}");
}
finally { AppSettings.Current.Gizmo = previous; }
sealed class TestApp : Application
{
    public override void Initialize() { Styles.Add(new FluentTheme()); Styles.Add(new StyleInclude(new Uri("avares://Prism/")) { Source = new Uri("avares://Prism/Styles/Theme.axaml") }); }
}
static class NativeSearch
{
    [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern bool SetDllDirectory(string path);
    public static void Initialize() => SetDllDirectory(Path.GetFullPath("native/bin"));
}
