using System;
using System.Linq;
using Prism.Core.Sketching;
using Prism.Kernel;

namespace Prism.Core;

public partial class CadSession
{
    /// <summary>
    /// A mode-less viewport tool that owns the pointer while no command or sketch is open
    /// (standalone datum placement, etc.). It lives behind <see cref="ActiveCommand"/> and
    /// <see cref="Sketch"/> in the <see cref="ActiveTool"/> chain.
    /// </summary>
    public IViewportTool? FreeTool { get; private set; }

    /// <summary>Install or clear the ambient viewport tool; the outgoing tool is deactivated.</summary>
    public void SetFreeTool(IViewportTool? tool)
    {
        if (ReferenceEquals(FreeTool, tool)) return;
        if (tool != null && (ActiveCommand != null || Sketch != null)) { Say("Finish or cancel the active tool first.", true); return; }
        FreeTool?.Deactivate(this);
        FreeTool = tool;
        RaiseState();
        Invalidate();
    }

    SceneObject? _datumCarrier;
    SelItem? _lastTypedMove; DateTime _lastTypedMoveAt;

    SceneObject? FindDatumCarrier()
    {
        if (_datumCarrier is { } carrier && Objects.Contains(carrier)) return carrier;
        return _datumCarrier = Objects.FirstOrDefault(o => o.Kind == ObjKind.Sketch
            && o.SketchData is { Sections.Count: 0, Entities.Count: 0 }
            && o.HasBrep && o.Shape.Kind == ShapeKind.Vertex);
    }

    /// <summary>
    /// The object that owns model-space datums. It is an ordinary Sketch-kind object whose
    /// <see cref="SceneObject.SketchData"/> only carries <see cref="SketchModel.Datums"/>: picking,
    /// overlay drawing, the browser, undo and file save all already understand that shape. Its
    /// vertex shape tessellates to nothing so it is never drawn or picked itself.
    /// </summary>
    public SceneObject DatumCarrier()
    {
        if (FindDatumCarrier() is { } carrier) return carrier;
        var model = new SketchModel();
        _datumCarrier = AddObject(Occ.Vertex(Vec3.Zero), "Datums", ObjKind.Sketch, plane: Plane3.XY, sketch: model);
        return _datumCarrier;
    }

    /// <summary>Adds a free-standing datum to the model datums carrier. One undo step per placement.</summary>
    public SketchDatum AddModelDatum(SketchDatum datum)
    {
        var carrier = DatumCarrier();
        int i = Objects.IndexOf(carrier);
        var model = carrier.SketchData!.Clone();
        datum.Name = $"{datum.Kind} {model.Datums.Count(d => d.Kind == datum.Kind) + 1}";
        model.Datums.Add(datum);
        Checkpoint($"Datum {datum.Kind}", "origin-plane");
        Objects = Objects.SetItem(i, carrier.WithSketch(model));
        _datumCarrier = Objects[i];
        RaiseScene();
        Invalidate();
        return datum;
    }

    /// <summary>
    /// Selecting an axis or plane datum raises the rotate gizmo on it, so a wrong orientation can be fixed by hand.
    /// A running placement tool stays armed; the gizmo only answers clicks on its own handles.
    /// </summary>
    public void ShowDatumOrientation(SelItem item)
    {
        if (ActiveCommand != null || Sketch != null || DatumOf(item) is not { } d) return;
        // Already the gizmo's only target (a selection echo from our own edit): rebinding would reset a drag in progress.
        if (DatumVisible && Datum!.Targets is [var only] && only == item) return;
        ShowDatumGizmo(d.Frame.Origin, keepTool: FreeTool is DatumPickTool);
        Datum!.Mode = d.Kind is SketchDatumKind.Axis or SketchDatumKind.Plane ? 1 : 0;
        Datum.BindTargets(this, [item]);
        Say(d.Kind == SketchDatumKind.Gizmo
            ? $"Gizmo '{d.Name}': drag its handles, or type values in the Gizmo panel."
            : $"{d.Kind} '{d.Name}': drag a ring to reorient it, an arrow to move it, or type values in the Datum panel.");
    }

