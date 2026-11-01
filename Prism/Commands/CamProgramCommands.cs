using System;
using System.Linq;
using Prism.Core;
using Prism.Core.Cam;

namespace Prism.Commands;

public sealed class CamToolLibraryCommand : AnalysisCommand
{
    readonly ChoiceParam _preset,_shape;readonly TextParam _name;readonly NumberParam _dia,_rpm,_feed,_plunge,_step;
    readonly CamTool[] _tools;
    public CamToolLibraryCommand()
    {
        _tools=CamToolLibrary.Tools;_preset=Choice("Copy from",0,new[]{"Custom tool"}.Concat(_tools.Select(t=>t.Name)).ToArray());
        _name=Text("Tool name / material","My 6 mm end mill");_shape=Choice("Cutter shape",0,"Flat end mill","Ball nose");
        _dia=Num("Diameter",6,.1,100,.5);_rpm=Num("Spindle speed",12000,100,60000,500,"rpm");_feed=Num("Cutting feed",800,1,20000,50,"mm/min");_plunge=Num("Plunge feed",300,1,10000,25,"mm/min");_step=Num("Depth per pass",2,.05,100,.25);
        _preset.Changed=()=>{if(_preset.Index>0){var t=_tools[_preset.Index-1];ConfigureInputs(()=>{_name.Value=t.Name;_shape.Index=t.Ball?1:0;_dia.Value=t.Diameter;_rpm.Value=t.Rpm;_feed.Value=t.Feed;_plunge.Value=t.Plunge;_step.Value=t.StepDown;});}OnChanged();};
        Add(new ActionParam{Label="Save tool",Run=()=>{try{CamToolLibrary.SaveTool(new(_name.Value.Trim(),_dia.Value,_shape.Index==1,_rpm.Value,_feed.Value,_plunge.Value,_step.Value));S.Say("Saved tool. Reopen an operation to use it.");OnChanged();}catch(Exception e){S.Say(e.Message,true);}}});
    }
    public override string Title=>"CAM Tool Library";public override string Icon=>"cnc";
    public override string Hint=>"Copy a preset or enter a tool and material-specific cutting values. Save under a new name to add a tool, or reuse its name to update it. Values persist locally; existing generated paths keep their original settings.";
    protected override void Analyze(){Row("Available tools",CamToolLibrary.Tools.Length.ToString());Row("Tool presets","Starting values only; tune for your machine, tool and material.");}
}

public sealed class SelectCamSetupCommand : AnalysisCommand
{
    ChoiceParam _setup=null!;CamSetup[] _setups=[];
    public SelectCamSetupCommand(){Add(new ActionParam{Label="Use setup",Run=Select});}
    public override string Title=>"Select CAM Setup";public override string Icon=>"cnc";
    public override string Hint=>"Switch between named stock and work-zero setups. Toolpaths keep their original setup. Posting and stock review use the active setup. All setups use the global +Z tool axis.";
    protected override void OnBegin()
    {
        _setups=S.CamSetups.Concat(S.CamSetup is{} active?new[]{active}:[]).DistinctBy(s=>s.Name).ToArray();
        _setup=Choice("Saved setup",Math.Max(0,Array.FindIndex(_setups,s=>s.Name==S.CamSetup?.Name)),_setups.Select(s=>s.Name).DefaultIfEmpty("No saved setups").ToArray());
        Params.Move(Params.IndexOf(_setup),0);PanelSettings.Move(PanelSettings.IndexOf(_setup),0);
    }
    void Select()
    {
        try
        {
            if(_setups.Length==0)throw new InvalidOperationException("Create a named CAM setup first.");
            var setup=_setups[_setup.Index];var body=S.Find(setup.BodyId)??throw new InvalidOperationException("The setup model was deleted.");setup.Validate(body);
            if(S.CamSetup is{} old&&!S.CamSetups.Any(s=>s.Name==old.Name))S.CamSetups.Add(old);
            S.StopCamPlayback();S.CamSetup=setup;S.Dirty=true;S.Invalidate();OnChanged();
        }catch(Exception e){S.Say(e.Message,true);}
    }
    protected override void Analyze(){Row("Active setup",S.CamSetup?.Name??"None");Row("Saved setups",_setups.Length.ToString());Row("Tool axis","Global +Z; rotated work coordinate systems are not supported.",2);}
}

public sealed class CamProgramOrderCommand : Command
{
    ChoiceParam _operation=null!;readonly IntParam _position;Guid[] _ids=[];
    public CamProgramOrderCommand(){_position=Add(new IntParam{Label="Move to position",Value=1,Min=1,Max=10000});}
    public override string Title=>"CNC Operation Order";public override string Icon=>"toolpath";public override string OkText=>"Save order";
    public override string Hint=>"Choose a visible machining operation and its new position. Posting, setup sheets and stock review follow this order. Hide operations to exclude them from the program.";
    protected override void OnBegin()
    {
        var paths=S.MillingProgram().ToArray();_ids=paths.Select(o=>o.Id).ToArray();
        _operation=Choice("Operation",0,paths.Select((o,i)=>$"{i+1}. {o.Name}").DefaultIfEmpty("No operations").ToArray());
        Params.Move(Params.IndexOf(_operation),0);PanelSettings.Move(PanelSettings.IndexOf(_operation),0);
    }
    protected override CommandOutput? Build()
    {
        Results.Clear();if(_ids.Length==0){Results.Add(new("Program","Create a machining operation first.",2));return null;}
        var order=_ids.ToList();var id=order[_operation.Index];order.Remove(id);order.Insert(Math.Clamp(_position.Value-1,0,order.Count),id);
        for(int i=0;i<order.Count;i++)Results.Add(new($"{i+1}",S.Find(order[i])?.Name??"Deleted operation"));
        return new(){Label="Reorder CNC program",After=s=>{for(int i=0;i<order.Count;i++){int rank=i;var obj=s.Find(order[i]);if(obj?.Asm is ToolpathInfo p)s.Update(obj.Id,o=>o.WithAsm(p with{ProgramOrder=rank}));}}};
    }
}
