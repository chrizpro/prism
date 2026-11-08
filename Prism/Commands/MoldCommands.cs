using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core;
using Prism.Kernel;

namespace Prism.Commands;

/// <summary>Make a two-part mold from a block and a part: cut the part out of the block, split the block along a user-chosen parting plane and pull the halves apart.</summary>
public sealed class MoldCommand : Command
{
    readonly SelectionSlot _block, _part, _plane;
    readonly ChoiceParam _axis;
    readonly NumberParam _at, _gap, _shrink, _offset, _margin, _opacity;

    public MoldCommand()
    {
        _block = Slot(new SelectionSlot { Label = "Mold block (optional: Prism makes one around the part)", Filter = SelFilter.Solid, Optional = true });
        _part = Slot(new SelectionSlot { Label = "Part to cast (solid, or a closed surface)", Filter = SelFilter.Solid | SelFilter.SurfaceObj });
        _axis = Choice("Parting direction", 0, "Z (top and bottom halves)", "X (left and right halves)", "Y (front and back halves)");
        _at = Num("Split at", 50, 0, 100, 5, "% of the block");
        _plane = Slot(new SelectionSlot { Label = "Parting plane or face (optional, replaces the setting above)", Filter = SelFilter.Plane | SelFilter.Face | SelFilter.SurfaceObj, Optional = true });
        _gap = Num("Explode distance", 40, 0, 1000, 5, "mm");
        _shrink = Num("Shrinkage allowance", 0, 0, 20, 0.1, "%");
        _offset = Num("Cavity offset (clearance)", 0, -50, 50, 0.25, "mm");
        _margin = Num("Block margin (when no block is picked)", 20, 1, 500, 5, "mm");
        _opacity = Num("Part opacity while exploded", 55, 5, 100, 5, "%");
    }

    public override string Title => "Create Mold";
    public override string Icon => "mold";
    public override string OkText => "Create mold";
    public override string Hint => "Pick the part (and a block around it, or let Prism make one). Prism cuts the part's shape out of the block, splits the block where you choose (a percentage along the parting direction, or a plane or face you pick) and pulls the two halves apart to show the cavity.";

