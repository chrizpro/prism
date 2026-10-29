using System.Runtime.InteropServices;
using Prism.Core;
using Prism.Commands;
using Prism.Kernel;
Native.SetDllDirectory(Path.GetFullPath("native/bin"));int checks=0;
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
Console.WriteLine($"SUCCESS: {checks} checks");
static class Native{[DllImport("kernel32",CharSet=CharSet.Unicode)]public static extern bool SetDllDirectory(string path);}
