using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core;
using Prism.Core.Assembly;
using Prism.Kernel;

namespace Prism.Commands;

/// <summary>
/// The Direct Modeling gizmo. It stays open while the Direct Modeling tab is open: click a face, hole, slot, pocket, boss or fillet (one click picks the
/// whole feature) and controls appear on it. The shared gizmo moves the selection, the orange handle pulls every picked face along its own normal, and the
/// turning handle tilts a flat face. Each handle does the same job as one of the older tools (Move Face, Move Feature, Offset Face, Rotate Face), so this
/// command hands the work to them. Edits made here are plain undo steps: they are not recorded as history unless the body is tracked.
/// </summary>
public sealed class DirectEditCommand : Command
{
    const uint PullColour = 0xFFF0A030, TurnColour = 0xFFB070E0, SizeColour = 0xFF3FC6C6;

    readonly SelectionSlot _faces;
    readonly NumberParam _dx, _dy, _dz, _offset, _rotate, _radius;

    public DirectEditCommand()
    {
        _faces = Slot(new SelectionSlot { Label = "Click a face or feature (a hole, slot, pocket, boss or fillet)", Filter = SelFilter.Face, Multi = true, SmartPick = true });
        _dx = Num("Move X", 0); _dy = Num("Move Y", 0); _dz = Num("Move Z", 0);
        _offset = Num("Pull (positive moves faces out)", 0);
        _rotate = Num("Tilt", 0, -89, 89, 1, "°");
        _radius = Num("Radius change (round holes and bosses)", 0);
    }

    public override string Title => "Direct Edit";
    public override string Icon => "pushpull";
    public override string OkText => "Apply";
    // Switching to another tool never asks "discard unfinished work?" for something that is only a selection.
    public override bool HasInputChanges => false;
    // A direct edit is a plain undo step; the Edit Feature command and the others keep their own recipes.
    public override bool CanEdit => false;
    public override string Hint => "Click a face, hole, slot, pocket, boss or fillet. Drag the gizmo to move it, the orange handle to pull it out or in along its own normal, or the purple arc to tilt a flat face. Type a number while a handle is showing to set it exactly. Delete removes the feature.";

    // ------------------------------------------------------------ the picked geometry
    (SceneObject body, Feat? feature)? First()
    {
        if (S == null || _faces.Items.Count == 0) return null;
        try
        {
            var it = _faces.Items[0];
            if (DirectHelpers.BodyOf(S, it) is not { } body) return null;
            var f = FeatureFinder.FeatureOf(body, it.Index);
            return (body, f is { Kind: FeatureKind.Face } ? null : f);
        }
        catch (KernelException) { return null; }
    }

    bool PickedFeature(out bool allFeatures)
    {
        allFeatures = true; bool any = false;
        foreach (var grp in DirectHelpers.ByBody(_faces.Items))
        {
            if (DirectHelpers.BodyOf(S, grp.First()) is not { } body) { allFeatures = false; continue; }
            foreach (var it in grp)
            {
                var f = FeatureFinder.FeatureOf(body, it.Index);
                if (f is null or { Kind: FeatureKind.Face }) allFeatures = false; else any = true;
            }
        }
        return any;
    }

    /// <summary>Where the controls sit on a picked face and which way it faces: the middle of a flat face, a point on a curved one.</summary>
    (Vec3 point, Vec3 normal)? Anchor(SelItem it)
    {
        if (S.FaceFrame(it) is not { } frame) return null;
        bool flat = S.ShapeOf(it) is { } face && Occ.FaceInfoOf(face).Type == "Plane";
        return flat && S.FaceMiddle(it) is { } middle ? (middle, frame.normal) : frame;
    }

