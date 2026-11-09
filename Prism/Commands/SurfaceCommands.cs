using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core;
using Prism.Kernel;

namespace Prism.Commands;

static class SurfaceUtil
{
    public static Shape CurveShape(CadSession s, SelItem item)
    {
        if (item.Kind == SelKind.Edge) return s.ShapeOf(item)!;
        return Occ.Profile([s.Find(item.Obj)!.Shape], false);
    }

    /// <summary>Open-boundary loops of a surface as edge-index lists. With a start index, only the loop that edge belongs to is returned.</summary>
    public static List<List<int>> OpeningLoops(Shape shape, int start = -1)
    {
        var free = Occ.FreeEdges(shape);
        if (free.Length == 0) throw new FormatException("This surface has no open edges: there is nothing to fill.");
        var info = free.ToDictionary(i => i, i => Occ.EdgeInfoOf(shape.Sub(SubKind.Edge, i)));
        var loops = new List<List<int>>(); var used = new HashSet<int>();
        IEnumerable<int> starts = start >= 0 ? (free.Contains(start) ? [start] : throw new FormatException("That edge is not an open edge. Click an edge on the border of a hole.")) : free;
        foreach (int s0 in starts)
        {
            if (used.Contains(s0)) continue;
            var loop = new List<int> { s0 }; used.Add(s0);
            var end = info[s0].End; var begin = info[s0].Start;
            for (int guard = 0; guard < 10000 && end.DistanceTo(begin) > 1e-3; guard++)
            {
                int next = -1; bool flip = false;
                foreach (int c in free)
                {
                    if (used.Contains(c)) continue;
                    if (info[c].Start.DistanceTo(end) < 1e-3) { next = c; flip = false; break; }
                    if (info[c].End.DistanceTo(end) < 1e-3) { next = c; flip = true; break; }
                }
                if (next < 0) break;
                loop.Add(next); used.Add(next); end = flip ? info[next].Start : info[next].End;
            }
            loops.Add(loop);
        }
        return loops;
    }

    public static CommandOutput Single(Shape shape, string name = "Surface", ObjKind kind = ObjKind.Surface)
    {
        var o = new CommandOutput();
        o.Add.Add(new(shape, name, kind));
        return o;
    }
}

/// <summary>Planar surface bounded by closed sketch/curve loops.</summary>
public sealed class PlanarSurfaceCommand : Command
{
    readonly SelectionSlot _loops;
    public PlanarSurfaceCommand() { _loops = Slot(new SelectionSlot { Label = "Closed boundary", Filter = SelFilter.SketchObj | SelFilter.CurveObj, Multi = true }); }
    public override string Title => "Planar Surface";
    public override string Hint => "Pick closed sketches or curves. Nested loops become holes.";
    protected override CommandOutput? Build()
    {
        if (!Complete) return null;
        var shapes = ObjectsOf(_loops).Select(o => o.Shape).ToList();
        var f = ProfileUtil.Closed(shapes);
        if (!f.HasFaces) throw new KernelException("The selected curves do not form a closed planar loop.");
        return SurfaceUtil.Single(f);
    }
}

public sealed class RuledSurfaceCommand : Command
{
    /// <summary>Picks edges, curves or faces spread around the model, which is hard in an orthographic view: start in perspective at the home view.</summary>
    public override bool WantsHomeView => true;
    readonly SelectionSlot _a, _b;
    public RuledSurfaceCommand()
    {
        _a = Slot(new SelectionSlot { Label = "First curve", Filter = SelFilter.AnyCurve | SelFilter.Edge });
        _b = Slot(new SelectionSlot { Label = "Second curve", Filter = SelFilter.AnyCurve | SelFilter.Edge });
    }
    public override string Title => "Ruled Surface";
    public override string Hint => "Pick two curves; a surface of straight lines joins them.";
    protected override CommandOutput? Build()
    {
        if (!Complete) return null;
        return SurfaceUtil.Single(Occ.Ruled(SurfaceUtil.CurveShape(S, _a.Items[0]), SurfaceUtil.CurveShape(S, _b.Items[0])));
    }
}

