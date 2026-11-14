using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Prism.Core;
using Prism.Kernel;
using Prism.Meshing;

namespace Prism.Commands;

static class ToolShapes
{
    /// <summary>A shape for a picked plane (a big square), face or object.</summary>
    public static Shape For(CadSession s, SelItem it)
    {
        if (it.Kind == SelKind.Origin || (it.Kind == SelKind.Object && s.Find(it.Obj)?.Kind == ObjKind.Plane))
        {
            var pl = s.PlaneOf(it) ?? throw new FormatException("Pick a plane.");
            double h = Math.Max(s.OriginSize() * 6, 1000);
            return Occ.Profile([Occ.Polyline([pl.PointAt(-h, -h), pl.PointAt(h, -h), pl.PointAt(h, h), pl.PointAt(-h, h)], true)], true);
        }
        return s.ShapeOf(it) ?? throw new FormatException("Pick a plane, surface, face or body.");
    }

    public static Vec3 Centroid(Shape piece)
    {
        var m = piece.Tessellate(); var sum = Vec3.Zero; int n = m.Positions.Length / 3;
        for (int i = 0; i < n; i++) sum += new Vec3(m.Positions[3 * i], m.Positions[3 * i + 1], m.Positions[3 * i + 2]);
        return n == 0 ? (m.Min + m.Max) * 0.5 : sum / n;
    }
}

/// <summary>Cut a surface with a plane, another surface, a face or a solid and keep the side you want.</summary>
public sealed class TrimSurfaceCommand : Command
{
    readonly SelectionSlot _target, _tool;
    readonly ChoiceParam _keep;

    public TrimSurfaceCommand()
    {
        _target = Slot(new SelectionSlot { Label = "Surface to trim", Filter = SelFilter.SurfaceObj });
        _tool = Slot(new SelectionSlot { Label = "Cutting tool (plane, surface, face or solid)", Filter = SelFilter.Plane | SelFilter.SurfaceObj | SelFilter.Face | SelFilter.Solid });
        _keep = Choice("Keep", 0, "The front side (where the tool's normal points, or outside a solid)", "The back side (the other side, or inside a solid)");
    }

    public override string Title => "Trim Surface";
    public override string Icon => "trim";
    public override string OkText => "Trim";
    public override string Hint => "Pick the surface, then what cuts it: a plane, another surface or face, or a solid. Choose which side stays. Use Keep to flip if the wrong side goes.";

    protected override CommandOutput? Build()
    {
        Results.Clear();
        if (_target.Items.Count == 0 || _tool.Items.Count == 0) return null;
        var obj = S.Find(_target.Items[0].Obj) ?? throw new FormatException("That surface is gone.");
        var shape = obj.Shape; var toolItem = _tool.Items[0]; var tool = ToolShapes.For(S, toolItem);
        bool front = _keep.Index == 0;
        bool solidTool = toolItem.Kind == SelKind.Object && S.Find(toolItem.Obj)?.Kind == ObjKind.Solid;
        Shape result;
        if (solidTool) result = front ? Occ.Cut(shape, tool) : Occ.Common(shape, tool);
        else
        {
            var pieces = Occ.Split(shape, tool);
            int n = pieces.Count(SubKind.Face);
            // which side of the tool is each piece on?
            Func<Vec3, double> side;
            if (S.PlaneOf(toolItem) is { } pl && toolItem.Kind != SelKind.Face) { var nrm = pl.Normal; side = c => Vec3.Dot(c - pl.Origin, nrm); }
            else
            {
                var tm = tool.Tessellate(); var tri = new TriMesh((float[])tm.Positions.Clone(), (int[])tm.Indices.Clone()); var bvh = tri.Bvh;
                side = c => bvh.ClosestPoint(c) is { } hit ? Vec3.Dot(c - hit.Point, tri.FaceNormal(hit.Tri)) : 0;
            }
            var keep = new List<Shape>();
            for (int i = 0; i < n; i++)
            {
                var piece = pieces.Sub(SubKind.Face, i);
                if ((side(ToolShapes.Centroid(piece)) >= 0) == front) keep.Add(piece);
            }
            if (keep.Count == 0) throw new FormatException("That would remove the whole surface. Flip the side to keep.");
            if (keep.Count == n && n == shape.Count(SubKind.Face)) throw new FormatException("The tool does not cut this surface. Move or extend it so it crosses the surface.");
            result = keep.Count == 1 ? keep[0] : Occ.Sew(keep, 0.01, false);
            Results.Add(new ResultLine("Pieces", $"{n}, keeping {keep.Count}"));
        }
        var o = new CommandOutput { Label = "Trim surface", Icon = "trim" };
        o.Replace.Add((obj, result));
        return o;
    }
}

