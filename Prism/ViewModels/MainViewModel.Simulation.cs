using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Prism.Commands;
using Prism.Core;
using Prism.Simulation;

namespace Prism.ViewModels;

/// <summary>One fixture or load line in the study tree.</summary>
public sealed class SimRow
{
    public Guid Id { get; init; }
    public bool IsFixture { get; init; }
    public string Title { get; init; } = "";
    public string Detail { get; init; } = "";
    public string Glyph { get; init; } = "";
}

/// <summary>Study setup, running and results display.</summary>
public sealed partial class MainViewModel
{
    public ObservableCollection<SimRow> SimRows { get; } = [];
    public ObservableCollection<ResultLine> SimStats { get; } = [];
    public ObservableCollection<string> SimNotes { get; } = [];
    public ObservableCollection<string> LegendLabels { get; } = [];
    public IReadOnlyList<string> ResultKinds { get; } = ["von Mises stress", "Displacement (magnitude)", "Factor of safety", "Displacement X", "Displacement Y", "Displacement Z"];

    [ObservableProperty] public partial bool ShowSimPanel { get; set; }
    [ObservableProperty] public partial string SimTitle { get; set; } = "";
    [ObservableProperty] public partial string SimBody { get; set; } = "";
    [ObservableProperty] public partial string SimMaterial { get; set; } = "";
    [ObservableProperty] public partial double SimElementSize { get; set; }
    [ObservableProperty] public partial string SimAutoSize { get; set; } = "";
    [ObservableProperty] public partial bool SimBusy { get; set; }
    [ObservableProperty] public partial string SimStatus { get; set; } = "";
    [ObservableProperty] public partial bool HasResult { get; set; }
    [ObservableProperty] public partial bool HasStudy { get; set; }
    [ObservableProperty] public partial int ResultKindIndex { get; set; }
    [ObservableProperty] public partial double DeformScale { get; set; } = 1;
    [ObservableProperty] public partial bool ShowMeshEdges { get; set; }
    [ObservableProperty] public partial bool ShowLegend { get; set; }
    [ObservableProperty] public partial string LegendTitle { get; set; } = "";
    [ObservableProperty] public partial bool LegendReversed { get; set; }
    bool _syncSim;
    StudyRunner.Prepared? _prepared; Guid _preparedStudy; double _preparedSize;

    partial void OnSimElementSizeChanged(double value)
    {
        if (_syncSim || Session.ActiveStudy is not { } st) return;
        Session.Dirty=true; st.ElementSize = Math.Max(value, 0); _prepared = null;
    }

    partial void OnResultKindIndexChanged(int value) { if (!_syncSim) RebuildResultView(); }
    partial void OnDeformScaleChanged(double value) { if (!_syncSim) RebuildResultView(); }
    partial void OnShowMeshEdgesChanged(bool value) { if (Session.Result is { } r) { r.ShowEdges = value; Session.Invalidate(); } }

    // ------------------------------------------------------------ study management
    public void NewStudy()
    {
        var s = Session;
        var body = s.SelectedObjects.FirstOrDefault(o => o.Kind is ObjKind.Solid or ObjKind.Form)
                   ?? (s.Objects.Count(o => o.Kind == ObjKind.Solid) == 1 ? s.Objects.First(o => o.Kind == ObjKind.Solid) : null);
        if (body == null) { s.Say("Select the solid body to study first (or have exactly one solid in the model).", true); return; }
        var study = s.NewStudy(body);
        var others = s.SelectedObjects.Where(o => o.Kind == ObjKind.Solid && o.Id != body.Id).ToList();
        study.OtherBodies.AddRange(others.Select(o => o.Id));
        s.Say(others.Count == 0 ? $"Created {study.Name} for {body.Name}. Add a Fixed Support and a load, then Run Study." : $"Created {study.Name} for {others.Count + 1} bodies, bonded where they touch. Give each a material, add a Fixed Support and a load, then Run Study.");
        UpdatePanels();
    }

