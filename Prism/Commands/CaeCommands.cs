using System;
using System.Linq;
using Prism.Core;
using Prism.Simulation;

namespace Prism.Commands;

public sealed class CaeSolveCommand : AnalysisCommand
{
    readonly bool _thermal;
    readonly SelectionSlot _body,_first,_second;
    readonly ChoiceParam _type;
    readonly NumberParam _hot,_cold,_initial,_duration,_size;
    readonly IntParam _modes,_showMode;
    readonly TextParam _name;
    string? _setupError;
    public CaeSolveCommand(bool thermal)
    {
        _thermal=thermal;
        _body=Slot(new SelectionSlot {Label="Solid body",Filter=SelFilter.Solid});
        _first=Slot(new SelectionSlot {Label=thermal?"First temperature faces":"Fixed support faces",Filter=SelFilter.Face,Multi=true});
        _second=Slot(new SelectionSlot {Label="Second temperature faces",Filter=SelFilter.Face,Multi=true,IsVisible=thermal,Optional=!thermal});
        _type=Choice("Thermal analysis",0,"Steady conduction","Transient conduction");
        _hot=Num("First temperature",100,-273.14,100000,5,"°C"); _cold=Num("Second temperature",20,-273.14,100000,5,"°C");
        _initial=Num("Initial temperature",20,-273.14,100000,5,"°C"); _duration=Num("Duration",60,.001,1e9,1,"s");
        _size=Num("Element size (0 = automatic)",0,0,10000,1);
        _modes=Add(new IntParam {Label="Number of modes",Min=1,Max=20,Value=6});
        _showMode=Add(new IntParam {Label="Mode to display",Min=1,Max=20,Value=1});
        _name=Text("Setup name",thermal?"Thermal setup":"Modal setup");
        Add(new ActionParam {Label="Save setup",Run=SaveSetup});
        Add(new ActionParam {Label="Run analysis",Run=Start});
        Add(new ActionParam {Label="Stop analysis",Run=()=>S.StopCae()});
        Add(new ActionParam {Label="Show mode",Run=()=>
        {
            if(S.Cae?.Outcome is not {Kind:CaeKind.Modal} result) {S.Say("Run natural-frequency analysis first.",true);return;}
            if(_showMode.Value>result.Modes.Count){S.Say("Choose one of the returned modes.",true);return;}
            if(S.Find(result.Prepared.Body.Id)!=result.Prepared.Body){S.Say("The source body changed; run the analysis again.",true);return;}
            S.ShowResult(ResultView.Field(result,_showMode.Value-1));
        }});
    }
    public override string Title=>_thermal?"Thermal Analysis":"Natural Frequencies";
    public override string Icon=>_thermal?"cae-thermal":"cae-modal";
    public override string Hint=>_thermal?"Prescribed-temperature conduction in one homogeneous solid. Pick separated boundary faces; other faces are insulated. Assign a material, then Run analysis. Transient mode uses a uniform initial temperature.":
        "Linear free-vibration frequencies and mode shapes for one homogeneous solid with fixed faces. Assign a material. Displayed mode amplitude is arbitrary; this is not a forced or impact response.";
    protected override void OnBegin(){S.CaeChanged+=Refresh;}
    public override void OnEnd(){S.CaeChanged-=Refresh;}
    void Refresh(){if(ReferenceEquals(S.ActiveCommand,this))OnChanged();}
    protected override void UpdateVisibility()
    {
        _type.IsVisible=_hot.IsVisible=_cold.IsVisible=_thermal;
        _initial.IsVisible=_duration.IsVisible=_thermal&&_type.Index==1;
        _modes.IsVisible=_showMode.IsVisible=!_thermal;
        foreach(var p in Params.OfType<ActionParam>()) if(p.Label=="Show mode")p.IsVisible=!_thermal;
    }
    void Start()
    {
        try
        {
            _setupError=null;
            var (body,settings)=Settings();
            S.StartCae(body,settings);
        }
        catch(Exception e){_setupError=e.Message;S.Say(e.Message,true);Refresh();}
    }
    (SceneObject,CaeSettings) Settings()
    {
            if(_body.Items.Count==0||_first.Items.Count==0||(_thermal&&_second.Items.Count==0))throw new InvalidOperationException("Choose a body and its boundary faces first.");
            var body=S.Find(_body.Items[0].Obj)!;
            if(body.Material==null)throw new InvalidOperationException("Assign a material before running CAE.");
            int[] Faces(SelectionSlot slot)
            {
                if(slot.Items.Any(i=>i.Obj!=body.Id))throw new InvalidOperationException("All boundary faces must belong to the selected body.");
                return slot.Items.Select(i=>i.Index).Distinct().ToArray();
            }
            var settings=new CaeSettings(_thermal?(_type.Index==0?CaeKind.SteadyThermal:CaeKind.TransientThermal):CaeKind.Modal,
                Faces(_first),_thermal?Faces(_second):[],_hot.Value,_cold.Value,_initial.Value,_duration.Value,_modes.Value,_size.Value);
            StudySetup.Validate(body,null,settings.FirstFaces.Concat(settings.SecondFaces).ToArray());
            if(settings.Kind!=CaeKind.Modal&&settings.FirstFaces.Intersect(settings.SecondFaces).Any())throw new InvalidOperationException("Temperature boundaries must not overlap.");
            return (body,settings);
    }
    void SaveSetup()
    {
        try{_setupError=null;var(body,settings)=Settings();if(string.IsNullOrWhiteSpace(_name.Value))throw new InvalidOperationException("Enter a setup name.");var setup=new SavedCaeCase(_name.Value.Trim(),body.Id,settings,StudySetup.Signature(body));S.CaeCases.RemoveAll(c=>c.Name==setup.Name);S.CaeCases.Add(setup);S.Dirty=true;S.Say("Saved CAE setup: "+setup.Name);Refresh();}
        catch(Exception e){_setupError=e.Message;S.Say(e.Message,true);Refresh();}
    }
    protected override void Analyze()
    {
        if(_setupError!=null){Row("Setup error",_setupError,3);return;}
        if(S.Cae is not { } job){Row("Setup","Assign material, pick body and faces, then Run analysis.");return;}
        Row("Status",job.Status,job.Error==null?0:3);
        if(job.Outcome is not { } r)return;
        Row("Source body",r.Prepared.Body.Name);Row("Mesh",$"{r.Prepared.Mesh.NodeCount:N0} nodes, {r.Prepared.Mesh.TetCount:N0} quadratic tetrahedra");
        if(r.Temperatures.Length>0){Row("Minimum temperature",$"{r.Temperatures.Min():G6} °C");Row("Maximum temperature",$"{r.Temperatures.Max():G6} °C");}
        for(int i=0;i<r.Frequencies.Length;i++)Row($"Mode {i+1}",$"{r.Frequencies[i]:G6} Hz");
        Row("Result files",r.Folder);
    }
}

