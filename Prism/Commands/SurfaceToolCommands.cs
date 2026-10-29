using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core;
using Prism.Kernel;

namespace Prism.Commands;

/// <summary>Take a sewn shell (or a solid) apart into separate surfaces: everything, or just the faces you pick.</summary>
public sealed class UnstitchCommand : Command
{
    readonly SelectionSlot _src;
    public UnstitchCommand()
    {
        _src = Slot(new SelectionSlot { Label = "Faces, or whole surfaces and bodies", Filter = SelFilter.Face | SelFilter.SurfaceObj | SelFilter.Solid, Multi = true });
    }
    public override string Title => "Unsew";
    public override string Hint => "Pick faces to pull out of their surface, or pick a whole sewn surface (or solid) to break it into one surface per face. Picked faces become separate surfaces; the rest stays sewn together.";

    protected override CommandOutput? Build()
    {
        if (_src.Items.Count == 0) return null;
        Results.Clear();
        var o = new CommandOutput { Label = "Unsew", Icon = "unstitch" };
        int made = 0;
        foreach (var g in _src.Items.GroupBy(i => i.Obj))
        {
            var obj = S.Find(g.Key) ?? throw new InvalidOperationException("A picked surface is gone.");
            int n = obj.Mesh.FaceCount;
            var faceIdx = g.Any(i => i.Kind != SelKind.Face) ? Enumerable.Range(0, n).ToHashSet() : g.Select(i => i.Index).ToHashSet();
            if (n < 2) { Results.Add(new ResultLine(obj.Name, "has only one face, so there is nothing to separate", 1)); continue; }
            foreach (var i in faceIdx.OrderBy(i => i))
            {
                var face = S.ShapeOf(new SelItem(obj.Id, SelKind.Face, i)) ?? throw new InvalidOperationException("Could not read a face.");
                o.Add.Add(new(face, "Surface", ObjKind.Surface)); made++;
            }
            var rest = Enumerable.Range(0, n).Where(i => !faceIdx.Contains(i)).Select(i => S.ShapeOf(new SelItem(obj.Id, SelKind.Face, i))!).ToList();
            if (rest.Count > 0) o.Add.Add(new(rest.Count == 1 ? rest[0] : Occ.Sew(rest, 0.01, false), "Surface", ObjKind.Surface));
            o.Remove.Add(obj);
        }
        if (made == 0) return null;
        Results.Add(new ResultLine("Separate surfaces", made.ToString()));
        return o;
    }
}

/// <summary>Undo a trim: give a face its whole natural surface back, or keep its outline and fill the holes.</summary>
public sealed class UntrimCommand : Command
{
    readonly SelectionSlot _src;
    readonly ChoiceParam _mode;
    public UntrimCommand()
    {
        _src = Slot(new SelectionSlot { Label = "Surface or face", Filter = SelFilter.SurfaceObj | SelFilter.Face, Multi = true });
        _mode = Choice("What to restore", 0, "The whole natural surface", "Fill the holes only (keep the outline)");
    }
    public override string Title => "Untrim";
    public override string Hint => "Pick a trimmed surface or face. 'Natural surface' removes every trim and gives back the full patch it was cut from; 'Fill the holes' keeps the outer outline and closes the cut-outs. Planes and other endless surfaces only offer the second.";

    protected override CommandOutput? Build()
    {
        if (_src.Items.Count == 0) return null;
        Results.Clear();
        var o = new CommandOutput { Label = "Untrim", Icon = "untrim" };
        foreach (var it in _src.Items)
        {
            var obj = S.Find(it.Obj) ?? throw new InvalidOperationException("A picked surface is gone.");
            bool whole = it.Kind != SelKind.Face || obj.Mesh.FaceCount == 1;
            var face = whole ? obj.Shape : S.ShapeOf(it)!;
            var res = Occ.Untrim(face, _mode.Index);
            if (whole) o.Replace.Add((obj, res));
            else { o.Add.Add(new(res, "Untrimmed", ObjKind.Surface)); Results.Add(new ResultLine(obj.Name, "has several faces: the untrimmed face is a new surface, the original is kept", 1)); }
        }
        return o;
    }
}

/// <summary>Grow a surface past one of its edges, continuing its curvature.</summary>
public sealed class ExtendSurfaceCommand : Command
{
    readonly SelectionSlot _src, _edge;
    readonly NumberParam _len;
    public ExtendSurfaceCommand()
    {
        _src = Slot(new SelectionSlot { Label = "Surface or face", Filter = SelFilter.SurfaceObj | SelFilter.Face });
        _edge = Slot(new SelectionSlot { Label = "Edge to extend", Filter = SelFilter.Edge });
        _len = Num("Extend by", 10, 0.001, 1e5, 1);
    }
    public override string Title => "Extend";
    public override string Hint => "Pick a surface, then the edge to extend, then how far. The surface keeps its shape and just gets longer (a trimmed face is first restored to its full patch).";

    public override HandleSpec? Handle
    {
        get
        {
            if (_edge.Items.Count == 0) return null;
            try
            {
                var e = Occ.EdgeInfoOf(S.ShapeOf(_edge.Items[0])!); var mid = (e.Start + e.End) * 0.5;
                if (_src.Items.Count == 0) return null;
                var obj = S.Find(_src.Items[0].Obj); if (obj == null) return null;
                var c = CenterOf(obj.Shape);
                var dir = (mid - c); dir = dir.Length < 1e-9 ? Vec3.UnitZ : dir.Normalized();
                return new HandleSpec(mid, dir, _len);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException) { return null; }
        }
    }

    protected override CommandOutput? Build()
    {
        if (_src.Items.Count == 0 || _edge.Items.Count == 0) return null;
        var it = _src.Items[0];
        var obj = S.Find(it.Obj) ?? throw new InvalidOperationException("The surface is gone.");
        var face = it.Kind == SelKind.Face ? S.ShapeOf(it)! : obj.Shape;
        var edge = Occ.EdgeInfoOf(S.ShapeOf(_edge.Items[0])!);
        var res = Occ.ExtendFace(face, (edge.Start + edge.End) * 0.5, _len.Value);
        var o = new CommandOutput { Label = "Extend surface", Icon = "extend-surface" };
        if (it.Kind != SelKind.Face || obj.Mesh.FaceCount == 1) o.Replace.Add((obj, res)); else o.Add.Add(new(res, "Extended", ObjKind.Surface));
        return o;
    }
}

/// <summary>Turn surfaces around so the other side faces out (matters for thickening, offsets and shading).</summary>
public sealed class ReverseNormalCommand : Command
{
    readonly SelectionSlot _src;
    public ReverseNormalCommand()
    {
        _src = Slot(new SelectionSlot { Label = "Surfaces", Filter = SelFilter.SurfaceObj, Multi = true });
    }
    public override string Title => "Reverse Normal";
    public override string Hint => "Pick surfaces to flip. The arrows of Thicken and Offset follow the normal, so flip a surface when they point the wrong way.";

    protected override CommandOutput? Build()
    {
        if (_src.Items.Count == 0) return null;
        var o = new CommandOutput { Label = "Reverse normal", Icon = "reverse-normal" };
        foreach (var obj in ObjectsOf(_src).Distinct()) o.Replace.Add((obj, Occ.Reverse(obj.Shape)));
        return o;
    }
}
