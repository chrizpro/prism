using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core;
using Prism.Kernel;

namespace Prism.Commands;

/// <summary>Base for read-only tools: they fill <see cref="Command.Results"/> and never change the document.</summary>
public abstract class AnalysisCommand : Command
{
    public override bool SupportsPreview => false;
    public override string OkText => "Done";
    public override string Icon => "analyze";
    protected abstract void Analyze();

    protected override CommandOutput? Build()
    {
        Results.Clear();
        Analyze();
        return new CommandOutput { NoOp = true };
    }

    protected void Row(string label, string value, int level = 0) => Results.Add(new ResultLine(label, value, level));
    protected static string N(double v, string unit = "") => $"{v:#,0.###}{(unit.Length > 0 ? " " + unit : "")}";
    protected static string P(Vec3 p) { static double Z(double v) => Math.Abs(v) < 5e-4 ? 0 : v; return $"{Z(p.X):0.###},  {Z(p.Y):0.###},  {Z(p.Z):0.###}"; }

    /// <summary>The objects to analyse: the slot's picks, or every visible body when nothing is picked.</summary>
    protected List<SceneObject> Targets(SelectionSlot slot, Func<SceneObject, bool> accept)
    {
        var picked = ObjectsOf(slot).Where(accept).Distinct().ToList();
        return picked.Count > 0 ? picked : S.Objects.Where(o => o.Visible && accept(o)).ToList();
    }

    protected static bool IsBody(SceneObject o) => o.Kind is ObjKind.Solid or ObjKind.Surface or ObjKind.Form;
}

// ================================================================= Measure
public sealed record MeasureItem(string Label, SelItem? Sel, Vec3 Point, Shape Shape, bool IsPoint);

public sealed class MeasureCommand : AnalysisCommand
{
    public readonly ChoiceParam PickMode;
    public List<MeasureItem> Picks { get; } = [];
    (Vec3 a, Vec3 b)? _closest;

    public MeasureCommand()
    {
        PickMode = Choice("Pick", 0, "Faces and edges", "Whole bodies", "Points on surfaces", "Add up several (areas, lengths, volumes)");
        Add(new ActionParam { Label = "Clear picks", Run = () => { Picks.Clear(); OnChanged(); } });
        Tool = new MeasureTool(this);
    }

    public override string Title => "Measure";
    public override string Hint => "Click a face, edge or body to see its size. Click a second one for the distance and angle between them. Hold Alt to measure a point.";

    public SelFilter Filter => PickMode.Index switch { 1 => SelFilter.AnyObject, 2 => SelFilter.Face | SelFilter.Edge, 3 => SelFilter.Face | SelFilter.Edge | SelFilter.AnyObject, _ => SelFilter.Face | SelFilter.Edge };
    public bool Multi => PickMode.Index == 3;
    public bool PointMode => PickMode.Index == 2;

    public void AddPick(PickResult r, bool forcePoint)
    {
        if (Picks.Count >= 2 && !Multi || Picks.Count >= 60) Picks.Clear();
        MeasureItem item;
        if (PointMode || forcePoint)
            item = new MeasureItem($"Point ({r.Point.X:0.##}, {r.Point.Y:0.##}, {r.Point.Z:0.##})", null, r.Point, Occ.Vertex(r.Point), true);
        else
        {
            var shape = S.ShapeOf(r.Item) ?? throw new KernelException("Nothing to measure there.");
            item = new MeasureItem(S.Describe(r.Item), r.Item, r.Point, shape, false);
        }
        Picks.Add(item);
        S.SelectItems(Picks.Where(p => p.Sel != null).Select(p => p.Sel!.Value));
        OnChanged();
    }

    public void SetPicks(IEnumerable<MeasureItem> items) { Picks.Clear(); Picks.AddRange(items.Take(Multi ? 60 : 2)); OnChanged(); }

