using System.Runtime.InteropServices;
using Prism.Core;
using Prism.Kernel;
using Prism.Simulation;
using Prism.Commands;

NativeSearch.Initialize();int checks=0;
void Check(bool ok,string text){if(!ok)throw new Exception(text);Console.WriteLine("PASS "+text);checks++;}
var root=Path.GetFullPath("docs/cae-studies");Directory.CreateDirectory(root);
var session=new CadSession();var body=session.AddObject(Occ.Box(10,10,20),"Thermal block",ObjKind.Solid);session.Update(body.Id,o=>o.WithMaterial(Materials.Find("6061")!));body=session.Find(body.Id)!;
int[] zFaces=Enumerable.Range(0,body.Shape.Count(SubKind.Face)).OrderBy(i=>body.Shape.Sub(SubKind.Face,i).Props().Center.Z).ToArray();
var steady=CaeSolver.Solve(body,new(CaeKind.SteadyThermal,[zFaces[0]],[zFaces[^1]],ElementSize:4),Path.Combine(root,"thermal-steady"));
var mesh=steady.Prepared.Mesh;
Check(steady.Temperatures.Length==mesh.NodeCount,"thermal solver returns every mesh node");
double error=Enumerable.Range(0,mesh.NodeCount).Max(n=>Math.Abs(steady.Temperatures[n]-(100-4*mesh.Nodes[3*n+2])));
Check(error<.02,$"steady conduction matches the analytic linear temperature field (max error {error:G4} °C)");
Check(File.Exists(Path.Combine(steady.Folder,"temperatures.csv")),"nodal temperature CSV is written");
var field=ResultView.Field(steady);Check(field.Kind==ResultKind.Scalar && field.Mesh.Scalars.Length==mesh.NodeCount,"temperature field uses the result renderer");
var transient=CaeSolver.Solve(body,new(CaeKind.TransientThermal,[zFaces[0]],[zFaces[^1]],Duration:.1,ElementSize:4),Path.Combine(root,"thermal-transient"));
var mid=Enumerable.Range(0,transient.Prepared.Mesh.NodeCount).Where(n=>Math.Abs(transient.Prepared.Mesh.Nodes[3*n+2]-10)<2).ToArray();
Check(mid.Length>0&&mid.Average(n=>transient.Temperatures[n])<55,"early transient interior remains cooler than steady state");
Check(transient.Temperatures.All(double.IsFinite),"transient temperatures are finite");
bool overlap=false;try{CaeSolver.Input(steady.Prepared,new(CaeKind.SteadyThermal,[zFaces[0]],[zFaces[0]]));}catch(InvalidOperationException){overlap=true;}
Check(overlap,"conflicting temperature boundaries are rejected");
using var canceled=new CancellationTokenSource();canceled.Cancel();bool canceledOk=false;
try{CaeSolver.Solve(body,new(CaeKind.SteadyThermal,[zFaces[0]],[zFaces[^1]]),Path.Combine(root,"canceled"),canceled.Token);}catch(OperationCanceledException){canceledOk=true;}
Check(canceledOk,"canceled jobs do not start solving");
var beam=session.AddObject(Occ.Box(10,10,100),"Modal cantilever",ObjKind.Solid);session.Update(beam.Id,o=>o.WithMaterial(Materials.Find("6061")!));beam=session.Find(beam.Id)!;
int fixedFace=Enumerable.Range(0,beam.Shape.Count(SubKind.Face)).MinBy(i=>beam.Shape.Sub(SubKind.Face,i).Props().Center.Z);
var modal=CaeSolver.Solve(beam,new(CaeKind.Modal,[fixedFace],[],Modes:6,ElementSize:7),Path.Combine(root,"modal-cantilever"));
int loadedFace=Enumerable.Range(0,beam.Shape.Count(SubKind.Face)).MaxBy(i=>beam.Shape.Sub(SubKind.Face,i).Props().Center.Z);
var stat=new StudyDef{BodyId=beam.Id,ElementSize=7};
stat.Fixtures.Add(new Fixture{Kind=FixtureKind.Fixed,Faces=[new(beam.Id,fixedFace)]});
stat.Loads.Add(new Load{Kind=LoadKind.Force,Value=100,Direction=Vec3.UnitZ,Faces=[new(beam.Id,loadedFace)],Total=true});
var staticResult=StudyRunner.Run(session,stat);
Check(Math.Abs(staticResult.MaxDisplacement-100*100/(68900.0*100))/(100*100/(68900.0*100))<.15,"existing static solver retains the expected axial displacement");
staticResult.Mesh.Dispose();
double analytic=Math.Pow(1.8751040687,2)/(2*Math.PI*100*100)*Math.Sqrt(68900*(Math.Pow(10,4)/12)/(2.7e-9*100));
Check(modal.Frequencies.Length==6&&modal.Modes.Count==6,"modal solver returns six frequencies and mode shapes");
Check(Math.Abs(modal.Frequencies[0]-analytic)/analytic<.08,$"first bending mode agrees with slender-beam theory within 8% ({modal.Frequencies[0]:G6} vs {analytic:G6} Hz)");
Check(modal.Frequencies.SequenceEqual(modal.Frequencies.Order()),"natural frequencies are ordered");
var mode=ResultView.Field(modal);Check(mode.Values.All(float.IsFinite)&&Math.Abs(mode.Values.Max()-1)<1e-6,"mode display uses normalized relative magnitude");
var laminar=PipeFlow.Calculate(.01,1,1e-6,1000,.001);
double poiseuille=128*.001*1*1e-6/(Math.PI*Math.Pow(.01,4));
Check(Math.Abs(laminar.PressureDrop-poiseuille)<1e-8,"laminar pipe loss matches Hagen–Poiseuille");
var turbulent=PipeFlow.Calculate(.05,10,.002,1000,.001,.00001);
double residual=1/Math.Sqrt(turbulent.Friction)+2*Math.Log10(.00001/(3.7*.05)+2.51/(turbulent.Reynolds*Math.Sqrt(turbulent.Friction)));
Check(Math.Abs(residual)<1e-9,"turbulent friction satisfies Colebrook–White");
bool transition=false;try{PipeFlow.Calculate(.01,1,3000*.001*Math.PI*.01/(4*1000),1000,.001);}catch(ArgumentException){transition=true;}
Check(transition,"transitional pipe flow is explicitly rejected");
var pipeCommand=new PipeFlowCommand();session.StartCommand(pipeCommand);
Check(pipeCommand.Error==null&&pipeCommand.Results.Count>=6,"default pipe-flow inputs produce a valid result");session.CancelCommand();
Check(File.Exists(CaeSolver.Executable),"installed CalculiX executable is available");
session.ShowResult(field);session.Update(body.Id,o=>o.WithColor(0xFF808080));
Check(session.Result==null,"changing the source body invalidates a displayed CAE field");
steady.Prepared.Mesh.Dispose();transient.Prepared.Mesh.Dispose();modal.Prepared.Mesh.Dispose();
session.Save(Path.Combine(root,"benchmark-bodies.prism"));
File.WriteAllText(Path.Combine(root,"benchmarks.json"),System.Text.Json.JsonSerializer.Serialize(new{thermalMaxErrorC=error,firstModeHz=modal.Frequencies[0],beamTheoryHz=analytic,hotFace=zFaces[0]+1,coldFace=zFaces[^1]+1,fixedFace=fixedFace+1,checks}));
Console.WriteLine($"SUCCESS: {checks} checks");
static class NativeSearch
{
    [DllImport("kernel32",CharSet=CharSet.Unicode)]static extern bool SetDllDirectory(string path);
    public static void Initialize()=>SetDllDirectory(Path.GetFullPath("native/bin"));
}
