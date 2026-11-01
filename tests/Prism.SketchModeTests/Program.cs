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
using Prism.Core;
using Prism.Core.Sketching;
using Prism.Kernel;
using Prism.ViewModels;
using Prism.Views;

NativeSearch.Initialize();
AppBuilder.Configure<TestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia().SetupWithoutStarting();
int checks = 0;
void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
var vm = new MainViewModel(); vm.Session.EnterSketch(Plane3.XY, alignView: false);
var sketch = vm.Session.Sketch!;
var panel = new SketchPanel { DataContext = vm }; var host = new Window { Width = 340, Height = 900, Content = panel };
host.Show(); Dispatcher.UIThread.RunJobs();
var spatial = panel.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "SpatialSketchControls");
var checkbox = panel.GetVisualDescendants().OfType<CheckBox>().Single(p => p.Name == "Enable3D");
Check(!sketch.Is3DEnabled && checkbox.IsChecked == false && !spatial.IsVisible, "new sketches start in a clean 2D mode");
vm.Session.Key("SketchGizmo"); vm.Session.Key("PlaneXZ");
Check(!sketch.Is3DEnabled && !sketch.GizmoEditing && sketch.Plane == Plane3.XY, "spatial shortcuts cannot bypass the 3D checkbox");
var overlay2D = new Overlay(); sketch.BuildOverlay(vm.Session, overlay2D);
Check(!overlay2D.Labels.Any(l => l.Text is "X" or "Y" or "Z" or "N"), "2D mode does not draw spatial gizmo axes");
var output = Path.GetFullPath("docs/sketch-mode"); Directory.CreateDirectory(output);
foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
{
    Application.Current!.RequestedThemeVariant = theme; Dispatcher.UIThread.RunJobs();
    using var bitmap = new RenderTargetBitmap(new PixelSize(340, 900)); bitmap.Render(panel); bitmap.Save(Path.Combine(output, "2d-" + theme.Key.ToString()!.ToLowerInvariant() + ".png"));
}
checkbox.IsChecked = true; Dispatcher.UIThread.RunJobs();
Check(sketch.Is3DEnabled && vm.Sketch3DEnabled && spatial.IsVisible, "checking 3D transitions the session and reveals advanced controls");
vm.Session.Key("SketchGizmo"); Check(sketch.GizmoEditing, "spatial keyboard controls work after opting in");
var overlay3D = new Overlay(); sketch.BuildOverlay(vm.Session, overlay3D);
Check(overlay3D.Tris.Count >= 24 && overlay3D.Lines.Count > overlay2D.Lines.Count, "3D mode adds the plane and active move arrows without inactive rotation rings");
foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
{
    Application.Current!.RequestedThemeVariant = theme; Dispatcher.UIThread.RunJobs();
    using var bitmap = new RenderTargetBitmap(new PixelSize(340, 900)); bitmap.Render(panel); bitmap.Save(Path.Combine(output, "3d-" + theme.Key.ToString()!.ToLowerInvariant() + ".png"));
}
sketch.SetDrawingPlane(vm.Session, Plane3.XZ, forceNew: true); var count = sketch.DrawingPlanes.Count;
vm.SketchDrawFlat = true; Dispatcher.UIThread.RunJobs();
Check(sketch.Is3DEnabled && sketch.IsDrawingFlat && !sketch.GizmoEditing && sketch.DrawingPlanes.Count == count && !spatial.IsVisible, "returning to 2D hides controls without deleting other drawing planes");
Check(!panel.GetVisualDescendants().OfType<CheckBox>().Single(p=>p.Name=="Free3DEditing").IsVisible,"free depth editing is hidden while drawing flat");
var flatMode=sketch.Is3DEnabled;vm.Session.Camera.Yaw+=.4;vm.Session.Camera.Orthographic=false;
Check(sketch.Is3DEnabled==flatMode,"orbiting and perspective changes do not change drawing mode");
vm.Sketch3DEnabled=true;vm.SketchFreeEditing=true;var freeSaved=sketch.ExportModel();
Check(new SketchContext(sketch.RootPlane,freeSaved).FreeEditing,"free editing choice survives reopening");
vm.SketchDrawFlat=true;
var exported = sketch.ExportModel(); var restored = new SketchContext(sketch.RootPlane, exported);
Check(restored.Is3DEnabled && restored.IsDrawingFlat && restored.DrawingPlanes.Count == count, "saved mode and multi-plane geometry round-trip through sketch definitions");
exported.SpatialEditing = null;
Check(new SketchContext(sketch.RootPlane, exported).Is3DEnabled, "legacy multi-plane sketches reopen with spatial editing enabled");
var plain = new SketchModel(); var modeOnly = plain.Clone(); modeOnly.SpatialEditing = true;
Check(plain.SameDefinition(modeOnly), "changing display mode alone does not alter the geometric definition");
host.Close(); vm.Session.ExitSketch(false);

