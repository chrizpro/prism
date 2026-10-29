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
    readonly SelectionSlot _body;readonly TextParam _name;readonly NumberParam _side,_top,_bottom,_dia,_feed,_plunge,_rpm,_clear;readonly ChoiceParam _zero;
    public CamSetupCommand(){_body=Slot(new(){Label="Model body",Filter=SelFilter.Solid});_name=Text("Setup name","Setup 1");_side=Num("Stock allowance XY",3,0,10000,1);_top=Num("Top allowance",1,0,10000,.5);_bottom=Num("Bottom allowance",0,0,10000,.5);_zero=Choice("Work zero (G54)",0,"Stock lower-left, top","Model origin");_dia=Num("Tool diameter",6,.1,100,.5);_feed=Num("Cutting feed",800,1,20000,50,"mm/min");_plunge=Num("Plunge feed",300,1,10000,25,"mm/min");_rpm=Num("Spindle speed",12000,100,60000,500,"rpm");_clear=Num("Clearance above stock",5,.5,100,1);}
    public override string Title=>"CAM Setup";public override string Icon=>"cnc";public override string OkText=>"Save setup";
    public override string Hint=>"Define axis-aligned stock around one model, G54 work zero and shared tool settings for new 2.5D operations. Tool axis is global +Z. Changing a setup requires regenerating its toolpaths.";
    protected override CommandOutput? Build(){if(!Complete)return null;var body=S.Find(_body.Items[0].Obj)!;var m=body.Mesh;double[] lo=[m.Min.X-_side.Value,m.Min.Y-_side.Value,m.Min.Z-_bottom.Value],hi=[m.Max.X+_side.Value,m.Max.Y+_side.Value,m.Max.Z+_top.Value];var setup=new CamSetup(string.IsNullOrWhiteSpace(_name.Value)?"Setup":_name.Value,body.Id,StudySetup.Signature(body),lo,hi,_zero.Index==0?[lo[0],lo[1],hi[2]]:[0,0,0],_dia.Value,_feed.Value,_plunge.Value,_rpm.Value,_clear.Value);setup.Validate(body);return new(){NoOp=true,After=s=>{s.CamSetup=setup;s.Dirty=true;s.Invalidate();s.Say("Saved CAM setup: "+setup.Name);}};}
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
    protected override void OnBegin(){var paths=S.Objects.Where(o=>o.Asm is ToolpathInfo).ToArray();_ids=paths.Select(o=>o.Id).ToArray();_path=Choice("Operation",0,paths.Select(o=>o.Name).DefaultIfEmpty("No toolpaths").ToArray());Params.Move(Params.IndexOf(_path),0);PanelSettings.Move(PanelSettings.IndexOf(_path),0);S.CamPlaybackChanged+=Refresh;}
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
        if(path.SetupSignature!=null&&(s.CamSetup==null||s.CamSetup.Signature()!=path.SetupSignature))throw new InvalidOperationException("CAM setup changed. Regenerate the operation before playback or output.");
        if(path.SetupBody is{} id){var body=s.Find(id)??throw new InvalidOperationException("The CAM model was deleted.");StudySetup.Validate(body,path.SourceSignature,[]);}
        if(path.Sources!=null)foreach(var pair in path.Sources){var source=s.Find(pair.Key)??throw new InvalidOperationException("An operation source was deleted. Regenerate the path.");StudySetup.Validate(source,pair.Value,[]);}
        _=new PathTimeline(path);
        if(!path.IsPrint)for(int i=1;i<path.MoveCount;i++)
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
    protected override void Analyze(){if(S.CamSetup is{} setup){Row("Setup",setup.Name);Row("Work zero",string.Join(", ",setup.WorkZero.Select(v=>v.ToString("0.###")))+" mm");}foreach(var obj in S.Objects.Where(o=>o.Asm is ToolpathInfo)){var p=(ToolpathInfo)obj.Asm!;try{CamValidation.Validate(S,p);Row(obj.Name,$"{p.Tool} · {p.MoveCount:N0} moves · {new PathTimeline(p).Duration/60:0.##} min");}catch(Exception e){Row(obj.Name,e.Message,3);}}Row("Scope","No stock removal or collision verification. Toolpaths use the global Z axis.",2);}
    void Save(){try{var paths=S.Objects.Where(o=>o.Asm is ToolpathInfo).ToArray();if(paths.Length==0)throw new InvalidOperationException("No toolpaths to report.");foreach(var obj in paths)CamValidation.Validate(S,(ToolpathInfo)obj.Asm!);Directory.CreateDirectory(_folder.Value);var report=new{setup=S.CamSetup,operations=paths.Select(o=>new{o.Name,path=o.Asm}),scope="Motion preview only; no stock removal or collision verification"};File.WriteAllText(Path.Combine(_folder.Value,"setup.json"),System.Text.Json.JsonSerializer.Serialize(report,new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));string H(string text)=>System.Net.WebUtility.HtmlEncode(text);var lines=paths.Select(o=>{var p=(ToolpathInfo)o.Asm!;return $"<tr><td>{H(o.Name)}</td><td>{H(p.Tool)}</td><td>{p.Feed:0.##}</td><td>{p.Plunge:0.##}</td><td>{p.Rpm:0}</td><td>{new PathTimeline(p).Duration/60:0.##}</td></tr>";});File.WriteAllText(Path.Combine(_folder.Value,"setup.html"),"<!doctype html><meta charset=utf-8><title>Prism setup sheet</title><style>body{font:15px system-ui;margin:40px;color:#172333}table{border-collapse:collapse}td,th{padding:10px;border:1px solid #bac4d1;text-align:left}</style><h1>"+H(S.CamSetup?.Name??"Operations")+"</h1><p>Global Z tool axis. G54: "+H(S.CamSetup==null?"Post settings":string.Join(", ",S.CamSetup.WorkZero))+" mm</p><table><tr><th>Operation<th>Tool<th>Feed mm/min<th>Plunge mm/min<th>Spindle rpm<th>Time min</tr>"+string.Join("",lines)+"</table><p>Feed-based timing excludes acceleration and setup time. No stock removal or collision verification.</p>");S.Say("Saved setup sheet to "+_folder.Value);}catch(Exception e){S.Say(e.Message,true);}}
}
