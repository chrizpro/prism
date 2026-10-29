using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core;
using Prism.Kernel;
using Prism.Simulation;

namespace Prism.Commands;

/// <summary>Add a fixture (support) to the active study by picking faces.</summary>
public sealed class FixtureCommand : Command
{
    readonly FixtureKind _kind;
    readonly SelectionSlot _faces;
    readonly BoolParam _x, _y, _z;

    public FixtureCommand(FixtureKind kind)
    {
        _kind = kind;
        _faces = Slot(new SelectionSlot { Label = "Faces to support", Filter = SelFilter.Face, Multi = true });
        _x = Check("Lock X", true); _y = Check("Lock Y", true); _z = Check("Lock Z", true);
    }

    public override string Title => _kind switch { FixtureKind.Fixed => "Fixed Support", FixtureKind.Slider => "Roller / Symmetry", _ => "Fixed Directions" };
    public override string Hint => _kind switch
    {
        FixtureKind.Fixed => "Pick the faces that are bolted, welded or clamped: they cannot move at all.",
        FixtureKind.Slider => "Pick flat faces (aligned with X, Y or Z) that may slide in their plane but not move through it. Use it for symmetry planes and rollers.",
        _ => "Pick faces and choose which global directions are locked.",
    };
    public override string Icon => "sim-fixed";
    public override string OkText => "Add fixture";
    protected override void UpdateVisibility() { _x.IsVisible = _y.IsVisible = _z.IsVisible = _kind == FixtureKind.Directional; }

    protected override CommandOutput? Build()
    {
        if (S.ActiveStudy == null) { Error = "Create a study first (Simulate > New Study)."; return null; }
        if (!Complete) return null;
        var st = S.ActiveStudy;
        var faces = _faces.Items.Where(i => i.Kind == SelKind.Face && i.Obj == st.BodyId).Select(i => new FaceRef(i.Obj, i.Index)).ToList();
        if (faces.Count == 0) { Error = "Pick faces of the study's body."; return null; }
        var kind = _kind; bool lx = _x.Value, ly = _y.Value, lz = _z.Value;
        return new CommandOutput
        {
            NoOp = true, Label = Title, Icon = Icon,
            After = s => { s.Dirty=true; st.Fixtures.Add(new Fixture { Name = $"{Title} {st.Fixtures.Count(f => f.Kind == kind) + 1}", Kind = kind, Faces = faces, LockX = lx, LockY = ly, LockZ = lz }); st.Results = null; s.ShowResult(null); },
        };
    }

    public override void BuildOverlay(CadSession s, Overlay o)
    {
        var st = s.ActiveStudy; var body = st == null ? null : s.Find(st.BodyId); if (body == null) return;
        foreach (var it in _faces.Items.Where(i => i.Kind == SelKind.Face && i.Obj == body.Id))
            if (StudyOverlay.FaceInfo(body, it.Index) is { } fi) o.AddDot(fi.center, 0xFF2EA043, 11);
    }
}

/// <summary>Add a load to the active study: force, pressure, torque or gravity.</summary>
public sealed class LoadCommand : Command
{
    readonly LoadKind _kind;
    readonly SelectionSlot? _faces;
    readonly NumberParam _value;
    readonly ChoiceParam _dir;
    readonly BoolParam _total;
    static readonly Vec3[] Dirs = [Vec3.UnitX, Vec3.UnitY, Vec3.UnitZ, -Vec3.UnitX, -Vec3.UnitY, -Vec3.UnitZ];

    public LoadCommand(LoadKind kind)
    {
        _kind = kind;
        if (kind != LoadKind.Gravity) _faces = Slot(new SelectionSlot { Label = "Faces to load", Filter = SelFilter.Face, Multi = true });
        _value = kind switch
        {
            LoadKind.Force => Num("Force", 100, 0, 1e9, 10, "N"),
            LoadKind.Pressure => Num("Pressure", 1, 0, 1e6, 0.1, "MPa"),
            LoadKind.Torque => Num("Torque", 1000, 0, 1e12, 100, "N·mm"),
            _ => Num("Gravity", 1, 0, 100, 0.1, "g"),
        };
        _dir = kind == LoadKind.Force ? Choice("Direction", 0, "Into the face (along its normal)", "+X", "+Y", "+Z", "-X", "-Y", "-Z")
             : kind == LoadKind.Gravity ? Choice("Direction", 5, "+X", "+Y", "+Z", "-X", "-Y", "-Z") : Choice("Direction", 0, "-");
        _total = Check("The force is the total over all picked faces", true);
    }

