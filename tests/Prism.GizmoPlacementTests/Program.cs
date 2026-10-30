using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Prism.Commands;
using Prism.Core;
using Prism.Core.Form;
using Prism.Core.Sketching;
using Prism.Kernel;
using Prism.ViewModels;
using Prism.Views;

Native.Initialize();AppBuilder.Configure<TestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions{UseHeadlessDrawing=false}).UseSkia().SetupWithoutStarting();
int checks=0;void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS "+message);checks++;}
bool Near(Vec3 a,Vec3 b)=>a.DistanceTo(b)<.001;
var oldGizmo=AppSettings.Current.Gizmo;AppSettings.Current.Gizmo=new();
var vm=new MainViewModel();var s=vm.Session;s.Camera.Width=1000;s.Camera.Height=700;s.Camera.Distance=230;s.Camera.Target=new(0,0,20);s.Camera.Orthographic=true;
void Move(IViewportTool tool,GizmoBinding binding,double distance)
{
    var frame=binding.Frame!.Value;var direction=frame.U;double length=GizmoVisual.Length(s,frame.Origin);
    double at=.7;foreach(var k in new[]{.7,.85,.95,.55}){var q=s.Camera.Project(frame.Origin+direction*length*k)!.Value;if(GizmoVisual.Hit(s,q.x,q.y,_=>frame.Origin,binding.Axes,0,planes:true).axis==0){at=k;break;}}
    var start=s.Camera.Project(frame.Origin+direction*length*at)!.Value;var finish=s.Camera.Project(frame.Origin+direction*(length*at+distance))!.Value;
    tool.PointerDown(s,new(start.x,start.y,true,false,false,false));tool.PointerMove(s,new(finish.x,finish.y,true,false,false,false));tool.PointerUp(s,new(finish.x,finish.y,false,false,false,false));
}
try
{
    var obj=s.AddObject(null,"Cage",ObjKind.Form,form:FormOps.Box(30,30,30,2,2,2));s.SelectItems([SelItem.Of(obj)]);s.StartCommand(()=>new EditFormCommand());var form=(EditFormCommand)s.ActiveCommand!;var tool=(FormEditTool)form.Tool!;
    form.SetSelectionMode(FormMode.Vertex);form.Verts.UnionWith([0,1]);form.OnChanged();
    Check(form.Params.OfType<ActionParam>().All(a=>a.Label!="Crease…"),"Crease launcher is removed from Edit Form");
    vm.OpenGizmoFromRibbon();Check(vm.GizmoPositioning&&Near(tool.Binding.Frame!.Value.Origin,Vec3.Zero),"a new ribbon gizmo starts at the world origin");vm.CancelGizmoPlacement();vm.CloseGizmoPanel();
    s.ViewportCursor=new Vec3(12,13,0);vm.OpenGizmoFromHotkey();Check(vm.GizmoPositioning&&Near(tool.Binding.Frame!.Value.Origin,new Vec3(12,13,0)),"the gizmo shortcut starts at the viewport cursor");vm.CancelGizmoPlacement();vm.CloseGizmoPanel();s.ViewportCursor=null;
    vm.OpenGizmoPanel();Check(vm.ShowGizmoPanel&&!vm.ShowCommandPanel&&ReferenceEquals(s.ActiveCommand,form),"Gizmo has its own panel without replacing Edit Form");
    form.ClearSelection();vm.BeginGizmoPlacement();vm.BindGizmoSelection();Check(vm.GizmoPositioning&&vm.ShowGizmoPanel&&!tool.Binding.Bound,"binding an empty selection is rejected while placement remains available");vm.CancelGizmoPlacement();form.Verts.UnionWith([0,1]);form.OnChanged();
    var geometry=form.Work!.V.ToArray();vm.BeginGizmoPlacement();vm.GizmoOriginX=20;vm.GizmoOriginY=10;vm.GizmoOriginZ=25;
    Check(vm.GizmoPositioning&&geometry.SequenceEqual(form.Work.V),"numeric gizmo placement leaves the cage unchanged");
    vm.GizmoRotationZ=90;Check(Vec3.Dot(tool.Binding.Frame!.Value.U,Vec3.UnitY)>.999&&geometry.SequenceEqual(form.Work.V),"numeric rotation changes only the gizmo frame");vm.GizmoRotationZ=0;
    var pivot=tool.Binding.Frame!.Value.Origin;Move(tool,tool.Binding,12);
    Check(Near(tool.Binding.Frame!.Value.Origin,pivot+Vec3.UnitX*12)&&geometry.SequenceEqual(form.Work.V),"dragging a move arrow repositions only the Freeform gizmo");
    vm.GizmoActiveOperation=1;var frame=tool.Binding.Frame!.Value;double length=GizmoVisual.Length(s,frame.Origin);
    var ring=GizmoVisual.Ring(frame.Origin,frame.U,frame.V,length).First(p=>{var q=s.Camera.Project(p)!.Value;var hit=GizmoVisual.Hit(s,q.x,q.y,_=>frame.Origin,tool.Binding.Axes,1);return hit.axis==2&&hit.operation==1;});
    var relative=ring-frame.Origin;var turned=frame.Origin+frame.U*(-Vec3.Dot(relative,frame.V))+frame.V*Vec3.Dot(relative,frame.U);
    var a=s.Camera.Project(ring)!.Value;var b=s.Camera.Project(turned)!.Value;tool.PointerDown(s,new(a.x,a.y,true,false,false,false));tool.PointerMove(s,new(b.x,b.y,true,false,false,false));tool.PointerUp(s,new(b.x,b.y,false,false,false,false));
    Check(Vec3.Dot(tool.Binding.Frame!.Value.U,frame.V)>.99&&geometry.SequenceEqual(form.Work.V),"rotation rings orient only the unbound gizmo");
    var placed=tool.Binding.Frame!.Value;vm.BindGizmoSelection();
    Check(tool.Binding.Bound&&!vm.ShowGizmoPanel&&vm.ShowCommandPanel&&tool.Binding.Frame==placed,"binding preserves the placed frame and returns to Edit Form");
    vm.GizmoActiveOperation=0;Move(tool,tool.Binding,8);
    Check(Near(form.Work.V[0],geometry[0]+placed.U*8)&&Near(form.Work.V[1],geometry[1]+placed.U*8),"bound cage transforms follow the placed local axis");
    Check(Near(tool.Binding.Frame!.Value.Origin,placed.Origin+placed.U*8),"bound pivot follows a translation of its geometry");
    var beforeRebind=form.Work.V.ToArray();vm.OpenGizmoPanel();vm.BeginGizmoPlacement();vm.GizmoOriginY+=7;var reboundFrame=tool.Binding.Frame!.Value;
    form.Verts.Add(2);form.OnChanged();vm.BindGizmoSelection();
    Check(tool.Binding.Bound&&tool.Binding.Frame==reboundFrame&&beforeRebind.SequenceEqual(form.Work.V),"repositioning and rebinding from two cage vertices to three leaves geometry unchanged");
    Move(tool,tool.Binding,4);
    Check(new[]{0,1,2}.All(i=>Near(form.Work.V[i],beforeRebind[i]+reboundFrame.U*4)),"the rebound gizmo transforms all three cage vertices");
    var moved=form.Work.V.ToArray();var bound=tool.Binding.Frame;vm.OpenGizmoPanel();vm.BeginGizmoPlacement();vm.GizmoOriginX+=20;vm.CloseGizmoPanel();
    Check(tool.Binding.Frame==bound&&moved.SequenceEqual(form.Work.V)&&vm.ShowCommandPanel,"leaving placement without binding restores the prior frame and editor");
    form.ClearSelection();form.Verts.Add(2);form.OnChanged();tool.BuildOverlay(s,new Overlay());Check(!tool.Binding.Bound&&tool.Binding.Frame==null,"changing the selection releases the previous binding");
    var panel=new GizmoOptionsPanel{DataContext=vm};vm.OpenGizmoPanel();vm.BeginGizmoPlacement();var window=new Window{Width=394,Height=900,Content=new ScrollViewer{Content=panel}};window.Show();Dispatcher.UIThread.RunJobs();
    string output=Path.GetFullPath("artifacts/gizmo-panel-review");Directory.CreateDirectory(output);
    foreach(var theme in new[]{ThemeVariant.Dark,ThemeVariant.Light}){Application.Current!.RequestedThemeVariant=theme;Dispatcher.UIThread.RunJobs();using var bitmap=new RenderTargetBitmap(new PixelSize(394,900));bitmap.Render((Visual)window.Content!);bitmap.Save(Path.Combine(output,"gizmo-"+theme.Key+".png"));}
    vm.CloseGizmoPanel();window.Close();s.CancelCommand();
    GizmoSettings.Current.AxisX=false;var hiddenOverlay=new Overlay();GizmoVisual.DrawOperation(hiddenOverlay,s,Vec3.Zero,GizmoVisual.WorldAxes,0,0);
    var hiddenPoint=s.Camera.Project(Vec3.UnitX*GizmoVisual.Length(s,Vec3.Zero)*.7)!.Value;
    Check(hiddenOverlay.Lines.All(line=>line.Color!=GizmoVisual.AxisColors[0])&&GizmoVisual.Hit(s,hiddenPoint.x,hiddenPoint.y,_=>Vec3.Zero,GizmoVisual.WorldAxes,0).axis!=0,"hidden axes have neither drawn handles nor active hit targets");GizmoSettings.Current.AxisX=true;

    var curve=s.AddObject(Occ.NurbsCurve([new(0,0,0),new(20,0,10),new(35,10,0),new(50,0,0)]),"Curve",ObjKind.Curve);s.SelectItems([SelItem.Of(curve)]);s.StartCommand(()=>new EditPointsCommand());var points=(EditPointsCommand)s.ActiveCommand!;var pointTool=(EditPointsTool)points.Tool!;pointTool.Selected.UnionWith([0,1]);points.SelectionChanged();var poles=points.Model!.Points.ToArray();
    vm.OpenGizmoPanel();vm.BeginGizmoPlacement();vm.GizmoOriginX=15;vm.GizmoOriginY=-12;vm.GizmoOriginZ=8;var pointPivot=pointTool.Binding.Frame!.Value.Origin;Move(pointTool,pointTool.Binding,6);
    Check(poles.SequenceEqual(points.Model.Points)&&Near(pointTool.Binding.Frame!.Value.Origin,pointPivot+Vec3.UnitX*6),"NURBS gizmo placement does not move control points");
    vm.BindGizmoSelection();Move(pointTool,pointTool.Binding,5);Check(Near(points.Model.Points[0],poles[0]+Vec3.UnitX*5)&&Near(points.Model.Points[1],poles[1]+Vec3.UnitX*5),"binding enables NURBS control-point transforms");s.CancelCommand();

    var model=new SketchModel();int p0=model.AddPoint(0,0),p1=model.AddPoint(20,0),p2=model.AddPoint(45,0);model.AddLine(p0,p1);s.EnterSketch(Plane3.XY,model);var sketch=s.Sketch!;sketch.Set3DEnabled(s,true);sketch.SetTool(s,new SketchGeometryEditTool());sketch.SelPoints.Add(p1);
    vm.OpenGizmoPanel();vm.BeginGizmoPlacement();vm.GizmoOriginX=7;vm.GizmoOriginY=9;vm.GizmoOriginZ=11;var sketchOrigin=sketch.GizmoFrame.Origin;
    Check(sketch.Model.P(p1).X==20&&sketch.Model.P(p1).Y==0,"3D sketch pivot placement leaves constrained geometry unchanged");vm.BindGizmoSelection();
    Check(sketch.GizmoTargetConfirmed&&!sketch.GizmoEditing&&Near(sketch.GizmoFrame.Origin,sketchOrigin)&&!vm.ShowGizmoPanel,"3D sketch binding preserves the independently placed pivot");
    sketch.SetGizmoTransform(s,sketchOrigin+new Vec3(3,0,0),Vec3.Zero);Check(Math.Abs(sketch.Model.P(p1).X-23)<.001&&Near(sketch.GizmoFrame.Origin,sketchOrigin+new Vec3(3,0,0)),"bound sketch transforms use the placed frame through the constraint solver");sketchOrigin=sketch.GizmoFrame.Origin;
    vm.OpenGizmoPanel();vm.BeginGizmoPlacement();vm.GizmoOriginZ+=20;var customPivot=sketch.GizmoFrame.Origin;
    var extra=s.Camera.Project(sketch.Plane.PointAt(sketch.Model.P(p2).X,sketch.Model.P(p2).Y))!.Value;
    s.PointerDown(new(extra.x,extra.y,true,true,false,false));s.PointerUp(new(extra.x,extra.y,false,true,false,false));
    Check(sketch.SelPoints.Contains(p2)&&Near(sketch.GizmoFrame.Origin,customPivot),"adding a sketch target during placement preserves the independently positioned pivot");
    vm.BindGizmoSelection();Check(sketch.GizmoTargetConfirmed&&Near(sketch.GizmoFrame.Origin,customPivot),"rebinding a sketch preserves its positioned pivot");sketchOrigin=customPivot;
    vm.OpenGizmoPanel();vm.BeginGizmoPlacement();vm.GizmoOriginZ+=30;double previousSize=sketch.GizmoSize;vm.GizmoPlaneSize=2;Check(sketch.GizmoSize==2,"plane size changes the sketch gizmo display");vm.CancelGizmoPlacement();Check(Near(sketch.GizmoFrame.Origin,sketchOrigin)&&sketch.GizmoSize==previousSize&&!vm.GizmoPositioning,"canceling sketch placement restores its previous pivot and plane size");vm.CloseGizmoPanel();s.ExitSketch(false);
    s.ViewportCursor=new Vec3(40,25,0);vm.OpenGizmoFromHotkey();
    Check(s.DatumVisible&&vm.GizmoIsDatum&&Near(s.Datum!.Frame.Origin,new Vec3(40,25,0)),"in plain model view the hotkey places a datum gizmo at the cursor");
    s.ViewportCursor=null;vm.OpenGizmoFromHotkey();Check(Near(s.Datum!.Frame.Origin,Vec3.Zero),"without a viewport cursor the datum gizmo appears at the origin");
    vm.GizmoOriginX=10;vm.GizmoOriginY=5;Check(Near(s.Datum.Frame.Origin,new Vec3(10,5,0)),"numeric fields move the datum gizmo");
    vm.GizmoRotationZ=90;Check(Vec3.Dot(s.Datum.Frame.U,Vec3.UnitY)>.999,"numeric rotation turns the datum gizmo");vm.GizmoRotationZ=0;
    Move(s.Datum,s.Datum.Binding,15);Check(Near(s.Datum.Frame.Origin,new Vec3(25,5,0)),"dragging the arrow moves the datum gizmo");
    s.Datum.Mode=1;var ds=s.Datum.Frame;double dl=GizmoVisual.Length(s,ds.Origin);
    vm.CloseGizmoPanel();Check(s.DatumVisible,"closing the panel keeps the datum gizmo");
    Check(s.Key("Escape")&&!s.DatumVisible,"Escape hides the datum gizmo");
    vm.OpenGizmoFromRibbon();Check(s.DatumVisible&&Near(s.Datum!.Frame.Origin,Vec3.Zero),"the ribbon places the datum gizmo at the origin");vm.CloseGizmoPanel();s.HideDatumGizmo();
    Console.WriteLine($"SUCCESS: {checks} checks");
}
finally{AppSettings.Current.Gizmo=oldGizmo;}
sealed class TestApp:Application{public override void Initialize(){Styles.Add(new FluentTheme());Styles.Add(new StyleInclude(new Uri("avares://Prism/")){Source=new Uri("avares://Prism/Styles/Theme.axaml")});}}
static class Native{[DllImport("kernel32",CharSet=CharSet.Unicode)]static extern bool SetDllDirectory(string path);public static void Initialize()=>SetDllDirectory(Path.GetFullPath("native/bin"));}
