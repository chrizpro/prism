using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Prism.Core;
using Prism.Kernel;

namespace Prism.Commands;

/// <summary>
/// Boundary Surface: one tool for spanning surfaces over boundaries made of curves and/or surface edges.
/// Picks may be free curves (position only), surface edges (each with its own G0/G1/G2 continuity and
/// a weight that pulls the fill toward the neighbour), one open edge or a whole surface (every opening
/// is found and filled, like Fill Opening), plus optional guide curves inside the outline.
/// </summary>
public sealed class BoundarySurfaceCommand : Command
{
    sealed class FillReq
    {
        public List<Shape> Hosts = []; public List<int> Ids = []; public List<int> Orders = []; public List<double> Weights = [];
        public List<Shape> Free = []; public List<SceneObject> Sources = [];
    }

    readonly SelectionSlot _boundary, _guides;
    readonly BoolParam _join;
    readonly NumberParam _tol;
    /// <summary>Per-edge "order:weight" pairs, hidden: it carries the dynamic pair values through feature recipes.</summary>
    readonly TextParam _edgeCfg;
    readonly ChoiceParam _fill, _inside;
    readonly IntParam _samples, _degree, _density, _iter;
    readonly NumberParam _full;
    readonly List<(ChoiceParam g, NumberParam w)> _pairs = [];
    readonly List<SelItem> _pairOf = [];
    bool _syncing;

    public BoundarySurfaceCommand()
    {
        _boundary = Slot(new SelectionSlot { Label = "Boundary", Filter = SelFilter.Edge | SelFilter.AnyCurve | SelFilter.SurfaceObj, Multi = true, SelectionRuleIndex = 1 });
        _guides = Slot(new SelectionSlot { Label = "Guide curves", Filter = SelFilter.AnyCurve | SelFilter.Edge, Multi = true, Optional = true, SelectionRuleIndex = 1 });
        _join = Check("Join the patch to the boundary surfaces (closes into a solid when it can)", true); _join.Section = "Join";
        _tol = Num("Joint tolerance", 0.05, 0.001, 5, 0.01); _tol.Section = "Join";
        _edgeCfg = Text("Edge continuity", ""); _edgeCfg.IsVisible = false;
        _fill = Choice("Fill with", 0, "Automatic (flat if the loop is flat)", "Flat surface", "Smooth surface");
        _inside = Choice("Inside curves", 0, "Follow them exactly", "Fit through points on them"); _inside.Section = "Inside curves";
        _samples = Add(new IntParam { Label = "Points taken along each curve", Value = 24, Min = 6, Max = 120, Section = "Inside curves" });
        _degree = Add(new IntParam { Label = "Surface degree", Value = 3, Min = 3, Max = 8, Section = "Inside curves" });
        _density = Add(new IntParam { Label = "Fit density", Value = 15, Min = 8, Max = 60, Section = "Inside curves" });
        _iter = Add(new IntParam { Label = "Smoothing passes", Value = 3, Min = 1, Max = 12, Section = "Inside curves" });
        _full = Num("Fullness", 1, 0.1, 4, 0.1, ""); _full.Section = "Blend";
    }

    public override string Title => "Boundary Surface";
    public override string Icon => "patch";
    public override string OkText => "Create";
    public override string Hint => "Pick the curves and surface edges around the new surface. A flat loop of curves becomes a flat surface, with loops inside it as holes; anything else becomes a smooth surface. Clicking one open edge fills the whole opening it belongs to; picking a surface fills every opening. Pick an edge on each of two separate surfaces to blend between them. Each boundary edge has its own continuity (G0/G1/G2) and weight; guide curves shape the inside.";

    /// <summary>Edges of objects with faces and whole surfaces get a continuity+weight pair; free curves are always G0.</summary>
    bool HasEdgeSettings(SelItem it)
    {
        var o = S.Find(it.Obj);
        if (it.Kind == SelKind.Object) return o?.Kind == ObjKind.Surface;
        return it.Kind == SelKind.Edge && o != null && o.HasBrep && o.Shape.Count(SubKind.Face) > 0;
    }

