using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core;
using Prism.Core.Assembly;
using Prism.Kernel;

namespace Prism.Commands;

/// <summary>
/// Edit an existing feature of any exact body, imported parts included: click a hole, slot, pocket, boss or fillet (one click picks the whole feature) and
/// move it, copy it, resize it or delete it. No history is needed. What the panel offers follows what was picked.
/// </summary>
public sealed class FeatureEditCommand : Command
{
    readonly SelectionSlot _faces;
    readonly ChoiceParam _action;
    readonly NumberParam _dx, _dy, _dz, _copies, _dia, _width, _length, _boreDia, _boreDepth, _sinkDia, _sinkAngle, _filletR, _chamferD, _chamferD2, _swapSize, _textDepth, _ribT, _threadDia, _threadPitch, _threadDepth;
    readonly ChoiceParam _type, _drag;
    readonly BoolParam _flip, _leftHand, _carry;

    public FeatureEditCommand()
    {
        _faces = Slot(new SelectionSlot { Label = "Feature to edit (click a hole, slot, pocket, boss, fillet, chamfer or text)", Filter = SelFilter.Face, Multi = true, SmartPick = true });
        _action = Choice("Action", 0, "Move", "Resize", "Copy", "Delete", "Change type");
        _dx = Num("Move X", 0); _dy = Num("Move Y", 0); _dz = Num("Move Z", 0);
        _copies = Num("Number of copies", 1, 1, 200, 1);
        _dia = Num("New diameter (0 = keep)", 0, 0, 1e5, 1);
        _width = Num("New slot width (0 = keep)", 0, 0, 1e5, 1);
        _length = Num("New slot length (0 = keep)", 0, 0, 1e5, 1);
        _filletR = Num("New fillet radius (0 = keep)", 0, 0, 1e5, 0.5);
        _chamferD = Num("New chamfer distance (0 = keep)", 0, 0, 1e5, 0.5);
        _chamferD2 = Num("Second chamfer distance (0 = equal)", 0, 0, 1e5, 0.5);
        _swapSize = Num("Size after the swap (0 = same setback)", 0, 0, 1e5, 0.5);
        _textDepth = Num("New text depth or height (0 = keep)", 0, 0, 1e5, 0.1);
        _ribT = Num("New rib thickness (0 = keep)", 0, 0, 1e5, 0.5);
        _type = Choice("New hole type", 1, "Plain", "Counterbore", "Countersink", "Threaded (modeled)");
        _boreDia = Num("Counterbore diameter", 12, 0.01, 1e5, 1);
        _boreDepth = Num("Counterbore depth", 4, 0.01, 1e5, 0.5);
        _sinkDia = Num("Countersink diameter", 12, 0.01, 1e5, 1);
        _sinkAngle = Num("Countersink angle", 90, 10, 170, 1, "°");
        _flip = Check("Other side", false);
        _threadDia = Num("Thread diameter", 6, 1.2, 1e4, 1);
        _threadPitch = Num("Thread pitch", 1, 0.05, 100, 0.05);
        _threadDepth = Num("Thread depth (0 = full hole)", 0, 0, 1e5, 0.5);
        _leftHand = Check("Left-hand thread", false);
        _drag = Choice("Drag handle along", 0, "X", "Y", "Z");
        _carry = Check("Bolts and nuts in the hole follow it", true);
    }

    /// <summary>Start with these faces picked and this action chosen (the context menu uses it). Action: 0 Move, 1 Resize, 2 Copy, 3 Delete, 4 Change type.</summary>
    public void Preset(IEnumerable<SelItem> faces, int action) => ConfigureInputs(() =>
    {
        _faces.Items.Clear(); foreach (var f in faces) _faces.TryAdd(f);
        _action.Index = action;
    });

