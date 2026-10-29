using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core;
using Prism.Core.Form;
using Prism.Kernel;

namespace Prism.Commands;

/// <summary>Commands whose result can be a new body or be combined (join / cut / intersect) with an existing one.</summary>
public abstract class BodyCommand : Command
{
    protected ChoiceParam? Op;
    protected SelectionSlot? Target;
    /// <summary>The command chooses New body or Cut by itself, from where the shape lands (until the user picks an option).</summary>
    protected bool AutoOp;
    bool _manualOp; int _autoIdx = -1;
    /// <summary>True when Cut had to point the other way to reach the body.</summary>
    protected bool FlippedForCut;

    protected void AddOperation(bool auto = false)
    {
        AutoOp = auto;
        Op = Add(new ChoiceParam { Label = "Operation", Options = ["New body", "Join", "Cut", "Intersect"], IsResult = true, IsIllustrated = true });
        // anything that changes the option from outside (the user, a script) is a deliberate choice: the automatic pick must not override it
        var prev = Op.Changed; Op.Changed = () => { _manualOp = true; prev?.Invoke(); };
        Target = Slot(new SelectionSlot { Label = "Target body (found automatically)", Caption = "Target body", Filter = SelFilter.Solid, IsVisible = false, Optional = true, IsResult = true });
    }

    protected override void UpdateVisibility()
    {
        if (Op == null || Target == null) return;
        bool show = Op.Index != 0;
        if (Target.IsVisible != show) { Target.IsVisible = show; if (!show) Target.IsActive = false; }
    }

    protected IEnumerable<SceneObject> Bodies() => S.Objects.Where(o => o.Kind == ObjKind.Solid && o.Visible);

    SceneObject? NearestBody(Shape tool)
    {
        SceneObject? best = null; double bd = 1e-3;
        foreach (var b in Bodies())
        {
            try { var d = Occ.Distance(b.Shape, tool).Distance; if (d <= bd) { bd = d; best = b; } }
            catch (Exception e) when (e is not OutOfMemoryException) { }
        }
        return best;
    }

    static double Overlap(SceneObject body, Shape tool)
    {
        try { return Math.Abs(Occ.Mass(Occ.Common(body.Shape, tool)).Volume); }
        catch (Exception e) when (e is not OutOfMemoryException) { return 0; }
    }

    SceneObject ResolveTarget(Shape tool)
    {
        if (ObjectsOf(Target!).FirstOrDefault() is { } picked) return picked;
        if (NearestBody(tool) is { } near) return near;
        var all = Bodies().ToList();
        if (all.Count == 1) return all[0];
        throw new KernelException("No body found to combine with. Pick the target body.");
    }

    protected CommandOutput Finish(Shape tool, string name, ObjKind? kind = null, Func<Shape>? reversed = null)
    {
        var o = new CommandOutput(); FlippedForCut = false;
        if (Op == null) { o.Add.Add(new(tool, name, kind)); return o; }
        if (AutoOp)
        {
            if (_autoIdx >= 0 && Op.Index != _autoIdx) _manualOp = true;        // the user chose something themselves: leave it alone
            if (!_manualOp)
            {
                int want = 0;
                if (NearestBody(tool) is { } near)
                {
                    double vol = 0; try { vol = Math.Abs(Occ.Mass(tool).Volume); } catch (Exception e) when (e is not OutOfMemoryException) { }       // a reversed solid reports a negative volume
                    double inside = vol > 1e-6 ? Overlap(near, tool) / vol : 0;
                    if (inside > 0.35) want = 2;
                    Results.Add(new ResultLine("Automatic operation", $"{inside * 100:0}% of the new shape is inside {near.Name} -> {(want == 2 ? "Cut" : "new body")} (choose another Operation to override)"));      // mostly inside a body: that is a cut (a wing root or boss that only pokes in stays a new body)
                }
                if (want != Op.Index) { Op.SetSilently(want); UpdateVisibility(); }
                _autoIdx = want;
            }
        }
        if (Op.Index == 0) { o.Add.Add(new(tool, name, kind)); return o; }
        var target = ResolveTarget(tool);
        if (Op.Index is 2 or 3 && reversed != null && Overlap(target, tool) < 1e-9)
        {
            // asked to cut, but the shape points away from the body: turn it round so it cuts
            var back = reversed();
            if (Overlap(target, back) > 1e-9) { tool = back; FlippedForCut = true; }
        }
        var res = Op.Index switch
        {
            1 => Occ.Fuse(target.Shape, tool),
            2 => Occ.Cut(target.Shape, tool),
            _ => Occ.Common(target.Shape, tool),
        };
        o.Replace.Add((target, res));
        if (Op.Index == 2) o.Ghosts.Add(tool);       // show the tool faintly so you can see what is being cut away
        return o;
    }

    protected bool TargetMissing => Op != null && Op.Index != 0 && Target!.Items.Count == 0 && !Bodies().Any();
}

// ---------------------------------------------------------------- primitives
public abstract class PrimitiveCommand : BodyCommand
{
    protected NumberParam X = null!, Y = null!, Z = null!;
    protected abstract string Prefix { get; }
    protected abstract Shape Make();
    public override string Hint => "Set the dimensions, then press OK. Use Operation to join or cut from an existing body.";

    protected void AddPosition()
    {
        X = Num("Position X", 0); Y = Num("Position Y", 0); Z = Num("Position Z", 0);
        AddOperation();
    }

    protected override CommandOutput? Build()
    {
        if (TargetMissing) return null;
        var s = Make();
        if (X.Value != 0 || Y.Value != 0 || Z.Value != 0) s = Occ.Translate(s, new(X.Value, Y.Value, Z.Value));
        return Finish(s, Prefix, ObjKind.Solid);
    }
}

