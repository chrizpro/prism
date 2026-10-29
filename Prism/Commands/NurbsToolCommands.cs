using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Prism.Core;
using Prism.Kernel;

namespace Prism.Commands;

/// <summary>Shared helpers for the curve and surface tools in this file.</summary>
static class NurbsUtil
{
    public const SelFilter CurveIn = SelFilter.AnyCurve | SelFilter.Edge;
    public const SelFilter SurfaceIn = SelFilter.SurfaceObj | SelFilter.Solid | SelFilter.Face;

    public static Shape Curve(CadSession s, SelItem item) => SurfaceUtil.CurveShape(s, item);

    /// <summary>Every separate wire (loop or chain) of a curve or sketch; a lone edge or wire counts as one.</summary>
    public static List<Shape> Wires(Shape shape)
    {
        int n = shape.Count(SubKind.Wire);
        return n == 0 ? [shape] : Enumerable.Range(0, n).Select(i => shape.Sub(SubKind.Wire, i)).ToList();
    }

    public static Shape OfItem(CadSession s, SelItem item) => item.Kind is SelKind.Face or SelKind.Edge ? s.ShapeOf(item)! : s.Find(item.Obj)!.Shape;

    public static CommandOutput Curves(IEnumerable<Shape> shapes, string name = "Curve")
    {
        var o = new CommandOutput();
        foreach (var s in shapes) o.Add.Add(new(s, name, ObjKind.Curve));
        return o;
    }

    /// <summary>Drop the objects a tool consumed (only whole objects, never single edges of a body).</summary>
    public static void RemoveSources(CadSession s, CommandOutput o, IEnumerable<SelItem> items)
    {
        foreach (var obj in items.Where(i => i.Kind == SelKind.Object).Select(i => s.Find(i.Obj)).Where(x => x != null).Distinct()) o.Remove.Add(obj!);
    }

    /// <summary>Both end points of every edge run of a curve, each with the direction leaving the curve and the curvature vector there.</summary>
    public static List<(Vec3 p, Vec3 outward, Vec3 k, bool junction)> Ends(Shape curve)
    {
        var res = new List<(Vec3, Vec3, Vec3, bool)>();
        var edges = Occ.CurveSamples(curve, 200, 64);
        for (int e = 0; e < edges.Count; e++)
        {
            var a = edges[e];
            var start = (a[0].p, (a[0].p - a[2].p).Normalized(), a[0].k);
            var end = (a[^1].p, (a[^1].p - a[^3].p).Normalized(), a[^1].k);
            // an end that touches another edge of the same curve is a joint, not a free end
            bool joinedStart = edges.Where((_, j) => j != e).Any(b => b[0].p.DistanceTo(start.Item1) < 1e-5 || b[^1].p.DistanceTo(start.Item1) < 1e-5);
            bool joinedEnd = edges.Where((_, j) => j != e).Any(b => b[0].p.DistanceTo(end.Item1) < 1e-5 || b[^1].p.DistanceTo(end.Item1) < 1e-5);
            res.Add((start.Item1, start.Item2, start.Item3, joinedStart));
            res.Add((end.Item1, end.Item2, end.Item3, joinedEnd));
        }
        return res;
    }

    public static Vec3 DirectionOf(int choice) => choice switch { 0 => -Vec3.UnitZ, 1 => Vec3.UnitZ, 2 => -Vec3.UnitX, 3 => Vec3.UnitX, 4 => -Vec3.UnitY, _ => Vec3.UnitY };
    public static readonly string[] DirectionNames = ["Down (-Z)", "Up (+Z)", "Left (-X)", "Right (+X)", "Back (-Y)", "Front (+Y)"];
}

// =================================================================== curve editing
/// <summary>Round or chamfer the corners of a flat curve.</summary>
public sealed class CurveFilletCommand : Command
{
    readonly bool _chamfer;
    readonly SelectionSlot _curve;
    readonly NumberParam _size;
    readonly BoolParam _keep;

    public CurveFilletCommand(bool chamfer)
    {
        _chamfer = chamfer;
        _curve = Slot(new SelectionSlot { Label = "Flat curve or sketch", Filter = SelFilter.AnyCurve | SelFilter.Edge });
        _size = Num(chamfer ? "Chamfer distance" : "Fillet radius", chamfer ? 3 : 5, 0.001);
        _keep = Check("Keep the original curve", true);
    }
    public override string Title => _chamfer ? "Chamfer Curve" : "Fillet Curve";
    public override string Icon => _chamfer ? "chamfer" : "fillet";
    public override string Hint => "Pick a flat curve or sketch: every corner is rounded (or cut) by the size you set. An open curve keeps its two ends. The curve has to lie in one plane.";
    protected override CommandOutput? Build()
    {
        if (!Complete) return null;
        var item = _curve.Items[0];
        var o = NurbsUtil.Curves(NurbsUtil.Wires(NurbsUtil.Curve(S, item)).Select(w => Occ.WireFillet(w, _size.Value, _chamfer)).ToList(), _chamfer ? "Chamfered curve" : "Filleted curve");
        if (!_keep.Value) NurbsUtil.RemoveSources(S, o, [item]);
        return o;
    }
}

