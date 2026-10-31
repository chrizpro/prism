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
// the panel's Gizmo plane and Axis boxes follow what is done to the gizmo in the viewport
s.EnterSketch(Plane3.XY); Settle();
var live = s.Sketch!; live.Set3DEnabled(s, true); live.SetGizmoEditing(s, true);
live.SelectGizmoPlane(s, 1);
Check(vm.SketchGizmoPlane == 1, "choosing a gizmo plane in the viewport updates the panel's Gizmo plane box");
live.SelectGizmoPlane(s, 2);
Check(vm.SketchGizmoPlane == 2, "and again for the next plane");
live.SetGizmoEditing(s, false);
live.SetTool(s, new LineTool());
double handleLength = s.Camera.PixelSizeAt(live.GizmoFrame.Origin) * 96;
var yArrow = s.Camera.Project(live.GizmoFrame.Origin + Vec3.UnitY * handleLength * .5)!.Value;
s.PointerDown(new(yArrow.x, yArrow.y, true, false, false, false)); s.PointerUp(new(yArrow.x, yArrow.y, false, false, false, false));
Check(live.SelectedGizmoAxis == 1 && vm.SketchGizmoAxis == 1, "clicking the gizmo's Y arrow in the viewport updates the panel's Axis box");
var xArrow = s.Camera.Project(live.GizmoFrame.Origin + Vec3.UnitX * handleLength * .5)!.Value;
s.PointerDown(new(xArrow.x, xArrow.y, true, false, false, false)); s.PointerUp(new(xArrow.x, xArrow.y, false, false, false, false));
Check(live.SelectedGizmoAxis == 0 && vm.SketchGizmoAxis == 0, "and the X arrow too");
// the panel changes the gizmo as well
vm.SketchGizmoAxis = 2;
Check(live.SelectedGizmoAxis == 2, "picking an axis in the panel selects it on the gizmo");
s.ExitSketch(false);

// Edit Geometry: the first click binds the gizmo; Shift-click adds or removes points and the gizmo follows the average
s.EnterSketch(Plane3.XY); Settle();
var edit = s.Sketch!; edit.Set3DEnabled(s, true);
s.Camera.Distance = 120;
int pa = edit.Model.AddPoint(-30, 0), pb = edit.Model.AddPoint(30, 0); edit.Model.AddPointEntity(pa); edit.Model.AddPointEntity(pb); edit.Solve(s);
edit.SetTool(s, new SketchGeometryEditTool());
void ClickAt(double u, double v, bool shift) { var px = s.Camera.Project(edit.Plane.PointAt(u, v))!.Value; s.PointerDown(new(px.x, px.y, true, shift, false, false)); s.PointerUp(new(px.x, px.y, false, shift, false, false)); }
ClickAt(-30, 0, false);
Check(edit.GizmoTargetConfirmed && !edit.GizmoEditing && edit.SelPoints.Count == 1, "the first click in Edit Geometry binds the gizmo");
ClickAt(30, 0, false);
Check(edit.SelPoints.Count == 1, "a plain click on another point does not retarget a bound gizmo");
ClickAt(30, 0, true);
Check(edit.SelPoints.Count == 2 && edit.GizmoFrame.Origin.DistanceTo(edit.Plane.PointAt(0, 0)) < 1e-6 && edit.GizmoTargetConfirmed, "Shift-click adds the point and the gizmo moves to the average");
ClickAt(30, 0, true);
Check(edit.SelPoints.Count == 1 && edit.GizmoFrame.Origin.DistanceTo(edit.Plane.PointAt(-30, 0)) < 1e-6, "Shift-click on a bound point takes it out again");
s.ExitSketch(false);

