using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Prism.Core;
using Prism.Core.Cam;
using Prism.Kernel;
using Prism.Simulation;

namespace Prism.Commands;
public sealed class CamSetupCommand : Command
{
    readonly SelectionSlot _body;readonly TextParam _name;readonly NumberParam _side,_top,_bottom,_dia,_feed,_plunge,_rpm,_clear;readonly ChoiceParam _zero,_zeroZ,_turns;readonly NumberParam _px,_py;
    public CamSetupCommand(){_body=Slot(new(){Label="Model body",Filter=SelFilter.Solid});_name=Text("Setup name","Setup 1");_side=Num("Stock allowance XY",3,0,10000,1);_top=Num("Top allowance",1,0,10000,.5);_bottom=Num("Bottom allowance",0,0,10000,.5);_zero=Choice("Work zero (G54)",0,CamSetup.ZeroXYNames);_dia=Num("Tool diameter",6,.1,100,.5);_feed=Num("Cutting feed",800,1,20000,50,"mm/min");_plunge=Num("Plunge feed",300,1,10000,25,"mm/min");_rpm=Num("Spindle speed",12000,100,60000,500,"rpm");_clear=Num("Clearance above stock",5,.5,100,1);_zeroZ=Choice("Work zero height (Z)",0,CamSetup.ZeroZNames);_px=Num("Picked zero X",0);_py=Num("Picked zero Y",0);_turns=Choice("Machine X axis points",0,CamSetup.TurnNames);PanelSettings.Move(PanelSettings.IndexOf(_zeroZ),PanelSettings.IndexOf(_zero)+1);PanelSettings.Move(PanelSettings.IndexOf(_px),PanelSettings.IndexOf(_zeroZ)+1);PanelSettings.Move(PanelSettings.IndexOf(_py),PanelSettings.IndexOf(_px)+1);PanelSettings.Move(PanelSettings.IndexOf(_turns),PanelSettings.IndexOf(_py)+1);}
    protected override void UpdateVisibility(){_px.IsVisible=_py.IsVisible=_zero.Index==CamSetup.PickedPointXY;}
    // recipes saved before the work zero grid hold no height or picked point: model origin then meant a zero height too
    protected override FeatureRecipe UpgradeRecipe(FeatureRecipe r)
    {
        if(r.Values.Count==Params.Count-4)r=r with{Values=[..r.Values,r.Values[4] is int i&&i==CamSetup.ModelOriginXY?CamSetup.ModelOriginZ:0,0.0,0.0]};
        return r.Values.Count==Params.Count-1?r with{Values=[..r.Values,0]}:r;      // recipes from before the work X direction choice
    }
    /// <summary>With Picked point chosen, a click on the model sets the work zero X and Y.</summary>
    public override bool PickPoint(PickResult hit,bool additive){if(_zero.Index!=CamSetup.PickedPointXY||_body.Items.Count==0)return false;ConfigureInputs(()=>{_px.Value=hit.Point.X;_py.Value=hit.Point.Y;});OnChanged();return true;}
    // ------------------------------------------------------------ preview in the viewport
    (double[] lo,double[] hi)? Stock()
    {
        if(S==null||_body.Items.Count==0||S.Find(_body.Items[0].Obj) is not{HasBrep:true} body)return null;
        var m=body.Mesh;
        return ([m.Min.X-_side.Value,m.Min.Y-_side.Value,m.Min.Z-_bottom.Value],[m.Max.X+_side.Value,m.Max.Y+_side.Value,m.Max.Z+_top.Value]);
    }
    Vec3 DotAt(double[] lo,double[] hi,int col,int row)=>new(lo[0]+(hi[0]-lo[0])*col/2,lo[1]+(hi[1]-lo[1])*row/2,_zeroZ.Index==1?lo[2]:hi[2]);
    /// <summary>Shows the stock, the nine stock points to click, and where the work zero will be, while the setup is still open.</summary>
    public override void BuildOverlay(CadSession s,Overlay o)
    {
        if(Stock() is not{} st)return;
        var(lo,hi)=st;
        Vec3 P(int i)=>new((i&1)==0?lo[0]:hi[0],(i&2)==0?lo[1]:hi[1],(i&4)==0?lo[2]:hi[2]);
        for(int i=0;i<8;i++)foreach(int bit in new[]{1,2,4})if((i&bit)==0)o.AddLine(P(i),P(i|bit),0xFF35B6FF,1.5f);
        foreach(var(opt,col,row) in CamSetup.StockPoints())o.AddDot(DotAt(lo,hi,col,row),opt==_zero.Index?0xFFFFB020:0xFFE8EEF8,opt==_zero.Index?11:8);
        try
        {
            var z=CamSetup.ComputeZero(lo,hi,_zero.Index,_zeroZ.Index,[_px.Value,_py.Value]);var zero=new Vec3(z[0],z[1],z[2]);
            double length=Math.Max((P(7)-P(0)).Length*.12,3);
            var(tc,ts)=CamSetup.Quarter(_turns.Index);o.AddLine(zero,zero+new Vec3(tc,ts,0)*length,0xFFFF6B6F,3);o.AddLine(zero,zero+new Vec3(-ts,tc,0)*length,0xFF3FCF8E,3);o.AddLine(zero,zero+Vec3.UnitZ*length,0xFF4C94FF,3);
            o.AddDot(zero,0xFFFFB020,12);o.AddLabel(zero,$"Work zero (G54)  X {z[0]:0.##}  Y {z[1]:0.##}  Z {z[2]:0.##}");
        }
        catch(InvalidOperationException){}
    }
    /// <summary>Clicking near one of the nine dots chooses that stock point.</summary>
    public override bool ClickScreen(CadSession s,double px,double py)
    {
        if(Stock() is not{} st)return false;
        var(lo,hi)=st;int best=-1;double bestDist=16;
        foreach(var(opt,col,row) in CamSetup.StockPoints())
        {
            if(s.Camera.Project(DotAt(lo,hi,col,row)) is not{} q)continue;
            double d=Math.Sqrt((q.x-px)*(q.x-px)+(q.y-py)*(q.y-py));
            if(d<bestDist){bestDist=d;best=opt;}
        }
        if(best<0)return false;
        _zero.Index=best;return true;
    }
    public override string Title=>"CAM Setup";public override string Icon=>"cnc";public override string OkText=>"Save setup";
    protected override void OnBegin()
    {
        if(S.CamSetup is not{} setup||S.Find(setup.BodyId) is not{} body)return;
        ConfigureInputs(()=>{_body.Items.Add(SelItem.Of(body));_name.Value=setup.Name;_side.Value=Math.Max(0,body.Mesh.Min.X-setup.StockMin[0]);_top.Value=Math.Max(0,setup.StockMax[2]-body.Mesh.Max.Z);_bottom.Value=Math.Max(0,body.Mesh.Min.Z-setup.StockMin[2]);if(setup.ZeroXY>=0){_zero.Index=setup.ZeroXY;_zeroZ.Index=setup.ZeroZ;_turns.Index=setup.ZeroTurns;if(setup.PickedXY is{Length:2}pk){_px.Value=pk[0];_py.Value=pk[1];}}else if(setup.WorkZero.All(v=>v==0)){_zero.Index=CamSetup.ModelOriginXY;_zeroZ.Index=CamSetup.ModelOriginZ;}_dia.Value=setup.ToolDiameter;_feed.Value=setup.Feed;_plunge.Value=setup.Plunge;_rpm.Value=setup.Rpm;_clear.Value=setup.Clearance;});
    }
    public override string Hint=>"Define axis-aligned stock around one model, G54 work zero and shared tool settings for new 2.5D operations. Tool axis is global +Z. Changing a setup requires regenerating its toolpaths.";
    protected override CommandOutput? Build(){if(!Complete)return null;var body=S.Find(_body.Items[0].Obj)!;var m=body.Mesh;double[] lo=[m.Min.X-_side.Value,m.Min.Y-_side.Value,m.Min.Z-_bottom.Value],hi=[m.Max.X+_side.Value,m.Max.Y+_side.Value,m.Max.Z+_top.Value];var setup=new CamSetup(string.IsNullOrWhiteSpace(_name.Value)?"Setup":_name.Value,body.Id,StudySetup.Signature(body),lo,hi,CamSetup.ComputeZero(lo,hi,_zero.Index,_zeroZ.Index,[_px.Value,_py.Value]),_dia.Value,_feed.Value,_plunge.Value,_rpm.Value,_clear.Value,_zero.Index,_zeroZ.Index,_zero.Index==CamSetup.PickedPointXY?[_px.Value,_py.Value]:null,_turns.Index);setup.Validate(body);return new(){NoOp=true,After=s=>{s.SaveCamSetup(setup);s.Say("Saved CAM setup: "+setup.Name);}};}
}
public sealed class ImportGcodeCommand : Command
{
    readonly TextParam _file;
    public ImportGcodeCommand(){_file=Text("G-code file","","Full path from your external slicer");}
    public override string Title=>"Import Nozzle Path";public override string Icon=>"gcode";public override string OkText=>"Import preview";
    public override string Hint=>"Read an external slicer's linear G0/G1 path for nozzle playback. Supports mm/inches, absolute/relative moves, G92 and M82/M83 extrusion. Unsupported motion is rejected. No machine commands are executed.";
    protected override CommandOutput? Build(){if(string.IsNullOrWhiteSpace(_file.Value))return null;var info=new FileInfo(_file.Value);if(!info.Exists)throw new FileNotFoundException("Choose an existing G-code file.");if(info.Length>32_000_000)throw new InvalidOperationException("Preview is limited to 32 MB.");var preview=LinearGcode.Parse(File.ReadAllText(info.FullName));var output=new CommandOutput{Label="Import nozzle path"};output.Add.Add(new(Occ.Line(Vec3.Zero,new(0,0,.01)),Path.GetFileNameWithoutExtension(info.Name),ObjKind.Toolpath,Asm:preview.Path));Results.Clear();foreach(var note in preview.Notes)Results.Add(new ResultLine("Preview note",note,2));return output;}
}
public sealed class CamPlaybackCommand : AnalysisCommand
{
    ChoiceParam _path=null!;readonly NumberParam _position,_speed;Guid[] _ids=[];
    public CamPlaybackCommand(){_position=Num("Seek position",0,0,100,1,"%");_speed=Num("Playback speed",10,.1,1000,1,"×");Add(new ActionParam{Label="Play / resume",Run=Play});Add(new ActionParam{Label="Pause",Run=()=>S.PauseCam()});Add(new ActionParam{Label="Seek",Run=()=>{try{Load();S.PauseCam();S.SeekCam(_position.Value/100);}catch(Exception e){S.Say(e.Message,true);}}});Add(new ActionParam{Label="Reset",Run=()=>{S.PauseCam();S.SeekCam(0);}});}
    public override string Title=>"Toolpath Playback";public override string Icon=>"animation";
    public override string Hint=>"Animate a cutter or nozzle along the programmed path. Pause, seek and change speed to inspect moves. This previews motion only: no stock removal, machine kinematics or collision checking.";
    protected override void OnBegin(){var paths=S.Objects.Where(o=>o.Visible&&o.Asm is ToolpathInfo).OrderBy(o=>((ToolpathInfo)o.Asm!).ProgramOrder).ToArray();_ids=paths.Select(o=>o.Id).ToArray();_path=Choice("Operation",0,paths.Select(o=>o.Name).DefaultIfEmpty("No toolpaths").ToArray());Params.Move(Params.IndexOf(_path),0);PanelSettings.Move(PanelSettings.IndexOf(_path),0);S.CamPlaybackChanged+=Refresh;}
    void Refresh(){if(ReferenceEquals(S.ActiveCommand,this))OnChanged();}
    public override void OnEnd(){S.CamPlaybackChanged-=Refresh;S.StopCamPlayback();}
    void Load(){if(_ids.Length==0)throw new InvalidOperationException("Create a CNC path or import nozzle G-code first.");var obj=S.Find(_ids[Math.Clamp(_path.Index,0,_ids.Length-1)])??throw new InvalidOperationException("The toolpath was deleted.");CamValidation.Validate(S,(ToolpathInfo)obj.Asm!);if(S.CamPathId!=obj.Id)S.LoadCamPath(obj);}
    void Play(){try{Load();S.CamSpeed=_speed.Value;S.PlayCam();}catch(Exception e){S.Say(e.Message,true);}}
    protected override void Analyze(){if(S.CamPlaying)S.CamSpeed=_speed.Value;if(S.CamTimeline is not{} t){Row("Playback","Choose a path, then Play or Seek.");return;}var(_,_,tip)=t.At(S.CamSeconds);Row("Status",S.CamPlaying?"Playing":"Paused");Row("Time",$"{S.CamSeconds:0.0} / {t.Duration:0.0} s (feed-based estimate)");Row("Tool tip",$"X {tip.X:0.###} · Y {tip.Y:0.###} · Z {tip.Z:0.###} mm");if(t.Path.MoveCount>40000)Row("Display","Sparse overview for large paths; tool-tip motion uses every programmed move.",2);Row("Verification","Motion preview only; collision and stock removal are not calculated.",2);}
}
public static class CamValidation
{
    public static void Validate(CadSession s,ToolpathInfo path)
    {
        if(new[]{path.ToolDia,path.Feed,path.Plunge,path.Rpm,path.SafeZ}.Any(v=>!double.IsFinite(v))||path.ToolDia<=0||path.Feed<=0||path.Plunge<=0||path.Rpm<0)throw new InvalidOperationException("Invalid tool or feed data.");
        if(path.SetupSignature!=null&&s.SetupFor(path)==null)throw new InvalidOperationException("CAM setup changed or was removed. Regenerate the operation before playback or output.");
        if(path.SetupBody is{} id){var body=s.Find(id)??throw new InvalidOperationException("The CAM model was deleted.");StudySetup.Validate(body,path.SourceSignature,[]);}
        if(path.Sources!=null)foreach(var pair in path.Sources){var source=s.Find(pair.Key)??throw new InvalidOperationException("An operation source was deleted. Regenerate the path.");StudySetup.Validate(source,pair.Value,[]);}
        _=new PathTimeline(path);
        if(!path.IsPrint&&!path.Operation.StartsWith("Turning",StringComparison.Ordinal)&&(path.Move(0).kind!=MoveKind.Rapid||path.Move(0).z<path.SafeZ-1e-6))throw new InvalidOperationException("Milling must start with a rapid at or above clearance.");
        if(!path.IsPrint&&!path.Operation.StartsWith("Turning",StringComparison.Ordinal))for(int i=1;i<path.MoveCount;i++)
        {
            var a=path.Move(i-1);var b=path.Move(i);
            if(b.kind==MoveKind.Rapid&&(Math.Abs(a.x-b.x)>1e-6||Math.Abs(a.y-b.y)>1e-6)&&Math.Min(a.z,b.z)<path.SafeZ-1e-6)throw new InvalidOperationException("A lateral rapid runs below the clearance plane. Regenerate or correct this operation.");
        }
    }
}
public sealed class CamReviewCommand : AnalysisCommand
{
    readonly TextParam _folder;
    public CamReviewCommand(){_folder=Text("Setup sheet folder",Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"Prism","CAM"));Add(new ActionParam{Label="Save setup sheet",Run=Save});}
    public override string Title=>"CAM Review";public override string Icon=>"check";
    public override string Hint=>"Review source freshness, tool settings, travel and feed-based time. Export a setup sheet before posting. A successful review does not certify machine clearance or cutting conditions.";
    protected override void Analyze(){if(S.CamSetup is{} setup){Row("Setup",setup.Name);Row("Work zero",string.Join(", ",setup.WorkZero.Select(v=>v.ToString("0.###")))+" mm");}foreach(var obj in Program()){var p=(ToolpathInfo)obj.Asm!;try{CamValidation.Validate(S,p);Row(obj.Name,$"{p.Tool} · {p.MoveCount:N0} moves · {new PathTimeline(p).Duration/60:0.##} min");}catch(Exception e){Row(obj.Name,e.Message,3);}}Row("Scope","Feed-based review only. Use Stock review for sampled milling removal and contacts; tool axis is global Z.",2);}
    System.Collections.Generic.IEnumerable<SceneObject> Program()=>S.CamSetup==null?S.MillingProgram():S.ActiveMillingProgram();
    void Save(){try{var paths=Program().ToArray();if(paths.Length==0)throw new InvalidOperationException("No toolpaths to report.");foreach(var obj in paths)CamValidation.Validate(S,(ToolpathInfo)obj.Asm!);Directory.CreateDirectory(_folder.Value);var report=new{setup=S.CamSetup,operations=paths.Select(o=>new{o.Name,path=o.Asm}),scope="Motion preview only; no stock removal or collision verification"};File.WriteAllText(Path.Combine(_folder.Value,"setup.json"),System.Text.Json.JsonSerializer.Serialize(report,new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));string H(string text)=>System.Net.WebUtility.HtmlEncode(text);var lines=paths.Select(o=>{var p=(ToolpathInfo)o.Asm!;return $"<tr><td>{H(o.Name)}</td><td>{H(p.Tool)}</td><td>{p.Feed:0.##}</td><td>{p.Plunge:0.##}</td><td>{p.Rpm:0}</td><td>{new PathTimeline(p).Duration/60:0.##}</td></tr>";});File.WriteAllText(Path.Combine(_folder.Value,"setup.html"),"<!doctype html><meta charset=utf-8><title>Prism setup sheet</title><style>body{font:15px system-ui;margin:40px;color:#172333}table{border-collapse:collapse}td,th{padding:10px;border:1px solid #bac4d1;text-align:left}</style><h1>"+H(S.CamSetup?.Name??"Operations")+"</h1><p>Global Z tool axis. G54: "+H(S.CamSetup==null?"Post settings":string.Join(", ",S.CamSetup.WorkZero))+" mm</p><table><tr><th>Operation<th>Tool<th>Feed mm/min<th>Plunge mm/min<th>Spindle rpm<th>Time min</tr>"+string.Join("",lines)+"</table><p>Feed-based timing excludes acceleration and setup time. No stock removal or collision verification.</p>");S.Say("Saved setup sheet to "+_folder.Value);}catch(Exception e){S.Say(e.Message,true);}}
}