    /// <summary>Several picks at once: each one's size, then the totals (areas of faces, lengths of edges, volumes of bodies).</summary>
    void AnalyzeMany()
    {
        double area = 0, length = 0, volume = 0, bodyArea = 0; int faces = 0, edges = 0, bodies = 0, points = 0;
        Vec3 lo = new(double.MaxValue, double.MaxValue, double.MaxValue), hi = -lo;
        for (int i = 0; i < Picks.Count; i++)
        {
            var it = Picks[i]; string tag = $"{i + 1}. ";
            if (it.IsPoint) { points++; Row(tag + "Point", P(it.Point)); continue; }
            var sel = it.Sel!.Value;
            try
            {
                if (sel.Kind == SelKind.Face) { var f = Occ.FaceInfoOf(it.Shape); faces++; area += f.Area; Row(tag + it.Label, N(f.Area, "mmÂ²")); }
                else if (sel.Kind == SelKind.Edge) { var e = Occ.EdgeInfoOf(it.Shape); edges++; length += e.Length; Row(tag + it.Label, N(e.Length, "mm")); }
                else { var m = Occ.Mass(it.Shape); bodies++; bodyArea += m.Area; if (m.Solid) { volume += m.Volume; Row(tag + it.Label, N(m.Volume, "mmÂ³")); } else Row(tag + it.Label, N(m.Area, "mmÂ²")); }
                var p = it.Shape.Props(); lo = new Vec3(Math.Min(lo.X, p.Min.X), Math.Min(lo.Y, p.Min.Y), Math.Min(lo.Z, p.Min.Z)); hi = new Vec3(Math.Max(hi.X, p.Max.X), Math.Max(hi.Y, p.Max.Y), Math.Max(hi.Z, p.Max.Z));
            }
            catch (KernelException) { Row(tag + it.Label, "could not measure"); }
        }
        Row("", "");
        Row("Picked", $"{Picks.Count} item(s)", 1);
        if (faces > 0) { Row($"Total area of {faces} face(s)", N(area, "mmÂ²"), 1); Row("   Average", N(area / faces, "mmÂ²")); }
        if (edges > 0) { Row($"Total length of {edges} edge(s)", N(length, "mm"), 1); Row("   Average", N(length / edges, "mm")); }
        if (bodies > 0) { if (volume > 0) Row($"Total volume of {bodies} body(ies)", N(volume, "mmÂ³"), 1); Row("   Total surface area", N(bodyArea, "mmÂ²")); }
        if (hi.X >= lo.X) Row("Overall size", $"{hi.X - lo.X:0.##} Ã— {hi.Y - lo.Y:0.##} Ã— {hi.Z - lo.Z:0.##} mm");
        if (Picks.Count >= 2)
        {
            var (d, a, b) = Occ.Distance(Picks[0].Shape, Picks[1].Shape);
            _closest = (a, b);
            Row("Distance, 1 to 2", N(d, "mm"), 1); Row("   Î”X", N(b.X - a.X, "mm")); Row("   Î”Y", N(b.Y - a.Y, "mm")); Row("   Î”Z", N(b.Z - a.Z, "mm"));
        }
    }

    void Describe(MeasureItem it, string prefix)
    {
        if (it.IsPoint) { Row(prefix + "Point", P(it.Point)); return; }
        var sel = it.Sel!.Value;
        if (sel.Kind == SelKind.Face)
        {
            var f = Occ.FaceInfoOf(it.Shape);
            Row(prefix + it.Label, f.Type);
            Row("   Area", N(f.Area, "mm²"));
            if (f.Type == "Cylinder" || f.Type == "Sphere") { Row("   Radius", N(f.R1, "mm")); Row("   Diameter", N(2 * f.R1, "mm")); }
            else if (f.Type == "Cone") { Row("   Base radius", N(f.R1, "mm")); Row("   Half-angle", N(f.R2 * 180 / Math.PI, "°")); }
            else if (f.Type == "Torus") { Row("   Major radius", N(f.R1, "mm")); Row("   Minor radius", N(f.R2, "mm")); }
            else if (f.Type == "Plane") Row("   Normal", $"{f.Normal.X:0.###}, {f.Normal.Y:0.###}, {f.Normal.Z:0.###}");
        }
        else if (sel.Kind == SelKind.Edge)
        {
            var e = Occ.EdgeInfoOf(it.Shape);
            Row(prefix + it.Label, e.Type);
            Row("   Length", N(e.Length, "mm"));
            if (e.Type == "Circle") { Row("   Radius", N(e.Radius, "mm")); Row("   Diameter", N(2 * e.Radius, "mm")); }
            Row("   From", P(e.Start)); Row("   To", P(e.End));
        }
        else
        {
            var m = Occ.Mass(it.Shape); var p = it.Shape.Props();
            Row(prefix + it.Label, m.Solid ? "Solid" : "Surface");
            if (m.Solid) Row("   Volume", N(m.Volume, "mm³"));
            Row("   Area", N(m.Area, "mm²"));
            Row("   Size", $"{p.Size.X:0.##} × {p.Size.Y:0.##} × {p.Size.Z:0.##} mm");
        }
    }