// the tint over a closed shape follows the shape when its corners are dragged
s.EnterSketch(Plane3.XY); Settle();
var tint = s.Sketch!; s.Camera.Distance = 120;
var rect = new RectTool(RectMode.TwoCorner); tint.SetTool(s, rect); rect.Feed(s, -20, -10); rect.Feed(s, 20, 10);
tint.SetTool(s, new SelectTool());
double FillArea(out double minX, out double maxX)
{
    var overlay = new Overlay(); tint.BuildOverlay(s, overlay);
    var fill = overlay.Tris.Where(tr => tr.Color == 0x304C94FF).ToList();
    minX = fill.SelectMany(tr => new[] { tr.A.X, tr.B.X, tr.C.X }).DefaultIfEmpty(0).Min(); maxX = fill.SelectMany(tr => new[] { tr.A.X, tr.B.X, tr.C.X }).DefaultIfEmpty(0).Max();
    return fill.Sum(tr => Vec3.Cross(tr.B - tr.A, tr.C - tr.A).Length / 2);
}
double areaBefore = FillArea(out _, out _);
Check(Math.Abs(areaBefore - 800) < 1, "a closed rectangle is tinted: " + areaBefore);
var cornerPx = s.Camera.Project(tint.Plane.PointAt(20, 10))!.Value;
s.PointerDown(new(cornerPx.x, cornerPx.y, true, false, false, false));
s.PointerMove(new(cornerPx.x + 60, cornerPx.y, true, false, false, false));
s.PointerUp(new(cornerPx.x + 60, cornerPx.y, false, false, false, false));
var outline = tint.Model.Polylines(tint.Plane).SelectMany(x => x.pts).ToList();
double outlineMaxX = outline.Max(p => p.X), outlineMinX = outline.Min(p => p.X);
double areaAfter = FillArea(out double fillMinX, out double fillMaxX);
Check(outlineMaxX > 25, "dragging a corner moved the outline: max X " + outlineMaxX);
Check(Math.Abs(fillMaxX - outlineMaxX) < 1e-3 && Math.Abs(fillMinX - outlineMinX) < 1e-3, $"the tint follows the moved outline (fill {fillMinX:0.##}..{fillMaxX:0.##}, outline {outlineMinX:0.##}..{outlineMaxX:0.##})");
var farCorner = tint.Model.Points.OrderByDescending(pt => pt.X).First(); farCorner.Z = 15;
var liftedOverlay = new Overlay(); tint.BuildOverlay(s, liftedOverlay);
var liftedTint = liftedOverlay.Tris.Where(tr => tr.Color == 0x304C94FF).SelectMany(tr => new[] { tr.A, tr.B, tr.C }).ToList();
Check(liftedTint.Count > 0 && Math.Abs(liftedTint.Max(pt => pt.Z) - 15) < 1e-6, "a corner lifted off the plane lifts the tint with it, so the tint never stays behind");
farCorner.Z = 0;
s.ExitSketch(false);