/// <summary>Parallel copy of a flat curve.</summary>
public sealed class OffsetCurveCommand : Command
{
    readonly SelectionSlot _curve;
    readonly NumberParam _d;
    readonly ChoiceParam _corners;
    public OffsetCurveCommand()
    {
        _curve = Slot(new SelectionSlot { Label = "Flat curve or sketch", Filter = SelFilter.AnyCurve | SelFilter.Edge });
        _d = Num("Offset distance", 5);
        _corners = Choice("Corners", 0, "Rounded", "Sharp");
    }
    public override string Title => "Offset Curve";
    public override string Icon => "offset";
    public override string Hint => "Copy a flat curve parallel to itself. A positive distance goes one way, a negative one the other; closed curves grow or shrink.";
    protected override CommandOutput? Build()
    {
        if (!Complete) return null;
        return NurbsUtil.Curves(NurbsUtil.Wires(NurbsUtil.Curve(S, _curve.Items[0])).Select(w => Occ.OffsetPlanar(w, _d.Value, _corners.Index == 1)).ToList(), "Offset curve");
    }
}

/// <summary>A curve that joins two curves with position, tangent or curvature continuity.</summary>
public sealed class BlendCurveCommand : Command
{
    readonly SelectionSlot _a, _b;
    readonly ChoiceParam _cont;
    readonly NumberParam _bulgeA, _bulgeB;
    public BlendCurveCommand()
    {
        _a = Slot(new SelectionSlot { Label = "First curve", Filter = NurbsUtil.CurveIn });
        _b = Slot(new SelectionSlot { Label = "Second curve", Filter = NurbsUtil.CurveIn });
        _cont = Choice("Continuity", 1, "Position (G0)", "Tangent (G1)", "Curvature (G2)");
        _bulgeA = Num("Bulge at first curve", 1, 0.05, 5, 0.1, "");
        _bulgeB = Num("Bulge at second curve", 1, 0.05, 5, 0.1, "");
    }
    public override string Title => "Blend Curve";
    public override string Icon => "curve-through";
    public override string Hint => "Pick two curves; the nearest ends are joined by a new curve. Tangent keeps their direction, curvature also keeps their bend. Bulge makes the blend fuller or tighter at each end.";
    protected override CommandOutput? Build()
    {
        if (!Complete) return null;
        var ea = NurbsUtil.Ends(NurbsUtil.Curve(S, _a.Items[0])).Where(e => !e.junction).ToList();
        var eb = NurbsUtil.Ends(NurbsUtil.Curve(S, _b.Items[0])).Where(e => !e.junction).ToList();
        if (ea.Count == 0 || eb.Count == 0) throw new KernelException("A closed curve has no ends to blend.");
        var (A, B) = ea.SelectMany(x => eb.Select(y => (x, y))).OrderBy(t => t.x.p.DistanceTo(t.y.p)).First();
        double L = A.p.DistanceTo(B.p);
        if (L < 1e-6) throw new KernelException("The curves already touch there.");
        Shape curve;
        if (_cont.Index == 0) curve = Occ.Line(A.p, B.p);
        else if (_cont.Index == 1)
        {
            double a = L * _bulgeA.Value / 3, b = L * _bulgeB.Value / 3;
            curve = Occ.NurbsCurve([A.p, A.p + A.outward * a, B.p + B.outward * b, B.p], 3);
        }
        else
        {
            double sa = L * _bulgeA.Value, sb = L * _bulgeB.Value;           // speeds at the two ends
            var p1 = A.p + A.outward * (sa / 5); var p2 = p1 * 2 - A.p + A.k * (sa * sa / 20);
            var p4 = B.p + B.outward * (sb / 5); var p3 = p4 * 2 - B.p + B.k * (sb * sb / 20);
            curve = Occ.NurbsCurve([A.p, p1, p2, p3, p4, B.p], 5);
        }
        return NurbsUtil.Curves([curve], "Blend curve");
    }
}

/// <summary>Join curves that touch end to end into one.</summary>
public sealed class JoinCurvesCommand : Command
{
    readonly SelectionSlot _curves;
    readonly BoolParam _keep;
    public JoinCurvesCommand()
    {
        _curves = Slot(new SelectionSlot { Label = "Curves to join", Filter = NurbsUtil.CurveIn, Multi = true });
        _keep = Check("Keep the originals", false);
    }
    public override string Title => "Join Curves";
    public override string Icon => "knit";
    public override string Hint => "Pick curves whose ends touch; they become one curve. Curves that do not touch stay separate pieces.";
    protected override CommandOutput? Build()
    {
        if (!Complete) return null;
        var joined = Occ.Profile(_curves.Items.Select(i => NurbsUtil.Curve(S, i)).ToList(), false);
        var o = NurbsUtil.Curves([joined], "Joined curve");
        if (!_keep.Value) NurbsUtil.RemoveSources(S, o, _curves.Items);
        Results.Clear(); Results.Add(new ResultLine("Pieces", $"{joined.Count(SubKind.Wire)} curve(s) from {joined.Count(SubKind.Edge)} segments"));
        return o;
    }
}

/// <summary>Break a curve into its segments.</summary>
public sealed class ExplodeCurveCommand : Command
{
    readonly SelectionSlot _curve;
    readonly BoolParam _keep;
    public ExplodeCurveCommand()
    {
        _curve = Slot(new SelectionSlot { Label = "Curve or sketch", Filter = SelFilter.AnyCurve });
        _keep = Check("Keep the original", false);
    }
    public override string Title => "Explode Curve";
    public override string Icon => "unstitch";
    public override string Hint => "Break a joined curve or a sketch into separate curves, one per segment.";
    protected override CommandOutput? Build()
    {
        if (!Complete) return null;
        var shape = NurbsUtil.Curve(S, _curve.Items[0]);
        int n = shape.Count(SubKind.Edge);
        if (n < 2) throw new KernelException("This curve is a single segment already.");
        var o = NurbsUtil.Curves(Enumerable.Range(0, n).Select(i => shape.Sub(SubKind.Edge, i)).ToList(), "Segment");
        if (!_keep.Value) NurbsUtil.RemoveSources(S, o, _curve.Items);
        Results.Clear(); Results.Add(new ResultLine("Segments", n.ToString()));
        return o;
    }
}

