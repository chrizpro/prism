using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core;
using Prism.Core.Assembly;
using Prism.Kernel;

namespace Prism.Commands;

// =================================================================== helpers shared by the direct-modeling tools
static class DirectHelpers
{
    public static SceneObject? BodyOf(CadSession s, SelItem it) { var o = s.Find(it.Obj); return o != null && o.Kind == ObjKind.Solid && o.HasBrep ? o : null; }

    public static IEnumerable<IGrouping<Guid, SelItem>> ByBody(IEnumerable<SelItem> items) => items.Where(i => i.Kind == SelKind.Face).GroupBy(i => i.Obj);

    /// <summary>All cylindrical faces of the body that share this face's axis and radius (the pieces of one hole or boss), including itself.</summary>
    public static List<int> Coaxial(SceneObject body, int faceIndex)
    {
        var face = body.Shape.Sub(SubKind.Face, faceIndex); var info = Occ.FaceInfoOf(face);
        var group = new List<int> { faceIndex };
        if (info.Type != "Cylinder" || Occ.AxisOf(face) is not { } ax) return group;
        var dir = ax.Dir.Normalized(); int n = body.Shape.Count(SubKind.Face);
        for (int f = 0; f < n; f++)
        {
            if (f == faceIndex) continue;
            var g = body.Shape.Sub(SubKind.Face, f); var gi = Occ.FaceInfoOf(g);
            if (gi.Type != "Cylinder" || Math.Abs(gi.R1 - info.R1) > 1e-6 || Occ.AxisOf(g) is not { } ga) continue;
            if (Vec3.Cross(ga.Dir.Normalized(), dir).Length > 1e-6) continue;
            var off = ga.Origin - ax.Origin; if ((off - dir * Vec3.Dot(off, dir)).Length > 1e-5) continue;
            group.Add(f);
        }
        return group;
    }

    /// <summary>Axis-aligned extent of the picked faces measured along an axis.</summary>
    public static (double t0, double t1) ExtentAlong(SceneObject o, IEnumerable<int> faces, Vec3 origin, Vec3 dir)
    {
        var m = o.Mesh; double t0 = double.MaxValue, t1 = double.MinValue;
        foreach (int f in faces)
        {
            if (f + 1 >= m.FaceTriStart.Length) continue;
            for (int t = m.FaceTriStart[f]; t < m.FaceTriStart[f + 1]; t++)
                for (int c = 0; c < 3; c++)
                {
                    int vi = m.Indices[3 * t + c] * 3;
                    double d = Vec3.Dot(new Vec3(m.Positions[vi], m.Positions[vi + 1], m.Positions[vi + 2]) - origin, dir);
                    t0 = Math.Min(t0, d); t1 = Math.Max(t1, d);
                }
        }
        return (t0, t1);
    }

    /// <summary>
    /// Grow or shrink a whole round feature (a hole or a boss) by moving its wall. Positive always means "pull the wall out of the part", like Push / Pull does for a flat face:
    /// a boss gets fatter and a hole gets smaller.
    /// </summary>
    public static Shape OffsetRound(CadSession s, SceneObject body, SelItem it, double dist, Shape current, HashSet<int> claimed)
    {
        var face = body.Shape.Sub(SubKind.Face, it.Index); var info = Occ.FaceInfoOf(face);
        var ax = Occ.AxisOf(face) ?? throw new FormatException("That round face has no axis.");
        var group = Coaxial(body, it.Index);
        if (group.Any(claimed.Contains)) return current;
        foreach (var g in group) claimed.Add(g);
        var dir = ax.Dir.Normalized(); double r = info.R1;
        var (t0, t1) = ExtentAlong(body, group, ax.Origin, dir);
        double full = 2 * Math.PI * r * (t1 - t0), have = group.Sum(f => Occ.FaceInfoOf(body.Shape.Sub(SubKind.Face, f)).Area);
        if (have < full * 0.9) throw new FormatException("That round face is only part of a circle (a fillet or a slot end). Use Fillet to change its radius, or Delete Face to remove it.");
        var fr = s.FaceFrame(it) ?? throw new FormatException("Could not read that face.");
        var rel = fr.point - ax.Origin; var radial = rel - dir * Vec3.Dot(rel, dir);
        bool hole = Vec3.Dot(fr.normal, radial) < 0;
        double newR = hole ? r - dist : r + dist;
        if (newR <= 1e-6) throw new FormatException("That would shrink the feature to nothing.");
        var oldTool = Cylinder(ax.Origin, dir, t0, t1, r); var newTool = Cylinder(ax.Origin, dir, t0, t1, newR);
        if (newR > r) return hole ? Occ.Cut(current, newTool) : Occ.Fuse(current, newTool);
        var ring = Occ.Cut(oldTool, newTool);
        return hole ? Occ.Fuse(current, ring) : Occ.Cut(current, ring);
    }