public sealed class BoxCommand : PrimitiveCommand
{
    NumberParam W, D, H;
    public override string Title => "Box";
    protected override string Prefix => "Box";
    public BoxCommand() { W = Num("Width (X)", 40, 0.001); D = Num("Depth (Y)", 30, 0.001); H = Num("Height (Z)", 20, 0.001); AddPosition(); }
    protected override Shape Make() => Occ.Box(W.Value, D.Value, H.Value);
}

public sealed class CylinderCommand : PrimitiveCommand
{
    NumberParam R, H;
    public override string Title => "Cylinder";
    protected override string Prefix => "Cylinder";
    public CylinderCommand() { R = Num("Radius", 12, 0.001); H = Num("Height", 30, 0.001); AddPosition(); }
    protected override Shape Make() => Occ.Cylinder(R.Value, H.Value);
}

public sealed class SphereCommand : PrimitiveCommand
{
    NumberParam R;
    public override string Title => "Sphere";
    protected override string Prefix => "Sphere";
    public SphereCommand() { R = Num("Radius", 18, 0.001); AddPosition(); }
    protected override Shape Make() => Occ.Sphere(R.Value);
}

public sealed class ConeCommand : PrimitiveCommand
{
    NumberParam R1, R2, H;
    public override string Title => "Cone";
    protected override string Prefix => "Cone";
    public ConeCommand() { R1 = Num("Base radius", 16, 0); R2 = Num("Top radius", 4, 0); H = Num("Height", 30, 0.001); AddPosition(); }
    protected override Shape Make() => Occ.Cone(R1.Value, R2.Value, H.Value);
}

public sealed class TorusCommand : PrimitiveCommand
{
    NumberParam R, r;
    public override string Title => "Torus";
    protected override string Prefix => "Torus";
    public TorusCommand() { R = Num("Major radius", 20, 0.002); r = Num("Minor radius", 5, 0.001); AddPosition(); }
    protected override Shape Make() => Occ.Torus(R.Value, Math.Min(r.Value, R.Value * 0.99));
}

// ---------------------------------------------------------------- profile helpers
static class ProfileUtil
{
    public static (Shape shape, Vec3 normal) Resolve(CadSession s, SelectionSlot slot, bool wantFaces)
    {
        if (slot.Items.Count == 1 && slot.Items[0].Kind != SelKind.Edge)
        {
            var resolved = Resolve(s, slot.Items[0], wantFaces);
            ValidateClosed(resolved.shape, wantFaces);
            return resolved;
        }
        if (slot.Items.Count == 0) throw new KernelException("Pick a profile first.");
        var shapes = slot.Items.Select(item => s.ShapeOf(item) ?? throw new KernelException("A profile edge no longer exists.")).ToList();
        var profile = Occ.Profile(shapes, wantFaces);
        ValidateClosed(profile, wantFaces);
        var owner = s.Find(slot.Items[0].Obj);
        return (profile, owner?.SketchPlane?.Normal ?? NormalOf(profile) ?? Vec3.UnitZ);
    }
    static void ValidateClosed(Shape profile, bool wantFaces)
    {
        if (!wantFaces) return;
        int faceWires = Enumerable.Range(0, profile.Count(SubKind.Face)).Sum(i => profile.Sub(SubKind.Face, i).Count(SubKind.Wire));
        if (!profile.HasFaces || profile.Count(SubKind.Wire) > faceWires)
            throw new KernelException("A solid needs a closed profile. Add the missing edges, or use the Surface command for an open chain.");
    }
    /// <summary>Resolves a selected sketch/curve/face into a profile shape and its natural extrusion direction.</summary>
    public static (Shape shape, Vec3 normal) Resolve(CadSession s, SelItem item, bool wantFaces)
    {
        var obj = s.Find(item.Obj) ?? throw new KernelException("Profile no longer exists.");
        if (item.Kind == SelKind.Face)
        {
            var face = s.ShapeOf(item)!;
            var fr = s.FaceFrame(item);
            return (face, fr?.normal ?? Vec3.UnitZ);
        }
        var sh = item.Kind == SelKind.Edge ? s.ShapeOf(item)! : obj.Shape;
        Shape prof;
        if (item.Kind == SelKind.Object && obj.SketchData is { Sections.Count: > 0 } document && obj.SketchPlane is { } root)
        {
            var pieces = new List<Shape>();
            foreach (var (model, frame) in document.DrawingModels(root))
            {
                var edges = model.ToShapes(frame, includeSections: false);
                if (edges.Count > 0) pieces.Add(Occ.Profile(edges, wantFaces));
            }
            prof = Occ.Compound(pieces);
        }
        else prof = Occ.Profile([sh], wantFaces);
        Vec3 n;
        if (obj.SketchPlane is { } pl) n = pl.Normal;
        else n = NormalOf(prof) ?? Vec3.UnitZ;
        return (prof, n);
    }

    public static Vec3? NormalOf(Shape profile)
    {
        if (!profile.HasFaces) return null;
        var m = profile.Tessellate();
        if (m.Normals.Length < 3) return null;
        return new Vec3(m.Normals[0], m.Normals[1], m.Normals[2]).Normalized();
    }

    public static Shape OrientedWire(Shape s) => s;
}

