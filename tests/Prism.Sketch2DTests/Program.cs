using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;
using Prism.Core;
using Prism.Core.Sketching;
using Prism.Kernel;
using Prism.ViewModels;
using Prism.Views;
using Prism.Ribbon;

Native.SetDllDirectory(Path.GetFullPath("native/bin"));
AppBuilder.Configure<TestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
int checks=0;
void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
var vm=new MainViewModel{HomeOpen=false};vm.Session.EnterSketch(Plane3.XY,alignView:true);vm.Session.Ortho=true;
var view=new MainView{DataContext=vm};var host=new Window{Width=1500,Height=1000,Content=view};host.Show();Avalonia.Threading.Dispatcher.UIThread.RunJobs();
Check(!view.GetVisualDescendants().OfType<Button>().Any(b=>Equals(b.Content,"Edit Geometry")&&b.IsVisible),"2D hides the graphics Edit Geometry button");
Check(!view.GetVisualDescendants().OfType<CheckBox>().Any(b=>Equals(b.Content,"Grid snap")),"Grid Snap is absent from the upper-left drawing panel");
var snap=vm.QuickBar.OfType<RibbonButton>().Single(b=>b.Label=="Grid Snap");snap.Invoke();Check(vm.Session.SketchGridSnap,"viewport toolbar Grid Snap toggles actual snapping");snap.Invoke();
Check(SketchShortcuts.All.Any(s=>s.Gesture=="Shift"),"shared shortcut catalogue documents the axis modifier");

