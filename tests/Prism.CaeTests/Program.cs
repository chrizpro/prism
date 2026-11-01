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
// ---- fatigue (stress-life)
var steel=Materials.Find("1045")!;var fs=new FatigueSettings(-1,MeanStressModel.Goodman,SurfaceFinish.Ground,1,1,1e6);
var fc=Fatigue.Curve(steel,fs);
double kaGround=Math.Min(1.58*Math.Pow(steel.Ultimate,-0.085),1);
Check(Math.Abs(fc.Se-kaGround*0.5*steel.Ultimate)<1e-9,"steel endurance limit is Marin-corrected 0.5·Sut");
Check(Math.Abs(fc.A*Math.Pow(1e3,fc.B)-fc.F*steel.Ultimate)<1e-6&&Math.Abs(fc.A*Math.Pow(1e6,fc.B)-fc.Se)<1e-6,"S-N curve passes through 0.9·Sut at 10³ cycles and Se at the 10⁶ knee");
Check(Fatigue.LifeAt(fc,fc.Se*0.99)==Fatigue.LifeCap&&Math.Abs(Fatigue.LifeAt(fc,fc.A*Math.Pow(1e5,fc.B))-1e5)<1,"steel runs out below Se and follows Basquin above it");
double reversedLife=Fatigue.LifeAt(fc,Fatigue.EquivalentAmplitude(fc,fs,400));
var pulsating=fs with{LoadRatio=0};
double eq0=Fatigue.EquivalentAmplitude(fc,pulsating,400);
Check(Math.Abs(eq0-200/(1-200/steel.Ultimate))<1e-9&&Fatigue.LifeAt(fc,eq0)>=reversedLife&&eq0<400,"Goodman correction: pulsating loading with the same peak is gentler than reversed loading");
Check(Math.Abs(Fatigue.SafetyFactor(fc,fs,fc.Se/2)-2)<1e-9&&Math.Abs(Fatigue.SafetyFactor(fc,fs,fc.Se)-1)<1e-9,"fully reversed safety factor is allowable / alternating stress");
double gerberN=Fatigue.SafetyFactor(fc,fs with{LoadRatio=0,Mean=MeanStressModel.Gerber},200);
double gerberCheck=gerberN*100/fc.Se+Math.Pow(gerberN*100/steel.Ultimate,2);
Check(Math.Abs(gerberCheck-1)<1e-9,"Gerber safety factor lands on the Gerber parabola");
bool brittle=false;try{Fatigue.Curve(Materials.Find("pla")!,fs);}catch(InvalidOperationException){brittle=true;}
Check(brittle,"materials without a stress-life model (plastics) are rejected");
bool noYield=false;try{Fatigue.Curve(Materials.Find("cast-iron")!,fs with{Mean=MeanStressModel.Soderberg});}catch(InvalidOperationException){noYield=true;}
Check(noYield,"Soderberg is refused for materials with no yield point");
var alFc=Fatigue.Curve(Materials.Find("6061")!,fs);
Check(!alFc.HasKnee&&Fatigue.LifeAt(alFc,alFc.Se*0.9)>Fatigue.LifeAt(alFc,alFc.Se),"aluminum has no endurance limit: lower stress keeps lengthening life");
var fatBeam=new StudyDef{BodyId=beam.Id,ElementSize=7};
fatBeam.Fixtures.Add(new Fixture{Kind=FixtureKind.Fixed,Faces=[new(beam.Id,fixedFace)]});
fatBeam.Loads.Add(new Load{Kind=LoadKind.Force,Value=100,Direction=Vec3.UnitX,Faces=[new(beam.Id,loadedFace)],Total=true});
var fatStatic=StudyRunner.Run(session,fatBeam);
var fatigue=Fatigue.Analyze(fatStatic,Materials.Find("6061")!,fs with{Finish=SurfaceFinish.Machined,SizeFactor=.9,ReliabilityFactor=.897});
Check(fatigue.Life.Length==fatStatic.Mesh.NodeCount&&fatigue.Life.All(x=>x>=1&&x<=Fatigue.LifeCap),"fatigue life is reported for every node");
int hot=Array.IndexOf(fatStatic.Solution.VonMises,fatStatic.Solution.VonMises.Max());
Check(fatigue.MinLife==fatigue.Life[hot]&&fatigue.MinSafety<=fatigue.Safety.Min()+1e-12,"shortest life sits at the node with the highest stress");
var fatigueView=ResultView.Build(fatStatic,beam.Id,ResultKind.Scalar,0,100,fatigue.Safety,"Fatigue safety factor","",5,Math.Min(fatigue.MinSafety,4.99));
Check(fatigueView.Kind==ResultKind.Scalar&&fatigueView.Values.All(float.IsFinite),"fatigue field paints through the result renderer");
fatStatic.Mesh.Dispose();
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
