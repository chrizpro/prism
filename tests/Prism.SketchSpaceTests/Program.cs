using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Media;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Prism.Commands;
using Prism.Core;
using Prism.Core.Sketching;
using Prism.Kernel;
using Prism.ViewModels;
using Prism.Views;

AppBuilder.Configure<TestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia().SetupWithoutStarting();
Native.SetDllDirectory(Path.GetFullPath("native/bin"));
int checks = 0;
void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
var output = Path.GetFullPath("artifacts/sketch-space-tests"); Directory.CreateDirectory(output);
// Plane selection must never steal the drawing camera or intercept ordinary sketch clicks.
var navigation=new CadSession(); navigation.Camera.Yaw=.43;navigation.Camera.Pitch=.27;navigation.Camera.Roll=.18;
navigation.Camera.Target=new(13,24,35);navigation.Camera.Distance=321;navigation.Camera.Orthographic=true;
var camera=navigation.Camera;
var cameraState=(camera.Yaw,camera.Pitch,camera.Roll,camera.Target,camera.Distance,camera.Orthographic);
navigation.EnterSketch(Plane3.YZ);
Check(ReferenceEquals(camera,navigation.Camera)&&cameraState==(camera.Yaw,camera.Pitch,camera.Roll,camera.Target,camera.Distance,camera.Orthographic),"starting a sketch on another plane preserves camera orientation, framing and projection by default");
var navSketch=navigation.Sketch!;navSketch.Set3DEnabled(navigation,true);
double navLength=camera.PixelSizeAt(navSketch.GizmoFrame.Origin)*96;
var planeTarget=camera.Project(navSketch.GizmoFrame.Origin+(navSketch.GizmoFrame.U+navSketch.GizmoFrame.Normal)*navLength*.28)!.Value;
Check(navSketch.GizmoHit(navigation,planeTarget.x,planeTarget.y,false) is not (>=4 and <=6),"gizmo planes have no pick targets outside gizmo edit mode");
var initialPlane=navSketch.Plane;navigation.PointerDown(new(planeTarget.x,planeTarget.y,true,false,false,false));navigation.PointerUp(new(planeTarget.x,planeTarget.y,false,false,false,false));
Check(navSketch.Plane==initialPlane&&navSketch.ActivePlane==0,"clicking a gizmo plane while selecting geometry leaves the drawing plane alone");
navSketch.SelectGizmoPlane(navigation,1);navigation.Key("PlaneXY");
Check(navSketch.Plane==initialPlane,"panel plane selection and plane shortcuts require gizmo edit mode");
navSketch.SetGizmoEditing(navigation,true);navigation.PointerDown(new(planeTarget.x,planeTarget.y,true,false,false,false));navigation.PointerUp(new(planeTarget.x,planeTarget.y,false,false,false,false));
Check(navSketch.GizmoPlaneIndex==1,"the same plane target becomes selectable in gizmo edit mode");
Check(cameraState==(camera.Yaw,camera.Pitch,camera.Roll,camera.Target,camera.Distance,camera.Orthographic),"selecting a gizmo plane preserves the entire camera state");
Check(Prism.Rendering.GlViewport.DisplayGridPlane(navigation)==initialPlane,"the rendered grid stays on the entry plane when an empty sketch changes its root drawing plane");
navSketch.MoveDrawingPlane(navigation,new(40,50,60));navSketch.RotateDrawingPlane(navigation,new(20,30,40));navSketch.SetGizmoSize(navigation,2);
Check(Prism.Rendering.GlViewport.DisplayGridPlane(navigation)==initialPlane&&navigation.WorkPlane!=initialPlane,"gizmo translation, rotation and scale leave the reference grid fixed while drawing uses the active plane");
navSketch.TryUndo(navigation);navSketch.TryUndo(navigation);navSketch.TryUndo(navigation);
Check(Prism.Rendering.GlViewport.DisplayGridPlane(navigation)==initialPlane,"undo does not replace the grid anchor with an edited root plane");
var offsetGrid=new CadSession();var offsetGridPlane=new Plane3(new(12,34,56),Vec3.UnitY,Vec3.UnitZ);offsetGrid.EnterSketch(offsetGridPlane);
Check(Prism.Rendering.GlViewport.DisplayGridPlane(offsetGrid)==offsetGridPlane,"the grid anchor retains the selected plane's full origin and orientation");
offsetGrid.ExitSketch(false);offsetGrid.EnterSketch(Plane3.XZ);
Check(Prism.Rendering.GlViewport.DisplayGridPlane(offsetGrid)==Plane3.XZ,"a new sketch captures its own initial grid plane");
var createPlane=new CreateSketchCommand();
Check(createPlane.Params.OfType<BoolParam>().Any(p=>p.Label=="Look normal when starting"&&!p.Value),"Create Sketch makes initial camera alignment opt-in");