/// <summary>Cut a surface in two (or more) along a plane, face, surface, solid or curve. The pieces stay faces of the one surface, or each becomes its own surface.</summary>
public sealed class SplitSurfaceCommand : Command
{
    readonly SelectionSlot _target, _tool;
    readonly ChoiceParam _result;

    public SplitSurfaceCommand()
    {
        _target = Slot(new SelectionSlot { Label = "Surface to split", Filter = SelFilter.SurfaceObj | SelFilter.Face });
        _tool = Slot(new SelectionSlot { Label = "Splitting tool (plane, face, surface, sketch, curve or solid)", Filter = SelFilter.Plane | SelFilter.SurfaceObj | SelFilter.Face | SelFilter.Solid | SelFilter.SketchObj | SelFilter.CurveObj | SelFilter.Edge });
        _result = Choice("Result", 0, "One surface, several faces", "Separate surfaces");
        _result.HelpText = "One surface keeps the pieces joined as faces you can select, delete or load on their own. Separate surfaces makes each piece its own object, with its own color.";
    }

    public override string Title => "Split Surface";
    public override string Icon => "splitbody";
    public override string OkText => "Split";
    public override string Hint => "Pick the surface (at the Face level, pick one face to split just that face), then what cuts it: a plane, another surface or face, a solid, or a sketch or curve — curves split along their projection on the surface. The surface is divided into faces; choose Separate surfaces under Result to make each piece its own object. Select a face and press Delete to remove it.";

    protected override CommandOutput? Build()
    {
        Results.Clear();
        if (_target.Items.Count == 0 || _tool.Items.Count == 0) return null;
        var obj = S.Find(_target.Items[0].Obj) ?? throw new FormatException("That surface is gone.");
        if (obj.Kind != ObjKind.Surface || !obj.HasBrep) throw new FormatException("Split Surface works on surfaces.");
        // at the Face pick level a picked face is split on its own and the rest of the surface is kept as it is
        var picked = _target.Items[0];
        int only = picked.Kind == SelKind.Face && S.Mode == SelMode.Face ? picked.Index : -1;
        if (only >= obj.Shape.Count(SubKind.Face)) throw new FormatException("That face is gone.");
        var pieces = only < 0 ? Cut(obj.Shape, _tool.Items[0]) : CutFace(obj.Shape, only, _tool.Items[0]);
        int n = pieces.Count(SubKind.Face);
        if (n <= obj.Shape.Count(SubKind.Face)) throw new FormatException("The tool does not split the surface. A plane, face, surface or solid must cross it completely; a sketch or curve splits along its projection on the surface.");
        var o = new CommandOutput { Label = "Split surface", Icon = "splitbody" };
        if (_result.Index == 0)
        {
            // one surface: the faces stay joined along the cut, so Thicken, Sew and analysis loads see them as one body
            var faces = Enumerable.Range(0, n).Select(i => pieces.Sub(SubKind.Face, i)).ToList();
            o.Replace.Add((obj, Occ.Sew(faces, 0.01, false)));
            Results.Add(new ResultLine("Faces", $"{obj.Shape.Count(SubKind.Face)}  →  {n}"));
            return o;
        }
        o.Replace.Add((obj, pieces.Sub(SubKind.Face, 0)));
        for (int i = 1; i < n; i++) o.Add.Add(new NewObject(pieces.Sub(SubKind.Face, i), "Split piece", ObjKind.Surface));
        Results.Add(new ResultLine("Pieces", n.ToString()));
        return o;
    }

    /// <summary>Split one face by the tool and gather it with every other face of the surface, ready to sew.</summary>
    Shape CutFace(Shape surface, int index, SelItem tool)
    {
        using var face = surface.Sub(SubKind.Face, index);
        var cut = Cut(face, tool);
        var all = new List<Shape>();
        for (int i = 0; i < surface.Count(SubKind.Face); i++) if (i != index) all.Add(surface.Sub(SubKind.Face, i));
        for (int i = 0; i < cut.Count(SubKind.Face); i++) all.Add(cut.Sub(SubKind.Face, i));
        return Occ.Compound(all);
    }

