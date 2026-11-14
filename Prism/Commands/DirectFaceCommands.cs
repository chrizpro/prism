using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core;
using Prism.Kernel;

namespace Prism.Commands;

// =================================================================== Offset Face
/// <summary>Move every picked face along its own normal by a distance. Flat faces slide, whole holes and bosses grow or shrink; neighbours extend or trim.</summary>
public sealed class OffsetFaceCommand : Command
{
    readonly SelectionSlot _faces;
    readonly NumberParam _dist;
    readonly ChoiceParam _scope;

    public OffsetFaceCommand()
    {
        _faces = Slot(new SelectionSlot { Label = "Faces to offset", Filter = SelFilter.Face, Multi = true, SmartPick = true });
        _dist = Num("Offset distance (positive moves faces out)", 1);
        _scope = Choice("Also edit", 0, DirectHelpers.ScopeOptions); _scope.IsVisible = false;   // for scripts: people choose with "Select by"
    }

    public override string Title => "Offset Face";
    public override string Icon => "pushpull";
    public override string OkText => "Apply";
    public override HandleSpec? Handle => _faces.Items.Count == 0 ? null : SurfaceHandle(_faces.Items[0], _dist);
    public override string Hint => "Edit existing geometry: pick faces and a distance, and every face moves along its own normal. Positive moves a face out of the part (the body grows), negative moves it in. On a surface the face is carried along its normal and walls join it back to its neighbours, like a raised or sunk panel. Flat faces slide and their neighbours extend or trim to follow; a whole hole or boss changes radius (a hole gets smaller when offset out). Clicking any face of a hole, slot, pocket or boss picks the whole feature.";

    protected override CommandOutput? Build()
    {
        Results.Clear();
        if (_faces.Items.Count == 0 || Math.Abs(_dist.Value) < 1e-9) return null;
        var o = new CommandOutput { Label = "Offset face", Icon = "pushpull" };
        foreach (var grp in DirectHelpers.ByBody(_faces.Items))
        {
            if (S.Find(grp.Key) is { Kind: ObjKind.Surface, HasBrep: true } sheet)
            {
                // a surface face is carried along its normal and joined back with walls along the step
                var picked = grp.Select(i => i.Index).ToHashSet();
                o.Replace.Add((sheet, SurfaceFaceEdit.Offset(sheet.Shape, picked, _dist.Value)));
                Results.Add(new ResultLine("Surface faces offset", $"{picked.Count}  by {_dist.Value:0.###} mm"));
                continue;
            }
            var body = DirectHelpers.BodyOf(S, grp.First()) ?? throw new FormatException("Offset Face works on solid bodies and on faces of a surface.");
            var flats = new List<(Shape face, Vec3 n, Vec3 origin)>(); var rounds = new List<SelItem>();
            foreach (var it in DirectHelpers.Scope(S, body, grp, _scope.Index, Results))
            {
                var face = S.ShapeOf(it) ?? throw new FormatException("That face is gone.");
                var info = Occ.FaceInfoOf(face);
                if (info.Type == "Cylinder") { rounds.Add(it); continue; }
                if (info.Type != "Plane") throw new FormatException("Offset Face works on flat faces and on holes and bosses (round faces).");
                var pl = S.PlaneOf(it) ?? throw new FormatException("Could not read that face.");
                flats.Add((face, info.Normal.Normalized(), pl.Origin));
            }
            var result = body.Shape;
            // rounds first: they are edited from the original body's tool shapes; the flat moves then act on the result
            var fuse = new List<Shape>(); var cut = new List<Shape>(); var claimed = new HashSet<int>();
            foreach (var it in rounds)
            {
                var (f, c) = DirectHelpers.RoundTool(S, body, it, _dist.Value, claimed);
                if (f != null) fuse.Add(f);
                if (c != null) cut.Add(c);
            }
            if (fuse.Count + cut.Count > 0) result = DirectHelpers.Batch(result, fuse, cut);
            foreach (var (face, n, origin) in flats) result = DirectHelpers.MoveFaceToPlane(result, face, n, origin + n * _dist.Value, n);
            o.Replace.Add((body, result));
            if (flats.Count > 0) Results.Add(new ResultLine("Faces offset", $"{flats.Count}  by {_dist.Value:0.##} mm"));
            if (claimed.Count > 0) Results.Add(new ResultLine("Round features resized", $"wall moved {_dist.Value:0.##} mm"));
        }
        return o;
    }
}

