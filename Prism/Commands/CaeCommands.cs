using System;
using System.Linq;
using Prism.Core;
using Prism.Kernel;
using Prism.Simulation;

namespace Prism.Commands;

public sealed class CaeSolveCommand : AnalysisCommand
{
    readonly bool _thermal;
    public bool IsThermal => _thermal;
    readonly SelectionSlot _body,_first,_second,_heated,_cooled;
    readonly ChoiceParam _type,_coolant;
    readonly NumberParam _hot,_cold,_initial,_duration,_size,_power,_film,_ambient,_emissivity;
    readonly IntParam _modes,_showMode;
    readonly TextParam _name;
    string? _setupError;
    public CaeSolveCommand(bool thermal)
    {
        _thermal=thermal;
        _body=Slot(new SelectionSlot {Label="Solid bodies (touching bodies are bonded)",Filter=SelFilter.Solid,Multi=true});
        _first=Slot(new SelectionSlot {Label=thermal?"First temperature faces (optional)":"Fixed support faces",Filter=SelFilter.Face,Multi=true,Optional=thermal});
        _second=Slot(new SelectionSlot {Label="Second temperature faces (optional)",Filter=SelFilter.Face,Multi=true,IsVisible=thermal,Optional=true});
        _heated=Slot(new SelectionSlot {Label="Heated faces (optional)",Filter=SelFilter.Face,Multi=true,IsVisible=thermal,Optional=true});
        _cooled=Slot(new SelectionSlot {Label="Cooled faces (optional)",Filter=SelFilter.Face,Multi=true,IsVisible=thermal,Optional=true});
        _type=Choice("Thermal analysis",0,"Steady conduction","Transient conduction");
        _hot=Num("First temperature",100,-273.14,100000,5,"°C"); _cold=Num("Second temperature",20,-273.14,100000,5,"°C");
        _power=Num("Heat power",10,0,1e9,1,"W");
        _coolant=Choice("Cooling",0,"Still air (about 10 W/m²·K)","Air from a fan (about 50 W/m²·K)","Moving water (about 1000 W/m²·K)","Custom");
        _film=Num("Cooling coefficient",25,0,1e7,5,"W/m²·K");_ambient=Num("Surrounding temperature",20,-273.14,100000,5,"°C");_emissivity=Num("Radiation emissivity (0 = none)",0,0,1,.1,"");
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
    public override string Hint=>_thermal?"Heat through a solid. Pick one body, or several touching bodies with their own materials: they are bonded with perfect heat flow across the joint. Hold faces at a temperature, put heat into faces, or cool faces with air, water and radiation. Faces you leave alone are insulated. Cooling values are typical figures, so use your own when you have them. Transient mode uses a uniform initial temperature.":
        "Linear free-vibration frequencies and mode shapes with fixed faces. Pick one body, or several touching bodies with their own materials: they are bonded. Assign materials. Displayed mode amplitude is arbitrary; this is not a forced or impact response.";
    protected override void OnBegin(){S.CaeChanged+=Refresh;}
    public override void OnEnd(){S.CaeChanged-=Refresh;}
    void Refresh(){if(ReferenceEquals(S.ActiveCommand,this))OnChanged();}
    protected override void UpdateVisibility()
    {
        _type.IsVisible=_hot.IsVisible=_cold.IsVisible=_thermal;
        if(_coolant!=null)
        {
            _power.IsVisible=_thermal&&_heated.Items.Count>0;
            _coolant.IsVisible=_ambient.IsVisible=_emissivity.IsVisible=_thermal&&_cooled.Items.Count>0;
            _film.IsVisible=_thermal&&_cooled.Items.Count>0&&_coolant.Index==3;
            _hot.IsVisible=_thermal&&_first.Items.Count>0;_cold.IsVisible=_thermal&&_second.Items.Count>0;
        }
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
            if(_body.Items.Count==0||(!_thermal&&_first.Items.Count==0))throw new InvalidOperationException("Choose a body and its boundary faces first.");
            var bodies=_body.Items.Select(i=>i.Obj).Distinct().Select(id=>S.Find(id)!).ToList();
            var body=bodies[0];
            foreach(var part in bodies)if(part.Material==null)throw new InvalidOperationException($"Assign a material to {part.Name} before running CAE.");
            var offset=new System.Collections.Generic.Dictionary<Guid,int>();int total=0;
            foreach(var part in bodies){offset[part.Id]=total;total+=part.Shape.Count(SubKind.Face);}
            int[] Faces(SelectionSlot slot)
            {
                if(slot.Items.Any(i=>!offset.ContainsKey(i.Obj)))throw new InvalidOperationException("All boundary faces must belong to the selected bodies.");
                return slot.Items.Select(i=>offset[i.Obj]+i.Index).Distinct().ToArray();
            }
            var settings=new CaeSettings(_thermal?(_type.Index==0?CaeKind.SteadyThermal:CaeKind.TransientThermal):CaeKind.Modal,
                Faces(_first),_thermal?Faces(_second):[],_hot.Value,_cold.Value,_initial.Value,_duration.Value,_modes.Value,_size.Value,bodies.Count>1?bodies.Skip(1).Select(b=>b.Id).ToArray():null,
                _thermal?Faces(_heated):null,_power.Value,_thermal?Faces(_cooled):null,_coolant.Index switch{0=>10,1=>50,2=>1000,_=>_film.Value},_ambient.Value,_emissivity.Value);
            foreach(var part in bodies)StudySetup.Validate(part,null,[.. _first.Items.Concat(_second.Items).Concat(_heated.Items).Concat(_cooled.Items).Where(i=>i.Obj==part.Id).Select(i=>i.Index)]);
            if(settings.Kind!=CaeKind.Modal&&settings.FirstFaces.Intersect(settings.SecondFaces).Any())throw new InvalidOperationException("Temperature boundaries must not overlap.");
            return (body,settings);
    }
    void SaveSetup()
    {
        try{_setupError=null;var(body,settings)=Settings();if(settings.OtherBodies!=null)throw new InvalidOperationException("Saved setups cover one body for now. Run this one directly.");if(string.IsNullOrWhiteSpace(_name.Value))throw new InvalidOperationException("Enter a setup name.");var setup=new SavedCaeCase(_name.Value.Trim(),body.Id,settings,StudySetup.Signature(body));S.CaeCases.RemoveAll(c=>c.Name==setup.Name);S.CaeCases.Add(setup);S.Dirty=true;S.Say("Saved CAE setup: "+setup.Name);Refresh();}
        catch(Exception e){_setupError=e.Message;S.Say(e.Message,true);Refresh();}
    }
    protected override void Analyze()
    {
        if(_setupError!=null){Row("Setup error",_setupError,3);return;}
        if(S.Cae is not { } job){Row("Setup","Assign material, pick body and faces, then Run analysis.");return;}
        Row("Status",job.Status,job.Error==null?0:3);
        if(job.Outcome is not { } r)return;
        Row(r.Prepared.Parts==null?"Source body":"Bodies",r.Prepared.Parts==null?r.Prepared.Body.Name:string.Join(", ",r.Prepared.Parts.Select(p=>$"{p.Body.Name} ({p.Body.Material?.Name})")));if(r.Prepared.Bonds.Count>0)Row("Joints",$"{r.Prepared.Bonds.Count} bonded");Row("Mesh",$"{r.Prepared.Mesh.NodeCount:N0} nodes, {r.Prepared.Mesh.TetCount:N0} quadratic tetrahedra");
        if(r.Temperatures.Length>0){Row("Cooling note","Cooling coefficients are typical figures. Real values depend on the air or water flow around the part.");Row("Minimum temperature",$"{r.Temperatures.Min():G6} °C");Row("Maximum temperature",$"{r.Temperatures.Max():G6} °C");}
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