var main = new MainView { DataContext = vm }; var mainHost = new Window { Width = 1000, Height = 700, Content = main };
mainHost.Show(); Dispatcher.UIThread.RunJobs();
Check(!main.GetVisualDescendants().OfType<Button>().Any(b => Equals(b.Tag, "diagnostics")), "far-right panel selector buttons have been removed");
var picker = main.FindControl<Button>("PanelPicker")!;
picker.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
var flyout = (MenuFlyout)Avalonia.Controls.Primitives.FlyoutBase.GetAttachedFlyout(picker)!;
var items = flyout.ItemsSource!.Cast<MenuItem>().ToList();
Check(items.Count == 6 && items.Single(i => i.Header!.ToString()!.EndsWith("Properties")).IsEnabled == false, "panel menu keeps Properties enabled and locked");
items.Single(i => i.Header!.ToString()!.EndsWith("Diagnostics")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Dispatcher.UIThread.RunJobs();
items.Single(i => i.Header!.ToString()!.EndsWith("References")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Dispatcher.UIThread.RunJobs();
Check(vm.IsRightPanelOpen("diagnostics") && vm.IsRightPanelOpen("references"), "panel menu supports multiple enabled panels");
vm.SetRightPanelEnabled("properties", false);
Check(vm.IsRightPanelOpen("properties"), "Properties cannot be disabled while the context changes");
vm.SelectRightPanel("diagnostics"); flyout.Hide(); Dispatcher.UIThread.RunJobs();
Check(vm.ShowDiagnosticsStudio && main.FindControl<Grid>("WorkGrid")!.ColumnDefinitions[4].Width.Value == 394, "panel picker opens the chosen panel at full content width");
var probeSession=new CadSession();probeSession.EnterSketch(Plane3.XY);var editProbe=probeSession.Sketch!;
editProbe.Set3DEnabled(probeSession,true);editProbe.SetTool(probeSession,new SketchGeometryEditTool());
Check(editProbe.GizmoEditing&&editProbe.GeometryEditing,"3D Edit Geometry always begins in gizmo setup");
var setupDefinition=editProbe.ExportModel();editProbe.RotateDrawingPlane(probeSession,new(15,25,35));
Check(editProbe.ExportModel().SameDefinition(setupDefinition),"setup orientation leaves geometry untouched");
probeSession.Key("SketchGizmo");Check(!editProbe.GizmoEditing&&editProbe.GeometryEditing,"G manually exits setup while retaining geometry edit mode");
var cleanGizmo=new Overlay();editProbe.BuildOverlay(probeSession,cleanGizmo);
Check(!cleanGizmo.Labels.Any(l=>l.Text is "XY" or "XZ" or "YZ" or "X" or "Y" or "Z" || l.Text.StartsWith("Scale plane") || l.Text.StartsWith("Scale selection")),"gizmo overlay has no axis, plane or transform labels");
editProbe.Set3DEnabled(probeSession,false);editProbe.SetTool(probeSession,new SketchGeometryEditTool());
Check(!editProbe.GizmoEditing&&editProbe.GeometryEditing,"2D geometry editing starts directly without gizmo setup");
// Persistent mode buttons, live target centers and preservation without Enter.
editProbe.ToggleGeometryEditing(probeSession);Check(!editProbe.GeometryEditing,"Edit Geometry toggles off explicitly");
editProbe.Set3DEnabled(probeSession,true);editProbe.ToggleGeometryEditing(probeSession);
Check(editProbe.GeometryEditing&&editProbe.GizmoEditing,"Edit Geometry toggles on and starts gizmo setup");
int firstTarget=editProbe.Model.AddPoint(20,20),secondTarget=editProbe.Model.AddPoint(60,40);
editProbe.Model.AddPointEntity(firstTarget);editProbe.Model.AddPointEntity(secondTarget);
int targetCurve=editProbe.Model.AddSpline([firstTarget,secondTarget],false,false,false);editProbe.Solve(probeSession);
void TargetPoint(double u,double v,bool shift)
{
 var px=probeSession.Camera.Project(editProbe.Plane.PointAt(u,v))!.Value;
 probeSession.PointerDown(new(px.x,px.y,true,shift,false,false));probeSession.PointerUp(new(px.x,px.y,false,shift,false,false));
}
TargetPoint(20,20,false);Check(!editProbe.GizmoEditing&&editProbe.GizmoTargetConfirmed&&editProbe.GizmoFrame.Origin.DistanceTo(editProbe.Plane.PointAt(20,20))<1e-6,"clicking a point in Edit Geometry binds the gizmo to it straight away");
editProbe.SelPoints.Add(secondTarget);editProbe.CenterGizmoOnSelection(probeSession);Check(editProbe.GizmoFrame.Origin.DistanceTo(editProbe.Plane.PointAt(40,30))<1e-6,"adding a second point moves the bound gizmo to the average of both");
Check(editProbe.GeometryEditing&&!editProbe.GizmoEditing&&editProbe.SelPoints.Count==2&&editProbe.GizmoTargetConfirmed,"both clicked points stay selected and bound, with no Enter needed");
editProbe.SetGizmoEditing(probeSession,true);TargetPoint(20,20,false);
var pointStart=editProbe.Model.P(firstTarget);var worldStart=editProbe.Plane.PointAt(pointStart.X,pointStart.Y);
var centerPixel=probeSession.Camera.Project(editProbe.GizmoFrame.Origin)!.Value;
probeSession.PointerDown(new(centerPixel.x,centerPixel.y,true,false,false,false));
probeSession.PointerMove(new(centerPixel.x+45,centerPixel.y+18,true,false,false,false));
probeSession.PointerUp(new(centerPixel.x+45,centerPixel.y+18,false,false,false,false));
var pointEnd=editProbe.Model.P(firstTarget);var worldEnd=editProbe.Plane.PointAt(pointEnd.X,pointEnd.Y);
Check(worldEnd.DistanceTo(worldStart)>1,"dragging the gizmo center after manual setup exit moves its selected point in an oblique view");
Check(editProbe.Model.Polylines(editProbe.Plane).First(poly=>poly.ent.Id==targetCurve).pts[0].DistanceTo(worldEnd)<1e-5,"the connected curve endpoint follows the transformed point");
Check(editProbe.GizmoFrame.Origin.DistanceTo(worldEnd)<1e-5,"the gizmo stays attached to the transformed point after release rather than snapping back");
Check(editProbe.TryUndo(probeSession)&&editProbe.Plane.PointAt(editProbe.Model.P(firstTarget).X,editProbe.Model.P(firstTarget).Y).DistanceTo(worldStart)<1e-5,"the point gizmo transform is one undoable transaction");
// Points on another drawing plane bind the gizmo with a single click.
editProbe.SetGizmoEditing(probeSession,false);editProbe.SetDrawingPlane(probeSession,Plane3.XZ,forceNew:true);
int otherA=editProbe.Model.AddPoint(-30,10),otherB=editProbe.Model.AddPoint(-60,25);editProbe.Model.AddPointEntity(otherA);editProbe.Model.AddPointEntity(otherB);
editProbe.Model.AddSpline([otherA,otherB],false,false,false);editProbe.Solve(probeSession);
int otherPlaneIndex=editProbe.ActivePlane;editProbe.SelectPlane(probeSession,0);editProbe.SetGizmoEditing(probeSession,true);
var otherPlane=editProbe.DrawingPlanes[otherPlaneIndex].Plane;
void TargetOtherPoint(double u,double v,bool shift)
{
 var px=probeSession.Camera.Project(otherPlane.PointAt(u,v))!.Value;
 probeSession.PointerDown(new(px.x,px.y,true,shift,false,false));probeSession.PointerUp(new(px.x,px.y,false,shift,false,false));
}
TargetOtherPoint(-30,10,false);
Check(editProbe.ActivePlane==otherPlaneIndex&&editProbe.SelPoints.Count==1&&editProbe.GizmoFrame.Origin.DistanceTo(otherPlane.PointAt(-30,10))<1e-6,"one click on a point of another drawing plane switches to it and binds the gizmo there");
Check(editProbe.GizmoTargetConfirmed&&!editProbe.GizmoEditing&&editProbe.SelPoints.Count==1,"the point on the other plane is bound without leaving setup by hand");
var lockedOrigin=editProbe.GizmoFrame.Origin;var lockedSelection=editProbe.SelPoints.ToList();
TargetOtherPoint(-60,25,false);
Check(editProbe.ActivePlane==otherPlaneIndex&&editProbe.SelPoints.SetEquals(new[]{otherB})&&editProbe.GizmoFrame.Origin.DistanceTo(otherPlane.PointAt(-60,25))<1e-6&&editProbe.GizmoTargetConfirmed,"a plain click retargets the gizmo to the newly selected point");
TargetOtherPoint(-30,10,false);
TargetOtherPoint(-60,25,true);
Check(editProbe.SelPoints.Count==2&&editProbe.GizmoTargetConfirmed&&editProbe.GizmoFrame.Origin.DistanceTo(lockedOrigin)>1e-6,"Shift-clicking another point adds it and the bound gizmo moves to the average");
TargetOtherPoint(-60,25,true);Check(editProbe.SelPoints.SetEquals(lockedSelection)&&editProbe.GizmoFrame.Origin.DistanceTo(lockedOrigin)<1e-6,"Shift-clicking it again takes it back out");
var lockedCurve=otherPlane.PointAt(-45,17.5);var lockedPixel=probeSession.Camera.Project(lockedCurve)!.Value;
probeSession.PointerDown(new(lockedPixel.x,lockedPixel.y,true,false,false,false));probeSession.PointerUp(new(lockedPixel.x,lockedPixel.y,false,false,false,false));
Check(editProbe.SelEntities.Count==1&&editProbe.GizmoTargetConfirmed,"clicking a curve retargets the bound gizmo to that curve");
// Saved datums are listed in the model tree and can be reused from there.
{
 var treeVm=new MainViewModel{HomeOpen=false};treeVm.Session.EnterSketch(Plane3.XY);var treeSketch=treeVm.Session.Sketch!;
 treeSketch.Set3DEnabled(treeVm.Session,true);treeSketch.RotateDrawingPlane(treeVm.Session,new(0,40,0));
 var saved=treeSketch.SaveDatum(treeVm.Session,SketchDatumKind.Plane,"Tilted plane");treeVm.RebuildBrowser();
 var datumNode=treeVm.AllBrowserNodes().FirstOrDefault(n=>n.Kind==NodeKind.Datum);
 Check(datumNode is { Title:"Tilted plane",DatumKind:SketchDatumKind.Plane,IsActiveSketchDatum:true,HasEye:false},"a saved plane appears in the model tree under the sketch being edited");
 treeSketch.UseDatum(treeVm.Session,null);Check(treeSketch.ActiveDatumId==null,"live gizmo detaches from the saved plane");
 treeVm.UseDatumNode(datumNode!);Check(treeSketch.ActiveDatumId==saved.Id,"selecting the tree row makes the saved plane the active reference");
 treeVm.Session.ExitSketch(true);treeVm.RebuildBrowser();
 var finishedNode=treeVm.AllBrowserNodes().FirstOrDefault(n=>n.Kind==NodeKind.Datum);
 Check(finishedNode is { IsActiveSketchDatum:false,SelectionItem:{ Kind:SelKind.Datum } },"saved planes stay in the tree under the finished sketch");
}
// Free 3D editing lets a point leave its drawing plane; without it the point stays on the plane.
foreach(bool freeMode in new[]{false,true})
{
 var freeSession=new CadSession();freeSession.EnterSketch(Plane3.XY,alignView:false);var freeSketch=freeSession.Sketch!;
 freeSketch.Set3DEnabled(freeSession,true);freeSketch.SetTool(freeSession,new SketchGeometryEditTool());freeSketch.FreeEditing=freeMode;
 int fa=freeSketch.Model.AddPoint(20,20),fb=freeSketch.Model.AddPoint(60,40);freeSketch.Model.AddPointEntity(fa);freeSketch.Model.AddPointEntity(fb);
 freeSketch.Model.AddSpline([fa,fb],false,false,false);freeSketch.Solve(freeSession);
 var firstPixel=freeSession.Camera.Project(freeSketch.Plane.PointAt(20,20))!.Value;
 freeSession.PointerDown(new(firstPixel.x,firstPixel.y,true,false,false,false));freeSession.PointerUp(new(firstPixel.x,firstPixel.y,false,false,false,false));
 var gizmoPixel=freeSession.Camera.Project(freeSketch.GizmoFrame.Origin)!.Value;
 freeSession.PointerDown(new(gizmoPixel.x,gizmoPixel.y,true,false,false,false));
 freeSession.PointerMove(new(gizmoPixel.x,gizmoPixel.y-60,true,false,false,false));
 freeSession.PointerUp(new(gizmoPixel.x,gizmoPixel.y-60,false,false,false,false));
 double lift=freeSketch.Model.P(fa).Z;
 Check(freeMode?Math.Abs(lift)>1:Math.Abs(lift)<1e-9,freeMode?"Free 3D editing lifts a point off its drawing plane":"without Free 3D editing a point stays on its drawing plane");
 if(freeMode)
 {
  var world=SketchModel.To3(freeSketch.Plane,freeSketch.Model.P(fa));
  Check(freeSketch.GizmoFrame.Origin.DistanceTo(world)<1e-5&&freeSketch.Model.Polylines(freeSketch.Plane).First().pts[0].DistanceTo(world)<1e-5,"the gizmo and the connected curve follow the lifted point");
  Check(freeSketch.TryUndo(freeSession)&&Math.Abs(freeSketch.Model.P(fa).Z)<1e-9,"lifting a point is one undoable step");
 }
}
// Combined display keeps translation available after choosing Rotate or Scale.
var savedGizmoDisplay = GizmoSettings.Current.DisplayMode; GizmoSettings.Current.DisplayMode = 1;
foreach(var operation in new[]{SketchGizmoOperation.Move,SketchGizmoOperation.Rotate,SketchGizmoOperation.Size})
foreach(double yaw in new[]{-.6,.4,1.2})
{
 var arrowVm=new MainViewModel{HomeOpen=false};var arrowSession=arrowVm.Session;
 arrowSession.EnterSketch(Plane3.XY);arrowSession.Camera.Yaw=yaw;arrowSession.Camera.Pitch=.55;
 var arrowSketch=arrowSession.Sketch!;arrowSketch.Set3DEnabled(arrowSession,true);
 var draw=new SplineTool(false);arrowSketch.SetTool(arrowSession,draw);draw.Feed(arrowSession,20,20);draw.Feed(arrowSession,45,35);draw.Feed(arrowSession,70,20);
 var controls=new SketchSpacePanel{DataContext=arrowVm};var buttonHost=new Window{Content=controls,Width=360,Height=1000};buttonHost.Show();Dispatcher.UIThread.RunJobs();
 // the less-used 3D sketch tools sit in a collapsed "More 3D sketch tools" group; open it so its buttons exist
 foreach(var group in controls.GetVisualDescendants().OfType<Expander>().Where(x=>Equals(x.Header,"More 3D sketch tools")))group.IsExpanded=true;Dispatcher.UIThread.RunJobs();
 void Press(string tag)=>controls.GetVisualDescendants().OfType<Button>().Single(b=>Equals(b.Tag,tag)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
 Press("select");Check(arrowSketch.GeometryEditing&&!arrowSketch.GizmoEditing,"Select button enables direct point editing without setup");
 var selectedPixel=arrowSession.Camera.Project(new Vec3(20,20,0))!.Value;
 arrowSession.PointerDown(new(selectedPixel.x,selectedPixel.y,true,false,false,false));arrowSession.PointerUp(new(selectedPixel.x,selectedPixel.y,false,false,false,false));
 int selectedId=arrowSketch.SelPoints.Single();
 Check(!arrowSketch.GizmoEditing&&arrowSketch.GizmoTargetConfirmed,"clicking the curve point binds the gizmo without Enter or a Gizmo button");
 arrowSketch.GizmoOperation=operation;
 double handleLength=arrowSession.Camera.PixelSizeAt(arrowSketch.GizmoFrame.Origin)*96;
 var shaft=arrowSession.Camera.Project(arrowSketch.GizmoFrame.Origin+arrowSketch.GizmoFrame.U*handleLength*.45)!.Value;
 var axisTip=arrowSession.Camera.Project(arrowSketch.GizmoFrame.Origin+arrowSketch.GizmoFrame.U*handleLength)!.Value;
 var originPixel=arrowSession.Camera.Project(arrowSketch.GizmoFrame.Origin)!.Value;
 double directionX=axisTip.x-originPixel.x,directionY=axisTip.y-originPixel.y,screenLength=Math.Sqrt(directionX*directionX+directionY*directionY);
 double endX=shaft.x+directionX/screenLength*30,endY=shaft.y+directionY/screenLength*30;
 var original=arrowSketch.Plane.PointAt(arrowSketch.Model.P(selectedId).X,arrowSketch.Model.P(selectedId).Y);
 Check(arrowSketch.GizmoHit(arrowSession,shaft.x,shaft.y,false)==1,"translation shaft is pickable independently of the current operation");
 arrowSession.PointerDown(new(shaft.x,shaft.y,true,false,false,false));arrowSession.PointerMove(new(endX,endY,true,false,false,false));arrowSession.PointerUp(new(endX,endY,false,false,false,false));
 var moved=arrowSketch.Plane.PointAt(arrowSketch.Model.P(selectedId).X,arrowSketch.Model.P(selectedId).Y);
 Check(moved.X>original.X+1&&Math.Abs(moved.Y-original.Y)<1e-4&&moved.DistanceTo(arrowSketch.GizmoFrame.Origin)<1e-4,"arrow shaft translates the bound curve point even after Rotate or Scale was selected");
 buttonHost.Close();
}
GizmoSettings.Current.DisplayMode = savedGizmoDisplay;
var retention=new CadSession();retention.EnterSketch(Plane3.XY);var retained=retention.Sketch!;
var pendingSpline=new SplineTool(false);retained.SetTool(retention,pendingSpline);pendingSpline.Feed(retention,10,10);pendingSpline.Feed(retention,30,25);pendingSpline.Feed(retention,60,10);
retained.SetTool(retention,new SelectTool());
Check(retained.Model.Entities.Any(e=>e.Type==EntType.Spline&&e.Pts.Count==3),"changing tools keeps a spline without requiring Enter");
Check(retained.TryUndo(retention)&&retained.Model.Entities.Count==0,"automatically retained spline remains undoable");
var unfinishedRect=new RectTool(RectMode.TwoCorner);retained.SetTool(retention,unfinishedRect);unfinishedRect.Feed(retention,15,35);retention.Key("Escape");
Check(retained.Model.Entities.Any(e=>e.Type==EntType.Point),"leaving an incomplete shape preserves its placed point");
var finishSpline=new SplineTool(false);retained.SetTool(retention,finishSpline);finishSpline.Feed(retention,10,50);finishSpline.Feed(retention,30,70);retention.ExitSketch(true);
Check(retention.Objects.Single().SketchData!.Entities.Any(e=>e.Type==EntType.Spline),"Finish Sketch retains an unfinished spline without a separate Enter");
// Parts library: built-in parts, folder scan, thumbnails and insertion.
{
 var builtIn=PartsLibrary.BuiltIn();
 Check(builtIn.Count>30&&builtIn.Select(p=>p.Name).Distinct().Count()==builtIn.Count,"the library ships built-in common parts with unique names");
 foreach(var sample in new[]{"Hex nut M6","Flat washer M8","Hex bolt M5x20","Socket head screw M4x12","Ball bearing 8x22x7"})
 {
  var part=builtIn.Single(p=>p.Name==sample);var (shape,_,_)=PartsLibrary.Load(part);
  var px=PartsLibrary.RenderThumbnail(shape,64);int lit=0;for(int i=0;i<64*64;i++)if(px[i*4]!=0x2E||px[i*4+1]!=0x25)lit++;
  Console.WriteLine($"  {sample}: {shape.Tessellate().TriangleCount} triangles, {lit} lit pixels");
  Check(shape.Tessellate().TriangleCount>0&&lit>200,$"{sample} builds a solid and renders a thumbnail");
 }
 string libDir=Path.Combine(Path.GetTempPath(),"prism-lib-test-"+Guid.NewGuid().ToString("N")[..8]);Directory.CreateDirectory(Path.Combine(libDir,"Brackets"));
 var (nutShape,_,_)=PartsLibrary.Load(builtIn.Single(p=>p.Name=="Hex nut M6"));
 Occ.ExportStep([nutShape],Path.Combine(libDir,"Brackets","nut.step"));File.WriteAllText(Path.Combine(libDir,"notes.txt"),"ignored");
 var scanned=PartsLibrary.Scan(libDir);
 Check(scanned.Count==1&&scanned[0].Category=="Brackets"&&scanned[0].Name=="nut","scanning a folder lists STEP parts by sub-folder and ignores other files");
 Check(PartsLibrary.Scan(Path.Combine(libDir,"missing")).Count==0&&PartsLibrary.Scan(null).Count==0,"an empty or missing library folder is simply empty");
 var libVm=new MainViewModel{HomeOpen=false};var oldPath=AppSettings.Current.PartsLibraryPath;
 libVm.SetLibraryFolder(libDir);
 Check(libVm.LibraryGroups.Any(g=>g.Title=="Brackets"&&g.Items.Count==1)&&libVm.LibraryGroups.Any(g=>g.Title.StartsWith("Built-in")),"the library panel groups file parts and built-in parts");
 libVm.LibraryShowBuiltIn=false;Check(libVm.LibraryGroups.All(g=>!g.Title.StartsWith("Built-in")),"built-in parts can be hidden");
 libVm.LibraryShowBuiltIn=true;libVm.LibraryFilter="washer m6";Check(libVm.LibraryGroups.Sum(g=>g.Items.Count)==1,"the search box filters the library");
 libVm.LibraryFilter="";
 libVm.InsertLibraryItem(libVm.LibraryGroups.First(g=>g.Title=="Brackets").Items[0]);
 libVm.Session.CommitCommand();
 Check(libVm.Session.Components.Count()==1&&libVm.Session.Components.First().Component!.Grounded,"inserting a library part adds it as the first (grounded) component");
 AppSettings.Current.PartsLibraryPath=oldPath;AppSettings.Save(false);try{Directory.Delete(libDir,true);}catch{}
}
// File exchange: IGES, BREP, glTF and DXF.
{
 string dir=Path.Combine(Path.GetTempPath(),"prism-exchange-"+Guid.NewGuid().ToString("N")[..8]);Directory.CreateDirectory(dir);
 var ex=new CadSession();ex.AddObject(Occ.Box(30,20,10),"Body");
 string iges=Path.Combine(dir,"part.igs"),brep=Path.Combine(dir,"part.brep"),glb=Path.Combine(dir,"part.glb");
 ex.ExportIges(iges);var fromIges=new CadSession();fromIges.ImportFile(iges);
 double igesMinX=fromIges.Objects.Min(o=>o.Mesh.Min.X),igesMaxX=fromIges.Objects.Max(o=>o.Mesh.Max.X),igesMaxZ=fromIges.Objects.Max(o=>o.Mesh.Max.Z);
 Console.WriteLine($"  IGES import: {fromIges.Objects.Count} object(s), x {igesMinX:0.##}..{igesMaxX:0.##}, top z {igesMaxZ:0.##}");
 Check(fromIges.Objects.Count>0&&Math.Abs(igesMaxX-igesMinX-30)<1e-3&&Math.Abs(igesMaxZ-10)<1e-3,"IGES export and import round-trip a box");
 ex.ExportBrep(brep);var fromBrep=new CadSession();fromBrep.ImportFile(brep);
 Check(fromBrep.Objects.Count==1&&Math.Abs((fromBrep.Objects[0].Mesh.Max-fromBrep.Objects[0].Mesh.Min).Length-Math.Sqrt(1400))<0.01,"BREP export and import round-trip a box exactly");
 ex.ExportGlb(glb);var bytes=File.ReadAllBytes(glb);
 Check(bytes.Length>200&&BitConverter.ToUInt32(bytes,0)==0x46546C67u&&BitConverter.ToUInt32(bytes,4)==2&&BitConverter.ToUInt32(bytes,8)==(uint)bytes.Length,"glTF export writes a valid GLB header and length");
 string json=System.Text.Encoding.UTF8.GetString(bytes,20,(int)BitConverter.ToUInt32(bytes,12));
 var glbDoc=System.Text.Json.JsonDocument.Parse(json).RootElement;var posAccessor=glbDoc.GetProperty("accessors")[0];
 double glbWidth=posAccessor.GetProperty("max")[0].GetDouble()-posAccessor.GetProperty("min")[0].GetDouble(),glbHeight=posAccessor.GetProperty("max")[1].GetDouble()-posAccessor.GetProperty("min")[1].GetDouble();
 Check(glbDoc.GetProperty("asset").GetProperty("version").GetString()=="2.0"&&Math.Abs(glbWidth-0.030)<1e-6&&Math.Abs(glbHeight-0.010)<1e-6,"the GLB mesh is in metres with Z-up turned into Y-up (30 mm wide, 10 mm tall)");
 // DXF out and back in
 var sk=new SketchModel();int a=sk.AddPoint(0,0),b=sk.AddPoint(40,0),c=sk.AddPoint(40,25);sk.AddLine(a,b);sk.AddLine(b,c);
 sk.AddCircle(sk.AddPoint(10,10),6);sk.AddArc(sk.AddPoint(60,0),sk.AddPoint(70,0),sk.AddPoint(60,10));
 var dxfSession=new CadSession();dxfSession.AddObject(Occ.Profile(sk.ToShapes(Plane3.XY),faces:false),"Outline",ObjKind.Sketch,plane:Plane3.XY,sketch:sk);
 string dxf=Path.Combine(dir,"outline.dxf");dxfSession.ExportDxf(dxf);string dxfText=File.ReadAllText(dxf);
 Check(dxfText.Contains("LINE")&&dxfText.Contains("CIRCLE")&&dxfText.Contains("ARC"),"DXF export writes true LINE, CIRCLE and ARC entities");
 var back=new CadSession();back.ImportFile(dxf);var backSketch=back.Objects.Single(o=>o.Kind==ObjKind.Sketch).SketchData!;
 Check(backSketch.Entities.Count(e=>e.Type==EntType.Line)==2&&backSketch.Entities.Count(e=>e.Type==EntType.Circle)==1&&backSketch.Entities.Count(e=>e.Type==EntType.Arc)==1&&Math.Abs(backSketch.Entities.First(e=>e.Type==EntType.Circle).R-6)<1e-6,"a DXF round-trips lines, a circle and an arc into a sketch");
 File.WriteAllText(Path.Combine(dir,"poly.dxf"),"0\nSECTION\n2\nENTITIES\n0\nLWPOLYLINE\n90\n4\n70\n1\n10\n0\n20\n0\n10\n10\n20\n0\n10\n10\n20\n10\n10\n0\n20\n10\n0\nENDSEC\n0\nEOF\n");
 var polyDoc=new CadSession();polyDoc.ImportFile(Path.Combine(dir,"poly.dxf"));
 Check(polyDoc.Objects.Single().SketchData!.Entities.Count==4,"a closed LWPOLYLINE becomes four connected lines");
 try{Directory.Delete(dir,true);}catch{}
}
// Animation timeline: rows, adding, retiming and deleting keys.
{
 var animVm=new MainViewModel{HomeOpen=false};animVm.Session.StartCommand(new Prism.Commands.AnimationCommand());
 var anim=animVm.AnimationEditor;
 Check(anim!=null&&animVm.ShowAnimationTimeline,"opening the Animation command shows the timeline dock");
 int changes=0;anim!.TimelineChanged+=()=>changes++;
 var explodeRow=anim.Rows().Single(r=>r.Label=="Exploded view");
 Check(explodeRow.Track==null,"a row with no keys has no track yet");
 anim.AddKey(explodeRow,2);
 var track=anim.Rows().Single(r=>r.Label=="Exploded view").Track;
 Check(track!=null&&track.Keys.Count==2&&track.Keys[0].T==0&&Math.Abs(track.Keys[1].T-2)<1e-9,"adding a key creates the track with its starting key at 0 s");
 anim.MoveKey(track!,track.Keys[1],3.5);
 Check(Math.Abs(track.Keys[1].T-3.5)<1e-9,"dragging a key retimes it");
 anim.MoveKey(track,track.Keys[0],1);
 Check(track.Keys[0].T==0,"the starting key at 0 s cannot be moved");
 anim.Time=4;Check(Math.Abs(anim.Time-4)<1e-9&&animVm.TimelineTimeText.StartsWith("4.00"),"scrubbing sets the time and updates the readout");
 anim.DeleteKey(track,track.Keys[1]);
 Check(anim.Rows().Single(r=>r.Label=="Exploded view").Track==null&&changes>=4,"deleting the last moving key removes the track and the timeline is notified");
 anim.AddKey(explodeRow,1.5);anim.AddKey(explodeRow,3.2);anim.Time=2.1;
 var timeline=new Prism.Controls.AnimationTimelineView{Command=anim};var timelineHost=new Window{Width=900,Height=120,Content=timeline};timelineHost.Show();Dispatcher.UIThread.RunJobs();
 using(var bitmap=new RenderTargetBitmap(new PixelSize(900,120))){bitmap.Render(timeline);bitmap.Save(Path.Combine(output,"timeline.png"));}
 timelineHost.Close();
 animVm.Session.CancelCommand();Check(!animVm.ShowAnimationTimeline,"closing the command hides the timeline dock");
}
// Alt-dragging a gizmo move handle copies the bound curve and leaves the original.
{
    var cs=new CadSession();cs.EnterSketch(Plane3.XY);var ck=cs.Sketch!;ck.Set3DEnabled(cs,true);ck.SetDrawingMode(cs,flat:true);
    ck.SetTool(cs,new SelectTool());
    int a=ck.Model.AddPoint(20,20),b=ck.Model.AddPoint(60,40);ck.Model.AddPointEntity(a);ck.Model.AddPointEntity(b);
    int curve=ck.Model.AddSpline([a,b],false,false,false);ck.Solve(cs);
    ck.SelEntities.Clear();ck.SelPoints.Clear();ck.SelEntities.Add(curve);ck.CenterGizmoOnSelection(cs);
    // a moved independent shape lives on its own drawing plane, so count splines across the whole exported sketch
    int Splines(){var m=ck.ExportModel();return m.Entities.Count(e=>e.Type==EntType.Spline)+m.Sections.Sum(x=>x.Model.Entities.Count(e=>e.Type==EntType.Spline));}
    int before=Splines();var o=cs.Camera.Project(ck.GizmoFrame.Origin)!.Value;var ux=cs.Camera.Project(ck.GizmoFrame.Origin+ck.GizmoFrame.U)!.Value;
    double ax=ux.x-o.x,ay=ux.y-o.y,al=Math.Sqrt(ax*ax+ay*ay);bool grabbed=false;double gx=0,gy=0;
    for(double off=25;off<=140&&!grabbed;off+=5)
    {
        gx=o.x+ax/al*off;gy=o.y+ay/al*off;
        cs.PointerDown(new(gx,gy,true,false,false,true));
        if(Splines()>before)grabbed=true;else cs.PointerUp(new(gx,gy,false,false,false,true));
    }
    Check(grabbed,"Alt on a gizmo move handle starts a copy");
    cs.PointerMove(new(gx+40,gy,true,false,false,true));cs.PointerUp(new(gx+40,gy,false,false,false,true));
    var root=ck.ExportModel();
    Check(Splines()==before+1,"the drag leaves one extra curve");
    Check(root.Entities.Any(e=>e.Id==curve)&&Math.Abs(root.P(root.E(curve).Pts[0]).X-20)<1e-9,"the original stays where it was");
    Check(ck.SelEntities.Count==1&&!ck.SelEntities.Contains(curve),"the copy is the selection");
    Check(ck.TryUndo(cs)&&Splines()==before,"one undo removes the copy");
}
// Draw flat follows an axis-aligned view: draw on the side view, then on the top view, in one sketch.
{
    var fs=new CadSession();fs.EnterSketch(Plane3.XY,alignView:false);var fk=fs.Sketch!;fk.Set3DEnabled(fs,true);fk.SetDrawingMode(fs,flat:true,adjustView:false);
    fs.Camera.Orthographic=true;fs.Camera.Yaw=0;fs.Camera.Pitch=Math.PI/2-1e-4;
    fk.Model.AddLine(fk.Model.AddPoint(0,0),fk.Model.AddPoint(30,0));fk.Solve(fs);
    fk.SetTool(fs,new LineTool(0));
    fs.PointerDown(new(300,300,true,false,false,false));fs.PointerUp(new(300,300,false,false,false,false));fs.Key("Escape");
    var topNormal=fk.Plane.Normal;
    fs.Camera.Yaw=Math.PI/2;fs.Camera.Pitch=0;
    Console.WriteLine("DBG eye="+fs.Camera.EyeDirection+" topNormal="+topNormal);
    fk.SetTool(fs,new LineTool(0));
    fs.PointerDown(new(300,300,true,false,false,false));fs.PointerUp(new(300,300,false,false,false,false));
    Check(Math.Abs(topNormal.Z)>.99,"flat drawing from the top view uses the XY plane");
    Check(Math.Abs(fk.Plane.Normal.X)>.99||Math.Abs(fk.Plane.Normal.Y)>.99,"flat drawing from a side view switches to that view's plane");
    Check(fk.DrawingPlanes.Count>=2,"the first plane and its curves are kept");
}
// Flat editing: a curve drawn on the top view can be picked and edited from the side view.
{
    var es=new CadSession();es.EnterSketch(Plane3.XY,alignView:false);var ek=es.Sketch!;ek.Set3DEnabled(es,true);ek.SetDrawingMode(es,flat:true,adjustView:false);
    int p0=ek.Model.AddPoint(0,0),p1=ek.Model.AddPoint(40,0);int topLine=ek.Model.AddLine(p0,p1);ek.Solve(es);
    es.Camera.Orthographic=true;es.Camera.Yaw=Math.PI/2;es.Camera.Pitch=0;
    ek.SetTool(es,new SelectTool());
    // second curve on the side-view plane, so the top-view line is on another section
    ek.SetDrawingPlane(es,Plane3.YZ,forceNew:true);ek.Model.AddLine(ek.Model.AddPoint(0,10),ek.Model.AddPoint(0,50));ek.Solve(es);
    ek.SelectPlane(es,0);int active0=ek.ActivePlane;
    var mid=es.Camera.Project(new Vec3(0,0,10))!.Value;
    // click on the curve that sits on the YZ plane while plane 1 is active
    var onYz=es.Camera.Project(ek.DrawingPlanes[1].Plane.PointAt(0,30))!.Value;
    es.PointerDown(new(onYz.x,onYz.y,true,false,false,false));es.PointerUp(new(onYz.x,onYz.y,false,false,false,false));
    Check(ek.ActivePlane==1&&ek.SelEntities.Count==1,"clicking a curve on another plane in flat mode switches to it and selects the curve");
}
// Rotating the unbound gizmo with its rings (only ring pixels: the center and arrows are skipped).
foreach(var (bound,ortho,tool) in new[]{(false,false,"select"),(true,false,"select"),(false,true,"line"),(false,false,"spline"),(true,true,"select")})
{
    var rs=new CadSession();rs.EnterSketch(Plane3.XY,alignView:false);var rk=rs.Sketch!;rk.Set3DEnabled(rs,true);
    rs.Camera.Orthographic=ortho;rs.Camera.Yaw=ortho?0:-.6;rs.Camera.Pitch=ortho?Math.PI/2-1e-4:.5;
    int ra=rk.Model.AddPoint(20,20),rb=rk.Model.AddPoint(60,40);int rc=rk.Model.AddSpline([ra,rb],false,false,false);rk.Solve(rs);
    rk.SetTool(rs,tool=="line"?new LineTool(0):tool=="spline"?new SplineTool(false):new SelectTool());
    if(bound){rk.SelEntities.Add(rc);rk.CenterGizmoOnSelection(rs);}
    rk.SetGizmoEditing(rs,true);rk.GizmoOperation=SketchGizmoOperation.Rotate;rs.Invalidate();
    var c=rs.Camera.Project(rk.GizmoFrame.Origin)!.Value;var f0=rk.GizmoFrame;bool turned=false;int ringHits=0;
    for(double r=40;r<=220&&!turned;r+=4)for(double a2=0;a2<6.28&&!turned;a2+=.1)
    {
        double px=c.x+r*Math.Cos(a2),py=c.y+r*Math.Sin(a2);
        int hit=rk.GizmoHit(rs,px,py,true);
        if(hit<7||hit>9)continue;ringHits++;
        rs.PointerDown(new(px,py,true,false,false,false));rs.PointerMove(new(px+25,py+10,true,false,false,false));rs.PointerUp(new(px+25,py+10,false,false,false,false));
        if(rk.GizmoFrame.U.DistanceTo(f0.U)>1e-3||rk.GizmoFrame.V.DistanceTo(f0.V)>1e-3)turned=true;
    }
    Check(ringHits>0&&turned,$"DBG rotation ring draggable bound={bound} ortho={ortho} tool={tool} hits={ringHits}");
}
// Box and lasso select everything visible, including curves drawn from other views, and Delete removes them together.
foreach (bool lassoMode in new[] { false, true })
{
    var xs = new CadSession(); xs.EnterSketch(Plane3.XY, alignView: false); var xk = xs.Sketch!; xk.Set3DEnabled(xs, true); xk.SetDrawingMode(xs, flat: true, adjustView: false);
    xk.Model.AddLine(xk.Model.AddPoint(0, 0), xk.Model.AddPoint(40, 0)); xk.Solve(xs);
    xk.SetDrawingPlane(xs, Plane3.YZ, forceNew: true); xk.Model.AddLine(xk.Model.AddPoint(0, 5), xk.Model.AddPoint(0, 45)); xk.Solve(xs);
    xs.Camera.Orthographic = false; xs.Camera.Yaw = -.6; xs.Camera.Pitch = .5;
    var xt = new SelectTool(); if (lassoMode) xt.UseLasso(true); xk.SetTool(xs, xt);
    var c0 = xs.Camera.Project(new Vec3(20, 0, 0))!.Value; var c1 = xs.Camera.Project(new Vec3(0, 0, 25))!.Value;
    double minX = Math.Min(c0.x, c1.x) - 120, maxX = Math.Max(c0.x, c1.x) + 120, minY = Math.Min(c0.y, c1.y) - 120, maxY = Math.Max(c0.y, c1.y) + 120;
    xs.PointerDown(new(minX, minY, true, false, false, false));
    if (lassoMode) { xs.PointerMove(new(maxX, minY, true, false, false, false)); xs.PointerMove(new(maxX, maxY, true, false, false, false)); xs.PointerMove(new(minX, maxY, true, false, false, false)); }
    else xs.PointerMove(new(maxX, maxY, true, false, false, false));
    var live = new Overlay(); xs.BuildOverlay(live);
    Console.WriteLine($"DBG rects={live.Rects.Count} paths={live.ScreenPaths.Count} tool={xk.Tool?.GetType().Name} geo={xk.GeometryEditing} min=({minX:0},{minY:0}) max=({maxX:0},{maxY:0})");
    if (lassoMode) Check(live.ScreenPaths.Count == 1, "the lasso loop is drawn in screen space while dragging");
    else Check(live.Rects.Count == 1, "the selection box is drawn while dragging");
    xs.PointerUp(new(lassoMode ? minX : maxX, maxY, false, false, false, false));
    int picked = xk.SelEntities.Count + xk.ExtraSelection.Sum(x => x.Value.Count);
    Check(picked == 2, (lassoMode ? "lasso" : "box") + " select picks curves from every plane in view");
    xs.Key("Delete");
    var left = xk.ExportModel(); int remaining = left.Entities.Count + left.Sections.Sum(x => x.Model.Entities.Count);
    Check(remaining == 0, "Delete removes the curves on both planes");
    Check(xk.TryUndo(xs) && xk.ExportModel() is var back && back.Entities.Count + back.Sections.Sum(x => x.Model.Entities.Count) == 2, "one undo brings both back");
}
// A visible 2D sketch is something to attach to: curves snap onto its circle.
{
    var ss = new CadSession(); ss.EnterSketch(Plane3.XY, alignView: false); var s1 = ss.Sketch!;
    s1.Model.AddCircle(s1.Model.AddPoint(0, 0), 20); s1.Solve(ss); ss.ExitSketch(true);
    Check(ss.Objects.Any(o => o.Kind == ObjKind.Sketch && o.Visible), "the finished 2D sketch stays visible");
    ss.EnterSketch(Plane3.XY, alignView: false); var s2 = ss.Sketch!;
    ss.Camera.Orthographic = true; ss.Camera.Yaw = 0; ss.Camera.Pitch = Math.PI / 2 - 1e-4;
    var onCircle = ss.Camera.Project(new Vec3(20, 0, 0))!.Value;
    var near = s2.Snap(ss, onCircle.x + 3, onCircle.y + 2, true);
    Check(near is { } hit && Math.Abs(Math.Sqrt(hit.U * hit.U + hit.V * hit.V) - 20) < .5, "a point near a visible sketch's circle snaps onto it");
    s2.Set3DEnabled(ss, true); s2.SetDrawingMode(ss, flat: true, adjustView: false);
    ss.Camera.Yaw = Math.PI / 2; ss.Camera.Pitch = 0;
    var seen = ss.Camera.Project(new Vec3(0, 20, 0));
    Check(seen != null, "the sketch is seen from the side view");
}
// A pending spline click that refers to a point the model no longer has must not crash the solver.
{
    var cs2 = new CadSession(); cs2.EnterSketch(Plane3.XY, alignView: false); var ck2 = cs2.Sketch!;
    var st = new SplineTool(false); ck2.SetTool(cs2, st);
    st.Feed(cs2, 10, 10); st.Feed(cs2, 30, 20);
    var pending = typeof(ClickTool).GetProperty("Clicks", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)?.GetValue(st) as List<SnapInfo>;
    pending?.Add(new SnapInfo(50, 10, SnapKind.Point, 9999));
    cs2.Key("Escape");
    Check(ck2.Model.Entities.Count(e => e.Type == EntType.Spline) == 1, "a stale point id in a pending spline becomes a plain point instead of crashing");
}
// Drawing in space offers the plane squares near the gizmo, and clicking one draws on that plane.
var cueNow = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc); SketchContext.Clock = () => cueNow;
{
    var ps = new CadSession(); ps.EnterSketch(Plane3.XY, alignView: false); var pk = ps.Sketch!; pk.Set3DEnabled(ps, true); pk.SetDrawingMode(ps, flat: false, adjustView: false);
    ps.Camera.Orthographic = false; ps.Camera.Yaw = -.6; ps.Camera.Pitch = .5;
    pk.SetTool(ps, new LineTool(0));
    var g = ps.Camera.Project(pk.GizmoFrame.Origin)!.Value;
    ps.PointerMove(new(g.x + 6, g.y + 6, false, false, false, false));
    var overlayNear = new Overlay(); pk.BuildOverlay(ps, overlayNear);
    var overlayFar = new Overlay(); ps.PointerMove(new(g.x + 900, g.y + 900, false, false, false, false)); pk.BuildOverlay(ps, overlayFar);
    Check(overlayNear.Tris.Count > overlayFar.Tris.Count, "the plane squares appear when the pointer nears the gizmo and hide when it leaves");
    bool locked = false;
    for (double r = 8; r < 90 && !locked; r += 3) for (double a = 0; a < 6.28 && !locked; a += .15)
    {
        double px = g.x + r * Math.Cos(a), py = g.y + r * Math.Sin(a);
        ps.PointerMove(new(px, py, false, false, false, false));
        int hit = pk.GizmoHit(ps, px, py, false);
        if (hit is < 4 or > 6) continue;
        cueNow += TimeSpan.FromSeconds(2); ps.PointerMove(new(px, py, false, false, false, false));      // rest on the square until the cue shows
        ps.PointerDown(new(px, py, true, false, false, false)); ps.PointerUp(new(px, py, false, false, false, false));
        locked = pk.GizmoPlaneIndex == hit - 4;
    }
    Check(locked, "clicking a plane square while drawing selects that plane");
}
// A dropdown that holds the running sketch tool looks pressed (Spline sits inside "More drawing tools").
{
    var rvm = new MainViewModel(); rvm.Session.EnterSketch(Plane3.XY, alignView: false);
    rvm.Session.Sketch!.SetTool(rvm.Session, new SplineTool(false));
    foreach (var t in rvm.Tabs) foreach (var b in t.Buttons) b.Refresh();
    var sketchTab = rvm.Tabs.Single(t => t.Title == "Sketch");
    var more = sketchTab.Groups.Single(g => g.Title == "Draw").Items.OfType<Prism.Ribbon.RibbonButton>().Single(b => b.HasMenu);
    Check(more.IsChecked && more.Label == "Spline", "the drawing-tools dropdown shows Spline as pressed while the spline tool runs");
    rvm.Session.Sketch!.SetTool(rvm.Session, new SelectTool());
    foreach (var t in rvm.Tabs) foreach (var b in t.Buttons) b.Refresh();
    Check(!more.IsChecked, "and it releases when another tool takes over");
}
// Holding Ctrl over an indicated plane keeps drawing on it however far the pointer wanders; releasing ends the lock.
{
    var ls = new CadSession(); ls.EnterSketch(Plane3.XY, alignView: false); var lk = ls.Sketch!; lk.Set3DEnabled(ls, true); lk.SetDrawingMode(ls, flat: false, adjustView: false);
    ls.Camera.Orthographic = false; ls.Camera.Yaw = -.6; ls.Camera.Pitch = .5;
    lk.SetTool(ls, new LineTool(0));
    var lg = ls.Camera.Project(lk.GizmoFrame.Origin)!.Value; int target = -1; double tx = 0, ty = 0;
    for (double r = 8; r < 90 && target < 0; r += 3) for (double a = 0; a < 6.28 && target < 0; a += .15)
    {
        double px = lg.x + r * Math.Cos(a), py = lg.y + r * Math.Sin(a);
        ls.PointerMove(new(px, py, false, false, false, false));
        int hit = lk.GizmoHit(ls, px, py, false);
        if (hit is >= 4 and <= 6 && hit - 4 != lk.GizmoPlaneIndex) { target = hit - 4; tx = px; ty = py; }
    }
    Check(target >= 0, "found a plane square other than the current plane");
    ls.PointerMove(new(lg.x + 500, lg.y - 400, false, false, false, false)); cueNow += TimeSpan.FromSeconds(5);
    ls.PointerMove(new(tx, ty, false, false, false, false)); cueNow += TimeSpan.FromSeconds(.5);
    ls.PointerMove(new(tx, ty, false, false, true, false));
    Check(lk.LockedPlane == -1, "resting on a plane square for half a second does not offer it yet (1.5 s default)");
    cueNow += TimeSpan.FromSeconds(1.2); ls.PointerMove(new(tx, ty, false, false, false, false));
    var cueOverlay = new Overlay(); ls.BuildOverlay(cueOverlay);
    Check(cueOverlay.Labels.Any(l => l.Text.Contains("hold Ctrl")), "after the rest time the plane is offered and says Ctrl locks it");
    ls.PointerMove(new(tx, ty, false, false, true, false));
    Check(lk.LockedPlane == target && lk.GizmoPlaneIndex == target, "Ctrl over an indicated plane locks drawing to it");
    var lockedOverlay = new Overlay(); ls.BuildOverlay(lockedOverlay);
    Check(lockedOverlay.Labels.Any(l => l.Text.Contains("locked")), "the locked plane is labelled");
    ls.PointerMove(new(lg.x + 400, lg.y - 300, false, false, true, false));
    Check(lk.LockedPlane == target && lk.GizmoPlaneIndex == target, "the lock holds while the pointer is far from the gizmo");
    ls.PointerMove(new(lg.x + 400, lg.y - 300, false, false, false, false));
    Check(lk.LockedPlane == -1 && lk.GizmoPlaneIndex == target, "releasing Ctrl ends the lock and the plane stays current");
}
// 3D sketching attaches to whatever is visible: a line can start on the end of a curve from another sketch, off the drawing plane.
{
    var os = new CadSession(); os.EnterSketch(Plane3.XY, alignView: false); var o1 = os.Sketch!;
    o1.Model.AddLine(o1.Model.AddPoint(0, 0), o1.Model.AddPoint(40, 0)); o1.Solve(os); os.ExitSketch(true);
    os.EnterSketch(Plane3.YZ, alignView: false); var o2 = os.Sketch!; o2.Set3DEnabled(os, true); o2.SetDrawingMode(os, flat: false, adjustView: false);
    os.Camera.Orthographic = false; os.Camera.Yaw = -.6; os.Camera.Pitch = .5;
    o2.SetTool(os, new LineTool(0));
    var end = os.Camera.Project(new Vec3(40, 0, 0))!.Value;
    var snap = o2.Snap(os, end.x + 2, end.y + 1, true);
    Check(snap is { Kind: SnapKind.External } hit2 && Math.Abs(Math.Abs(hit2.Depth) - 40) < 1e-6, "in 3D sketch a line snaps to the end of another sketch's curve, off the drawing plane");
}
// Snapping a spline point onto another spline must not crash the solver (the crash.log KeyNotFoundException).
{
    var cs3 = new CadSession(); cs3.EnterSketch(Plane3.XY, alignView: false); var ck3 = cs3.Sketch!;
    int sa = ck3.Model.AddPoint(0, 0), sb = ck3.Model.AddPoint(30, 20), sc = ck3.Model.AddPoint(60, 0);
    int baseSpline = ck3.Model.AddSpline([sa, sb, sc], false, false); ck3.Solve(cs3);
    bool solved = ck3.Edit(cs3, "That spline", () =>
    {
        int pid = ck3.PointFor(new SnapInfo(30, 20, SnapKind.Entity, baseSpline));
        int pid2 = ck3.Model.AddPoint(30, 50);
        ck3.Model.AddSpline([pid, pid2], false, false);
    });
    Check(solved && ck3.Model.Entities.Count(e => e.Type == EntType.Spline) == 2, "a spline can start on another spline's curve without crashing");
}
// Right-click Look at in the object tree: origin planes and saved planes know their own plane.
{
    var tvm = new MainViewModel(); tvm.Session.EnterSketch(Plane3.XY, alignView: false);
    var originNode = new BrowserNode { Kind = NodeKind.Origin, OriginIndex = 1, Title = "XZ" };
    Check(tvm.CanLookAtNode(originNode) && tvm.LookAtNode(originNode), "Look at works on an origin plane row even while sketching");
}
// A plane made from the gizmo becomes the working reference; Live gizmo returns the gizmo to the global axes.
{
    var gs = new CadSession(); gs.EnterSketch(Plane3.XY, alignView: false); var gk = gs.Sketch!; gk.Set3DEnabled(gs, true); gk.SetDrawingMode(gs, flat: false, adjustView: false);
    gk.RotateDrawingPlane(gs, new(0, 35, 0));
    var tilted = gk.GizmoFrame;
    Check(tilted.U.DistanceTo(Vec3.UnitX) > .1, "the gizmo can be tilted");
    var made = gk.SaveDatum(gs, SketchDatumKind.Plane, "Tilted plane");
    Check(gk.ActiveDatumId == made.Id && gk.GizmoFrame.U.DistanceTo(tilted.U) < 1e-9, "the plane just made from the gizmo is selected and the gizmo stays in its orientation");
    var withPlane = new Overlay(); gs.BuildOverlay(withPlane);
    Check(withPlane.Labels.Any(l => l.Text == "Tilted plane"), "the plane made from the gizmo is drawn and named");
    gk.UseDatum(gs, null);
    Check(gk.ActiveDatumId == null && gk.GizmoFrame.U.DistanceTo(Vec3.UnitX) < 1e-9 && gk.GizmoFrame.V.DistanceTo(Vec3.UnitY) < 1e-9, "Live gizmo puts the gizmo back on the global axes");
    Check(gk.Datums.Any(d => d.Id == made.Id), "the saved plane is kept");
}
mainHost.Close(); Console.WriteLine($"SUCCESS: {checks} checks");
sealed class TestApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme()); Styles.Add(new StyleInclude(new Uri("avares://Prism/")) { Source = new Uri("avares://Prism/Styles/Theme.axaml") });
    }
}
static class NativeSearch
{
    [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern bool SetDllDirectory(string path);
    public static void Initialize() => SetDllDirectory(Path.GetFullPath("native/bin"));
}
