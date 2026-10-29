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
        var f = Occ.Profile(shapes, true);
        if (!f.HasFaces) throw new KernelException("The selected curves do not form a closed planar loop.");
        return SurfaceUtil.Single(f);
    }
}

public sealed class RuledSurfaceCommand : Command
{
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

/// <summary>Fills a closed boundary of curves with a smooth patch.</summary>
public sealed class PatchCommand : Command
{
    readonly SelectionSlot _edges;
    public PatchCommand() { _edges = Slot(new SelectionSlot { Label = "Boundary curves", Filter = SelFilter.AnyCurve | SelFilter.Edge, Multi = true }); }
    public override string Title => "Patch";
    public override string Hint => "Pick the curves/edges that surround the hole to fill.";
    protected override CommandOutput? Build()
    {
        if (!Complete) return null;
        var edges = _edges.Items.Select(i => SurfaceUtil.CurveShape(S, i)).ToList();
        return SurfaceUtil.Single(Occ.Patch(edges));
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