    public override IReadOnlyList<HandleSpec> Handles
    {
        get
        {
            if (S == null || _faces.Items.Count == 0) return [];
            try
            {
                if (DirectHelpers.BodyOf(S, _faces.Items[0]) is not { } body || FeatureFinder.FeatureOf(body, _faces.Items[0].Index) is not { } f) return [];
                if (_action.Index is 0 or 2)
                {
                    var (dir, p) = _drag.Index switch { 1 => (Vec3.UnitY, _dy), 2 => (Vec3.UnitZ, _dz), _ => (Vec3.UnitX, _dx) };
                    return [new(f.Centre, dir, p)];
                }
                if (_action.Index != 1) return [];
                var b = Meshing.SurfaceMath.Basis(f.Dir);
                if (f.Round is { } r) return [new(r.Origin, b.u, _dia, 2)];                        // the tip sits on the new wall
                if (f.Kind == FeatureKind.Fillet) return [new(f.Centre, b.u, _filletR)];
                if (f.Kind == FeatureKind.Slot)
                {
                    var ends = f.Faces.Select(i => Occ.AxisOf(body.Shape.Sub(SubKind.Face, i))).Where(a => a != null).Select(a => a!.Value.Origin).Distinct().ToList();
                    if (ends.Count >= 2)
                    {
                        var span = ends[1] - ends[0]; if (span.Length < 1e-9) return [];
                        var along = span.Normalized(); var mid = (ends[0] + ends[1]) * 0.5;
                        var across = Vec3.Cross(f.Dir, along).Normalized();
                        return [new(mid, across, _width, 0.5), new(mid, along, _length, 0.5)];   // tips at the new walls
                    }
                }
                return [];
            }
            catch (Exception e) when (e is not OutOfMemoryException) { return []; }
        }
    }
    public override HandleSpec? Handle => Handles.Count > 0 ? Handles[0] : null;

    public override string Title => "Edit Feature";
    public override string Icon => "movehole";
    public override string OkText => "Apply";
    public override string Hint => "Click any face of a hole, slot, pocket, boss or fillet: the whole feature is picked. Then choose what to do. Move slides it by the X / Y / Z you type. Copy makes copies at that spacing. Resize changes the diameter of a round hole or boss, the width and length of a slot, the radius of a fillet or the distances of a chamfer. Change type turns a hole into a plain hole, a counterbore, a countersink or a threaded hole, a chamfer into a fillet and a fillet into a chamfer. Delete removes it and closes the gap. Drag the arrow in the view, or type the distances. “Select by” can pick every feature of the same size or a whole pattern at once.";

    protected override void UpdateVisibility()
    {
        int a = _action.Index; bool move = a is 0 or 2;
        _dx.IsVisible = _dy.IsVisible = _dz.IsVisible = move;
        _copies.IsVisible = a == 2;
        var kinds = PickedKinds();
        _dia.IsVisible = a == 1 && (kinds.Count == 0 || kinds.Any(k => k is FeatureKind.Hole or FeatureKind.Boss));
        _width.IsVisible = _length.IsVisible = a == 1 && (kinds.Count == 0 || kinds.Contains(FeatureKind.Slot));
        _filletR.IsVisible = a == 1 && kinds.Contains(FeatureKind.Fillet);
        _chamferD.IsVisible = _chamferD2.IsVisible = a == 1 && kinds.Contains(FeatureKind.Chamfer);
        _textDepth.IsVisible = a == 1 && kinds.Contains(FeatureKind.Text);
        _ribT.IsVisible = a == 1 && kinds.Contains(FeatureKind.Rib);
        _drag.IsVisible = move;
        _carry.IsVisible = a is 0 or 1 or 3 && (kinds.Count == 0 || kinds.Any(k => k is FeatureKind.Hole or FeatureKind.Counterbore or FeatureKind.Countersink));
        bool swap = kinds.Count > 0 && kinds.All(k => k is FeatureKind.Fillet or FeatureKind.Chamfer);
        bool flipText = kinds.Count > 0 && kinds.All(k => k == FeatureKind.Text);   // text flips between engraved and embossed, nothing to set
        bool retype = a == 4 && !swap && !flipText; int t = _type.Index;
        _swapSize.IsVisible = a == 4 && swap;
        _type.IsVisible = retype; _flip.IsVisible = retype && t != 0;
        _boreDia.IsVisible = _boreDepth.IsVisible = retype && t == 1;
        _sinkDia.IsVisible = _sinkAngle.IsVisible = retype && t == 2;
        _threadDia.IsVisible = _threadPitch.IsVisible = _threadDepth.IsVisible = _leftHand.IsVisible = retype && t == 3;
    }