    /// <summary>Typed relocation of one datum: keeps its orientation.</summary>
    public void MoveDatumTo(SelItem item, Vec3 origin)
    {
        if (item.Kind != SelKind.Datum) return;
        // Typing digits is a burst of edits to one datum: only the first of a burst starts an undo step.
        bool burst = item == _lastTypedMove && DateTime.UtcNow - _lastTypedMoveAt < TimeSpan.FromSeconds(2);
        _lastTypedMove = item; _lastTypedMoveAt = DateTime.UtcNow;
        Update(item.Obj, o =>
        {
            if (o.SketchData is not { } model || item.Index >= model.Datums.Count) return o;
            var m = model.Clone();
            var f = m.Datums[item.Index].Frame;
            m.Datums[item.Index].Frame = new Plane3(origin, f.U, f.V);
            return o.WithSketch(m);
        }, checkpoint: !burst, label: "Move datum");
        if (DatumVisible && Datum!.Targets.Contains(item)) ShowDatumOrientation(item);     // rebind so the gizmo follows the typed location
    }

    /// <summary>The datum that was placed last by <see cref="AddModelDatum"/>, as a selectable item.</summary>
    public SelItem? LastModelDatum()
    {
        if (FindDatumCarrier() is not { } carrier || carrier.SketchData!.Datums.Count == 0) return null;
        return new SelItem(carrier.Id, SelKind.Datum, carrier.SketchData.Datums.Count - 1);
    }

    /// <summary>
    /// Rewrites one datum's frame in place (dragging it with the move gizmo, typing panel values).
    /// Undo checkpointing is the caller's choice — a drag checkpoints once at grab time.
    /// </summary>
    public void SetDatumFrame(SelItem item, Plane3 frame)
    {
        if (item.Kind != SelKind.Datum) return;
        Update(item.Obj, o =>
        {
            if (o.SketchData is not { } model || item.Index >= model.Datums.Count) return o;
            var m = model.Clone();
            m.Datums[item.Index].Frame = frame;
            return o.WithSketch(m);
        }, checkpoint: false, label: "Move datum");
    }

    /// <summary>Moves an object selection target by a rigid transform; datums keep their sketch-data representation.</summary>
    /// <summary>Why a part cannot be dragged freely: grounded, or positioned by a joint (Drive Joints moves it). Null when it is free.</summary>
    public string? WhyPartIsHeld(SceneObject o)
    {
        if (o.Component is not { } ci) return null;
        if (ci.Grounded) return $"{o.Name} is grounded (fixed). Unground it to move it.";
        if (JointObjects.Any(j => j.joint.ChildKey == ci.Key || j.joint.ParentKey == ci.Key)) return $"{o.Name} is held by a joint. Use Drive Joints to move it, or delete the joint.";
        return null;
    }

    /// <summary>
    /// The Move gizmo's scale (owner request 2026-11-23): scale an object or datum about <paramref name="center"/> by <paramref name="factors"/> along the three
    /// orthonormal <paramref name="axes"/> (the gizmo's own). Bodies, surfaces, curves, meshes and freeforms stretch per axis; a sketch scales only evenly, so it
    /// stays an editable sketch; a datum only has its position scaled; an assembly part is refused (its placement is rigid).
    /// </summary>
    public void ScaleTarget(SelItem item, Vec3 center, Vec3[] axes, Vec3 factors, bool checkpoint = false)
    {
        double[] k = [factors.X, factors.Y, factors.Z];
        Vec3 Map(Vec3 p) { var d = p - center; var r = center; for (int i = 0; i < 3; i++) r += axes[i] * (Vec3.Dot(d, axes[i]) * k[i]); return r; }
        bool even = Math.Abs(k[0] - k[1]) < 1e-9 && Math.Abs(k[1] - k[2]) < 1e-9;
        if (item.Kind == SelKind.Datum)
        {
            if (DatumOf(item) is null) return;
            Update(item.Obj, o =>
            {
                if (o.SketchData is not { } model || item.Index >= model.Datums.Count) return o;
                var m = model.Clone(); var f = m.Datums[item.Index].Frame;
                m.Datums[item.Index].Frame = new Plane3(Map(f.Origin), f.U, f.V);
                return o.WithSketch(m);
            }, checkpoint: checkpoint, label: "Scale");
            return;
        }
        var obj = Find(item.Obj);
        if (obj == null) return;
        if (obj.Component != null) { Say($"{obj.Name} is an assembly part: parts keep their size. Scale it in its own part file.", true); return; }
        // the linear part as a row-major 3x3 matrix, and the move that keeps the centre still
        static double Comp(Vec3 v, int i) => i == 0 ? v.X : i == 1 ? v.Y : v.Z;
        var m3 = new double[9];
        for (int r = 0; r < 3; r++) for (int c = 0; c < 3; c++)
        {
            double v = 0; for (int i = 0; i < 3; i++) v += k[i] * Comp(axes[i], r) * Comp(axes[i], c);
            m3[3 * r + c] = v;
        }
        var shift = center - new Vec3(m3[0] * center.X + m3[1] * center.Y + m3[2] * center.Z, m3[3] * center.X + m3[4] * center.Y + m3[5] * center.Z, m3[6] * center.X + m3[7] * center.Y + m3[8] * center.Z);
        if (obj.Tri is { } tri)
            Update(item.Obj, o => o.WithTri(tri.Transformed(Map)), checkpoint: checkpoint, label: "Scale");
        else if (obj.Form is { } form)
        {
            var scaled = form.Clone();
            for (int i = 0; i < scaled.V.Count; i++) scaled.V[i] = Map(scaled.V[i]);
            Update(item.Obj, o => o.WithForm(scaled), checkpoint: checkpoint, label: "Scale");
        }
        else if (obj is { Kind: ObjKind.Sketch, SketchData: { } model, SketchPlane: { } sp, HasBrep: true } && obj.Shape is { } drawn)
        {
            if (!even) { Say("A sketch scales evenly only, so it stays an editable sketch. Use the centre square, or scale the other items on their own.", true); return; }
            var m = model.Clone(); Commands.TransformCommand.ScaleSketchModel(m, k[0]);
            Update(item.Obj, o => o.WithSketch(Occ.Scale(drawn, center, k[0]), m, new Plane3(Map(sp.Origin), sp.U, sp.V)), checkpoint: checkpoint, label: "Scale");
        }
        else if (obj.SketchData != null) { /* a datum carrier keeps its own frames */ }
        else if (obj.HasBrep && obj.Shape is { } shape)
            Update(item.Obj, o => o.Kind == ObjKind.Plane && o.SketchPlane is { } pl
                ? o.WithShape(Occ.Affine(shape, m3, shift)).WithPlane(new Plane3(Map(pl.Origin), pl.U, pl.V))
                : o.WithShape(Occ.Affine(shape, m3, shift)), checkpoint: checkpoint, label: "Scale");
    }