/// <summary>Keep part of a curve.</summary>
public sealed class TrimCurveCommand : Command
{
    readonly SelectionSlot _curve;
    readonly NumberParam _from, _to;
    readonly BoolParam _keep;
    public TrimCurveCommand()
    {
        _curve = Slot(new SelectionSlot { Label = "Curve (a single segment)", Filter = NurbsUtil.CurveIn });
        _from = Num("Keep from (%)", 0, 0, 100, 5, "%");
        _to = Num("Keep to (%)", 70, 0, 100, 5, "%");
        _keep = Check("Keep the original", false);
    }
    public override string Title => "Trim Curve";
    public override string Icon => "trim";
    public override string Hint => "Keep the part of the curve between two positions, given as a percentage of its length range.";
    protected override CommandOutput? Build()
    {
        if (!Complete) return null;
        var shape = NurbsUtil.Curve(S, _curve.Items[0]);
        if (shape.Count(SubKind.Edge) != 1) throw new KernelException("Trim works on one segment. Use Explode Curve first.");
        var o = NurbsUtil.Curves([Occ.CurveTrim(shape, _from.Value / 100, _to.Value / 100)], "Trimmed curve");
        if (!_keep.Value) NurbsUtil.RemoveSources(S, o, _curve.Items);
        return o;
    }
}

/// <summary>Cut a curve in two or more pieces.</summary>
public sealed class SplitCurveCommand : Command
{
    readonly SelectionSlot _curve;
    readonly TextParam _at;
    readonly BoolParam _keep;
    public SplitCurveCommand()
    {
        _curve = Slot(new SelectionSlot { Label = "Curve (a single segment)", Filter = NurbsUtil.CurveIn });
        _at = Text("Split at (% , separated)", "50", "e.g. 25, 50, 75");
        _keep = Check("Keep the original", false);
    }
    public override string Title => "Split Curve";
    public override string Icon => "trim";
    public override string Hint => "Cut a curve at one or more positions, given as percentages along it.";
    protected override CommandOutput? Build()
    {
        if (!Complete) return null;
        var shape = NurbsUtil.Curve(S, _curve.Items[0]);
        if (shape.Count(SubKind.Edge) != 1) throw new KernelException("Split works on one segment. Use Explode Curve first.");
        var cuts = _at.Value.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(t => double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v / 100 : double.NaN)
            .Where(v => v > 0.001 && v < 0.999).Distinct().OrderBy(v => v).ToList();
        if (cuts.Count == 0) throw new KernelException("Give one or more positions between 0 and 100 percent.");
        var edges = new List<double> { 0 }; edges.AddRange(cuts); edges.Add(1);
        var pieces = Enumerable.Range(0, edges.Count - 1).Select(i => Occ.CurveTrim(shape, edges[i], edges[i + 1])).ToList();
        var o = NurbsUtil.Curves(pieces, "Curve piece");
        if (!_keep.Value) NurbsUtil.RemoveSources(S, o, _curve.Items);
        Results.Clear(); Results.Add(new ResultLine("Pieces", pieces.Count.ToString()));
        return o;
    }
}

/// <summary>Make a curve longer.</summary>
public sealed class ExtendCurveCommand : Command
{
    readonly SelectionSlot _curve;
    readonly ChoiceParam _end;
    readonly NumberParam _len;
    readonly BoolParam _keep;
    public ExtendCurveCommand()
    {
        _curve = Slot(new SelectionSlot { Label = "Curve (a single segment)", Filter = NurbsUtil.CurveIn });
        _end = Choice("Extend", 1, "Start", "End", "Both ends");
        _len = Num("Length", 10, 0.001);
        _keep = Check("Keep the original", false);
    }
    public override string Title => "Extend Curve";
    public override string Icon => "extend-surface";
    public override string Hint => "Grow a curve past its end in the direction it was heading, keeping it smooth.";
    protected override CommandOutput? Build()
    {
        if (!Complete) return null;
        var shape = NurbsUtil.Curve(S, _curve.Items[0]);
        if (shape.Count(SubKind.Edge) != 1) throw new KernelException("Extend works on one segment. Use Explode Curve first.");
        var r = shape;
        if (_end.Index != 1) r = Occ.CurveExtend(r, false, _len.Value);
        if (_end.Index != 0) r = Occ.CurveExtend(r, true, _len.Value);
        var o = NurbsUtil.Curves([r], "Extended curve");
        if (!_keep.Value) NurbsUtil.RemoveSources(S, o, _curve.Items);
        Results.Clear(); Results.Add(new ResultLine("New length", $"{Occ.CurveLength(r):0.###} mm"));
        return o;
    }
}

/// <summary>Evenly spaced points along a curve.</summary>
public sealed class DivideCurveCommand : Command
{
    readonly SelectionSlot _curve;
    readonly IntParam _count;
    public DivideCurveCommand()
    {
        _curve = Slot(new SelectionSlot { Label = "Curve", Filter = NurbsUtil.CurveIn });
        _count = Add(new IntParam { Label = "Segments", Value = 10, Min = 1, Max = 500 });
    }
    public override string Title => "Divide Curve";
    public override string Icon => "origin-point";
    public override string Hint => "Place points at equal distances along a curve. The length of the curve is reported.";
    protected override CommandOutput? Build()
    {
        if (!Complete) return null;
        var (pts, len) = Occ.CurveDivide(NurbsUtil.Curve(S, _curve.Items[0]), _count.Value);
        Results.Clear();
        Results.Add(new ResultLine("Curve length", $"{len:0.###} mm"));
        Results.Add(new ResultLine("Spacing", $"{len / _count.Value:0.###} mm"));
        var o = new CommandOutput();
        o.Add.Add(new(Occ.Compound(pts.Select(Occ.Vertex).ToList()), "Points", ObjKind.Curve));
        return o;
    }
}