    HashSet<FeatureKind> PickedKinds()
    {
        var kinds = new HashSet<FeatureKind>();
        if (S == null) return kinds;
        try
        {
            foreach (var grp in DirectHelpers.ByBody(_faces.Items))
                if (DirectHelpers.BodyOf(S, grp.First()) is { } body)
                    foreach (var it in grp) if (FeatureFinder.FeatureOf(body, it.Index) is { } f) kinds.Add(f.Kind);
        }
        catch (KernelException) { }
        return kinds;
    }

    protected override CommandOutput? Build()
    {
        Results.Clear();
        if (_faces.Items.Count == 0) return null;
        int act = _action.Index; var move = new Vec3(_dx.Value, _dy.Value, _dz.Value);
        if (act is 0 or 2 && move.Length < 1e-9) return null;
        if (act == 1 && _dia.Value <= 0 && _width.Value <= 0 && _length.Value <= 0 && _filletR.Value <= 0 && _chamferD.Value <= 0 && _textDepth.Value <= 0 && _ribT.Value <= 0) return null;
        var o = new CommandOutput { Label = $"{_action.Selected} feature", Icon = "movehole" };
        foreach (var grp in DirectHelpers.ByBody(_faces.Items))
        {
            var body = DirectHelpers.BodyOf(S, grp.First()) ?? throw new FormatException("Pick faces on a solid body.");
            var feats = grp.Select(i => FeatureFinder.FeatureOf(body, i.Index) ?? throw new FormatException("That face is gone.")).Distinct().ToList();
            // fill: material added where a hole was; strip: a boss cut away; drill: new holes; grow: new bosses
            List<Shape> fill = [], strip = [], drill = [], grow = []; List<int> fillets = []; List<Feat> resizeFillets = []; List<Shape> grooves = [];
            List<Feat> resizeChamfers = [], toChamfer = [], toFillet = [], ribs = [];
            foreach (var f in feats)
            {
                bool boss = Raised(body, f);
                switch (f.Kind)
                {
                    case FeatureKind.Face: throw new FormatException("That is a plain face, not a feature. Use Move Face or Push / Pull to change it.");
                    case FeatureKind.Fillet:
                        if (act == 1) { if (_filletR.Value > 0) { fillets.AddRange(f.Faces); resizeFillets.Add(f); } break; }
                        if (act == 4) { fillets.AddRange(f.Faces); toChamfer.Add(f); break; }
                        if (act != 3) throw new FormatException("A fillet can be resized, deleted or swapped for a chamfer here. Use Fillet to add a new one.");
                        fillets.AddRange(f.Faces); break;
                    case FeatureKind.Rib when act == 1:
                        if (_ribT.Value > 0) ribs.Add(f);
                        break;
                    case FeatureKind.Chamfer:
                        if (act == 1) { if (_chamferD.Value > 0) { fillets.AddRange(f.Faces); resizeChamfers.Add(f); } break; }
                        if (act == 4) { fillets.AddRange(f.Faces); toFillet.Add(f); break; }
                        if (act != 3) throw new FormatException("A chamfer can be resized, deleted or swapped for a fillet here. Use Chamfer to add a new one.");
                        fillets.AddRange(f.Faces); break;
                    default:
                        var tool = ToolOf(body, f);
                        switch (act)
                        {
                            case 0: (boss ? strip : fill).Add(tool); (boss ? grow : drill).Add(Occ.Translate(tool, move)); break;
                            case 2: for (int k = 1; k <= (int)_copies.Value; k++) (boss ? grow : drill).Add(Occ.Translate(tool, move * k)); break;
                            case 3: (boss ? strip : fill).Add(tool); break;
                            case 4 when f.Kind == FeatureKind.Thread && f.Round is { } tr:
                                // Change type on a threaded hole removes the thread and leaves the plain bore (its minor diameter, the tap drill)
                                fill.Add(tool); drill.Add(DirectHelpers.Cylinder(tr.Origin, f.Dir, tr.T0, tr.T1, tr.Radius)); break;
                            case 4 when f.Kind == FeatureKind.Text:
                                // engraved becomes embossed (or back): the letters are reflected through the face they sit on, which keeps them reading the same way
                                var ti = FeatureFinder.TextOf(body, f) ?? throw new FormatException("Could not read that text.");
                                (boss ? strip : fill).Add(tool); (boss ? drill : grow).Add(AlongNormal(tool, ti, -1)); break;
                            case 4: fillets.AddRange(f.Faces); drill.Add(RetypedHole(body, f, out var groove)); if (groove != null) grooves.Add(groove); break;   // the old hole is healed away, then the new one is cut
                            default:
                                var resized = Resized(body, f);
                                if (resized == null) continue;
                                (boss ? strip : fill).Add(tool); (boss ? grow : drill).Add(resized); break;
                        }
                        break;
                }
            }
            var start = fillets.Count > 0 ? Occ.Defeature(body.Shape, [.. fillets.Distinct()]) : body.Shape;
            if (resizeFillets.Count > 0) start = Resharpened(body, start, resizeFillets, (s, e) => Occ.Fillet(s, e, _filletR.Value));
            if (resizeChamfers.Count > 0)
                start = Resharpened(body, start, resizeChamfers, (s, e) => _chamferD2.Value > 0 ? Occ.Chamfer2(s, e, _chamferD.Value, _chamferD2.Value, 0) : Occ.Chamfer(s, e, _chamferD.Value));
            if (toFillet.Count > 0)
            {
                double r = _swapSize.Value > 0 ? _swapSize.Value : toFillet.Min(f => FeatureFinder.ChamferOf(body, f) is { } c ? Math.Min(c.SetbackA, c.SetbackB) : double.MaxValue);
                if (r is <= 0 or double.MaxValue) throw new FormatException("Could not read that chamfer's size. Type the fillet radius in Size after the swap.");
                start = Resharpened(body, start, toFillet, (s, e) => Occ.Fillet(s, e, r));
            }
            if (toChamfer.Count > 0)
            {
                double d = _swapSize.Value > 0 ? _swapSize.Value : RadiusOf(body, toChamfer);
                start = Resharpened(body, start, toChamfer, (s, e) => Occ.Chamfer(s, e, d));
            }
            // a rib keeps its centre plane: each side moves out (or in) by half the change
            foreach (var rf in ribs)
            {
                var rib = FeatureFinder.RibOf(body, rf) ?? throw new FormatException("Could not read that rib.");
                double half = (_ribT.Value - rib.Thickness) / 2;
                foreach (int side in new[] { rib.SideA, rib.SideB })
                {
                    using var face = body.Shape.Sub(SubKind.Face, side); var info = Occ.FaceInfoOf(face);
                    var nrm = Vec3.Dot(info.Normal, side == rib.SideA ? rib.Normal : -rib.Normal) < 0 ? -info.Normal.Normalized() : info.Normal.Normalized();
                    using var again = FindFaceLike(start, face.Props().Center, nrm) ?? throw new FormatException("Could not find the rib's side again.");
                    start = DirectHelpers.MoveFaceToPlane(start, again, nrm, again.Props().Center + nrm * half, nrm);
                }
            }
            var result = DirectHelpers.Batch(DirectHelpers.Batch(start, fill, strip), grow, drill);
            foreach (var g in grooves) result = Occ.Cut(result, g);
            o.Replace.Add((body, result));
            if (_carry.Value && act is 0 or 1 or 3) CarryFasteners(o, body, feats, act, move);
            foreach (var g in feats.GroupBy(f => f.Name))
                Results.Add(new ResultLine(g.Key + (g.Count() > 1 ? "s" : ""), $"{_action.Selected.ToLowerInvariant()}: {g.Count()}"));
        }
        return o;
    }