    bool IsCurveTool(SelItem it) => it.Kind is SelKind.Edge or SelKind.Entity
        || it.Kind == SelKind.Object && S.Find(it.Obj)?.Kind is ObjKind.Sketch or ObjKind.Curve;

    Shape Cut(Shape target, SelItem toolItem)
    {
        if (!IsCurveTool(toolItem)) return Occ.Split(target, ToolShapes.For(S, toolItem));
        var wire = S.ShapeOf(toolItem) ?? throw new FormatException("That curve is gone.");
        // A curve splits along its shadow on the surface: project along its own plane's normal when it has one,
        // or pull it straight down the surface's normals. The curtain through the curve is the fallback.
        try
        {
            var projected = S.PlaneOf(toolItem) is { } pl ? Occ.ProjectCurve(wire, target, pl.Normal, false) : Occ.ProjectCurve(wire, target, Vec3.UnitZ, true);
            var pieces = SplitByEach(target, projected, SubKind.Edge);
            if (pieces.Count(SubKind.Face) > target.Count(SubKind.Face)) return pieces;
        }
        catch (Exception e) when (e is not OutOfMemoryException) { }
        return SplitByEach(target, Curtain(target, wire, toolItem), SubKind.Face);
    }

    /// <summary>Split by each part of the tool in turn. The kernel does not intersect the parts of one tool with each other,
    /// so curves that cross (two lines of one sketch) handed over together leave cuts out.</summary>
    internal static Shape SplitByEach(Shape target, Shape tool, SubKind part)
    {
        int n = tool.Count(part);
        if (n <= 1) return Occ.Split(target, tool);
        var pieces = target; int done = 0;
        for (int i = 0; i < n; i++)
        {
            try { pieces = Occ.Split(pieces, tool.Sub(part, i)); done++; }
            catch (KernelException) { }   // a part that misses the surface: the others still cut
        }
        if (done == 0) throw new FormatException("The tool does not split the surface.");
        return pieces;
    }

    Shape Curtain(Shape target, Shape wire, SelItem it)
    {
        var wc = ToolShapes.Centroid(wire); var tc = ToolShapes.Centroid(target);
        var dir = S.PlaneOf(it) is { } pl ? pl.Normal : tc - wc;
        if (dir.Length < 1e-9) dir = tc - wc;
        if (dir.Length > 1e-9 && (tc - wc).Length > 1e-9 && Vec3.Dot(dir, tc - wc) < 0) dir = -dir;
        if (dir.Length < 1e-9) dir = Vec3.UnitZ;
        var span = (tc - wc).Length + target.Props().Size.Length + wire.Props().Size.Length + 1;
        return Occ.Extrude(wire, dir.Normalized() * span);
    }
}

/// <summary>Make solids out of the spaces enclosed by surfaces, planes and bodies.</summary>
public sealed class BoundaryFillCommand : Command
{
    readonly SelectionSlot _src;
    readonly TextParam _cells;

    public BoundaryFillCommand()
    {
        _src = Slot(new SelectionSlot { Label = "Surfaces, planes and bodies that enclose the space", Filter = SelFilter.SurfaceObj | SelFilter.Plane | SelFilter.Solid | SelFilter.Face, Multi = true });
        _cells = Text("Spaces to keep (blank = all)", "", "numbers as listed below, e.g. 1, 3");
    }

    public override string Title => "Enclosed Body";
    public override string Icon => "fill";
    public override string OkText => "Create solids";
    public override string Hint => "Pick surfaces, planes or bodies that together close off a space (for example a surface that cuts a block in two, or planes around a shape). Each enclosed space becomes a solid; list the ones you want in 'Spaces to keep'.";