// ---------------------------------------------------------------- extrude / revolve / sweep / loft
public sealed class ExtrudeCommand : BodyCommand
{
    readonly bool _surface;
    readonly SelectionSlot _profile, _to;
    readonly ChoiceParam _extent, _stopAt;
    readonly NumberParam _dist, _dist2, _taper, _toOffset, _region;
    readonly BoolParam _flip;
    public ExtrudeCommand(bool surface = false)
    {
        _surface = surface;
        _profile = Slot(new SelectionSlot { Label = "Profile", Filter = SelFilter.SketchObj | SelFilter.CurveObj | SelFilter.Face | SelFilter.Edge, Multi = true, ProfileSelection = true });
        _extent = Add(new ChoiceParam { Label = "Extent", Options = ["Distance", "Two sides", "Symmetric", "To object", "Through all", "Through all, both ways"], IsIllustrated = true });
        _extent.Caption = "Mode";
        _dist = Num("Distance", 20, -1e5);
        _dist2 = Num("Distance 2 (other side)", 10, 0, 1e5);
        _to = Slot(new SelectionSlot { Label = "Extend to (face, plane or body)", Section = "Extent", Filter = SelFilter.Face | SelFilter.Plane | SelFilter.Solid | SelFilter.SurfaceObj, IsVisible = false, Optional = true });
        _stopAt = Choice("Stop at", 0, "Near side", "Far side");
        _toOffset = Num("Offset from the target", 0, -1e5, 1e5, 1);
        _taper = Num("Taper angle", 0, -80, 80, 1, "°");
        _flip = Check("Reverse direction", false);
        _region = Num("Region (0 = all of them)", 0, 0, 999, 1, ""); _region.IsVisible = false;
        foreach (var p in Params) p.Section = "Extent";
        _taper.Section = "Refine";
        if (!surface) AddOperation(true);
        _extent.Changed = () => { UpdateVisibility(); OnChanged(); };
    }
    public override string Title => _surface ? "Extrude Surface" : "Extrude";
    public override string Hint => _surface ? "Choose a profile or an open edge chain, then set the extrusion extent." : "Choose a profile, set its extent, then choose how the result combines with your model. Use selection intent to pick individual edges or a chain.";

    int Ext => _extent.Index;

    protected override void UpdateVisibility()
    {
        bool linear = Ext <= 2, to = Ext == 3;
        _dist.Caption = Ext == 2 ? "Total length" : "Distance";
        _dist.HelpText = Ext == 2 ? $"{_dist.Value / 2:0.###} mm each side" : "";
        _dist.IsVisible = linear; _dist2.IsVisible = Ext == 1; _taper.IsVisible = linear && !_surface;
        if (_to.IsVisible != to) { _to.IsVisible = to; if (to) foreach (var s in Slots) s.IsActive = s == _to; else _to.IsActive = false; }
        _stopAt.IsVisible = to; _toOffset.IsVisible = to;
        base.UpdateVisibility();
    }

    (SelItem? item, Vec3 origin, Vec3 n) _hc;
    int _hcSelection;
    int _hcRegion;
    bool _autoFlip;

    /// <summary>A sketch with several separate closed shapes can be extruded one shape at a time (and again from the same sketch): number them left to right and let the user pick one.</summary>
    Shape PickRegion(Shape prof)
    {
        int n = prof.HasFaces ? prof.Count(SubKind.Face) : 0;
        if (n < 2) { _region.IsVisible = false; return prof; }
        _region.IsVisible = true;
        var order = Enumerable.Range(0, n).Select(i => { var f = prof.Sub(SubKind.Face, i); return (i, f, c: f.Props().Center); })
            .OrderBy(r => Math.Round(r.c.X, 3)).ThenBy(r => Math.Round(r.c.Y, 3)).ThenBy(r => r.c.Z).ToList();
        int pick = (int)_region.Value;
        if (pick > 0 && pick <= n) return order[pick - 1].f;
        if (_profile.Items.Count > 0 && S.Find(_profile.Items[0].Obj)?.SketchData is { Sections.Count: > 0 })
            throw new KernelException("This sketch has multiple drawing planes. Choose one Region to extrude normal to that profile.");
        return prof;
    }

    Vec3 RegionNormal(Shape profile, Vec3 fallback) => _profile.Items.Count > 0 && S.Find(_profile.Items[0].Obj)?.SketchData is { Sections.Count: > 0 }
        ? ProfileUtil.NormalOf(profile) ?? fallback : fallback;

    bool Resolved()
    {
        if (_profile.Items.Count == 0) return false;
        var it = _profile.Items[0];
        int selection = _profile.Items.Aggregate(17, (hash, item) => HashCode.Combine(hash, item));
        if (!Equals(_hc.item, it) || _hcRegion != (int)_region.Value || _hcSelection != selection)
        {
            try { var (prof, n) = ProfileUtil.Resolve(S, _profile, !_surface); prof = PickRegion(prof); _hcRegion = (int)_region.Value; _hcSelection = selection; _hc = (it, CenterOf(prof), RegionNormal(prof, n)); }
            catch (Exception e) when (e is not OutOfMemoryException) { return false; }
        }
        return true;
    }

    double Sign => (_flip.Value ? -1 : 1) * (_autoFlip ? -1 : 1);

    public override IReadOnlyList<HandleSpec> Handles
    {
        get
        {
            if (!Resolved() || Ext > 2) return [];
            var dir = _hc.n * Sign;
            var list = new List<HandleSpec> { new(_hc.origin, dir, _dist, Ext == 2 ? 2 : 1) };         // symmetric: the arrow marks one half
            if (Ext == 1) list.Add(new HandleSpec(_hc.origin, -dir, _dist2));
            return list;
        }
    }
    public override HandleSpec? Handle => Handles.Count > 0 ? Handles[0] : null;

    /// <summary>How far past the profile plane the bodies in the scene reach (forward, backward) along dir.</summary>
    (double fwd, double back) SceneReach(Vec3 origin, Vec3 dir)
    {
        double f = 0, b = 0; bool any = false;
        foreach (var o in S.Objects.Where(o => o.Kind == ObjKind.Solid && o.Visible))
        {
            var m = o.Mesh; any = true;
            foreach (var x in new[] { m.Min.X, m.Max.X }) foreach (var y in new[] { m.Min.Y, m.Max.Y }) foreach (var z in new[] { m.Min.Z, m.Max.Z })
            {
                double t = Vec3.Dot(new Vec3(x, y, z) - origin, dir);
                f = Math.Max(f, t); b = Math.Max(b, -t);
            }
        }
        if (!any) throw new KernelException("There is no body to go through.");
        return (f, b);
    }