    /// <summary>Rotate a vector about an axis (Rodrigues).</summary>
    public static Vec3 RotateAbout(Vec3 v, Vec3 axis, double deg)
    {
        double a = deg * Math.PI / 180, c = Math.Cos(a), sn = Math.Sin(a); var k = axis.Normalized();
        return v * c + Vec3.Cross(k, v) * sn + k * (Vec3.Dot(k, v) * (1 - c));
    }

    static Shape HalfSpace(Vec3 p, Vec3 n, double h, bool below)
    {
        var box = Occ.Translate(Occ.Box(2 * h, 2 * h, h), new Vec3(0, 0, below ? -h : 0));
        var f = Xform.FromFrame(p, Xform.AnyPerpendicular(n), n);
        return Occ.Transform(box, f.R, f.T);
    }

    /// <summary>
    /// Re-skin one flat face so it lies on a new plane. Material between the old face and the new plane (inside the face's outline, swept along its normal) is added where the new
    /// plane is outside the part and cut where it is inside, so a tilted target gives a wedge. Neighbour faces extend or trim to meet the new plane.
    /// </summary>
    public static Shape MoveFaceToPlane(Shape body, Shape face, Vec3 outward, Vec3 target, Vec3 targetNormal)
    {
        var pr = body.Props(); var diag = (pr.Max - pr.Min).Length; var centre = (pr.Min + pr.Max) * 0.5;
        double reach = 2 * (diag + (target - centre).Length) + 10;
        var nt = targetNormal.Normalized(); if (Vec3.Dot(nt, outward) < 0) nt = -nt;
        if (Vec3.Dot(nt, outward) < 1e-3) throw new FormatException("The target plane is square-on to that face, so the face can never reach it.");
        Shape? add = null, remove = null;
        try { var a = Occ.Common(Occ.Extrude(face, outward * reach), HalfSpace(target, nt, reach, true)); if (a.Props().Volume > 1e-9) add = a; } catch (KernelException) { }
        try { var r = Occ.Common(Occ.Extrude(face, -outward * reach), HalfSpace(target, nt, reach, false)); if (r.Props().Volume > 1e-9) remove = r; } catch (KernelException) { }
        if (add == null && remove == null) throw new FormatException("That face is already on the target plane.");
        var res = body;
        if (add != null) res = Occ.Fuse(res, add);
        if (remove != null) res = Occ.Cut(res, remove);
        return res;
    }

    /// <summary>A solid cylinder: axis through origin along dir, from t0 to t1.</summary>
    public static Shape Cylinder(Vec3 origin, Vec3 dir, double t0, double t1, double radius)
    {
        var d = dir.Normalized();
        var frame = Xform.FromFrame(origin + d * t0, Xform.AnyPerpendicular(d), d);
        return Occ.Transform(Occ.Cylinder(radius, Math.Max(t1 - t0, 1e-6)), frame.R, frame.T);
    }
}

// =================================================================== Push / Pull
/// <summary>Drag a flat face outward or inward by a distance; the body grows or shrinks with it.</summary>
public sealed class PushPullCommand : Command
{
    readonly SelectionSlot _faces;
    readonly NumberParam _dist;

    public PushPullCommand()
    {
        _faces = Slot(new SelectionSlot { Label = "Faces to push or pull", Filter = SelFilter.Face, Multi = true });
        _dist = Num("Distance (positive pulls out, negative pushes in)", 10);
    }

    public override string Title => "Push / Pull";
    public override string Icon => "pushpull";
    public override string OkText => "Apply";
    public override HandleSpec? Handle => _faces.Items.Count == 0 ? null : SurfaceHandle(_faces.Items[0], _dist);
    public override string Hint => "Pick one or more faces, then pull the orange arrow or type a distance. Positive pulls the face out (the body grows), negative pushes it in (material is removed). Works on flat faces, on whole holes and bosses (the radius changes) and on flat surface faces (they sweep into a solid). No history needed.";

