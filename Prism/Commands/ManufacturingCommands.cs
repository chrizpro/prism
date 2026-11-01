using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core;
using Prism.Core.Cam;
using Prism.Kernel;
using Prism.Meshing;

namespace Prism.Commands;

/// <summary>Explicitly requested analysis, invalidated when either geometry or parameters change.</summary>
public abstract class ManufacturingCheckCommand : AnalysisCommand
{
    protected readonly SelectionSlot Model;
    bool _run; string? _key; ResultLine[] _rows=[];
    protected readonly List<(Vec3 a,Vec3 b,Vec3 c,uint color)> Highlights=[];
    protected readonly List<(Vec3 point,string label)> Markers=[];
    protected ManufacturingCheckCommand()
    {
        Model=Slot(new(){Label="Model",Filter=SelFilter.Solid|SelFilter.SurfaceObj|SelFilter.MeshObj|SelFilter.Form});
        Add(new ActionParam{Label="Run checks",Run=()=>{_run=true;try{OnChanged();}finally{_run=false;}}});
    }
    protected override void OnBegin()
    {
        var picked=S.SelectedObjects.Where(Printable).ToArray();var visible=S.Objects.Where(o=>o.Visible&&Printable(o)).ToArray();
        if(Model.Items.Count==0){if(picked.Length==1)Model.Items.Add(SelItem.Of(picked[0]));else if(visible.Length==1)Model.Items.Add(SelItem.Of(visible[0]));}
        var run=Params.OfType<ActionParam>().Single();Params.Move(Params.IndexOf(run),Params.Count-1);PanelSettings.Move(PanelSettings.IndexOf(run),PanelSettings.Count-1);
    }
    protected SceneObject? Body => ObjectsOf(Model).FirstOrDefault();
    static bool Printable(SceneObject o)=>o.Kind is ObjKind.Solid or ObjKind.Surface or ObjKind.Mesh or ObjKind.Form;
    protected TriMesh Mesh(SceneObject body)=>S.PrintMesh(body)??throw new InvalidOperationException("Choose a solid, closed form or mesh.");
    protected abstract void RunChecks(SceneObject body,TriMesh mesh);
    protected override void Analyze()
    {
        var body=Body;
        var key=body==null?"":body.Id.ToString()+System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(body)+string.Join("|",Params.Select(p=>p switch{NumberParam n=>n.Value.ToString("R",System.Globalization.CultureInfo.InvariantCulture),ChoiceParam c=>c.Index.ToString(),_=>""}))+ExtraKey;
        if(key!=_key){_rows=[];Highlights.Clear();Markers.Clear();_key=key;}
        if(body==null){Row("Model","Pick the body or mesh to check.",2);return;}
        if(_run)
        {
            Highlights.Clear();Markers.Clear();_rows=[];
            try{Row("Model",body.Name);RunChecks(body,Mesh(body));_rows=Results.ToArray();}
            catch{Highlights.Clear();Markers.Clear();throw;}
        }
        else if(_rows.Length>0)foreach(var row in _rows)Results.Add(row);
        else Row("Checks","Choose the model and settings, then Run checks.");
    }
    protected virtual string ExtraKey=>"";
    public override void BuildOverlay(CadSession s,Overlay overlay)
    {
        foreach(var(a,b,c,color)in Highlights)overlay.AddTri(a,b,c,color);
        foreach(var(point,label)in Markers){overlay.AddDot(point,0xFFFF8A1F,7);overlay.AddLabel(point,label);}
    }
    protected void Highlight(TriMesh mesh,int t,uint color)
    {
        if(Highlights.Count>=8000)return;var(a,b,c)=mesh.Tri(t);Highlights.Add((mesh.P(a),mesh.P(b),mesh.P(c),color));
    }
    protected void Health(TriMesh mesh)
    {
        var r=MeshAnalysis.Report(mesh);
        Row("Mesh health",r.Printable?"Closed, manifold, outward oriented":"Repair open edges, normals or non-manifold geometry before export",r.Printable?1:3);
        Row("Dimensions",$"{r.Size.X:0.##} × {r.Size.Y:0.##} × {r.Size.Z:0.##} mm");
    }
}