    /// <summary>Distance from the profile to the chosen target along dir.</summary>
    double ToTarget(Vec3 origin, Vec3 dir)
    {
        if (_to.Items.Count == 0) throw new KernelException("Pick the face, plane or body to extend to.");
        var item = _to.Items[0];
        (Vec3 p, Vec3 n)? plane = null;
        if (item.Kind == SelKind.Face && S.FaceFrame(item) is { } fr && Occ.FaceInfoOf(S.ShapeOf(item)!).Type.Contains("lane", StringComparison.OrdinalIgnoreCase)) plane = (fr.point, fr.normal);
        else if (item.Kind == SelKind.Origin && item.Index < 3) { var pl = CadSession.OriginPlane(item.Index); plane = (pl.Origin, pl.Normal); }
        else if (item.Kind == SelKind.Object && S.Find(item.Obj) is { SketchPlane: { } sp, Kind: ObjKind.Plane }) plane = (sp.Origin, sp.Normal);
        double t;
        if (plane is { } pn)
        {
            double den = Vec3.Dot(pn.n, dir);
            if (Math.Abs(den) < 1e-6) throw new KernelException("The target is parallel to the extrusion, so it can never be reached.");
            t = Vec3.Dot(pn.p - origin, pn.n) / den;
        }
        else
        {
            var sh = item.Kind == SelKind.Face ? S.ShapeOf(item)! : S.Find(item.Obj)!.Shape;
            var pr = sh.Props(); double lo = double.MaxValue, hi = double.MinValue;
            foreach (var x in new[] { pr.Min.X, pr.Max.X }) foreach (var y in new[] { pr.Min.Y, pr.Max.Y }) foreach (var z in new[] { pr.Min.Z, pr.Max.Z })
            { double d = Vec3.Dot(new Vec3(x, y, z) - origin, dir); lo = Math.Min(lo, d); hi = Math.Max(hi, d); }
            t = _stopAt.Index == 0 ? lo : hi;
        }
        t += _toOffset.Value;
        if (t <= 1e-6) throw new KernelException("The target is behind the profile. Try Reverse direction.");
        return t;
    }

    static Shape Prism(Shape prof, Vec3 dir, double len, double taper)
    {
        if (Math.Abs(len) < 1e-9) throw new KernelException("Zero length.");
        var d = len < 0 ? -dir : dir; double l = Math.Abs(len);
        return Math.Abs(taper) > 1e-6 ? Occ.ExtrudeDraft(prof, d, l, taper) : Occ.Extrude(prof, d * l);
    }

    Shape BuildShape(Shape prof, Vec3 dir, double f, double b)
    {
        double taper = _taper.IsVisible ? _taper.Value : 0;
        if (b < 1e-9) return Prism(prof, dir, f, taper);
        if (Math.Abs(taper) < 1e-6) return Occ.Extrude(Occ.Translate(prof, -dir * b), dir * (f + b));
        return Occ.Fuse(Prism(prof, dir, f, taper), Prism(prof, -dir, b, taper));
    }

    protected override CommandOutput? Build()
    {
        if (!Complete) return null;
        Results.Clear();
        var (prof, n) = ProfileUtil.Resolve(S, _profile, !_surface);
        int regions = prof.HasFaces ? prof.Count(SubKind.Face) : 0;
        prof = PickRegion(prof);
        n = RegionNormal(prof, n);
        if (regions > 1) Results.Add(new ResultLine("Regions", _region.Value >= 1 && _region.Value <= regions ? $"number {(int)_region.Value} of {regions}" : $"all {regions}"));
        var dir = n * (_flip.Value ? -1 : 1);
        double f, b = 0; string how;
        switch (Ext)
        {
            case 0: f = _dist.Value; how = $"{_dist.Value:0.##} mm"; if (Math.Abs(f) < 1e-6) return null; break;
            case 1: f = _dist.Value; b = _dist2.Value; how = $"{f:0.##} mm one way, {b:0.##} mm the other"; if (Math.Abs(f) + b < 1e-6) return null; break;
            case 2: f = b = _dist.Value / 2; how = $"{_dist.Value:0.##} mm, centred"; if (Math.Abs(_dist.Value) < 1e-6) return null; break;
            case 3: if (_to.Items.Count == 0) return null; f = ToTarget(CenterOf(prof), dir); how = $"{f:0.##} mm, up to the target"; break;
            default:
            {
                var (fw, bw) = SceneReach(CenterOf(prof), dir);
                if (Ext == 5) { f = fw + 1; b = bw + 1; how = "through everything, both ways"; }
                else if (fw >= bw) { f = fw + 1; how = "through everything"; }
                else { f = -(bw + 1); how = "through everything (towards the body)"; }
                break;
            }
        }
        var shape = BuildShape(prof, dir, f, b);
        Results.Add(new ResultLine("Extent", how));
        if (_surface) { var o = new CommandOutput(); o.Add.Add(new(shape, "Surface", ObjKind.Surface)); return o; }
        var baseProf = prof;
        // a Cut that points away from the body is turned around automatically, but only for a plain one-way distance
        var res = Finish(shape, "Extrusion", null, Ext == 0 ? () => Occ.Extrude(baseProf, -dir * f) : null);
        _autoFlip = FlippedForCut;
        if (Op != null && Op.Index == 2) Results.Add(new ResultLine("Operation", _autoFlip ? "Cut (turned to point into the body)" : "Cut"));
        return res;
    }
}