    protected override CommandOutput? Build()
    {
        Results.Clear();
        if (_faces.Items.Count == 0 || Math.Abs(_dist.Value) < 1e-9) return null;
        var o = new CommandOutput { Label = "Push / Pull", Icon = "pushpull" };
        foreach (var grp in DirectHelpers.ByBody(_faces.Items))
        {
            var obj = S.Find(grp.Key) ?? throw new FormatException("That body is gone.");
            if (obj.Kind == ObjKind.Surface && obj.HasBrep)
            {
                // pulling a flat patch of a surface sweeps it into a new solid
                foreach (var it in grp)
                {
                    var face = S.ShapeOf(it) ?? throw new FormatException("That face is gone.");
                    var info = Occ.FaceInfoOf(face);
                    if (info.Type != "Plane") throw new FormatException("Only flat surface faces can be pulled into a solid. For a curved surface use Thicken.");
                    o.Add.Add(new NewObject(Occ.Extrude(face, info.Normal.Normalized() * _dist.Value), "Pulled face", ObjKind.Solid));
                }
                Results.Add(new ResultLine("Surface faces pulled into solids", $"{grp.Count()}  by {_dist.Value:0.##} mm"));
                continue;
            }
            var body = DirectHelpers.BodyOf(S, grp.First()) ?? throw new FormatException(obj.Kind == ObjKind.Form
                ? "Freeform bodies are shaped with Edit Freeform (Extrude Faces). Convert to NURBS first to use Push / Pull."
                : "Push / Pull works on solid bodies and flat surface faces.");
            // build every prism from the original faces first: face numbers change after each boolean
            var prisms = new List<Shape>(); var rounds = new List<SelItem>();
            foreach (var it in grp)
            {
                var face = S.ShapeOf(it) ?? throw new FormatException("That face is gone.");
                var info = Occ.FaceInfoOf(face);
                if (info.Type == "Cylinder") { rounds.Add(it); continue; }
                if (info.Type != "Plane") throw new FormatException("Push / Pull works on flat faces and on holes and bosses (round faces).");
                prisms.Add(Occ.Extrude(face, info.Normal.Normalized() * _dist.Value));
            }
            var result = body.Shape;
            foreach (var p in prisms) result = _dist.Value > 0 ? Occ.Fuse(result, p) : Occ.Cut(result, p);
            var claimed = new HashSet<int>();
            foreach (var it in rounds) result = DirectHelpers.OffsetRound(S, body, it, _dist.Value, result, claimed);
            o.Replace.Add((body, result));
            if (prisms.Count > 0) Results.Add(new ResultLine("Faces moved", $"{prisms.Count}  by {_dist.Value:0.##} mm"));
            if (claimed.Count > 0) Results.Add(new ResultLine("Round features resized", $"wall moved {_dist.Value:0.##} mm"));
        }
        return o;
    }
}

// =================================================================== Move / resize a hole or boss
/// <summary>
/// Pick a hole or a boss (a round feature) and slide it, or change its diameter. The old feature is filled in or cut away and a new one is made
/// at the new place with the same depth.
/// </summary>
public sealed class MoveHoleCommand : Command
{
    readonly SelectionSlot _faces;
    readonly NumberParam _dx, _dy, _dz, _dia;

    public MoveHoleCommand()
    {
        _faces = Slot(new SelectionSlot { Label = "A hole or boss (click its round face)", Filter = SelFilter.Face, Multi = true });
        _dx = Num("Move X", 0); _dy = Num("Move Y", 0); _dz = Num("Move Z", 0);
        _dia = Num("New diameter (0 = keep)", 0, 0, 1e5, 1);
    }

    public override string Title => "Move Hole / Boss";
    public override string Icon => "movehole";
    public override string OkText => "Apply";
    public override string Hint => "Click the round face of a hole or boss, then type how far to move it (for example Move Z = -10 slides it down 10 mm). You can also give a new diameter. Every face of the same hole is picked up automatically, and its depth is kept.";

    sealed record Feature(List<int> Faces, Vec3 Origin, Vec3 Dir, double Radius, bool IsHole, double T0, double T1);