    public void RemoveSimRow(Guid id)
    {
        if (Session.ActiveStudy is not { } st) return;
        Session.Dirty=true; st.Fixtures.RemoveAll(f => f.Id == id); st.Loads.RemoveAll(l => l.Id == id);
        st.Results = null; Session.ShowResult(null);
    }

    void RefreshSim()
    {
        var st = Session.ActiveStudy;
        HasStudy = st != null;
        _syncSim = true;
        SimRows.Clear(); SimStats.Clear(); SimNotes.Clear(); LegendLabels.Clear();
        if (st != null)
        {
            var body = Session.Find(st.BodyId);
            SimTitle = st.Name; SimBody = body?.Name ?? "(missing body)";
            SimMaterial = body?.Material?.Name ?? $"none assigned - {Materials.Default.Name} will be used";
            SimElementSize = st.ElementSize;
            SimAutoSize = body == null ? "" : $"automatic: {StudyRunner.AutoSize(body):0.##} mm";
            foreach (var f in st.Fixtures)
                SimRows.Add(new SimRow { Id = f.Id, IsFixture = true, Title = f.Name, Detail = $"{f.Faces.Count} face{(f.Faces.Count == 1 ? "" : "s")}" + (f.Kind == FixtureKind.Directional ? $" · locks {(f.LockX ? "X" : "")}{(f.LockY ? "Y" : "")}{(f.LockZ ? "Z" : "")}" : ""), Glyph = "sim-fixed" });
            foreach (var l in st.Loads)
                SimRows.Add(new SimRow
                {
                    Id = l.Id, Title = l.Name, Glyph = l.Kind switch { LoadKind.Pressure => "sim-pressure", LoadKind.Torque => "sim-torque", LoadKind.Gravity => "sim-gravity", _ => "sim-force" },
                    Detail = l.Kind switch
                    {
                        LoadKind.Pressure => $"{l.Value:0.##} MPa on {l.Faces.Count} face(s)",
                        LoadKind.Torque => $"{l.Value:0.##} N·mm",
                        LoadKind.Gravity => $"{l.Value:0.##} g",
                        _ => $"{l.Value:0.##} N on {l.Faces.Count} face(s)",
                    },
                });
        }
        var r = Session.Result;
        HasResult = r?.Source != null && r.Kind != ResultKind.MeshOnly;
        if (r?.Source is { } res)
        {
            if (r.Kind != ResultKind.MeshOnly)
            {
                SimStats.Add(new ResultLine("Max von Mises", $"{res.MaxVonMises:0.##} MPa", res.MaxVonMises > res.Yield ? 3 : 0));
                SimStats.Add(new ResultLine("Yield strength", double.IsFinite(res.Yield)?$"{res.Yield:0.##} MPa":"Not defined"));
                SimStats.Add(new ResultLine("Max displacement", $"{res.MaxDisplacement:0.####} mm"));
                SimStats.Add(new ResultLine("Min factor of safety", !double.IsFinite(res.Yield)?"No yield basis":res.MinSafety >= 999 ? "> 1000" : $"{res.MinSafety:0.##}", !double.IsFinite(res.Yield)?2:res.MinSafety < 1 ? 3 : res.MinSafety < 1.5 ? 2 : 1));
                SimStats.Add(new ResultLine("Reaction / applied", $"{res.Solution.Reaction.Length:0.##} / {res.Solution.Applied:0.##} N"));
                SimStats.Add(new ResultLine("Solve time", $"{res.SolveSeconds:0.#} s"));
            }
            else SimStats.Add(new ResultLine("Mesh", $"{res.Mesh.TetCount:#,0} elements, quality min {res.Mesh.MinQuality:0.##}", res.Mesh.MinQuality > 0.1 ? 1 : 2));
            foreach (var n in res.Notes) SimNotes.Add(n);
            ShowLegend = r.Kind != ResultKind.MeshOnly;
            LegendTitle = string.IsNullOrEmpty(r.Unit) ? r.Title : $"{r.Title} ({r.Unit})";
            LegendReversed = r.Min > r.Max;
            for (int i = 0; i < 5; i++)
            {
                double t = 1 - i / 4.0;            // top = Max colour end
                double v = r.Min + (r.Max - r.Min) * t;
                if (r.Min > r.Max) v = r.Max + (r.Min - r.Max) * (1 - t);
                LegendLabels.Add(v.ToString(Math.Abs(v) >= 100 ? "0" : Math.Abs(v) >= 1 ? "0.0" : "0.000", CultureInfo.InvariantCulture));
            }
            ResultKindIndex = r.Kind switch { ResultKind.VonMises => 0, ResultKind.Displacement => 1, ResultKind.SafetyFactor => 2, ResultKind.DisplacementX => 3, ResultKind.DisplacementY => 4, ResultKind.DisplacementZ => 5, _ => ResultKindIndex };
            DeformScale = r.Scale; ShowMeshEdges = r.ShowEdges;
        }
        else ShowLegend = false;
        if (r is { Kind: ResultKind.Scalar })
        {
            ShowLegend=true;LegendTitle=$"{r.Title} ({r.Unit})"; LegendReversed=false;
            for(int i=0;i<5;i++) LegendLabels.Add((r.Max-(r.Max-r.Min)*i/4).ToString("G4",CultureInfo.InvariantCulture));
        }
        _syncSim = false;
        UpdatePanels();
        RefreshRibbon();
    }