/// <summary>Redraw a curve with a chosen number of control points.</summary>
public sealed class RebuildCurveCommand : Command
{
    readonly SelectionSlot _curve;
    readonly IntParam _points;
    readonly BoolParam _keep;
    public RebuildCurveCommand()
    {
        _curve = Slot(new SelectionSlot { Label = "Curve", Filter = NurbsUtil.CurveIn });
        _points = Add(new IntParam { Label = "Points", Value = 8, Min = 3, Max = 200 });
        _keep = Check("Keep the original", true);
    }
    public override string Title => "Rebuild Curve";
    public override string Icon => "raise-degree";
    public override string Hint => "Replace a curve with a smooth one through evenly spaced points. Fewer points make it simpler and fairer; more points follow the original more closely.";
    protected override CommandOutput? Build()
    {
        if (!Complete) return null;
        var src = NurbsUtil.Curve(S, _curve.Items[0]);
        var (pts, len) = Occ.CurveDivide(src, _points.Value - 1);
        bool closed = pts[0].DistanceTo(pts[^1]) < 1e-6;
        var list = closed ? pts.Take(pts.Length - 1).ToList() : pts.ToList();
        var rebuilt = Occ.Spline(list, closed);
        // how far the new curve strays from the old one
        double worst = 0; var dense = Occ.CurveDivide(src, 200).points;
        var check = Occ.CurveDivide(rebuilt, 400).points;
        foreach (var p in dense) worst = Math.Max(worst, check.Min(q => q.DistanceTo(p)));
        Results.Clear(); Results.Add(new ResultLine("Largest deviation", $"{worst:0.####} mm", worst < len * 0.01 ? 1 : 2));
        var o = NurbsUtil.Curves([rebuilt], "Rebuilt curve");
        if (!_keep.Value) NurbsUtil.RemoveSources(S, o, _curve.Items);
        return o;
    }
}

// =================================================================== curves from surfaces
/// <summary>Drop curves onto a surface.</summary>
public sealed class ProjectCurveCommand : Command
{
    readonly SelectionSlot _curves, _target;
    readonly ChoiceParam _method, _dir;
    public ProjectCurveCommand()
    {
        _curves = Slot(new SelectionSlot { Label = "Curves to project", Filter = NurbsUtil.CurveIn, Multi = true });
        _target = Slot(new SelectionSlot { Label = "Surface to project onto", Filter = NurbsUtil.SurfaceIn });
        _method = Choice("Method", 0, "Along a direction", "Pull normal to the surface");
        _dir = Choice("Direction", 0, NurbsUtil.DirectionNames);
    }
    public override string Title => "Project Curve";
    public override string Icon => "section";
    public override string Hint => "Project curves onto a surface or face: straight along a direction, or by pulling each point to the nearest spot on the surface.";
    protected override void UpdateVisibility() { if (_dir != null && _method != null) _dir.IsVisible = _method.Index == 0; }
    protected override CommandOutput? Build()
    {
        if (!Complete) return null;
        var curves = Occ.Compound(_curves.Items.Select(i => NurbsUtil.Curve(S, i)).ToList());
        var projected = Occ.ProjectCurve(curves, NurbsUtil.OfItem(S, _target.Items[0]), NurbsUtil.DirectionOf(_dir.Index), _method.Index == 1);
        Results.Clear(); Results.Add(new ResultLine("Projected segments", projected.Count(SubKind.Edge).ToString()));
        return NurbsUtil.Curves([projected], "Projected curve");
    }
}

/// <summary>Curves of constant U or V on a surface.</summary>
public sealed class IsoCurveCommand : Command
{
    readonly SelectionSlot _face;
    readonly ChoiceParam _dir;
    readonly NumberParam _pos;
    readonly IntParam _count;
    public IsoCurveCommand()
    {
        _face = Slot(new SelectionSlot { Label = "Surface or face", Filter = SelFilter.SurfaceObj | SelFilter.Face | SelFilter.Solid });
        _dir = Choice("Direction", 0, "U lines (constant U)", "V lines (constant V)");
        _pos = Num("Position (%)", 50, 0, 100, 5, "%");
        _count = Add(new IntParam { Label = "How many", Value = 1, Min = 1, Max = 60 });
    }
    public override string Title => "Isocurves";
    public override string Icon => "curve-through";
    public override string Hint => "Pull curves of constant U or V out of a surface. One curve goes at the position you set; several are spread evenly across the surface.";
    protected override void UpdateVisibility() { if (_pos != null && _count != null) _pos.IsVisible = _count.Value <= 1; }
    protected override CommandOutput? Build()
    {
        if (!Complete) return null;
        var face = NurbsUtil.OfItem(S, _face.Items[0]);
        int n = _count.Value;
        var fractions = n <= 1 ? [_pos.Value / 100] : Enumerable.Range(1, n).Select(i => i / (n + 1.0)).ToArray();
        return NurbsUtil.Curves(fractions.Select(t => Occ.Isocurve(face, _dir.Index == 1, t)).ToList(), "Isocurve");
    }
}