    protected override void Analyze()
    {
        _closest = null;
        if (Picks.Count == 0) { Row("Nothing picked", "Click geometry in the viewport"); return; }
        if (Multi) { AnalyzeMany(); return; }
        Describe(Picks[0], Picks.Count == 2 ? "A: " : "");
        if (Picks.Count < 2) { Row("Next", "Click a second item to measure between them", 0); return; }
        Row("", "");
        Describe(Picks[1], "B: ");
        Row("", "");
        var (d, a, b) = Occ.Distance(Picks[0].Shape, Picks[1].Shape);
        _closest = (a, b);
        Row("Distance", N(d, "mm"), 1);
        Row("   ΔX", N(b.X - a.X, "mm")); Row("   ΔY", N(b.Y - a.Y, "mm")); Row("   ΔZ", N(b.Z - a.Z, "mm"));
        var dirA = DirectionOf(Picks[0]); var dirB = DirectionOf(Picks[1]);
        if (dirA is { } da && dirB is { } db)
        {
            double dot = Math.Clamp(Math.Abs(Vec3.Dot(da.v, db.v)), 0, 1);
            if (da.isNormal && db.isNormal) { double ang = Math.Acos(dot) * 180 / Math.PI; Row("Angle between faces", N(ang, "°")); if (ang < 0.01) Row("   The faces are parallel", ""); }
            else if (!da.isNormal && !db.isNormal) Row("Angle between lines", N(Math.Acos(dot) * 180 / Math.PI, "°"));
            else Row("Angle line to face", N(Math.Asin(dot) * 180 / Math.PI, "°"));
        }
    }

    /// <summary>A plane's normal, or a straight edge's direction.</summary>
    (Vec3 v, bool isNormal)? DirectionOf(MeasureItem it)
    {
        if (it.IsPoint || it.Sel is not { } sel) return null;
        try
        {
            if (sel.Kind == SelKind.Face) { var f = Occ.FaceInfoOf(it.Shape); return f.Type == "Plane" ? (f.Normal, true) : null; }
            if (sel.Kind == SelKind.Edge) { var e = Occ.EdgeInfoOf(it.Shape); return e.Type == "Line" ? ((e.End - e.Start).Normalized(), false) : null; }
        }
        catch (KernelException) { }
        return null;
    }

    public override void BuildOverlay(CadSession s, Overlay o)
    {
        foreach (var p in Picks) o.AddDot(p.Point, 0xFFFF8A1F, 11);
        if (_closest is { } c)
        {
            o.AddLine(c.a, c.b, 0xFF35B6FF, 2.4f); o.AddDot(c.a, 0xFF35B6FF, 9); o.AddDot(c.b, 0xFF35B6FF, 9);
            o.AddLabel((c.a + c.b) * 0.5, $"{c.a.DistanceTo(c.b):0.###} mm", true, 1);
        }
    }
}