    protected override void UpdateVisibility()
    {
        if (_full == null || S == null) return;
        SyncItemParams();
        bool blend = IsBlend(), inside = _guides.Items.Count > 0;
        _fill.IsVisible = !blend && !inside;
        _full.IsVisible = blend;
        _inside.IsVisible = inside;
        _samples.IsVisible = _degree.IsVisible = _density.IsVisible = _iter.IsVisible = inside && _inside.Index == 1;
    }

    /// <summary>Two surface edges that do not touch: there is no closed loop, so the surface spans the gap between them.</summary>
    bool IsBlend()
    {
        if (_boundary.Items.Count != 2 || _guides.Items.Count > 0 || _boundary.Items.Any(i => i.Kind != SelKind.Edge || !HasEdgeSettings(i))) return false;
        var a = Occ.EdgeInfoOf(S.ShapeOf(_boundary.Items[0])!); var b = Occ.EdgeInfoOf(S.ShapeOf(_boundary.Items[1])!);
        foreach (var p in new[] { a.Start, a.End }) foreach (var q in new[] { b.Start, b.End }) if (p.DistanceTo(q) < 1e-6) return false;
        return true;
    }

    /// <summary>Keep one continuity + weight row per param-bearing boundary item; values persist via <see cref="_edgeCfg"/>.</summary>
    void SyncItemParams()
    {
        if (_syncing) return;
        _syncing = true;
        try
        {
            var items = _boundary.Items.Where(HasEdgeSettings).ToList();
            var saved = _edgeCfg.Value.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim().Split(':'))
                .Select(a => (
                    a.Length > 0 && int.TryParse(a[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int g) ? g : 1,
                    a.Length > 1 && double.TryParse(a[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double w) ? w : 0.5))
                .ToList();
            while (_pairs.Count < items.Count)
            {
                var g = new ChoiceParam { Label = "Continuity", Section = "Boundary", Options = ["Position (G0)", "Tangent (G1)", "Curvature (G2)"], Index = 1, Changed = OnChanged };
                var w = new NumberParam { Label = "Weight", Section = "Boundary", Value = 0.5, Min = 0, Max = 2, Step = 0.1, Unit = "", Changed = OnChanged };
                _pairs.Add((g, w));
                Params.Add(g); Params.Add(w); PanelSettings.Add(g); PanelSettings.Add(w);
            }
            while (_pairs.Count > items.Count)
            {
                var (g, w) = _pairs[^1]; _pairs.RemoveAt(_pairs.Count - 1);
                Params.Remove(g); Params.Remove(w); PanelSettings.Remove(g); PanelSettings.Remove(w);
            }
            _pairOf.Clear();
            for (int i = 0; i < items.Count; i++)
            {
                _pairOf.Add(items[i]);
                var (g, w) = _pairs[i];
                g.Caption = S.Describe(items[i]) + " · continuity";
                w.Caption = S.Describe(items[i]) + " · weight";
                if (i < saved.Count)
                {
                    if (g.Index != saved[i].Item1) g.SetSilently(Math.Clamp(saved[i].Item1, 0, 2));
                    if (Math.Abs(w.Value - saved[i].Item2) > 1e-9) w.Value = saved[i].Item2;
                }
            }
            var csv = string.Join(", ", _pairs.Select(p => p.g.Index + ":" + p.w.Value.ToString(CultureInfo.InvariantCulture)));
            if (_edgeCfg.Value != csv) _edgeCfg.Value = csv;
        }
        finally { _syncing = false; }
    }

    int PairIndex(SelItem it) => _pairOf.IndexOf(it);

    void AddSupported(FillReq f, SceneObject obj, int edgeId, SelItem item)
    {
        if (!f.Sources.Contains(obj)) f.Sources.Add(obj);
        f.Hosts.Add(obj.Shape); f.Ids.Add(edgeId);
        int p = PairIndex(item);
        f.Orders.Add(p >= 0 ? _pairs[p].g.Index : 0);
        f.Weights.Add(p >= 0 ? _pairs[p].w.Value : 0);
    }

    FillReq OpeningFill(SceneObject obj, List<int> loop, SelItem item)
    {
        var f = new FillReq();
        foreach (int id in loop) AddSupported(f, obj, id, item);
        return f;
    }

    /// <summary>Turn the picks into one fill request per boundary loop.</summary>
    List<FillReq> Resolve()
    {
        var fills = new List<FillReq>();
        var explicitFill = new FillReq();
        foreach (var it in _boundary.Items)
        {
            var obj = S.Find(it.Obj) ?? throw new FormatException("That object is gone.");
            if (it.Kind == SelKind.Object && obj.Kind == ObjKind.Surface)
            {
                foreach (var loop in SurfaceUtil.OpeningLoops(obj.Shape))
                    fills.Add(OpeningFill(obj, loop, it));
            }
            else if (it.Kind == SelKind.Edge && obj.HasBrep && obj.Shape.Count(SubKind.Face) > 0)
            {
                // A single picked edge means "the opening it borders" (Fill Opening behaviour); more picks are the boundary itself.
                if (_boundary.Items.Count == 1)
                {
                    try { fills.AddRange(SurfaceUtil.OpeningLoops(obj.Shape, it.Index).Select(l => OpeningFill(obj, l, it))); }
                    catch (FormatException) { AddSupported(explicitFill, obj, it.Index, it); }
                }
                else AddSupported(explicitFill, obj, it.Index, it);
            }
            else explicitFill.Free.Add(SurfaceUtil.CurveShape(S, it));
        }
        if (explicitFill.Hosts.Count + explicitFill.Free.Count > 0) fills.Add(explicitFill);
        return fills;
    }

    Shape FillOnce(FillReq f, List<Shape> guides)
    {
        bool freeLoop = f.Hosts.Count == 0 && guides.Count == 0;
        if (_fill.Index == 1 && !freeLoop) throw new FormatException("Flat surface works on a loop of free curves or sketches, with no surface edges or guide curves.");
        if (freeLoop && _fill.Index != 2)
        {
            Shape? flat = null;
            try { flat = Occ.Profile(f.Free, true); } catch (KernelException) when (_fill.Index == 0) { }
            if (flat is { HasFaces: true }) return flat;
            if (_fill.Index == 1) throw new FormatException("The curves do not form a closed flat loop. Choose Smooth surface for a bent boundary.");
        }
        // Pure curve boundaries keep the proven Edge Surface behaviour (ruled / Coons); everything else uses the plate fill.
        if (f.Hosts.Count == 0 && f.Free.Count is >= 2 and <= 4 && guides.Count == 0)
            return Occ.EdgeSurface(f.Free);
        if (guides.Count > 0)
        {
            if (_inside.Index == 1) return Fitted(f, guides);
            try { return Occ.BoundaryFill(f.Hosts, f.Ids.ToArray(), f.Orders.ToArray(), f.Weights.ToArray(), f.Free, guides); }
            catch (KernelException)
            {
                Results.Add(new ResultLine("Inside curves", "could not be followed exactly, so the surface was fitted through points on them", 2));
                return Fitted(f, guides);
            }
        }
        return Occ.BoundaryFill(f.Hosts, f.Ids.ToArray(), f.Orders.ToArray(), f.Weights.ToArray(), f.Free, guides);
    }

    /// <summary>A surface that follows the outline exactly and passes close to points sampled on the inside curves.</summary>
    Shape Fitted(FillReq f, List<Shape> guides)
    {
        var outline = f.Free.Concat(f.Hosts.Select((h, i) => h.Sub(SubKind.Edge, f.Ids[i]))).ToList();
        var points = new List<Vec3>();
        foreach (var g in guides) points.AddRange(Occ.CurveDivide(g, _samples.Value).points);
        var shape = Occ.PatchThrough(outline, points, _degree.Value, _density.Value, _iter.Value, 1e-3);
        // a plate through few points can balloon far outside the outline and the points it is meant to follow; refuse that instead of returning a wrong surface
        var box = Occ.Compound(outline).Props(); var res = shape.Props();
        var lo = box.Min; var hi = box.Max;
        foreach (var p in points) { lo = new Vec3(Math.Min(lo.X, p.X), Math.Min(lo.Y, p.Y), Math.Min(lo.Z, p.Z)); hi = new Vec3(Math.Max(hi.X, p.X), Math.Max(hi.Y, p.Y), Math.Max(hi.Z, p.Z)); }
        double over = new[] { lo.X - res.Min.X, lo.Y - res.Min.Y, lo.Z - res.Min.Z, res.Max.X - hi.X, res.Max.Y - hi.Y, res.Max.Z - hi.Z, 0 }.Max();
        Results.Add(new ResultLine("Overshoot past the curves", $"{over:0.###} mm", over > 0.15 * (hi - lo).Length ? 3 : 1));
        if (over > 0.15 * (hi - lo).Length) throw new FormatException("The surface bulges far outside its curves. Add more curves across it, or raise the points taken along each curve.");
        return shape;
    }

    /// <summary>Span the gap between two surface edges. The lower of the two continuities is used, as the blend has one setting for both ends.</summary>
    Shape Blend()
    {
        var a = _boundary.Items[0]; var b = _boundary.Items[1];
        int order = Math.Min(_pairs[PairIndex(a)].g.Index, _pairs[PairIndex(b)].g.Index);
        return Occ.BlendSurface(S.Find(a.Obj)!.Shape, a.Index, S.Find(b.Obj)!.Shape, b.Index, order, _full.Value);
    }

    protected override CommandOutput? Build()
    {
        Results.Clear();
        if (_boundary.Items.Count == 0) return null;
        var guides = _guides.Items.Select(i => SurfaceUtil.CurveShape(S, i)).ToList();
        bool blend = IsBlend();
        var fills = blend ? [] : Resolve();
        var patches = new List<Shape>();
        if (blend) patches.Add(Blend());
        foreach (var f in fills)
        {
            Shape? patch;
            try { patch = FillOnce(f, guides); }
            catch (KernelException first)
            {
                patch = null;
                // Plate fills can refuse a high continuity: step every edge down a level and retry until it works or G0 fails too.
                while (patch == null && f.Orders.Any(x => x > 0))
                {
                    for (int k = 0; k < f.Orders.Count; k++) if (f.Orders[k] > 0) f.Orders[k]--;
                    try { patch = Occ.BoundaryFill(f.Hosts, f.Ids.ToArray(), f.Orders.ToArray(), f.Weights.ToArray(), f.Free, guides); }
                    catch (KernelException) { }
                }
                if (patch == null) { Results.Add(new ResultLine("Boundary", "could not be filled: " + first.Message, 2)); continue; }
                Results.Add(new ResultLine("Boundary", "filled with a lower continuity than requested", 2));
            }
            patches.Add(patch);
        }
        if (patches.Count == 0) throw new FormatException("No boundary could be filled. Try lower continuity or a different selection.");
        if (patches.Count < fills.Count) Results.Add(new ResultLine("Boundaries filled", $"{patches.Count} of {fills.Count}", 2));
        var sources = blend ? _boundary.Items.Select(i => S.Find(i.Obj)!).Distinct().ToList() : fills.SelectMany(f => f.Sources).Distinct().ToList();
        var o = new CommandOutput { Label = "Boundary surface", Icon = "patch" };
        if (_join.Value && sources.Count > 0)
        {
            var sewn = Occ.Sew(sources.Select(x => x.Shape).Concat(patches).ToList(), _tol.Value, true);
            if (sources.Count == 1) o.Replace.Add((sources[0], sewn));
            else
            {
                o.Remove.AddRange(sources);
                o.Add.Add(new(sewn, sewn.HasSolids ? "Body" : "Boundary surface", sewn.HasSolids ? ObjKind.Solid : ObjKind.Surface));
            }
            Results.Add(new ResultLine("Result", sewn.HasSolids ? "closed into a solid" : "joined to the surface", 1));
        }
        else foreach (var p in patches) o.Add.Add(new(p, "Boundary surface", ObjKind.Surface));
        return o;
    }
}