public sealed class FdmCheckCommand : ManufacturingCheckCommand
{
    readonly NumberParam _nozzle,_walls,_angle,_x,_y,_z,_infill,_density;
    public FdmCheckCommand()
    {
        _nozzle=Num("Nozzle diameter",.4,.1,3,.05);_walls=Num("Minimum perimeter lines",2,1,10,1);
        _angle=Num("Overhang threshold",45,10,80,5,"°");
        _x=Num("Build width X",256,1,5000);_y=Num("Build depth Y",256,1,5000);_z=Num("Build height Z",256,1,5000);
        _infill=Num("Infill for weight estimate",20,0,100,5,"%");_density=Num("Filament density",1.24,.1,10,.01,"g/ml");
    }
    public override string Title=>"FDM Preparation";
    public override string Icon=>"print3d";
    public override string Hint=>"Check CAD bodies or meshes for nozzle-based walls, bed fit, contact and overhangs. Orange faces need support review; red faces have sampled thin walls. Layer strength, bridging and actual supports must be reviewed in your slicer.";
    protected override void RunChecks(SceneObject body,TriMesh mesh)
    {
        Health(mesh);var report=MeshAnalysis.Report(mesh);double minimum=_nozzle.Value*_walls.Value;
        var thickness=MeshAnalysis.Thickness(mesh,Math.Max(minimum*4,mesh.Size.Length));
        int thin=thickness.Count(v=>v<minimum);var overhang=MeshAnalysis.Overhang(mesh,_angle.Value);
        Row("Fits build volume",mesh.Size.X<=_x.Value&&mesh.Size.Y<=_y.Value&&mesh.Size.Z<=_z.Value?"Yes, after placing on the bed":"Rotate, split or resize the part",mesh.Size.X<=_x.Value&&mesh.Size.Y<=_y.Value&&mesh.Size.Z<=_z.Value?1:3);
        Row("Wall target",$"{minimum:0.##} mm ({_walls.Value:0} × {_nozzle.Value:0.##} mm nozzle)");
        Row("Thin wall samples",$"{thin:N0} / {thickness.Length:N0} vertices",thin>0?2:1);
        Row("Support-facing area",$"{overhang.SupportArea:0.##} mm² ({100*overhang.SupportArea/Math.Max(overhang.TotalArea,1e-9):0.#}%)",overhang.SupportArea>0?2:1);
        Row("Bed contact",$"{overhang.BedArea:0.##} mm²",overhang.BedArea<1?2:0);
        var best=MeshAnalysis.OrientCandidates(mesh,_angle.Value,1).First();var(axis,deg)=MeshAnalysis.Align(best.down,-Vec3.UnitZ);
        Row("Low-support orientation",$"Rotate {deg:0.#}° around ({axis.X:0.##}, {axis.Y:0.##}, {axis.Z:0.##}); support-facing area {best.support:0.##} mm²");
        if(report.Printable)
        {
            double volume=Math.Abs(report.Volume),shell=Math.Min(volume,report.Area*minimum);
            Row("Estimated filament",$"{(shell+(volume-shell)*_infill.Value/100)*_density.Value/1000:0.#} g; excludes supports, brim and purge");
        }
        for(int t=0;t<mesh.TriCount;t++){var(a,b,c)=mesh.Tri(t);if(Math.Min(thickness[a],Math.Min(thickness[b],thickness[c]))<minimum)Highlight(mesh,t,0xAAFF4545);else if(overhang.Label[t]==2)Highlight(mesh,t,0xAAFFAD33);}
        Row("Next step","Export to your slicer; inspect bridges, layer strength, supports and first-layer adhesion.");
    }
}

