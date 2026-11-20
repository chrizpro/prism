using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core;
using Prism.Kernel;

namespace Prism.Commands;

/// <summary>
/// Edit by dimension, as in KeyCreator: pick what to move (a flat face, or any face of a hole, slot, pocket or boss, which picks the whole
/// feature) and what to measure from (a flat face or a feature), read the distance between them, type a new one, and the picked side moves by the
/// difference along the measuring direction. Flat faces slide and the body stretches to follow; features move as a whole. Symmetric moves both
/// sides by half the change. Parallel flat faces measure square to the faces, a feature measures from its centre line.
/// </summary>
public sealed class EditDistanceCommand : Command
{
    readonly SelectionSlot _move, _from;
    readonly NumberParam _distance;
    readonly BoolParam _symmetric;
    string _seeded = "";

    public EditDistanceCommand()
    {
        _move = Slot(new SelectionSlot { Label = "Move (a flat face, or a hole, slot, pocket or boss)", Filter = SelFilter.Face, Multi = true, SmartPick = true });
        _from = Slot(new SelectionSlot { Label = "Measured from (a flat face or a feature)", Filter = SelFilter.Face, Multi = true, SmartPick = true });
        _distance = Num("Distance", 0, 0, 1e6, 1);
        _symmetric = Check("Symmetric (move both sides)", false);
    }

    public override string Title => "Edit Distance";
    public override string Icon => "measure";
    public override string OkText => "Apply";
    public override string Hint => "Click what to move (a flat face, or a hole, slot, pocket or boss: one click picks the whole feature), then what to measure from. The panel shows the distance between them; type a new one, or drag the arrow, and the first pick moves to match. Symmetric moves both picks by half the change.";

    /// <summary>One side of the dimension: a flat face (point and normal) or a feature (its centre and axis direction).</summary>
    sealed record Side(SceneObject Body, Feat? Feature, int Face, Vec3 Point, Vec3 Dir, bool IsPlane);

    Side? Read(SelectionSlot slot)
    {
        if (S == null || slot.Items.Count == 0 || DirectHelpers.BodyOf(S, slot.Items[0]) is not { } body) return null;
        int face = slot.Items[0].Index;
        var feat = FeatureFinder.FeatureOf(body, face);
        if (feat is { Kind: FeatureKind.Fillet or FeatureKind.Chamfer })
            throw new FormatException("Pick a flat face or a hole, slot, pocket or boss. A fillet or chamfer follows the faces it joins.");
        if (feat is { Kind: not FeatureKind.Face })
            return new Side(body, feat, face, feat.Round is { } r ? r.Origin + feat.Dir.Normalized() * ((r.T0 + r.T1) / 2) : feat.Centre, feat.Dir.Normalized(), false);
        using var shape = body.Shape.Sub(SubKind.Face, face);
        var info = Occ.FaceInfoOf(shape);
        if (info.Type != "Plane") throw new FormatException("Pick a flat face, or a face of a hole, slot, pocket or boss.");
        var pl = S.PlaneOf(slot.Items[0]) ?? throw new FormatException("Could not read that face.");
        return new Side(body, null, face, pl.Origin, info.Normal.Normalized(), true);
    }

    /// <summary>The measuring direction (unit, pointing from the reference to the moved side) and the current distance.</summary>
    static (Vec3 dir, double distance) Measure(Side move, Side from)
    {
        var gap = move.Point - from.Point;
        if (move.IsPlane && from.IsPlane)
        {
            if (Vec3.Cross(move.Dir, from.Dir).Length > 1e-4) throw new FormatException("Those two faces are not parallel, so there is no single distance between them. Pick parallel faces.");
            var n = from.Dir; double d = Vec3.Dot(gap, n);
            return (d >= 0 ? n : -n, Math.Abs(d));
        }
        if (move.IsPlane != from.IsPlane)
        {
            var (plane, feature) = move.IsPlane ? (move, from) : (from, move);
            if (Math.Abs(Vec3.Dot(plane.Dir, feature.Dir)) > 1e-4)
                throw new FormatException("A feature is measured from a face that runs along it (the face's normal square to the feature's axis), such as an edge of the plate a hole goes through.");
            var n = plane.Dir; double d = Vec3.Dot(gap, n);
            return (d >= 0 ? n : -n, Math.Abs(d));
        }
        if (Vec3.Cross(move.Dir, from.Dir).Length > 1e-4) throw new FormatException("Those two features do not run the same way, so there is no single distance between their centre lines.");
        var across = gap - from.Dir * Vec3.Dot(gap, from.Dir);
        if (across.Length < 1e-9) throw new FormatException("Those two features share a centre line, so they are 0 apart. Pick something beside the feature to measure from.");
        return (across.Normalized(), across.Length);
    }

    (Side move, Side from, Vec3 dir, double current)? Current()
    {
        if (Read(_move) is not { } move || Read(_from) is not { } from) return null;
        if (move.Body.Id == from.Body.Id && (move.Feature != null && move.Feature == from.Feature || move.Feature == null && move.Face == from.Face))
            throw new FormatException("Pick two different things: what to move, and what to measure from.");
        var (dir, d) = Measure(move, from);
        return (move, from, dir, d);
    }