public sealed class RevolveCommand : BodyCommand
{
    readonly bool _surface;
    readonly SelectionSlot _profile, _axisEdge;
    readonly ChoiceParam _axis, _extent;
    readonly NumberParam _angle, _angle2;
    readonly BoolParam _flip;
    public RevolveCommand(bool surface = false)
    {
        _surface = surface;
        _profile = Slot(new SelectionSlot { Label = "Profile", Filter = SelFilter.SketchObj | SelFilter.CurveObj | SelFilter.Face | SelFilter.Edge, Multi = true, ProfileSelection = true });
        _axis = Choice("Axis", 2, "World X", "World Y", "World Z", "Pick a line");
        _axis.Caption = "Axis type";
        _axis.Section = "Axis";
        _axisEdge = Slot(new SelectionSlot { Label = "Axis line", Section = "Axis", Filter = SelFilter.Edge | SelFilter.Axis, IsVisible = false });
        _extent = Add(new ChoiceParam { Label = "Extent", Options = ["Angle", "Full 360°", "Two sides", "Symmetric"], IsIllustrated = true, IsAngular = true });
        _extent.Caption = "Mode";
        _angle = Num("Angle", 360, -360, 360, 1, "°");
        _angle2 = Num("Angle 2 (other side)", 90, 0, 360, 1, "°");
        _flip = Check("Reverse direction", false);
        foreach (var p in Params.Where(p => p != _axis)) p.Section = "Extent";
        if (!surface) AddOperation();
        _extent.Changed = () => { UpdateVisibility(); OnChanged(); };
    }
    public override string Title => _surface ? "Revolve Surface" : "Revolve";
    public override string Hint => _surface ? "Choose a profile or edge chain, choose the rotation axis, then set the angle." : "Choose a profile, choose the rotation axis, then set the angle and result.";

    protected override void UpdateVisibility()
    {
        bool pick = _axis.Index == 3;
        _angle.Caption = _extent.Index == 3 ? "Total angle" : "Angle";
        _angle.HelpText = _extent.Index == 3 ? $"{_angle.Value / 2:0.###}° each side" : "";
        if (_axisEdge.IsVisible != pick) { _axisEdge.IsVisible = pick; if (pick) foreach (var s in Slots) s.IsActive = s == _axisEdge; }
        _angle.IsVisible = _extent.Index != 1; _angle2.IsVisible = _extent.Index == 2;
        base.UpdateVisibility();
    }

    bool Axis(out Vec3 o, out Vec3 a)
    {
        o = Vec3.Zero; a = Vec3.UnitZ;
        switch (_axis.Index)
        {
            case 0: a = Vec3.UnitX; return true;
            case 1: a = Vec3.UnitY; return true;
            case 2: return true;
            default:
                if (_axisEdge.Items.Count == 0) return false;
                if (_axisEdge.Items[0].Kind == SelKind.Origin) { (o, a) = CadSession.OriginAxis(_axisEdge.Items[0].Index); return true; }
                if(S.DatumOf(_axisEdge.Items[0]) is { } datum){o=datum.Frame.Origin;a=datum.Frame.U;return true;}
                var pts = S.EdgePoints(_axisEdge.Items[0]);
                if (pts.Count < 2) throw new KernelException("Could not read the axis line.");
                o = pts[0]; a = pts[^1] - pts[0];
                if (a.Length < 1e-9) throw new KernelException("The axis line is degenerate.");
                return true;
        }
    }

    (SelItem? item, Vec3 center) _hc;
    SelItem[] _hcItems = [];
    public override IReadOnlyList<HandleSpec> Handles
    {
        get
        {
            if (_profile.Items.Count == 0 || _extent.Index == 1 || _extent.Index == 3) return [];
            try
            {
                if (!Axis(out var o, out var a)) return [];
                var it = _profile.Items[0];
                if (!_hcItems.SequenceEqual(_profile.Items))
                {
                    var (prof, _) = ProfileUtil.Resolve(S, _profile, !_surface); _hc = (it, CenterOf(prof));
                    _hcItems = _profile.Items.ToArray();
                }
                a = a.Normalized(); var rel = _hc.center - o; var radial = rel - a * Vec3.Dot(rel, a);
                double r = radial.Length; if (r < 1e-6) return [];
                var tangent = Vec3.Cross(a, radial).Normalized() * (_flip.Value ? -1 : 1);
                double mul = 180 / (Math.PI * r);                    // degrees per mm of arc, so the arrow tip follows the swept arc
                var list = new List<HandleSpec> { new(_hc.center, tangent, _angle, mul) };
                if (_extent.Index == 2) list.Add(new HandleSpec(_hc.center, -tangent, _angle2, mul));
                return list;
            }
            catch (Exception e) when (e is not OutOfMemoryException) { return []; }
        }
    }
    public override HandleSpec? Handle => Handles.Count > 0 ? Handles[0] : null;

    protected override CommandOutput? Build()
    {
        if (!Complete) return null;
        var (prof, _) = ProfileUtil.Resolve(S, _profile, !_surface);
        if (!Axis(out var o, out var a)) return null;
        a = a.Normalized(); double s = _flip.Value ? -1 : 1;
        Shape shape;
        switch (_extent.Index)
        {
            case 1: shape = Occ.Revolve(prof, o, a, 360); break;
            case 2:
                if (Math.Abs(_angle.Value) + _angle2.Value < 1e-6) return null;
                shape = Occ.Revolve(Occ.Rotate(prof, o, a, -s * _angle2.Value), o, a, s * (_angle.Value + _angle2.Value)); break;
            case 3:
                if (Math.Abs(_angle.Value) < 1e-6) return null;
                shape = Occ.Revolve(Occ.Rotate(prof, o, a, -s * _angle.Value / 2), o, a, s * _angle.Value); break;
            default: shape = Occ.Revolve(prof, o, a, s * _angle.Value); break;
        }
        if (_surface) { var r = new CommandOutput(); r.Add.Add(new(shape, "Surface", ObjKind.Surface)); return r; }
        return Finish(shape, "Revolve");
    }
}

