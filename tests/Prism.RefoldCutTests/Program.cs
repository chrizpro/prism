using System.Runtime.InteropServices;
using Prism.Core;
using Prism.Commands;
using Prism.Kernel;
Native.SetDllDirectory(Path.GetFullPath("native/bin"));int checks=0;
void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
try {
var s=new CadSession();s.Open(Path.GetFullPath("artifacts/flatten-cut-refold-review/cut-flat-pattern.prism"));var flat=s.Objects.Single(o=>o.FlatMap!=null&&o.Kind==ObjKind.Surface);var map=flat.FlatMap!;
Check(flat.Shape.IsValid&&flat.Shape.Count(SubKind.Wire)==12,"MCP subtraction created a valid flat face with 11 actual cutouts");
var area=flat.Shape.Props().Area;Check(area<map.Width*map.Height-900,"cutouts remove real surface area");
s.ClearSelection();s.StartCommand(()=>new RefoldCommand());var cmd=(RefoldCommand)s.ActiveCommand!;cmd.Pick(SelItem.Of(flat),false);var result=cmd.TryBuild();
Check(result is {Add.Count:1}&&cmd.Error==null,"a cut pattern can refold without selecting any sketch: "+cmd.Error);
var face=result!.Add[0].Shape!;Check(face.IsValid&&face.Count(SubKind.Face)==1,"returned cut cylinder is a valid single face");
Check(face.Count(SubKind.Wire)==12,"all 11 cutout boundaries remain on the curved face");
Check(Math.Abs(face.Props().Area-area)<.001,"refolding preserves the cut surface area rather than restoring removed material");
using(var center=Occ.Vertex(new(-25,0,35)))Check(Occ.Distance(face,center).Distance>10,"the gear window is an actual opening, with no surface at its center");
var mesh=face.Tessellate();Check(mesh.TriangleCount>0,"cut cylindrical face tessellates successfully");
double maxRadial=0;for(int i=0;i<mesh.Positions.Length;i+=3){var p=new Vec3(mesh.Positions[i],mesh.Positions[i+1],mesh.Positions[i+2]);var origin=new Vec3(map.Cylinder[0],map.Cylinder[1],map.Cylinder[2]);var axis=new Vec3(map.Cylinder[3],map.Cylinder[4],map.Cylinder[5]);var d=p-origin;maxRadial=Math.Max(maxRadial,Math.Abs((d-axis*Vec3.Dot(d,axis)).Length-map.Cylinder[9]));}Check(maxRadial<.0001,"trimmed face vertices remain on the original cylinder");
s.CommitCommand();var returned=s.Objects.Last();s.Undo();Check(s.Objects.All(o=>o.Id!=returned.Id),"cut-face refolding is a single undo step");s.Redo();Check(s.Find(returned.Id)?.Shape.Count(SubKind.Wire)==12,"redo preserves the holes");
string path=Path.GetFullPath("artifacts/flatten-cut-refold-review/test-returned-face.prism");s.Save(path);var opened=new CadSession();opened.Open(path);Check(opened.Find(returned.Id)!.Shape.IsValid&&opened.Find(returned.Id)!.Shape.Count(SubKind.Wire)==12,"cut refolded face survives save and reopen");
// Reject edge trimming explicitly until an invertible outer-boundary mapping is supported.
using var cornerTool=Occ.Translate(Occ.Box(10,10,10),new(map.FlatX,map.FlatY,-5));using var notched=Occ.Cut(flat.Shape,cornerTool);Check(notched.Props().Area<area-20,"edge-cut negative fixture actually removes material");bool rejected=false;string reason="No error";try{using var folded=Occ.CylinderRefoldFace(notched,map.Cylinder,map.FlatX,map.FlatY);}catch(KernelException e){rejected=true;reason=e.Message;}Check(rejected,"unsupported outer-boundary edits are rejected instead of silently discarded: "+reason);
Console.WriteLine($"SUCCESS: {checks} checks");
} catch(Exception error) {Console.Error.WriteLine(error);Environment.Exit(1);}
static class Native{[DllImport("kernel32",CharSet=CharSet.Unicode)]public static extern bool SetDllDirectory(string path);}