    /// <summary>
    /// Bolts, screws and nuts found in the edited holes go with them: moved by the same distance, removed with the hole, or scaled across their axis
    /// to the new size (from one metric size to the next when both diameters are standard, otherwise by the ratio of the diameters).
    /// </summary>
    void CarryFasteners(CommandOutput o, SceneObject body, List<Feat> feats, int act, Vec3 move)
    {
        var done = new HashSet<Guid>(o.Replace.Select(r => r.Old.Id));
        int moved = 0;
        foreach (var f in feats)
        {
            if (FastenerFinder.Bore(body, f) is not { } bore) continue;
            foreach (var fast in FastenerFinder.In(S.Objects, body, f))
            {
                var parts = fast.Nuts.Prepend(fast.Body).Where(p => done.Add(p.Id)).ToList();
                if (parts.Count == 0) continue;
                moved += parts.Count;
                if (act == 3) { o.Remove.AddRange(parts); continue; }
                if (act == 0) { foreach (var p in parts) o.Replace.Add((p, Occ.Translate(p.Shape, move))); continue; }
                if (_dia.Value <= 0 || f.Round == null) continue;
                double oldD = 2 * bore.Radius, newD = _dia.Value;
                string? Size(double d) => StandardHoles.Match(d)?.Split(' ')[0];
                double k = Size(oldD) is { } s0 && Size(newD) is { } s1 && StandardHoles.Of(s0) is { } a0 && StandardHoles.Of(s1) is { } a1 ? a1.Nominal / a0.Nominal : newD / oldD;
                // each part grows evenly about where it bears on the plate (under a bolt's head, a nut's face), so it stays seated and its round
                // faces stay exact cylinders; a bolt one size up is also longer, as the next size usually is
                var ax = fast.Axis.Normalized();
                double hole0 = Vec3.Dot(bore.Origin + f.Dir.Normalized() * bore.T0 - fast.Origin, ax), hole1 = Vec3.Dot(bore.Origin + f.Dir.Normalized() * bore.T1 - fast.Origin, ax);
                foreach (var p in parts)
                {
                    var pm = p.Mesh; double sum = 0; int cnt = 0;
                    for (int v = 0; v < pm.Positions.Length; v += 3)
                    {
                        var q = new Vec3(pm.Positions[v], pm.Positions[v + 1], pm.Positions[v + 2]) - fast.Origin; double t = Vec3.Dot(q, ax);
                        if ((q - ax * t).Length > bore.Radius * 1.05) { sum += t; cnt++; }
                    }
                    double headT = cnt > 0 ? sum / cnt : hole1;
                    double bearT = Math.Abs(headT - hole0) < Math.Abs(headT - hole1) ? hole0 : hole1;
                    o.Replace.Add((p, Occ.Scale(p.Shape, fast.Origin + ax * bearT, k)));
                }
            }
        }
        if (moved > 0) Results.Add(new ResultLine("Fasteners", act == 3 ? $"removed: {moved}" : act == 0 ? $"moved: {moved}" : $"resized: {moved}"));
    }