public sealed record PipeFlowResult(double Velocity,double Reynolds,double Friction,double PressureDrop,double Head,double HydraulicPower,string Regime);
public static class PipeFlow
{
    /// <summary>SI calculation for fully developed, steady incompressible flow in a circular full pipe.</summary>
    public static PipeFlowResult Calculate(double diameter,double length,double flow,double density,double viscosity,double roughness=0,double minorLoss=0)
    {
        if(new[]{diameter,length,flow,density,viscosity,roughness,minorLoss}.Any(x=>!double.IsFinite(x))||diameter<=0||length<=0||flow<=0||density<=0||viscosity<=0||roughness<0||roughness/diameter>.05||minorLoss<0)
            throw new ArgumentException("Use positive bore, length, flow, density and viscosity; roughness must be 0–5% of bore and minor-loss coefficient nonnegative.");
        double velocity=flow/(Math.PI*diameter*diameter/4),re=density*velocity*diameter/viscosity;
        if(re>=2000&&re<4000)throw new ArgumentException($"Re = {re:0}: transitional flow. A reliable pressure drop is not reported in the 2000–4000 transition range.");
        double f=64/re;
        if(re>=4000)
        {
            double lo=1e-6,hi=1;
            for(int i=0;i<80;i++){double mid=(lo+hi)/2;double residual=1/Math.Sqrt(mid)+2*Math.Log10(roughness/(3.7*diameter)+2.51/(re*Math.Sqrt(mid)));if(residual>0)lo=mid;else hi=mid;}
            f=(lo+hi)/2;
        }
        double dp=(f*length/diameter+minorLoss)*density*velocity*velocity/2;
        return new(velocity,re,f,dp,dp/(density*9.80665),dp*flow,re<2000?"Laminar":"Turbulent (Colebrook–White)");
    }
}
public sealed class PipeFlowCommand : AnalysisCommand
{
    readonly NumberParam _bore,_length,_flow,_density,_viscosity,_roughness,_minor;
    public PipeFlowCommand()
    {
        _bore=Num("Internal diameter",10,.01,100000,1);_length=Num("Pipe length",1000,.01,1e9,100);
        _flow=Num("Volume flow",.5,.000001,1e9,.1,"L/min");_density=Num("Fluid density",998.2,.001,100000,10,"kg/m³");
        _viscosity=Num("Dynamic viscosity",.001002,1e-9,10000,.0001,"Pa·s");
        _roughness=Num("Absolute roughness",.0015,0,1000,.001);_minor=Num("Total minor-loss coefficient",0,0,100000,.1,"");
    }
    public override string Title=>"Pipe Flow";
    public override string Icon=>"cae-fluid";
    public override string Hint=>"Steady, incompressible, fully developed flow in a circular full pipe. Darcy–Weisbach pressure drop; laminar 64/Re or turbulent Colebrook friction. No 3D CFD, compressibility, entrance-flow or thermal coupling.";
    protected override void Analyze()
    {
        var r=PipeFlow.Calculate(_bore.Value/1000,_length.Value/1000,_flow.Value/60000,_density.Value,_viscosity.Value,_roughness.Value/1000,_minor.Value);
        Row("Regime",r.Regime);Row("Reynolds number",$"{r.Reynolds:G6}");Row("Mean velocity",$"{r.Velocity:G6} m/s");
        Row("Darcy friction factor",$"{r.Friction:G6}");Row("Pressure drop",$"{r.PressureDrop:G6} Pa");Row("Head loss",$"{r.Head:G6} m");Row("Hydraulic power",$"{r.HydraulicPower:G6} W");
    }
}