    protected override CommandOutput? Build()
    {
        Results.Clear();
        if (_src.Items.Count < 1) return null;
        var shapes = _src.Items.Select(i => ToolShapes.For(S, i)).ToList();
        var vol = Occ.MakeVolumes(shapes);
        int n = vol.Count(SubKind.Solid);
        if (n == 0) throw new FormatException("Those shapes do not enclose any space. Add a surface or plane to close the gaps.");
        var want = new HashSet<int>();
        foreach (var tok in _cells.Value.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries)) if (int.TryParse(tok, NumberStyles.Integer, CultureInfo.InvariantCulture, out int k)) want.Add(k);
        var o = new CommandOutput { Label = "Enclosed solid", Icon = "fill" };
        for (int i = 0; i < n; i++)
        {
            var cell = vol.Sub(SubKind.Solid, i);
            Results.Add(new ResultLine($"Space {i + 1}", $"{cell.Props().Volume:#,0.##} mm³"));
            if (want.Count == 0 || want.Contains(i + 1)) o.Add.Add(new(cell, "Enclosed solid", ObjKind.Solid));
        }
        if (o.Add.Count == 0) throw new FormatException("None of the listed spaces exist.");
        return o;
    }
}

/// <summary>Click an open edge of a surface and the opening it belongs to is filled with a smooth patch.</summary>
public sealed class FillOpeningCommand : Command
{
    readonly SelectionSlot _src;
    readonly BoolParam _join;
    readonly NumberParam _tol;
    readonly ChoiceParam _continuity;

    public FillOpeningCommand()
    {
        _src = Slot(new SelectionSlot { Label = "An open edge (or the whole surface to fill every opening)", Filter = SelFilter.Edge | SelFilter.SurfaceObj });
        _continuity = Choice("Continuity", 0, "Position (G0)", "Tangent (G1)");
        _continuity.HelpText = "Match the patch to the adjacent surface along the opening. Tangency may fail for incompatible boundaries.";
        _join = Check("Join the patch to the surface (closes it into a solid when it can)", true);
        _tol = Num("Joint tolerance", 0.05, 0.001, 5, 0.01);
    }

    public override string Title => "Fill Opening";
    public override string Icon => "patch";
    public override string OkText => "Fill";
    public override string Hint => "Click one open (boundary) edge of a surface: the whole opening it belongs to is found and filled with a patch. Or pick the whole surface to fill every opening at once. Works best on the exact NURBS surfaces.";

    protected override CommandOutput? Build()
    {
        Results.Clear();
        if (_src.Items.Count == 0) return null;
        var it = _src.Items[0];
        var obj = S.Find(it.Obj) ?? throw new FormatException("That surface is gone.");
        if (obj.Kind != ObjKind.Surface) throw new FormatException("This works on surfaces. For a solid use Delete Face; for a mesh use Mesh Repair.");
        var shape = obj.Shape;
        var loops = SurfaceUtil.OpeningLoops(shape, it.Kind == SelKind.Edge ? it.Index : -1);
        var patches = new List<Shape>();
        foreach (var loop in loops)
        {
            try { patches.Add(_continuity.Index == 0 ? Occ.Patch(loop.Select(i => shape.Sub(SubKind.Edge, i)).ToList()) : Occ.PatchSupported(shape, loop.ToArray(), _continuity.Index)); }
            catch (KernelException e) when (_continuity.Index == 0)
            {
                // a position-only patch cannot be built across a single closed edge such as a circle ("V1==V2"); a patch supported by the surface can
                try { patches.Add(Occ.PatchSupported(shape, loop.ToArray(), 1)); Results.Add(new ResultLine("Opening", "filled with a tangent patch: a position-only patch cannot span this boundary", 1)); }
                catch (KernelException e2) { Results.Add(new ResultLine("Opening", "could not be filled: " + e.Message + " / " + e2.Message, 2)); }
            }
            catch (KernelException e) { Results.Add(new ResultLine("Opening", "could not be filled: " + e.Message, 2)); }
        }
        if (patches.Count == 0) throw new FormatException("The opening could not be filled with the selected continuity. Try a different continuity or boundary.");
        Results.Add(new ResultLine("Openings filled", $"{patches.Count} of {loops.Count}"));
        var o = new CommandOutput { Label = "Fill opening", Icon = "patch" };
        if (_join.Value)
        {
            var all = new List<Shape> { shape }; all.AddRange(patches);
            var sewn = Occ.Sew(all, _tol.Value, true);
            o.Replace.Add((obj, sewn));
            Results.Add(new ResultLine("Result", sewn.HasSolids ? "closed into a solid" : "joined to the surface", 1));
        }
        else foreach (var p in patches) o.Add.Add(new(p, "Patch", ObjKind.Surface));
        return o;
    }
}
