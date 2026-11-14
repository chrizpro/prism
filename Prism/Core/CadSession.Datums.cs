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

    /// <summary>
    /// The object that owns model-space datums. It is an ordinary Sketch-kind object whose
    /// <see cref="SceneObject.SketchData"/> only carries <see cref="SketchModel.Datums"/>: picking,
    /// overlay drawing, the browser, undo and file save all already understand that shape. Its
    /// vertex shape tessellates to nothing so it is never drawn or picked itself.
    /// </summary>
    public SceneObject DatumCarrier()
    {
        if (_datumCarrier is { } c && Objects.Contains(c)) return c;
        _datumCarrier = Objects.FirstOrDefault(o => o.Kind == ObjKind.Sketch
            && o.SketchData is { Sections.Count: 0, Entities.Count: 0 }
            && o.HasBrep && o.Shape.Kind == ShapeKind.Vertex);
        if (_datumCarrier != null) return _datumCarrier;
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
        var carrier = DatumCarrier();
        int n = carrier.SketchData!.Datums.Count;
        return n == 0 ? null : new SelItem(carrier.Id, SelKind.Datum, n - 1);
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
        else if (o.SketchData != null) { /* sketch bodies and the datum carrier keep their own frames */ }
        else if (o.HasBrep && o.Shape is { } shape)
            Update(item.Obj, obj => obj.Kind == ObjKind.Plane && obj.SketchPlane is { } pl
                ? obj.WithShape(delta.Apply(shape)).WithPlane(new Plane3(delta.Apply(pl.Origin), delta.Rotate(pl.U), delta.Rotate(pl.V)))
                : obj.WithShape(delta.Apply(shape)), checkpoint: checkpoint, label: "Move");
    }
}