public sealed class MeasureTool(MeasureCommand cmd) : IViewportTool
{
    public string Prompt => "Measure: click geometry. A second click gives the distance and angle. Alt = measure the exact clicked point.";
    public bool PointerDown(CadSession s, PointerInfo p)
    {
        if (!p.Left) return false;
        var hit = s.Pick(p.X, p.Y, cmd.Filter);
        if (hit == null) { cmd.Picks.Clear(); cmd.OnChanged(); return true; }
        try { cmd.AddPick(hit.Value, p.Alt); }
        catch (KernelException e) { s.Say(e.Message, true); }
        return true;
    }
    public void PointerMove(CadSession s, PointerInfo p)
    {
        var r = s.Pick(p.X, p.Y, cmd.Filter);
        var h = r?.Item;
        if (h != s.Hover) { s.Hover = h; s.Invalidate(); }
    }
    public void PointerUp(CadSession s, PointerInfo p) { }
    public bool Key(CadSession s, string key) => false;
    public void BuildOverlay(CadSession s, Overlay o) { }
    public void Deactivate(CadSession s) { s.Hover = null; }
}

// ================================================================= Mass properties
public sealed class PropertiesCommand : AnalysisCommand
{
    static readonly (string name, double density)[] Materials =
    [
        ("Use each body's material", -1), ("Steel", 7.85), ("Stainless steel", 8.0), ("Aluminum", 2.70), ("Titanium", 4.51), ("Copper", 8.96), ("Brass", 8.50),
        ("ABS plastic", 1.05), ("PLA", 1.24), ("Nylon", 1.14), ("Wood (oak)", 0.75), ("Water", 1.00), ("Custom", 1.00),
    ];
    readonly SelectionSlot _bodies;
    readonly ChoiceParam _material;
    readonly NumberParam _density;
    (Vec3 cog, Vec3 min, Vec3 max)? _marker;

    public PropertiesCommand()
    {
        _bodies = Slot(new SelectionSlot { Label = "Bodies (none = all)", Filter = SelFilter.Solid | SelFilter.SurfaceObj | SelFilter.Form, Multi = true, Optional = true });
        _material = Choice("Material", 0, Materials.Select(m => m.name).ToArray());
        _density = Num("Density", 7.85, 0.0001, 100, 0.1, "g/cm³");
        _density.IsVisible = false;
        _material.Changed = () =>
        {
            bool own = _material.Index == 0;
            if (!own && _material.Index < Materials.Length - 1) _density.Value = Materials[_material.Index].density;
            _density.IsVisible = !own;
            OnChanged();
        };
    }

    public override string Title => "Properties";
    public override string Hint => "Volume, mass, center of mass and moments of inertia of the selected bodies (or everything if nothing is selected).";

    protected override void Analyze()
    {
        _marker = null;
        var objs = Targets(_bodies, IsBody);
        if (objs.Count == 0) { Row("No bodies", "Create or select a body first", 2); return; }
        double vol = 0, area = 0, wsum = 0, mass = 0; Vec3 cog = Vec3.Zero, min = new(double.MaxValue, double.MaxValue, double.MaxValue), max = -min; Occ_Moments? single = null;
        bool anySolid = false;
        foreach (var o in objs)
        {
            var m = Occ.Mass(o.Shape); var p = o.Shape.Props();
            vol += m.Volume; area += m.Area; anySolid |= m.Solid;
            // each body weighs what its own material says (steel when it has none), unless a fixed material was chosen here
            double rhoBody = (_material.Index == 0 ? (o.Material?.Density ?? 7.85) : _density.Value) / 1000.0;
            if (m.Solid) mass += m.Volume * rhoBody;
            double w = m.Solid ? m.Volume * rhoBody : m.Area;
            cog += m.Cog * w; wsum += w;
            min = new(Math.Min(min.X, p.Min.X), Math.Min(min.Y, p.Min.Y), Math.Min(min.Z, p.Min.Z));
            max = new(Math.Max(max.X, p.Max.X), Math.Max(max.Y, p.Max.Y), Math.Max(max.Z, p.Max.Z));
            if (objs.Count == 1) single = new Occ_Moments(m.Moments);
        }
        if (wsum > 0) cog /= wsum;
        double rho = (_material.Index == 0 && objs.Count == 1 ? (objs[0].Material?.Density ?? 7.85) : _density.Value) / 1000.0;               // g/mm³ (moments of a single body)
        Row("Bodies", objs.Count.ToString());
        if (anySolid)
        {
            Row("Volume", $"{N(vol, "mm³")}   ({vol / 1000:#,0.###} cm³)");
            Row("Mass", $"{mass:#,0.###} g   ({mass / 1000:#,0.####} kg)", 1);
            if (_material.Index == 0 && objs.Count == 1) Row("Material", objs[0].Material?.Name ?? "none set - using steel (7.85 g/cm³)");
        }
        else Row("Mass", "n/a - open surfaces have no volume", 2);
        Row("Surface area", N(area, "mm²"));
        Row("Center of mass", P(cog));
        if (single is { } mo && anySolid)
        {
            Row("Principal moments", "about the center of mass (g·mm²)");
            Row("   Iₓ", N(mo.M.X * rho)); Row("   Iᵧ", N(mo.M.Y * rho)); Row("   I_z", N(mo.M.Z * rho));
        }
        var sz = max - min;
        Row("Bounding box", $"{sz.X:0.##} × {sz.Y:0.##} × {sz.Z:0.##} mm");
        _marker = (cog, min, max);
    }