// =================================================================== Rotate Face
/// <summary>Turn flat faces about an axis (a straight edge, the face's own direction or a world axis); the body follows.</summary>
public sealed class RotateFaceCommand : Command
{
    readonly SelectionSlot _faces, _edge;
    readonly NumberParam _angle;
    readonly ChoiceParam _axis, _scope;

    public RotateFaceCommand()
    {
        _faces = Slot(new SelectionSlot { Label = "Flat faces to rotate", Filter = SelFilter.Face, Multi = true, SmartPick = true });
        _edge = Slot(new SelectionSlot { Label = "Axis: a straight edge (optional)", Filter = SelFilter.Edge, Optional = true });
        _angle = Num("Rotation angle", 10, -89, 89, 1, "°");
        _axis = Choice("Rotate about", 0, "The face's own X direction", "The face's own Y direction", "World X", "World Y", "World Z");
        _scope = Choice("Also edit", 0, DirectHelpers.ScopeOptions); _scope.IsVisible = false;   // for scripts: people choose with "Select by"
    }

    public override string Title => "Rotate Face";
    public override string Icon => "moveface";
    public override string OkText => "Apply";
    public override HandleSpec? Handle
    {
        get
        {
            if (_faces.Items.Count == 0 || S.PlaneOf(_faces.Items[0]) is not { } pl) return null;
            Vec3 axis, pt = pl.Origin;
            if (_edge.Items.Count > 0)
            {
                var e = S.ShapeOf(_edge.Items[0]); if (e == null) return null;
                var ei = Occ.EdgeInfoOf(e); if (ei.Type != "Line" || (ei.End - ei.Start).Length < 1e-9) return null;
                axis = (ei.End - ei.Start).Normalized();
                pt = ei.Start + axis * Vec3.Dot(pl.Origin - ei.Start, axis);   // foot of the face centre on the hinge line
            }
            else axis = _axis.Index switch { 0 => pl.U, 1 => pl.V, 2 => Vec3.UnitX, 3 => Vec3.UnitY, _ => Vec3.UnitZ };
            var lever = pl.Origin - pt; lever -= axis * Vec3.Dot(lever, axis);
            Vec3 refDir; double radius;
            if (lever.Length > 1e-6) { refDir = lever.Normalized(); radius = lever.Length; }
            else
            {
                var n = S.FaceFrame(_faces.Items[0]) is { } f ? f.normal : pl.Normal;
                var n0 = n - axis * Vec3.Dot(n, axis); if (n0.Length < 1e-6) return null;
                refDir = n0.Normalized();
                radius = S.Find(_faces.Items[0].Obj) is { } o ? Math.Max((o.Mesh.Max - o.Mesh.Min).Length * 0.15, 10) : 10;
            }
            return new HandleSpec(pt, axis, _angle, 1, refDir, radius);
        }
    }
    public override string Hint => "Edit existing flat faces: pick them, then pick a straight edge to turn about, or leave the edge empty and choose the face's own direction or a world axis (through the middle of the face). The face tilts by the angle and the body stretches to follow; neighbouring faces extend or trim. A picked edge wins over the “Rotate about” choice.";

    protected override CommandOutput? Build()
    {
        Results.Clear();
        if (_faces.Items.Count == 0 || Math.Abs(_angle.Value) < 1e-9) return null;
        (Vec3 p, Vec3 d)? line = null;
        if (_edge.Items.Count > 0)
        {
            var e = S.ShapeOf(_edge.Items[0]) ?? throw new FormatException("That edge is gone.");
            var ei = Occ.EdgeInfoOf(e);
            if (ei.Type != "Line" || (ei.End - ei.Start).Length < 1e-9) throw new FormatException("Pick a straight edge as the axis.");
            line = (ei.Start, (ei.End - ei.Start).Normalized());
        }
        var o = new CommandOutput { Label = "Rotate face", Icon = "moveface" };
        foreach (var grp in DirectHelpers.ByBody(_faces.Items))
        {
            var body = DirectHelpers.BodyOf(S, grp.First()) ?? throw new FormatException("Rotate Face works on solid bodies.");
            var plan = new List<(Shape face, Vec3 n, Vec3 pt, Vec3 nt)>();
            foreach (var it in DirectHelpers.Scope(S, body, grp, _scope.Index, Results))
            {
                var face = S.ShapeOf(it) ?? throw new FormatException("That face is gone.");
                var info = Occ.FaceInfoOf(face);
                if (info.Type != "Plane") throw new FormatException("Rotate Face works on flat faces.");
                var pl = S.PlaneOf(it) ?? throw new FormatException("Could not read that face.");
                var n = info.Normal.Normalized();
                Vec3 pt = pl.Origin, axis;
                if (line is { } l) { axis = l.d; pt = l.p + DirectHelpers.RotateAbout(pl.Origin - l.p, axis, _angle.Value); }
                else axis = _axis.Index switch { 0 => pl.U, 1 => pl.V, 2 => Vec3.UnitX, 3 => Vec3.UnitY, _ => Vec3.UnitZ };
                plan.Add((face, n, pt, DirectHelpers.RotateAbout(n, axis, _angle.Value)));
            }
            var result = body.Shape;
            foreach (var (face, n, pt, nt) in plan) result = DirectHelpers.MoveFaceToPlane(result, face, n, pt, nt);
            o.Replace.Add((body, result));
            Results.Add(new ResultLine("Faces rotated", $"{plan.Count}  by {_angle.Value:0.##}°"));
        }
        return o;
    }
}