    public override IReadOnlyList<HandleSpec> Handles
    {
        get
        {
            if (First() is not { } first) return [];
            try
            {
                var (body, feature) = first; var it = _faces.Items[0];
                var list = new List<HandleSpec>();
                var frame = Anchor(it);
                if (frame is { } fr && Math.Abs(_dx.Value) + Math.Abs(_dy.Value) + Math.Abs(_dz.Value) + Math.Abs(_rotate.Value) < 1e-9)
                    list.Add(new HandleSpec(fr.point, fr.normal, _offset, 1, Colour: PullColour));
                var centre = feature?.Centre ?? frame?.point ?? CenterOf(body.Shape);
                // a round hole or boss gets a handle on its rim that sets the radius
                if (feature is { Round: { } round, Kind: FeatureKind.Hole or FeatureKind.Boss } && Math.Abs(_dx.Value) + Math.Abs(_dy.Value) + Math.Abs(_dz.Value) + Math.Abs(_offset.Value) < 1e-9)
                {
                    var radial = Xform.AnyPerpendicular(feature.Dir).Normalized();
                    list.Add(new HandleSpec(round.Origin + feature.Dir.Normalized() * (round.T1), radial, _radius, 1, Colour: SizeColour));
                }
                if (feature == null && _faces.Items.Count == 1 && S.PlaneOf(it) is { } pl && Occ.FaceInfoOf(S.ShapeOf(it)!).Type == "Plane")
                {
                    var size = body.Mesh.Max - body.Mesh.Min;
                    list.Add(new HandleSpec(frame?.point ?? centre, pl.U, _rotate, 1, pl.Normal, Math.Max(size.Length * 0.12, 1), true, TurnColour));
                }
                return list;
            }
            catch (Exception e) when (e is KernelException or InvalidOperationException) { return []; }
        }
    }

    /// <summary>Moving the picked face or feature is a change of position, so the shared gizmo does it; the pull, radius and tilt handles stay as sizes.</summary>
    public override GizmoSpec? Gizmo
    {
        get
        {
            if (First() is not { } first) return null;
            try
            {
                var (body, feature) = first; var frame = Anchor(_faces.Items[0]);
                var centre = feature?.Centre ?? frame?.point ?? CenterOf(body.Shape);
                NumberParam?[] move = [_dx, _dy, _dz];
                // one control per direction: while the pull arrow leaves the same spot along a world axis, it stands in for that axis's arrow
                bool pullShowing = Math.Abs(_dx.Value) + Math.Abs(_dy.Value) + Math.Abs(_dz.Value) + Math.Abs(_rotate.Value) < 1e-9;
                if (pullShowing && frame is { } fr && (fr.point - centre).Length < 1e-6)
                    for (int i = 0; i < 3; i++) if (Vec3.Dot(fr.normal.Normalized(), GizmoVisual.WorldAxes[i]) > 0.999) move[i] = null;
                return new GizmoSpec(centre + new Vec3(_dx.Value, _dy.Value, _dz.Value), move, GizmoSpec.None);
            }
            catch (Exception e) when (e is KernelException or InvalidOperationException) { return null; }
        }
    }

    // ------------------------------------------------------------ build: hand the work to the tool that already does it
    /// <summary>The recipe of the tool that did the last edit, kept so a tracked body can record it as an editable history step.</summary>
    public FeatureRecipe? InnerRecipe { get; private set; }

    CommandOutput Run(Command c, Action<Command>? prepare = null, params (string label, double value)[] values)
    {
        c.Begin(S);
        c.ConfigureInputs(() =>
        {
            c.Slots[0].Items.Clear();        // only what was clicked: the tool must not also take a body left selected in the document
            foreach (var it in _faces.Items) c.Slots[0].TryAdd(it);
            prepare?.Invoke(c);
            foreach (var (label, value) in values)
                c.Params.OfType<NumberParam>().First(p => p.Label.StartsWith(label, StringComparison.OrdinalIgnoreCase)).Value = value;
        });
        var output = c.TryBuild() ?? throw new FormatException(c.Error ?? "Nothing to change.");
        InnerRecipe = c.Capture();
        return output;
    }