    /// <summary>The solid a feature occupies: the void of a hole, slot or pocket (to fill or re-cut) or the material of a boss (to remove or re-add).</summary>
    internal static Shape ToolOf(SceneObject body, Feat f)
    {
        // a threaded hole's void is everything inside its major diameter that is not material: the bore and the helical groove together
        if (f.Kind == FeatureKind.Thread && FeatureFinder.ThreadOf(body, f) is { } th && f.Round is { } tr)
        {
            using var full = DirectHelpers.Cylinder(tr.Origin, f.Dir, tr.T0, tr.T1, th.Major / 2);
            return Occ.Cut(full, body.Shape);
        }
        if (f.Round is { } r) return DirectHelpers.Cylinder(r.Origin, f.Dir, r.T0, r.T1, r.Radius);
        var filled = Occ.Defeature(body.Shape, [.. f.Faces]);
        return Raised(body, f) ? Occ.Cut(body.Shape, filled) : Occ.Cut(filled, body.Shape);
    }

    /// <summary>The flat face of <paramref name="body"/> through <paramref name="point"/> with this normal (after an earlier step of the same edit changed the body).</summary>
    static Shape? FindFaceLike(Shape body, Vec3 point, Vec3 normal)
    {
        for (int i = 0; i < body.Count(SubKind.Face); i++)
        {
            var face = body.Sub(SubKind.Face, i); var info = Occ.FaceInfoOf(face);
            if (info.Type == "Plane" && Vec3.Cross(info.Normal.Normalized(), normal).Length < 1e-6 && Math.Abs(Vec3.Dot(face.Props().Center - point, normal)) < 1e-6) return face;
            face.Dispose();
        }
        return null;
    }

    /// <summary>Material standing up from the body (a boss, embossed text) rather than a void cut into it.</summary>
    internal static bool Raised(SceneObject body, Feat f) => f.Kind is FeatureKind.Boss or FeatureKind.Rib || f.Kind == FeatureKind.Text && FeatureFinder.TextOf(body, f) is { Engraved: false };