/// <summary>
/// Fills a closed boundary of curves, sketches or edges. A flat loop gets an exact flat surface (nested loops become holes); anything bent gets a
/// smooth patch. "Fill with" can force either one. This replaces the separate Planar Surface tool, which stays available to scripts.
/// </summary>
public sealed class PatchCommand : Command
{
    /// <summary>Picks edges, curves or faces spread around the model, which is hard in an orthographic view: start in perspective at the home view.</summary>
    public override bool WantsHomeView => true;
    readonly SelectionSlot _edges;
    readonly ChoiceParam _kind;
    public PatchCommand()
    {
        _edges = Slot(new SelectionSlot { Label = "Boundary: curves, sketches or edges", Filter = SelFilter.AnyCurve | SelFilter.Edge | SelFilter.SketchObj, Multi = true, SelectionRuleIndex = 1 });
        _kind = Choice("Fill with", 0, "Automatic (flat if the loop is flat)", "Flat surface", "Smooth patch");
    }
    public override string Title => "Patch";
    public override string Hint => "Pick the curves, sketches or edges that surround the area to fill. A flat loop becomes a flat surface (loops inside it become holes); a bent loop becomes a smooth patch. Use “Fill with” to choose.";
    protected override CommandOutput? Build()
    {
        if (!Complete) return null;
        var shapes = _edges.Items.Select(i => i.Kind == SelKind.Object && S.Find(i.Obj) is { } o ? o.Shape : SurfaceUtil.CurveShape(S, i)).ToList();
        if (_kind.Index != 2)
        {
            Shape? flat = null;
            try { flat = ProfileUtil.Closed(shapes); } catch (KernelException) when (_kind.Index == 0) { }
            if (flat is { HasFaces: true }) return SurfaceUtil.Single(flat);
            if (_kind.Index == 1) throw new KernelException("The selected curves do not form a closed flat loop. Choose Smooth patch for a bent boundary.");
        }
        return SurfaceUtil.Single(Occ.Patch(shapes));
    }
}

public sealed class OffsetSurfaceCommand : Command
{
    readonly SelectionSlot _src;
    readonly NumberParam _d;
    public OffsetSurfaceCommand()
    {
        _src = Slot(new SelectionSlot { Label = "Surface or face", Filter = SelFilter.SurfaceObj | SelFilter.Face });
        _d = Num("Offset distance", 5);
    }
    public override string Title => "Offset Surface";
    public override string Hint => "Pick a surface or a face, then pull the orange arrow or type a distance. Negative distance offsets the other way.";
    public override HandleSpec? Handle => _src.Items.Count == 0 ? null : SurfaceHandle(_src.Items[0], _d);
    protected override CommandOutput? Build()
    {
        if (!Complete) return null;
        return SurfaceUtil.Single(Occ.Offset(S.ShapeOf(_src.Items[0])!, _d.Value), "Offset");
    }
}

public sealed class ThickenCommand : Command
{
    readonly SelectionSlot _src;
    readonly NumberParam _t;
    public ThickenCommand()
    {
        _src = Slot(new SelectionSlot { Label = "Surface", Filter = SelFilter.SurfaceObj });
        _t = Num("Thickness", 3);
    }
    public override string Title => "Thicken";
    public override string Hint => "Pick a surface to turn into a solid, then pull the orange arrow or type a thickness. Negative thickness goes the other way.";
    public override HandleSpec? Handle => _src.Items.Count == 0 ? null : SurfaceHandle(_src.Items[0], _t);
    protected override CommandOutput? Build()
    {
        if (!Complete) return null;
        var obj = S.Find(_src.Items[0].Obj)!;
        var o = new CommandOutput();
        o.Remove.Add(obj);
        var thick = Occ.Thicken(obj.Shape, _t.Value);
        if (!thick.IsValid) throw new FormatException("Thickening gave an invalid solid. The surface probably has a sharp point, a very tight bend (tighter than the thickness) or overlapping edges: smooth those out, or use a smaller thickness.");
        o.Add.Add(new(thick, "Thickened", ObjKind.Solid));
        return o;
    }
}

public sealed class SewCommand : Command
{
    readonly SelectionSlot _surfaces;
    readonly NumberParam _tol;
    readonly BoolParam _solid;
    public SewCommand()
    {
        _surfaces = Slot(new SelectionSlot { Label = "Surfaces", Filter = SelFilter.SurfaceObj, Multi = true });
        _tol = Num("Tolerance", 0.01, 1e-6, 10, 0.01);
        _solid = Check("Make solid if closed", true);
    }
    public override string Title => "Knit";
    public override string Hint => "Pick the surfaces to join along their shared edges.";
    protected override CommandOutput? Build()
    {
        if (_surfaces.Items.Count < 1) return null;
        var objs = ObjectsOf(_surfaces).ToList();
        var res = Occ.Sew(objs.Select(o => o.Shape).ToList(), _tol.Value, _solid.Value);
        var o = new CommandOutput();
        o.Remove.AddRange(objs);
        o.Add.Add(new(res, res.HasSolids ? "Body" : "Knit"));
        return o;
    }
}
