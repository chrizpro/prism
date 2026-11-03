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
// ---- several bodies, each with its own material, bonded where they touch
{
    var s=new CadSession();
    SceneObject Add(string name,Shape shape,string material){var o=s.AddObject(shape,name,ObjKind.Solid);s.Update(o.Id,x=>x.WithMaterial(Materials.Find(material)!));return s.Find(o.Id)!;}
    int End(SceneObject o,bool top){var faces=Enumerable.Range(0,o.Shape.Count(SubKind.Face));return top?faces.MaxBy(i=>o.Shape.Sub(SubKind.Face,i).Props().Center.Z):faces.MinBy(i=>o.Shape.Sub(SubKind.Face,i).Props().Center.Z);}
    var alu=Add("Aluminum",Occ.Box(10,10,20),"6061");var stl=Add("Steel",Occ.Translate(Occ.Box(10,10,30),new Vec3(0,0,20)),"1045");
    var ma=alu.Material!;var ms=stl.Material!;
    int steelFaceOffset=alu.Shape.Count(SubKind.Face);

    // heat through two layers in series: the joint temperature follows from the resistances
    var heat=CaeSolver.Solve(alu,new(CaeKind.SteadyThermal,[End(alu,false)],[steelFaceOffset+End(stl,true)],100,0,ElementSize:4,OtherBodies:[stl.Id]),Path.Combine(root,"bonded-thermal"),others:[stl]);
    double ra=20/ma.Conductivity,rb=30/ms.Conductivity,joint=100*rb/(ra+rb);
    var m2=heat.Prepared.Mesh;var at=Enumerable.Range(0,m2.NodeCount).Where(n=>Math.Abs(m2.Nodes[3*n+2]-20)<1e-6).ToArray();
    Check(heat.Prepared.Bonds.Count==1&&at.Length>0,"touching bodies are found and bonded");
    double seen=at.Average(n=>heat.Temperatures[n]);
    Check(Math.Abs(seen-joint)<.5,$"two materials in series: joint temperature {seen:0.###} °C against {joint:0.###} °C by hand");

    // a bar of two materials pulled along its length: the same stress, stretches that add up
    var st=new StudyDef{BodyId=alu.Id,ElementSize=4};st.OtherBodies.Add(stl.Id);
    st.Fixtures.Add(new Fixture{Kind=FixtureKind.Fixed,Faces=[new(alu.Id,End(alu,false))]});
    st.Loads.Add(new Load{Kind=LoadKind.Force,Value=1000,Direction=Vec3.UnitZ,Faces=[new(stl.Id,End(stl,true))],Total=true});
    var res=StudyRunner.Run(s,st);
    double area=100,want=1000*20/(ma.Youngs*1000*area)+1000*30/(ms.Youngs*1000*area);
    var mesh2=res.Mesh;double tip=Enumerable.Range(0,mesh2.NodeCount).Where(n=>Math.Abs(mesh2.Nodes[3*n+2]-50)<1e-6).Average(n=>res.Solution.Displacement[3*n+2]);
    Check(Math.Abs(tip-want)/want<.05,$"two materials pulled in line: stretch {tip:G5} mm against {want:G5} mm by hand");
    var middle=Enumerable.Range(0,mesh2.NodeCount).Where(n=>Math.Abs(mesh2.Nodes[3*n+2]-10)<4||Math.Abs(mesh2.Nodes[3*n+2]-35)<4).ToArray();
    double mean=middle.Average(n=>res.Solution.VonMises[n]);
    Check(Math.Abs(mean-10)<.7,$"both carry the same stress, F/A = 10 MPa ({mean:0.##} MPa)");
    Check(Math.Abs(res.Solution.Reaction.Length-1000)<1,$"the supports carry the load ({res.Solution.Reaction.Length:0.#} N)");
    int alNode=Enumerable.Range(0,mesh2.NodeCount).First(n=>Math.Abs(mesh2.Nodes[3*n+2]-10)<4),stNode=Enumerable.Range(0,mesh2.NodeCount).Last(n=>Math.Abs(mesh2.Nodes[3*n+2]-35)<4);
    Check(Math.Abs(res.SafetyFactor[alNode]/res.SafetyFactor[stNode]-ma.Yield/ms.Yield)<.25,"each body's safety factor uses its own yield strength");
    Check(res.MaterialName.Contains(ma.Name)&&res.MaterialName.Contains(ms.Name),"the study names both materials");

    // one bar cut in two and bonded must vibrate like the whole bar
    var whole=Add("Whole",Occ.Translate(Occ.Box(10,10,100),new Vec3(40,0,0)),"6061");
    var low=Add("Lower",Occ.Translate(Occ.Box(10,10,50),new Vec3(80,0,0)),"6061");var high=Add("Upper",Occ.Translate(Occ.Box(10,10,50),new Vec3(80,0,50)),"6061");
    var one=CaeSolver.Solve(whole,new(CaeKind.Modal,[End(whole,false)],[],Modes:2,ElementSize:7),Path.Combine(root,"bonded-modal-whole"));
    var two=CaeSolver.Solve(low,new(CaeKind.Modal,[End(low,false)],[],Modes:2,ElementSize:7,OtherBodies:[high.Id]),Path.Combine(root,"bonded-modal-split"),others:[high]);
    Check(Math.Abs(two.Frequencies[0]-one.Frequencies[0])/one.Frequencies[0]<.03,$"a bar cut in two and bonded vibrates like the whole bar ({two.Frequencies[0]:0.#} Hz against {one.Frequencies[0]:0.#} Hz)");

    bool gap=false;try{var far=Add("Far",Occ.Translate(Occ.Box(5,5,5),new Vec3(300,0,0)),"6061");CaeAssembly.Mesh([alu,far],4);}catch(InvalidOperationException e){gap=e.Message.Contains("does not touch");}
    Check(gap,"a body that touches nothing is refused");
    bool over=false;try{var inside=Add("Inside",Occ.Translate(Occ.Box(5,5,5),new Vec3(0,0,5)),"6061");CaeAssembly.Mesh([alu,inside],4);}catch(InvalidOperationException e){over=e.Message.Contains("overlap");}
    Check(over,"overlapping bodies are refused");
    var fig=ResultView.Build(res,alu.Id,ResultKind.VonMises,1,50);
    Check(fig.Covers(stl.Id)&&fig.Covers(alu.Id),"the result is drawn in place of every body");
    bool noFatigue=false;s.Studies.Add(st);s.ActiveStudy=st;st.Results=res;try{s.StartCommand(()=>new FatigueCommand());var panel=s.ActiveCommand!;panel.OnChanged();noFatigue=string.Join(" ",panel.Results.Select(r=>r.Value)).Contains("one-body")||(panel.Error??"").Contains("one-body");}catch(InvalidOperationException e){noFatigue=e.Message.Contains("one-body");}s.CancelCommand();
    Check(noFatigue,"Fatigue stays on one-body studies for now");
}
// ---- pipe networks
{
    var water=new NetworkFluid(998.2,.001002,1.5e-6);
    var one=PipeNetwork.Solve([new NetworkPipe("A","B",10,.02)],[],water,new Dictionary<string,double>{["A"]=20000,["B"]=0});
    var check1=PipeFlow.Calculate(.02,10,one.Pipes[0].Flow,998.2,.001002,1.5e-6);
    Check(Math.Abs(check1.PressureDrop-20000)<30,$"one pipe agrees with Pipe Flow ({check1.PressureDrop:0} Pa against 20000 Pa)");
    var slow=new NetworkFluid(998.2,.5);
    var par=PipeNetwork.Solve([new NetworkPipe("A","B",1,.02),new NetworkPipe("A","B",1,.04)],[],slow,new Dictionary<string,double>{["A"]=5000,["B"]=0});
    double ratio=par.Pipes[1].Flow/par.Pipes[0].Flow;
    Check(par.Pipes.All(p=>p.Regime=="laminar")&&Math.Abs(ratio-16)<1e-3,$"laminar pipes side by side share flow by diameter⁴ ({ratio:0.###} : 1)");
    var tee=PipeNetwork.Solve([new NetworkPipe("A","B",5,.04),new NetworkPipe("B","C",8,.025),new NetworkPipe("B","D",8,.025)],[],water,new Dictionary<string,double>{["A"]=200000,["C"]=0,["D"]=0});
    Check(Math.Abs(tee.Pipes[0].Flow-tee.Pipes[1].Flow-tee.Pipes[2].Flow)<1e-9&&Math.Abs(tee.Pipes[1].Flow-tee.Pipes[2].Flow)<1e-9,"flow is conserved at a tee, and equal branches split evenly");
    var res=PipeNetwork.Solve([new NetworkPipe("B","C",20,.05)],[new NetworkPump("A","B",30,.01)],water,new Dictionary<string,double>{["A"]=0,["C"]=0},heights:new Dictionary<string,double>{["C"]=10});
    double liftNeeded=10+res.Pipes[0].HeadLoss,given=res.Pumps[0].Head;
    Check(Math.Abs(liftNeeded-given)<1e-5&&res.Pumps[0].Flow>0&&res.Pumps[0].Flow<.01,$"a pump settles where its curve meets the system ({given:0.###} m at {res.Pumps[0].Flow*60000:0.#} L/min)");
    var blocked=PipeNetwork.Solve([new NetworkPipe("B","C",20,.05)],[new NetworkPump("A","B",5,.01)],water,new Dictionary<string,double>{["A"]=0,["C"]=0},heights:new Dictionary<string,double>{["C"]=10});
    Check(blocked.Pumps[0].Flow<1e-6&&blocked.Notes.Any(n=>n.Contains("cannot beat")),"a pump too weak for the lift delivers nothing and says so");
    bool loose=false;try{PipeNetwork.Solve([new NetworkPipe("A","B",1,.02),new NetworkPipe("C","D",1,.02)],[],water,new Dictionary<string,double>{["A"]=1});}catch(ArgumentException e){loose=e.Message.Contains("not connected");}
    Check(loose,"a pipe cut off from any known pressure is refused");
    var s=new CadSession();s.StartCommand(()=>new PipeNetworkCommand());var c=(PipeNetworkCommand)s.ActiveCommand!;c.OnChanged();
    Check(c.Result!=null&&c.Results.Any(r=>r.Label.StartsWith("Pressure at B")),"Pipe Network works from the panel with its starting rows");
    s.CancelCommand();
}
// ---- cooling and heating in Thermal
{
    var s=new CadSession();
    var block=s.AddObject(Occ.Box(10,10,20),"Heat block",ObjKind.Solid);s.Update(block.Id,o=>o.WithMaterial(Materials.Find("6061")!));block=s.Find(block.Id)!;
    var k=block.Material!.Conductivity;
    int bottom=Enumerable.Range(0,6).MinBy(i=>block.Shape.Sub(SubKind.Face,i).Props().Center.Z),top=Enumerable.Range(0,6).MaxBy(i=>block.Shape.Sub(SubKind.Face,i).Props().Center.Z);
    double Top(CaeOutcome o){var m=o.Prepared.Mesh;return Enumerable.Range(0,m.NodeCount).Where(n=>Math.Abs(m.Nodes[3*n+2]-20)<1e-6).Average(n=>o.Temperatures[n]);}
    double Bottom(CaeOutcome o){var m=o.Prepared.Mesh;return Enumerable.Range(0,m.NodeCount).Where(n=>Math.Abs(m.Nodes[3*n+2])<1e-6).Average(n=>o.Temperatures[n]);}
    var film=CaeSolver.Solve(block,new(CaeKind.SteadyThermal,[bottom],[],100,ElementSize:4,CoolFaces:[top],FilmCoefficient:1000,Ambient:20),Path.Combine(root,"cooling-film"));
    double rCond=.02/k,rFilm=1.0/1000,hand=20+80*rFilm/(rCond+rFilm);
    Check(Math.Abs(Top(film)-hand)<.05,$"a hot wall cooled by a film: surface {Top(film):0.###} °C against {hand:0.###} °C by hand");
    var fin=CaeSolver.Solve(block,new(CaeKind.SteadyThermal,[],[],ElementSize:4,HeatFaces:[bottom],HeatPower:2,CoolFaces:[top],FilmCoefficient:500,Ambient:25),Path.Combine(root,"cooling-power"));
    double tSurface=25+2/(500*1e-4),tBase=tSurface+2*.02/(k*1e-4);
    Check(Math.Abs(Top(fin)-tSurface)<.1&&Math.Abs(Bottom(fin)-tBase)<.1,$"2 W in, cooled at the far end: {Top(fin):0.##} and {Bottom(fin):0.##} °C against {tSurface:0.##} and {tBase:0.##} °C by hand");
    var glow=CaeSolver.Solve(block,new(CaeKind.SteadyThermal,[],[],ElementSize:4,HeatFaces:[bottom],HeatPower:1,CoolFaces:[top],Ambient:20,Emissivity:.9),Path.Combine(root,"cooling-radiation"));
    double ambient=293.15,tRad=Math.Pow(1/(1e-4*.9*5.670374e-8)+Math.Pow(ambient,4),.25)-273.15;
    Check(Math.Abs(Top(glow)-tRad)<1,$"radiation alone: {Top(glow):0.##} °C against {tRad:0.##} °C by hand");
    bool nowhere=false;try{CaeSolver.Solve(block,new(CaeKind.SteadyThermal,[],[],ElementSize:6,HeatFaces:[bottom],HeatPower:1),Path.Combine(root,"cooling-none"));}catch(InvalidOperationException e){nowhere=e.Message.Contains("somewhere to go");}
    Check(nowhere,"heat with nowhere to go is refused");
}
// ---- basic 3D flow
{
    var s=new CadSession();
    var rod=s.AddObject(Occ.Cylinder(10,120),"Pipe fluid",ObjKind.Solid);rod=s.Find(rod.Id)!;
    var clock=System.Diagnostics.Stopwatch.StartNew();
    s.StartCommand(()=>new FlowStudyCommand());var c=(FlowStudyCommand)s.ActiveCommand!;
    SetChoice(c,"Flow",1);SetChoice(c,"Fluid",1);
    double re=40,mu=1.002e-3,rho=998.2,u=re*mu/(rho*.02);
    c.Params.OfType<NumberParam>().First(p=>p.Label=="Speed").Value=u;
    int End(SceneObject o,bool high){var f=Enumerable.Range(0,o.Shape.Count(SubKind.Face)).Where(i=>Occ.FaceInfoOf(o.Shape.Sub(SubKind.Face,i)).Type=="Plane");return high?f.MaxBy(i=>o.Shape.Sub(SubKind.Face,i).Props().Center.Z):f.MinBy(i=>o.Shape.Sub(SubKind.Face,i).Props().Center.Z);}
    c.Slots[0].Items.Clear();c.Slots[0].TryAdd(SelItem.Of(rod));c.Slots[1].TryAdd(new SelItem(rod.Id,SelKind.Face,End(rod,false)));c.Slots[2].TryAdd(new SelItem(rod.Id,SelKind.Face,End(rod,true)));
    var pipe=FlowSolver.Solve(c.Setup());
    Console.WriteLine($"FLOW pipe {clock.Elapsed.TotalSeconds:0.#} s, {pipe.Nx}x{pipe.Ny}x{pipe.Nz}, {pipe.Steps} steps, settled {pipe.Settled}, Re {pipe.Reynolds:0.#}/{pipe.SolvedReynolds:0.#}");
    int at(double z)=>(int)((z-pipe.Origin.Z)/pipe.Dx);
    double gradient=(pipe.MeanPressure(2,at(60))-pipe.MeanPressure(2,at(100)))/.04,theory=32*mu*u/(.02*.02);
    Console.WriteLine($"FLOW pipe gradient {gradient:0.###} theory {theory:0.###} in {pipe.FlowIn:0.####} out {pipe.FlowOut:0.####} peak {pipe.MaxSpeed/u:0.###}");
    Check(Math.Abs(gradient-theory)/theory<.15,$"laminar pipe: pressure gradient {gradient:0.##} Pa/m against {theory:0.##} Pa/m by Hagen–Poiseuille");
    Check(Math.Abs(pipe.MaxSpeed/u-2)<.3,$"the middle of the pipe runs at twice the mean speed ({pipe.MaxSpeed/u:0.##} ×)");
    Check(Math.Abs(pipe.FlowIn-pipe.FlowOut)/pipe.FlowIn<.08,$"flow in equals flow out ({pipe.FlowIn:0.###} and {pipe.FlowOut:0.###} L/min)");
    c.Show(pipe);c.OnChanged();var ov=new Overlay();c.BuildOverlay(s,ov);
    Check(ov.Tris.Count>100&&ov.Lines.Count>100&&c.Results.Any(r=>r.Label=="Pressure drop"),"the slice, streamlines and pressure drop show in the panel");
    s.CancelCommand();

    var sq=new CadSession();var ball=sq.AddObject(Occ.Sphere(10),"Ball",ObjKind.Solid);ball=sq.Find(ball.Id)!;
    clock.Restart();
    sq.StartCommand(()=>new FlowStudyCommand());var b=(FlowStudyCommand)sq.ActiveCommand!;
    b.Slots[0].Items.Clear();b.Slots[0].TryAdd(SelItem.Of(ball));
    double ua=100*1.825e-5/(1.204*.02);b.Params.OfType<NumberParam>().First(p=>p.Label=="Speed").Value=ua;
    var sphere=FlowSolver.Solve(b.Setup());
    double cdTheory=24/100.0*(1+.15*Math.Pow(100,.687));
    Console.WriteLine($"FLOW sphere {clock.Elapsed.TotalSeconds:0.#} s, {sphere.Nx}x{sphere.Ny}x{sphere.Nz}, {sphere.Steps} steps, settled {sphere.Settled}, Cd {sphere.DragCoefficient:0.###} theory {cdTheory:0.###}, dx {sphere.Dx:0.###}");
    Check(Math.Abs(sphere.DragCoefficient-cdTheory)/cdTheory<.25,$"sphere at Re 100: drag coefficient {sphere.DragCoefficient:0.##} against {cdTheory:0.##} measured");
    sq.CancelCommand();

    sq.StartCommand(()=>new FlowStudyCommand());var fast=(FlowStudyCommand)sq.ActiveCommand!;
    fast.Slots[0].Items.Clear();fast.Slots[0].TryAdd(SelItem.Of(ball));SetChoice(fast,"Detail",0);fast.Params.OfType<NumberParam>().First(p=>p.Label=="Speed").Value=30;
    var car=FlowSolver.Solve(fast.Setup(),maxSteps:60);fast.Show(car);fast.OnChanged();
    Check(car.SolvedReynolds<car.Reynolds&&fast.Results.Any(r=>r.Value.Contains("not a number")),$"fast flow is run thicker and says so (Re {car.Reynolds:#,0} solved at {car.SolvedReynolds:#,0})");
    sq.CancelCommand();
}
static void SetChoice(Command c,string label,int index)=>c.Params.OfType<ChoiceParam>().First(p=>p.Label==label).Index=index;
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