    readonly record struct Occ_Moments(Vec3 M);

    public override void BuildOverlay(CadSession s, Overlay o)
    {
        if (_marker is not { } m) return;
        CadSession.AddBox(o, m.min, m.max, 0xFFFFC53D);
        o.AddDot(m.cog, 0xFFFF8A1F, 14);
        double ps = s.Camera.PixelSizeAt(m.cog) * 22;
        o.AddLine(m.cog - Vec3.UnitX * ps, m.cog + Vec3.UnitX * ps, 0xFFE5484D, 2f); o.AddLine(m.cog - Vec3.UnitY * ps, m.cog + Vec3.UnitY * ps, 0xFF3FCF8E, 2f); o.AddLine(m.cog - Vec3.UnitZ * ps, m.cog + Vec3.UnitZ * ps, 0xFF4C94FF, 2f);
        o.AddLabel(m.cog, "Center of mass", true, 0);
    }
}

// ================================================================= Section analysis
public sealed class SectionCommand : AnalysisCommand
{
    readonly SelectionSlot _bodies;
    readonly SelectionSlot _customPlane;
    readonly ChoiceParam _plane;
    readonly NumberParam _offset;
    readonly BoolParam _flip, _keep;
    readonly List<List<Vec3>> _curves = [];
    (Vec3 origin, Vec3 normal)? _cut; double _size = 60;
    int _previousPlane = -1;

    public SectionCommand()
    {
        _bodies = Slot(new SelectionSlot { Label = "Bodies (none = all)", Filter = SelFilter.Solid | SelFilter.SurfaceObj | SelFilter.Form, Multi = true, Optional = true });
        _plane = Choice("Section plane", 0, "XY (cut across Z)", "XZ (cut across Y)", "YZ (cut across X)", "Custom plane or planar face");
        _customPlane = Slot(new SelectionSlot { Label = "Custom plane or planar face", Filter = SelFilter.Plane | SelFilter.Face, IsVisible = false, Optional = true });
        _offset = Num("Offset", 0);
        _flip = Check("Flip side", false);
        _keep = Check("Keep section view when done", true);
        _plane.Changed = () =>
        {
            if (_plane.Index == 3) { _offset.Value = 0; _bodies.IsActive = false; _customPlane.IsActive = true; }
            else { _customPlane.IsActive = false; _bodies.IsActive = true; }
            OnChanged();
        };
    }

    public override string Title => "Section";
    public override string Hint => "Cut the model with a plane to look inside; the cut outline, area and perimeter are reported. Drag the offset to scan through the part.";
    public override string Icon => "section";

