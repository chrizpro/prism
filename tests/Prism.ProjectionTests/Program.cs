using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Headless;
using Prism.Core;
using Prism.Core.Sketching;
using Prism.Kernel;
using Prism.ViewModels;

NativeSearch.Initialize();
AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
int checks = 0;
void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
bool Near(Vec3 a, Vec3 b) => a.DistanceTo(b) < 1e-6;
var session = new CadSession(); session.EnterSketch(Plane3.XZ);
foreach (var (z, radius) in new[] { (0.0, 10.0), (20.0, 6.0) })
{
    using var circle = Occ.Circle(new(0, 0, z), Vec3.UnitZ, radius);
    var intersections = ProjectTool.Intersections(circle, Plane3.XZ);
    Check(intersections.Count == 2 && intersections.All(p => p.Count == 1), "offset circle produces two exact pierce points");
    Check(intersections.Any(p => Near(p[0], new(radius, 0, z))) && intersections.Any(p => Near(p[0], new(-radius, 0, z))), "circle intersections lie on both profile and rail plane");
    Check(ProjectTool.Bring(session, session.Sketch!, intersections, "Intersection", true) == 2, "intersection points import as construction");
}
var sk = session.Sketch!;
Check(sk.Model.Entities.Count == 4 && sk.Model.Entities.All(e => e.Type == EntType.Point && e.Construction), "four rail anchors are construction point entities");
Check(sk.Result.Converged && sk.Result.FreePoints.Count == 0, "imported rail anchors are fixed");
var importedOverlay = new Overlay(); sk.BuildOverlay(session, importedOverlay);
Check(importedOverlay.Dots.Count >= 4, "rendering imported intersection points does not interpret fixed points as angular dimensions");
var snapPixel = session.Camera.Project(new Vec3(10, 0, 0))!.Value;
Check(sk.Snap(session, snapPixel.x, snapPixel.y)?.Kind == SnapKind.Point, "rail tools snap to imported intersections");
var anchor = sk.Model.Entities.First(e => Near(sk.Plane.PointAt(sk.Model.P(e.A).X, sk.Model.P(e.A).Y), new(10, 0, 0))).A;
var other = sk.Model.AddPoint(12, 25); sk.Model.AddLine(anchor, other); sk.Solve(session);
sk.Model.ShowConstruction = false;
Check(sk.PointVisible(anchor), "a hidden reference shared with a normal rail stays available as the rail endpoint");
Check(sk.Model.Entities.Where(e => e.Construction).All(e => !sk.EntityVisible(e)), "hidden construction entities are excluded from picking");
sk.Model.ShowConstruction = true;
Check(sk.TryUndo(session) && sk.Model.Entities.Count == 2, "one undo removes one profile's intersection import");
using (var tangent = Occ.Circle(new(0, 10, 0), Vec3.UnitZ, 10))
    Check(ProjectTool.Intersections(tangent, Plane3.XZ).Count == 1, "tangent circle gives one pierce point");
using (var miss = Occ.Circle(new(0, 20, 0), Vec3.UnitZ, 5))
    Check(ProjectTool.Intersections(miss, Plane3.XZ).Count == 0, "non-crossing circle gives no intersections");
using (var line = Occ.Line(new(2, -5, 3), new(2, 5, 3)))
    Check(Near(ProjectTool.Intersections(line, Plane3.XZ).Single().Single(), new(2, 0, 3)), "line-plane crossing is exact");
using (var box = Occ.Box(10, 10, 10))
    Check(ProjectTool.Intersections(box, new(new(0, 0, 5), Vec3.UnitX, Vec3.UnitY)).Count == 4, "body section preserves its four edges without extra endpoint entities");