public sealed class ResinCheckCommand : ManufacturingCheckCommand
{
    readonly NumberParam _cell,_wall,_x,_y,_z,_density;
    public ResinCheckCommand()
    {
        _cell=Num("Sampling size",1,.1,10,.1,"mm");_wall=Num("Minimum wall / feature",1,.1,20,.1,"mm");
        _x=Num("Build width X",192,1,5000);_y=Num("Build depth Y",120,1,5000);_z=Num("Build height Z",200,1,5000);_density=Num("Resin density",1.1,.1,10,.01,"g/ml");
    }
    public override string Title=>"Resin Preparation";public override string Icon=>"print3d";
    public override string Hint=>"Screen CAD bodies and meshes for sealed resin cavities, potential cups, unsupported layer starts and thin features along +Z. Hollow and add drain/vent holes before rechecking. Sampled checks can miss features below the grid size; validate final supports and exposures in your resin slicer.";
    protected override void RunChecks(SceneObject body,TriMesh mesh)
    {
        Health(mesh);var r=ResinAnalysis.Analyze(mesh,_cell.Value);
        Row("Sampling",$"{r.Cell:0.###} mm cells; {r.Layers} sampled layers. Features below this size can be missed.",2);
        bool fits=mesh.Size.X<=_x.Value&&mesh.Size.Y<=_y.Value&&mesh.Size.Z<=_z.Value;
        Row("Fits build volume",fits?"Yes, before supports and raft":"Rotate, split or resize the part",fits?1:3);
        Row("Sealed cavities",$"{r.EnclosedVoids}; sampled trapped volume {r.TrappedVolume/1000:0.###} ml",r.EnclosedVoids>0?3:1);
        Row("Potential suction cups",$"{r.CupLayers} sampled layers; largest unvented area {r.MaxCupArea:0.##} mm²",r.CupLayers>0?2:1);
        Row("Unsupported layer starts",$"{r.IslandRegions} regions; support review required",r.IslandRegions>0?2:1);
        Row("Peak layer area",$"{r.PeakLayerArea:0.##} mm²; compare tilt orientations to reduce peel area");
        Row("Solid resin estimate",$"{Math.Abs(mesh.Volume())/1000:0.##} ml · {Math.Abs(mesh.Volume())*_density.Value/1000:0.#} g; excludes supports");
        var thickness=MeshAnalysis.Thickness(mesh,Math.Max(mesh.Size.Length,_wall.Value*4));int thin=thickness.Count(t=>t<_wall.Value);
        Row("Thin feature samples",$"{thin:N0} / {thickness.Length:N0} below {_wall.Value:0.##} mm",thin>0?2:1);
        for(int t=0;t<mesh.TriCount;t++){var(a,b,c)=mesh.Tri(t);if(Math.Min(thickness[a],Math.Min(thickness[b],thickness[c]))<_wall.Value)Highlight(mesh,t,0xAAFF4545);}
        foreach(var p in r.VoidPoints)Markers.Add((p,"Sealed cavity: add drainage"));foreach(var p in r.IslandPoints)Markers.Add((p,"Island: review supports"));
        Row("Drainage","Place drain and vent openings at cavity low points in the print orientation. Recheck after hollowing or rotating.",2);
        Row("Next step","Use your resin slicer to inspect support contacts, fine islands, cups, exposure and raft clearance.");
    }
}