var s = new CadSession();
double yaw = s.Camera.Yaw, pitch = s.Camera.Pitch;
s.EnterSketch(Plane3.XY, alignView: false);
var sk = s.Sketch!;
sk.Set3DEnabled(s, true); sk.GizmoFollowsSketch = false;
Check(!s.Camera.Locked && s.Camera.Yaw == yaw && s.Camera.Pitch == pitch, "spatial entry preserves the camera and allows free orbit");
var rectangle = new RectTool(RectMode.TwoCorner); sk.SetTool(s, rectangle); rectangle.Feed(s, -20, -10); rectangle.Feed(s, 20, 10);
Check(sk.Model.Entities.Count == 4 && sk.Model.Constraints.Count(c => !c.Hidden) == 4, "unified rectangles keep the standard geometric constraints");
int line = sk.Model.Entities[0].Id;
Check(sk.Edit(s, "width", () => sk.Model.Add(CType.LineLength, 40, dimension: true, refs: line)), "driving rectangle dimensions solve in the unified workflow");
var firstDefinition = sk.Model.Clone();
s.Key("SketchGizmo"); s.Key("PlaneXZ");
Check(sk.DrawingPlanes.Count == 2 && sk.ActivePlane == 1 && sk.Plane.Normal.DistanceTo(-Vec3.UnitY) < 1e-8, "plane shortcut adds a spatial drawing plane without flattening previous curves");
sk.RotateDrawingPlane(s, new(0,0,30)); sk.MoveDrawingPlane(s, new(0,0,30));
var tilted = sk.Plane;
rectangle = new RectTool(RectMode.TwoCorner); sk.SetTool(s, rectangle); rectangle.Feed(s, 5,5); rectangle.Feed(s, 45,25);
Check(sk.DrawingPlanes[0].Model.SameDefinition(firstDefinition), "drawing on a rotated offset plane preserves the original constraints and geometry");
Check(sk.Model.ToShapes(sk.Plane).Count == 4 && sk.ExportModel().ToShapes(sk.RootPlane).Count == 8, "exact kernel geometry includes both constrained planes");
var beforeSize = sk.ExportModel(); sk.SetGizmoSize(s, 2);
Check(sk.ExportModel().SameDefinition(beforeSize) && sk.GizmoSize == 2, "gizmo scaling only changes display extent");
Check(sk.TryUndo(s) && sk.GizmoSize == 1 && sk.ActivePlane == 1, "display-size change is one undoable step");
sk.SelectPlane(s, 0);
Check(sk.Model.SameDefinition(firstDefinition), "returning to a plane restores its editable constraint system");
sk.SelectPlane(s, 1);
s.ExitSketch(true);
var obj = s.Objects.Single(o=>o.Kind == ObjKind.Sketch);
Check(obj.SketchData!.Sections.Count == 1 && obj.SketchPlane == Plane3.XY, "finishing from a secondary plane retains the root frame and one sketch identity");
string file = Path.Combine(output,"two-plane.prism"); s.Save(file);
Check(JsonDocument.Parse(File.ReadAllText(file)).RootElement.GetProperty("Version").GetInt32() >= 3, "multi-plane documents require version 3 or newer so older editors cannot silently discard secondary planes");
var opened = new CadSession(); opened.Open(file);
var saved = opened.Objects.Single(o=>o.Kind==ObjKind.Sketch);
Check(saved.Id == obj.Id && saved.SketchData!.Sections[0].Plane == tilted, "save and reopen preserve sketch identity and arbitrary plane frames");
Check(opened.EditSketch(saved) && opened.Sketch!.DrawingPlanes.Count == 2, "saved multi-plane sketch reopens in the same editor");
opened.Sketch!.SelectPlane(opened,1);
Check(opened.Sketch.Model.Entities.Count == 4 && opened.Sketch.Result.Converged, "secondary-plane curves remain constrained and editable after reopen");
var clone = saved.SketchData!.Clone(); clone.Sections[0].Model.ShowConstruction = !clone.Sections[0].Model.ShowConstruction;
Check(clone.SameDefinition(saved.SketchData), "secondary-plane display settings do not falsely change construction history");
opened.ExitSketch(false);
opened.SelectObjects([saved.Id]);
var extrude = new ExtrudeCommand(); opened.StartCommand(extrude);
extrude.Params.OfType<NumberParam>().Single(p=>p.Label.StartsWith("Region")).Value = 2;
var extrusion = extrude.TryBuild();
Check(extrusion?.Add.Count > 0 && Math.Abs(extrusion.Add[0].Shape!.Props().Center.Z - 45) < 1e-4, "a secondary-plane rectangle extrudes along its own normal rather than the root plane normal");
opened.CancelCommand();

var snapSession = new CadSession(); snapSession.EnterSketch(Plane3.XY,alignView:true); snapSession.Ortho = true;
var snap = snapSession.Sketch!;
var external = snapSession.AddObject(Occ.Line(new(30,0,0),new(80,0,0)),"External curve",ObjKind.Curve);
var screen = snapSession.Camera.Project(new(50,0,0))!.Value;
Check(snap.Snap(snapSession,screen.x,screen.y)!.Value.Kind == SnapKind.Grid, "cross-sketch snapping is optional and off by default");
snap.SnapOtherCurves = true;
Check(snap.Snap(snapSession,screen.x,screen.y)!.Value.Kind == SnapKind.External, "enabled snapping finds a curve in another object");
Check(snap.Snap(snapSession,screen.x,screen.y,allow:false)!.Value.Kind == SnapKind.Grid, "Alt bypass also disables external curve snapping");
snapSession.Update(external.Id,o=>o.WithVisible(false));
Check(snap.Snap(snapSession,screen.x,screen.y)!.Value.Kind == SnapKind.Grid, "hidden curves do not attract external snaps");
snapSession.AddObject(Occ.Line(new(30,0,20),new(80,0,20)),"Off plane curve",ObjKind.Curve);
Check(snap.Snap(snapSession,screen.x,screen.y)!.Value.Kind == SnapKind.Grid, "off-plane snaps are rejected unless projection is explicitly enabled");
snap.ProjectOtherCurves = true;
var projected = snap.Snap(snapSession,screen.x,screen.y)!.Value;
Check(projected.Kind == SnapKind.Projected && Math.Abs(projected.U-50)<1e-4 && Math.Abs(projected.V)<1e-4, "off-plane projection is explicit and lands on the active plane");
int point = snap.PointFor(projected);
Check(snap.Model.P(point).X > 49 && snap.Model.Constraints.Count == 1, "external snapshots never create dangling local constraint references");

