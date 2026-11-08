using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Kernel;
using Prism.Meshing;

namespace Prism.Core.Cam;

public sealed record StockResult(double Cell, int Nx, int Ny, Vec3 Origin, double[] Heights, double[] Target,
    double RemovedVolume, double RemainingVolume, double GougeVolume, int RapidContacts, int HolderContacts, int GougeMoves);

/// <summary>Sampled swept flat/ball cutter against a 2.5D stock grid. Undercuts and machine kinematics are excluded.</summary>
public static class StockSimulation
{
    public static StockResult Run(CamSetup setup, TriMesh target, IReadOnlyList<ToolpathInfo> paths,
        double requestedCell, double fluteLength, double holderDiameter, double tolerance, double fraction=1)
    {
        if(new[]{requestedCell,fluteLength,holderDiameter}.Any(v=>!double.IsFinite(v)||v<=0)||!double.IsFinite(tolerance)||tolerance<0||!double.IsFinite(fraction)||fraction<0||fraction>1)
            throw new InvalidOperationException("Use positive finite sampling and tool dimensions, a nonnegative tolerance and a position from 0 to 1.");
        if(paths.Count==0)throw new InvalidOperationException("Create milling toolpaths first.");
        if(paths.Any(p=>p.IsPrint||p.Operation.StartsWith("Turning",StringComparison.Ordinal)))throw new InvalidOperationException("Stock simulation supports milling paths only. Hide turning and printing paths.");
        var lo=new Vec3(setup.StockMin[0],setup.StockMin[1],setup.StockMin[2]);var hi=new Vec3(setup.StockMax[0],setup.StockMax[1],setup.StockMax[2]);
        double cell=Math.Max(requestedCell,Math.Max(hi.X-lo.X,hi.Y-lo.Y)/180);
        if(paths.Any(p=>cell>p.ToolDia/2))throw new InvalidOperationException("Grid size is larger than a cutter radius. Use a finer grid, smaller stock bounds or a larger tool.");
        int nx=(int)Math.Ceiling((hi.X-lo.X)/cell),ny=(int)Math.Ceiling((hi.Y-lo.Y)/cell);
        var stock=Enumerable.Repeat(hi.Z,nx*ny).ToArray();var model=new double[stock.Length];
        for(int y=0;y<ny;y++)for(int x=0;x<nx;x++)
        {
            var hit=target.Bvh.Raycast(new(lo.X+(x+.5)*cell,lo.Y+(y+.5)*cell,hi.Z+cell),-Vec3.UnitZ,hi.Z-lo.Z+2*cell);
            model[y*nx+x]=hit is{} h?hi.Z+cell-h.t:lo.Z;
        }
        var timelines=paths.Select(p=>new PathTimeline(p)).ToArray();double end=timelines.Sum(t=>t.Duration)*fraction,elapsed=0;
        long visits=0;int rapid=0,holder=0,gouge=0;
        foreach(var timeline in timelines)
        {
            var path=timeline.Path;double radius=path.ToolDia/2;bool ball=path.Tool.Contains("ball",StringComparison.OrdinalIgnoreCase);
            if(holderDiameter<path.ToolDia)throw new InvalidOperationException("Holder diameter must be at least every cutter diameter.");
            double reach=Math.Max(radius,holderDiameter/2);
            for(int move=1;move<path.MoveCount;move++)
            {
                double segmentStart=elapsed+timeline.Seconds[move-1];if(segmentStart>=end)break;
                var a=timeline.Point(move-1);var b=timeline.Point(move);
                double dt=timeline.Seconds[move]-timeline.Seconds[move-1];double f=dt>0?Math.Clamp((end-segmentStart)/dt,0,1):1;b=a+(b-a)*f;
                double samples=Math.Ceiling(a.DistanceTo(b)/(cell*.5));
                if(samples>2_000_000)throw new InvalidOperationException("Simulation path is too long. Use a coarser sampling size.");
                int steps=Math.Max(1,(int)samples);bool rapidHit=false,holderHit=false,gougeHit=false;
                bool cutting=path.Move(move).kind!=MoveKind.Rapid;
                for(int step=0;step<=steps;step++)
                {
                    var tip=a+(b-a)*(step/(double)steps);
                    int x0=Math.Max(0,(int)Math.Floor((tip.X-reach-lo.X)/cell)),x1=Math.Min(nx-1,(int)Math.Floor((tip.X+reach-lo.X)/cell));
                    int y0=Math.Max(0,(int)Math.Floor((tip.Y-reach-lo.Y)/cell)),y1=Math.Min(ny-1,(int)Math.Floor((tip.Y+reach-lo.Y)/cell));
                    if((visits+=(long)Math.Max(0,x1-x0+1)*Math.Max(0,y1-y0+1))>20_000_000)throw new InvalidOperationException("Simulation exceeds the sampling budget. Increase grid size or shorten the operation list.");
                    for(int y=y0;y<=y1;y++)for(int x=x0;x<=x1;x++)
                    {
                        double dx=lo.X+(x+.5)*cell-tip.X,dy=lo.Y+(y+.5)*cell-tip.Y,d2=dx*dx+dy*dy;int i=y*nx+x;
                        if(d2<=holderDiameter*holderDiameter/4 && stock[i]>tip.Z+fluteLength+tolerance)holderHit=true;
                        if(d2>radius*radius)continue;
                        double bottom=tip.Z+(ball?radius-Math.Sqrt(Math.Max(0,radius*radius-d2)):0);
                        if(!cutting){if(stock[i]>lo.Z+tolerance&&stock[i]>bottom+tolerance)rapidHit=true;continue;} // a cell at the stock floor holds no material, so leaving a hole that went into the spoilboard is not a collision
                        if(model[i]>lo.Z+tolerance&&bottom<model[i]-tolerance)gougeHit=true;
                        stock[i]=Math.Max(lo.Z,Math.Min(stock[i],bottom));
                    }
                }
                if(rapidHit)rapid++;if(holderHit)holder++;if(gougeHit)gouge++;
            }
            elapsed+=timeline.Duration;if(elapsed>=end)break;
        }
        double removed=0,remaining=0,gouged=0;
        for(int i=0;i<stock.Length;i++){removed+=hi.Z-stock[i];remaining+=Math.Max(0,stock[i]-model[i]-tolerance);gouged+=Math.Max(0,model[i]-stock[i]-tolerance);}
        return new(cell,nx,ny,lo,stock,model,removed*cell*cell,remaining*cell*cell,gouged*cell*cell,rapid,holder,gouge);
    }
}
