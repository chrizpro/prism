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

    /// <summary>Every face of the feature this face belongs to (both halves of a hole, the walls of a slot...), or just the face itself.</summary>
    public static List<int> Coaxial(CadSession s, SceneObject body, int faceIndex) =>
        FeatureFinder.FeatureOf(body, faceIndex) is { } f ? [.. f.Faces] : [faceIndex];

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
    public static (Shape? fuse, Shape? cut) RoundTool(CadSession s, SceneObject body, SelItem it, double dist, HashSet<int> claimed)
    {
        var feat = FeatureFinder.FeatureOf(body, it.Index) ?? throw new FormatException("That face is gone.");
        if (feat.Faces.Any(claimed.Contains)) return (null, null);
        if (feat.Round is not { } rd) throw new FormatException(NotRound(feat));
        foreach (var g in feat.Faces) claimed.Add(g);
        double r = rd.Radius, newR = rd.Hole ? r - dist : r + dist;
        if (newR <= 1e-6) throw new FormatException("That would shrink the feature to nothing.");
        var dir = feat.Dir; var oldTool = Cylinder(rd.Origin, dir, rd.T0, rd.T1, r); var newTool = Cylinder(rd.Origin, dir, rd.T0, rd.T1, newR);
        if (newR > r) return rd.Hole ? (null, newTool) : (newTool, null);
        var ring = Occ.Cut(oldTool, newTool);
        return rd.Hole ? (ring, null) : (null, ring);
    }

    /// <summary>Why a feature cannot be treated as a round hole or boss, in words that say what to do instead.</summary>
    public static string NotRound(Feat feat) => feat.Kind switch
    {
        FeatureKind.Slot or FeatureKind.Pocket or FeatureKind.Boss => $"That is a {feat.Name.ToLowerInvariant()}, not a round hole. Use Delete Face to remove it, or move its walls with Move Face.",
        FeatureKind.Fillet => "That is a fillet or round. Use Fillet to change its radius, or Delete Face to remove it.",
        _ => "Pick the round (cylindrical) face of a hole or boss.",
    };

    /// <summary>
    /// Add every <paramref name="fuse"/> tool to the body and cut every <paramref name="cut"/> tool from it, as one boolean each instead of one per feature
    /// (a boolean plus clean-up on a part with dozens of features is what makes pattern edits slow). Tools must not overlap each other; if the kernel
    /// refuses the batch, the tools are applied one at a time.
    /// </summary>
    public static Shape Batch(Shape start, IReadOnlyList<Shape> fuse, IReadOnlyList<Shape> cut)
    {
        var r = start;
        if (fuse.Count + cut.Count > 1)
        {
            try
            {
                if (fuse.Count > 0) r = Occ.Fuse(r, Occ.Compound(fuse));
                if (cut.Count > 0) r = Occ.Cut(r, Occ.Compound(cut));
                return r;
            }
            catch (KernelException) { r = start; }
        }
        foreach (var t in fuse) r = Occ.Fuse(r, t);
        foreach (var t in cut) r = Occ.Cut(r, t);
        return r;
    }

    /// <summary>The solid that fills a complete hole (to fuse) or equals a complete boss (to cut), or null when the round faces are only part of a circle.</summary>
    public static (Shape tool, bool hole)? FullRound(CadSession s, SceneObject body, int faceIndex, HashSet<int> claimed)
    {
        if (FeatureFinder.FeatureOf(body, faceIndex) is not { Round: { } rd } feat || feat.Faces.Any(claimed.Contains)) return null;
        foreach (var g in feat.Faces) claimed.Add(g);
        return (Cylinder(rd.Origin, feat.Dir, rd.T0, rd.T1, rd.Radius), rd.Hole);
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

    // ---------------------------------------------------------------- pattern recognition (the work is in FeatureFinder)
    public static readonly string[] ScopeOptions = ["Only what I picked", "Matching features", "The whole pattern"];

    /// <summary>
    /// For scripts and replayed history, where nothing was clicked: add the faces of the picked features' siblings. Scope 1 is every feature of the same kind and size,
    /// scope 2 only those laid out as one pattern with the pick (a line, grid or circle). <paramref name="patternCount"/> is how many siblings form a pattern with a pick.
    /// </summary>
    public static List<SelItem> Scope(CadSession s, SceneObject body, IEnumerable<SelItem> picks, int scope, out int extra, out int patternCount)
    {
        var result = picks.ToList(); extra = 0; patternCount = 0;
        var taken = result.Where(i => i.Kind == SelKind.Face).Select(i => i.Index).ToHashSet();
        var add = new List<int>();
        try
        {
            foreach (var it in result.Where(i => i.Kind == SelKind.Face).ToList())
            {
                if (FeatureFinder.FeatureOf(body, it.Index) is not { } pick) continue;
                patternCount += FeatureFinder.PatternSize(body, pick);
                if (scope > 0) add.AddRange(FeatureFinder.Siblings(body, pick, scope >= 2, taken).Select(f => f.Face));
            }
        }
        catch (KernelException) { return result; }
        foreach (int f in add) if (taken.Add(f)) { result.Add(new SelItem(body.Id, SelKind.Face, f)); extra++; }
        return result;
    }

    /// <summary>The faces to act on for one body, plus a results line saying what pattern recognition did.</summary>
    public static List<SelItem> Scope(CadSession s, SceneObject body, IEnumerable<SelItem> picks, int scope, ICollection<ResultLine> results)
    {
        var all = Scope(s, body, picks, scope, out int extra, out int pattern);
        if (extra > 0) results.Add(new ResultLine(scope >= 2 ? "Pattern recognised" : "Matching features included", $"{extra} more"));
        else if (scope == 0 && pattern > 0) results.Add(new ResultLine("Pattern found", $"{pattern} more like it. Choose Pattern in “Select by” to include them."));
        return all;
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
    readonly ChoiceParam _scope;

    public PushPullCommand()
    {
        _faces = Slot(new SelectionSlot { Label = "Faces to push or pull", Filter = SelFilter.Face, Multi = true, SmartPick = true });
        _dist = Num("Distance (positive pulls out, negative pushes in)", 10);
        _scope = Choice("Also edit", 0, DirectHelpers.ScopeOptions); _scope.IsVisible = false;   // for scripts: people choose with "Select by"
    }

    public override string Title => "Push / Pull";
    public override string Icon => "pushpull";
    public override string OkText => "Apply";
    public override HandleSpec? Handle => _faces.Items.Count == 0 ? null : SurfaceHandle(_faces.Items[0], _dist);
    public override string Hint => "Pick one or more faces, then pull the orange arrow or type a distance. Positive pulls the face out (the body grows), negative pushes it in (material is removed). Works on flat faces, on whole holes and bosses (the radius changes) and on flat surface faces (they sweep into a solid). No history needed. Clicking any face of a hole, slot, pocket or boss picks the whole feature; “Select by” can pick one face, a tangent chain, every feature of the same size, or the whole pattern.";

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
            foreach (var it in DirectHelpers.Scope(S, body, grp, _scope.Index, Results))
            {
                var face = S.ShapeOf(it) ?? throw new FormatException("That face is gone.");
                var info = Occ.FaceInfoOf(face);
                if (info.Type == "Cylinder") { rounds.Add(it); continue; }
                if (info.Type != "Plane") throw new FormatException("Push / Pull works on flat faces and on holes and bosses (round faces).");
                prisms.Add(Occ.Extrude(face, info.Normal.Normalized() * _dist.Value));
            }
            var fuse = new List<Shape>(); var cut = new List<Shape>();
            foreach (var p in prisms) (_dist.Value > 0 ? fuse : cut).Add(p);
            var claimed = new HashSet<int>();
            foreach (var it in rounds)
            {
                var (f, c) = DirectHelpers.RoundTool(S, body, it, _dist.Value, claimed);
                if (f != null) fuse.Add(f);
                if (c != null) cut.Add(c);
            }
            var result = DirectHelpers.Batch(body.Shape, fuse, cut);
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
    readonly ChoiceParam _scope;

    public MoveHoleCommand()
    {
        _faces = Slot(new SelectionSlot { Label = "A hole or boss (click its round face)", Filter = SelFilter.Face, Multi = true, SmartPick = true });
        _dx = Num("Move X", 0); _dy = Num("Move Y", 0); _dz = Num("Move Z", 0);
        _dia = Num("New diameter (0 = keep)", 0, 0, 1e5, 1);
        _scope = Choice("Also edit", 0, DirectHelpers.ScopeOptions); _scope.IsVisible = false;   // for scripts: people choose with "Select by"
    }

    public override string Title => "Move Hole / Boss";
    public override string Icon => "movehole";
    public override string OkText => "Apply";
    public override string Hint => "Click the round face of a hole or boss, then type how far to move it (for example Move Z = -10 slides it down 10 mm). You can also give a new diameter. Every face of the same hole is picked up automatically, and its depth is kept. Clicking any face of a hole, slot, pocket or boss picks the whole feature; “Select by” can pick one face, a tangent chain, every feature of the same size, or the whole pattern.";

    sealed record Feature(List<int> Faces, Vec3 Origin, Vec3 Dir, double Radius, bool IsHole, double T0, double T1);

    List<Feature> Features(SceneObject body, IEnumerable<SelItem> picks)
    {
        var list = new List<Feature>(); var claimed = new HashSet<int>();
        foreach (var it in picks)
        {
            if (claimed.Contains(it.Index)) continue;
            var feat = FeatureFinder.FeatureOf(body, it.Index) ?? throw new FormatException("That face is gone.");
            if (feat.Round is not { } r) throw new FormatException(DirectHelpers.NotRound(feat));
            foreach (var g in feat.Faces) claimed.Add(g);
            list.Add(new Feature([.. feat.Faces], r.Origin, feat.Dir, r.Radius, r.Hole, r.T0, r.T1));
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
            var feats = Features(body, DirectHelpers.Scope(S, body, grp, _scope.Index, Results));
            List<Shape> fill = [], strip = [], drill = [], grow = [];
            foreach (var f in feats)
            {
                double newR = _dia.Value > 0 ? _dia.Value / 2 : f.Radius;
                var oldTool = DirectHelpers.Cylinder(f.Origin, f.Dir, f.T0, f.T1, f.Radius);
                var newTool = DirectHelpers.Cylinder(f.Origin + move, f.Dir, f.T0, f.T1, newR);
                // first fill every old hole and remove every old boss, then drill the new holes and add the new bosses: one boolean for each group
                (f.IsHole ? fill : strip).Add(oldTool);
                (f.IsHole ? drill : grow).Add(newTool);
                if (feats.Count <= 6) Results.Add(new ResultLine(f.IsHole ? "Hole" : "Boss", $"Ø{f.Radius * 2:0.##}  →  Ø{newR * 2:0.##},  moved {move.Length:0.##} mm"));
            }
            if (feats.Count > 6) Results.Add(new ResultLine("Holes and bosses", $"{feats.Count} moved {move.Length:0.##} mm"));
            result = DirectHelpers.Batch(DirectHelpers.Batch(result, fill, strip), grow, drill);
            o.Replace.Add((body, result));
        }
        return o;
    }
}

// =================================================================== Remove faces / features
/// <summary>Delete faces and let the neighbours close the gap: removes holes, fillets, rounds, pockets and bosses without redoing the model.</summary>
public sealed class DeleteFaceCommand : Command
{
    readonly SelectionSlot _faces;

    readonly ChoiceParam _scope;

    public DeleteFaceCommand()
    {
        _faces = Slot(new SelectionSlot { Label = "Faces to remove (a hole, fillet, pocket...)", Filter = SelFilter.Face, Multi = true, SmartPick = true });
        _scope = Choice("Also edit", 0, DirectHelpers.ScopeOptions); _scope.IsVisible = false;   // for scripts: people choose with "Select by"
    }

    public override string Title => "Delete Face";
    public override string Icon => "deleteface";
    public override string OkText => "Remove";
    public override string Hint => "Pick the faces of a hole, a fillet, a pocket or a boss. They are removed and the surrounding faces extend to close the gap. Handy for simplifying an imported part before editing it. Clicking any face of a hole, slot, pocket or boss picks the whole feature; “Select by” can pick one face, a tangent chain, every feature of the same size, or the whole pattern.";

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
            var picks = DirectHelpers.Scope(S, body, grp, _scope.Index, Results);
            var faces = picks.SelectMany(i => DirectHelpers.Coaxial(S, body, i.Index)).Distinct().ToArray();
            // many complete holes and bosses: fill and cut them in two booleans instead of healing each one (the kernel's face removal is slow on dozens of features)
            var claimed = new HashSet<int>(); List<Shape> plugs = [], bosses = []; List<int> rest = [];
            foreach (var it in picks)
            {
                if (claimed.Contains(it.Index)) continue;
                if (DirectHelpers.FullRound(S, body, it.Index, claimed) is { } r) (r.hole ? plugs : bosses).Add(r.tool);
                else foreach (var f in DirectHelpers.Coaxial(S, body, it.Index)) rest.Add(f);
            }
            Shape healed;
            if (plugs.Count + bosses.Count >= 4)
            {
                var start = rest.Count > 0 ? Occ.Defeature(body.Shape, [.. rest.Distinct()]) : body.Shape;
                healed = DirectHelpers.Batch(start, plugs, bosses);
            }
            else healed = Occ.Defeature(body.Shape, faces);
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
    readonly ChoiceParam _scope;

    public DraftFaceCommand()
    {
        _faces = Slot(new SelectionSlot { Label = "Faces to draft", Filter = SelFilter.Face, Multi = true, SmartPick = true });
        _neutral = Slot(new SelectionSlot { Label = "Neutral plane (a plane or flat face the walls hinge on)", Filter = SelFilter.Plane | SelFilter.Face });
        _angle = Num("Draft angle", 3, -45, 45, 0.5, "°");
        _flip = Check("Reverse pull direction", false);
        _scope = Choice("Also edit", 0, DirectHelpers.ScopeOptions); _scope.IsVisible = false;   // for scripts: people choose with "Select by"
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

    public override string Hint => "Pick the walls to lean, then the neutral plane (usually the parting face): the walls pivot where they meet it, so that edge stays put. The pull direction is the neutral plane's normal (a flat face points out of the part): positive angles open the walls up along it, like a mould that lets the part slide out. Use Reverse if the walls lean the wrong way. Clicking any face of a hole, slot, pocket or boss picks the whole feature; “Select by” can pick one face, a tangent chain, every feature of the same size, or the whole pattern.";

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
            o.Replace.Add((body, Occ.Draft(body.Shape, DirectHelpers.Scope(S, body, grp, _scope.Index, Results).Select(i => i.Index).Distinct().ToArray(), pull, -_angle.Value, pl.Origin, pl.Normal)));
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
    readonly ChoiceParam _scope;

    public ReplaceFaceCommand()
    {
        _faces = Slot(new SelectionSlot { Label = "Flat faces to replace", Filter = SelFilter.Face, Multi = true, SmartPick = true });
        _target = Slot(new SelectionSlot { Label = "Target plane or flat face", Filter = SelFilter.Plane | SelFilter.Face });
        _scope = Choice("Also edit", 0, DirectHelpers.ScopeOptions); _scope.IsVisible = false;   // for scripts: people choose with "Select by"
    }

    public override string Title => "Replace Face";
    public override string Icon => "replaceface";
    public override string OkText => "Apply";
    public override string Hint => "Pick the flat faces that should move, then the plane or face they should end up on. Material is added or removed between the two, and the neighbouring faces stretch to follow. A tilted target gives a wedge. Clicking any face of a hole, slot, pocket or boss picks the whole feature; “Select by” can pick one face, a tangent chain, every feature of the same size, or the whole pattern.";

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
            foreach (var it in DirectHelpers.Scope(S, body, grp, _scope.Index, Results))
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
    readonly ChoiceParam _axis, _scope;

    public MoveFaceCommand()
    {
        _faces = Slot(new SelectionSlot { Label = "Flat faces to move", Filter = SelFilter.Face, Multi = true, SmartPick = true });
        _dx = Num("Move X", 0); _dy = Num("Move Y", 0); _dz = Num("Move Z", 0);
        _tilt = Num("Tilt angle", 0, -89, 89, 1, "°");
        _axis = Choice("Tilt about", 0, "The face's own X direction", "The face's own Y direction", "World X", "World Y", "World Z");
        _scope = Choice("Also edit", 0, DirectHelpers.ScopeOptions); _scope.IsVisible = false;   // for scripts: people choose with "Select by"
    }

    public override string Title => "Move Face";
    public override string Icon => "moveface";
    public override string OkText => "Apply";
    public override string Hint => "Pick flat faces, then type how far to move them and/or how far to tilt them about the middle of the face. Only the part of the move that is square to a flat face changes the shape (sliding along the face does nothing). The neighbouring faces stretch to follow. Clicking any face of a hole, slot, pocket or boss picks the whole feature; “Select by” can pick one face, a tangent chain, every feature of the same size, or the whole pattern.";

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
            foreach (var it in DirectHelpers.Scope(S, body, grp, _scope.Index, Results))
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