    /// <summary>Stretches a text's solid along the normal of the face it sits on, keeping that face fixed: factor 2 doubles the depth, -1 reflects it to the other side.</summary>
    static Shape AlongNormal(Shape tool, TextInfo t, double factor)
    {
        // a reflection through a linear map would turn the solid inside out; the kernel's mirror keeps its faces facing outwards
        if (Math.Abs(factor + 1) < 1e-12) return Occ.Mirror(tool, t.Normal.Normalized() * t.PlaneHeight, t.Normal);
        var n = t.Normal.Normalized(); double k = factor - 1;
        double[] m = [1 + k * n.X * n.X, k * n.X * n.Y, k * n.X * n.Z, k * n.Y * n.X, 1 + k * n.Y * n.Y, k * n.Y * n.Z, k * n.Z * n.X, k * n.Z * n.Y, 1 + k * n.Z * n.Z];
        var p0 = n * t.PlaneHeight;
        var mp = new Vec3(m[0] * p0.X + m[1] * p0.Y + m[2] * p0.Z, m[3] * p0.X + m[4] * p0.Y + m[5] * p0.Z, m[6] * p0.X + m[7] * p0.Y + m[8] * p0.Z);
        return Occ.Affine(tool, m, p0 - mp);
    }

    /// <summary>The feature after Resize, or null when the typed sizes do not apply to it.</summary>
    Shape? Resized(SceneObject body, Feat f)
    {
        if (f.Round is { } r)
        {
            if (_dia.Value <= 0) return null;
            return DirectHelpers.Cylinder(r.Origin, f.Dir, r.T0, r.T1, _dia.Value / 2);
        }
        if (f.Kind == FeatureKind.Slot)
        {
            if (_width.Value <= 0 && _length.Value <= 0) return null;
            return SlotTool(body, f, _width.Value, _length.Value);
        }
        if (f.Kind == FeatureKind.Text && FeatureFinder.TextOf(body, f) is { } text)
        {
            if (_textDepth.Value <= 0) return null;
            return AlongNormal(ToolOf(body, f), text, _textDepth.Value / text.Depth);
        }
        throw new FormatException($"Resize works on round holes and bosses, slots and text. For a {f.Name.ToLowerInvariant()}, move its walls with Move Face.");
    }

    /// <summary>A slot (two round ends joined by two flat walls) with a new width and length, same centre, direction and depth.</summary>
    static Shape SlotTool(SceneObject body, Feat f, double width, double length)
    {
        var ends = f.Faces.Select(i => (info: Occ.FaceInfoOf(body.Shape.Sub(SubKind.Face, i)), ax: Occ.AxisOf(body.Shape.Sub(SubKind.Face, i)), i))
            .Where(x => x.info.Type == "Cylinder" && x.ax != null).ToList();
        if (ends.Count != 2) throw new FormatException("That slot has an unusual shape; Resize needs two round ends.");
        var d = f.Dir; var o1 = ends[0].ax!.Value.Origin; var o2 = ends[1].ax!.Value.Origin; double r = ends[0].info.R1;
        var (t0, t1) = DirectHelpers.ExtentAlong(body, ends.Select(e => e.i), o1, d);
        var span = (o2 - o1) - d * Vec3.Dot(o2 - o1, d); double half = span.Length / 2;
        if (half < 1e-6) throw new FormatException("That slot has an unusual shape; Resize needs two round ends.");
        double w = width > 0 ? width : 2 * r, len = length > 0 ? length : 2 * half + 2 * r;
        if (len < w) throw new FormatException("A slot cannot be shorter than it is wide.");
        double nh = (len - w) / 2, nr = w / 2, height = t1 - t0;
        Shape Place(Shape s, double x, double y) { var m = s.Props().Min; return Occ.Translate(s, new Vec3(x - m.X, y - m.Y, -m.Z)); }
        Shape local = Place(Occ.Cylinder(nr, height), -nh - nr, -nr);
        if (nh > 1e-9)
        {
            local = Occ.Fuse(local, Place(Occ.Cylinder(nr, height), nh - nr, -nr));
            local = Occ.Fuse(local, Place(Occ.Box(2 * nh, 2 * nr, height), -nh, -nr));
        }
        var fr = Xform.FromFrame(o1 + d * t0 + span * 0.5, span.Normalized(), d);
        return Occ.Transform(local, fr.R, fr.T);
    }

