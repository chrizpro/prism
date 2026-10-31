using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;using Avalonia.Controls;using Avalonia.Headless;using Avalonia.Markup.Xaml.Styling;using Avalonia.Themes.Fluent;
using Prism.Core;using Prism.Core.Sketching;using Prism.Kernel;using Prism.Commands;
Native.SetDllDirectory(Path.GetFullPath("native/bin"));AppBuilder.Configure<TestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
int checks=0;void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
CadSession Session(){var s=new CadSession();s.EnterSketch(Plane3.XY,alignView:true);return s;}
void Click(CadSession s,double x,double y,bool shift=false){var p=s.Camera.Project(s.Sketch!.Plane.PointAt(x,y))!.Value;s.PointerDown(new(p.x,p.y,true,shift,false,false));s.PointerUp(new(p.x,p.y,false,shift,false,false));}
var orthographic=Session();Check(orthographic.Ortho&&orthographic.Camera.Orthographic,"sketch entry defaults to orthographic");
var numeric=Session();var numericRect=new RectTool(RectMode.TwoCorner);numeric.Sketch!.SetTool(numeric,numericRect);Click(numeric,10,10);var numericPointer=numeric.Camera.Project(new Vec3(60,40,0))!.Value;numericRect.PointerMove(numeric,new(numericPointer.x,numericPointer.y,false,false,false,false));numericRect.PanelSet(numeric,"draw-0",30d);numericRect.PanelSet(numeric,"draw-1",12d);
Check(numeric.Sketch.Cursor is { } numericPreview&&Math.Abs(numericPreview.U-40)<1e-5&&Math.Abs(numericPreview.V-22)<1e-5&&numeric.Sketch.Model.Entities.Count==0,"right-panel dimensions update the live rectangle preview without committing geometry");numericRect.PanelSet(numeric,"place",null);
Check(numeric.Sketch.Model.Entities.Count(e=>e.Type==EntType.Line)==4&&numeric.Sketch.Result.Converged,"right-panel Place point commits the numeric rectangle");
foreach(var mode in new[]{RectMode.TwoCorner,RectMode.Center})
{
 var s=Session();var rect=new RectTool(mode);s.Sketch!.SetTool(s,rect);rect.Feed(s,20,20);var preview=s.Camera.Project(new Vec3(60,40,0))!.Value;rect.PointerMove(s,new(preview.x,preview.y,false,false,false,false));rect.Key(s,"Char:R");
 double a=37*Math.PI/180;var rotation=s.Camera.Project(new Vec3(20+50*Math.Cos(a),20+50*Math.Sin(a),0))!.Value;rect.PointerMove(s,new(rotation.x,rotation.y,false,false,false,false));rect.PointerDown(s,new(rotation.x,rotation.y,true,false,false,false));
 var edge=s.Sketch.Model.Entities.First(e=>e.Type==EntType.Line&&!e.Construction);var p=s.Sketch.Model.P(edge.A);var q=s.Sketch.Model.P(edge.B);
 Check(Math.Abs(Math.Atan2(q.Y-p.Y,q.X-p.X)*180/Math.PI-37)<1e-4,mode+" rotates interactively without changing its drawing method");
 Check(s.Sketch.Result.Converged,mode+" rotated rectangle retains valid constraints");
}
var patternSession=Session();var model=patternSession.Sketch!.Model;int first=model.AddLine(model.AddPoint(10,10),model.AddPoint(40,10)),second=model.AddLine(model.AddPoint(10,30),model.AddPoint(40,30));patternSession.Sketch.Solve(patternSession);
var pattern=new SketchPatternTool(false);patternSession.Sketch.SetTool(patternSession,pattern);Click(patternSession,25,10);Click(patternSession,25,30);
Check(patternSession.Sketch.SelEntities.SetEquals(new[]{first,second}),"Pattern accepts multiple source picks after tool activation");Check(model.Entities.Count==2,"Pattern selection does not start placement prematurely");pattern.Key(patternSession,"Enter");Click(patternSession,50,50);Click(patternSession,70,50);
Check(patternSession.Sketch.Model.Entities.Count==6,"Pattern places copies after explicit selection completion");
Click(patternSession,25,10);Check(patternSession.Sketch.SelEntities.Contains(first)&&pattern.Panel!.Single(c=>c.Id=="selection").Label.StartsWith("Done selecting"),"Pattern returns to source selection for its next use");
var referenceSession=Session();var referenceSketch=referenceSession.Sketch!;
var rectangle=new RectTool(RectMode.TwoCorner);referenceSketch.SetTool(referenceSession,rectangle);rectangle.Feed(referenceSession,10,10);rectangle.Feed(referenceSession,20,30);
var axis=referenceSketch.SaveDatum(referenceSession,SketchDatumKind.Axis,"Revolve axis",1);var plane=referenceSketch.SaveDatum(referenceSession,SketchDatumKind.Plane,"Offset datum",normalOffset:15);
referenceSession.ExitSketch(true);var obj=referenceSession.Objects.Single();int axisIndex=obj.SketchData!.Datums.FindIndex(d=>d.Id==axis.Id),planeIndex=obj.SketchData.Datums.FindIndex(d=>d.Id==plane.Id);
var axisItem=new SelItem(obj.Id,SelKind.Datum,axisIndex);var planeItem=new SelItem(obj.Id,SelKind.Datum,planeIndex);
Check(referenceSession.Matches(axisItem,SelFilter.Axis)&&referenceSession.DatumOf(axisItem)!.Frame.U.DistanceTo(Vec3.UnitY)<1e-6,"saved datum axis is a usable modeling axis reference");
Check(referenceSession.Matches(planeItem,SelFilter.Plane)&&Math.Abs(referenceSession.PlaneOf(planeItem)!.Value.Origin.Z-15)<1e-6,"saved offset datum plane is a usable plane reference");
var axisFrame=referenceSession.DatumOf(axisItem)!.Frame;var axisScreen=referenceSession.Camera.Project(axisFrame.Origin+axisFrame.U*referenceSession.Camera.PixelSizeAt(axisFrame.Origin)*25)!.Value;
Check(referenceSession.Pick(axisScreen.x,axisScreen.y,SelFilter.Axis)?.Item==axisItem,"saved datum axis can be selected by clicking its visible shaft");
var planeFrame=referenceSession.PlaneOf(planeItem)!.Value;var planeScreen=referenceSession.Camera.Project(planeFrame.PointAt(5,5))!.Value;
Check(referenceSession.Pick(planeScreen.x,planeScreen.y,SelFilter.Plane)?.Item==planeItem,"saved datum plane can be selected in the viewport");
var revolve=new RevolveCommand();referenceSession.StartCommand(revolve);revolve.Params.OfType<ChoiceParam>().Single(p=>p.Label=="Axis").Index=3;
var profileSlot=revolve.Slots.Single(p=>p.Label=="Profile");profileSlot.Items.Clear();profileSlot.Items.Add(SelItem.Of(obj));var axisSlot=revolve.Slots.Single(p=>p.Label=="Axis line");axisSlot.Items.Add(axisItem);revolve.OnChanged();referenceSession.CommitCommand();
Check(referenceSession.Objects.Any(o=>o.Kind==ObjKind.Solid),"Revolve builds a solid using a saved datum axis");
var g=Session();var sk=g.Sketch!;int p1=sk.Model.AddPoint(20,20),p2=sk.Model.AddPoint(60,40);sk.Model.AddPointEntity(p1);sk.Model.AddPointEntity(p2);sk.Solve(g);var gizmo=sk.SaveDatum(g,SketchDatumKind.Gizmo);sk.UseDatum(g,gizmo.Id);
Check(sk.PlanarGizmoActive&&sk.GizmoEditing&&!sk.Is3DEnabled,"selecting a 2D datum gizmo enables planar transform setup without switching to 3D");Click(g,20,20);sk.SelPoints.Add(p2);sk.CenterGizmoOnSelection(g);
double length=g.Camera.PixelSizeAt(sk.GizmoFrame.Origin)*96;var handle=g.Camera.Project(sk.GizmoFrame.Origin+sk.GizmoFrame.U*length*.5)!.Value;var old=sk.Model.P(p1).X;
g.PointerDown(new(handle.x,handle.y,true,false,false,false));g.PointerMove(new(handle.x+35,handle.y,true,false,false,false));g.PointerUp(new(handle.x+35,handle.y,false,false,false,false));
Check(sk.Model.P(p1).X>old+1&&Math.Abs(sk.Model.P(p2).X-sk.Model.P(p1).X-40)<1e-4&&!sk.Is3DEnabled,"2D gizmo translates multiple points while preserving their separation");
void DragCenter(SketchGizmoOperation operation,double dx,double dy)
{sk.GizmoOperation=operation;var center=g.Camera.Project(sk.GizmoFrame.Origin)!.Value;g.PointerDown(new(center.x,center.y,true,false,false,false));g.PointerMove(new(center.x+dx,center.y+dy,true,false,false,false));g.PointerUp(new(center.x+dx,center.y+dy,false,false,false,false));}
var beforeRotation=(sk.Model.P(p2).X-sk.Model.P(p1).X,sk.Model.P(p2).Y-sk.Model.P(p1).Y);
DragCenter(SketchGizmoOperation.Rotate,45,0);
double Distance(){var a=sk.Model.P(p1);var b=sk.Model.P(p2);return Math.Sqrt(Math.Pow(b.X-a.X,2)+Math.Pow(b.Y-a.Y,2));}
Check(Math.Abs(Distance()-Math.Sqrt(2000))<1e-4&&Math.Abs(sk.Model.P(p2).Y-sk.Model.P(p1).Y-beforeRotation.Item2)>1&&!sk.Is3DEnabled,"2D gizmo rotates both selected points around their shared pivot");
double beforeScale=Distance();DragCenter(SketchGizmoOperation.Size,45,0);
Check(Distance()>beforeScale*1.1&&!sk.Is3DEnabled,"2D gizmo scales the selected point set");
var f=Session();var fm=f.Sketch!.Model;fm.AddLine(fm.AddPoint(10,10),fm.AddPoint(70,10));fm.AddLine(fm.AddPoint(10,10),fm.AddPoint(10,70));f.Sketch.Solve(f);var fillet=new FilletTool(false);f.Sketch.SetTool(f,fillet);Click(f,40,10);var near=f.Camera.Project(new Vec3(10,40,0))!.Value;fillet.PointerMove(f,new(near.x,near.y,false,false,false,false));var ghost=new Overlay();fillet.BuildOverlay(f,ghost);
Check(ghost.Lines.Count>0&&fm.Entities.Count==2,"Fillet previews its result without modifying source geometry");
fillet.PointerDown(f,new(near.x,near.y,true,false,false,false));fillet.PointerMove(f,new(near.x+12,near.y-8,true,false,false,false));fillet.PointerUp(f,new(near.x+12,near.y-8,false,false,false,false));
Check(f.Sketch.Model.Entities.Any(e=>e.Type==EntType.Arc)&&f.Sketch.Result.Converged,"Fillet supports viewport drag sizing and commit on release");
var d=Session();int line=d.Sketch!.Model.AddLine(d.Sketch.Model.AddPoint(10,10),d.Sketch.Model.AddPoint(70,10));d.Sketch.Solve(d);var dimension=new DimensionTool(DimMode.Smart);d.Sketch.SetTool(d,dimension);var down=d.Camera.Project(new Vec3(40,10,0))!.Value;var up=d.Camera.Project(new Vec3(40,30,0))!.Value;
dimension.PointerDown(d,new(down.x,down.y,true,false,false,false));dimension.PointerMove(d,new(up.x,up.y,true,false,false,false));var dimPreview=new Overlay();dimension.BuildOverlay(d,dimPreview);Check(dimPreview.Lines.Count>=3,"Smart Dimension previews extension lines during viewport dragging");dimension.PointerUp(d,new(up.x,up.y,false,false,false,false));
Check(dimension.Panel!.Any(c=>c.Id=="apply"&&c.Enabled),"dragged dimension exposes its value and Apply in the right panel");dimension.PanelSet(d,"apply",null);var constraint=d.Sketch.Model.Constraints.Single(c=>c.IsDimension);
Check(constraint.LabelU!=null&&constraint.LabelV!=null&&d.Sketch.Result.Converged,"dimension commits with the dragged layout and preserves its driving value");
Check(JsonSerializer.Deserialize<SketchModel>(JsonSerializer.Serialize(d.Sketch.ExportModel()))!.Constraints.Single(c=>c.IsDimension).LabelV==constraint.LabelV,"dimension layout survives serialization");
dimension.PointerDown(d,new(down.x,down.y,true,false,false,false));dimension.Key(d,"Enter");dimension.Key(d,"Escape");
Check(dimension.Panel!.Single(c=>c.Id=="apply").Enabled==false,"canceling a pending dimension clears its right-panel stage");
Console.WriteLine($"SUCCESS: {checks} checks");
sealed class TestApp:Application{public override void Initialize(){Styles.Add(new FluentTheme());Styles.Add(new StyleInclude(new Uri("avares://Prism/")){Source=new Uri("avares://Prism/Styles/Theme.axaml")});}}
static class Native{[DllImport("kernel32",CharSet=CharSet.Unicode)]public static extern bool SetDllDirectory(string path);}