    protected override void UpdateVisibility() => Seed();

    /// <summary>When the pair of picks changes, the distance starts at what it is now, so a typed value always means "what it should be".</summary>
    void Seed()
    {
        string key = string.Join(",", _move.Items.Select(i => $"{i.Obj}:{i.Index}")) + "|" + string.Join(",", _from.Items.Select(i => $"{i.Obj}:{i.Index}"));
        if (key == _seeded || S == null) return;
        _seeded = key;
        try { if (Current() is { } c) ConfigureInputs(() => _distance.Value = Math.Round(c.current, 6)); }
        catch (Exception e) when (e is FormatException or KernelException) { }
    }

    public override IReadOnlyList<HandleSpec> Handles
    {
        get
        {
            try { return Current() is { } c ? [new HandleSpec(c.from.Point, c.dir, _distance, 1)] : []; }
            catch (Exception e) when (e is FormatException or KernelException) { return []; }
        }
    }
    public override HandleSpec? Handle => Handles.Count > 0 ? Handles[0] : null;

    public override void BuildOverlay(CadSession s, Overlay o)
    {
        try
        {
            if (Current() is not { } c) return;
            const uint blue = 0xFF3A8DDE, grey = 0xFF8A94A6;
            var start = c.from.Point; var now = start + c.dir * c.current; var target = start + c.dir * _distance.Value;
            o.AddLine(start, now, grey, 1.5f);
            o.AddLine(start, target, blue, 2.5f);
            o.AddDot(start, blue); o.AddDot(target, blue);
            o.AddLabel((start + target) * 0.5, $"{_distance.Value:0.###} mm", true, 1);
        }
        catch (Exception e) when (e is FormatException or KernelException) { }
    }

    protected override CommandOutput? Build()
    {
        Results.Clear();
        Seed();
        if (Current() is not { } c) return null;
        Results.Add(new ResultLine("Distance now", $"{c.current:0.###} mm"));
        double change = _distance.Value - c.current;
        if (Math.Abs(change) < 1e-9) return null;
        if (_distance.Value < 0) throw new FormatException("A distance cannot be negative.");
        double moveShare = _symmetric.Value ? change / 2 : change;
        var o = new CommandOutput { Label = "Edit distance", Icon = "measure" };
        var shapes = new Dictionary<Guid, (SceneObject body, Shape shape)>();
        Shape Of(SceneObject b) => shapes.TryGetValue(b.Id, out var v) ? v.shape : b.Shape;
        void Moved(Side side, Vec3 by)
        {
            var body = side.Body; var start = Of(body);
            Shape result;
            if (side.Feature is { } f)
            {
                if (start != body.Shape) throw new FormatException("Symmetric moves of two features on the same body: move one feature at a time.");
                var tool = FeatureEditCommand.ToolOf(body, f);
                bool boss = f.Kind == FeatureKind.Boss;
                result = DirectHelpers.Batch(DirectHelpers.Batch(start, boss ? [] : [tool], boss ? [tool] : []), boss ? [Occ.Translate(tool, by)] : [], boss ? [] : [Occ.Translate(tool, by)]);
            }
            else
            {
                using var face = body.Shape.Sub(SubKind.Face, side.Face);
                if (start != body.Shape)
                {
                    // the other side already changed this body: find the same flat face again by its plane
                    using var again = FindPlane(start, side.Point, side.Dir) ?? throw new FormatException("Could not find that face again after the first move.");
                    result = DirectHelpers.MoveFaceToPlane(start, again, side.Dir, side.Point + by, side.Dir);
                }
                else result = DirectHelpers.MoveFaceToPlane(start, face, side.Dir, side.Point + by, side.Dir);
            }
            shapes[body.Id] = (body, result);
        }
        // a feature is moved from the body's original shape, so when one side is a feature and the other a face of the same body, the feature goes first
        if (_symmetric.Value && c.from.Feature != null && c.move.Feature == null && c.from.Body.Id == c.move.Body.Id)
        {
            Moved(c.from, c.dir * -(change / 2));
            Moved(c.move, c.dir * moveShare);
        }
        else
        {
            Moved(c.move, c.dir * moveShare);
            if (_symmetric.Value) Moved(c.from, c.dir * -(change / 2));
        }
        foreach (var (body, shape) in shapes.Values) o.Replace.Add((body, shape));
        Results.Add(new ResultLine("Moved by", _symmetric.Value ? $"{Math.Abs(change) / 2:0.###} mm each" : $"{Math.Abs(change):0.###} mm"));
        return o;
    }

    static Shape? FindPlane(Shape body, Vec3 point, Vec3 normal)
    {
        for (int i = 0; i < body.Count(SubKind.Face); i++)
        {
            var face = body.Sub(SubKind.Face, i); var info = Occ.FaceInfoOf(face);
            if (info.Type == "Plane" && Vec3.Cross(info.Normal.Normalized(), normal).Length < 1e-6 && Math.Abs(Vec3.Dot(DirectHelpers.PointOn(face) - point, normal)) < 1e-6) return face;
            face.Dispose();
        }
        return null;
    }
}
