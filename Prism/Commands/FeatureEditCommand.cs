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
    readonly NumberParam _dx, _dy, _dz, _copies, _dia, _width, _length;

    public FeatureEditCommand()
    {
        _faces = Slot(new SelectionSlot { Label = "Feature to edit (click a hole, slot, pocket, boss or fillet)", Filter = SelFilter.Face, Multi = true, SmartPick = true });
        _action = Choice("Action", 0, "Move", "Resize", "Copy", "Delete");
        _dx = Num("Move X", 0); _dy = Num("Move Y", 0); _dz = Num("Move Z", 0);
        _copies = Num("Number of copies", 1, 1, 200, 1);
        _dia = Num("New diameter (0 = keep)", 0, 0, 1e5, 1);
        _width = Num("New slot width (0 = keep)", 0, 0, 1e5, 1);
        _length = Num("New slot length (0 = keep)", 0, 0, 1e5, 1);
    }

    public override string Title => "Edit Feature";
    public override string Icon => "movehole";
    public override string OkText => "Apply";
    public override string Hint => "Click any face of a hole, slot, pocket, boss or fillet: the whole feature is picked. Then choose what to do. Move slides it by the X / Y / Z you type. Copy makes copies at that spacing. Resize changes the diameter of a round hole or boss, or the width and length of a slot. Delete removes it and closes the gap. “Select by” can pick every feature of the same size or a whole pattern at once.";

    protected override void UpdateVisibility()
    {
        int a = _action.Index; bool move = a is 0 or 2;
        _dx.IsVisible = _dy.IsVisible = _dz.IsVisible = move;
        _copies.IsVisible = a == 2;
        var kinds = PickedKinds();
        _dia.IsVisible = a == 1 && (kinds.Count == 0 || kinds.Any(k => k is FeatureKind.Hole or FeatureKind.Boss));
        _width.IsVisible = _length.IsVisible = a == 1 && (kinds.Count == 0 || kinds.Contains(FeatureKind.Slot));
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
        if (act == 1 && _dia.Value <= 0 && _width.Value <= 0 && _length.Value <= 0) return null;
        var o = new CommandOutput { Label = $"{_action.Selected} feature", Icon = "movehole" };
        foreach (var grp in DirectHelpers.ByBody(_faces.Items))
        {
            var body = DirectHelpers.BodyOf(S, grp.First()) ?? throw new FormatException("Pick faces on a solid body.");
            var feats = grp.Select(i => FeatureFinder.FeatureOf(body, i.Index) ?? throw new FormatException("That face is gone.")).Distinct().ToList();
            // fill: material added where a hole was; strip: a boss cut away; drill: new holes; grow: new bosses
            List<Shape> fill = [], strip = [], drill = [], grow = []; List<int> fillets = [];
            foreach (var f in feats)
            {
                bool boss = f.Kind == FeatureKind.Boss;
                switch (f.Kind)
                {
                    case FeatureKind.Face: throw new FormatException("That is a plain face, not a feature. Use Move Face or Push / Pull to change it.");
                    case FeatureKind.Fillet:
                        if (act != 3) throw new FormatException("A fillet can only be deleted here. Use Fillet to add a new one.");
                        fillets.AddRange(f.Faces); break;
                    default:
                        var tool = ToolOf(body, f);
                        switch (act)
                        {
                            case 0: (boss ? strip : fill).Add(tool); (boss ? grow : drill).Add(Occ.Translate(tool, move)); break;
                            case 2: for (int k = 1; k <= (int)_copies.Value; k++) (boss ? grow : drill).Add(Occ.Translate(tool, move * k)); break;
                            case 3: (boss ? strip : fill).Add(tool); break;
                            default:
                                var resized = Resized(body, f);
                                if (resized == null) continue;
                                (boss ? strip : fill).Add(tool); (boss ? grow : drill).Add(resized); break;
                        }
                        break;
                }
            }
            var start = fillets.Count > 0 ? Occ.Defeature(body.Shape, [.. fillets.Distinct()]) : body.Shape;
            o.Replace.Add((body, DirectHelpers.Batch(DirectHelpers.Batch(start, fill, strip), grow, drill)));
            foreach (var g in feats.GroupBy(f => f.Name))
                Results.Add(new ResultLine(g.Key + (g.Count() > 1 ? "s" : ""), $"{_action.Selected.ToLowerInvariant()}: {g.Count()}"));
        }
        return o;
    }

    /// <summary>The solid a feature occupies: the void of a hole, slot or pocket (to fill or re-cut) or the material of a boss (to remove or re-add).</summary>
    internal static Shape ToolOf(SceneObject body, Feat f)
    {
        if (f.Round is { } r) return DirectHelpers.Cylinder(r.Origin, f.Dir, r.T0, r.T1, r.Radius);
        var filled = Occ.Defeature(body.Shape, [.. f.Faces]);
        return f.Kind == FeatureKind.Boss ? Occ.Cut(body.Shape, filled) : Occ.Cut(filled, body.Shape);
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
        throw new FormatException($"Resize works on round holes and bosses and on slots. For a {f.Name.ToLowerInvariant()}, move its walls with Move Face.");
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
}