    // ---------------------------------------------------------------- hole type
    static Shape PlaceAlong(Shape local, Vec3 origin, Vec3 d, double z0)
    {
        var fr = Xform.FromFrame(origin + d * z0, Xform.AnyPerpendicular(d), d);
        return Occ.Transform(local, fr.R, fr.T);
    }

    /// <summary>A hole of the chosen type through the same place as the feature: same axis, same smallest diameter and same depth; the bore or countersink opens on the entry side.</summary>
    Shape RetypedHole(SceneObject body, Feat f, out Shape? groove)
    {
        groove = null;
        if (f.Kind is not (FeatureKind.Hole or FeatureKind.Counterbore or FeatureKind.Countersink))
            throw new FormatException($"Change type works on holes, counterbores and countersinks. A {f.Name.ToLowerInvariant()} has no hole type.");
        var cyls = f.Faces.Select(i => (i, info: Occ.FaceInfoOf(body.Shape.Sub(SubKind.Face, i)), ax: Occ.AxisOf(body.Shape.Sub(SubKind.Face, i)))).Where(x => x.info.Type == "Cylinder" && x.ax != null).ToList();
        if (cyls.Count == 0) throw new FormatException("That hole has no round wall to work from.");
        var d = f.Dir; var small = cyls.OrderBy(c => c.info.R1).First(); double r = small.info.R1; var o = small.ax!.Value.Origin;
        var (t0, t1) = DirectHelpers.ExtentAlong(body, f.Faces, o, d);
        // which end the existing bore or countersink opens on; a plain hole opens on whichever end faces the positive axis
        bool entryTop;
        var steps = f.Faces.Where(i => !cyls.Any(c => c.i == i && c.info.R1 <= r + 1e-9)).ToList();
        if (steps.Count > 0) { var (s0, s1) = DirectHelpers.ExtentAlong(body, steps, o, d); entryTop = (s0 + s1) / 2 > (t0 + t1) / 2; }
        else { double m = Math.Max(Math.Abs(d.X), Math.Max(Math.Abs(d.Y), Math.Abs(d.Z))); double dom = Math.Abs(d.X) == m ? d.X : Math.Abs(d.Y) == m ? d.Y : d.Z; entryTop = dom > 0; }
        if (_flip.Value) entryTop = !entryTop;
        var hole = DirectHelpers.Cylinder(o, d, t0, t1, r);
        switch (_type.Index)
        {
            case 1:
                double R = _boreDia.Value / 2, depth = _boreDepth.Value;
                if (R <= r) throw new FormatException("The counterbore must be wider than the hole.");
                if (depth >= t1 - t0) throw new FormatException("The counterbore must be shallower than the hole.");
                var bore = entryTop ? DirectHelpers.Cylinder(o, d, t1 - depth, t1 + 0.5, R) : DirectHelpers.Cylinder(o, d, t0 - 0.5, t0 + depth, R);
                return Occ.Fuse(hole, bore);
            case 2:
                double Rc = _sinkDia.Value / 2, h = (Rc - r) / Math.Tan(_sinkAngle.Value * Math.PI / 360);
                if (Rc <= r) throw new FormatException("The countersink must be wider than the hole.");
                if (h >= t1 - t0) throw new FormatException("That countersink is deeper than the hole. Use a smaller diameter or a wider angle.");
                var cone = entryTop ? PlaceAlong(Occ.Cone(r, Rc, h), o, d, t1 - h) : PlaceAlong(Occ.Cone(Rc, r, h), o, d, t0);
                return Occ.Fuse(hole, cone);
            case 3:
                // the same construction as the Hole tool: a bore at the thread's minor diameter plus a modeled groove along it
                double D = _threadDia.Value, P = _threadPitch.Value, td = _threadDepth.Value > 0 ? _threadDepth.Value : t1 - t0;
                if (D <= 1.082532 * P) throw new FormatException("The pitch must be smaller than the diameter / 1.083.");
                if (td > t1 - t0 + 1e-6 || td / P > 200) throw new FormatException("The thread must fit inside the hole, with at most 200 turns.");
                var into = entryTop ? -d : d; var entry = o + d * (entryTop ? t1 : t0);
                // the Hole tool's construction: a right-handed frame (x, z x x, z), the bore starting 0.01 mm outside the entry so no boolean meets a coincident face
                var gx = Xform.AnyPerpendicular(into); var gy = Vec3.Cross(into, gx).Normalized();
                double[] frame = [gx.X, gy.X, into.X, gx.Y, gy.Y, into.Y, gx.Z, gy.Z, into.Z];
                var core = Occ.Transform(Occ.Cylinder((D - 1.082532 * P) / 2, t1 - t0 + 0.01), frame, entry - into * 0.01);
                groove = Occ.Transform(Occ.ThreadGroove(D, P, td, _leftHand.Value), frame, entry);   // cut after the bore, as the Hole tool does
                return core;
            default: return hole;
        }
    }