    static ResultKind KindOf(int i) => i switch { 0 => ResultKind.VonMises, 1 => ResultKind.Displacement, 2 => ResultKind.SafetyFactor, 3 => ResultKind.DisplacementX, 4 => ResultKind.DisplacementY, _ => ResultKind.DisplacementZ };

    void RebuildResultView()
    {
        if (Session.ActiveStudy?.Results is not { } res) return;
        var body = Session.Find(Session.ActiveStudy.BodyId); if (body == null) return;
        double size = body.Mesh.Max.DistanceTo(body.Mesh.Min);
        var kind=KindOf(ResultKindIndex);
        if(kind==ResultKind.SafetyFactor&&!double.IsFinite(res.Yield)){Session.Say("No yield-based safety factor is defined for this material.",true);kind=ResultKind.VonMises;}
        var view = ResultView.Build(res, body.Id, kind, Math.Max(DeformScale, 0), size);
        view.ShowEdges = ShowMeshEdges;
        Session.ShowResult(view);
    }

    public void SetResultKind(ResultKind k)
    {
        if (Session.ActiveStudy?.Results == null) { Session.Say("Run the study first.", true); return; }
        ResultKindIndex = k switch { ResultKind.VonMises => 0, ResultKind.Displacement => 1, ResultKind.SafetyFactor => 2, ResultKind.DisplacementX => 3, ResultKind.DisplacementY => 4, _ => 5 };
        RebuildResultView();
    }

    public void ClearResults() { Session.ShowResult(null); Session.ShowMotion(null); }