// Exercise the actual session pointer route: Ctrl moves the drawing frame without drawing a curve.
var dragSession = new CadSession(); dragSession.EnterSketch(Plane3.XY,alignView:false);
var drag = dragSession.Sketch!; drag.Set3DEnabled(dragSession, true); drag.GizmoFollowsSketch = false; var center = dragSession.Camera.Project(drag.Plane.Origin)!.Value;
int count = drag.Model.Entities.Count;
dragSession.PointerDown(new(center.x,center.y,true,false,true,false,1));
dragSession.PointerMove(new(center.x+60,center.y+25,true,false,true,false,1));
dragSession.PointerUp(new(center.x+60,center.y+25,false,false,false,false,1));
Check(drag.Plane.Origin.Length > 1 && drag.Model.Entities.Count == count, "Ctrl-drag center moves the work plane through the session input route");
Check(drag.TryUndo(dragSession) && drag.Plane == Plane3.XY, "plane drag is a single undoable transaction");
dragSession.Key("SketchGizmo");
Check(drag.GizmoEditing, "G enters gizmo editing"); dragSession.Key("SketchEdit");
Check(drag.GizmoEditing && drag.GeometryEditing && drag.Tool is SketchGeometryEditTool, "E enters constrained geometry editing with target setup in 3D");
drag.SetGizmoEditing(dragSession,true); drag.GizmoOperation = SketchGizmoOperation.Rotate;
dragSession.PointerDown(new(center.x,center.y,true,false,true,false,1));
dragSession.PointerMove(new(center.x+30,center.y-20,true,false,true,false,1));
dragSession.PointerUp(new(center.x+30,center.y-20,false,false,false,false,1));
Check(drag.GizmoFrame.Normal.DistanceTo(Vec3.UnitZ) > .1 && Math.Abs(Vec3.Dot(drag.GizmoFrame.U,drag.GizmoFrame.V)) < 1e-9 && drag.Plane==Plane3.XY, "free rotation keeps an orthonormal drawing frame");
Check(drag.TryUndo(dragSession) && drag.GizmoFrame == Plane3.XY && drag.Plane == Plane3.XY, "free plane rotation can be undone");
drag.GizmoOperation = SketchGizmoOperation.Move;
dragSession.PointerDown(new(center.x,center.y,true,false,true,false,1));
dragSession.PointerMove(new(center.x+40,center.y+15,true,false,true,false,1));
dragSession.Key("Escape");
Check(drag.Plane == Plane3.XY, "Escape cancels an in-progress plane drag atomically");
dragSession.PointerUp(new(center.x+40,center.y+15,false,false,false,false,1));
drag.SetGizmoEditing(dragSession,false); var dragRect = new RectTool(RectMode.TwoCorner); drag.SetTool(dragSession,dragRect); dragRect.Feed(dragSession,10,10); dragRect.Feed(dragSession,30,30);
var unchanged = drag.ExportModel(); int planeCount = drag.DrawingPlanes.Count;
drag.SetGizmoEditing(dragSession,true); drag.GizmoOperation = SketchGizmoOperation.Size;
dragSession.PointerDown(new(center.x,center.y,true,false,true,false,1));
dragSession.PointerMove(new(center.x+80,center.y,true,false,true,false,1));
dragSession.PointerUp(new(center.x+80,center.y,false,false,false,false,1));
Check(drag.GizmoSize > 1 && drag.DrawingPlanes.Count == planeCount && unchanged.SameDefinition(drag.ExportModel()), "size-mode dragging neither creates a drawing plane nor changes existing geometry");
drag.GizmoOperation = SketchGizmoOperation.Move;
dragSession.PointerDown(new(center.x,center.y,true,false,true,false,1));
dragSession.PointerUp(new(center.x,center.y,false,false,false,false,1));
Check(drag.DrawingPlanes.Count == planeCount, "clicking a gizmo without dragging does not leave an empty plane behind");

var typedSession = new CadSession(); typedSession.EnterSketch(Plane3.XY);
var typedRect = new RectTool(RectMode.TwoCorner); typedSession.Sketch!.SetTool(typedSession,typedRect); typedRect.Feed(typedSession,0,0);
var opposite = typedSession.Camera.Project(new(40,20,0))!.Value;
typedRect.PointerMove(typedSession,new(opposite.x,opposite.y,false,false,false,false));
foreach (string key in new[]{"Char:4","Char:0","Tab","Char:2","Char:0","Enter"}) typedSession.Key(key);
var corners = typedSession.Sketch!.Model.Points.Where(p=>p.Id!=0).ToList();
Check(typedSession.Sketch.Model.Entities.Count == 4 && Math.Abs(corners.Max(p=>p.X)-40)<1e-6 && Math.Abs(corners.Max(p=>p.Y)-20)<1e-6, "typed width-Tab-height-Enter creates an exact 40 by 20 rectangle without finishing the sketch");
var curveNormalSession = new CadSession();
var circle = curveNormalSession.AddObject(Occ.Circle(new(10,20,30),new Vec3(1,1,1).Normalized(),15),"Tilted circle",ObjKind.Curve);
var curvePlane = curveNormalSession.PlaneOf(SelItem.Of(circle));
Check(curvePlane != null && Math.Abs(Vec3.Dot(curvePlane.Value.Normal,new Vec3(1,1,1).Normalized())) > .999, "Look normal resolves the plane of a standalone tilted curve");