/// <summary>Curves where two surfaces cross.</summary>
public sealed class IntersectSurfacesCommand : Command
{
    readonly SelectionSlot _a, _b;
    public IntersectSurfacesCommand()
    {
        _a = Slot(new SelectionSlot { Label = "First surface or body", Filter = NurbsUtil.SurfaceIn });
        _b = Slot(new SelectionSlot { Label = "Second surface or body", Filter = NurbsUtil.SurfaceIn });
    }
    public override string Title => "Intersect Surfaces";
    public override string Icon => "section";
    public override string Hint => "Make curves along the lines where two surfaces (or bodies) cross.";
    protected override CommandOutput? Build()
    {
        if (!Complete) return null;
        var cut = Occ.Intersect(NurbsUtil.OfItem(S, _a.Items[0]), NurbsUtil.OfItem(S, _b.Items[0]));
        if (cut.Count(SubKind.Edge) == 0) throw new KernelException("Those two surfaces do not cross.");
        Results.Clear(); Results.Add(new ResultLine("Curve segments", cut.Count(SubKind.Edge).ToString()));
        return NurbsUtil.Curves([cut], "Intersection curve");
    }
}

/// <summary>Slice a body with evenly spaced planes into a stack of section curves.</summary>
public sealed class ContourCommand : Command
{
    readonly SelectionSlot _body;
    readonly ChoiceParam _axis;
    readonly NumberParam _step;
    public ContourCommand()
    {
        _body = Slot(new SelectionSlot { Label = "Body or surface", Filter = SelFilter.Solid | SelFilter.SurfaceObj });
        _axis = Choice("Slice along", 2, "X", "Y", "Z");
        _step = Num("Spacing", 5, 0.01);
    }
    public override string Title => "Contour";
    public override string Icon => "section";
    public override string Hint => "Cut a body or surface with parallel planes at a fixed spacing and keep the outlines as curves (for layered models, ribs and templates).";
    protected override CommandOutput? Build()
    {
        if (!Complete) return null;
        var shape = NurbsUtil.OfItem(S, _body.Items[0]); var pr = shape.Props();
        int axis = _axis.Index; var n = axis == 0 ? Vec3.UnitX : axis == 1 ? Vec3.UnitY : Vec3.UnitZ;
        double lo = axis == 0 ? pr.Min.X : axis == 1 ? pr.Min.Y : pr.Min.Z, hi = axis == 0 ? pr.Max.X : axis == 1 ? pr.Max.Y : pr.Max.Z;
        if ((hi - lo) / _step.Value > 300) throw new KernelException("That spacing would make more than 300 slices. Use a larger spacing.");
        var curves = new List<Shape>();
        for (double t = lo + _step.Value; t < hi - 1e-6; t += _step.Value)
        {
            try { var c = Occ.Section(shape, n * t, n); if (c.Count(SubKind.Edge) > 0) curves.Add(c); } catch (KernelException) { }
        }
        if (curves.Count == 0) throw new KernelException("No slices cut the shape.");
        Results.Clear(); Results.Add(new ResultLine("Slices", curves.Count.ToString()));
        return NurbsUtil.Curves(curves, "Contour");
    }
}

/// <summary>Copy edges of a body or surface as curves.</summary>
public sealed class DuplicateEdgeCommand : Command
{
    readonly SelectionSlot _edges;
    public DuplicateEdgeCommand() { _edges = Slot(new SelectionSlot { Label = "Edges", Filter = SelFilter.Edge, Multi = true }); }
    public override string Title => "Duplicate Edge";
    public override string Icon => "curve-through";
    public override string Hint => "Copy the edges you click as curves. Edges that touch end to end become one curve.";
    protected override CommandOutput? Build()
    {
        if (!Complete) return null;
        var joined = Occ.Profile(_edges.Items.Select(i => S.ShapeOf(i)!).ToList(), false);
        return NurbsUtil.Curves([joined], "Edge curve");
    }
}

/// <summary>Show a curvature comb on curves and report how tight they bend.</summary>
public sealed class CurveCurvatureCommand : Command
{
    readonly SelectionSlot _curves;
    readonly IntParam _density;
    readonly NumberParam _scale;
    public CurveCurvatureCommand()
    {
        _curves = Slot(new SelectionSlot { Label = "Curves", Filter = NurbsUtil.CurveIn, Multi = true });
        _density = Add(new IntParam { Label = "Sample density", Value = 120, Min = 20, Max = 400 });
        _scale = Num("Comb length (pixels)", 46, 10, 200, 5, "px");
    }
    public override string Title => "Curvature Graph";
    public override string Icon => "curvature";
    public override string Hint => "Hairs along a curve show how much it bends. Smooth, even hairs mean a fair curve; a jump in their tips means the bend changes suddenly there.";
    List<(Vec3 p, Vec3 k)[]> _edges = [];
    protected override CommandOutput? Build()
    {
        Results.Clear(); _edges = [];
        var o = new CommandOutput { NoOp = true };
        if (!Complete) return o;
        foreach (var item in _curves.Items) _edges.AddRange(Occ.CurveSamples(NurbsUtil.Curve(S, item), _density.Value, 64));
        double maxK = _edges.SelectMany(e => e).Select(s => s.k.Length).DefaultIfEmpty(0).Max();
        double meanK = _edges.SelectMany(e => e).Select(s => s.k.Length).DefaultIfEmpty(0).Average();
        Results.Add(new ResultLine("Tightest bend", maxK > 1e-9 ? $"radius {1 / maxK:0.###} mm" : "none: the curve is straight", 0));
        Results.Add(new ResultLine("Typical bend", meanK > 1e-9 ? $"radius {1 / meanK:0.###} mm" : "straight"));
        Results.Add(new ResultLine("Segments", _edges.Count.ToString()));
        return o;
    }
    public override void BuildOverlay(CadSession s, Overlay o) { if (_edges.Count > 0) CurvatureComb.Draw(o, s, _edges, _scale.Value); }
}