    // ------------------------------------------------------------ mesh and run
    public async void MeshStudy()
    {
        var st = Session.ActiveStudy; if (st == null || SimBusy) return;
        SimBusy = true; SimStatus = "Meshing…";
        try
        {
            var data = await Task.Run(() => StudyRunner.Mesh(Session, st));
            _prepared = data; _preparedStudy = st.Id; _preparedSize = st.ElementSize;
            var body = data.Body;
            var fake = new StudyResults { OtherBodies = data.Parts?.Skip(1).Select(x => x.Body).ToArray() ?? [], Mesh = data.Mesh, Solution = new Prism.Kernel.FemMesh.StaticResult(new double[data.Mesh.NodeCount * 3], new double[data.Mesh.NodeCount * 6], new double[data.Mesh.NodeCount], 0, 0, Prism.Kernel.Vec3.Zero, 0), BoundaryNormal = data.BoundaryNormal, Notes = [$"{data.Mesh.TetCount:#,0} tetrahedra, {data.Mesh.NodeCount:#,0} nodes, element size {data.Maxh:0.##} mm."] };
            var view = ResultView.Build(fake, body.Id, ResultKind.MeshOnly, 1, body.Mesh.Max.DistanceTo(body.Mesh.Min));
            Session.ShowResult(view);
            SimStatus = $"Mesh ready: {data.Mesh.TetCount:#,0} elements";
            Session.Say(SimStatus);
        }
        catch (Exception e) when (e is not OutOfMemoryException) { SimStatus = e.Message; Session.Say(e.Message, true); }
        finally { SimBusy = false; }
    }

    void FinishRun(StudyDef st, StudyResults res)
    {
        st.Results = res; _prepared = null;
        var body = Session.Find(st.BodyId)!;
        double size = res.OtherBodies.Select(o => o.Mesh).Prepend(body.Mesh).Max(m => m.Max.DistanceTo(m.Min));
        _syncSim = true; DeformScale = Math.Round(ResultView.AutoScale(res, size), 1); ResultKindIndex = 0; _syncSim = false;
        var view = ResultView.Build(res, body.Id, ResultKind.VonMises, DeformScale, size);
        view.ShowEdges = ShowMeshEdges;
        Session.ShowResult(view);
        SimStatus = $"Done in {res.SolveSeconds:0.#} s";
        Session.Say($"{st.Name}: max stress {res.MaxVonMises:0.#} MPa, max displacement {res.MaxDisplacement:0.###} mm, " + (double.IsFinite(res.Yield)?$"min factor of safety {res.MinSafety:0.##}.":"no yield-based factor of safety available."));
    }

    /// <summary>Run on the calling thread (used by automation and tests).</summary>
    public StudyResults RunStudySync()
    {
        var st = Session.ActiveStudy ?? throw new InvalidOperationException("Create a study first.");
        var res = StudyRunner.Run(Session, st);
        FinishRun(st, res);
        return res;
    }

    public async void RunStudy()
    {
        var st = Session.ActiveStudy; if (st == null || SimBusy) { if (st == null) Session.Say("Create a study first (Simulate > New Study).", true); return; }
        SimBusy = true; SimStatus = "Starting…";
        void Status(string m) => Dispatcher.UIThread.Post(() => SimStatus = m);
        try
        {
            var runSession=Session;
            string setupJson=System.Text.Json.JsonSerializer.Serialize(st);
            var setup=System.Text.Json.JsonSerializer.Deserialize<StudyDef>(setupJson)!;
            var source=runSession.Find(st.BodyId)??throw new InvalidOperationException("The study body was deleted.");
            var prep = _prepared != null && ReferenceEquals(_prepared.Body, Session.Find(st.BodyId)) && _preparedStudy == st.Id && Math.Abs(_preparedSize - st.ElementSize) < 1e-12 ? _prepared : null;
            var res = await Task.Run(() =>
            {
                var mesh=prep??StudyRunner.Mesh(runSession,setup);
                try{return StudyRunner.Run(runSession,setup,mesh,Status);}catch{if(prep==null)mesh.Mesh.Dispose();throw;}
            });
            if(!ReferenceEquals(Session,runSession)||!ReferenceEquals(runSession.Find(st.BodyId),source)||runSession.ActiveStudy!=st||System.Text.Json.JsonSerializer.Serialize(st)!=setupJson)
            {res.Mesh.Dispose();_prepared=null;runSession.Say("Study completed, but its source or setup changed; run again before using the results.",true);return;}
            FinishRun(st, res);
        }
        catch (Exception e) when (e is not OutOfMemoryException) { SimStatus = e.Message; Session.Say(e.Message, true); }
        finally { SimBusy = false; }
    }
}
