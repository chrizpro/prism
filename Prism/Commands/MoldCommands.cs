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
    readonly NumberParam _at, _gap;

    public MoldCommand()
    {
        _block = Slot(new SelectionSlot { Label = "Mold block", Filter = SelFilter.Solid });
        _part = Slot(new SelectionSlot { Label = "Part to cast (solid, or a closed surface)", Filter = SelFilter.Solid | SelFilter.SurfaceObj });
        _axis = Choice("Parting direction", 0, "Z (top and bottom halves)", "X (left and right halves)", "Y (front and back halves)");
        _at = Num("Split at", 50, 0, 100, 5, "% of the block");
        _plane = Slot(new SelectionSlot { Label = "Parting plane or face (optional, replaces the setting above)", Filter = SelFilter.Plane | SelFilter.Face | SelFilter.SurfaceObj, Optional = true });
        _gap = Num("Explode distance", 40, 0, 1000, 5, "mm");
    }

    public override string Title => "Create Mold";
    public override string Icon => "mold";
    public override string OkText => "Create mold";
    public override string Hint => "Pick the block and the part inside it. Prism cuts the part's shape out of the block, splits the block where you choose (a percentage along the parting direction, or a plane or face you pick) and pulls the two halves apart to show the cavity.";

    protected override CommandOutput? Build()
    {
        Results.Clear();
        if (_block.Items.Count == 0 || _part.Items.Count == 0) return null;
        var block = S.Find(_block.Items[0].Obj) ?? throw new FormatException("That block is gone.");
        var part = S.Find(_part.Items[0].Obj) ?? throw new FormatException("That part is gone.");
        if (block.Kind != ObjKind.Solid || !block.HasBrep) throw new FormatException("The mold block must be a solid body.");
        if (block.Id == part.Id) throw new FormatException("Pick a different body for the part.");
        var bmin = block.Mesh.Min; var bmax = block.Mesh.Max; var pmin = part.Mesh.Min; var pmax = part.Mesh.Max;
        const double tol = 1e-6;
        if (pmin.X < bmin.X - tol || pmin.Y < bmin.Y - tol || pmin.Z < bmin.Z - tol || pmax.X > bmax.X + tol || pmax.Y > bmax.Y + tol || pmax.Z > bmax.Z + tol)
            throw new FormatException("The part must sit inside the mold block. Move or enlarge the block so it surrounds the part.");

        Shape partShape = part.Shape;
        if (partShape.Count(SubKind.Solid) == 0)
        {
            partShape = Occ.Sew([part.Shape], 1e-3, true);
            if (partShape.Count(SubKind.Solid) == 0) throw new FormatException("The part is not closed. Use a solid, or a surface that encloses a volume.");
        }
        double partVolume = Occ.Mass(partShape).Volume;
        var cavity = Occ.Cut(block.Shape, partShape);
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
        o.Replace.Add((block, down.Count(SubKind.Solid) > 0 ? down.Sub(SubKind.Solid, 0) : down));
        o.Add.Add(new NewObject(up.Count(SubKind.Solid) > 0 ? up.Sub(SubKind.Solid, 0) : up, block.Name + " (upper half)", ObjKind.Solid));
        Results.Add(new ResultLine("Parting at", $"{split:0.###} mm along {(a == 0 ? "Z" : a == 1 ? "X" : "Y")}"));
        Results.Add(new ResultLine("Cavity volume", $"{partVolume / 1000:0.###} cm³ (the part)"));
        Results.Add(new ResultLine("Halves apart by", $"{_gap.Value:0.#} mm"));
        return o;
    }
}
