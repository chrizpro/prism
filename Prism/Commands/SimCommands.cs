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
        var faces = _faces.Items.Where(i => i.Kind == SelKind.Face && st.Has(i.Obj)).Select(i => new FaceRef(i.Obj, i.Index)).ToList();
        if (faces.Count == 0) { Error = "Pick faces of the study's bodies."; return null; }
        var kind = _kind; bool lx = _x.Value, ly = _y.Value, lz = _z.Value;
        return new CommandOutput
        {
            NoOp = true, Label = Title, Icon = Icon,
            After = s => { s.Dirty=true; st.Fixtures.Add(new Fixture { Name = $"{Title} {st.Fixtures.Count(f => f.Kind == kind) + 1}", Kind = kind, Faces = faces, LockX = lx, LockY = ly, LockZ = lz }); st.Results = null; s.ShowResult(null); },
        };
    }

    public override void BuildOverlay(CadSession s, Overlay o)
    {
        var st = s.ActiveStudy; if (st == null) return;
        foreach (var it in _faces.Items.Where(i => i.Kind == SelKind.Face && st.Has(i.Obj)))
            if (s.Find(it.Obj) is { } body && StudyOverlay.FaceInfo(body, it.Index) is { } fi) o.AddDot(fi.center, 0xFF2EA043, 11);
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
    readonly VectorInput? _vec;
    readonly FaceRegionInput? _region;
    SelItem? _sizedFor;

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
        if (kind is LoadKind.Force or LoadKind.Gravity)
        {
            // one shared way to say which way something points: the first entries are the ones loads always had, then custom components, a tilt from the face and a direction taken from geometry
            _vec = new VectorInput(faceModes: kind == LoadKind.Force, defaultAxis: 5);
            _dir = Add(_vec.Kind);
            foreach (var p in _vec.Params.Skip(1)) Add(p);
            Slot(_vec.From);
        }
        else _dir = Choice("Direction", 0, "-");
        if (kind is LoadKind.Force or LoadKind.Pressure)
        {
            // where on the face it acts: all of it, a circle or a rectangle
            _region = new FaceRegionInput();
            foreach (var p in _region.Params) Add(p);
        }
        _total = Check("The force is the total over all picked faces", true);
    }

    public override string Title => _kind switch { LoadKind.Force => "Force", LoadKind.Pressure => "Pressure", LoadKind.Torque => "Torque", _ => "Gravity" };
    public override string Hint => _kind switch
    {
        LoadKind.Force => "Pick the faces the force pushes on, enter its size, and choose its direction: into the face, along an axis, a custom vector (X, Y, Z), tilted from the face normal, or along an edge or plane. It is spread evenly over the faces, or over a circle or rectangle you place on one flat face (Apply to).",
        LoadKind.Pressure => "Pick faces that are pressed on (positive pushes into the part), enter the pressure in MPa (N/mm²). To press on only part of a flat face, set Apply to a circle or rectangle.",
        LoadKind.Torque => "Pick a flat face and enter a twisting moment about its normal (N·mm). Positive turns counter-clockwise seen from outside.",
        _ => "The part's own weight. Gravity pulls along the chosen direction (an axis, a custom vector, or an edge or plane); 1 g is Earth gravity.",
    };
    public override string Icon => _kind switch { LoadKind.Pressure => "sim-pressure", LoadKind.Torque => "sim-torque", LoadKind.Gravity => "sim-gravity", _ => "sim-force" };
    public override string OkText => "Add load";
    protected override void UpdateVisibility()
    {
        if (_total == null) return;
        _dir.IsVisible = _vec != null; _total.IsVisible = _kind == LoadKind.Force;
        _vec?.UpdateVisibility();
        if (_region == null || _faces == null) return;
        _region.UpdateVisibility();
        // a starting size that suits the face just picked
        var first = _faces.Items.FirstOrDefault(i => i.Kind == SelKind.Face);
        if (first.Obj != Guid.Empty && first != _sizedFor)
        {
            _sizedFor = first;
            if (S?.Find(first.Obj) is { } body && StudyOverlay.FaceInfo(body, first.Index) is { } fi) _region.SizeFor(fi.size);
        }
    }

    /// <summary>The first picked face's centre and outward normal, for placing a region on it; null when no flat face is picked.</summary>
    (Vec3 centre, Vec3 normal, double size)? FirstFace()
    {
        var item = _faces?.Items.FirstOrDefault(i => i.Kind == SelKind.Face) ?? default;
        if (item.Obj == Guid.Empty || S.Find(item.Obj) is not { } body || StudyOverlay.FaceInfo(body, item.Index) is not { } fi) return null;
        return (fi.center, fi.normal, fi.size);
    }

    /// <summary>Outward normals of the picked faces of the study's bodies.</summary>
    List<Vec3> FaceNormals()
    {
        var list = new List<Vec3>();
        if (_faces == null) return list;
        foreach (var it in _faces.Items.Where(i => i.Kind == SelKind.Face))
            if (S.Find(it.Obj) is { } body && StudyOverlay.FaceInfo(body, it.Index) is { } fi) list.Add(fi.normal);
        return list;
    }

    protected override CommandOutput? Build()
    {
        Results.Clear();
        if (S.ActiveStudy == null) { Error = "Create a study first (Simulate > New Study)."; return null; }
        var st = S.ActiveStudy;
        var faces = _faces?.Items.Where(i => i.Kind == SelKind.Face && st.Has(i.Obj)).Select(i => new FaceRef(i.Obj, i.Index)).ToList() ?? [];
        if (_kind != LoadKind.Gravity && faces.Count == 0) { Error = null; return null; }
        var ld = new Load { Kind = _kind, Faces = faces, Value = _value.Value, Total = _total.Value };
        if (_vec != null)
        {
            var normals = FaceNormals();
            var resolved = _vec.Resolve(S, normals);
            ld.AlongNormal = _kind == LoadKind.Force && resolved.AlongNormal;
            if (!ld.AlongNormal) ld.Direction = VectorInput.Unit(resolved.Direction);
            Report(ld, normals);
        }
        if (_region is { IsWholeFace: false })
        {
            if (faces.Count != 1) throw new FormatException("An area on a face needs exactly one face. Pick one face, or set Apply to the whole face.");
            var shape = S.ShapeOf(_faces!.Items.First(i => i.Kind == SelKind.Face)) ?? throw new FormatException("That face is gone.");
            if (Occ.FaceInfoOf(shape).Type != "Plane") throw new FormatException("Loading part of a face works on flat faces for now. Curved faces are not supported yet.");
            var face = FirstFace() ?? throw new FormatException("Pick a face first.");
            _region.Resolve(ld, face.centre, face.normal);
            Results.Add(new ResultLine("Loaded area", $"{ld.RegionArea:0.##} mm² ({(ld.Region == LoadRegionShape.Circle ? "circle" : "rectangle")}; only the part on the face carries load)"));
            if (_kind == LoadKind.Pressure) Results.Add(new ResultLine("Force from the pressure", $"about {ld.Value * ld.RegionArea:0.##} N"));
            else if (ld.RegionArea > 0) Results.Add(new ResultLine("Average pressure on the area", $"{ld.Value / ld.RegionArea:0.###} MPa"));
        }
        ld.Name = $"{Title} {st.Loads.Count(l => l.Kind == _kind) + 1}";
        return new CommandOutput { NoOp = true, Label = Title, Icon = Icon, After = s => { s.Dirty=true; st.Loads.Add(ld); st.Results = null; s.ShowResult(null); } };
    }

    /// <summary>What the solver will be given, in plain numbers: the vector, and for a flat face how much of it pushes and how much slides.</summary>
    void Report(Load ld, IReadOnlyList<Vec3> normals)
    {
        if (_kind == LoadKind.Gravity)
        {
            var a = ld.Direction * (9810.0 * ld.Value);
            Results.Add(new ResultLine("Direction", VectorInput.Describe(ld.Direction)));
            Results.Add(new ResultLine("Acceleration", $"({a.X:0.##}, {a.Y:0.##}, {a.Z:0.##}) mm/s²"));
            return;
        }
        if (ld.AlongNormal) { Results.Add(new ResultLine("Direction", "into each face, along its own normal")); return; }
        var f = ld.Direction * ld.Value;
        Results.Add(new ResultLine("Direction", VectorInput.Describe(ld.Direction)));
        Results.Add(new ResultLine(ld.Total ? "Force vector" : "Force vector on each face", $"({f.X:0.##}, {f.Y:0.##}, {f.Z:0.##}) N"));
        if (normals.Count > 0 && normals.All(n => Vec3.Dot(n.Normalized(), normals[0].Normalized()) > 0.9999))
        {
            var (into, shear) = VectorInput.Split(ld.Direction, normals[0], ld.Value);
            Results.Add(new ResultLine(into >= 0 ? "Pushing into the face" : "Pulling out of the face", $"{Math.Abs(into):0.##} N"));
            Results.Add(new ResultLine("Sliding across the face", $"{shear:0.##} N"));
        }
    }

    /// <summary>The tilt can be dragged: a turning handle on the first picked face, swinging the load away from straight-in.</summary>
    public override IReadOnlyList<HandleSpec> Handles
    {
        get
        {
            if (_faces == null || FirstFace() is not { } face) return [];
            var list = new List<HandleSpec>();
            try
            {
                if (_vec is { Mode: VectorInput.Source.Tilted })
                {
                    var into = -face.normal.Normalized();
                    var lean = VectorInput.Tilted(face.normal, 90, _vec.Around.Value);     // the in-face direction the load leans toward
                    list.Add(new HandleSpec(face.centre, Vec3.Cross(into, lean), _vec.Tilt, 1, into, Math.Max(face.size * 0.8, 6)));
                }
                if (_region != null) list.AddRange(_region.Handles(face.centre, face.normal));
            }
            catch (Exception e) when (e is FormatException or KernelException) { }
            return list;
        }
    }
    public override HandleSpec? Handle => Handles.Count > 0 ? Handles[0] : null;

    public override void BuildOverlay(CadSession s, Overlay o)
    {
        var st = s.ActiveStudy; if (st == null || _faces == null) return;
        Vec3[]? fixedDirection = null; bool alongNormal = false;
        try { if (_vec != null) { var r = _vec.Resolve(s, FaceNormals()); alongNormal = r.AlongNormal; if (!alongNormal) fixedDirection = [VectorInput.Unit(r.Direction)]; } }
        catch (FormatException) { }
        foreach (var it in _faces.Items.Where(i => i.Kind == SelKind.Face && st.Has(i.Obj)))
            if (s.Find(it.Obj) is { } body && StudyOverlay.FaceInfo(body, it.Index) is { } fi)
            {
                // where it acts: the whole face (a dot at its middle) or the area you chose (its outline, and the arrow lands in its middle)
                var at = fi.center;
                if (_region is { IsWholeFace: false } && FirstFace() is { } first && first.centre.DistanceTo(fi.center) < 1e-9)
                {
                    var outline = _region.Outline(fi.center, fi.normal);
                    if (outline.Count > 2) { o.AddPolyline(outline, 0xFFE5484D, 2.6f, true); at = outline.Aggregate(Vec3.Zero, (a, p) => a + p) / outline.Count; }
                }
                o.AddDot(at, 0xFFE5484D, 11);
                // the direction the load will act, drawn where it will be applied
                var dir = alongNormal ? -fi.normal.Normalized() : fixedDirection?[0];
                if (_kind == LoadKind.Force && dir is { } d) StudyOverlay.Arrow(o, at - d * Math.Max(fi.size * 0.7, 5), at, Math.Max(fi.size * 0.12, 1), 0xFFE5484D, 3f);
            }
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