// =================================================================== surfaces
/// <summary>A surface spanning two to four boundary curves.</summary>
public sealed class EdgeSurfaceCommand : Command
{
    readonly SelectionSlot _edges;
    public EdgeSurfaceCommand() { _edges = Slot(new SelectionSlot { Label = "Boundary curves (2 to 4)", Filter = NurbsUtil.CurveIn, Multi = true }); }
    public override string Title => "Edge Surface";
    public override string Icon => "patch";
    public override string Hint => "Pick two, three or four curves that meet end to end (two: a ruled surface). A smooth surface spans the gap between them.";
    protected override CommandOutput? Build()
    {
        if (!Complete) return null;
        if (_edges.Items.Count is < 2 or > 4) throw new KernelException("Pick two, three or four curves.");
        return SurfaceUtil.Single(Occ.EdgeSurface(_edges.Items.Select(i => NurbsUtil.Curve(S, i)).ToList()), "Edge surface");
    }
}

/// <summary>A surface joining an edge of one surface to an edge of another with controlled continuity.</summary>
public sealed class BlendSurfaceCommand : Command
{
    readonly SelectionSlot _a, _b;
    readonly ChoiceParam _cont;
    readonly NumberParam _full;
    public BlendSurfaceCommand()
    {
        _a = Slot(new SelectionSlot { Label = "First edge", Filter = SelFilter.Edge });
        _b = Slot(new SelectionSlot { Label = "Second edge", Filter = SelFilter.Edge });
        _cont = Choice("Continuity", 1, "Position (G0)", "Tangent (G1)", "Curvature (G2)");
        _full = Num("Fullness", 1, 0.1, 4, 0.1, "");
    }
    public override string Title => "Blend Surface";
    public override string Icon => "patch";
    public override string Hint => "Pick an open edge on each of two surfaces. The blend leaves both edges in the direction of their surfaces (tangent), or with their bend too (curvature).";
    protected override CommandOutput? Build()
    {
        if (!Complete) return null;
        var ia = _a.Items[0]; var ib = _b.Items[0];
        var shape = Occ.BlendSurface(S.Find(ia.Obj)!.Shape, ia.Index, S.Find(ib.Obj)!.Shape, ib.Index, _cont.Index, _full.Value);
        return SurfaceUtil.Single(shape, "Blend surface");
    }
}

/// <summary>A round tube (or solid rod) along a curve.</summary>
public sealed class PipeCommand : Command
{
    readonly SelectionSlot _path;
    readonly NumberParam _r0, _r1;
    readonly ChoiceParam _result;
    public PipeCommand()
    {
        _path = Slot(new SelectionSlot { Label = "Path curve", Filter = NurbsUtil.CurveIn });
        _r0 = Num("Radius at the start", 5, 0.001);
        _r1 = Num("Radius at the end", 5, 0.001);
        _result = Choice("Result", 0, "Solid (capped)", "Surface");
    }
    public override string Title => "Pipe";
    public override string Icon => "sweep-surface";
    public override string Hint => "A round pipe along the curve. Give different radii at the two ends for a taper.";
    protected override CommandOutput? Build()
    {
        if (!Complete) return null;
        var path = NurbsUtil.Curve(S, _path.Items[0]);
        var first = Occ.CurveSamples(path, 8, 64)[0];
        var tangent = (first[1].p - first[0].p).Normalized();
        var profile = Occ.Circle(first[0].p, tangent, _r0.Value);
        bool solid = _result.Index == 0;
        var shape = Math.Abs(_r1.Value - _r0.Value) < 1e-9 ? Occ.Sweep(profile, path, solid) : Occ.Sweep2(profile, path, solid, 0, _r1.Value / _r0.Value, Vec3.UnitZ);
        return SurfaceUtil.Single(shape, solid ? "Pipe" : "Pipe surface", solid ? ObjKind.Solid : ObjKind.Surface);
    }
}

/// <summary>Sweep a profile along two rails.</summary>
public sealed class SweepTwoRailsCommand : Command
{
    readonly SelectionSlot _profile, _r1, _r2;
    readonly ChoiceParam _result;
    public SweepTwoRailsCommand()
    {
        _profile = Slot(new SelectionSlot { Label = "Profile", Filter = NurbsUtil.CurveIn });
        _r1 = Slot(new SelectionSlot { Label = "First rail", Filter = NurbsUtil.CurveIn });
        _r2 = Slot(new SelectionSlot { Label = "Second rail", Filter = NurbsUtil.CurveIn });
        _result = Choice("Result", 1, "Solid (capped)", "Surface");
    }
    public override string Title => "Sweep 2 Rails";
    public override string Icon => "sweep-surface";
    public override string Hint => "Carry a profile along two rails: the first rail is the path, the second steers how the profile turns and grows. The profile should touch both rails at its start.";
    protected override CommandOutput? Build()
    {
        if (!Complete) return null;
        bool solid = _result.Index == 0;
        var shape = Occ.SweepRails(NurbsUtil.Curve(S, _profile.Items[0]), NurbsUtil.Curve(S, _r1.Items[0]), NurbsUtil.Curve(S, _r2.Items[0]), solid);
        return SurfaceUtil.Single(shape, solid ? "Sweep" : "Sweep surface", solid ? ObjKind.Solid : ObjKind.Surface);
    }
}

