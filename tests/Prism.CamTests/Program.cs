using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Prism.Commands;
using Prism.Core;
using Prism.Core.Cam;
using Prism.Kernel;
using Prism.Meshing;
using Prism.Simulation;
using Prism.ViewModels;
using Prism.Views;
using Prism.Ribbon;

AppBuilder.Configure<TestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions{UseHeadlessDrawing=false}).UseSkia().SetupWithoutStarting();
NativeSearch.Initialize();int checks=0;
void Check(bool value,string label){if(!value)throw new Exception(label);Console.WriteLine("PASS "+label);checks++;}
void Reject(Action action,string label){try{action();}catch(InvalidOperationException){Check(true,label);return;}throw new Exception(label);}
TriMesh Mesh(Shape shape)=>MeshAnalysis.FromMeshData(shape.Tessellate(.05));
using var outer=Occ.Box(20,20,20);
using var inside=Occ.Translate(Occ.Box(12,12,12),new(0,0,4));
using var hollow=Occ.Cut(outer,inside);
var block=ResinAnalysis.Analyze(Mesh(outer),.5);
Check(block.EnclosedVoids==0&&block.CupLayers==0&&block.IslandRegions==0,"solid block has no resin voids, cups or islands");
var sealedReport=ResinAnalysis.Analyze(Mesh(hollow),.5);
Check(sealedReport.EnclosedVoids==1&&Math.Abs(sealedReport.TrappedVolume-1728)<1,"sealed hollow detects its analytically known 12 mm cubic cavity");
Check(sealedReport.CupLayers>0,"sealed hollow flags potential unvented printing layers");
using var openInner=Occ.Translate(Occ.Box(12,12,22),new(0,0,4));
using var cup=Occ.Cut(outer,openInner);
var cupReport=ResinAnalysis.Analyze(Mesh(cup),.5);
Check(cupReport.EnclosedVoids==0&&cupReport.CupLayers>0,"open-top cup is not a sealed void but needs suction review while printing");
using var vent=Occ.Translate(Occ.Box(24,4,4),new(0,0,4));
using var vented=Occ.Cut(cup,vent);
var ventedReport=ResinAnalysis.Analyze(Mesh(vented),.5);
Check(ventedReport.EnclosedVoids==0&&ventedReport.CupLayers==0,"side vent at the cavity floor prevents sampled suction cups");
var floating=TriMesh.Merge([Mesh(Occ.Box(10,10,2)),Mesh(Occ.Translate(Occ.Box(4,4,2),new(0,0,8)))]);
Check(ResinAnalysis.Analyze(floating,.5).IslandRegions==1,"disconnected upper body creates one unsupported island start");
Reject(()=>ResinAnalysis.Analyze(new TriMesh([0,0,0,1,0,0,0,1,0],[0,1,2]),1),"open mesh is rejected rather than certified cavity-free");
var session=new CadSession();var body=session.AddObject(Occ.Box(10,10,5),"Part",ObjKind.Solid);
var setup=new CamSetup("Test",body.Id,StudySetup.Signature(body),[-5,-5,0],[5,5,10],[0,0,10]);session.CamSetup=setup;
ToolpathInfo Path(double z,MoveKind kind=MoveKind.Cut,string tool="6 mm end mill")=>new("Face",tool,6,600,200,12000,15,[-8,0,15,0,-8,0,z,2,8,0,z,(int)kind],[0],false,false);
var target=Mesh(body.Shape);
var removal=StockSimulation.Run(setup,target,[Path(5)],.5,25,20,.01);
Check(removal.RemovedVolume>200&&removal.GougeMoves==0&&removal.RapidContacts==0,"face pass removes stock without gouging the target");
Check(removal.RemainingVolume>0,"partial face pass reports remaining stock");
var gouge=StockSimulation.Run(setup,target,[Path(3)],.5,25,20,.01);
Check(gouge.GougeMoves>0&&gouge.GougeVolume>0,"toolpath below target detects gouging");
var rapid=StockSimulation.Run(setup,target,[Path(8,MoveKind.Rapid)],.5,25,20,.01);
Check(rapid.RapidContacts>0,"lateral rapid through uncut stock reports contact");
var holder=StockSimulation.Run(setup,target,[Path(5)],.5,1,20,.01);
Check(holder.HolderContacts>0,"short flute length detects holder contact against stock");
var ball=StockSimulation.Run(setup,target,[Path(5,tool:"6 mm ball nose")],.5,25,20,.01);
Check(ball.RemovedVolume<removal.RemovedVolume,"ball cutter removes less stock than a flat cutter at the same tip height");
var before=StockSimulation.Run(setup,target,[Path(5)],.5,25,20,.01,0);
Check(before.RemovedVolume==0,"zero program position preserves initial stock");
Reject(()=>StockSimulation.Run(setup,target,[Path(5) with{IsPrint=true}],1,25,20,.1),"print paths are rejected by milling simulation");
foreach(var command in new ManufacturingCheckCommand[]{new FdmCheckCommand(),new ResinCheckCommand(),new CncDesignCheckCommand()})
{
    session.StartCommand(command);command.Params.OfType<ActionParam>().Single(p=>p.Label=="Run checks").Run();
    Check(command.Error==null&&command.Results.Any(r=>r.Label=="Model"),command.Title+" accepts exact CAD bodies directly: "+command.Error);
    session.CancelCommand();
}
var importOutput=new CommandOutput();importOutput.Add.Add(new(null,"Imported mesh",Tri:Mesh(outer)));session.Apply(importOutput);
var meshCommand=new ResinCheckCommand();session.StartCommand(meshCommand);meshCommand.Slots[0].Items.Add(SelItem.Of(session.Objects.Last()));meshCommand.Params.OfType<ActionParam>().Single().Run();
Check(meshCommand.Error==null&&meshCommand.Results.Any(r=>r.Label=="Sealed cavities"),"resin checks also accept imported meshes");session.CancelCommand();
var workspace=MainViewModel.WorkspaceDefs.Single(w=>w.Id=="make");
Check(workspace.Tabs.Contains("3D FDM")&&workspace.Tabs.Contains("3D Resin")&&!workspace.Tabs.Contains("Tooling"),"CAM exposes distinct FDM and resin workflows without mold tooling");
var safe=Path(5) with{SetupBody=body.Id,SourceSignature=StudySetup.Signature(body),SetupSignature=setup.Signature()};
session.SaveCamSetup(setup);var secondSetup=setup with{Name="Second setup",WorkZero=[-5,-5,10]};session.SaveCamSetup(secondSetup);
CamValidation.Validate(session,safe);Check(true,"switching active setup preserves paths attached to another saved setup");
session.SaveCamSetup(setup with{Feed=123});Reject(()=>CamValidation.Validate(session,safe),"editing a named setup invalidates its previous paths");
session.SaveCamSetup(setup);
var first=session.AddObject(Occ.Line(Vec3.Zero,Vec3.UnitZ),"First",ObjKind.Toolpath);session.Update(first.Id,o=>o.WithAsm(safe));
var second=session.AddObject(Occ.Line(Vec3.Zero,Vec3.UnitZ),"Second",ObjKind.Toolpath);session.Update(second.Id,o=>o.WithAsm(safe with{Operation="Second pass"}));
var reorder=new CamProgramOrderCommand();session.StartCommand(reorder);reorder.Params.OfType<ChoiceParam>().Single().Index=1;reorder.Params.OfType<IntParam>().Single().Value=1;session.CommitCommand();
Check(session.MillingProgram().First().Id==second.Id,"committed operation order drives the machining program");
session.Undo();Check(session.MillingProgram().First().Id==first.Id,"operation reordering is undoable");session.Redo();
session.Update(first.Id,o=>o.WithAsm(((ToolpathInfo)o.Asm!) with{SetupName=setup.Name}));session.SaveCamSetup(setup with{Feed=321});
Check(session.ActiveMillingProgram().Any(o=>o.Id==first.Id),"stale operations stay in their named setup for validation instead of being silently omitted");
Reject(()=>CamValidation.Validate(session,(ToolpathInfo)session.Find(first.Id)!.Asm!),"named setup review rejects stale operations");session.SaveCamSetup(setup);
string file=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"prism-cam-"+Guid.NewGuid().ToString("N")+".prism");
try{session.Save(file);var reopened=new CadSession();reopened.Open(file);Check(reopened.CamSetups.Count==2&&reopened.MillingProgram().First().Id==second.Id,"named setups and operation order survive save/reopen");reopened.NewDocument();Check(reopened.CamSetups.Count==0&&reopened.CamSetup==null,"new document clears all CAM setups");}finally{File.Delete(file);}
var invalidRapid=Path(8,MoveKind.Rapid);
Reject(()=>Post.Cnc([invalidRapid],Controller.GRBL,Vec3.Zero,"Bad rapid"),"low-level post rejects lateral rapids below clearance");
Reject(()=>Post.Cnc([safe with{Moves=[-8,0,5,0,-8,0,3,2]}],Controller.GRBL,Vec3.Zero,"Low start"),"low-level post rejects an initial milling rapid below clearance");
var post=Post.Cnc([safe with{Operation="Later",ProgramOrder=2},safe with{Operation="Earlier",ProgramOrder=1,ToolDia=3}],Controller.GRBL,Vec3.Zero,"Title)\nM3");
Check(post.IndexOf("(Earlier)",StringComparison.Ordinal)<post.IndexOf("(Later)",StringComparison.Ordinal)&&post.Contains("Fit tool 2"),"post honors program order and changes cutters for different diameters");
Check(!post.Contains("\nM3\n"),"part names cannot escape G-code comments");
var turn=new ToolpathInfo("Turning","Lathe tool",6,200,100,1500,20,[15,0,5,0,10,0,5,2,10,0,-5,1,15,0,-5,0],[0],false,false);
Check(Post.Lathe([turn],Controller.LinuxCNC,"Lathe").Contains("G7")&&!Post.Lathe([turn],Controller.Mach3,"Lathe").Contains("G7"),"lathe post emits G7 only for LinuxCNC");
CamValidation.Validate(session,turn);Check(true,"turning validation does not apply the milling Z-clearance rule");
var roughSession=new CadSession();var roughBody=roughSession.AddObject(Occ.Box(10,10,5),"Roughing model",ObjKind.Solid);
roughSession.CamSetup=new("Rough stock",roughBody.Id,StudySetup.Signature(roughBody),[-10,-10,0],[10,10,8],[-10,-10,8]);
var roughCommand=new Rough3DCommand();roughSession.StartCommand(roughCommand);var roughOutput=roughCommand.TryBuild();
Check(roughOutput?.Add.Single().Asm is ToolpathInfo,"3D roughing generates a layered stock-clearing path: "+roughCommand.Error);
var roughPath=(ToolpathInfo)roughOutput!.Add.Single().Asm!;CamValidation.Validate(roughSession,roughPath);
var roughStock=StockSimulation.Run(roughSession.CamSetup,Mesh(roughBody.Shape),[roughPath],.5,25,20,.1);
Check(roughStock.RemovedVolume>0&&roughStock.GougeMoves==0,"generated 3D roughing removes surrounding stock without sampled target gouges");roughSession.CancelCommand();
string shots=System.IO.Path.GetFullPath("docs/cam-workflows");Directory.CreateDirectory(shots);
void Capture(Control control,string name,int width,int height)
{
    var window=new Window{Width=width,Height=height,Content=control};window.Show();Dispatcher.UIThread.RunJobs();
    using var bitmap=new RenderTargetBitmap(new PixelSize(width,height));bitmap.Render(control);bitmap.Save(System.IO.Path.Combine(shots,name+".png"));window.Close();
}
AppSettings.Current.Ribbon=new RibbonLayout();AppSettings.Current.LastPicks.Clear();
var vm=new MainViewModel();var previewBody=vm.Session.AddObject(hollow,"Resin sample",ObjKind.Solid);
vm.WorkspaceId="make";
foreach(var tab in vm.Tabs)tab.IsVisible=tab.IsFile||workspace.Tabs.Contains(tab.Title);
foreach(var title in new[]{"Subtractive","3D FDM","3D Resin"})Check(vm.Tabs.Single(t=>t.Title==title).IsVisible,title+" is visible in CAM");
Check(vm.Tabs.Where(t=>t.IsVisible&&!t.IsFile).Select(t=>t.Title).SequenceEqual(new[]{"Home","Subtractive","3D FDM","3D Resin","Analyze","View"}),"CAM tabs follow the manufacturing workflow order");
Check(vm.Tabs.Single(t=>t.Title=="3D Resin").Buttons.Any(b=>b.Label=="Drain / vent hole")&&vm.Tabs.Single(t=>t.Title=="3D FDM").Buttons.Any(b=>b.Label=="Import nozzle path"),"resin drainage and FDM nozzle review are exposed in their own ribbons");
foreach(var theme in new[]{ThemeVariant.Dark,ThemeVariant.Light})
{
    Application.Current!.RequestedThemeVariant=theme;string suffix=theme==ThemeVariant.Dark?"dark":"light";
    foreach(var title in new[]{"Subtractive","3D FDM","3D Resin"}){vm.SelectTab(vm.Tabs.Single(t=>t.Title==title));Capture(new RibbonView{DataContext=vm},title.Replace(" ","-").ToLowerInvariant()+"-"+suffix,1024,180);}
    foreach(var command in new ManufacturingCheckCommand[]{new FdmCheckCommand(),new ResinCheckCommand()})
    {
        vm.Session.StartCommand(command);command.Params.OfType<ActionParam>().Single(p=>p.Label=="Run checks").Run();
        Capture(new CommandPanel{DataContext=vm},(command is ResinCheckCommand?"resin":"fdm")+"-panel-"+suffix,394,960);vm.Session.CancelCommand();
    }
}
vm.Session.NewDocument();var stockBody=vm.Session.AddObject(Occ.Box(10,10,5),"Stock review sample",ObjKind.Solid);
vm.Session.CamSetup=new("Review setup",stockBody.Id,StudySetup.Signature(stockBody),[-10,-10,0],[10,10,8],[-10,-10,8]);
vm.Session.StartCommand(new Rough3DCommand());vm.Session.CommitCommand();
var stockCommand=new CncStockCommand();vm.Session.StartCommand(stockCommand);stockCommand.Params.OfType<ActionParam>().Single().Run();
Check(stockCommand.Error==null&&stockCommand.Results.Any(r=>r.Label=="Removed stock"),"stock-review panel analyzes generated paths on the active setup: "+stockCommand.Error);
Capture(new CommandPanel{DataContext=vm},"stock-panel-light",394,960);
stockCommand.Params.OfType<NumberParam>().Single(p=>p.Label=="Program position").Value=50;
Check(!stockCommand.Results.Any(r=>r.Label=="Removed stock"),"changing review parameters invalidates old simulation results");vm.Session.CancelCommand();
Console.WriteLine($"SUCCESS: {checks} checks");
class TestApp:Application{public override void Initialize(){Styles.Add(new FluentTheme());Styles.Add(new StyleInclude(new Uri("avares://Prism/")){Source=new Uri("avares://Prism/Styles/Theme.axaml")});}}
static class NativeSearch{[DllImport("kernel32",CharSet=CharSet.Unicode)]static extern bool SetDllDirectory(string path);public static void Initialize()=>SetDllDirectory(System.IO.Path.GetFullPath("native/bin"));}