// =================================================================== Remove Fillets
/// <summary>Remove existing fillets and rounds (picked chains, or every round up to a radius) and let the neighbouring faces close the gap.</summary>
public sealed class RemoveFilletsCommand : Command
{
    readonly SelectionSlot _faces, _body;
    readonly NumberParam _radius;

    public RemoveFilletsCommand()
    {
        _faces = Slot(new SelectionSlot { Label = "Fillets to remove (click one: the whole chain is picked)", Filter = SelFilter.Face, Multi = true, SmartPick = true, Optional = true });
        _body = Slot(new SelectionSlot { Label = "Or a body to clean up", Filter = SelFilter.Solid, Optional = true });
        _radius = Num("Also remove all rounds up to radius (0 = only picked)", 0, 0, 1e4, 0.5);
    }

    public override string Title => "Remove Fillets";
    public override string Icon => "deleteface";
    public override string OkText => "Remove";
    public override string Hint => "Edit existing geometry: click a fillet or round to remove its whole chain, or pick a body and type a radius to remove every round up to that size. The neighbouring faces extend to close the gap, so the sharp corner comes back. Handy for simplifying an imported part.";

    static double RadiusOf(SceneObject body, int face)
    {
        var info = Occ.FaceInfoOf(body.Shape.Sub(SubKind.Face, face));
        return info.Type switch { "Cylinder" => info.R1, "Torus" => info.R2, "Sphere" => info.R1, "Cone" => double.MaxValue, _ => double.MaxValue };
    }

    protected override CommandOutput? Build()
    {
        Results.Clear();
        if (_faces.Items.Count == 0 && (_body.Items.Count == 0 || _radius.Value <= 0)) return null;
        var o = new CommandOutput { Label = "Remove fillets", Icon = "deleteface" };
        var bodies = _faces.Items.Select(i => i.Obj).Concat(_body.Items.Select(i => i.Obj)).Distinct().ToList();
        foreach (var id in bodies)
        {
            var body = S.Find(id) is { Kind: ObjKind.Solid, HasBrep: true } b ? b : throw new FormatException("Remove Fillets works on solid bodies.");
            var take = new HashSet<int>();
            foreach (var it in _faces.Items.Where(i => i.Obj == id && i.Kind == SelKind.Face))
            {
                var feat = FeatureFinder.FeatureOf(body, it.Index) ?? throw new FormatException("That face is gone.");
                if (feat.Kind != FeatureKind.Fillet) throw new FormatException("That is not a fillet or round. Use Delete Face for holes, pockets and bosses.");
                foreach (var f in feat.Faces) take.Add(f);
            }
            if (_radius.Value > 0 && _body.Items.Any(i => i.Obj == id))
                foreach (var feat in FeatureFinder.Features(body).Where(f => f.Kind == FeatureKind.Fillet))
                    if (feat.Faces.All(f => RadiusOf(body, f) <= _radius.Value + 1e-6)) foreach (var f in feat.Faces) take.Add(f);
            if (take.Count == 0) { Results.Add(new ResultLine("Fillets removed", "none found")); continue; }
            o.Replace.Add((body, Occ.Defeature(body.Shape, [.. take])));
            Results.Add(new ResultLine("Fillet faces removed", take.Count.ToString()));
        }
        return o.Replace.Count > 0 ? o : null;
    }
}