public sealed class SweepCommand : BodyCommand
{
    readonly bool _surface;
    readonly SelectionSlot _profile, _path;
    readonly ChoiceParam _orient;
    readonly NumberParam _endScale;
    public SweepCommand(bool surface = false)
    {
        _surface = surface;
        _profile = Slot(new SelectionSlot { Label = "Profile", Filter = SelFilter.SketchObj | SelFilter.CurveObj | SelFilter.Face });
        _path = Slot(new SelectionSlot { Label = "Path", Filter = SelFilter.AnyCurve | SelFilter.Edge });
        _orient = Choice("Profile orientation", 0, "Follow the path", "Keep parallel (no turning)", "Keep upright (Z stays up)");
        _endScale = Num("Scale at the end", 1, 0.01, 100, 0.1, "");
        if (!surface) AddOperation();
    }
    public override string Title => _surface ? "Sweep Surface" : "Sweep";
    public override string Hint => "Select the profile, then the path to sweep it along. Choose how the profile turns, and a scale to grow or shrink it along the way.";

    protected override CommandOutput? Build()
    {
        if (!Complete) return null;
        var (prof, _) = ProfileUtil.Resolve(S, _profile.Items[0], !_surface);
        var pathItem = _path.Items[0];
        var path = pathItem.Kind == SelKind.Edge ? S.ShapeOf(pathItem)! : Occ.Profile([S.ShapeOf(pathItem)!], false);
        var shape = Occ.Sweep2(prof, path, !_surface, _orient.Index, _endScale.Value, Vec3.UnitZ);
        if (_surface) { var r = new CommandOutput(); r.Add.Add(new(shape, "Surface", ObjKind.Surface)); return r; }
        return Finish(shape, "Sweep");
    }
}

public sealed class LoftCommand : BodyCommand
{
    readonly bool _surface;
    readonly SelectionSlot _sections, _rails;
    readonly BoolParam _ruled, _closed;
    readonly IntParam _railSteps;
    public LoftCommand(bool surface = false)
    {
        _surface = surface;
        _sections = Slot(new SelectionSlot { Label = "Sections (in order)", Caption = "Profiles", Filter = SelFilter.AnyCurve | SelFilter.Face, Multi = true, Ordered = true });
        _rails = Slot(new SelectionSlot { Label = "Guide rails (optional, one or two)", Caption = "Guide rails", Filter = SelFilter.AnyCurve | SelFilter.Edge, Multi = true, Optional = true, MaxCount = 2 });
        _ruled = Check("Straight (ruled) transitions", false);
        _closed = Check("Close the loft (last section back to the first)", false);
        _ruled.Caption = "Ruled transitions";
        _closed.Caption = "Closed loft";
        _closed.HelpText = "Connect the last profile back to the first.";
        _railSteps = Add(new IntParam { Label = "Guide rail accuracy (sections along the rail)", Caption = "Rail accuracy", Min = 4, Max = 40, Value = 12, IsAdvanced = true, HelpText = "Number of intermediate sections used to follow the rails." });
        if (!surface) AddOperation();
    }
    public override string Title => _surface ? "Loft Surface" : "Loft";
    public override string Hint => "Click two or more profiles in the order they should be connected. Add one or two guide rails (curves that run along the shape) to control its outline between the profiles.";

    protected override void UpdateVisibility()
    {
        _railSteps.IsVisible = _rails.Items.Count > 0;
        base.UpdateVisibility();
    }

    protected override CommandOutput? Build()
    {
        if (_sections.Items.Count < 2 || TargetMissing) return null;
        Results.Clear();
        var secs = _sections.Items.Select(i => i.Kind == SelKind.Object ? S.Find(i.Obj)!.Shape : S.ShapeOf(i)!).ToList();
        if (_closed.Value && secs.Count > 2) secs.Add(secs[0]);
        Shape shape;
        if (_rails.Items.Count > 0)
        {
            var rails = _rails.Items.Take(2).Select(i => i.Kind == SelKind.Edge ? S.ShapeOf(i)! : Occ.Profile([i.Kind == SelKind.Object ? S.Find(i.Obj)!.Shape : S.ShapeOf(i)!], false)).ToList();
            if (_rails.Items.Count > 2) Results.Add(new ResultLine("Guide rails", "Only the first two rails are used", 1));
            shape = GuidedLoft.Build(secs, rails, !_surface, _ruled.Value, _railSteps.Value);
            Results.Add(new ResultLine("Guide rails", $"{rails.Count} rail(s) steer the shape through {_railSteps.Value} sections"));
        }
        else shape = Occ.Loft(secs, !_surface, _ruled.Value);
        if (_surface) { var r = new CommandOutput(); r.Add.Add(new(shape, "Surface", ObjKind.Surface)); return r; }
        return Finish(shape, "Loft");
    }
}

/// <summary>Loft that follows guide rails: the plain loft is cut by planes square to the first rail, and each cut is moved (one rail) or moved, turned and scaled (two rails) so it touches the rails; the final loft goes through those cuts.</summary>
static class GuidedLoft
{
    public static Shape Build(List<Shape> secs, List<Shape> rails, bool solid, bool ruled, int steps)
    {
        using var skin = Occ.Loft(secs, false, ruled);                       // the unguided shape, only used to read cross-sections off
        var samples = Occ.CurveSamples(rails[0], steps + 1, 1);
        if (samples.Count == 0) throw new KernelException("Could not read the guide rail.");
        var pts = samples[0].Select(s => s.p).ToList();
        // the rail runs from the first section to the last: make sure of that
        if (Occ.Distance(secs[0], rails[0]).Distance > Occ.Distance(secs[^1], rails[0]).Distance) pts.Reverse();
        var wires = new List<Shape>();
        for (int i = 0; i < pts.Count; i++)
        {
            Shape w;
            if (i == 0) w = secs[0];
            else if (i == pts.Count - 1) w = secs[^1];
            else
            {
                var t = (pts[i + 1] - pts[i - 1]).Normalized();
                try { w = Occ.Section(skin, pts[i], t); }
                catch (KernelException) { continue; }                          // a cut that misses the skin is simply skipped
            }
            wires.Add(Fit(w, pts[i], rails));
        }
        if (wires.Count < 2) throw new KernelException("The guide rail does not run along the shape.");
        return Occ.Loft(wires, solid, ruled);
    }

