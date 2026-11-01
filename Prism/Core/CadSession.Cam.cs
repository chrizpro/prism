using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using Prism.Core.Cam;
using Prism.Kernel;

namespace Prism.Core;
public sealed partial class CadSession
{
    public CamSetup? CamSetup {get;set;}
    public List<CamSetup> CamSetups {get;}=[];
    public void SaveCamSetup(CamSetup setup)
    {
        setup.Validate(Find(setup.BodyId)??throw new InvalidOperationException("The CAM setup model was deleted."));
        if(CamSetup is{} previous&&previous.Name!=setup.Name&&!CamSetups.Any(s=>s.Name==previous.Name))CamSetups.Add(previous);
        CamSetups.RemoveAll(s=>s.Name==setup.Name);CamSetups.Add(setup);CamSetup=setup;Dirty=true;Invalidate();
    }
    public CamSetup? SetupFor(ToolpathInfo path)=>path.SetupSignature==null?null:
        (CamSetup?.Signature()==path.SetupSignature?CamSetup:CamSetups.FirstOrDefault(s=>s.Signature()==path.SetupSignature));
    public IEnumerable<SceneObject> MillingProgram()=>Objects.Where(o=>o.Visible&&o.Asm is ToolpathInfo{IsPrint:false}).OrderBy(o=>((ToolpathInfo)o.Asm!).ProgramOrder);
    public IEnumerable<SceneObject> ActiveMillingProgram()=>MillingProgram().Where(o=>
        o.Asm is ToolpathInfo p&&(p.SetupSignature==null||p.SetupName!=null&&p.SetupName==CamSetup?.Name||p.SetupName==null&&p.SetupSignature==CamSetup?.Signature()));
    public PathTimeline? CamTimeline {get;private set;}
    public Guid? CamPathId {get;private set;}
    public double CamSeconds {get;private set;}
    public double CamSpeed {get;set;}=1;
    public bool CamPlaying {get;private set;}
    public event Action? CamPlaybackChanged;
    DispatcherTimer? _camTimer;readonly Stopwatch _camClock=new();double _lastCamTick;int _camTicks;
    public void LoadCamPath(SceneObject obj)
    {
        if(obj.Asm is not ToolpathInfo tp)throw new InvalidOperationException("Select a toolpath first.");
        StopCamPlayback();CamTimeline=new(tp);CamPathId=obj.Id;CamSeconds=0;CamViewLayer=int.MaxValue;Invalidate();
    }
    public void SeekCam(double fraction){if(CamTimeline==null)return;if(!double.IsFinite(fraction))throw new ArgumentException("Position must be finite.");CamSeconds=Math.Clamp(fraction,0,1)*CamTimeline.Duration;Invalidate();CamPlaybackChanged?.Invoke();}
    public void PlayCam()
    {
        if(CamTimeline==null)throw new InvalidOperationException("Load a toolpath first.");
        if(!double.IsFinite(CamSpeed)||CamSpeed<=0||CamSpeed>1000)throw new InvalidOperationException("Playback speed must be 0–1000×.");
        if(CamSeconds>=CamTimeline.Duration)CamSeconds=0;
        _camTimer??=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(33)};
        _camTimer.Tick-=CamTick;_camTimer.Tick+=CamTick;_camClock.Restart();_lastCamTick=0;CamPlaying=true;_camTimer.Start();
    }
    void CamTick(object? sender,EventArgs e)
    {
        if(CamTimeline==null||CamPathId==null||Find(CamPathId.Value)?.Asm!=CamTimeline.Path){StopCamPlayback();return;}
        double now=_camClock.Elapsed.TotalSeconds;CamSeconds=Math.Min(CamTimeline.Duration,CamSeconds+(now-_lastCamTick)*CamSpeed);_lastCamTick=now;
        if(CamSeconds>=CamTimeline.Duration){CamPlaying=false;_camTimer?.Stop();}
        Invalidate();if(++_camTicks%8==0||!CamPlaying)CamPlaybackChanged?.Invoke();
    }
    public void PauseCam(){CamPlaying=false;_camTimer?.Stop();CamPlaybackChanged?.Invoke();}
    public void StopCamPlayback(){CamPlaying=false;_camTimer?.Stop();CamTimeline=null;CamPathId=null;CamSeconds=0;Invalidate();}
    (Vec3 min,Vec3 max)? CamBounds()
    {
        Vec3 lo=new(double.MaxValue,double.MaxValue,double.MaxValue),hi=-lo;bool any=false;
        void Include(Vec3 p){lo=new(Math.Min(lo.X,p.X),Math.Min(lo.Y,p.Y),Math.Min(lo.Z,p.Z));hi=new(Math.Max(hi.X,p.X),Math.Max(hi.Y,p.Y),Math.Max(hi.Z,p.Z));any=true;}
        if(CamSetup is{} setup&&setup.StockMin.Length==3&&setup.StockMax.Length==3){Include(new(setup.StockMin[0],setup.StockMin[1],setup.StockMin[2]));Include(new(setup.StockMax[0],setup.StockMax[1],setup.StockMax[2]));}
        foreach(var obj in Objects)if(obj.Visible&&!HiddenIds.Contains(obj.Id)&&obj.Asm is ToolpathInfo tp)for(int i=0;i<tp.MoveCount;i++){var(x,y,z,_)=tp.Move(i);Include(new(x,y,z));}
        return any?(lo,hi+Vec3.UnitZ*8):null;
    }
    void DrawCamSetup(Overlay overlay)
    {
        if(CamSetup is not {} setup)return;
        var a=setup.StockMin;var b=setup.StockMax;if(a.Length!=3||b.Length!=3)return;
        Vec3 P(int i)=>new((i&1)==0?a[0]:b[0],(i&2)==0?a[1]:b[1],(i&4)==0?a[2]:b[2]);
        for(int i=0;i<8;i++)foreach(int bit in new[]{1,2,4})if((i&bit)==0)overlay.AddLine(P(i),P(i|bit),0xAA35B6FF,1);
        var zero=setup.Origin();double length=Math.Max((P(7)-P(0)).Length*.1,3);overlay.AddLine(zero,zero+Vec3.UnitX*length,0xFFFF6B6F);overlay.AddLine(zero,zero+Vec3.UnitY*length,0xFF3FCF8E);overlay.AddLine(zero,zero+Vec3.UnitZ*length,0xFF4C94FF);overlay.AddLabel(zero,"G54 · "+setup.Name);
    }
    void DrawCamTool(Overlay overlay)
    {
        if(CamTimeline is not{} timeline)return;
        var(_,_,tip)=timeline.At(CamSeconds);bool nozzle=timeline.Path.IsPrint;double radius=Math.Max(timeline.Path.ToolDia*.5,nozzle?1:0.5),height=Math.Max(radius*5,6);
        if(nozzle){radius=2.5;height=6;}
        var top=tip+Vec3.UnitZ*height;uint color=nozzle?0xFFFFC53D:0xFF35B6FF;
        for(int i=0;i<16;i++){double a=i*Math.PI/8,b=(i+1)*Math.PI/8;var p=top+new Vec3(Math.Cos(a)*radius,Math.Sin(a)*radius,0);var q=top+new Vec3(Math.Cos(b)*radius,Math.Sin(b)*radius,0);overlay.AddLine(p,q,color);if(nozzle)overlay.AddTri(tip,p,q,color);else {var low=p-Vec3.UnitZ*height;overlay.AddLine(low,p,color);overlay.AddLine(low,q-Vec3.UnitZ*height,color);}}
        if(nozzle)
        {
            var center=top+Vec3.UnitZ*2;
            Vec3 P(int i)=>center+new Vec3((i&1)==0?-4:4,(i&2)==0?-4:4,(i&4)==0?-2:2);
            foreach(var face in new[]{new[]{0,1,3,2},new[]{4,6,7,5},new[]{0,4,5,1},new[]{2,3,7,6},new[]{0,2,6,4},new[]{1,5,7,3}})
            {overlay.AddTri(P(face[0]),P(face[1]),P(face[2]),0xFF667587);overlay.AddTri(P(face[0]),P(face[2]),P(face[3]),0xFF667587);}
            for(int i=0;i<8;i++)foreach(int bit in new[]{1,2,4})if((i&bit)==0)overlay.AddLine(P(i),P(i|bit),0xFFB9C6D6,1);
        }
        overlay.AddDot(tip,0xFFFF8A1F,6);overlay.AddLabel(top+Vec3.UnitZ*(nozzle?12:2),$"{(nozzle?"Nozzle":"Cutter")} · {CamSeconds:0.0}/{timeline.Duration:0.0} s");
    }
}