    List<Feature> Features(SceneObject body, IEnumerable<SelItem> picks)
    {
        var list = new List<Feature>(); var claimed = new HashSet<int>();
        int faceCount = body.Shape.Count(SubKind.Face);
        foreach (var it in picks)
        {
            if (claimed.Contains(it.Index)) continue;
            var face = S.ShapeOf(it) ?? throw new FormatException("That face is gone.");
            var info = Occ.FaceInfoOf(face);
            if (info.Type != "Cylinder") throw new FormatException("Pick the round (cylindrical) face of a hole or boss.");
            var ax = Occ.AxisOf(face) ?? throw new FormatException("That face has no axis.");
            var dir = ax.Dir.Normalized(); double r = info.R1;
            // every coaxial cylinder of the same size belongs to the same hole
            var group = new List<int>();
            for (int f = 0; f < faceCount; f++)
            {
                var g = body.Shape.Sub(SubKind.Face, f); var gi = Occ.FaceInfoOf(g);
                if (gi.Type != "Cylinder" || Math.Abs(gi.R1 - r) > 1e-6) continue;
                if (Occ.AxisOf(g) is not { } ga) continue;
                var gd = ga.Dir.Normalized();
                if (Vec3.Cross(gd, dir).Length > 1e-6) continue;
                var off = ga.Origin - ax.Origin; if ((off - dir * Vec3.Dot(off, dir)).Length > 1e-5) continue;
                group.Add(f);
            }
            if (!group.Contains(it.Index)) group.Add(it.Index);
            foreach (var g in group) claimed.Add(g);
            // hole or boss: does the surface normal point toward the axis?
            var fr = S.FaceFrame(it) ?? throw new FormatException("Could not read that face.");
            var rel = fr.point - ax.Origin; var radial = rel - dir * Vec3.Dot(rel, dir);
            bool hole = Vec3.Dot(fr.normal, radial) < 0;
            var (t0, t1) = DirectHelpers.ExtentAlong(body, group, ax.Origin, dir);
            list.Add(new Feature(group, ax.Origin, dir, r, hole, t0, t1));
        }
        return list;
    }

    protected override CommandOutput? Build()
    {
        Results.Clear();
        if (_faces.Items.Count == 0) return null;
        var move = new Vec3(_dx.Value, _dy.Value, _dz.Value);
        if (move.Length < 1e-9 && _dia.Value <= 0) return null;
        var o = new CommandOutput { Label = "Move hole / boss", Icon = "movehole" };
        foreach (var grp in DirectHelpers.ByBody(_faces.Items))
        {
            var body = DirectHelpers.BodyOf(S, grp.First()) ?? throw new FormatException("Pick faces on a solid body.");
            var result = body.Shape;
            var feats = Features(body, grp);
            foreach (var f in feats)
            {
                double newR = _dia.Value > 0 ? _dia.Value / 2 : f.Radius;
                var oldTool = DirectHelpers.Cylinder(f.Origin, f.Dir, f.T0, f.T1, f.Radius);
                var newTool = DirectHelpers.Cylinder(f.Origin + move, f.Dir, f.T0, f.T1, newR);
                if (f.IsHole)
                {
                    Shape filled;
                    try { filled = Occ.Defeature(result, FacesNow(result, f)); }
                    catch (KernelException) { filled = Occ.Fuse(result, oldTool); }          // plain fill if healing is not possible
                    result = Occ.Cut(filled, newTool);
                }
                else result = Occ.Fuse(Occ.Cut(result, oldTool), newTool);
                Results.Add(new ResultLine(f.IsHole ? "Hole" : "Boss", $"Ø{f.Radius * 2:0.##}  →  Ø{newR * 2:0.##},  moved {move.Length:0.##} mm"));
            }
            o.Replace.Add((body, result));
        }
        return o;
    }

    /// <summary>The face numbers of a feature in the (possibly already edited) shape: found again by geometry, since numbers change.</summary>
    int[] FacesNow(Shape shape, Feature f)
    {
        var res = new List<int>(); int n = shape.Count(SubKind.Face);
        for (int i = 0; i < n; i++)
        {
            var g = shape.Sub(SubKind.Face, i); var gi = Occ.FaceInfoOf(g);
            if (gi.Type != "Cylinder" || Math.Abs(gi.R1 - f.Radius) > 1e-6 || Occ.AxisOf(g) is not { } ga) continue;
            if (Vec3.Cross(ga.Dir.Normalized(), f.Dir).Length > 1e-6) continue;
            var off = ga.Origin - f.Origin; if ((off - f.Dir * Vec3.Dot(off, f.Dir)).Length > 1e-5) continue;
            res.Add(i);
        }
        return [.. res];
    }
}