    protected override CommandOutput? Build()
    {
        Results.Clear();
        if (_part.Items.Count == 0) return null;
        var part = S.Find(_part.Items[0].Obj) ?? throw new FormatException("That part is gone.");
        var block = _block.Items.Count > 0 ? S.Find(_block.Items[0].Obj) ?? throw new FormatException("That block is gone.") : null;
        if (block != null && (block.Kind != ObjKind.Solid || !block.HasBrep)) throw new FormatException("The mold block must be a solid body.");
        if (block != null && block.Id == part.Id) throw new FormatException("Pick a different body for the part.");
        var pmin = part.Mesh.Min; var pmax = part.Mesh.Max;
        // the cavity is the part grown by the offset and the shrinkage allowance
        double grow = Math.Max(0, _offset.Value) + Math.Max(pmax.X - pmin.X, Math.Max(pmax.Y - pmin.Y, pmax.Z - pmin.Z)) * _shrink.Value / 200;
        pmin = new Vec3(pmin.X - grow, pmin.Y - grow, pmin.Z - grow); pmax = new Vec3(pmax.X + grow, pmax.Y + grow, pmax.Z + grow);
        Shape blockShape;
        Vec3 bmin, bmax;
        if (block != null) { bmin = block.Mesh.Min; bmax = block.Mesh.Max; blockShape = block.Shape; }
        else
        {
            double m = _margin.Value;
            bmin = new Vec3(pmin.X - m, pmin.Y - m, pmin.Z - m); bmax = new Vec3(pmax.X + m, pmax.Y + m, pmax.Z + m);
            blockShape = Occ.Translate(Occ.Box(bmax.X - bmin.X, bmax.Y - bmin.Y, bmax.Z - bmin.Z), new Vec3((bmin.X + bmax.X) / 2, (bmin.Y + bmax.Y) / 2, bmin.Z));
        }
        const double tol = 1e-6;
        if (pmin.X < bmin.X - tol || pmin.Y < bmin.Y - tol || pmin.Z < bmin.Z - tol || pmax.X > bmax.X + tol || pmax.Y > bmax.Y + tol || pmax.Z > bmax.Z + tol)
            throw new FormatException("The part must sit inside the mold block. Move or enlarge the block so it surrounds the part.");

        Shape partShape = part.Shape;
        if (partShape.Count(SubKind.Solid) == 0)
        {
            partShape = Occ.Sew([part.Shape], 1e-3, true);
            if (partShape.Count(SubKind.Solid) == 0) throw new FormatException("The part is not closed. Use a solid, or a surface that encloses a volume.");
        }
        var mass = Occ.Mass(partShape);
        double partVolume = mass.Volume;
        Shape tool = partShape;
        if (_shrink.Value > 0) tool = Occ.Scale(tool, mass.Cog, 1 + _shrink.Value / 100);
        if (Math.Abs(_offset.Value) > 1e-9) tool = Occ.Offset(tool, _offset.Value);
        var cavity = Occ.Cut(blockShape, tool);
        if (cavity.Count(SubKind.Solid) == 0) throw new FormatException("The part does not cut the block.");

        int a = _axis.Index; // 0 = Z, 1 = X, 2 = Y
        Vec3 dir = a == 0 ? Vec3.UnitZ : a == 1 ? Vec3.UnitX : Vec3.UnitY;
        double Along(Vec3 v) => a == 0 ? v.Z : a == 1 ? v.X : v.Y;
        Shape lower, upper;
        double split;
        if (_plane.Items.Count > 0)
        {
            var pieces = Occ.Split(cavity, ToolShapes.For(S, _plane.Items[0]));
            int n = pieces.Count(SubKind.Solid);
            if (n < 2) throw new FormatException("The parting plane does not cut all the way through the block. Extend it so it crosses the block completely.");
            var solids = Enumerable.Range(0, n).Select(i => pieces.Sub(SubKind.Solid, i)).OrderBy(s => Along(Occ.Mass(s).Cog)).ToList();
            lower = solids[0]; upper = solids[^1];
            split = Along(Occ.Mass(lower).Cog);
        }
        else
        {
            double lo = Along(bmin), hi = Along(bmax);
            split = lo + (hi - lo) * _at.Value / 100;
            double m = Math.Max(10, (hi - lo)), ex = bmax.X - bmin.X + 2 * m, ey = bmax.Y - bmin.Y + 2 * m, ez = bmax.Z - bmin.Z + 2 * m;
            Shape Half(bool top)
            {
                double dx = ex, dy = ey, dz = ez;
                var origin = new Vec3(bmin.X - m, bmin.Y - m, bmin.Z - m);
                double lenBelow = split - Along(origin), lenAbove = Along(bmax) + m - split;
                double len = top ? lenAbove : lenBelow;
                if (a == 0) dz = len; else if (a == 1) dx = len; else dy = len;
                if (top) origin = a == 0 ? new Vec3(origin.X, origin.Y, split) : a == 1 ? new Vec3(split, origin.Y, origin.Z) : new Vec3(origin.X, split, origin.Z);
                return Occ.Common(cavity, Occ.Translate(Occ.Box(dx, dy, dz), origin + new Vec3(dx / 2, dy / 2, 0)));   // Occ.Box is centred in X and Y and sits on Z = 0
            }
            lower = Half(false); upper = Half(true);
            if (lower.Count(SubKind.Solid) == 0 || upper.Count(SubKind.Solid) == 0) throw new FormatException("The split is outside the block. Choose a position between 0 and 100 percent.");
        }
        var o = new CommandOutput { Label = "Mold", Icon = "splitbody" };
        var down = Occ.Translate(lower, dir * (-_gap.Value / 2));
        var up = Occ.Translate(upper, dir * (_gap.Value / 2));
        var downSolid = down.Count(SubKind.Solid) > 0 ? down.Sub(SubKind.Solid, 0) : down;
        string baseName = block?.Name ?? "Mold";
        if (block != null) o.Replace.Add((block, downSolid)); else o.Add.Add(new NewObject(downSolid, baseName + " (lower half)", ObjKind.Solid));
        o.Add.Add(new NewObject(up.Count(SubKind.Solid) > 0 ? up.Sub(SubKind.Solid, 0) : up, baseName + " (upper half)", ObjKind.Solid));
        // the part gets its own colour and is shown see-through, so it reads as the thing being cast
        double clear = 1 - _opacity.Value / 100; Guid partId = part.Id;
        o.After = session => session.Update(partId, x => x.WithColor(0xFFE8873A).WithLook(x.Look with { Surface = (x.Look.Surface ?? new SurfaceStyle()) with { Transmission = (float)(clear * .9), Ior = 1.05f, Override = true } }), checkpoint: false);
        Results.Add(new ResultLine("Parting at", $"{split:0.###} mm along {(a == 0 ? "Z" : a == 1 ? "X" : "Y")}"));
        Results.Add(new ResultLine("Cavity volume", $"{Occ.Mass(tool).Volume / 1000:0.###} cm³ (the part {partVolume / 1000:0.###} cm³" + (_shrink.Value > 0 || Math.Abs(_offset.Value) > 0 ? $", grown by {_shrink.Value:0.##}% and {_offset.Value:0.##} mm)" : ")")));
        Results.Add(new ResultLine("Halves apart by", $"{_gap.Value:0.#} mm"));
        return o;
    }
}