    protected override void OnBegin()
    {
        var bodies = S.Objects.Where(o => o.Visible && IsBody(o)).ToList();
        if (bodies.Count > 0) _offset.Value = (bodies.Min(o => o.Mesh.Min.Z) + bodies.Max(o => o.Mesh.Max.Z)) / 2;
    }
    protected override void UpdateVisibility()
    {
        if (_customPlane == null) return;
        _customPlane.IsVisible = _plane.Index == 3;
        if (_previousPlane != _plane.Index)
        {
            _previousPlane = _plane.Index;
            _bodies.IsActive = _plane.Index != 3;
            _customPlane.IsActive = _plane.Index == 3;
        }
    }

    public override void OnEnd() { if (!_keep.Value) { S.Clip = null; S.Invalidate(); } }
    public override void OnCancel() { S.Clip = null; S.Invalidate(); }

    public override HandleSpec? Handle
    {
        get
        {
            if (_plane.Index == 3)
            {
                if (_customPlane.Items.Count == 0) return null;
                var plane = S.PlaneOf(_customPlane.Items[0]); if (plane == null) return null;
                return new HandleSpec(plane.Value.Origin, plane.Value.Normal, _offset);
            }
            var n = _plane.Index == 0 ? Vec3.UnitZ : _plane.Index == 1 ? Vec3.UnitY : Vec3.UnitX;
            return new HandleSpec(Vec3.Zero, n, _offset);
        }
    }

    protected override void Analyze()
    {
        _curves.Clear(); _cut = null;
        var n = _plane.Index == 0 ? Vec3.UnitZ : _plane.Index == 1 ? Vec3.UnitY : Vec3.UnitX;
        var origin = n * _offset.Value;
        if (_plane.Index == 3)
        {
            if (_customPlane.Items.Count == 0) { S.Clip = null; Row("Section", "Select a custom plane or planar face.", 2); S.Invalidate(); return; }
            var item = _customPlane.Items[0];
            if (item.Kind == SelKind.Face && S.ShapeOf(item) is { } face && Occ.FaceInfoOf(face).Type != "Plane") throw new FormatException("Select a planar face or construction plane.");
            var plane = S.PlaneOf(item) ?? throw new FormatException("Select a planar face or construction plane.");
            n = plane.Normal; origin = plane.Origin + n * _offset.Value;
        }
        S.Clip = new CadSession.ClipPlane(origin, _flip.Value ? -n : n);
        _cut = (origin, n);
        var objs = Targets(_bodies, IsBody);
        double area = 0, perim = 0; int cuts = 0;
        foreach (var o in objs)
        {
            try
            {
                var sec = Occ.Section(o.Shape, origin, n);
                var mesh = sec.Tessellate();
                if (mesh.EdgePoints.Length == 0) continue;
                for (int i = 0; i + 5 < mesh.EdgePoints.Length; i += 6)
                {
                    var a = new Vec3(mesh.EdgePoints[i], mesh.EdgePoints[i + 1], mesh.EdgePoints[i + 2]); var b = new Vec3(mesh.EdgePoints[i + 3], mesh.EdgePoints[i + 4], mesh.EdgePoints[i + 5]);
                    _curves.Add([a, b]); perim += a.DistanceTo(b);
                }
                var prof = Occ.Profile([sec], true);
                if (prof.HasFaces) area += prof.Props().Area;
                cuts++;
            }
            catch (KernelException) { }
        }
        foreach (var o in objs) { var m = o.Mesh; _size = Math.Max(_size, Math.Max((m.Max - m.Min).Length, 1)); }
        if (cuts == 0) { Row("Section", "The plane does not cut anything - move the offset", 2); return; }
        Row("Bodies cut", cuts.ToString());
        Row("Section area", N(area, "mm²"), 1);
        Row("Outline length", N(perim, "mm"));
    }

