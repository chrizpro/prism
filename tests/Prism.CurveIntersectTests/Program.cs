using System.Runtime.InteropServices;
using Prism.Core;
using Prism.Commands;
using Prism.Kernel;

Native.SetDllDirectory(Path.GetFullPath("native/bin"));
int checks=0;
void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
CurveIntersectCommand Command(CadSession session,SelItem a,SelItem b)
{
    session.StartCommand(()=>new CurveIntersectCommand());var command=(CurveIntersectCommand)session.ActiveCommand!;
    command.Pick(a,false);command.Pick(b,false);
    return command;
}
IEnumerable<Vec3> Points(Shape shape){var mesh=shape.Tessellate();for(int i=0;i<mesh.EdgePoints.Length;i+=3)yield return new(mesh.EdgePoints[i],mesh.EdgePoints[i+1],mesh.EdgePoints[i+2]);}
double Bezier(double t,double a,double b,double c,double d)=>Math.Pow(1-t,3)*a+3*Math.Pow(1-t,2)*t*b+3*(1-t)*t*t*c+t*t*t*d;
var s=new CadSession();var (first,second)=CurveIntersectFixture.Add(s);var original=Occ.CurveNet(first.Shape).Points;
var cmd=Command(s,SelItem.Of(first),SelItem.Of(second));var output=cmd.TryBuild();
Check(output is {Add.Count:1}&&cmd.Error==null&&s.Preview.Count==1,"two planar NURBS profiles produce a live curve preview through normal selection");
var curve=output!.Add[0].Shape!;
Check(curve.IsValid&&curve.Count(SubKind.Wire)==1&&curve.Count(SubKind.Face)==0,"result is a connected, valid 3D curve, not a surface");
Check(Occ.CurveNet(curve.Sub(SubKind.Edge,0)).Points.Length>3,"result exposes NURBS control points for downstream curve editing");
using(var profile=Occ.Circle(new(-60,-20,0),new(1,65d/40,65d/40),1))
using(var swept=Occ.Sweep(profile,curve,false))Check(swept.IsValid&&swept.HasFaces,"intersection is usable as a surface sweep rail");
var points=Points(curve).ToList();
Check(points.Count>10&&points.All(p=>{double t=(p.X+60)/120;return Math.Abs(p.Y-Bezier(t,-20,45,-45,20))<.002&&Math.Abs(p.Z-Bezier(t,0,65,-25,40))<.002;}),"intersection projects back onto both original cubic profiles");
Check(Math.Abs(curve.Props().Min.X+60)<.01&&Math.Abs(curve.Props().Max.X-60)<.01,"intersection preserves both full shared endpoints");
Check(s.Objects.Count==2&&Occ.CurveNet(first.Shape).Points.SequenceEqual(original),"preview leaves the sources and document unchanged");
var overlay=new Overlay();cmd.BuildOverlay(s,overlay);Check(overlay.Lines.Count>0&&overlay.Labels.Count==3,"preview shows normal projection guides and identifies the two inputs and 3D result");
cmd.Params.OfType<BoolParam>().Single(p=>p.Label=="Show projection guides").Value=false;overlay.Clear();cmd.BuildOverlay(s,overlay);Check(overlay.Lines.Count==0,"projection guides can be hidden");
s.CommitCommand();Check(s.Objects.Count==3&&s.Objects.Last().Kind==ObjKind.Curve,"Create curve adds one reusable curve and keeps both sources");
var sample=Path.GetFullPath("artifacts/curve-intersect-review/two-plane-intersection.prism");s.Save(sample);var restored=new CadSession();restored.Open(sample);Check(restored.Objects.Count==3&&restored.Objects.Last().Shape.IsValid,"the resulting B-rep curve survives save and reopen");
s.Undo();Check(s.Objects.Count==2,"intersection is one undo step");s.Redo();Check(s.Objects.Count==3,"redo restores the intersection curve");
var oblique=new CadSession();double angle=18*Math.PI/180;var support=new Plane3(Vec3.Zero,Vec3.UnitX,new(0,Math.Cos(angle),Math.Sin(angle)));
var lineA=oblique.AddObject(Occ.Line(new(-10,0,0),new(10,0,0)),"A",ObjKind.Curve,plane:Plane3.XY);
var lineB=oblique.AddObject(Occ.Line(support.PointAt(-10,5),support.PointAt(10,5)),"B",ObjKind.Curve,plane:support);
var angled=Command(oblique,SelItem.Of(lineA),SelItem.Of(lineB));var angledOutput=angled.TryBuild();Check(angledOutput!=null&&Points(angledOutput.Add[0].Shape!).All(p=>Math.Abs(p.Y)<.001&&Math.Abs(p.Z-5/Math.Sin(angle))<.001),"oblique support planes use their own normals and do not clip the result");
var invalid=new CadSession();var parallelA=invalid.AddObject(Occ.Circle(Vec3.Zero,Vec3.UnitZ,10),"A",ObjKind.Curve);var parallelB=invalid.AddObject(Occ.Circle(new(0,0,20),Vec3.UnitZ,10),"B",ObjKind.Curve);
var parallel=Command(invalid,SelItem.Of(parallelA),SelItem.Of(parallelB));Check(parallel.TryBuild()==null&&parallel.Error!.Contains("parallel"),"parallel support planes report an actionable error");
var duplicate=Command(invalid,SelItem.Of(parallelA),SelItem.Of(parallelA));Check(duplicate.TryBuild()==null&&duplicate.Error!.Contains("different"),"the same curve cannot be used twice");
var disjoint=new CadSession();var da=disjoint.AddObject(Occ.Line(new(0,0,0),new(10,0,0)),"A",ObjKind.Curve,plane:Plane3.XY);var db=disjoint.AddObject(Occ.Line(new(20,0,0),new(30,0,0)),"B",ObjKind.Curve,plane:Plane3.XZ);var noOverlap=Command(disjoint,SelItem.Of(da),SelItem.Of(db));Check(noOverlap.TryBuild()==null&&noOverlap.Error!=null,"disjoint projected ranges create no empty curve object");
var closed=new CadSession();var ca=closed.AddObject(Occ.Circle(Vec3.Zero,Vec3.UnitZ,10),"A",ObjKind.Curve);var cb=closed.AddObject(Occ.Circle(Vec3.Zero,Vec3.UnitY,7),"B",ObjKind.Curve);var branches=Command(closed,SelItem.Of(ca),SelItem.Of(cb));var together=branches.TryBuild();Check(together!=null&&together.Add[0].Shape!.Count(SubKind.Wire)>=2,"closed profiles preserve disconnected intersection branches");branches.Params.OfType<BoolParam>().Single(p=>p.Label=="Separate disconnected curves").Value=true;var separated=branches.TryBuild();Check(separated is {Add.Count:>=2}&&separated.Add.All(o=>o.Shape!.IsValid),"disconnected branches can become separate curve objects");
Console.WriteLine($"SUCCESS: {checks} checks");
static class Native{[DllImport("kernel32",CharSet=CharSet.Unicode)]public static extern bool SetDllDirectory(string path);}