    static Shape Fit(Shape w, Vec3 railPoint, List<Shape> rails)
    {
        var a1 = Occ.Distance(w, rails[0]);
        var res = Occ.Translate(w, a1.B - a1.A);                                // rail 1: slide the section onto the rail
        if (rails.Count < 2) return res;
        var p1 = a1.B;
        var d2 = Occ.Distance(res, rails[1]);
        var from = d2.A - p1; var to = d2.B - p1;
        if (from.Length < 1e-6 || to.Length < 1e-6) return res;
        double k = to.Length / from.Length;
        if (k < 0.05 || k > 20) return res;                                    // the rail is not near this cut: leave it
        res = Occ.Scale(res, p1, k);
        var axis = Vec3.Cross(from, to);
        if (axis.Length > 1e-9)
        {
            double ang = Math.Acos(Math.Clamp(Vec3.Dot(from.Normalized(), to.Normalized()), -1, 1)) * 180 / Math.PI;
            res = Occ.Rotate(res, p1, axis.Normalized(), ang);
        }
        return res;
    }
}

// ---------------------------------------------------------------- dress-up
public abstract class EdgeDressCommand : Command
{
    protected readonly SelectionSlot Edges;
    protected readonly NumberParam Size;
    protected EdgeDressCommand(double size)
    {
        Edges = Slot(new SelectionSlot { Label = "Edges", Filter = SelFilter.Edge, Multi = true });
        Size = Num(SizeLabel, size, 0.001);
    }
    protected abstract string SizeLabel { get; }
    protected abstract Shape Apply(Shape s, int[] edges, double size);
    public override string Hint => "Click edges on a body (click again to deselect).";

    protected override CommandOutput? Build()
    {
        if (!Complete) return null;
        var o = new CommandOutput();
        foreach (var g in Edges.Items.GroupBy(i => i.Obj))
        {
            var obj = S.Find(g.Key)!;
            o.Replace.Add((obj, Apply(obj.Shape, g.Select(i => i.Index).ToArray(), Size.Value)));
        }
        return o;
    }
}

public sealed class FilletCommand : EdgeDressCommand
{
    readonly NumberParam _endRadius;
    public FilletCommand() : base(2) { _endRadius = Num("End radius (0 = same as radius)", 0, 0); }
    public override string Title => "Fillet";
    protected override string SizeLabel => "Radius";
    public override string Hint => "Click edges on a body (click again to deselect). Give an end radius to make the fillet grow or shrink along each edge.";
    protected override Shape Apply(Shape s, int[] edges, double size)
        => _endRadius.Value > 1e-9 && Math.Abs(_endRadius.Value - size) > 1e-9 ? Occ.FilletVariable(s, edges, size, _endRadius.Value) : Occ.Fillet(s, edges, size);
}

public sealed class ChamferCommand : EdgeDressCommand
{
    public ChamferCommand() : base(1.5) { }
    public override string Title => "Chamfer";
    protected override string SizeLabel => "Distance";
    protected override Shape Apply(Shape s, int[] edges, double size) => Occ.Chamfer(s, edges, size);
}

public sealed class ShellCommand : Command
{
    readonly SelectionSlot _faces;
    readonly NumberParam _t;
    readonly ChoiceParam _dir;
    public ShellCommand()
    {
        _faces = Slot(new SelectionSlot { Label = "Faces to remove", Filter = SelFilter.Face | SelFilter.Solid, Multi = true });
        _t = Num("Wall thickness", 2, 0.001);
        _dir = Choice("Direction", 0, "Inside", "Outside");
    }
    public override string Title => "Shell";
    public override string Hint => "Click the faces to open up. Pick the body itself for a closed hollow.";

    protected override CommandOutput? Build()
    {
        if (!Complete) return null;
        var o = new CommandOutput();
        double t = _t.Value * (_dir.Index == 0 ? -1 : 1);
        foreach (var g in _faces.Items.GroupBy(i => i.Obj))
        {
            var obj = S.Find(g.Key)!;
            var faces = g.Where(i => i.Kind == SelKind.Face).Select(i => i.Index).ToArray();
            o.Replace.Add((obj, Occ.Shell(obj.Shape, faces, t)));
        }
        return o;
    }
}

// ---------------------------------------------------------------- booleans
public sealed class BooleanCommand : Command
{
    public enum Kind { Union, Subtract, Intersect }
    readonly Kind _kind;
    readonly SelectionSlot _target, _tools;
    readonly BoolParam _keep;
    public BooleanCommand(Kind kind)
    {
        _kind = kind;
        _target = Slot(new SelectionSlot { Label = kind == Kind.Subtract ? "Body to cut from" : "Base body", Filter = SelFilter.Solid | SelFilter.SurfaceObj });
        _tools = Slot(new SelectionSlot { Label = kind == Kind.Subtract ? "Tool bodies" : "Other bodies", Filter = SelFilter.Solid | SelFilter.SurfaceObj, Multi = true });
        _keep = Check("Keep tool bodies", false);
    }
    public override string Title => _kind.ToString();
    public override string Hint => "Pick the base body, then the bodies to combine with it.";

    protected override CommandOutput? Build()
    {
        if (!Complete) return null;
        var target = S.Find(_target.Items[0].Obj)!;
        var tools = _tools.Items.Select(i => S.Find(i.Obj)!).Where(t => t.Id != target.Id).Distinct().ToList();
        if (tools.Count == 0) return null;
        var res = target.Shape;
        foreach (var t in tools)
            res = _kind switch { Kind.Union => Occ.Fuse(res, t.Shape), Kind.Subtract => Occ.Cut(res, t.Shape), _ => Occ.Common(res, t.Shape) };
        var o = new CommandOutput();
        o.Replace.Add((target, res));
        if (!_keep.Value) o.Remove.AddRange(tools);
        return o;
    }
}

