using System;
using Prism.Core;
using Prism.Core.Cam;
using Prism.Kernel;

namespace Prism.Commands;

/// <summary>Layered raster roughing of a top-accessible model, leaving a finishing allowance.</summary>
public sealed class Rough3DCommand : CncOperationCommand
{
    readonly SelectionSlot _model;readonly NumberParam _stepover,_allowance;
    public Rough3DCommand()
    {
        _model=Slot(new(){Label="Model to rough",Filter=SelFilter.Solid});
        _stepover=Num("Roughing stepover",40,5,80,5,"% of tool");_allowance=Num("Stock to leave",.5,.05,10,.1);
    }
    public override string Title=>"CNC 3D Rough";
    public override string Hint=>"Clear rectangular setup stock around the model with layered raster passes and a flat end mill. Leaves a sampled finishing allowance on top-accessible surfaces. Entries plunge vertically: use a center-cutting tool and suitable depth per pass. Review stock removal before posting; undercuts are not machined.";
    protected override void OnBegin(){base.OnBegin();if(S.CamSetup is{} setup&&S.Find(setup.BodyId) is{} body){if(_model.Items.Count==0)_model.Items.Add(SelItem.Of(body));Depth.Value=setup.StockMax[2]-body.Mesh.Min.Z;}}
    protected override CommandOutput? Build()
    {
        Results.Clear();if(!Complete)return null;
        var setup=S.CamSetup??throw new InvalidOperationException("Create rectangular stock in CAM setup before 3D roughing.");
        var body=S.Find(_model.Items[0].Obj)??throw new InvalidOperationException("Choose the model to rough.");setup.Validate(body);
        if(TopZ.Value<setup.StockMax[2]||TopZ.Value-Depth.Value<setup.StockMin[2]-1e-6)throw new InvalidOperationException("Roughing top must be at or above stock top, and the final depth must stay within stock bounds.");
        var mesh=body.Mesh;var map=HeightMap.FromMesh(mesh,Math.Max(.05,Dia.Value/12),Radius+_allowance.Value+1);map.Dilate(Radius+_allowance.Value,false);
        double x0=setup.StockMin[0]-Radius,x1=setup.StockMax[0]+Radius,y0=setup.StockMin[1]-Radius,y1=setup.StockMax[1]+Radius;
        double sample=Math.Max(map.Cell,.05),step=Dia.Value*_stepover.Value/100;
        int columns=(int)Math.Ceiling((x1-x0)/sample)+1,rows=(int)Math.Ceiling((y1-y0)/step)+1;
        long levels=(long)Math.Ceiling(Depth.Value/StepDown.Value);
        if((long)columns*rows*levels>2_000_000)throw new InvalidOperationException("Roughing exceeds two million samples. Use a larger tool, stepover or depth per pass.");
        Start();int runs=0;
        foreach(double z in Levels())for(int row=0;row<rows;row++)
        {
            double y=Math.Min(y1,y0+row*step);bool inRun=false;double endX=0;bool reverse=row%2==1;
            void Finish(){if(!inRun)return;Go(endX,y,z,MoveKind.Cut);Go(endX,y,SafeZ,MoveKind.Rapid);inRun=false;runs++;}
            for(int k=0;k<columns;k++)
            {
                double x=reverse?Math.Max(x0,x1-k*sample):Math.Min(x1,x0+k*sample);double top=double.NaN;
                // Take the highest nearby cell to leave a conservative band at sampled model boundaries.
                for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++){double h=map.Tip(x+dx*map.Cell,y+dy*map.Cell);if(double.IsFinite(h))top=double.IsNaN(top)?h:Math.Max(top,h);}
                bool free=double.IsNaN(top)||z>=top+_allowance.Value;
                if(!free){Finish();continue;}
                if(!inRun){Pass();Go(x,y,SafeZ,MoveKind.Rapid);Go(x,y,z,MoveKind.Plunge);inRun=true;}
                endX=x;
            }
            Finish();
        }
        if(runs==0)throw new InvalidOperationException("No stock can be reached at this depth and allowance.");
        Results.Add(new("Roughing runs",runs.ToString()));Results.Add(new("Stock to leave",$"{_allowance.Value:0.###} mm"));
        return Output("CNC 3D rough",Seal("3D roughing"));
    }
}