    // ---------------------------------------------------------------- fillet radius
    /// <summary>The largest radius of the picked fillets (a torus's tube radius, otherwise the round's radius).</summary>
    static double RadiusOf(SceneObject body, IEnumerable<Feat> feats)
    {
        double r = 0;
        foreach (int i in feats.SelectMany(f => f.Faces))
        {
            using var face = body.Shape.Sub(SubKind.Face, i); var info = Occ.FaceInfoOf(face);
            r = Math.Max(r, info.Type == "Torus" ? info.R2 : info.R1);
        }
        if (r <= 0) throw new FormatException("Could not read that fillet. Delete it and add a new Fillet instead.");
        return r;
    }

    /// <summary>
    /// Rounds or bevels again the edges that picked fillets or chamfers replaced. Their faces were already removed, so the sharp edges are back:
    /// they are the edges the removal created (not on the original body) that run alongside the removed faces. All of them are redone together,
    /// so neighbours that meet are treated as one.
    /// </summary>
    static Shape Resharpened(SceneObject body, Shape current, List<Feat> feats, Func<Shape, int[], Shape> redo)
    {
        var m = body.Mesh; var original = body.Shape;
        double size = feats.Max(f => FeatureFinder.ChamferOf(body, f) is { } c ? Math.Max(c.SetbackA, c.SetbackB) : f.Kind == FeatureKind.Fillet ? RadiusOf(body, [f]) : 0);
        var pts = new List<Vec3>();
        foreach (int i in feats.SelectMany(f => f.Faces))
            for (int t = m.FaceTriStart[i]; t < m.FaceTriStart[i + 1]; t++)
                for (int c = 0; c < 3; c++) { int vi = m.Indices[3 * t + c] * 3; pts.Add(new Vec3(m.Positions[vi], m.Positions[vi + 1], m.Positions[vi + 2])); }
        if (size <= 0 || pts.Count == 0) throw new FormatException("Could not read that feature's size. Delete it and add it again instead.");
        double reach = 2.5 * size + 0.01;
        bool Near(Vec3 p) { foreach (var q in pts) if ((q - p).Length <= reach) return true; return false; }
        var oldEdges = new List<Vec3>();
        for (int e = 0; e < original.Count(SubKind.Edge); e++) { var ei = Occ.EdgeInfoOf(original.Sub(SubKind.Edge, e)); oldEdges.Add((ei.Start + ei.End) * 0.5); oldEdges.Add(ei.Start); }
        bool WasThere(Vec3 mid, Vec3 start) { foreach (var q in oldEdges) if ((q - mid).Length < 1e-4 || (q - start).Length < 1e-4) return true; return false; }
        var found = new List<int>();
        for (int e = 0; e < current.Count(SubKind.Edge); e++)
        {
            var ei = Occ.EdgeInfoOf(current.Sub(SubKind.Edge, e)); var mid = (ei.Start + ei.End) * 0.5;
            if (WasThere(mid, ei.Start) || !Near(ei.Start) || !Near(ei.End)) continue;
            found.Add(e);
        }
        if (found.Count == 0) throw new FormatException("Could not find the sharp edges that feature replaced. Delete it and add it again instead.");
        return redo(current, [.. found]);
    }
}