    public override void BuildOverlay(CadSession s, Overlay o)
    {
        foreach (var seg in _curves) o.AddLine(seg[0], seg[1], 0xFFFFC53D, 3.2f);
        if (_cut is { } c)
        {
            var pl = c.normal == Vec3.UnitZ ? Plane3.XY : c.normal == Vec3.UnitY ? Plane3.XZ : Plane3.YZ;
            Vec3 P(double u, double v) => c.origin + pl.U * (u * _size * 0.7) + pl.V * (v * _size * 0.7);
            uint fill = 0x1AFFC53D;
            o.AddTri(P(-1, -1), P(1, -1), P(1, 1), fill); o.AddTri(P(-1, -1), P(1, 1), P(-1, 1), fill);
            o.AddPolyline([P(-1, -1), P(1, -1), P(1, 1), P(-1, 1)], 0x90FFC53D, 1.4f, true);
        }
    }
}

// ================================================================= Interference
public sealed class InterferenceCommand : AnalysisCommand
{
    readonly SelectionSlot _bodies;
    readonly BoolParam _create;

    public InterferenceCommand()
    {
        _bodies = Slot(new SelectionSlot { Label = "Bodies to check (none = all solids)", Filter = SelFilter.Solid | SelFilter.Form, Multi = true, Optional = true });
        _create = Check("Create the overlapping volumes as bodies", false);
    }

    public override string Title => "Interference";
    public override string Hint => "Find where bodies overlap. Pick bodies, or leave empty to check every solid against every other.";
    public override string Icon => "interference";

    protected override CommandOutput? Build()
    {
        Results.Clear();
        var objs = Targets(_bodies, o => o.Kind is ObjKind.Solid or ObjKind.Form);
        var o2 = new CommandOutput { NoOp = !_create.Value, Label = "Interference bodies", Icon = "intersect" };
        if (objs.Count < 2) { Row("Need two bodies", "Pick at least two solids", 2); return o2; }
        int pairs = 0, hits = 0;
        for (int i = 0; i < objs.Count; i++)
            for (int j = i + 1; j < objs.Count; j++)
            {
                pairs++;
                try
                {
                    var common = Occ.Common(objs[i].Shape, objs[j].Shape);
                    double v = common.HasSolids ? common.Props().Volume : 0;
                    if (v > 1e-6)
                    {
                        hits++;
                        Row($"{objs[i].Name} ∩ {objs[j].Name}", N(v, "mm³"), 3);
                        o2.Add.Add(new(common, "Interference", ObjKind.Solid));
                    }
                }
                catch (KernelException) { Row($"{objs[i].Name} / {objs[j].Name}", "could not be checked", 2); }
            }
        if (hits == 0) Row("Result", $"No interference between {objs.Count} bodies ({pairs} pair{(pairs == 1 ? "" : "s")} checked)", 1);
        else Row("Overlapping pairs", $"{hits} of {pairs}", 3);
        return o2;
    }

    protected override void Analyze() { }
}

// ================================================================= Geometry check
public sealed class CheckCommand : AnalysisCommand
{
    readonly SelectionSlot _bodies;
    readonly List<(SceneObject obj, int[] edges)> _free = [];

    public CheckCommand() { _bodies = Slot(new SelectionSlot { Label = "Bodies (none = all)", Filter = SelFilter.Solid | SelFilter.SurfaceObj | SelFilter.Form, Multi = true, Optional = true }); }
    public override string Title => "Check Geometry";
    public override string Hint => "Test bodies for validity and open edges. Open edges are drawn in red.";
    public override string Icon => "check";

    protected override void Analyze()
    {
        _free.Clear();
        var objs = Targets(_bodies, IsBody);
        if (objs.Count == 0) { Row("No bodies", "Create or select a body first", 2); return; }
        int bad = 0;
        foreach (var o in objs)
        {
            try
            {
                var c = Occ.Check(o.Shape);
                bool closed = c.FreeEdges == 0 && c.Faces > 0;
                bool fine = c.Valid && (closed || o.Kind == ObjKind.Surface);
                if (!fine) bad++;
                Row(o.Name, fine ? "OK" : "Problems found", fine ? 1 : 3);
                Row("   Geometry", c.Valid ? "Valid" : "Invalid (self-intersections or bad edges)", c.Valid ? 1 : 3);
                Row("   Watertight", closed ? "Yes (closed)" : $"No - {c.FreeEdges} open edge{(c.FreeEdges == 1 ? "" : "s")}", closed ? 1 : o.Kind == ObjKind.Surface ? 0 : 3);
                Row("   Contents", $"{c.Solids} solid, {c.Shells} shell, {c.Faces} faces, {c.Edges} edges");
                if (c.FreeEdges > 0) _free.Add((o, Occ.FreeEdges(o.Shape)));
            }
            catch (KernelException e) { bad++; Row(o.Name, e.Message, 3); }
        }
        Row("Summary", bad == 0 ? $"All {objs.Count} bod{(objs.Count == 1 ? "y is" : "ies are")} healthy" : $"{bad} of {objs.Count} need attention", bad == 0 ? 1 : 3);
    }