    public void TransformTarget(SelItem item, Assembly.Xform delta, bool checkpoint = false)
    {
        if (item.Kind == SelKind.Datum)
        {
            if (DatumOf(item) is not { } datum) return;
            Update(item.Obj, o =>
            {
                if (o.SketchData is not { } model || item.Index >= model.Datums.Count) return o;
                var m = model.Clone();
                var f = m.Datums[item.Index].Frame;
                m.Datums[item.Index].Frame = new Plane3(delta.Apply(f.Origin), delta.Rotate(f.U), delta.Rotate(f.V));
                return o.WithSketch(m);
            }, checkpoint: checkpoint, label: "Move datum");
            return;
        }
        var o = Find(item.Obj);
        if (o == null || delta.IsIdentity) return;
        if (o.Component is { } ci)
        {
            // an assembly part: its placement is the truth the joint solver reads, so it moves together with the shape
            if (WhyPartIsHeld(o) is { } why) { Say(why, true); return; }
            var np = delta * ci.Placement;
            Update(item.Obj, obj => obj.WithAsm(ci.WithPlacement(np), np.Apply(LocalShapeOf(obj))), checkpoint: checkpoint, label: $"Move {o.Name}");
            return;
        }
        if (o.Tri is { } tri)
            Update(item.Obj, obj => obj.WithTri(tri.Transformed(delta.Apply)), checkpoint: checkpoint, label: "Move");
        else if (o.Form is { } form)
        {
            var moved = form.Clone();
            for (int i = 0; i < moved.V.Count; i++) moved.V[i] = delta.Apply(moved.V[i]);
            Update(item.Obj, obj => obj.WithForm(moved), checkpoint: checkpoint, label: "Move");
        }
        else if (o is { Kind: ObjKind.Sketch, SketchData: { } model, SketchPlane: { } sp, HasBrep: true } && o.Shape is { } drawn)
            // a sketch (text included) moves by moving its plane, so it stays an editable sketch where it lands
            Update(item.Obj, obj => obj.WithSketch(delta.Apply(drawn), model.Clone(), new Plane3(delta.Apply(sp.Origin), delta.Rotate(sp.U), delta.Rotate(sp.V))), checkpoint: checkpoint, label: "Move");
        else if (o.SketchData != null) { /* the datum carrier keeps its own frames: its datums move one by one */ }
        else if (o.HasBrep && o.Shape is { } shape)
            Update(item.Obj, obj => obj.Kind == ObjKind.Plane && obj.SketchPlane is { } pl
                ? obj.WithShape(delta.Apply(shape)).WithPlane(new Plane3(delta.Apply(pl.Origin), delta.Rotate(pl.U), delta.Rotate(pl.V)))
                : obj.WithShape(delta.Apply(shape)), checkpoint: checkpoint, label: "Move");
    }
}
