using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core;
using Prism.Core.Assembly;
using Prism.Kernel;

namespace Prism.Commands;

static class FaceOpsHelpers
{
    /// <summary>The body with every pair of neighbouring faces on one surface merged (the kernel does this after each boolean; a common with a box that holds the whole body triggers it).</summary>
    public static Shape Unify(Shape body)
    {
        var p = body.Props();
        var big = Occ.Box(p.Max.X - p.Min.X + 20, p.Max.Y - p.Min.Y + 20, p.Max.Z - p.Min.Z + 20);
        big = Occ.Translate(big, p.Min - new Vec3(10, 10, 10) - big.Props().Min);
        return Occ.Common(body, big);
    }

    /// <summary>The plain edges (start, end) of a face.</summary>
    public static List<EdgeInfo> LineEdges(Shape face)
    {
        var l = new List<EdgeInfo>();
        for (int i = 0; i < face.Count(SubKind.Edge); i++)
        {
            var ei = Occ.EdgeInfoOf(face.Sub(SubKind.Edge, i));
            if (ei.Type == "Line" && (ei.End - ei.Start).Length > 1e-9) l.Add(ei);
        }
        return l;
    }

    /// <summary>True when two straight edges lie on one line and overlap.</summary>
    public static bool Overlap(EdgeInfo a, EdgeInfo b)
    {
        var d = a.End - a.Start; double len = d.Length; var u = d / len;
        double Off(Vec3 p) { var v = p - a.Start; return (v - u * Vec3.Dot(v, u)).Length; }
        if (Off(b.Start) > 1e-5 || Off(b.End) > 1e-5) return false;
        double t1 = Vec3.Dot(b.Start - a.Start, u), t2 = Vec3.Dot(b.End - a.Start, u);
        return Math.Min(len, Math.Max(t1, t2)) - Math.Max(0, Math.Min(t1, t2)) > 1e-6;
    }

    /// <summary>A solid frustum along an axis from t0 to t1 with radius ra at t0 and rb at t1.</summary>
    public static Shape Frustum(Vec3 origin, Vec3 dir, double t0, double t1, double ra, double rb)
    {
        var d = dir.Normalized(); var frame = Xform.FromFrame(origin + d * t0, Xform.AnyPerpendicular(d), d);
        return Occ.Transform(Occ.Cone(Math.Max(ra, 1e-6), Math.Max(rb, 1e-6), Math.Max(t1 - t0, 1e-6)), frame.R, frame.T);
    }
}

// =================================================================== Split Face
/// <summary>Divide one face in two along a plane, an edge or a sketch curve. The body keeps its shape; only the face is cut.</summary>
public sealed class SplitFaceCommand : Command
{
    readonly SelectionSlot _face, _tool;

    public SplitFaceCommand()
    {
        _face = Slot(new SelectionSlot { Label = "Face to split", Filter = SelFilter.Face, SmartPick = true });
        _tool = Slot(new SelectionSlot { Label = "Splitting plane, edge or sketch", Filter = SelFilter.Plane | SelFilter.Edge | SelFilter.SketchObj | SelFilter.CurveObj });
    }

    public override string Title => "Split Face";
    public override string Icon => "splitbody";
    public override string OkText => "Split";
    public override string Hint => "Edit existing geometry: pick a face, then a plane, an edge or a sketch that crosses it. The face is divided in two along that line; the shape of the body does not change. Use it to make room for a different edit on one half (for example a Push / Pull of only part of a face). A curve that does not cross the whole face leaves it as it was.";