    public override void BuildOverlay(CadSession s, Overlay o)
    {
        foreach (var (obj, edges) in _free)
        {
            var m = obj.Mesh;
            foreach (var e in edges)
            {
                if (e + 1 >= m.EdgeSegStart.Length) continue;
                for (int sg = m.EdgeSegStart[e]; sg < m.EdgeSegStart[e + 1]; sg++)
                {
                    var E = m.EdgePoints;
                    o.AddLine(new(E[6 * sg], E[6 * sg + 1], E[6 * sg + 2]), new(E[6 * sg + 3], E[6 * sg + 4], E[6 * sg + 5]), 0xFFFF4D4D, 4.5f);
                }
            }
        }
    }
}

// ================================================================= Curvature map
public sealed class CurvatureCommand : AnalysisCommand
{
    readonly BoolParam _auto;
    readonly NumberParam _radius;
    public CurvatureCommand()
    {
        _auto = Check("Automatic range", true);
        _radius = Num("Saturate at radius", 25, 0.01, 1e6, 1);
    }
    public override string Title => "Curvature Map";
    public override string Hint => "Colors surfaces by how sharply they bend: blue = concave, green = flat, red = convex. Smooth gradients mean fair surfaces.";
    public override string Icon => "curvature";
    protected override void UpdateVisibility() { _radius.IsVisible = !_auto.Value; }
    protected override void Analyze()
    {
        S.Analysis = CadSession.AnalysisKind.Curvature;
        S.CurvRange = _auto.Value ? 0 : 1.0 / _radius.Value;
        Row("Legend", "red = convex, green = flat, blue = concave");
        Row("Range", _auto.Value ? "automatic" : $"±1/{_radius.Value:0.##} mm");
        Row("Tip", "Use Clear Analysis to return to normal shading", 0);
    }
    public override void OnCancel() { S.Analysis = CadSession.AnalysisKind.None; }
}

// ================================================================= Draft analysis
public sealed class DraftCommand : AnalysisCommand
{
    readonly ChoiceParam _dir;
    readonly NumberParam _min;
    static readonly Vec3[] Dirs = [Vec3.UnitZ, -Vec3.UnitZ, Vec3.UnitX, -Vec3.UnitX, Vec3.UnitY, -Vec3.UnitY];
    public DraftCommand()
    {
        _dir = Choice("Pull direction", 0, "Z (up)", "-Z (down)", "X", "-X", "Y", "-Y");
        _min = Num("Minimum draft angle", 3, 0, 45, 0.5, "°");
    }
    public override string Title => "Draft Analysis";
    public override string Hint => "Checks whether faces can release from a mold in the pull direction.";
    public override string Icon => "draft";
    protected override void Analyze()
    {
        S.Analysis = CadSession.AnalysisKind.Draft;
        S.DraftDir = Dirs[_dir.Index]; S.DraftMin = _min.Value;
        Row($"Green", $"draft of at least {_min.Value:0.#}° - releases cleanly", 1);
        Row("Yellow / orange", "less than the minimum - may drag or stick", 2);
        Row("Red", "undercut: faces away from the pull direction", 3);
        Row("Blue", "faces perpendicular to the pull (top or bottom)");
    }
    public override void OnCancel() { S.Analysis = CadSession.AnalysisKind.None; }
}