// =================================================================== Remove faces / features
/// <summary>Delete faces and let the neighbours close the gap: removes holes, fillets, rounds, pockets and bosses without redoing the model.</summary>
public sealed class DeleteFaceCommand : Command
{
    readonly SelectionSlot _faces;

    public DeleteFaceCommand() { _faces = Slot(new SelectionSlot { Label = "Faces to remove (a hole, fillet, pocket...)", Filter = SelFilter.Face, Multi = true }); }

    public override string Title => "Delete Face";
    public override string Icon => "deleteface";
    public override string OkText => "Remove";
    public override string Hint => "Pick the faces of a hole, a fillet, a pocket or a boss. They are removed and the surrounding faces extend to close the gap. Handy for simplifying an imported part before editing it.";

    protected override CommandOutput? Build()
    {
        Results.Clear();
        if (_faces.Items.Count == 0) return null;
        var o = new CommandOutput { Label = "Delete face", Icon = "deleteface" };
        foreach (var grp in DirectHelpers.ByBody(_faces.Items))
        {
            var obj = S.Find(grp.Key) ?? throw new FormatException("That body is gone.");
            if (obj.Kind == ObjKind.Surface && obj.HasBrep)
            {
                // on a surface, deleting a face leaves a gap: the remaining faces stay as they are
                var gone = grp.Select(i => i.Index).ToHashSet(); int total = obj.Shape.Count(SubKind.Face);
                var keep = Enumerable.Range(0, total).Where(i => !gone.Contains(i)).Select(i => obj.Shape.Sub(SubKind.Face, i)).ToList();
                if (keep.Count == 0) o.Remove.Add(obj); else o.Replace.Add((obj, keep.Count == 1 ? keep[0] : Occ.Sew(keep, 0.01, false)));
                Results.Add(new ResultLine("Faces removed", gone.Count.ToString()));
                continue;
            }
            var body = DirectHelpers.BodyOf(S, grp.First()) ?? throw new FormatException("Delete Face works on solid bodies and surfaces. For a freeform use Delete Faces in Edit Freeform.");
            // a hole or boss may be made of several round faces end to end: take them all
            var faces = grp.SelectMany(i => DirectHelpers.Coaxial(body, i.Index)).Distinct().ToArray();
            var healed = Occ.Defeature(body.Shape, faces);
            o.Replace.Add((body, healed));
            Results.Add(new ResultLine("Faces removed", faces.Length.ToString()));
        }
        return o;
    }
}

// =================================================================== Draft
/// <summary>Lean the walls of a part away from a pull direction so it releases from a mould, or tilt any wall by an angle about a hinge plane.</summary>
public sealed class DraftFaceCommand : Command
{
    readonly SelectionSlot _faces, _neutral;
    readonly NumberParam _angle;
    readonly BoolParam _flip;

    public DraftFaceCommand()
    {
        _faces = Slot(new SelectionSlot { Label = "Faces to draft", Filter = SelFilter.Face, Multi = true });
        _neutral = Slot(new SelectionSlot { Label = "Neutral plane (a plane or flat face the walls hinge on)", Filter = SelFilter.Plane | SelFilter.Face });
        _angle = Num("Draft angle", 3, -45, 45, 0.5, "°");
        _flip = Check("Reverse pull direction", false);
    }

    public override string Title => "Draft";
    public override string Icon => "draft";
    public override string OkText => "Apply";
    public override HandleSpec? Handle
    {
        get
        {
            if (_faces.Items.Count == 0 || _neutral.Items.Count == 0) return null;
            var pl = S.PlaneOf(_neutral.Items[0]); var face = _faces.Items[0];
            if (pl is not { } plane || S.FaceFrame(face) is not { } f || S.Find(face.Obj) is not { } obj) return null;
            var pull = plane.Normal * (_flip.Value ? -1 : 1);
            var hinge = Vec3.Cross(pull, f.normal);
            if (hinge.Length < 1e-6) return null;
            double radius = Math.Max((obj.Mesh.Max - obj.Mesh.Min).Length * 0.2, 1);
            return new HandleSpec(f.point, hinge.Normalized(), _angle, 1, pull.Normalized(), radius);
        }
    }

