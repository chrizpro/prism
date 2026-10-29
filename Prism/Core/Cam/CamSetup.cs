using System;
using System.Linq;
using Prism.Kernel;
namespace Prism.Core.Cam;

public sealed record CamSetup(string Name, Guid BodyId, string ShapeSignature, double[] StockMin, double[] StockMax,
    double[] WorkZero, double ToolDiameter=6, double Feed=800, double Plunge=300, double Rpm=12000, double Clearance=5)
{
    public string Signature()=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(this))));
    public void Validate(SceneObject body)
    {
        if(body.Id!=BodyId)throw new InvalidOperationException("The CAM setup belongs to a different body.");
        Simulation.StudySetup.Validate(body,ShapeSignature,[]);
        if(StockMin.Length!=3||StockMax.Length!=3||WorkZero.Length!=3||StockMin.Concat(StockMax).Concat(WorkZero).Any(v=>!double.IsFinite(v))||Enumerable.Range(0,3).Any(i=>StockMax[i]<=StockMin[i]))throw new InvalidOperationException("Invalid stock bounds or work zero.");
        if(new[]{ToolDiameter,Feed,Plunge,Rpm,Clearance}.Any(v=>!double.IsFinite(v)||v<=0))throw new InvalidOperationException("Tool, feeds, spindle speed and clearance must be positive.");
        var m=body.Mesh;double[] lo=[m.Min.X,m.Min.Y,m.Min.Z],hi=[m.Max.X,m.Max.Y,m.Max.Z];
        if(Enumerable.Range(0,3).Any(i=>StockMin[i]>lo[i]+.001||StockMax[i]<hi[i]-.001))throw new InvalidOperationException("Stock must enclose the selected model.");
    }
    public Vec3 Origin()=>new(WorkZero[0],WorkZero[1],WorkZero[2]);
}

public sealed class PathTimeline
{
    public ToolpathInfo Path {get;}public double[] Seconds {get;}
    public double Duration=>Seconds[^1];
    public PathTimeline(ToolpathInfo path)
    {
        if(path.MoveCount<2||path.Moves.Length%4!=0||path.Moves.Any(x=>!double.IsFinite(x)))throw new InvalidOperationException("A finite path with at least two moves is required.");
        if(path.Feeds!=null&&(path.Feeds.Length!=path.MoveCount||path.Feeds.Any(f=>!double.IsFinite(f)||f<=0)))throw new InvalidOperationException("Every move needs a positive finite feed.");
        if(path.LayerStarts.Any(i=>i<0||i>=path.MoveCount)||path.LayerStarts.Zip(path.LayerStarts.Skip(1)).Any(p=>p.First>=p.Second))throw new InvalidOperationException("Invalid layer/pass offsets.");
        for(int i=0;i<path.MoveCount;i++)if(path.Moves[i*4+3]<0||path.Moves[i*4+3]>7||path.Moves[i*4+3]!=(int)path.Moves[i*4+3])throw new InvalidOperationException("Invalid toolpath move type.");
        Path=path;Seconds=new double[path.MoveCount];
        for(int i=1;i<path.MoveCount;i++){var a=Point(i-1);var b=Point(i);Seconds[i]=Seconds[i-1]+a.DistanceTo(b)/path.FeedAt(i)*60;}
    }
    public Vec3 Point(int i){var(x,y,z,_)=Path.Move(i);return new(x,y,z);}
    public (int segment,double fraction,Vec3 tip) At(double seconds)
    {
        if(!double.IsFinite(seconds))throw new ArgumentException("Time must be finite.");
        seconds=Math.Clamp(seconds,0,Duration);int found=Array.BinarySearch(Seconds,seconds);int end=found>=0?found:~found;end=Math.Clamp(end,1,Seconds.Length-1);
        double fraction=(seconds-Seconds[end-1])/Math.Max(Seconds[end]-Seconds[end-1],1e-15);fraction=Math.Clamp(fraction,0,1);
        return(end,fraction,Point(end-1)+(Point(end)-Point(end-1))*fraction);
    }
}