// ---------------------------------------------------------------- transforms
public sealed class TransformCommand : Command
{
    public enum Kind { Move, Rotate, Scale, Mirror }
    readonly Kind _kind;
    readonly SelectionSlot _objs;
    readonly NumberParam? _dx, _dy, _dz, _angle, _factor;
    readonly ChoiceParam? _axis, _pivot, _plane;
    readonly BoolParam _copy;

    public TransformCommand(Kind kind)
    {
        _kind = kind;
        _objs = Slot(new SelectionSlot { Label = "Objects", Filter = SelFilter.AnyObject, Multi = true });
        switch (kind)
        {
            case Kind.Move: _dx = Num("Δ X", 10); _dy = Num("Δ Y", 0); _dz = Num("Δ Z", 0); break;
            case Kind.Rotate: _axis = Choice("Axis", 2, "X", "Y", "Z"); _angle = Num("Angle", 45, -360, 360, 1, "°"); _pivot = Choice("Pivot", 0, "Object center", "World origin"); break;
            case Kind.Scale: _factor = Num("Factor", 1.5, 0.001, 1000, 0.1, "×"); _pivot = Choice("Pivot", 0, "Object center", "World origin"); break;
            case Kind.Mirror: _plane = Choice("Mirror plane", 0, "XY (Z = 0)", "XZ (Y = 0)", "YZ (X = 0)"); break;
        }
        _copy = Check("Create copy", kind == Kind.Mirror);
    }
    public override string Title => _kind.ToString();
    public override string Hint => "Select the objects to transform.";

    protected override CommandOutput? Build()
    {
        if (!Complete) return null;
        var o = new CommandOutput();
        foreach (var obj in ObjectsOf(_objs).Distinct())
        {
            if (obj.Form != null) { AddFormResult(o, obj); continue; }
            if (obj.Tri is { } tri) { AddMeshResult(o, obj, tri); continue; }
            var center = (_pivot?.Index ?? 0) == 0 ? obj.Shape.Props().Center : Vec3.Zero;
            Shape r = _kind switch
            {
                Kind.Move => Occ.Translate(obj.Shape, new(_dx!.Value, _dy!.Value, _dz!.Value)),
                Kind.Rotate => Occ.Rotate(obj.Shape, center, _axis!.Index == 0 ? Vec3.UnitX : _axis.Index == 1 ? Vec3.UnitY : Vec3.UnitZ, _angle!.Value),
                Kind.Scale => Occ.Scale(obj.Shape, center, _factor!.Value),
                _ => Occ.Mirror(obj.Shape, Vec3.Zero, _plane!.Index == 0 ? Vec3.UnitZ : _plane.Index == 1 ? Vec3.UnitY : Vec3.UnitX),
            };
            if (_copy.Value) o.Add.Add(new(r, StripNumber(obj.Name), obj.Kind, obj.Color, obj.SketchPlane, null, obj.Look));
            else o.Replace.Add((obj, r));
        }
        return o;
    }

    static string StripNumber(string n) { int i = n.LastIndexOf(' '); return i > 0 && int.TryParse(n[(i + 1)..], out _) ? n[..i] : n; }

    void AddMeshResult(CommandOutput o, SceneObject obj, Meshing.TriMesh tri)
    {
        var center = (_pivot?.Index ?? 0) == 0 ? tri.Center : Vec3.Zero;
        Meshing.TriMesh r = _kind switch
        {
            Kind.Move => Meshing.MeshOps.Move(tri, new(_dx!.Value, _dy!.Value, _dz!.Value)),
            Kind.Rotate => Meshing.MeshOps.Rotate(tri, center, _axis!.Index == 0 ? Vec3.UnitX : _axis.Index == 1 ? Vec3.UnitY : Vec3.UnitZ, _angle!.Value),
            Kind.Scale => Meshing.MeshOps.Scale(tri, center, _factor!.Value),
            _ => Meshing.MeshOps.Mirror(tri, Vec3.Zero, _plane!.Index == 0 ? Vec3.UnitZ : _plane.Index == 1 ? Vec3.UnitY : Vec3.UnitX),
        };
        if (_copy.Value) o.Add.Add(new(null, StripNumber(obj.Name), ObjKind.Mesh, obj.Color, null, null, obj.Look, r));
        else o.ReplaceMeshes.Add((obj, r));
    }

    /// <summary>Forms are transformed by moving their control points.</summary>
    void AddFormResult(CommandOutput o, SceneObject obj)
    {
        var f = obj.Form!.Clone();
        var (mn, mx) = f.Bounds();
        var center = (_pivot?.Index ?? 0) == 0 ? (mn + mx) * 0.5 : Vec3.Zero;
        Func<Vec3, Vec3> t;
        switch (_kind)
        {
            case Kind.Move: { var d = new Vec3(_dx!.Value, _dy!.Value, _dz!.Value); t = p => p + d; break; }
            case Kind.Scale: { double k = _factor!.Value; t = p => center + (p - center) * k; break; }
            case Kind.Rotate:
            {
                var ax = _axis!.Index == 0 ? Vec3.UnitX : _axis.Index == 1 ? Vec3.UnitY : Vec3.UnitZ;
                double a = _angle!.Value * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
                t = p => { var v = p - center; return center + v * c + Vec3.Cross(ax, v) * s + ax * (Vec3.Dot(ax, v) * (1 - c)); };
                break;
            }
            default:
            {
                var n = _plane!.Index == 0 ? Vec3.UnitZ : _plane.Index == 1 ? Vec3.UnitY : Vec3.UnitX;
                t = p => p - n * (2 * Vec3.Dot(p, n));
                break;
            }
        }
        for (int i = 0; i < f.V.Count; i++) f.V[i] = t(f.V[i]);
        if (_kind == Kind.Mirror) FormOps.FlipNormals(f);
        if (_copy.Value) o.Add.Add(new(null, StripNumber(obj.Name), ObjKind.Form, obj.Color, null, f));
        else o.ReplaceForms.Add((obj, f));
    }
}