    protected override CommandOutput? Build()
    {
        Results.Clear();
        if (_face.Items.Count == 0 || _tool.Items.Count == 0) return null;
        var fi = _face.Items[0];
        var body = DirectHelpers.BodyOf(S, fi) ?? throw new FormatException("Split Face works on solid bodies.");
        var face = S.ShapeOf(fi) ?? throw new FormatException("That face is gone.");
        var ti = _tool.Items[0];
        Shape curves;
        if (ti.Kind != SelKind.Edge && S.Find(ti.Obj)?.Kind != ObjKind.Curve && S.Find(ti.Obj)?.Kind != ObjKind.Sketch && S.PlaneOf(ti) is { } pl)
            curves = Occ.Section(face, pl.Origin, pl.Normal);
        else
        {
            curves = S.ShapeOf(ti) ?? throw new FormatException("Pick a plane, an edge or a sketch.");
            if (S.FaceFrame(fi) is { } fr) { try { curves = Occ.ProjectCurve(curves, face, fr.normal, false); } catch (KernelException) { } }
        }
        int before = body.Shape.Count(SubKind.Face);
        Shape res;
        try { res = Occ.Split(body.Shape, curves); }
        catch (KernelException) { throw new FormatException("That does not cross the face. Extend the plane, edge or sketch so it runs from one side of the face to the other."); }
        if (res.Count(SubKind.Solid) != 1 || res.Count(SubKind.Face) <= before)
            throw new FormatException("That does not divide the face. The plane, edge or sketch must run from one side of the face to the other.");
        res = res.Sub(SubKind.Solid, 0);
        Results.Add(new ResultLine("Faces", $"{before}  →  {res.Count(SubKind.Face)}"));
        var o = new CommandOutput { Label = "Split face", Icon = "splitbody" };
        o.Replace.Add((body, res));
        return o;
    }
}

// =================================================================== Merge Faces
/// <summary>Join neighbouring faces that lie on the same surface into one (the slivers left by a split or by an imported model).</summary>
public sealed class MergeFacesCommand : Command
{
    readonly SelectionSlot _faces;

    public MergeFacesCommand()
    {
        _faces = Slot(new SelectionSlot { Label = "Faces to merge (neighbours on one surface)", Filter = SelFilter.Face, Multi = true, SmartPick = true });
    }

    public override string Title => "Merge Faces";
    public override string Icon => "replaceface";
    public override string OkText => "Merge";
    public override string Hint => "Edit existing geometry: pick two or more neighbouring faces that lie on the same surface (a flat face cut in pieces, a cylinder split in halves) and they become one face. The shape does not change. Any other neighbours on the same surface elsewhere on the part are tidied too.";

    protected override CommandOutput? Build()
    {
        Results.Clear();
        if (_faces.Items.Count == 0) return null;
        var o = new CommandOutput { Label = "Merge faces", Icon = "replaceface" };
        foreach (var grp in DirectHelpers.ByBody(_faces.Items))
        {
            var body = DirectHelpers.BodyOf(S, grp.First()) ?? throw new FormatException("Merge Faces works on solid bodies.");
            var infos = grp.Select(i => Occ.FaceInfoOf(S.ShapeOf(i) ?? throw new FormatException("That face is gone."))).ToList();
            if (infos.Count < 2) throw new FormatException("Pick at least two faces to merge.");
            if (infos.Any(i => i.Type != infos[0].Type)) throw new FormatException("Those faces are not on the same kind of surface, so they cannot become one face.");
            int before = body.Shape.Count(SubKind.Face);
            var res = FaceOpsHelpers.Unify(body.Shape);
            int after = res.Count(SubKind.Face);
            if (after >= before) throw new FormatException("Those faces cannot be merged: they must be next to each other and lie on exactly the same surface.");
            o.Replace.Add((body, res));
            Results.Add(new ResultLine("Faces", $"{before}  →  {after}"));
        }
        return o;
    }
}

// =================================================================== Fill (Cap)
/// <summary>Close an open loop of edges with a flat cap, or a smooth patch when the loop is not flat, and sew it to the body.</summary>
public sealed class FillCapCommand : Command
{
    readonly SelectionSlot _edges;
    readonly ChoiceParam _cap;
    readonly NumberParam _tol;

    public FillCapCommand()
    {
        _edges = Slot(new SelectionSlot { Label = "An open edge (the whole open loop is found)", Filter = SelFilter.Edge, Multi = true });
        _cap = Choice("Cap", 0, "Automatic (flat when the loop is flat)", "Flat", "Smooth patch");
        _tol = Num("Joint tolerance", 0.05, 0.001, 5, 0.01);
    }

    public override string Title => "Fill / Cap";
    public override string Icon => "patch";
    public override string OkText => "Fill";
    public override string Hint => "Edit existing geometry: click an edge of an open loop (the rim of a missing face or of a gap in a shell) and the whole loop is capped with a flat face, or a smooth patch if the loop is not flat, then sewn to the body so it closes into a solid when it can. Unlike Delete Face, nothing is extended: the cap is made from the loop alone.";