/// <summary>Replace a surface by a simpler one with a chosen grid of points.</summary>
public sealed class RebuildSurfaceCommand : Command
{
    readonly SelectionSlot _src;
    readonly IntParam _u, _v;
    readonly BoolParam _keep;
    public RebuildSurfaceCommand()
    {
        _src = Slot(new SelectionSlot { Label = "Surface or face", Filter = SelFilter.SurfaceObj | SelFilter.Face });
        _u = Add(new IntParam { Label = "Points in U", Value = 8, Min = 4, Max = 60 });
        _v = Add(new IntParam { Label = "Points in V", Value = 8, Min = 4, Max = 60 });
        _keep = Check("Keep the original", true);
    }
    public override string Title => "Rebuild Surface";
    public override string Icon => "raise-degree";
    public override string Hint => "Rebuild a surface as one smooth patch through an even grid of points. Fewer points give a lighter, fairer surface; more points follow the original more closely.";
    protected override CommandOutput? Build()
    {
        if (!Complete) return null;
        var item = _src.Items[0]; var face = NurbsUtil.OfItem(S, item);
        var grid = Occ.SurfaceSamples(face, _u.Value, _v.Value);
        var rebuilt = Occ.FitSurface(grid, _u.Value, _v.Value);
        // how far the result is from the original (sampled on a finer grid)
        var fine = Occ.SurfaceSamples(face, 20, 20); var mesh = rebuilt.Tessellate();
        double worst = 0; var verts = Enumerable.Range(0, mesh.VertexCount).Select(i => new Vec3(mesh.Positions[3 * i], mesh.Positions[3 * i + 1], mesh.Positions[3 * i + 2])).ToList();
        foreach (var p in fine) worst = Math.Max(worst, verts.Min(q => q.DistanceTo(p)));
        double diag = (mesh.Max - mesh.Min).Length;
        Results.Clear(); Results.Add(new ResultLine("Largest deviation (approx.)", $"{worst:0.###} mm", worst < diag * 0.01 ? 1 : 2));
        var o = SurfaceUtil.Single(rebuilt, "Rebuilt surface");
        if (!_keep.Value) NurbsUtil.RemoveSources(S, o, [item]);
        return o;
    }
}

// =================================================================== network surface and deformers
/// <summary>A smooth surface inside a closed outline that is also pinned to curves running across it.</summary>
public sealed class NetworkSurfaceCommand : Command
{
    readonly SelectionSlot _outline, _across;
    readonly IntParam _samples, _degree, _density, _iter;
    public NetworkSurfaceCommand()
    {
        _outline = Slot(new SelectionSlot { Label = "Outline curves (a closed loop)", Filter = NurbsUtil.CurveIn, Multi = true });
        _across = Slot(new SelectionSlot { Label = "Curves across the surface", Filter = NurbsUtil.CurveIn, Multi = true, Optional = true });
        _samples = Add(new IntParam { Label = "Points taken along each curve", Value = 24, Min = 6, Max = 120 });
        _degree = Add(new IntParam { Label = "Surface degree", Value = 3, Min = 3, Max = 8 });
        _density = Add(new IntParam { Label = "Fit density", Value = 15, Min = 8, Max = 60 });
        _iter = Add(new IntParam { Label = "Smoothing passes", Value = 3, Min = 1, Max = 12 });
    }
    public override string Title => "Network Surface";
    public override string Icon => "patch";
    public override string Hint => "Pick the curves that make the outline (they must close into a loop), then the curves that run across the inside. The surface follows the outline exactly and passes close to the inner curves. More points along each curve follow them more tightly.";
    protected override CommandOutput? Build()
    {
        if (!Complete) return null;
        var outline = _outline.Items.Select(i => NurbsUtil.Curve(S, i)).ToList();
        var points = new List<Vec3>();
        foreach (var item in _across.Items)
        {
            var (pts, _) = Occ.CurveDivide(NurbsUtil.Curve(S, item), _samples.Value);
            points.AddRange(pts);
        }
        var shape = Occ.PatchThrough(outline, points, _degree.Value, _density.Value, _iter.Value, 1e-3);
        // a plate through few points can balloon far outside the outline and the points it is meant to follow; refuse that instead of returning a wrong surface
        var box = Occ.Compound(outline).Props(); var res = shape.Props();
        var lo = box.Min; var hi = box.Max;
        foreach (var p in points) { lo = new Vec3(Math.Min(lo.X, p.X), Math.Min(lo.Y, p.Y), Math.Min(lo.Z, p.Z)); hi = new Vec3(Math.Max(hi.X, p.X), Math.Max(hi.Y, p.Y), Math.Max(hi.Z, p.Z)); }
        double diag = (hi - lo).Length;
        double over = new[] { lo.X - res.Min.X, lo.Y - res.Min.Y, lo.Z - res.Min.Z, res.Max.X - hi.X, res.Max.Y - hi.Y, res.Max.Z - hi.Z, 0 }.Max();
        bool bad = over > 0.15 * diag;
        Results.Clear();
        Results.Add(new ResultLine("Inner curves used", _across.Items.Count.ToString()));
        Results.Add(new ResultLine("Overshoot past the curves", $"{over:0.###} mm", bad ? 3 : 1));
        if (bad) throw new KernelException("The surface bulges far outside its curves. Add more curves across it, or raise the points taken along each curve.");        return SurfaceUtil.Single(shape, "Network surface");
    }
}

