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
var checkbox = panel.GetVisualDescendants().OfType<CheckBox>().Single(p => p.Name == "Enable3DSketch");
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
checkbox.IsChecked = false; Dispatcher.UIThread.RunJobs();
Check(!sketch.Is3DEnabled && !sketch.GizmoEditing && sketch.DrawingPlanes.Count == count && !spatial.IsVisible, "returning to 2D hides controls without deleting other drawing planes");
var exported = sketch.ExportModel(); var restored = new SketchContext(sketch.RootPlane, exported);
Check(!restored.Is3DEnabled && restored.DrawingPlanes.Count == count, "saved mode and multi-plane geometry round-trip through sketch definitions");
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
Check(editProbe.ActivePlane==otherPlaneIndex&&editProbe.SelPoints.SetEquals(lockedSelection)&&editProbe.GizmoFrame.Origin.DistanceTo(lockedOrigin)<1e-6&&editProbe.GizmoTargetConfirmed,"a plain click on another point leaves the gizmo bound to its target");
TargetOtherPoint(-60,25,true);
Check(editProbe.SelPoints.Count==2&&editProbe.GizmoTargetConfirmed&&editProbe.GizmoFrame.Origin.DistanceTo(lockedOrigin)>1e-6,"Shift-clicking another point adds it and the bound gizmo moves to the average");
TargetOtherPoint(-60,25,true);Check(editProbe.SelPoints.SetEquals(lockedSelection)&&editProbe.GizmoFrame.Origin.DistanceTo(lockedOrigin)<1e-6,"Shift-clicking it again takes it back out");
var lockedCurve=otherPlane.PointAt(-45,17.5);var lockedPixel=probeSession.Camera.Project(lockedCurve)!.Value;
probeSession.PointerDown(new(lockedPixel.x,lockedPixel.y,true,false,false,false));probeSession.PointerUp(new(lockedPixel.x,lockedPixel.y,false,false,false,false));
Check(editProbe.SelPoints.SetEquals(lockedSelection)&&editProbe.GizmoFrame.Origin.DistanceTo(lockedOrigin)<1e-6,"clicking a curve or empty space outside Gizmo Handles does not move or clear the bound gizmo");
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
 Press("edit");Check(arrowSketch.GeometryEditing&&arrowSketch.GizmoEditing,"Edit Geometry button enters setup for arrow regression");
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