    protected override CommandOutput? Build()
    {
        Results.Clear();
        if (_edges.Items.Count == 0) return null;
        var o = new CommandOutput { Label = "Fill / cap", Icon = "patch" };
        foreach (var grp in _edges.Items.Where(i => i.Kind == SelKind.Edge).GroupBy(i => i.Obj))
        {
            var obj = S.Find(grp.Key) is { HasBrep: true } b ? b : throw new FormatException("Fill / Cap works on solids and surfaces.");
            var shape = obj.Shape; var seen = new HashSet<int>(); var caps = new List<Shape>();
            foreach (var it in grp)
            {
                if (seen.Contains(it.Index)) continue;
                List<int> loop;
                try { loop = SurfaceUtil.OpeningLoops(shape, it.Index)[0]; }
                catch (FormatException) { throw new FormatException("That edge is not on an open loop. This tool caps a gap in a surface or in a body that has lost a face; for a hole in a solid use Delete Face."); }
                foreach (int e in loop) seen.Add(e);
                caps.Add(Cap(shape, loop));
            }
            var all = new List<Shape> { shape }; all.AddRange(caps);
            var sewn = Occ.Sew(all, _tol.Value, true);
            o.Replace.Add((obj, sewn));
            Results.Add(new ResultLine("Loops capped", caps.Count.ToString()));
            Results.Add(new ResultLine("Result", sewn.HasSolids ? "closed into a solid" : "joined to the surface", 1));
        }
        return o;
    }

    Shape Cap(Shape shape, List<int> loop)
    {
        var edges = loop.Select(i => shape.Sub(SubKind.Edge, i)).ToList();
        Shape? flat = null;
        try
        {
            var f = Occ.Profile(edges, true);
            if (f.HasFaces) { var face = f.Sub(SubKind.Face, 0); if (Occ.FaceInfoOf(face).Type == "Plane") flat = face; }
        }
        catch (KernelException) { }
        if (_cap.Index == 0 && flat != null) return flat;
        if (_cap.Index == 1) return flat ?? throw new FormatException("That loop is not flat. Choose Automatic or Smooth patch.");
        // a smooth patch over a loop that is already flat bulges; the flat face is the right answer there
        try { return Occ.Patch(edges); }
        catch (KernelException) when (flat != null) { return flat; }
    }
}

// =================================================================== Extend / Trim Face
/// <summary>Grow or shrink a flat face, keeping its own plane, until it reaches a target plane or face.</summary>
public sealed class ExtendTrimFaceCommand : Command
{
    readonly SelectionSlot _faces, _target;

    public ExtendTrimFaceCommand()
    {
        _faces = Slot(new SelectionSlot { Label = "Flat faces to extend or trim", Filter = SelFilter.Face, Multi = true, SmartPick = true });
        _target = Slot(new SelectionSlot { Label = "Target plane or flat face", Filter = SelFilter.Plane | SelFilter.Face });
    }

    public override string Title => "Extend / Trim Face";
    public override string Icon => "pushpull";
    public override string OkText => "Apply";
    public override string Hint => "Edit existing flat faces: pick the faces, then the plane or face they should reach. Each face stays in its own plane and its edge nearest the target slides until it meets it: a face that stops short grows, one that runs past shrinks. Replace Face moves the face itself; this changes how far it reaches.";