var vm = new MainViewModel(); vm.Session.EnterSketch(Plane3.XY,alignView:false); vm.Sketch3DEnabled = true;
vm.SketchSpaceAction("rectangle"); var rt = (RectTool)vm.Session.Sketch!.Tool!; rt.Feed(vm.Session,-20,-10); rt.Feed(vm.Session,20,10);
vm.SketchSpaceAction("gizmo"); vm.SketchSpaceAction("xz"); vm.SketchOriginZ = "30"; vm.SketchSpaceAction("origin");
Check(vm.Session.Sketch.DrawingPlanes.Count == 2 && vm.Session.Sketch.Plane.Origin.Z == 30, "right-panel plane and origin actions match the session workflow");
vm.SketchSpaceAction("gizmo"); vm.SketchSpaceAction("edit"); vm.SketchSpaceAction("dimension");
Check(vm.Session.Sketch.Tool is DimensionTool, "right panel exposes the same editable dimension tool");
var keyWindow = new MainWindow { DataContext = vm };
keyWindow.Show();
keyWindow.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.G });
Check(vm.ShowGizmoPanel, "window key routing opens the Gizmo panel on G in a sketch");vm.CloseGizmoPanel();vm.Session.Key(SketchShortcuts.GizmoCommand);
keyWindow.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Z, KeyModifiers = KeyModifiers.Shift });
vm.Session.Key(SketchShortcuts.GizmoCommand);
Check(vm.Session.Sketch.GizmoDrawingPlane.Normal == Vec3.UnitZ && !vm.Session.Sketch.GizmoEditing, "Shift-Z selects the gizmo XY plane through the window key route and setup toggles off");
keyWindow.Close();
var followSession = new CadSession(); followSession.EnterSketch(Plane3.XY, alignView:false);
var follow = followSession.Sketch!; follow.Set3DEnabled(followSession,true);
var followRect = new RectTool(RectMode.TwoCorner); follow.SetTool(followSession,followRect);
followRect.Feed(followSession,10,15);
Check(follow.GizmoFollowsSketch && follow.GizmoFrame.Origin == new Vec3(10,15,0), "gizmo follows the first placed point by default");
Check(follow.Plane == Plane3.XY, "following a point does not rebase the geometry support plane");
followRect.Feed(followSession,50,35);
Check(follow.GizmoFrame.Origin == new Vec3(50,35,0) && follow.Model.Entities.Count == 4, "gizmo follows the completed rectangle without disturbing its corners");
follow.GizmoFollowsSketch = false;
follow.SetTool(followSession,new CircleTool(CircleMode.CenterRadius));
((ClickTool)follow.Tool!).Feed(followSession,80,60);
Check(follow.GizmoFrame.Origin == new Vec3(50,35,0), "turning off follow pins the gizmo while drawing continues");
follow.SetTool(followSession,new SelectTool());
var followDefinition = follow.Model.Clone();
follow.SetGizmoTransform(followSession,new(30,40,50),new(20,30,40));
Check(follow.GizmoFrame.Origin == new Vec3(30,40,50) && follow.GizmoRotationDegrees.DistanceTo(new(20,30,40)) < 1e-6, "gizmo exposes absolute location and rotation coordinates");
follow.SetGizmoSize(followSession,2.5);
follow.ResetGizmo(followSession,location:true,rotation:false,scale:false);
Check(follow.GizmoFrame.Origin == Vec3.Zero && follow.GizmoRotationDegrees.DistanceTo(new(20,30,40)) < 1e-6 && follow.GizmoSize == 2.5, "location reset zeros all axes while preserving rotation and scale");
follow.ResetGizmo(followSession,location:false,rotation:true,scale:false);
Check(follow.GizmoFrame.U == Vec3.UnitX && follow.GizmoFrame.V == Vec3.UnitY && follow.GizmoSize == 2.5, "rotation reset restores world axes without changing display scale");
follow.ResetGizmo(followSession,location:false,rotation:false,scale:true);
Check(follow.GizmoSize == 1 && follow.DrawingPlanes[0].Model.SameDefinition(followDefinition), "scale reset restores one and leaves earlier curves unchanged");
follow.SetGizmoTransform(followSession,new(15,25,30),new(10,20,30)); follow.SetGizmoSize(followSession,3);
follow.ResetGizmo(followSession);
Check(follow.GizmoFrame == Plane3.XY && follow.GizmoSize == 1, "reset all restores location zero, rotation zero and scale one");
Check(follow.TryUndo(followSession) && follow.GizmoFrame.Origin == new Vec3(15,25,30) && follow.GizmoSize == 3, "reset all is one undoable operation");
follow.SetGizmoEditing(followSession,true); follow.SelectGizmoPlane(followSession,1);
Check(Math.Abs(Vec3.Dot(follow.GizmoDrawingPlane.Normal,follow.GizmoFrame.V)) > .999, "selected XZ gizmo plane supplies the drawing normal");
follow.LookNormal(followSession); Thread.Sleep(300); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
Check(Math.Abs(Vec3.Dot(followSession.Camera.EyeDirection,follow.GizmoDrawingPlane.Normal)) > .999, "Look Normal aligns with the selected gizmo plane");

var scaleSession = new CadSession(); scaleSession.EnterSketch(Plane3.XY,alignView:false);
var scaleSketch = scaleSession.Sketch!; scaleSketch.Set3DEnabled(scaleSession,true);
double scaleLength = scaleSession.Camera.PixelSizeAt(scaleSketch.GizmoFrame.Origin) * 96;
var scaleHandle = scaleSession.Camera.Project(GizmoVisual.UniformPoint(scaleSession,scaleSketch.GizmoFrame.Origin,scaleLength))!.Value;
Check(scaleSketch.GizmoHit(scaleSession,scaleHandle.x,scaleHandle.y,false) != 10,"plane scale cannot intercept drawing outside gizmo edit mode");
scaleSketch.SetGizmoEditing(scaleSession,true);
scaleSketch.GizmoOperation=SketchGizmoOperation.Size;
Check(scaleSketch.GizmoHit(scaleSession,scaleHandle.x,scaleHandle.y,true) == 10, "plane scale has a visible directly draggable corner handle");
scaleSession.PointerDown(new(scaleHandle.x,scaleHandle.y,true,false,false,false));
scaleSession.PointerMove(new(scaleHandle.x+60,scaleHandle.y,true,false,false,false));
scaleSession.PointerUp(new(scaleHandle.x+60,scaleHandle.y,false,false,false,false));
Check(scaleSketch.GizmoSize > 1 && scaleSketch.Model.Entities.Count == 0, "dragging the scale handle enlarges planes without Ctrl or drawing geometry");

var chainSession = new CadSession(); chainSession.EnterSketch(Plane3.XY,alignView:false);
var chain = chainSession.Sketch!; chain.Set3DEnabled(chainSession,true); var chainTool = new LineTool(); chain.SetTool(chainSession,chainTool);
chainTool.Feed(chainSession,10,10); chainTool.Feed(chainSession,30,10);
chain.SetGizmoEditing(chainSession,true); chain.SelectGizmoPlane(chainSession,1); chainSession.Key("SketchGizmo");
Check(!chain.GizmoEditing&&chain.Tool is LineTool,"G resumes the active drawing tool after choosing a plane");
((ClickTool)chain.Tool!).Feed(chainSession,0,20);
Check(chain.ExportModel().ToShapes(chain.RootPlane).Count == 2, "a connected line continues from its last point after choosing another gizmo plane");

var inferenceSession=new CadSession();inferenceSession.EnterSketch(Plane3.XY,alignView:false);var inference=inferenceSession.Sketch!;inference.Set3DEnabled(inferenceSession,true);
var inferenceLine=new LineTool();inference.SetTool(inferenceSession,inferenceLine);inferenceLine.Feed(inferenceSession,10,10);
double inferredLength=inferenceSession.Camera.PixelSizeAt(inference.GizmoFrame.Origin)*280;
var inferredEnd=inference.GizmoFrame.Origin+Vec3.UnitZ*inferredLength;var inferredPixel=inferenceSession.Camera.Project(inferredEnd)!.Value;
inferenceSession.PointerMove(new(inferredPixel.x,inferredPixel.y,false,false,false,false));
Check(inferenceSession.Cursor3.DistanceTo(inferredEnd)<1e-5,"hovering a gizmo axis infers an exact 3D point along that direction");
inferenceSession.PointerDown(new(inferredPixel.x,inferredPixel.y,true,false,false,false));
Check(inference.Model.Entities.Count==1 && Math.Abs(Vec3.Dot(inference.Plane.Normal,Vec3.UnitY))>.999,"axis inference establishes a perpendicular plane only when the user places the point");
var inferredLine=inference.Model.Entities.Single();var inferredPoint=inference.Model.P(inferredLine.B);
Check(inference.Plane.PointAt(inferredPoint.X,inferredPoint.Y).DistanceTo(inferredEnd)<1e-5,"inferred drawing continues from the previous point without flattening the new endpoint");