// the same in Edit Geometry (3D sketch): corners dragged with the geometry tool
foreach (bool threeD in new[] { false, true })
{
    s.EnterSketch(Plane3.XY); Settle();
    var geo = s.Sketch!; s.Camera.Distance = 120; if (threeD) geo.Set3DEnabled(s, true);
    var r2 = new RectTool(RectMode.TwoCorner); geo.SetTool(s, r2); r2.Feed(s, -20, -10); r2.Feed(s, 20, 10);
    geo.SetTool(s, new SketchGeometryEditTool());
    var px = s.Camera.Project(geo.Plane.PointAt(20, 10))!.Value;
    s.PointerDown(new(px.x, px.y, true, false, false, false));
    s.PointerMove(new(px.x + 60, px.y, true, false, false, false));
    s.PointerUp(new(px.x + 60, px.y, false, false, false, false));
    var shapes = geo.DrawingPlanes.SelectMany(sec => sec.Model.Polylines(sec.Plane)).SelectMany(x => x.pts).ToList();
    var ov = new Overlay(); geo.BuildOverlay(s, ov);
    var tris = ov.Tris.Where(tr => tr.Color == 0x304C94FF).SelectMany(tr => new[] { tr.A, tr.B, tr.C }).ToList();
    double ox = shapes.Max(p => p.X);
    Check(tris.Count == 0 || Math.Abs(tris.Max(p => p.X) - ox) < 1e-3, $"{(threeD ? "3D" : "2D")} Edit Geometry: the tint follows the dragged corner (outline max X {ox:0.##}, tint {(tris.Count == 0 ? "none" : tris.Max(p => p.X).ToString("0.##"))})");
    s.ExitSketch(false);
}

// the gizmo panel: opening never unbinds, fields act on what the gizmo is bound to, and scale has three fields
s.EnterSketch(Plane3.XY); Settle();
var bound = s.Sketch!; bound.Set3DEnabled(s, true); s.Camera.Distance = 120;
int low = bound.Model.AddPoint(0, -20), high = bound.Model.AddPoint(0, 20); bound.Model.AddPointEntity(low); bound.Model.AddPointEntity(high); bound.Solve(s);
bound.SetTool(s, new SketchGeometryEditTool());
Check(!vm.ToolPanelFocused && vm.ToolSections.Count == 1 && vm.ToolSections[0].Title == "Sketch tool", "Edit Geometry keeps the sketch panel (Sketch tool: Box | Lasso) instead of opening its own panel");
bound.SelPoints.Add(low); bound.SelPoints.Add(high); bound.ConfirmGizmoTarget(s);
Check(bound.GizmoTargetConfirmed && !vm.GizmoPositioning, "two points are bound");
vm.OpenGizmoFromHotkey();
Check(!vm.GizmoPositioning && bound.GizmoTargetConfirmed && !bound.IndependentGizmoPlacement, "opening the gizmo panel never unbinds the gizmo");
Check(vm.GizmoScaleHeader.StartsWith("Scale the selection") && !vm.GizmoScaleZEnabled && vm.GizmoScaleZLabel.Contains("n/a"), "bound: Scale applies to the selection, and Z reads n/a on a flat sketch");
vm.GizmoScaleY = 0.5;
Check(Math.Abs(Math.Abs(bound.Model.P(high).Y - bound.Model.P(low).Y) - 20) < 1e-6 && Math.Abs(bound.Model.P(high).X) < 1e-9, "Scale Y 0.5 squeezes two points on a vertical line to half the distance and leaves X alone");
Check(Math.Abs(vm.GizmoScaleY - 1) < 1e-9, "the scale fields read 1 again afterwards (they are multipliers while bound)");
vm.GizmoOriginX = 10;
Check(!vm.GizmoPositioning && Math.Abs(bound.Model.P(high).X - 10) < 1e-6 && Math.Abs(bound.Model.P(low).X - 10) < 1e-6, "typing a location moves the bound points and does not unbind");
vm.BeginGizmoPlacement();
Check(vm.GizmoPositioning && vm.GizmoScaleHeader.StartsWith("Scale the plane"), "Unbind switches Scale to the plane's size");
vm.GizmoScaleX = 2; vm.GizmoScaleY = 1;
Check(Math.Abs(bound.GizmoSize - 2) < 1e-9 && Math.Abs(bound.GizmoStretch - .5) < 1e-9 && Math.Abs(bound.Model.P(high).X - 10) < 1e-6, "unbound, X and Y size the plane (width 2, height 1) and no geometry moves");
vm.CancelGizmoPlacement(); vm.CloseGizmoPanel();
bound.SetTool(s, new SketchGeometryEditTool(1));
Check(vm.ToolPanelFocused && vm.ToolPanelTitle.Contains("spline"), "Add points to a spline opens its own panel with Done");
vm.FinishTool();
Check(!vm.ToolPanelFocused, "Done returns to the sketch panel");
s.ExitSketch(false);

// an edge can be unbound just like a vertex
foreach (bool clickEdge in new[] { false, true })
{
    s.EnterSketch(Plane3.XY); Settle();
    var ub = s.Sketch!; ub.Set3DEnabled(s, true); s.Camera.Distance = 120;
    var rr = new RectTool(RectMode.TwoCorner); ub.SetTool(s, rr); rr.Feed(s, -20, -10); rr.Feed(s, 20, 10);
    ub.SetTool(s, new SketchGeometryEditTool());
    var spot = clickEdge ? ub.Plane.PointAt(0, 10) : ub.Plane.PointAt(20, 10);
    var pxu = s.Camera.Project(spot)!.Value;
    s.PointerDown(new(pxu.x, pxu.y, true, false, false, false)); s.PointerUp(new(pxu.x, pxu.y, false, false, false, false));
    string what = clickEdge ? "edge" : "vertex";
    Check(ub.GizmoTargetConfirmed && ub.HasGeometrySelection, $"clicking a {what} binds the gizmo");
    vm.OpenGizmoFromHotkey();
    Check(!vm.GizmoPositioning, $"{what}: opening the panel leaves it bound");
    vm.BeginGizmoPlacement();
    Check(vm.GizmoPositioning && ub.IndependentGizmoPlacement, $"{what}: Unbind detaches the gizmo (positioning {vm.GizmoPositioning})");
    // while unbound, dragging a gizmo arrow moves the gizmo and nothing else; Bind then re-attaches it
    var before = ub.Model.Points.Select(pt => (pt.X, pt.Y)).ToList();
    double len = s.Camera.PixelSizeAt(ub.GizmoFrame.Origin) * 96;
    var arrow = s.Camera.Project(ub.GizmoFrame.Origin + ub.GizmoFrame.U * len * .5)!.Value;
    var originBefore = ub.GizmoFrame.Origin;
    s.PointerDown(new(arrow.x, arrow.y, true, false, false, false)); s.PointerMove(new(arrow.x + 40, arrow.y, true, false, false, false)); s.PointerUp(new(arrow.x + 40, arrow.y, false, false, false, false));
    Check(ub.GizmoFrame.Origin.DistanceTo(originBefore) > 1 && ub.Model.Points.Select(pt => (pt.X, pt.Y)).SequenceEqual(before), $"{what}: dragging while unbound moves only the gizmo");
    vm.BindGizmoSelection();
    Check(!vm.GizmoPositioning && ub.GizmoTargetConfirmed, $"{what}: Bind re-attaches it");
    vm.CancelGizmoPlacement(); vm.CloseGizmoPanel();
    s.ExitSketch(false);
}

// other ways to let go of a bound gizmo behave the same for an edge as for a vertex
foreach (bool clickEdge in new[] { false, true })
{
    s.EnterSketch(Plane3.XY); Settle();
    var ex = s.Sketch!; ex.Set3DEnabled(s, true); s.Camera.Distance = 120;
    var rx = new RectTool(RectMode.TwoCorner); ex.SetTool(s, rx); rx.Feed(s, -20, -10); rx.Feed(s, 20, 10);
    ex.SetTool(s, new SketchGeometryEditTool());
    void Click(double u, double v) { var q = s.Camera.Project(ex.Plane.PointAt(u, v))!.Value; s.PointerDown(new(q.x, q.y, true, false, false, false)); s.PointerUp(new(q.x, q.y, false, false, false, false)); }
    string what = clickEdge ? "edge" : "vertex";
    if (clickEdge) Click(0, 10); else Click(20, 10);
    bool boundAtFirst = ex.GizmoTargetConfirmed;
    Click(0, -80);
    Console.WriteLine($"INFO {what}: bound after click {boundAtFirst}; after clicking empty space: selection {ex.HasGeometrySelection}, bound {ex.GizmoTargetConfirmed}");
    s.ExitSketch(false);
}

// Lasso is a method of the same Edit Geometry tool; an edge picked that way is bound, moves as a whole, and unbinds like any other
s.EnterSketch(Plane3.XY); Settle();
var las = s.Sketch!; las.Set3DEnabled(s, true); s.Camera.Distance = 120;
var rl = new RectTool(RectMode.TwoCorner); las.SetTool(s, rl); rl.Feed(s, -20, -10); rl.Feed(s, 20, 10);
las.SetTool(s, new SketchGeometryEditTool()); las.Tool!.SetOption(s, 1);
var edgePx = s.Camera.Project(las.Plane.PointAt(0, 10))!.Value;
s.PointerDown(new(edgePx.x, edgePx.y, true, false, false, false)); s.PointerUp(new(edgePx.x, edgePx.y, false, false, false, false));
Check(las.SelEntities.Count == 1 && las.GizmoTargetConfirmed, "Lasso mode: clicking an edge selects the whole edge and binds the gizmo");
double topBefore = las.Model.Points.Max(pt => pt.Y);
vm.GizmoOriginY = las.GizmoFrame.Origin.Y + 5;
Check(las.Model.Points.Max(pt => pt.Y) > topBefore + 4 && las.Model.Entities.Count(e => e.Type == EntType.Line) == 4, "Lasso mode: typing a location moves the whole edge (and the rectangle stays connected)");
vm.OpenGizmoFromHotkey(); vm.BeginGizmoPlacement();
Check(vm.GizmoPositioning, "Lasso mode: Unbind works on the edge");
vm.CancelGizmoPlacement(); vm.CloseGizmoPanel();
s.ExitSketch(false);

// pictures of the panels for review (PRISM_REVIEW_OUTPUT names the folder)
if (Environment.GetEnvironmentVariable("PRISM_REVIEW_OUTPUT") is { Length: > 0 } review)
{
    Directory.CreateDirectory(review);
    s.EnterSketch(Plane3.XY); s.Sketch!.Set3DEnabled(s, true, adjustView: true); Settle();
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
