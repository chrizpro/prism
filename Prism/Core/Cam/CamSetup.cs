using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Kernel;
namespace Prism.Core.Cam;

public sealed record CamSetup(string Name, Guid BodyId, string ShapeSignature, double[] StockMin, double[] StockMax,
    double[] WorkZero, double ToolDiameter=6, double Feed=800, double Plunge=300, double Rpm=12000, double Clearance=5,
    int ZeroXY=-1, int ZeroZ=0, double[]? PickedXY=null, int ZeroTurns=0)
{
    /// <summary>Work zero X/Y choices. Entries 0 and 1 keep the meaning they had before the grid existed (saved recipes store the index).</summary>
    public static readonly string[] ZeroXYNames = ["Stock lower-left corner", "Model origin", "Stock lower-middle", "Stock lower-right", "Stock left-middle", "Stock center", "Stock right-middle", "Stock upper-left", "Stock upper-middle", "Stock upper-right", "Picked point"];
    /// <summary>Which model direction the work X axis points along, in quarter turns about Z from model +X (work Y is then +Z cross X).</summary>
    public static readonly string[] TurnNames = ["Along model +X", "Along model +Y", "Along model −X", "Along model −Y"];
    public static readonly string[] ZeroZNames = ["Top of stock", "Bottom of stock", "Model origin"];
    public const int ModelOriginXY = 1, PickedPointXY = 10, ModelOriginZ = 2;
    // column and row (0 low, 1 middle, 2 high) of each stock point; null where the choice is not a stock point
    static readonly (int col, int row)?[] Grid = [(0, 0), null, (1, 0), (2, 0), (0, 1), (1, 1), (2, 1), (0, 2), (1, 2), (2, 2), null];

    /// <summary>The nine stock points as (option index, column, row), column and row 0 low, 1 middle, 2 high.</summary>
    public static IEnumerable<(int option,int col,int row)> StockPoints(){for(int i=0;i<Grid.Length;i++)if(Grid[i] is{} g)yield return(i,g.col,g.row);}

    /// <summary>The work zero in model coordinates for a choice of stock point, picked point and height.</summary>
    public static double[] ComputeZero(double[] lo, double[] hi, int xy, int z, double[]? picked)
    {
        if(xy<0||xy>=ZeroXYNames.Length||z<0||z>=ZeroZNames.Length)throw new InvalidOperationException("Choose a work zero from the list.");
        double x,y;
        if(xy==PickedPointXY)
        {
            if(picked is not{Length:2}||picked.Any(v=>!double.IsFinite(v)))throw new InvalidOperationException("Enter finite X and Y values for the picked work zero.");
            (x,y)=(picked[0],picked[1]);
        }
        else if(Grid[xy] is{} g){x=lo[0]+(hi[0]-lo[0])*g.col/2;y=lo[1]+(hi[1]-lo[1])*g.row/2;}
        else (x,y)=(0,0);
        return [x,y,z==0?hi[2]:z==1?lo[2]:0];
    }

    // The signed fields are the ones a setup had before the work zero choices existed.
    internal sealed record SignedFields(string Name, Guid BodyId, string ShapeSignature, double[] StockMin, double[] StockMax, double[] WorkZero, double ToolDiameter, double Feed, double Plunge, double Rpm, double Clearance);
    static string Hash(SignedFields f)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(f))));
    /// <summary>Identifies the setup that paths were made with. The work zero is left out: moving it only changes the posted numbers, so paths stay valid.</summary>
    public string Signature()=>Hash(new(Name,BodyId,ShapeSignature,StockMin,StockMax,[],ToolDiameter,Feed,Plunge,Rpm,Clearance));
    /// <summary>The signature paths made before work zero was left out carry.</summary>
    public string LegacySignature()=>Hash(new(Name,BodyId,ShapeSignature,StockMin,StockMax,WorkZero,ToolDiameter,Feed,Plunge,Rpm,Clearance));
    public bool Matches(string? signature)=>signature!=null&&(signature==Signature()||signature==LegacySignature());
    public void Validate(SceneObject body)
    {
        if(body.Id!=BodyId)throw new InvalidOperationException("The CAM setup belongs to a different body.");
        Simulation.StudySetup.Validate(body,ShapeSignature,[]);
        if(StockMin.Length!=3||StockMax.Length!=3||WorkZero.Length!=3||StockMin.Concat(StockMax).Concat(WorkZero).Any(v=>!double.IsFinite(v))||Enumerable.Range(0,3).Any(i=>StockMax[i]<=StockMin[i]))throw new InvalidOperationException("Invalid stock bounds or work zero.");
        if(ZeroXY<-1||ZeroXY>=ZeroXYNames.Length||ZeroZ<0||ZeroZ>=ZeroZNames.Length||ZeroTurns<0||ZeroTurns>=TurnNames.Length||PickedXY!=null&&(PickedXY.Length!=2||PickedXY.Any(v=>!double.IsFinite(v))))throw new InvalidOperationException("Invalid work zero choice.");
        if(new[]{ToolDiameter,Feed,Plunge,Rpm,Clearance}.Any(v=>!double.IsFinite(v)||v<=0))throw new InvalidOperationException("Tool, feeds, spindle speed and clearance must be positive.");
        var m=body.Mesh;double[] lo=[m.Min.X,m.Min.Y,m.Min.Z],hi=[m.Max.X,m.Max.Y,m.Max.Z];
        if(Enumerable.Range(0,3).Any(i=>StockMin[i]>lo[i]+.001||StockMax[i]<hi[i]-.001))throw new InvalidOperationException("Stock must enclose the selected model.");
    }
    /// <summary>Model-space offset from the work zero, written in the work axes.</summary>
    public static (double x,double y) ToWork(double dx,double dy,int turns){var(c,s)=Quarter(turns);return(dx*c+dy*s,-dx*s+dy*c);}
    public static (double c,double s) Quarter(int turns)=>((turns&3)switch{0=>(1.0,0.0),1=>(0.0,1.0),2=>(-1.0,0.0),_=>(0.0,-1.0)});
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