    public override string Hint => "Pick the walls to lean, then the neutral plane (usually the parting face): the walls pivot where they meet it, so that edge stays put. The pull direction is the neutral plane's normal (a flat face points out of the part): positive angles open the walls up along it, like a mould that lets the part slide out. Use Reverse if the walls lean the wrong way.";

    protected override CommandOutput? Build()
    {
        Results.Clear();
        if (_faces.Items.Count == 0 || _neutral.Items.Count == 0 || Math.Abs(_angle.Value) < 1e-9) return null;
        var pl = S.PlaneOf(_neutral.Items[0]) ?? throw new FormatException("Pick a plane or a flat face as the neutral plane.");
        var pull = pl.Normal * (_flip.Value ? -1 : 1);
        var o = new CommandOutput { Label = "Draft", Icon = "draft" };
        foreach (var grp in DirectHelpers.ByBody(_faces.Items))
        {
            var body = DirectHelpers.BodyOf(S, grp.First()) ?? throw new FormatException("Draft works on solid bodies.");
            o.Replace.Add((body, Occ.Draft(body.Shape, grp.Select(i => i.Index).Distinct().ToArray(), pull, -_angle.Value, pl.Origin, pl.Normal)));
            Results.Add(new ResultLine("Faces drafted", $"{grp.Count()}  by {_angle.Value:0.##}°"));
        }
        return o;
    }
}

// =================================================================== Replace / Move a flat face
/// <summary>Make flat faces lie on another plane or face: flush them up, or tilt them to match. The neighbours extend or trim to follow.</summary>
public sealed class ReplaceFaceCommand : Command
{
    readonly SelectionSlot _faces, _target;

    public ReplaceFaceCommand()
    {
        _faces = Slot(new SelectionSlot { Label = "Flat faces to replace", Filter = SelFilter.Face, Multi = true });
        _target = Slot(new SelectionSlot { Label = "Target plane or flat face", Filter = SelFilter.Plane | SelFilter.Face });
    }

    public override string Title => "Replace Face";
    public override string Icon => "replaceface";
    public override string OkText => "Apply";
    public override string Hint => "Pick the flat faces that should move, then the plane or face they should end up on. Material is added or removed between the two, and the neighbouring faces stretch to follow. A tilted target gives a wedge.";

    protected override CommandOutput? Build()
    {
        Results.Clear();
        if (_faces.Items.Count == 0 || _target.Items.Count == 0) return null;
        var pl = S.PlaneOf(_target.Items[0]) ?? throw new FormatException("Pick a plane or a flat face as the target.");
        var o = new CommandOutput { Label = "Replace face", Icon = "replaceface" };
        foreach (var grp in DirectHelpers.ByBody(_faces.Items))
        {
            var body = DirectHelpers.BodyOf(S, grp.First()) ?? throw new FormatException("Replace Face works on solid bodies.");
            var faces = new List<(Shape face, Vec3 n)>();
            foreach (var it in grp)
            {
                var face = S.ShapeOf(it) ?? throw new FormatException("That face is gone.");
                var info = Occ.FaceInfoOf(face);
                if (info.Type != "Plane") throw new FormatException("Replace Face works on flat faces.");
                faces.Add((face, info.Normal.Normalized()));
            }
            var result = body.Shape;
            foreach (var (face, n) in faces) result = DirectHelpers.MoveFaceToPlane(result, face, n, pl.Origin, pl.Normal);
            o.Replace.Add((body, result));
            Results.Add(new ResultLine("Faces replaced", faces.Count.ToString()));
        }
        return o;
    }
}

/// <summary>Slide flat faces along any direction and tilt them about an axis; the part stretches with them.</summary>
public sealed class MoveFaceCommand : Command
{
    readonly SelectionSlot _faces;
    readonly NumberParam _dx, _dy, _dz, _tilt;
    readonly ChoiceParam _axis;

    public MoveFaceCommand()
    {
        _faces = Slot(new SelectionSlot { Label = "Flat faces to move", Filter = SelFilter.Face, Multi = true });
        _dx = Num("Move X", 0); _dy = Num("Move Y", 0); _dz = Num("Move Z", 0);
        _tilt = Num("Tilt angle", 0, -89, 89, 1, "°");
        _axis = Choice("Tilt about", 0, "The face's own X direction", "The face's own Y direction", "World X", "World Y", "World Z");
    }