var projections = new CadSession(); projections.EnterSketch(Plane3.XY);
var upper = new List<Vec3> { new(-5, 0, 2), new(0, 5, 2), new(5, 0, 2) };
var lower = new List<Vec3> { new(-5, 0, 2), new(0, -5, 2), new(5, 0, 2) };
Check(ProjectTool.Bring(projections, projections.Sketch!, [upper, lower, [], upper.AsEnumerable().Reverse().ToList()], "Projection") == 2, "different arcs sharing endpoints survive while reversed duplicates and empty inputs are skipped");
var vm = new MainViewModel();
var ecad = MainViewModel.WorkspaceDefs.Single(w => w.Id == "ecad");
Check(ecad.MainTab == "ECAD" && !MainViewModel.WorkspaceDefs.Single(w => w.Id == "design").Tabs.Contains("ECAD"), "ECAD has its own workspace outside Design");
vm.Session.EnterSketch(Plane3.XY);
var model = vm.Session.Sketch!.Model;
model.AddLine(model.AddPoint(4, 5), model.AddPoint(9, 5), true); vm.Session.NotifySketchChanged();
var node = vm.AllBrowserNodes().Single(n => n.Kind == NodeKind.Construction);
node.IsVisible = false;
Check(!model.ShowConstruction && !vm.Session.Sketch.PointVisible(model.Entities.Single().A), "tree eye hides construction geometry and its unshared points");
var hiddenOverlay = new Overlay(); vm.Session.Sketch.BuildOverlay(vm.Session, hiddenOverlay);
node = vm.AllBrowserNodes().Single(n => n.Kind == NodeKind.Construction); node.IsVisible = true;
var visibleOverlay = new Overlay(); vm.Session.Sketch.BuildOverlay(vm.Session, visibleOverlay);
Check(visibleOverlay.Lines.Count > hiddenOverlay.Lines.Count, "tree visibility controls construction rendering");
vm.Session.ExitSketch(true);
Check(vm.Session.Objects.Single().SketchData!.Entities.Single().Construction, "construction-only sketch survives Finish Sketch");
node = vm.AllBrowserNodes().Single(n => n.Kind == NodeKind.Construction); node.IsVisible = false;
var saved = vm.Session.Objects.Single();
Check(!saved.SketchData!.ShowConstruction && saved.SketchData.Clone().ShowConstruction == false, "finished sketch visibility uses cloned model state");
var roundtrip = JsonSerializer.Deserialize<SketchModel>(JsonSerializer.Serialize(saved.SketchData))!;
Check(!roundtrip.ShowConstruction, "construction visibility survives model serialization");
var visibleCopy = roundtrip.Clone(); visibleCopy.ShowConstruction = true;
Check(roundtrip.SameDefinition(visibleCopy), "visibility changes do not masquerade as edits in feature history");
visibleCopy.P(visibleCopy.Entities.Single().A).X += 1;
Check(!roundtrip.SameDefinition(visibleCopy), "actual geometry changes still mark a sketch definition edit");
Check(JsonSerializer.Deserialize<SketchModel>("{}")!.ShowConstruction, "older files default to visible construction");
var file = Path.Combine(Path.GetTempPath(), "prism-projection-" + Guid.NewGuid() + ".prism");
vm.Session.Save(file); var reopened = new CadSession(); reopened.Open(file); File.Delete(file);
Check(reopened.Objects.Single().SketchData is { ShowConstruction: false }, "reference-only sketch and visibility roundtrip through an Prism file");
var picking = new CadSession();
picking.AddObject(Occ.Circle(new(0, 0, 20), Vec3.UnitZ, 6), "Offset circle", ObjKind.Sketch);
picking.EnterSketch(Plane3.XZ); picking.Camera.Width = 1000; picking.Camera.Height = 800;
var tool = new ProjectTool(2); picking.Sketch!.SetTool(picking, tool); tool.PanelSet(picking, "construction", true);
var pixel = picking.Camera.Project(new Vec3(6, 0, 20))!.Value;
var pointer = new PointerInfo(pixel.x, pixel.y, true, false, false, false);
tool.PointerMove(picking, pointer); var preview = new Overlay(); tool.BuildOverlay(picking, preview);
Check(preview.Dots.Count == 2, "Intersect previews both crossing points from an outside sketch edge");
tool.PointerDown(picking, pointer);
Check(picking.Sketch.Model.Entities.Count == 2 && picking.Sketch.Model.Entities.All(e => e.Construction), "viewport click and As construction option import both rail anchors");
var internalSession=new CadSession();internalSession.EnterSketch(Plane3.XY,alignView:false);var internalSketch=internalSession.Sketch!;
var internalCircle=internalSketch.Model.AddCircle(internalSketch.Model.AddPoint(20,20),10);internalSketch.Solve(internalSession);
var sourceSection=internalSketch.DrawingPlanes[0];internalSketch.SetDrawingPlane(internalSession,new(new(0,20,0),Vec3.UnitX,Vec3.UnitZ),forceNew:true);
Check(ProjectTool.IntersectSketchEntity(internalSession,sourceSection,internalCircle,pointsOnly:true)==2,"Pierce points includes two circle crossings from another plane in the same sketch");
Check(internalSketch.Model.Entities.All(e=>e.Type==EntType.Point),"Pierce points does not import the flattened circle as a line");
var internalOverlay=new Overlay();internalSketch.BuildOverlay(internalSession,internalOverlay);
Check(internalSketch.Result.Converged && internalOverlay.Dots.Count>=2,"same-sketch pierce references solve and render without crashing");
var hiddenPoint = model.Entities.Single().A;
Check(model.ToShapes(Plane3.XY).Count == 0, "construction references stay out of feature profiles");
Console.WriteLine($"SUCCESS: {checks} checks");

static class NativeSearch
{
    [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern bool SetDllDirectory(string path);
    public static void Initialize() => SetDllDirectory(Path.GetFullPath("native/bin"));
}
