using System.Runtime.InteropServices;
using Prism.Core;
using Prism.Commands;
using Prism.Kernel;
Native.SetDllDirectory(Path.GetFullPath("native/bin"));int checks=0;
Directory.CreateDirectory(Path.GetFullPath("artifacts/flatten-refold-review"));
void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
IEnumerable<Vec3> Points(Shape shape){var m=shape.Tessellate();for(int i=0;i<m.EdgePoints.Length;i+=3)yield return new(m.EdgePoints[i],m.EdgePoints[i+1],m.EdgePoints[i+2]);}
foreach(bool rotated in new[]{false,true})
{
 var s=new CadSession();var shape=Occ.Cylinder(25,70);if(rotated)shape=Occ.Translate(Occ.Rotate(shape,Vec3.Zero,Vec3.UnitY,33),new(50,-20,10));var body=s.AddObject(shape,"Cylinder",ObjKind.Solid);double volume=body.Shape.Props().Volume;
 int face=Enumerable.Range(0,body.Shape.Count(SubKind.Face)).Single(i=>Occ.CylinderFrame(body.Shape.Sub(SubKind.Face,i))!=null);
 s.StartCommand(()=>new FlattenCommand());var flatten=(FlattenCommand)s.ActiveCommand!;flatten.Pick(new(body.Id,SelKind.Face,face),false);var preview=flatten.TryBuild();
 Check(preview is {Add.Count:2},"one cylindrical face previews a flat face and outline");
 Check(Math.Abs(preview!.Add[0].FlatMap!.Width-50*Math.PI)<1e-7&&Math.Abs(preview.Add[0].FlatMap!.Height-70)<1e-7,"analytic layout preserves circumference and axial height");s.CommitCommand();var flat=s.Objects.Single(o=>o.FlatMap!=null&&o.Kind==ObjKind.Surface);var map=flat.FlatMap!;
 Check(s.Find(body.Id)!.Shape.Count(SubKind.Face)==3&&Math.Abs(s.Find(body.Id)!.Shape.Props().Volume-volume)<1e-7,"flattening one face keeps both caps and the original solid unchanged");
 Check(flat.WithColor(0xFFFFFFFF).FlatMap==map&&flat.WithShape(Occ.Translate(flat.Shape,new(1,0,0))).FlatMap==map,"display and geometry updates retain the return mapping");
 using(var reference=Occ.FromBrep(map.ReferenceBrep!))using(var cylinderTarget=body.Shape.Sub(SubKind.Face,face))using(var detail=Occ.Translate(Occ.Box(6,6,2),new(map.FlatX+map.Width*.2,map.FlatY+10,0)))using(var wrappedDetail=Occ.WrapSurface(detail,reference,cylinderTarget,[1,1,1,0,0,0,0,0,0,.01]))
 {
  var center=new Vec3(map.Cylinder[0],map.Cylinder[1],map.Cylinder[2]);var z=new Vec3(map.Cylinder[3],map.Cylinder[4],map.Cylinder[5]);var x=new Vec3(map.Cylinder[6],map.Cylinder[7],map.Cylinder[8]);var y=Vec3.Cross(z,x);double angle=map.Cylinder[10]+(map.Cylinder[11]-map.Cylinder[10])*.2;
  using var expected=Occ.Vertex(center+z*(map.Cylinder[12]+10)+(x*Math.Cos(angle)+y*Math.Sin(angle))*map.Cylinder[9]);
  Check(wrappedDetail.HasSolids&&wrappedDetail.IsValid&&Occ.Distance(wrappedDetail,expected).Distance<.01,"solid wrapping agrees with the analytic cylinder mapping");
 }
 double cx=map.FlatX+map.Width*.5,cy=map.FlatY+35;
 var star=Enumerable.Range(0,40).Select(i=>{double a=i*Math.PI/20,r=i%2==0?26:21;return new Vec3(cx+r*Math.Cos(a),cy+r*Math.Sin(a),0);}).ToArray();
 var drawing=s.AddObject(Occ.Profile([Occ.Polyline(star,true),Occ.Circle(new(cx,cy,0),Vec3.UnitZ,7),Occ.Spline([new(cx-60,cy-15,0),new(cx-45,cy+15,0),new(cx-32,cy-15,0)])],false),"Complex profile",ObjKind.Curve);
 s.ClearSelection();s.StartCommand(()=>new RefoldCommand());var refold=(RefoldCommand)s.ActiveCommand!;refold.ConfigureInputs(()=>{refold.Params.OfType<BoolParam>().Single(p=>p.Label=="Create a copy of the curved face").Value=false;refold.Slots[0].Items.Add(SelItem.Of(flat));refold.Slots[1].Items.Add(SelItem.Of(drawing));});refold.OnChanged();var result=refold.TryBuild();Check(result is {Add.Count:1}&&refold.Error==null,"complex star, circle and spline refold successfully");var wrapped=result!.Add[0].Shape!;
 Check(wrapped.IsValid&&wrapped.Count(SubKind.Edge)==drawing.Shape.Count(SubKind.Edge),"refold preserves all drawing edges in valid B-rep geometry");
 var origin=new Vec3(map.Cylinder[0],map.Cylinder[1],map.Cylinder[2]);var axis=new Vec3(map.Cylinder[3],map.Cylinder[4],map.Cylinder[5]);double error=Points(wrapped).Max(p=>{var d=p-origin;return Math.Abs((d-axis*Vec3.Dot(d,axis)).Length-25);});Check(error<.00005,"refolded geometry lies on the original cylinder within 0.00005 mm");
 refold.Params.OfType<BoolParam>().Single(p=>p.Label=="Create a copy of the curved face").Value=true;var withFace=refold.TryBuild();Check(withFace is {Add.Count:2}&&withFace.Add[1].Kind==ObjKind.Surface,"Refold can return the curved face as well as its drawing");Check(withFace!.Add[1].Shape!.IsValid&&Math.Abs(withFace.Add[1].Shape!.Props().Area-2*Math.PI*25*70)<.00001,"returned face restores the original cylindrical area exactly");
 s.CommitCommand();s.Undo();Check(s.Objects.All(o=>!o.Name.StartsWith("Refolded sketch")),"refold is one undo step");s.Redo();Check(s.Objects.Any(o=>o.Name.StartsWith("Refolded sketch")),"redo restores the wrapped profile");
 string path=Path.GetFullPath("artifacts/flatten-refold-review/roundtrip-test.prism");s.Save(path);var opened=new CadSession();opened.Open(path);var saved=opened.Objects.Single(o=>o.Id==flat.Id);Check(saved.FlatMap!=null&&saved.FlatMap.SourceId==body.Id,"the return map and original object identities survive save/open");
 opened.ClearSelection();opened.StartCommand(()=>new RefoldCommand());var again=(RefoldCommand)opened.ActiveCommand!;again.ConfigureInputs(()=>{again.Slots[0].Items.Add(SelItem.Of(saved));again.Slots[1].Items.Add(SelItem.Of(opened.Find(drawing.Id)!));});again.OnChanged();Check(again.TryBuild()!=null,"a reopened flat pattern can still refold its drawing");
 var outside=s.AddObject(Occ.Line(new(map.FlatX-10,cy,0),new(map.FlatX-5,cy,0)),"Outside",ObjKind.Curve);s.ClearSelection();s.StartCommand(()=>new RefoldCommand());var bad=(RefoldCommand)s.ActiveCommand!;bad.ConfigureInputs(()=>{bad.Slots[0].Items.Add(SelItem.Of(flat));bad.Slots[1].Items.Add(SelItem.Of(outside));});bad.OnChanged();Check(bad.TryBuild()==null&&bad.Error!.Contains("inside"),"out-of-pattern drawings are rejected rather than silently misplaced");
}
// General UV layouts and B-rep mapping: curved NURBS targets, sheets and solids.
var grid=Enumerable.Range(0,25).Select(i=>{double u=i/5d%1;int r=i/5,c=i%5;return new Vec3(c*20,r*15,8*Math.Sin(c*Math.PI/4)*Math.Sin(r*Math.PI/4));}).ToArray();
using var target=Occ.NurbsSurface(grid,5,5,3,3);
using var layout=Occ.UnwrapSurface(target,0,0,out double width,out double height);
Check(layout.IsValid&&width>50&&height>50,$"general NURBS face unwraps to a valid UV layout ({width:0.##} x {height:0.##})");
double[] settings=[1,1,1,0,0,0,0,0,0,.01];
using var curve=Occ.Line(new(width*.2,height*.3,0),new(width*.8,height*.7,0));
using var mappedCurve=Occ.WrapSurface(curve,layout,target,settings);
Check(mappedCurve.IsValid&&mappedCurve.Count(SubKind.Edge)==1,"curves wrap onto a general NURBS target");
using var sheet=Occ.Profile([Occ.Polyline([new(width*.2,height*.2,0),new(width*.4,height*.2,0),new(width*.4,height*.4,0),new(width*.2,height*.4,0)],true)],true);
using var mappedSheet=Occ.WrapSurface(sheet,layout,target,settings);
Check(mappedSheet.IsValid&&mappedSheet.Count(SubKind.Face)==1,"trimmed sheets wrap onto a general NURBS target");
var flatSamples=Occ.SurfaceSamples(sheet,11,11);var wrappedSamples=Occ.SurfaceSamples(mappedSheet,11,11);var targetSamples=Occ.SurfaceSamples(target,101,101);
double maxMappingError=flatSamples.Select((p,i)=>(wrappedSamples[i]-targetSamples[(int)Math.Round(p.X/width*100)*101+(int)Math.Round(p.Y/height*100)]).Length).Max();
Check(maxMappingError<.01,$"surface mapping agrees with independently sampled target coordinates ({maxMappingError:0.######} mm)");
using var box=Occ.Translate(Occ.Box(width*.15,height*.15,2),new(width*.2,height*.2,0));
using var mappedSolid=Occ.WrapSurface(box,layout,target,settings);
Check(mappedSolid.IsValid&&mappedSolid.HasSolids&&mappedSolid.Props().Volume>0,"solid topology and volume survive normal-offset surface mapping");
double[] altered=[.7,.8,.5,2,3,4,1,1,1,.01];
using var alteredSolid=Occ.WrapSurface(box,layout,target,altered);
Check(alteredSolid.IsValid&&alteredSolid.HasSolids&&alteredSolid.Props().Volume>0,"scale, offsets, UV swap, mirror and normal flip preserve valid solid orientation");
using var lineReference=Occ.Line(new(0,0,0),new(100,0,0));
using var targetPath=Occ.Spline([new(0,0,0),new(30,15,5),new(60,-5,10),new(100,20,20)]);
using var pathBox=Occ.Translate(Occ.Box(30,3,2),new(20,0,0));
using var bent=Occ.WrapAlongCurve(pathBox,lineReference,targetPath,settings);
Check(bent.IsValid&&bent.HasSolids&&bent.Props().Volume>0,"curve-to-curve mapping wraps solids through a spatial path and inflection");
var workflow=new CadSession();var targetObj=workflow.AddObject(Occ.FromBrep(target.ToBrep()),"Target",ObjKind.Surface);
workflow.SelectItems([SelItem.Of(targetObj)]);workflow.StartCommand(()=>new FlattenCommand());
workflow.ActiveCommand!.Restore(new FeatureRecipe(()=>new FlattenCommand(),"Flatten",[true,1,10d,.01d,0,"",null],[[SelItem.Of(targetObj)]]));
var legacy=workflow.ActiveCommand.TryBuild();Check(legacy is {Add.Count:>0} && Math.Abs(legacy.Add[0].Shape!.Props().Min.X)<.001,"old Flatten recipes migrate their input and placement settings");workflow.CancelCommand();
workflow.SelectItems([SelItem.Of(targetObj)]);workflow.StartCommand(()=>new FlattenCommand());workflow.CommitCommand();
var savedLayout=workflow.Objects.Single(o=>o.FlatMap!=null&&o.Kind==ObjKind.Surface);
Check(workflow.ActiveCommand==null&&savedLayout.FlatMap!.SourceFaceBrep!=null,"Keep flat and exit retains a persistent return mapping");
workflow.SelectItems([SelItem.Of(savedLayout)]);workflow.StartCommand(()=>new FlattenCommand());
Check(workflow.ActiveCommand!.Params.OfType<ChoiceParam>().Single(p=>p.Label=="Operation").Index==1,"reopening the combined tool on a layout selects Refold");
workflow.CommitCommand();Check(workflow.ActiveCommand==null&&!workflow.StatusIsError,"combined tool refolds a saved general surface layout");
string generalPath=Path.GetFullPath("artifacts/flatten-refold-review/general.prism");workflow.Save(generalPath);var reopened=new CadSession();reopened.Open(generalPath);
var reopenedFlat=reopened.Objects.Single(o=>o.Id==savedLayout.Id);reopened.SelectItems([SelItem.Of(reopenedFlat)]);reopened.StartCommand(()=>new FlattenCommand());reopened.CommitCommand();
Check(reopened.ActiveCommand==null&&!reopened.StatusIsError,"general source and reference mapping survives saving and reopening");
var localMap=savedLayout.FlatMap!;
var filletBody=workflow.AddObject(Occ.Translate(Occ.Box(8,8,3),new(localMap.FlatX+10,localMap.FlatY+10,0)),"Fillet input",ObjKind.Solid);
workflow.SelectItems([new(filletBody.Id,SelKind.Edge,0)]);workflow.StartCommand(()=>new FilletCommand());
workflow.ActiveCommand!.Params.OfType<NumberParam>().Single(p=>p.Label=="Radius").Value=.3;workflow.CommitCommand();
Check(!workflow.StatusIsError,"fillet fixture records a final editable fillet");
workflow.ClearSelection();workflow.StartCommand(()=>new RefoldCommand());var reblend=workflow.ActiveCommand!;
reblend.ConfigureInputs(()=>{reblend.Slots[0].Items.Add(SelItem.Of(savedLayout));reblend.Slots[1].Items.Add(SelItem.Of(workflow.Find(filletBody.Id)!));reblend.Params.OfType<BoolParam>().Single(p=>p.Label=="Create a copy of the curved face").Value=false;reblend.Params.OfType<BoolParam>().Single(p=>p.Label=="Rebuild last fillet after wrapping").Value=true;});
var reblended=reblend.TryBuild();Check(reblended is {Add.Count:1} && reblended.Add[0].Shape!.IsValid,"recorded fillet can be rebuilt at its specified radius after wrapping: "+reblend.Error);
Console.WriteLine($"SUCCESS: {checks} checks");
static class Native{[DllImport("kernel32",CharSet=CharSet.Unicode)]public static extern bool SetDllDirectory(string path);}