// Saved datum frames and selection transforms exercise the same APIs as the inspector.
var datumSession=new CadSession();datumSession.EnterSketch(Plane3.XY,alignView:false);
var datumSketch=datumSession.Sketch!;datumSketch.Set3DEnabled(datumSession,true);
datumSketch.SetGizmoTransform(datumSession,new(15,25,35),new(20,30,40));datumSketch.SetGizmoSize(datumSession,2);
var savedPlane=datumSketch.SaveDatum(datumSession,SketchDatumKind.Plane,"Fixture plane");
var savedGizmo=datumSketch.SaveDatum(datumSession,SketchDatumKind.Gizmo,"Fixture gizmo");
var savedAxis=datumSketch.SaveDatum(datumSession,SketchDatumKind.Axis,"Fixture axis",1);
datumSketch.SaveDatum(datumSession,SketchDatumKind.Point,"Fixture point");
Check(datumSketch.Datums.Count==4 && datumSketch.ActiveDatumId==savedAxis.Id,"multiple datum kinds retain a single active reference");
datumSketch.ResetGizmo(datumSession);datumSketch.UseDatum(datumSession,savedPlane.Id);
Check(datumSketch.GizmoFrame==savedPlane.Frame && datumSketch.GizmoSize==2,"saved plane restores origin, orientation and plane display size");
datumSketch.UseDatum(datumSession,null);Check(datumSketch.ActiveDatumId==null,"live gizmo can be restored independently from saved datums");
datumSession.ExitSketch(true);
var datumFile=Path.Combine(output,"datums.prism");datumSession.Save(datumFile);var datumReload=new CadSession();datumReload.Open(datumFile);
Check(datumReload.Objects.Single().SketchData!.Datums.Count==4,"datum-only sketch survives Finish Sketch and document roundtrip");
Check(datumReload.EditSketch(datumReload.Objects.Single()) && datumReload.Sketch!.Datums.Any(d=>d.Name=="Fixture plane"),"saved plane can be reused after reopening the sketch");

var editSession=new CadSession();editSession.EnterSketch(Plane3.XY,alignView:false);var edit=editSession.Sketch!;edit.Set3DEnabled(editSession,true);
var editRect=new RectTool(RectMode.TwoCorner);edit.SetTool(editSession,editRect);editRect.Feed(editSession,10,10);editRect.Feed(editSession,50,30);
var originalRect=edit.ExportModel();edit.SetTool(editSession,new SketchGeometryEditTool());
foreach(var entity in edit.Model.Entities)edit.SelEntities.Add(entity.Id);
edit.ConfirmGizmoTarget(editSession);
edit.CenterGizmoOnSelection(editSession);var pivot=edit.GizmoFrame.Origin;
Check(pivot.DistanceTo(new(30,20,0))<1e-6,"Edit Sketch centers its live transform gizmo on the selected rectangle");
edit.SetGizmoEditing(editSession,true);editSession.Key("Enter");edit.SetGizmoTransform(editSession,pivot+new Vec3(0,0,20),new(0,90,0));
Check(edit.GeometryEditing && Math.Abs(Vec3.Dot(edit.Plane.Normal,Vec3.UnitX))>.999 && edit.Result.Converged,"the same gizmo translates and rotates an entire constrained shape in Edit Sketch");
Check(Prism.Rendering.GlViewport.DisplayGridPlane(editSession)==Plane3.XY,"transforming all root geometry to a different plane leaves the sketch grid anchored");
var movedWorld=edit.Model.Polylines(edit.Plane).SelectMany(poly=>poly.pts).ToList();
Check(movedWorld.All(point=>Math.Abs(point.X-30)<1e-6) && Math.Abs((movedWorld.Max(p=>p.Z)-movedWorld.Min(p=>p.Z))-40)<1e-6,"spatial edit preserves the rectangle dimensions in its new plane");
edit.SetGizmoSize(editSession,2);
Check(Math.Abs(edit.Model.Points.Max(p=>p.X)-100)<1e-6 && edit.Result.Converged,"Edit Sketch scale changes selected geometry rather than only the display planes");
Check(edit.TryUndo(editSession) && edit.TryUndo(editSession) && edit.ExportModel().SameDefinition(originalRect),"move, rotate and scale undo restores the original shape and constraints");

// Datum selection in edit mode changes the pivot, preserving the selected curves.
edit.SetTool(editSession,new SelectTool());edit.SetGizmoTransform(editSession,Vec3.Zero,Vec3.Zero);
var originPivot=edit.SaveDatum(editSession,SketchDatumKind.Gizmo,"Origin pivot");
edit.SetTool(editSession,new SketchGeometryEditTool());foreach(var entity in edit.Model.Entities)edit.SelEntities.Add(entity.Id);
edit.ConfirmGizmoTarget(editSession);
edit.CenterGizmoOnSelection(editSession);edit.UseDatum(editSession,originPivot.Id);
Check(edit.SelEntities.Count==4 && edit.GizmoFrame.Origin==Vec3.Zero,"choosing a saved datum in Edit Sketch preserves geometry selection and changes its pivot");
edit.SetGizmoTransform(editSession,new(0,0,5),Vec3.Zero);
Check(edit.Model.Polylines(edit.Plane).SelectMany(poly=>poly.pts).All(point=>Math.Abs(point.Z-5)<1e-6),"the selected saved datum drives spatial translation of the original selection");