    protected override CommandOutput? Build()
    {
        Results.Clear();
        if (_faces.Items.Count == 0 || _target.Items.Count == 0) return null;
        var pl = S.PlaneOf(_target.Items[0]) ?? throw new FormatException("Pick a plane or a flat face as the target.");
        var nT = pl.Normal.Normalized();
        var o = new CommandOutput { Label = "Extend / trim face", Icon = "pushpull" };
        foreach (var grp in DirectHelpers.ByBody(_faces.Items))
        {
            var body = DirectHelpers.BodyOf(S, grp.First()) ?? throw new FormatException("Extend / Trim works on solid bodies.");
            int total = body.Shape.Count(SubKind.Face);
            var planar = new List<(int idx, Shape shape, Vec3 n, List<EdgeInfo> edges)>();
            for (int j = 0; j < total; j++)
            {
                var sh = body.Shape.Sub(SubKind.Face, j); var fi = Occ.FaceInfoOf(sh);
                if (fi.Type == "Plane") planar.Add((j, sh, fi.Normal.Normalized(), FaceOpsHelpers.LineEdges(sh)));
            }
            var plan = new List<(Shape end, Vec3 n)>(); double move = 0;
            foreach (var it in grp)
            {
                var f = planar.FirstOrDefault(p => p.idx == it.Index);
                if (f.shape == null) throw new FormatException("Extend / Trim works on flat faces.");
                var c = ToolShapes.Centroid(f.shape);
                (double t, int idx)? best = null;
                foreach (var e in f.edges)
                {
                    var u = (e.End - e.Start).Normalized(); var mid = (e.Start + e.End) * 0.5;
                    var d = Vec3.Cross(u, f.n); if (Vec3.Dot(d, mid - c) < 0) d = -d;
                    double den = Vec3.Dot(d, nT); if (Math.Abs(den) < 1e-9) continue;
                    double t = Vec3.Dot(pl.Origin - mid, nT) / den;
                    if (Math.Abs(t) < 1e-6) continue;
                    foreach (var nb in planar)
                    {
                        if (nb.idx == f.idx || Math.Abs(Vec3.Dot(nb.n, f.n)) > 0.999 || !nb.edges.Any(x => FaceOpsHelpers.Overlap(e, x))) continue;
                        if (Math.Abs(Vec3.Dot(nb.n, nT)) < 1e-3) continue;
                        if (best is not { } bt || Math.Abs(t) < Math.Abs(bt.t)) best = (t, nb.idx);
                    }
                }
                if (best is not { } pick) throw new FormatException("That face has no flat neighbour that can slide to the target, or it already reaches it.");
                var end = planar.First(p => p.idx == pick.idx);
                plan.Add((end.shape, end.n)); move += pick.t;
            }
            var result = body.Shape;
            foreach (var (end, n) in plan) result = DirectHelpers.MoveFaceToPlane(result, end, n, pl.Origin, nT);
            o.Replace.Add((body, result));
            Results.Add(new ResultLine("Faces changed", $"{plan.Count}"));
        }
        return o;
    }
}

// =================================================================== Match Face
/// <summary>Make picked faces take the surface of a source face: a plane, a cylinder radius or a cone angle. Neighbours follow.</summary>
public sealed class MatchFaceCommand : Command
{
    readonly SelectionSlot _faces, _source;

    public MatchFaceCommand()
    {
        _faces = Slot(new SelectionSlot { Label = "Faces to change", Filter = SelFilter.Face, Multi = true, SmartPick = true });
        _source = Slot(new SelectionSlot { Label = "Face to match (flat, round or conical)", Filter = SelFilter.Face });
    }

    public override string Title => "Match Face";
    public override string Icon => "replaceface";
    public override string OkText => "Apply";
    public override string Hint => "Edit existing faces so they take on another face's surface. Flat faces move onto the source plane (like Replace Face). Round holes and bosses take the source cylinder's radius. Conical faces take the source cone's angle and keep their wide end, so the narrow end moves. The neighbouring faces follow.";