    protected override CommandOutput? Build()
    {
        Results.Clear();
        if (_faces.Items.Count == 0) return null;
        var move = new Vec3(_dx.Value, _dy.Value, _dz.Value);
        bool moving = move.Length > 1e-9, pulling = Math.Abs(_offset.Value) > 1e-9, tilting = Math.Abs(_rotate.Value) > 1e-9;
        bool sizing = Math.Abs(_radius.Value) > 1e-9;
        int ops = (moving ? 1 : 0) + (pulling ? 1 : 0) + (tilting ? 1 : 0) + (sizing ? 1 : 0);
        if (ops == 0) return null;
        if (ops > 1) throw new FormatException("Use one handle at a time: move, pull, tilt or resize.");

        CommandOutput result;
        if (sizing)
        {
            if (First() is not { feature: { Round: { } round, Kind: FeatureKind.Hole or FeatureKind.Boss } }) throw new FormatException("Resize works on a round hole or boss.");
            double dia = 2 * (round.Radius + _radius.Value);
            if (dia <= 0) throw new FormatException("That would make the diameter zero or less.");
            var fe = new FeatureEditCommand { Factory = () => new FeatureEditCommand() }; fe.Begin(S);
            fe.Preset(_faces.Items, 1);
            fe.ConfigureInputs(() => { foreach (var p in fe.Params.OfType<NumberParam>()) if (p.Label.StartsWith("New diameter")) p.Value = dia; });
            result = fe.TryBuild() ?? throw new FormatException(fe.Error ?? "Nothing to change.");
            InnerRecipe = fe.Capture();
        }
        else if (pulling) result = Run(new OffsetFaceCommand { Factory = () => new OffsetFaceCommand() }, null, ("Offset distance", _offset.Value));
        else if (tilting) result = Run(new RotateFaceCommand { Factory = () => new RotateFaceCommand() }, null, ("Rotation angle", _rotate.Value));
        else if (PickedFeature(out bool all))
        {
            if (!all) throw new FormatException("Pick either whole features or plain faces to move, not both at once.");
            var fe = new FeatureEditCommand { Factory = () => new FeatureEditCommand() }; fe.Begin(S);
            fe.Preset(_faces.Items, 0);
            fe.ConfigureInputs(() =>
            {
                foreach (var p in fe.Params.OfType<NumberParam>())
                    if (p.Label == "Move X") p.Value = move.X; else if (p.Label == "Move Y") p.Value = move.Y; else if (p.Label == "Move Z") p.Value = move.Z;
            });
            result = fe.TryBuild() ?? throw new FormatException(fe.Error ?? "Nothing to change.");
            InnerRecipe = fe.Capture();
        }
        else result = Run(new MoveFaceCommand { Factory = () => new MoveFaceCommand() }, null, ("Move X", move.X), ("Move Y", move.Y), ("Move Z", move.Z));
        result.Label = sizing ? "Resize feature" : pulling ? "Pull face" : tilting ? "Tilt face" : "Move";
        result.Icon = "pushpull";
        return result;
    }

    // ------------------------------------------------------------ the Delete key
    /// <summary>Remove the picked feature (or plain faces) and let the neighbours close the gap. One undo step; it never deletes the whole body.</summary>
    public bool DeletePicked()
    {
        if (S == null) return false;
        if (_faces.Items.Count == 0) { S.Say("Click a face, hole, slot, pocket, boss or fillet first, then press Delete."); return true; }
        var del = new DeleteFaceCommand(); del.Begin(S);
        del.ConfigureInputs(() => { foreach (var it in _faces.Items) del.Slots[0].TryAdd(it); });
        var output = del.TryBuild();
        if (output == null) { S.Say(del.Error ?? "Those faces cannot be removed on their own.", true); return true; }
        output.Label ??= "Delete feature"; output.Icon ??= "deleteface";
        _faces.Items.Clear();
        ConfigureInputs(() => { });
        S.Apply(output);
        S.Say("Deleted the selected feature");
        return true;
    }
}