// Exercise edit operations through the same pointer/key route as the viewport.
CadSession EditableRectangle(bool spatial=false)
{
 var session=new CadSession();session.EnterSketch(Plane3.XY,alignView:true);session.Ortho=true;
 var rectangleTool=new RectTool(RectMode.TwoCorner);session.Sketch!.SetTool(session,rectangleTool);
 rectangleTool.Feed(session,20,20);rectangleTool.Feed(session,100,70);
 if(spatial)session.Sketch.Set3DEnabled(session,true);
 session.Sketch.SetTool(session,new SketchGeometryEditTool());
 if(spatial)session.Sketch.SetGizmoEditing(session,false);
 return session;
}
void ClickWorld(CadSession session,Vec3 point,bool shift=false)
{
 var pixel=session.Camera.Project(point)!.Value;
 session.PointerDown(new(pixel.x,pixel.y,true,shift,false,false));session.PointerUp(new(pixel.x,pixel.y,false,shift,false,false));
}
void DragWorld(CadSession session,Vec3 from,Vec3 to)
{
 var a=session.Camera.Project(from)!.Value;var b=session.Camera.Project(to)!.Value;
 session.PointerDown(new(a.x,a.y,true,false,false,false));session.PointerMove(new(b.x,b.y,true,false,false,false));session.PointerUp(new(b.x,b.y,false,false,false,false));
}
var cornerSession=EditableRectangle();var cornerSketch=cornerSession.Sketch!;var cornerOriginal=cornerSketch.ExportModel();
int cornerId=cornerSketch.Model.Points.Single(p=>Math.Abs(p.X-100)<1e-6&&Math.Abs(p.Y-70)<1e-6).Id;
DragWorld(cornerSession,new(100,70,0),new(120,90,0));
Check(cornerSketch.Model.P(cornerId).X>119.99&&cornerSketch.Model.P(cornerId).Y>89.99&&cornerSketch.Result.Converged,"Edit Sketch exposes draggable rectangle corners and preserves their constraints");
Check(cornerSketch.Model.Points.Any(p=>Math.Abs(p.X-20)<1e-5&&Math.Abs(p.Y-20)<1e-5),"dragging a rectangle corner keeps its opposite corner anchored");
Check(cornerSketch.TryUndo(cornerSession)&&cornerSketch.ExportModel().SameDefinition(cornerOriginal),"corner dragging is one reversible edit");
var groupSession=EditableRectangle();var groupSketch=groupSession.Sketch!;var groupOriginal=groupSketch.ExportModel();
var groupPixels=groupSketch.Model.Points.Where(p=>p.Id!=0).Select(p=>groupSession.Camera.Project(groupSketch.Plane.PointAt(p.X,p.Y))!.Value).ToList();
double left=groupPixels.Min(p=>p.x)-14,right=groupPixels.Max(p=>p.x)+14,top=groupPixels.Min(p=>p.y)-14,bottom=groupPixels.Max(p=>p.y)+14;
groupSession.PointerDown(new(left,top,true,false,false,false));groupSession.PointerMove(new(right,bottom,true,false,false,false));groupSession.PointerUp(new(right,bottom,false,false,false,false));
Check(groupSketch.SelEntities.Count==4,"box selection in Edit Sketch selects all four rectangle sides");
DragWorld(groupSession,new(60,20,0),new(75,30,0));
var groupWorld=groupSketch.Model.Polylines(groupSketch.Plane).SelectMany(p=>p.pts).ToList();
Check(groupSketch.SelEntities.Count==4&&Math.Abs(groupWorld.Min(p=>p.X)-35)<1e-5&&Math.Abs(groupWorld.Max(p=>p.Y)-80)<1e-5,"dragging an edge preserves the box selection and translates the entire rectangle");
Check(groupSketch.TryUndo(groupSession)&&groupSketch.ExportModel().SameDefinition(groupOriginal),"moving a selected rectangle is one reversible edit");

var bindSession=EditableRectangle(true);var bind=bindSession.Sketch!;var bindOriginal=bind.ExportModel();
bindSession.Key("SketchGizmo");ClickWorld(bindSession,new(100,70,0));
Check(bind.SelPoints.Count==1&&bind.ExportModel().SameDefinition(bindOriginal),"selecting a target point in gizmo setup does not move geometry");
bindSession.Key("Enter");
Check(bindSession.Sketch==bind&&!bind.GizmoEditing&&bind.GizmoTargetConfirmed&&bind.GizmoFrame.Origin.DistanceTo(new(100,70,0))<1e-6,"Enter binds the gizmo to the selected corner and exits setup without finishing the sketch");
DragWorld(bindSession,new(100,70,0),new(110,80,0));
Check(bind.GizmoFrame.Origin.DistanceTo(new(110,80,0))<.01,"a gizmo locked to a corner follows subsequent direct point edits");
var beforeSetup=bind.ExportModel();bindSession.Key("SketchGizmo");bind.SetGizmoTransform(bindSession,new(45,55,65),new(15,25,35));bind.SetGizmoSize(bindSession,2);
Check(bind.ExportModel().SameDefinition(beforeSetup)&&bind.SelPoints.Count==1,"gizmo setup changes position, orientation and display size without transforming or clearing its target");
bindSession.Key("SketchGizmo");
Check(!bind.GizmoEditing&&bind.GizmoRotationDegrees.DistanceTo(new(15,25,35))<1e-6,"leaving setup retains the custom gizmo orientation for the next geometry transform");