    public override string Title => "Move Face";
    public override string Icon => "moveface";
    public override string OkText => "Apply";
    public override string Hint => "Pick flat faces, then type how far to move them and/or how far to tilt them about the middle of the face. Only the part of the move that is square to a flat face changes the shape (sliding along the face does nothing). The neighbouring faces stretch to follow.";

    protected override CommandOutput? Build()
    {
        Results.Clear();
        var move = new Vec3(_dx.Value, _dy.Value, _dz.Value);
        if (_faces.Items.Count == 0 || (move.Length < 1e-9 && Math.Abs(_tilt.Value) < 1e-9)) return null;
        var o = new CommandOutput { Label = "Move face", Icon = "moveface" };
        foreach (var grp in DirectHelpers.ByBody(_faces.Items))
        {
            var body = DirectHelpers.BodyOf(S, grp.First()) ?? throw new FormatException("Move Face works on solid bodies.");
            var result = body.Shape;
            var plan = new List<(Shape face, Vec3 n, Vec3 pt, Vec3 nt)>();
            foreach (var it in grp)
            {
                var face = S.ShapeOf(it) ?? throw new FormatException("That face is gone.");
                var info = Occ.FaceInfoOf(face);
                if (info.Type != "Plane") throw new FormatException("Move Face works on flat faces. For holes and bosses use Move Hole / Boss.");
                var pl = S.PlaneOf(it) ?? throw new FormatException("Could not read that face.");
                var n = info.Normal.Normalized();
                Vec3 axis = _axis.Index switch { 0 => pl.U, 1 => pl.V, 2 => Vec3.UnitX, 3 => Vec3.UnitY, _ => Vec3.UnitZ };
                var nt = Math.Abs(_tilt.Value) < 1e-9 ? n : DirectHelpers.RotateAbout(n, axis, _tilt.Value);
                plan.Add((face, n, pl.Origin + move, nt));
            }
            foreach (var (face, n, pt, nt) in plan)
            {
                if (Math.Abs(Vec3.Dot(move, n)) < 1e-9 && Math.Abs(_tilt.Value) < 1e-9) continue;
                result = DirectHelpers.MoveFaceToPlane(result, face, n, pt, nt);
            }
            o.Replace.Add((body, result));
            Results.Add(new ResultLine("Faces moved", $"{plan.Count}"));
        }
        return o;
    }
}

// =================================================================== Split body
/// <summary>Cut a solid in two (or more) along a plane, face or surface and keep every piece as its own body.</summary>
public sealed class SplitBodyCommand : Command
{
    readonly SelectionSlot _body, _tool;

    public SplitBodyCommand()
    {
        _body = Slot(new SelectionSlot { Label = "Body to split", Filter = SelFilter.Solid });
        _tool = Slot(new SelectionSlot { Label = "Splitting tool (plane, face, surface or solid)", Filter = SelFilter.Plane | SelFilter.SurfaceObj | SelFilter.Face | SelFilter.Solid });
    }

    public override string Title => "Split Body";
    public override string Icon => "splitbody";
    public override string OkText => "Split";
    public override string Hint => "Pick the body, then what cuts it. Each piece becomes its own body.";

    protected override CommandOutput? Build()
    {
        Results.Clear();
        if (_body.Items.Count == 0 || _tool.Items.Count == 0) return null;
        var body = S.Find(_body.Items[0].Obj) ?? throw new FormatException("That body is gone.");
        if (body.Kind != ObjKind.Solid || !body.HasBrep) throw new FormatException("Split Body works on solid bodies.");
        var tool = ToolShapes.For(S, _tool.Items[0]);
        var pieces = Occ.Split(body.Shape, tool);
        int n = pieces.Count(SubKind.Solid);
        if (n < 2) throw new FormatException("The tool does not cut all the way through the body. Extend it so it crosses the body completely.");
        var o = new CommandOutput { Label = "Split body", Icon = "splitbody" };
        o.Replace.Add((body, pieces.Sub(SubKind.Solid, 0)));
        for (int i = 1; i < n; i++) o.Add.Add(new NewObject(pieces.Sub(SubKind.Solid, i), "Split piece", ObjKind.Solid));
        Results.Add(new ResultLine("Pieces", n.ToString()));
        return o;
    }
}