foreach(var mode in new[]{RectMode.TwoCorner,RectMode.Center})
foreach(double angle in new[]{30d,-25d,0d})
{
 var s=new CadSession();s.EnterSketch(Plane3.XY,alignView:true);var sk=s.Sketch!;var rect=new RectTool(mode);sk.SetTool(s,rect);rect.PanelSet(s,"orientation",angle);
 double radians=angle*Math.PI/180;double x=mode==RectMode.Center?20:40,y=mode==RectMode.Center?10:20;
 rect.Feed(s,20,20);rect.Feed(s,20+x*Math.Cos(radians)-y*Math.Sin(radians),20+x*Math.Sin(radians)+y*Math.Cos(radians));
 var sides=sk.Model.Entities.Where(e=>e.Type==EntType.Line&&!e.Construction).ToList();
 Check(sides.Count==4&&sk.Result.Converged,$"{mode} draws a constrained rectangle at {angle} degrees");
 var first=sides[0];var a=sk.Model.P(first.A);var b=sk.Model.P(first.B);
 Check(Math.Abs(Math.Atan2(b.Y-a.Y,b.X-a.X)*180/Math.PI-angle)<1e-5,"rectangle orientation matches requested angle");
 foreach(var ent in sk.Model.Entities)sk.SelEntities.Add(ent.Id);
 s.Key("Delete");Check(sk.Model.Entities.Count==0&&sk.Model.Points.All(p=>p.Id==0),"deleting a rectangle removes corners, center and construction diagonal");
 Check(sk.TryUndo(s)&&sk.Model.Entities.Count>=4,"complete rectangle deletion is undoable");
}
var lineSession=new CadSession();lineSession.EnterSketch(Plane3.XY,alignView:true);lineSession.Ortho=true;
var line=new LineTool();lineSession.Sketch!.SetTool(lineSession,line);line.Feed(lineSession,10,10);
void Place(double u,double v,bool shift){var px=lineSession.Camera.Project(new Vec3(u,v,0))!.Value;line.PointerMove(lineSession,new(px.x,px.y,false,shift,false,false));line.PointerDown(lineSession,new(px.x,px.y,true,shift,false,false));}
Place(40,27,true);var l1=lineSession.Sketch.Model.Entities.Single();
Check(Math.Abs(lineSession.Sketch.Model.P(l1.B).Y-10)<1e-6,"Shift locks the nearest horizontal direction");
Place(53,60,true);var l2=lineSession.Sketch.Model.Entities.Last();
Check(Math.Abs(lineSession.Sketch.Model.P(l2.B).X-40)<1e-6,"Shift locks the nearest vertical direction");
Place(70,77,false);var l3=lineSession.Sketch.Model.Entities.Last();
Check(Math.Abs(lineSession.Sketch.Model.P(l3.B).X-70)<1e-5&&Math.Abs(lineSession.Sketch.Model.P(l3.B).Y-77)<1e-5,"releasing Shift restores free drawing");
var datum=vm.Session.Sketch!.SaveDatum(vm.Session,SketchDatumKind.Axis,"2D axis",0);
Check(!vm.Session.Sketch.Is3DEnabled,"creating an axis reference does not switch a 2D sketch into 3D");
vm.Session.Sketch.UseDatum(vm.Session,datum.Id);Check(vm.Session.Sketch.ActiveDatumId==datum.Id&&!vm.Session.Sketch.Is3DEnabled,"2D axis selection retains its active reference without changing mode");
var gizmo=vm.Session.Sketch.SaveDatum(vm.Session,SketchDatumKind.Gizmo,"2D reference");vm.Session.Sketch.UseDatum(vm.Session,gizmo.Id);
Check(vm.Session.Sketch.ActiveDatumId==gizmo.Id&&!vm.Session.Sketch.Is3DEnabled,"2D gizmo reference can be selected without entering spatial editing");
var point=vm.Session.Sketch.Model.AddPoint(33,44);vm.Session.Sketch.Model.AddPointEntity(point);vm.Session.Sketch.SelPoints.Add(point);vm.Session.Key("Delete");
Check(vm.Session.Sketch.Model.PointOrNull(point)==null,"standalone retained points can be deleted");
vm.Session.Sketch.SetTool(vm.Session,new SelectTool());
var axisPixel=vm.Session.Camera.Project(datum.Frame.Origin+datum.Frame.U*vm.Session.Camera.PixelSizeAt(datum.Frame.Origin)*38*.6)!.Value;
vm.Session.PointerDown(new(axisPixel.x,axisPixel.y,true,false,false,false));vm.Session.PointerUp(new(axisPixel.x,axisPixel.y,false,false,false,false));
Check(vm.Session.Sketch.ActiveDatumId==datum.Id,"clicking a datum axis shaft selects it in the 2D viewport");
var gizmoPixel=vm.Session.Camera.Project(gizmo.Frame.Origin)!.Value;
vm.Session.PointerDown(new(gizmoPixel.x,gizmoPixel.y,true,false,false,false));vm.Session.PointerUp(new(gizmoPixel.x,gizmoPixel.y,false,false,false,false));
Check(vm.Session.Sketch.ActiveDatumId==gizmo.Id&&!vm.Session.Sketch.Is3DEnabled,"clicking a datum gizmo origin selects it without switching to 3D");
var zero=new CadSession();zero.EnterSketch(Plane3.XY);var zeroRect=new RectTool(RectMode.TwoCorner);zero.Sketch!.SetTool(zero,zeroRect);zeroRect.Feed(zero,20,20);zeroRect.Feed(zero,20,50);
Check(zero.Sketch.Model.Entities.All(e=>e.Type==EntType.Point)&&zero.Sketch.Result.Converged,"zero-width rectangles retain points instead of creating invalid zero-length edges");
var hidden=new CadSession();hidden.EnterSketch(Plane3.XY);var centerRect=new RectTool(RectMode.Center);hidden.Sketch!.SetTool(hidden,centerRect);centerRect.Feed(hidden,20,20);centerRect.Feed(hidden,40,30);hidden.Sketch.Model.ShowConstruction=false;
foreach(var edge in hidden.Sketch.Model.Entities.Where(e=>!e.Construction))hidden.Sketch.SelEntities.Add(edge.Id);hidden.Key("Delete");
Check(hidden.Sketch.Model.Entities.Count==0&&hidden.Sketch.Model.Points.All(p=>p.Id==0),"deleting visible rectangle sides also removes the hidden center diagonal and center point");
foreach(var mode in new[]{EllipseMode.Center,EllipseMode.TwoPoint,EllipseMode.ThreePoint})
{
 var es=new CadSession();es.EnterSketch(Plane3.XY);var ellipse=new EllipseTool(mode);es.Sketch!.SetTool(es,ellipse);
 if(mode==EllipseMode.Center){ellipse.Feed(es,20,20);ellipse.Feed(es,40,20);ellipse.Feed(es,20,30);}
 else if(mode==EllipseMode.TwoPoint){ellipse.Feed(es,10,10);ellipse.Feed(es,50,30);}
 else{ellipse.Feed(es,10,20);ellipse.Feed(es,50,20);ellipse.Feed(es,30,35);}
 var shapes=es.Sketch.Model.ToShapes(es.Sketch.Plane);Check(shapes.Count==1&&es.Sketch.Result.Converged,$"{mode} ellipse produces valid exact geometry");foreach(var shape in shapes)shape.Dispose();
}
var arcSession=new CadSession();arcSession.EnterSketch(Plane3.XY);var arc=new ArcTool(ArcMode.ThreePoint);arcSession.Sketch!.SetTool(arcSession,arc);arc.Feed(arcSession,10,10);arc.Feed(arcSession,30,30);arc.Feed(arcSession,50,10);
var arcShapes=arcSession.Sketch.Model.ToShapes(arcSession.Sketch.Plane);Check(arcShapes.Count==1&&arcSession.Sketch.Result.Converged,"three-point arc produces valid exact geometry");foreach(var shape in arcShapes)shape.Dispose();
var control=new CadSession();control.EnterSketch(Plane3.XY);var centered=new RectTool(RectMode.Center);control.Sketch!.SetTool(control,centered);centered.Feed(control,20,20);centered.Feed(control,40,30);
int centerId=control.Sketch.Model.Points.Single(p=>Math.Abs(p.X-20)<1e-7&&Math.Abs(p.Y-20)<1e-7).Id;
int extra=control.Sketch.Model.AddLine(control.Sketch.Model.AddPoint(100,100),control.Sketch.Model.AddPoint(120,100));control.Sketch.SelEntities.Add(extra);control.Key("Delete");
Check(control.Sketch.Model.PointOrNull(centerId)!=null&&control.Sketch.Model.Constraints.Any(c=>c.Type==CType.Midpoint),"deleting unrelated geometry preserves a live rectangle's center control and constraint");
foreach(string kind in new[]{"three-point rectangle","polygon","slot"})
{
 var drawing=new CadSession();drawing.EnterSketch(Plane3.XY);var context=drawing.Sketch!;
 ClickTool tool=kind=="polygon"?new PolygonTool(false):kind=="slot"?new SlotTool():new RectTool(RectMode.ThreePoint);
 context.SetTool(drawing,tool);tool.Feed(drawing,20,20);tool.Feed(drawing,60,30);if(kind!="polygon")tool.Feed(drawing,30,50);
 var exact=context.Model.ToShapes(context.Plane);Check(exact.Count>0&&context.Result.Converged,kind+" creates valid exact geometry");foreach(var shape in exact)shape.Dispose();
 foreach(var entity in context.Model.Entities)context.SelEntities.Add(entity.Id);drawing.Key("Delete");
 Check(context.Model.Entities.Count==0&&context.Model.Points.All(p=>p.Id==0),kind+" deletes without leftover control points");
}
var offsetSession=new CadSession();offsetSession.EnterSketch(Plane3.XY);var offsetModel=offsetSession.Sketch!.Model;
int sourceLine=offsetModel.AddLine(offsetModel.AddPoint(10,10),offsetModel.AddPoint(50,10));offsetSession.Sketch.Solve(offsetSession);offsetSession.Sketch.SelEntities.Add(sourceLine);
var offset=new OffsetTool();offsetSession.Sketch.SetTool(offsetSession,offset);offset.Key(offsetSession,"Enter");offset.PanelSet(offsetSession,"dist",3d);offset.PanelSet(offsetSession,"apply",null);
Check(offsetSession.Sketch.Model.Entities.Count==2&&offsetSession.Sketch.Result.Converged,"2D Offset still applies after explicit source selection");
var shared=new CadSession();shared.EnterSketch(Plane3.XY,alignView:true);shared.Ortho=true;int sharedPoint=shared.Sketch!.Model.AddPoint(100,20);shared.Sketch.Model.AddPointEntity(sharedPoint);
var sharedLine=new LineTool();shared.Sketch.SetTool(shared,sharedLine);sharedLine.Feed(shared,20,20);var sharedPixel=shared.Camera.Project(new Vec3(100,20,0))!.Value;
sharedLine.PointerDown(shared,new(sharedPixel.x,sharedPixel.y,true,true,false,false));
Check(shared.Sketch.Model.Entities.Last(e=>e.Type==EntType.Line).B==sharedPoint,"Shift axis locking preserves a coincident snap to an existing endpoint");
var baseLock=new CadSession();baseLock.EnterSketch(Plane3.XY,alignView:true);baseLock.Ortho=true;var baseRect=new RectTool(RectMode.ThreePoint);baseLock.Sketch!.SetTool(baseLock,baseRect);baseRect.Feed(baseLock,20,20);
var basePx=baseLock.Camera.Project(new Vec3(50,35,0))!.Value;baseRect.PointerDown(baseLock,new(basePx.x,basePx.y,true,true,false,false));baseRect.Feed(baseLock,50,60);
var lockedEdge=baseLock.Sketch.Model.Entities.First(e=>e.Type==EntType.Line);
Check(Math.Abs(baseLock.Sketch.Model.P(lockedEdge.A).Y-baseLock.Sketch.Model.P(lockedEdge.B).Y)<1e-6&&baseLock.Sketch.Model.Constraints.Any(c=>c.Type==CType.Horizontal),"Shift locks a three-point rectangle's base and retains the constraint after release");
var tiny=new CadSession();tiny.EnterSketch(Plane3.XY,alignView:true);tiny.Ortho=true;var tinyRect=new RectTool(RectMode.TwoCorner);tiny.Sketch!.SetTool(tiny,tinyRect);tinyRect.Feed(tiny,20,20);tinyRect.Feed(tiny,22.8,22.8);tiny.Sketch.SetTool(tiny,new SelectTool());tiny.Sketch.BuildOverlay(tiny,new Overlay());
var tinyCorner=tiny.Camera.Project(new Vec3(22.8,22.8,0))!.Value;
tiny.PointerDown(new(tinyCorner.x,tinyCorner.y,true,false,false,false));tiny.PointerUp(new(tinyCorner.x,tinyCorner.y,false,false,false,false));
Check(tiny.Sketch.SelPoints.Count==1&&tiny.Sketch.SelConstraint<0,"2D corner points take priority over nearby constraint glyphs on small rectangles");
// Shared Shift behavior is checked through pointer preview and placement, not scripting feeds.
foreach(string kind in new[]{"midpoint line","fit spline","control spline","polygon","circumscribed polygon","slot","three-point arc","center arc","two-point square","center square"})
{
 var shifted=new CadSession();shifted.EnterSketch(Plane3.XY,alignView:true);shifted.Ortho=true;var context=shifted.Sketch!;
 ClickTool tool=kind switch{"midpoint line"=>new LineTool(1),"fit spline"=>new SplineTool(false),"control spline"=>new SplineTool(true),"polygon"=>new PolygonTool(false),"circumscribed polygon"=>new PolygonTool(true),"slot"=>new SlotTool(),"three-point arc"=>new ArcTool(ArcMode.ThreePoint),"center arc"=>new ArcTool(ArcMode.CenterStartEnd),"two-point square"=>new RectTool(RectMode.TwoCorner),_=>new RectTool(RectMode.Center)};
 context.SetTool(shifted,tool);tool.Feed(shifted,20,20);
 var target=shifted.Camera.Project(new Vec3(60,35,0))!.Value;
 tool.PointerMove(shifted,new(target.x,target.y,false,true,false,false));
 bool square=kind.EndsWith("square");
 Check(context.Cursor is { } constrained&&Math.Abs(constrained.U-60)<1e-5&&Math.Abs(constrained.V-(square?60:20))<1e-5,kind+" applies Shift in its preview");
 tool.PointerMove(shifted,new(target.x,target.y,false,false,false,false));
 Check(context.Cursor is { } free&&Math.Abs(free.U-60)<1e-5&&Math.Abs(free.V-35)<1e-5,kind+" releases its temporary lock");
 tool.PointerDown(shifted,new(target.x,target.y,true,true,false,false));
 if(kind=="slot"||kind=="three-point arc")
 {
   var bend=shifted.Camera.Project(new Vec3(40,45,0))!.Value;
   tool.PointerMove(shifted,new(bend.x,bend.y,false,true,false,false));
   Check(context.Cursor is { } width&&Math.Abs(width.U-40)<1e-5&&Math.Abs(width.V-45)<1e-5,kind+" does not axis-lock its width/bend step");
   tool.PointerDown(shifted,new(bend.x,bend.y,true,true,false,false));
 }
 if(kind=="center arc")
 {
   var end=shifted.Camera.Project(new Vec3(35,60,0))!.Value;
   tool.PointerMove(shifted,new(end.x,end.y,false,true,false,false));tool.PointerDown(shifted,new(end.x,end.y,true,true,false,false));
   var arcEntity=context.Model.Entities.Single(e=>e.Type==EntType.Arc);var endpoint=context.Model.P(arcEntity.C);
   Check(Math.Abs(endpoint.X-20)<1e-5&&Math.Abs(endpoint.Y-60)<1e-5,"center arc end direction locks to a quadrant on its radius");
 }
 if(square)
 {
   Check(context.Model.Constraints.Any(c=>c.Type==CType.Equal),kind+" retains its equal-side constraint");
   var edges=context.Model.Entities.Where(e=>e.Type==EntType.Line&&!e.Construction).ToList();
   var lengths=edges.Select(e=>{var a=context.Model.P(e.A);var b=context.Model.P(e.B);return Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Y-b.Y,2));}).ToList();
   Check(edges.Count==4&&lengths.Max()-lengths.Min()<1e-6,kind+" commits equal sides");
 }
}
foreach(var mode in new[]{EllipseMode.Center,EllipseMode.TwoPoint,EllipseMode.ThreePoint})
{
 var circleSession=new CadSession();circleSession.EnterSketch(Plane3.XY,alignView:true);var ellipse=new EllipseTool(mode);circleSession.Sketch!.SetTool(circleSession,ellipse);ellipse.Feed(circleSession,20,20);
 if(mode==EllipseMode.ThreePoint)ellipse.Feed(circleSession,60,20);
 var px=circleSession.Camera.Project(mode==EllipseMode.ThreePoint?new Vec3(40,45,0):new Vec3(60,35,0))!.Value;
 ellipse.PointerDown(circleSession,new(px.x,px.y,true,true,false,false));
 Check(circleSession.Sketch.Model.Entities.Single().Type==EntType.Circle&&!ellipse.CircleLocked,"Shift preserves temporary circle proportions in "+mode);
}
var tangentSession=new CadSession();tangentSession.EnterSketch(Plane3.XY,alignView:true);tangentSession.Ortho=true;
var tangentModel=tangentSession.Sketch!.Model;tangentModel.AddLine(tangentModel.AddPoint(10,20),tangentModel.AddPoint(20,20));tangentSession.Sketch.Solve(tangentSession);
var tangentArc=new ArcTool(ArcMode.Tangent);tangentSession.Sketch.SetTool(tangentSession,tangentArc);tangentArc.Feed(tangentSession,20,20);
var tangentPixel=tangentSession.Camera.Project(new Vec3(35,60,0))!.Value;tangentArc.PointerDown(tangentSession,new(tangentPixel.x,tangentPixel.y,true,true,false,false));
Check(tangentSession.Sketch.Model.Entities.Any(e=>e.Type==EntType.Arc)&&tangentSession.Sketch.Result.Converged,"tangent arc accepts a Shift-locked endpoint while retaining tangency");
host.Close();Console.WriteLine($"SUCCESS: {checks} checks");
sealed class TestApp:Application{public override void Initialize(){Styles.Add(new FluentTheme());Styles.Add(new StyleInclude(new Uri("avares://Prism/")){Source=new Uri("avares://Prism/Styles/Theme.axaml")});}}
static class Native{[DllImport("kernel32",CharSet=CharSet.Unicode)]public static extern bool SetDllDirectory(string path);}