    public override string Title => _kind switch { LoadKind.Force => "Force", LoadKind.Pressure => "Pressure", LoadKind.Torque => "Torque", _ => "Gravity" };
    public override string Hint => _kind switch
    {
        LoadKind.Force => "Pick the faces the force pushes on, enter its size and direction. It is spread evenly over the faces.",
        LoadKind.Pressure => "Pick faces that are pressed on (positive pushes into the part), enter the pressure in MPa (N/mm²).",
        LoadKind.Torque => "Pick a flat face and enter a twisting moment about its normal (N·mm). Positive turns counter-clockwise seen from outside.",
        _ => "The part's own weight. Gravity pulls along the chosen direction; 1 g is Earth gravity.",
    };
    public override string Icon => _kind switch { LoadKind.Pressure => "sim-pressure", LoadKind.Torque => "sim-torque", LoadKind.Gravity => "sim-gravity", _ => "sim-force" };
    public override string OkText => "Add load";
    protected override void UpdateVisibility() { _dir.IsVisible = _kind is LoadKind.Force or LoadKind.Gravity; _total.IsVisible = _kind == LoadKind.Force; }

    protected override CommandOutput? Build()
    {
        if (S.ActiveStudy == null) { Error = "Create a study first (Simulate > New Study)."; return null; }
        var st = S.ActiveStudy;
        var faces = _faces?.Items.Where(i => i.Kind == SelKind.Face && i.Obj == st.BodyId).Select(i => new FaceRef(i.Obj, i.Index)).ToList() ?? [];
        if (_kind != LoadKind.Gravity && faces.Count == 0) { Error = null; return null; }
        var ld = new Load { Kind = _kind, Faces = faces, Value = _value.Value, Total = _total.Value };
        if (_kind == LoadKind.Force) { ld.AlongNormal = _dir.Index == 0; if (!ld.AlongNormal) ld.Direction = Dirs[_dir.Index - 1]; }
        if (_kind == LoadKind.Gravity) ld.Direction = Dirs[_dir.Index];
        ld.Name = $"{Title} {st.Loads.Count(l => l.Kind == _kind) + 1}";
        return new CommandOutput { NoOp = true, Label = Title, Icon = Icon, After = s => { s.Dirty=true; st.Loads.Add(ld); st.Results = null; s.ShowResult(null); } };
    }

    public override void BuildOverlay(CadSession s, Overlay o)
    {
        var st = s.ActiveStudy; var body = st == null ? null : s.Find(st.BodyId); if (body == null || _faces == null) return;
        foreach (var it in _faces.Items.Where(i => i.Kind == SelKind.Face && i.Obj == body.Id))
            if (StudyOverlay.FaceInfo(body, it.Index) is { } fi) o.AddDot(fi.center, 0xFFE5484D, 11);
    }
}

/// <summary>Click the result surface to read the value of what is shown (stress, displacement or safety factor) at that spot.</summary>
public sealed class ProbeCommand : Command
{
    readonly List<(Vec3 pos, float value, string label)> _probes = [];

    public ProbeCommand()
    {
        Add(new ActionParam { Label = "Clear probes", Run = () => { _probes.Clear(); OnChanged(); } });
        Tool = new ProbeTool(this);
    }

    public override string Title => "Probe";
    public override string Hint => "Click anywhere on the result to read its value. Each click adds a probe.";
    public override string Icon => "sim-probe";
    public override string OkText => "Done";

    public void AddProbe(CadSession s, double x, double y)
    {
        var rv = s.Result; if (rv == null) return;
        var h = Picking.RayMesh(s.Camera, rv.Mesh, x, y);
        if (h is not { } hit) return;
        int best = -1; double bd = double.MaxValue;
        for (int k = 0; k < 3; k++)
        {
            int v = rv.Mesh.Indices[3 * hit.tri + k];
            double d = rv.Positions[v].DistanceTo(hit.point);
            if (d < bd) { bd = d; best = v; }
        }
        if (best < 0) return;
        float val = rv.Values[best];
        string unit = string.IsNullOrEmpty(rv.Unit) ? "" : " " + rv.Unit;
        _probes.Add((rv.Positions[best], val, $"{val:0.###}{unit}"));
        if (_probes.Count > 10) _probes.RemoveAt(0);
        OnChanged();
    }

    protected override CommandOutput? Build()
    {
        Results.Clear();
        if (S.Result == null) { Results.Add(new ResultLine("No result", "run a study first", 2)); }
        else if (_probes.Count == 0) Results.Add(new ResultLine("Probes", "click on the model"));
        for (int i = 0; i < _probes.Count; i++) Results.Add(new ResultLine($"Probe {i + 1}", _probes[i].label));
        return new CommandOutput { NoOp = true };
    }

    public override void BuildOverlay(CadSession s, Overlay o)
    {
        foreach (var p in _probes) { o.AddDot(p.pos, 0xFFFFFFFF, 10); o.AddLabel(p.pos, p.label, true, 1); }
    }
}

public sealed class ProbeTool(ProbeCommand cmd) : IViewportTool
{
    public string Prompt => "Click on the result to probe its value.";
    public bool PointerDown(CadSession s, PointerInfo p) { if (!p.Left) return false; cmd.AddProbe(s, p.X, p.Y); return true; }
    public void PointerMove(CadSession s, PointerInfo p) { }
    public void PointerUp(CadSession s, PointerInfo p) { }
    public bool Key(CadSession s, string key) => false;
    public void BuildOverlay(CadSession s, Overlay o) { }
    public void Deactivate(CadSession s) { }
}
