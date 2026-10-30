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
    var start=s.Camera.Project(frame.Origin+direction*length*.7)!.Value;var finish=s.Camera.Project(frame.Origin+direction*(length*.7+distance))!.Value;
    tool.PointerDown(s,new(start.x,start.y,true,false,false,false));tool.PointerMove(s,new(finish.x,finish.y,true,false,false,false));tool.PointerUp(s,new(finish.x,finish.y,false,false,false,false));
}
try
{
    var obj=s.AddObject(null,"Cage",ObjKind.Form,form:FormOps.Box(30,30,30,2,2,2));s.SelectItems([SelItem.Of(obj)]);s.StartCommand(()=>new EditFormCommand());var form=(EditFormCommand)s.ActiveCommand!;var tool=(FormEditTool)form.Tool!;
    form.SetSelectionMode(FormMode.Vertex);form.Verts.UnionWith([0,1]);form.OnChanged();
    Check(form.Params.OfType<ActionParam>().All(a=>a.Label!="Crease…"),"Crease launcher is removed from Edit Form");
    vm.OpenGizmoPanel();Check(vm.ShowGizmoPanel&&!vm.ShowCommandPanel&&ReferenceEquals(s.ActiveCommand,form),"Gizmo has its own panel without replacing Edit Form");
    form.ClearSelection();vm.BeginGizmoPlacement();vm.BindGizmoSelection();Check(vm.GizmoPositioning&&vm.ShowGizmoPanel&&!tool.Binding.Bound,"binding an empty selection is rejected while placement remains available");vm.CancelGizmoPlacement();form.Verts.UnionWith([0,1]);form.OnChanged();
    var geometry=form.Work!.V.ToArray();vm.BeginGizmoPlacement();vm.GizmoOriginX=20;vm.GizmoOriginY=10;vm.GizmoOriginZ=25;
    Check(vm.GizmoPositioning&&geometry.SequenceEqual(form.Work.V),"numeric gizmo placement leaves the cage unchanged");
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

    var model=new SketchModel();int p0=model.AddPoint(0,0),p1=model.AddPoint(20,0);model.AddLine(p0,p1);s.EnterSketch(Plane3.XY,model);var sketch=s.Sketch!;sketch.Set3DEnabled(s,true);sketch.SetTool(s,new SketchGeometryEditTool());sketch.SelPoints.Add(p1);
    vm.OpenGizmoPanel();vm.BeginGizmoPlacement();vm.GizmoOriginX=7;vm.GizmoOriginY=9;vm.GizmoOriginZ=11;var sketchOrigin=sketch.GizmoFrame.Origin;
    Check(sketch.Model.P(p1).X==20&&sketch.Model.P(p1).Y==0,"3D sketch pivot placement leaves constrained geometry unchanged");vm.BindGizmoSelection();
    Check(sketch.GizmoTargetConfirmed&&!sketch.GizmoEditing&&Near(sketch.GizmoFrame.Origin,sketchOrigin)&&!vm.ShowGizmoPanel,"3D sketch binding preserves the independently placed pivot");
    sketch.SetGizmoTransform(s,sketchOrigin+new Vec3(3,0,0),Vec3.Zero);Check(Math.Abs(sketch.Model.P(p1).X-23)<.001&&Near(sketch.GizmoFrame.Origin,sketchOrigin+new Vec3(3,0,0)),"bound sketch transforms use the placed frame through the constraint solver");sketchOrigin=sketch.GizmoFrame.Origin;
    vm.OpenGizmoPanel();vm.BeginGizmoPlacement();vm.GizmoOriginZ+=30;vm.CancelGizmoPlacement();Check(Near(sketch.GizmoFrame.Origin,sketchOrigin)&&!vm.GizmoPositioning,"canceling sketch placement restores its previous pivot");vm.CloseGizmoPanel();s.ExitSketch(false);
    Console.WriteLine($"SUCCESS: {checks} checks");
}
finally{AppSettings.Current.Gizmo=oldGizmo;}
sealed class TestApp:Application{public override void Initialize(){Styles.Add(new FluentTheme());Styles.Add(new StyleInclude(new Uri("avares://Prism/")){Source=new Uri("avares://Prism/Styles/Theme.axaml")});}}
static class Native{[DllImport("kernel32",CharSet=CharSet.Unicode)]static extern bool SetDllDirectory(string path);public static void Initialize()=>SetDllDirectory(Path.GetFullPath("native/bin"));}