public sealed class CncStockCommand : ManufacturingCheckCommand
{
    readonly NumberParam _cell,_flute,_holder,_tolerance,_position;
    public CncStockCommand()
    {
        _cell=Num("Grid size",1,.1,10,.1);_flute=Num("Flute / holder clearance length",25,.1,500,1);
        _holder=Num("Holder diameter",20,.1,200,1);_tolerance=Num("Comparison tolerance",.1,0,5,.05);
        _position=Num("Program position",100,0,100,5,"%");
    }
    public override string Title=>"CNC Stock Review";public override string Icon=>"cnc";
    public override string Hint=>"Sample stock removal in visible milling operation order. Compare the result with the selected setup model: orange is remaining material, red is a possible gouge, blue is cut stock. Grid sampling excludes undercuts, fixtures, machine kinematics and exact cutter geometry. Tool dimensions apply to all operations.";
    protected override string ExtraKey=>System.Text.Json.JsonSerializer.Serialize(S.CamSetup)+string.Join("|",S.Objects.Where(o=>o.Visible&&o.Asm is ToolpathInfo).Select(o=>o.Id.ToString()+System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o.Asm!)));
    protected override void RunChecks(SceneObject body,TriMesh mesh)
    {
        var setup=S.CamSetup??throw new InvalidOperationException("Create a CAM setup first.");setup.Validate(body);
        var paths=S.ActiveMillingProgram().Select(o=>(ToolpathInfo)o.Asm!).ToArray();
        foreach(var path in paths)CamValidation.Validate(S,path);
        var r=StockSimulation.Run(setup,mesh,paths,_cell.Value,_flute.Value,_holder.Value,_tolerance.Value,_position.Value/100);
        Row("Sampling",$"{r.Cell:0.###} mm grid; {_position.Value:0.#}% of feed-based program time",2);
        Row("Removed stock",$"{r.RemovedVolume/1000:0.###} cm³");Row("Remaining material",$"{r.RemainingVolume/1000:0.###} cm³",r.RemainingVolume>0?2:1);
        Row("Possible gouges",$"{r.GougeMoves} moves; {r.GougeVolume/1000:0.###} cm³",r.GougeMoves>0?3:1);
        Row("Rapid contacts",$"{r.RapidContacts} sampled moves",r.RapidContacts>0?3:1);Row("Holder contacts",$"{r.HolderContacts} sampled moves",r.HolderContacts>0?3:1);
        int stride=Math.Max(1,(int)Math.Ceiling(Math.Sqrt(r.Heights.Length/3500.0)));
        for(int y=0;y<r.Ny;y+=stride)for(int x=0;x<r.Nx;x+=stride)
        {
            int i=y*r.Nx+x;double h=r.Heights[i],delta=h-r.Target[i];uint color=delta < -_tolerance.Value?0xCCFF4545:delta>_tolerance.Value?0xAAFFAD33:0x9935B6FF;
            var a=r.Origin+new Vec3(x*r.Cell,y*r.Cell,h-r.Origin.Z);var b=a+new Vec3(Math.Min(stride,r.Nx-x)*r.Cell,0,0);var c=a+new Vec3(0,Math.Min(stride,r.Ny-y)*r.Cell,0);var d=b+(c-a);
            Highlights.Add((a,b,c,color));Highlights.Add((b,d,c,color));
        }
        Row("Scope","Sampled 3-axis heightfield screening; inspect fixtures and full machine clearance separately.",2);
    }
}

public sealed class CncDesignCheckCommand : ManufacturingCheckCommand
{
    readonly NumberParam _diameter,_reach,_wall;
    public CncDesignCheckCommand(){_diameter=Num("Smallest cutter diameter",6,.1,100,.5);_reach=Num("Usable cutter reach",25,.1,500,1);_wall=Num("Minimum wall",1,.1,50,.1);}
    public override string Title=>"CNC Design Check";public override string Icon=>"cnc";
    public override string Hint=>"Screen +Z tool access, reach and thin walls on the model. Red faces are hidden from a top-down approach; orange faces exceed the selected reach. This does not certify tool access to corners or holes.";
    protected override void RunChecks(SceneObject body,TriMesh mesh)
    {
        Health(mesh);double hiddenArea=0,deepArea=0,eps=Math.Max(mesh.Size.Length*1e-6,1e-5);
        for(int t=0;t<mesh.TriCount;t++)
        {
            var(a,b,c)=mesh.Tri(t);var center=(mesh.P(a)+mesh.P(b)+mesh.P(c))/3;
            bool hidden=mesh.Bvh.Raycast(center+Vec3.UnitZ*eps,Vec3.UnitZ,mesh.Size.Z+1)!=null;
            bool deep=mesh.Max.Z-center.Z>_reach.Value;
            if(hidden&&mesh.FaceNormal(t).Z<.1){hiddenArea+=mesh.TriArea(t);Highlight(mesh,t,0xAAFF4545);}
            else if(deep){deepArea+=mesh.TriArea(t);Highlight(mesh,t,0xAAFFAD33);}
        }
        Row("Top-down inaccessible area",$"{hiddenArea:0.##} mm²; includes underside faces, consider flips or another setup",hiddenArea>0?2:1);
        Row("Beyond selected reach",$"{deepArea:0.##} mm² below the top {_reach.Value:0.##} mm",deepArea>0?2:1);
        var thickness=MeshAnalysis.Thickness(mesh,Math.Max(mesh.Size.Length,_wall.Value*4));int thin=thickness.Count(v=>v<_wall.Value);
        Row("Thin wall samples",$"{thin:N0} / {thickness.Length:N0} below {_wall.Value:0.##} mm",thin>0?2:1);
        Row("Inside corner target",$"Use an inside radius of at least {_diameter.Value/2:0.##} mm for this cutter; allow extra radius for smoother engagement.");
        Row("Manual review","Check narrow pockets, hole diameter/depth, clamping, corner radii and alternate setup directions.",2);
    }
}