    protected override CommandOutput? Build()
    {
        Results.Clear();
        if (_faces.Items.Count == 0 || _source.Items.Count == 0) return null;
        var src = Occ.FaceInfoOf(S.ShapeOf(_source.Items[0]) ?? throw new FormatException("The source face is gone."));
        if (src.Type is not ("Plane" or "Cylinder" or "Cone")) throw new FormatException("The source face must be flat, cylindrical or conical.");
        var o = new CommandOutput { Label = "Match face", Icon = "replaceface" };
        foreach (var grp in DirectHelpers.ByBody(_faces.Items))
        {
            var body = DirectHelpers.BodyOf(S, grp.First()) ?? throw new FormatException("Match Face works on solid bodies.");
            var result = body.Shape; int changed = 0;
            if (src.Type == "Plane")
            {
                var pl = S.PlaneOf(_source.Items[0]) ?? throw new FormatException("Could not read the source face.");
                var faces = new List<(Shape face, Vec3 n)>();
                foreach (var it in grp)
                {
                    var f = S.ShapeOf(it) ?? throw new FormatException("That face is gone."); var fi = Occ.FaceInfoOf(f);
                    if (fi.Type != "Plane") throw new FormatException("The source is flat, so the faces to change must be flat too.");
                    faces.Add((f, fi.Normal.Normalized()));
                }
                foreach (var (f, n) in faces) { result = DirectHelpers.MoveFaceToPlane(result, f, n, pl.Origin, pl.Normal); changed++; }
            }
            else if (src.Type == "Cylinder")
            {
                var fuse = new List<Shape>(); var cut = new List<Shape>(); var claimed = new HashSet<int>();
                foreach (var it in grp)
                {
                    if (Occ.FaceInfoOf(S.ShapeOf(it) ?? throw new FormatException("That face is gone.")).Type != "Cylinder") throw new FormatException("The source is a cylinder, so the faces to change must be round too.");
                    var feat = FeatureFinder.FeatureOf(body, it.Index) ?? throw new FormatException("That face is gone.");
                    if (feat.Round is not { } rd) throw new FormatException(DirectHelpers.NotRound(feat));
                    if (Math.Abs(rd.Radius - src.R1) < 1e-6 || feat.Faces.Any(claimed.Contains)) continue;
                    double dist = rd.Hole ? rd.Radius - src.R1 : src.R1 - rd.Radius;
                    var (fu, ct) = DirectHelpers.RoundTool(S, body, it, dist, claimed);
                    if (fu != null) fuse.Add(fu);
                    if (ct != null) cut.Add(ct);
                    changed++;
                }
                if (changed == 0) throw new FormatException("Those faces already have that radius.");
                result = DirectHelpers.Batch(result, fuse, cut);
            }
            else
            {
                double tanA = Math.Tan(Math.Abs(src.R2));
                foreach (var it in grp)
                {
                    var face = S.ShapeOf(it) ?? throw new FormatException("That face is gone."); var fi = Occ.FaceInfoOf(face);
                    if (fi.Type != "Cone") throw new FormatException("The source is a cone, so the faces to change must be conical too.");
                    result = MatchCone(result, body, it, face, tanA);
                    changed++;
                }
            }
            o.Replace.Add((body, result));
            Results.Add(new ResultLine("Faces matched", changed.ToString()));
        }
        return o;
    }

    Shape MatchCone(Shape start, SceneObject body, SelItem it, Shape face, double tanA)
    {
        var (origin, dir) = Occ.AxisOf(face) ?? throw new FormatException("Could not read that cone.");
        dir = dir.Normalized();
        var m = body.Mesh; int f = it.Index; double t0 = double.MaxValue, t1 = double.MinValue, r0 = 0, r1 = 0;
        for (int t = m.FaceTriStart[f]; t < m.FaceTriStart[f + 1]; t++)
            for (int c = 0; c < 3; c++)
            {
                int vi = m.Indices[3 * t + c] * 3; var p = new Vec3(m.Positions[vi], m.Positions[vi + 1], m.Positions[vi + 2]) - origin;
                double a = Vec3.Dot(p, dir), r = (p - dir * a).Length;
                if (a < t0) { t0 = a; r0 = r; }
                if (a > t1) { t1 = a; r1 = r; }
            }
        if (t1 - t0 < 1e-9 || Math.Abs(r1 - r0) < 1e-9) throw new FormatException("Could not read that cone.");
        // keep the wide end, move the narrow end so the wall has the source's angle
        double rn0, rn1, len = t1 - t0;
        if (r1 >= r0) { rn1 = r1; rn0 = r1 - len * tanA; } else { rn0 = r0; rn1 = r0 - len * tanA; }
        if (Math.Min(rn0, rn1) <= 1e-6) throw new FormatException("That angle would bring the cone to a point inside the feature.");
        if (Math.Abs(rn0 - r0) < 1e-6 && Math.Abs(rn1 - r1) < 1e-6) throw new FormatException("That face already has that angle.");
        var fr = S.FaceFrame(it) ?? throw new FormatException("Could not read that face.");
        var rel = fr.point - origin; var radial = rel - dir * Vec3.Dot(rel, dir);
        bool boss = Vec3.Dot(fr.normal, radial) > 0;
        var oldT = FaceOpsHelpers.Frustum(origin, dir, t0, t1, r0, r1); var newT = FaceOpsHelpers.Frustum(origin, dir, t0, t1, rn0, rn1);
        return boss ? DirectHelpers.Batch(DirectHelpers.Batch(start, [], [oldT]), [newT], []) : DirectHelpers.Batch(start, [oldT], [newT]);
    }
}
