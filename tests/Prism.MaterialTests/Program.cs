using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Prism.Core;
using Prism.Kernel;
using Prism.Rendering;
using Prism.Simulation;

NativeSearch.Initialize();
int checks=0;
void Check(bool condition,string message){if(!condition)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
void Reject(Action action,string message){try{action();}catch(InvalidOperationException){Check(true,message);return;}throw new Exception(message);}
var folder=Path.Combine(Path.GetTempPath(),"prism-material-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
var session=new CadSession();
foreach(var material in Materials.All)
{
    Check(material.Density>0&&material.Youngs>0&&material.Poisson> -1&&material.Poisson<.5&&material.SpecificHeat>0&&material.Conductivity>0,material.Id+" physical ranges");
    var body=session.AddObject(Occ.Sphere(2),material.Id,ObjKind.Solid);
    session.Update(body.Id,o=>o.WithMaterial(material));
}
string path=Path.Combine(folder,"library.prism");session.Save(path);
var reopened=new CadSession();reopened.Open(path);
foreach(var material in Materials.All)
{
    var actual=reopened.Objects.Single(o=>o.Look.MaterialId==material.Id);
    Check(actual.Material==material&&actual.Look.PhysicalSnapshot==material,material.Id+" complete engineering snapshot roundtrip (including undefined yield)");
}
Check(Materials.Find("bronze")!.Density==8.91&&Materials.Find("304")!.Yield==230&&Materials.Find("304")!.Ultimate==540,"audited bronze density and cold-rolled 304 strengths");
Check(Materials.Find("oak")!.Evidence.StrengthBasis.Contains("MOR"),"wood strength identified as bending rather than tensile");
foreach(string id in new[]{"oak","fir","cfrp","gfrp","silicone","tpu"})Reject(()=>MaterialRules.Structural(Materials.Find(id)!),id+" unsupported structural constitutive model rejected");
foreach(string id in new[]{"oak","fir","cfrp","gfrp"})Reject(()=>MaterialRules.Thermal(Materials.Find(id)!),id+" directional thermal model required");
Reject(()=>MaterialRules.Structural(Materials.Find("304")! with{Poisson=.5}),"incompressible linear material rejected");
MaterialRules.Structural(Materials.Find("6061")!);MaterialRules.Thermal(Materials.Find("304")!);
var steel=reopened.Objects.Single(o=>o.Look.MaterialId=="304");
var custom=steel.Material! with{Density=7.93,Evidence=steel.Material!.Evidence with{Revision="custom-test"}};
reopened.Update(steel.Id,o=>o.WithMaterial(custom).WithColor(0xFFCC3344).WithLook(o.WithMaterial(custom).Look with{Metal=0,Gloss=.92f,Pattern=SurfacePattern.Wood,PatternScale=31,PatternAngle=45}));
reopened.Save(path);var pinned=new CadSession();pinned.Open(path);var saved=pinned.Objects.Single(o=>o.Look.MaterialId=="304");
Check(saved.Material==custom&&saved.Look.Pattern==SurfacePattern.Wood&&saved.Look.PatternScale==31&&saved.Look.PatternAngle==45,"cosmetic changes preserve a custom pinned property set through save/reopen");
Check(saved.Material!.Density!=Materials.Find("304")!.Density,"library lookup does not overwrite saved physical properties");
// Remove newly added fields to exercise an older document with only a material id.
var legacy=JsonNode.Parse(File.ReadAllText(path))!;
void Strip(JsonNode? node){if(node is JsonObject obj){foreach(string key in new[]{"PhysicalMaterial","Pattern","PatternScale","PatternAngle"})obj.Remove(key);foreach(var item in obj.ToArray())Strip(item.Value);}else if(node is JsonArray arr)foreach(var item in arr)Strip(item);}
Strip(legacy);string legacyPath=Path.Combine(folder,"legacy.prism");File.WriteAllText(legacyPath,legacy.ToJsonString());var old=new CadSession();old.Open(legacyPath);
var oldSteel=old.Objects.Single(o=>o.Look.MaterialId=="304");Check(oldSteel.Material==Materials.Find("304")&&oldSteel.Look.Pattern==SurfacePattern.Automatic&&oldSteel.Look.PatternScale==12,"legacy material ids load with compatible finish defaults");
var p=new Vector3(17,9,3);var offset=new Vector3(40,-20,8);
foreach(var pattern in Enum.GetValues<SurfacePattern>().Where(x=>x!=SurfacePattern.Automatic))
{
    float value=MaterialSurface.Variation(pattern,p,Vector3.Zero,12,0);
    Check(float.IsFinite(value)&&value>0&&Math.Abs(value-MaterialSurface.Variation(pattern,p+offset,offset,12,0))<1e-5,pattern+" finite translation-invariant finish");
}
Check(Math.Abs(MaterialSurface.Variation(SurfacePattern.Wood,p,Vector3.Zero,12,0)-MaterialSurface.Variation(SurfacePattern.Wood,p,Vector3.Zero,12,90))>.01,"wood orientation changes grain");
Check(MaterialSurface.Pattern(Materials.Find("oak")!.Look)==SurfacePattern.Wood&&MaterialSurface.Pattern(Materials.Find("304")!.Look)==SurfacePattern.Brushed,"automatic material finish mapping");
var naked=new CadSession();var nakedBody=naked.AddObject(Occ.Sphere(2),"Unassigned",ObjKind.Solid);
Reject(()=>StudyRunner.Run(naked,new StudyDef{BodyId=nakedBody.Id}),"static study requires assigned properties before meshing");
var brittleSession=new CadSession();var brittle=brittleSession.AddObject(Occ.Box(8,8,20),"Brittle",ObjKind.Solid);
brittleSession.Update(brittle.Id,o=>o.WithMaterial(Materials.Find("glass")!));brittle=brittleSession.Find(brittle.Id)!;
var orderedFaces=Enumerable.Range(0,brittle.Shape.Count(SubKind.Face)).OrderBy(i=>brittle.Shape.Sub(SubKind.Face,i).Props().Center.Z).ToArray();
var study=new StudyDef{BodyId=brittle.Id,ElementSize=4};
study.Fixtures.Add(new Fixture{Kind=FixtureKind.Fixed,Faces=[new(brittle.Id,orderedFaces[0])]});
study.Loads.Add(new Load{Kind=LoadKind.Force,Value=10,Direction=Vec3.UnitZ,Faces=[new(brittle.Id,orderedFaces[^1])]});
var result=StudyRunner.Run(brittleSession,study);
Check(result.MaxDisplacement>0&&double.IsNaN(result.Yield)&&double.IsNaN(result.MinSafety)&&result.SafetyFactor.All(double.IsNaN),"brittle elastic solution does not invent a yield-based safety factor");
Reject(()=>ResultView.Build(result,brittle.Id,ResultKind.SafetyFactor,1,20),"undefined safety factor cannot produce a contour");
result.Mesh.Dispose();
Console.WriteLine($"SUCCESS: {checks} checks; artifacts: {folder}");
static class NativeSearch{[DllImport("kernel32",CharSet=CharSet.Unicode)]static extern bool SetDllDirectory(string path);public static void Initialize()=>SetDllDirectory(Path.GetFullPath("native/bin"));}