/// <summary>Where Create Mold's "part from an open tab" finds the other open documents (set by the main window).</summary>
public static class MoldSources
{
    public static Func<IReadOnlyList<(string Name, CadSession Session)>>? Documents { get; set; }
}

/// <summary>Bring a part from another open document into this one as a reference part: it is shown and used for the mold, but not saved with this file.</summary>
public sealed class MoldPartFromTabCommand : Command
{
    readonly ChoiceParam _tab;
    readonly IReadOnlyList<(string Name, CadSession Session)> _docs;

    public MoldPartFromTabCommand()
    {
        _docs = MoldSources.Documents?.Invoke() ?? [];
        _tab = Choice("Open tab", 0, _docs.Count == 0 ? ["(no other tab is open)"] : _docs.Select(d => d.Name).ToArray());
    }

    public override string Title => "Part from Open Tab";
    public override string Icon => "mold-tab";
    public override string OkText => "Bring in the part";
    public override string Hint => "Copy every visible solid and surface of another open tab into this document as a reference part. It is not saved with this file and is left out of drawings, so the mold file contains only the casing.";

    protected override CommandOutput? Build()
    {
        Results.Clear();
        if (_docs.Count == 0) { Results.Add(new ResultLine("Open tabs", "Open the part in another tab first.")); return null; }
        var source = _docs[Math.Clamp(_tab.Index, 0, _docs.Count - 1)].Session;
        var bodies = source.Objects.Where(o => o.Visible && o.HasBrep && !o.IsAnnotation && o.Kind is ObjKind.Solid or ObjKind.Surface).ToList();
        if (bodies.Count == 0) throw new FormatException("That tab has no solid or surface to bring in.");
        var o = new CommandOutput { Label = "Part from tab", Icon = "mold-tab" };
        foreach (var b in bodies) o.Add.Add(new NewObject(Occ.Copy(b.Shape), b.Name, b.Kind));
        var names = bodies.Select(b => b.Name).ToHashSet();
        var before = S.Objects.Select(x => x.Id).ToHashSet();
        o.After = session => { foreach (var x in session.Objects.Where(x => !before.Contains(x.Id))) session.Update(x.Id, y => y.WithReference(true), checkpoint: false); };
        Results.Add(new ResultLine("Bodies", bodies.Count.ToString()));
        return o;
    }
}