var multiSession=EditableRectangle(true);var multi=multiSession.Sketch!;var multiOriginal=multi.ExportModel();
multiSession.Key("SketchGizmo");
foreach(var cornerTarget in new[]{new Vec3(20,20,0),new Vec3(100,20,0),new Vec3(100,70,0),new Vec3(20,70,0)})ClickWorld(multiSession,cornerTarget,true);
Check(multi.SelPoints.Count==4&&multi.ExportModel().SameDefinition(multiOriginal),"Shift-click chooses multiple corner targets in gizmo setup without editing them");
multi.SetGizmoTransform(multiSession,new(10,15,20),new(0,30,20));multi.SetGizmoSize(multiSession,2);multiSession.Key("Enter");
Check(multi.GizmoSize==2&&multi.GeometryScale==1,"gizmo display size and target geometry scale have independent identities");
Check(!multi.GizmoEditing&&multi.GizmoFrame.Origin.DistanceTo(new(60,45,0))<1e-6&&multi.GizmoRotationDegrees.DistanceTo(new(0,30,20))<1e-6,"Enter centers the gizmo on multiple selected points while preserving its chosen orientation");
var beforeMultiWorld=multi.Model.Polylines(multi.Plane).SelectMany(p=>p.pts).ToList();var multiDelta=multi.GizmoFrame.U*10;
multi.SetGizmoTransform(multiSession,multi.GizmoFrame.Origin+multiDelta,multi.GizmoRotationDegrees);
var afterMultiWorld=multi.Model.Polylines(multi.Plane).SelectMany(p=>p.pts).ToList();
Check(beforeMultiWorld.Zip(afterMultiWorld).All(p=>(p.First+multiDelta).DistanceTo(p.Second)<1e-5),"selecting all corners targets the complete shape for an out-of-plane translation");
double targetLength=multiSession.Camera.PixelSizeAt(multi.GizmoFrame.Origin)*96;
var arrowPixel=multiSession.Camera.Project(multi.GizmoFrame.Origin+multi.GizmoFrame.U*targetLength)!.Value;
var beforeArrow=multi.GizmoFrame.Origin;
multiSession.PointerDown(new(arrowPixel.x,arrowPixel.y,true,false,false,false));multiSession.PointerMove(new(arrowPixel.x+40,arrowPixel.y,true,false,false,false));multiSession.PointerUp(new(arrowPixel.x+40,arrowPixel.y,false,false,false,false));
Check(!multi.GizmoEditing&&multi.GizmoFrame.Origin.DistanceTo(beforeArrow)>1&&multi.Result.Converged,"outside gizmo setup, a handle drag transforms the bound geometry through the viewport route");
var beforeScaleWorld=multi.Model.Polylines(multi.Plane).SelectMany(p=>p.pts).ToList();double beforeSpan=beforeScaleWorld.Max(p=>p.X)-beforeScaleWorld.Min(p=>p.X);
multi.SetGizmoSize(multiSession,multi.GeometryScale*2);var afterScaleWorld=multi.Model.Polylines(multi.Plane).SelectMany(p=>p.pts).ToList();
Check(Math.Abs((afterScaleWorld.Max(p=>p.X)-afterScaleWorld.Min(p=>p.X))-beforeSpan*2)<1e-5,"outside setup, the scale control scales the bound rectangle rather than only its plane display");

multi.GizmoOperation=SketchGizmoOperation.Rotate;
var ringBasis=new[]{multi.GizmoFrame.U,multi.GizmoFrame.V,multi.GizmoFrame.Normal};
(double x,double y)? ringPixel=null;double ringLength=multiSession.Camera.PixelSizeAt(multi.GizmoFrame.Origin)*96;
for(int sample=0;sample<72&&ringPixel==null;sample++)
{
 var ringPoint=multi.GizmoFrame.Origin+(ringBasis[2]*Math.Cos(sample*Math.PI/36)+ringBasis[0]*Math.Sin(sample*Math.PI/36))*ringLength*.8;
 var px=multiSession.Camera.Project(ringPoint)!.Value;
 if(multi.GizmoHit(multiSession,px.x,px.y,false)==8&&multi.PickPoint(multiSession,px.x,px.y)<0)ringPixel=(px.x,px.y);
}
Check(ringPixel!=null,"bound geometry exposes an interactive rotation ring outside gizmo setup");
var normalBeforeRing=multi.Plane.Normal;double lengthBeforeRing=multi.Model.Polylines(multi.Plane).First().pts[0].DistanceTo(multi.Model.Polylines(multi.Plane).First().pts[1]);
var rp=ringPixel!.Value;multiSession.PointerDown(new(rp.x,rp.y,true,false,false,false));multiSession.PointerMove(new(rp.x+25,rp.y-5,true,false,false,false));multiSession.PointerUp(new(rp.x+25,rp.y-5,false,false,false,false));
double lengthAfterRing=multi.Model.Polylines(multi.Plane).First().pts[0].DistanceTo(multi.Model.Polylines(multi.Plane).First().pts[1]);
Check(multi.Plane.Normal.DistanceTo(normalBeforeRing)>.01&&Math.Abs(lengthAfterRing-lengthBeforeRing)<1e-5,"dragging the ring rotates bound geometry without changing its lengths");
double displayBeforeHandle=multi.GizmoSize,geometryScaleBeforeHandle=multi.GeometryScale;
multi.GizmoOperation=SketchGizmoOperation.Size;
var geometryScaleHandle=multiSession.Camera.Project(GizmoVisual.UniformPoint(multiSession,multi.GizmoFrame.Origin,multiSession.Camera.PixelSizeAt(multi.GizmoFrame.Origin)*96))!.Value;
multiSession.PointerDown(new(geometryScaleHandle.x,geometryScaleHandle.y,true,false,false,false));multiSession.PointerMove(new(geometryScaleHandle.x+40,geometryScaleHandle.y-10,true,false,false,false));multiSession.PointerUp(new(geometryScaleHandle.x+40,geometryScaleHandle.y-10,false,false,false,false));
Check(multi.GeometryScale>geometryScaleBeforeHandle&&multi.GizmoSize==displayBeforeHandle,"dragging the scale handle scales bound geometry while retaining the gizmo display size");

var scaledDefinition=multi.ExportModel();var nextTargetPoint=multi.Model.Points.First(p=>p.Id!=0);
multi.SetGizmoEditing(multiSession,true);
ClickWorld(multiSession,multi.Plane.PointAt(nextTargetPoint.X,nextTargetPoint.Y));
multi.ConfirmGizmoTarget(multiSession);
Check(multi.GeometryScale==1&&multi.ExportModel().SameDefinition(scaledDefinition),"choosing a new geometry target starts its scale at one without resizing it");

