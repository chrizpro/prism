using System;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Prism.Core;
using Prism.Simulation;

namespace Prism.Commands;

public sealed class NamedFacesCommand : Command
{
    readonly SelectionSlot _faces; readonly TextParam _name;
    public NamedFacesCommand(){_faces=Slot(new(){Label="Faces",Filter=SelFilter.Face,Multi=true});_name=Text("Selection name","Boundary faces");}
    public override string Title=>"Save Face Selection";
    public override string Icon=>"sim-fixed";
    public override string Hint=>"Name faces from one body so they can be selected again for supports, loads or temperature boundaries. Geometry changes require reselection.";
    protected override CommandOutput? Build()
    {
        if(!Complete)return null;
        if(string.IsNullOrWhiteSpace(_name.Value))throw new InvalidOperationException("Enter a selection name.");
        var ids=_faces.Items.Select(i=>i.Obj).Distinct().ToArray();if(ids.Length!=1)throw new InvalidOperationException("Choose faces from one body.");
        var body=S.Find(ids[0])!;var set=new NamedFaceSet(_name.Value.Trim(),body.Id,_faces.Items.Select(i=>i.Index).Distinct().ToArray(),StudySetup.Signature(body));
        return new(){NoOp=true,After=s=>{s.FaceSets.RemoveAll(x=>x.Name==set.Name);s.FaceSets.Add(set);s.Dirty=true;s.RaiseStudy();}};
    }
}
public sealed class RecallFacesCommand : Command
{
    ChoiceParam _set=null!;
    public override string Title=>"Recall Face Selection";public override string Icon=>"sim-fixed";
    public override string Hint=>"Select a named boundary group, then press OK. The recalled faces can be used as the selection when opening another tool.";
    protected override void OnBegin(){_set=Choice("Named selection",0,S.FaceSets.Select(x=>x.Name).DefaultIfEmpty("No saved selections").ToArray());}
    protected override CommandOutput? Build()
    {
        var set=S.FaceSets.FirstOrDefault(x=>x.Name==_set.Selected);if(set==null)return null;
        var body=S.Find(set.BodyId)??throw new InvalidOperationException("The selection's body was deleted.");StudySetup.Validate(body,set.ShapeSignature,set.Faces);
        return new(){NoOp=true,After=s=>{s.ClearSelection();foreach(int face in set.Faces)s.SetSelection(new(body.Id,SelKind.Face,face),true);}};
    }
}
public sealed class SavedCaeCommand : AnalysisCommand
{
    ChoiceParam _case=null!;
    public SavedCaeCommand(){Add(new ActionParam{Label="Run saved setup",Run=Run});}
    public override string Title=>"Saved CAE Setups";public override string Icon=>"sim-run";
    public override string Hint=>"Rerun a saved thermal or modal setup with the body's currently assigned engineering properties. Stale face references are rejected.";
    protected override void OnBegin(){_case=Choice("Setup",0,S.CaeCases.Select(x=>x.Name).DefaultIfEmpty("No saved setups").ToArray());Params.Move(Params.IndexOf(_case),0);PanelSettings.Move(PanelSettings.IndexOf(_case),0);S.CaeChanged+=Refresh;}
    void Refresh(){if(ReferenceEquals(S.ActiveCommand,this))OnChanged();}
    public override void OnEnd()=>S.CaeChanged-=Refresh;
    void Run(){try{var c=S.CaeCases.FirstOrDefault(x=>x.Name==_case.Selected)??throw new InvalidOperationException("Save a setup from Thermal Analysis or Natural Frequencies first.");var body=S.Find(c.BodyId)??throw new InvalidOperationException("The body was deleted.");StudySetup.Validate(body,c.ShapeSignature,c.Settings.FirstFaces.Concat(c.Settings.SecondFaces).ToArray());S.StartCae(body,c.Settings);}catch(Exception e){S.Say(e.Message,true);}}
    protected override void Analyze(){foreach(var c in S.CaeCases)Row(c.Name,$"{c.Settings.Kind} · {S.Find(c.BodyId)?.Name??"Missing body"}");if(S.Cae is{} job){Row("Status",job.Status,job.Error==null?0:3);if(job.Outcome is{} result){if(result.Temperatures.Length>0)Row("Temperature range",$"{result.Temperatures.Min():G6}–{result.Temperatures.Max():G6} °C");for(int i=0;i<result.Frequencies.Length;i++)Row($"Mode {i+1}",$"{result.Frequencies[i]:G6} Hz");}}}
}
public sealed class ConvergenceCommand : AnalysisCommand
{
    readonly NumberParam _size;readonly TextParam _file;readonly List<(double h,int nodes,double displacement,double stress)> _rows=[];bool _running;string _status="Choose a static study with supports, loads and a material.";CancellationTokenSource _cancel=new();
    public ConvergenceCommand(){_size=Num("Coarse element size (0 = automatic)",0,0,10000,1);_file=Text("Save comparison CSV","", "Optional full file path");Add(new ActionParam{Label="Compare three meshes",Run=Start});Add(new ActionParam{Label="Stop after current solve",Run=()=>_cancel.Cancel()});}
    public override string Title=>"Mesh Refinement Check";public override string Icon=>"sim-mesh";
    public override string Hint=>"Compare h, 0.7h and 0.49h using maximum displacement. Peak stress is reported separately: stress singularities at supports and loads can grow under refinement. This is a comparison, not an accuracy certificate.";
    public override void OnEnd()=>_cancel.Cancel();
    async void Start()
    {
        if(_running)return;
        try
        {
            var original=S.ActiveStudy??throw new InvalidOperationException("Create a static study first.");if(original.OtherBodies.Count>0)throw new InvalidOperationException("Mesh convergence works on a one-body study for now.");var body=S.Find(original.BodyId)??throw new InvalidOperationException("The study body was deleted.");
            var st=new StudyDef{BodyId=body.Id,ShapeSignature=original.ShapeSignature};
            st.Fixtures.AddRange(original.Fixtures.Select(f=>new Fixture{Name=f.Name,Kind=f.Kind,Faces=[..f.Faces],LockX=f.LockX,LockY=f.LockY,LockZ=f.LockZ}));
            st.Loads.AddRange(original.Loads.Select(l=>new Load{Name=l.Name,Kind=l.Kind,Faces=[..l.Faces],Direction=l.Direction,AlongNormal=l.AlongNormal,Value=l.Value,Total=l.Total}));
            double h=_size.Value>0?_size.Value:StudyRunner.AutoSize(body);
            if(h*.49<body.Mesh.Max.DistanceTo(body.Mesh.Min)/70)throw new InvalidOperationException("Choose a coarser starting size for this three-mesh check.");
            string file=_file.Value;_running=true;_rows.Clear();_cancel.Dispose();_cancel=new();var token=_cancel.Token;
            await Task.Run(()=>{foreach(double factor in new[]{1,.7,.49}){token.ThrowIfCancellationRequested();st.ElementSize=h*factor;var p=StudyRunner.Mesh(body,st.ElementSize);try{var r=StudyRunner.Run(S,st,p);var row=(st.ElementSize,p.Mesh.NodeCount,r.MaxDisplacement,r.MaxVonMises);Dispatcher.UIThread.Invoke(()=>{_rows.Add(row);_status=$"Completed {_rows.Count}/3 meshes";OnChanged();});}finally{p.Mesh.Dispose();}}},token);
            if(!ReferenceEquals(S.Find(body.Id),body)||S.ActiveStudy!=original){_rows.Clear();throw new InvalidOperationException("The source or active setup changed. Run the mesh comparison again.");}
            _status="Three-mesh comparison complete. Compare displacement changes; inspect local stress separately.";
            if(!string.IsNullOrWhiteSpace(file)){File.WriteAllLines(file,new[]{"element_size_mm,nodes,max_displacement_mm,peak_von_mises_MPa"}.Concat(_rows.Select(r=>FormattableString.Invariant($"{r.h:G10},{r.nodes},{r.displacement:G10},{r.stress:G10}"))));_status+=" CSV: "+file;}
        }
        catch(OperationCanceledException){_status="Stopped after current solve.";}catch(Exception e){_status=e.Message;S.Say(e.Message,true);}finally{_running=false;if(ReferenceEquals(S.ActiveCommand,this))OnChanged();}
    }
    protected override void Analyze(){Row("Status",_status);for(int i=0;i<_rows.Count;i++){var r=_rows[i];Row($"h = {r.h:G4} mm",$"{r.nodes:N0} nodes · displacement {r.displacement:G6} mm · peak stress {r.stress:G6} MPa");if(i>0)Row("Displacement change",$"{Math.Abs(r.displacement-_rows[i-1].displacement)/Math.Max(Math.Abs(r.displacement),1e-15)*100:G4}%");}}
}
public sealed class ExportStudyCommand : AnalysisCommand
{
    readonly TextParam _folder;
    public ExportStudyCommand(){_folder=Text("Output folder",Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"Prism","CAE"));Add(new ActionParam{Label="Export static results",Run=Save});}
    public override string Title=>"Export CAE Results";public override string Icon=>"export";
    public override string Hint=>"Export the active static study's nodal displacement, stress and available safety factors to CSV, with setup, material and result notes in JSON. Thermal/modal case files are available in their result folder.";
    void Save(){try{var st=S.ActiveStudy??throw new InvalidOperationException("No active static study.");var r=st.Results??throw new InvalidOperationException("Run the study first.");var body=S.Find(st.BodyId)??throw new InvalidOperationException("Missing source body.");StudySetup.Validate(body,st.ShapeSignature,[]);Directory.CreateDirectory(_folder.Value);string F(double v)=>double.IsFinite(v)?v.ToString("G12",CultureInfo.InvariantCulture):"";File.WriteAllLines(Path.Combine(_folder.Value,"nodes.csv"),new[]{"node,x_mm,y_mm,z_mm,ux_mm,uy_mm,uz_mm,von_mises_MPa,yield_safety_factor"}.Concat(Enumerable.Range(0,r.Mesh.NodeCount).Select(i=>string.Join(",",new[]{(i+1).ToString(),F(r.Mesh.Nodes[i*3]),F(r.Mesh.Nodes[i*3+1]),F(r.Mesh.Nodes[i*3+2]),F(r.Solution.Displacement[i*3]),F(r.Solution.Displacement[i*3+1]),F(r.Solution.Displacement[i*3+2]),F(r.Solution.VonMises[i]),F(r.SafetyFactor[i])}))));File.WriteAllText(Path.Combine(_folder.Value,"study.json"),System.Text.Json.JsonSerializer.Serialize(new{setup=st,material=body.Material,r.Notes,r.MaxDisplacement,r.MaxVonMises}));S.Say("Exported CAE results to "+_folder.Value);}catch(Exception e){S.Say(e.Message,true);}}
    protected override void Analyze(){Row("Export","CSV + JSON; undefined safety factors are empty CSV fields.");}
}