/// <summary>Bend, twist, taper or stretch curves and surfaces by moving their control points.</summary>
public sealed class DeformCommand : Command
{
    readonly SelectionSlot _src;
    readonly ChoiceParam _mode, _axis, _toward;
    readonly NumberParam _angle, _scale, _from, _to;
    readonly IntParam _detail;
    public DeformCommand()
    {
        _src = Slot(new SelectionSlot { Label = "Curves or surfaces", Filter = SelFilter.SurfaceObj | SelFilter.CurveObj, Multi = true });
        _mode = Choice("Deform", 0, "Bend", "Twist", "Taper", "Stretch");
        _axis = Choice("Along axis", 2, "X", "Y", "Z");
        _toward = Choice("Bend toward", 0, "+X", "-X", "+Y", "-Y", "+Z", "-Z");
        _angle = Num("Angle", 45, -360, 360, 5, "°");
        _scale = Num("End scale", 0.5, 0.01, 20, 0.1, "");
        _from = Num("Starts at (%)", 0, 0, 100, 5, "%");
        _to = Num("Ends at (%)", 100, 0, 100, 5, "%");
        _detail = Add(new IntParam { Label = "Points to add detail up to", Value = 12, Min = 4, Max = 40 });
        UpdateVisibility();
    }
    public override string Title => "Deform";
    public override string Icon => "sculpt";
    public override string Hint => "Bend, twist, taper or stretch curves and surfaces along an axis. The deformation is spread over the control points, so detail is added first when a surface has too few. The region sets where along the object the effect starts and ends.";
    protected override void UpdateVisibility()
    {
        if (_mode == null || _angle == null) return;
        _angle.IsVisible = _mode.Index <= 1; _scale.IsVisible = _mode.Index >= 2; _toward.IsVisible = _mode.Index == 0;
    }

    static Vec3 Unit(int i) => i == 0 ? Vec3.UnitX : i == 1 ? Vec3.UnitY : Vec3.UnitZ;
    static double Get(Vec3 v, int i) => i == 0 ? v.X : i == 1 ? v.Y : v.Z;

    protected override CommandOutput? Build()
    {
        if (!Complete) return null;
        var models = new List<(SceneObject obj, NetModel model)>();
        foreach (var obj in ObjectsOf(_src))
        {
            var model = NetModel.From(obj);
            for (int tries = 0; tries < 3 && model.Points.Length < (model.IsSurface ? _detail.Value * _detail.Value : _detail.Value * 3); tries++)
            {
                var next = model.IsSurface ? Occ.RefineFace(model.Source) : Occ.RefineCurve(model.Source);
                model = NetModel.From(obj.WithShape(next));
            }
            models.Add((obj, model));
        }
        var all = models.SelectMany(m => m.model.Points).ToList();
        var lo = new Vec3(all.Min(p => p.X), all.Min(p => p.Y), all.Min(p => p.Z)); var hi = new Vec3(all.Max(p => p.X), all.Max(p => p.Y), all.Max(p => p.Z));
        int a = _axis.Index; var axis = Unit(a);
        double s0 = Get(lo, a) + (Get(hi, a) - Get(lo, a)) * _from.Value / 100, s1 = Get(lo, a) + (Get(hi, a) - Get(lo, a)) * _to.Value / 100;
        if (s1 - s0 < 1e-9) throw new KernelException("The region is empty: make 'Ends at' larger than 'Starts at'.");
        var centre = (lo + hi) * 0.5; double L = s1 - s0, theta = _angle.Value * Math.PI / 180;
        Vec3 bend = _mode.Index == 0 ? Unit(_toward.Index / 2) * (_toward.Index % 2 == 0 ? 1 : -1) : Vec3.Zero;
        if (_mode.Index == 0 && Math.Abs(Vec3.Dot(bend, axis)) > 0.5) throw new KernelException("Bend toward must be a different axis from the one you bend along.");

        Vec3 Map(Vec3 p)
        {
            double s = Get(p, a); double t = Math.Clamp((s - s0) / L, 0, 1);
            var rel = p - centre; var perp = rel - axis * Vec3.Dot(rel, axis);
            switch (_mode.Index)
            {
                case 1:                      // twist about the axis through the centre
                {
                    double ang = theta * t; double c = Math.Cos(ang), sn = Math.Sin(ang);
                    var k = axis; var rot = perp * c + Vec3.Cross(k, perp) * sn;
                    return centre + axis * Vec3.Dot(rel, axis) + rot;
                }
                case 2:                      // taper: perpendicular size changes along the axis
                    return centre + axis * Vec3.Dot(rel, axis) + perp * (1 + (_scale.Value - 1) * t);
                case 3:                      // stretch: the region is scaled along the axis, the rest slides
                    return p + axis * ((_scale.Value - 1) * (Math.Clamp(s, s0, s1) - s0));
                default:                     // bend: the axis turns into an arc towards the chosen direction
                {
                    if (Math.Abs(theta) < 1e-6) return p;
                    var bd = bend * Math.Sign(theta); double ang = Math.Abs(theta), R = L / ang;
                    double u = s - s0, d = Vec3.Dot(p, bd) - Vec3.Dot(centre, bd);            // d: offset towards the centre of the bend
                    var other = p - axis * Vec3.Dot(p, axis) - bd * Vec3.Dot(p, bd);          // the third direction stays as it is
                    double along, across;
                    if (u <= 0) { along = u; across = d; }
                    else
                    {
                        double phi = Math.Min(u, L) / R;
                        along = (R - d) * Math.Sin(phi); across = R - (R - d) * Math.Cos(phi);
                        if (u > L) { double ex = u - L; along += ex * Math.Cos(ang); across += ex * Math.Sin(ang); }
                    }
                    return other + axis * (s0 + along) + bd * (Vec3.Dot(centre, bd) + across);
                }
            }
        }
        var o = new CommandOutput();
        foreach (var (obj, model) in models) o.Replace.Add((obj, model.With(model.Points.Select(Map).ToArray())));
        Results.Clear(); Results.Add(new ResultLine("Objects deformed", models.Count.ToString()));
        Results.Add(new ResultLine("Control points", models.Sum(m => m.model.Points.Length).ToString()));
        return o;
    }
}