var deleteSession=EditableRectangle(true);foreach(var entity in deleteSession.Sketch!.Model.Entities)deleteSession.Sketch.SelEntities.Add(entity.Id);
deleteSession.Key("SketchGizmo");deleteSession.Key("Enter");deleteSession.Key("Delete");Check(deleteSession.Sketch.Model.Entities.Count==0&&!deleteSession.Sketch.GizmoTargetConfirmed,"Delete key still removes selected sketch geometry after ribbon cleanup");
var graphicsVm=new MainViewModel{HomeOpen=false};graphicsVm.Session.EnterSketch(Plane3.XY);
var sketchRibbon=graphicsVm.Tabs.Single(t=>t.Title=="Sketch");var mainSketchButtons=sketchRibbon.Groups.SelectMany(g=>g.Items).OfType<Prism.Ribbon.RibbonButton>().ToList();
var datumDropdown=mainSketchButtons.Single(b=>b.Menu?.Any(item=>item.Label=="Save gizmo")==true);
Check(datumDropdown.Menu!.Select(b=>b.Label).SequenceEqual(new[]{"Gizmo","Point","Axis","Plane","Save gizmo"}),"Datum groups gizmo options with sketch point, axis and saved references");
Check(!sketchRibbon.Buttons.Any(b=>b.Label is "Save plane" or "Grid Snap" or "Delete"),"Save Plane, Grid Snap and Delete are absent from the sketch ribbon");
Check(!graphicsVm.Tabs.SelectMany(t=>t.Buttons).Any(b=>b.Label=="Delete"),"modeling ribbon also leaves Delete to the keyboard and contextual menus");
Check(!SketchContext.ShowCurvature,"curvature comb starts disabled");
Check(graphicsVm.Tabs.SelectMany(t=>t.Buttons).Where(b=>b.Label=="Hole").All(b=>b.Icon=="hole"),"mechanical Hole uses the modeling palette icon rather than the PCB icon");

foreach(var mode in new[]{EllipseMode.Center,EllipseMode.TwoPoint,EllipseMode.ThreePoint})
{
 var es=new CadSession();es.EnterSketch(Plane3.XY);var et=new EllipseTool(mode,circleLock:true);es.Sketch!.SetTool(es,et);
 if(mode==EllipseMode.Center){et.Feed(es,20,20);et.Feed(es,30,20);}
 else if(mode==EllipseMode.TwoPoint){et.Feed(es,10,20);et.Feed(es,30,20);}
 else{et.Feed(es,10,20);et.Feed(es,20,30);et.Feed(es,30,20);}
 Check(es.Sketch.Model.Entities.Single() is {Type:EntType.Circle,R:10},$"one ellipse tool creates an exact circle using {mode}");
 Check(es.Sketch.Model.P(es.Sketch.Model.Entities.Single().A).X==20,"circle construction method produces the correct center");
}
var offsetSession=new CadSession();offsetSession.EnterSketch(Plane3.XY);var offsetSketch=offsetSession.Sketch!;
int oa=offsetSketch.Model.AddLine(offsetSketch.Model.AddPoint(10,10),offsetSketch.Model.AddPoint(50,10));
int ob=offsetSketch.Model.AddLine(offsetSketch.Model.AddPoint(10,30),offsetSketch.Model.AddPoint(50,30));offsetSketch.Solve(offsetSession);
var offsetTool=new OffsetTool();offsetSketch.SetTool(offsetSession,offsetTool);offsetTool.PanelSet(offsetSession,"chain",false);
foreach(var (y,shift) in new[]{(10d,false),(30d,true)})
{var pixel=offsetSession.Camera.Project(new(25,y,0))!.Value;offsetTool.PointerDown(offsetSession,new(pixel.x,pixel.y,true,shift,false,false));}
Check(offsetSketch.SelEntities.SetEquals(new[]{oa,ob}) && offsetSketch.Model.Entities.Count==2,"Offset Shift-click accumulates highlighted sources without creating geometry");
offsetTool.PanelSet(offsetSession,"dist",3d);offsetTool.PanelSet(offsetSession,"apply",null);
Check(offsetSketch.Model.Entities.Count==2,"Offset refuses to apply while source selection is still active");
offsetTool.Key(offsetSession,"Enter");offsetTool.PanelSet(offsetSession,"dist",3d);offsetTool.PanelSet(offsetSession,"apply",null);
Check(offsetSketch.Model.Entities.Count==4 && offsetSketch.Result.Converged,"Offset applies to both selected lines after explicit selection completion");
Check(offsetSketch.TryUndo(offsetSession)&&offsetSketch.Model.Entities.Count==2,"a multi-curve offset is one undoable edit");

var dockVm = new MainViewModel(); dockVm.Session.EnterSketch(Plane3.XY); var dockSketch = dockVm.Session.Sketch;
dockVm.OpenRightPanel("references"); dockVm.OpenRightPanel("parameters");
Check(dockVm.RightPanelTabs.Count == 4 && ReferenceEquals(dockVm.Session.Sketch,dockSketch), "multiple inspector tabs can open without leaving the sketch");
dockVm.SelectRightPanel("references"); Check(dockVm.ShowReferencesStudio && !dockVm.ShowSketchPanel, "selecting an inspector tab shows its own panel");
dockVm.SelectRightPanel("context"); Check(dockVm.ShowSketchPanel && ReferenceEquals(dockVm.Session.Sketch,dockSketch), "the Sketch tab restores the same active sketch");
dockVm.SetRightPanelEnabled("references",false);
Check(dockVm.RightPanelTabs.Count == 3 && dockVm.IsRightPanelOpen("parameters"), "unchecking one panel preserves other selected tabs");
var spacePanel = new SketchSpacePanel { DataContext = vm };
Check(!Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(spacePanel).OfType<Button>().Any(b => b.Content?.ToString() is "Line (L)" or "Circle (C)" or "Rectangle (R)" or "Spline (S)"), "3D options contain no duplicate sketch drawing tools");
var window = new Window { Width = 340, Height = 1100, Background = SolidColorBrush.Parse("#1A1F27"), Content = new SketchPanel { DataContext = vm } };
window.Show(); window.UpdateLayout();
using (var bitmap = new RenderTargetBitmap(new PixelSize(340,1100))) { bitmap.Render(window); bitmap.Save(Path.Combine(output,"sketch-panel.png")); }
window.Close();
Console.WriteLine($"SUCCESS: {checks} checks; artifacts in {output}");

sealed class TestApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://Prism/")) { Source = new Uri("avares://Prism/Styles/Theme.axaml") });
        RequestedThemeVariant = ThemeVariant.Dark;
    }
}
static class Native { [DllImport("kernel32",CharSet=CharSet.Unicode)] public static extern bool SetDllDirectory(string path); }

