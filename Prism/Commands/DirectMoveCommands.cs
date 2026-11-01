using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core;
using Prism.Core.Assembly;
using Prism.Kernel;

namespace Prism.Commands;

// =================================================================== Move Body
/// <summary>
/// Move or turn a body while keeping one relationship to another body: a face flush with a face, a round hole or pin coaxial with another, or a round face tangent to a flat one.
/// The command first snaps the body into the relationship (turning it square and sliding it into place), then limits the typed move and turn to what keeps it.
/// </summary>
public sealed class MoveBodyCommand : Command
{
    readonly SelectionSlot _mine, _other;
    readonly ChoiceParam _rel, _flushMode;
    readonly NumberParam _dx, _dy, _dz, _turn;
    readonly BoolParam _snap;

    public MoveBodyCommand()
    {
        _mine = Slot(new SelectionSlot { Label = "Face on the body to move", Filter = SelFilter.Face, MaxCount = 1 });
        _other = Slot(new SelectionSlot { Label = "Face on the other body to keep to", Filter = SelFilter.Face, MaxCount = 1 });
        _rel = Choice("Keep", 0, "Faces flush", "Holes / pins coaxial", "Tangent (round face on a flat one)");
        _flushMode = Choice("Flush means", 0, "Touching (faces meet)", "Aligned (same plane, same direction)");
        _snap = Check("Snap into the relationship first", true);
        _dx = Num("Move X", 0); _dy = Num("Move Y", 0); _dz = Num("Move Z", 0);
        _turn = Num("Turn about the kept axis", 0, -360, 360, 1, "°");
    }

    public override string Title => "Move Body";
    public override string Icon => "move";
    public override string OkText => "Move";
    public override string Hint => "Pick a face on the body to move, then a face on another body, and choose what to keep. Faces flush: the faces end up in one plane, and the move can only slide in that plane. Holes / pins coaxial: the two round faces share an axis, and the move can only run along it. Tangent: a cylinder rests on a flat face (or a flat face on a cylinder) and slides along it. The body first snaps into the relationship; the X / Y / Z you type are then cut down to what keeps it, and the turn is about the kept axis. Sphere, cylinder-on-cylinder and cone tangents are not supported.";

    public override IReadOnlyList<HandleSpec> Handles => [];

    protected override void UpdateVisibility() { _flushMode.IsVisible = _rel.Index == 0; }

    record struct Op(Vec3 Origin, Vec3 Axis, double Deg, Vec3 Shift);

    protected override CommandOutput? Build()
    {
        Results.Clear();
        if (_mine.Items.Count == 0 || _other.Items.Count == 0) return null;
        var mine = _mine.Items[0]; var other = _other.Items[0];
        if (mine.Obj == other.Obj) throw new FormatException("Pick the second face on a different body.");
        var body = S.Find(mine.Obj); if (body == null || !body.HasBrep || body.Kind is not (ObjKind.Solid or ObjKind.Surface)) throw new FormatException("Move Body works on solid and surface bodies.");
        var ops = new List<Op>();
        var (limit, axisPoint, axisDir, isLine, note) = Solve(mine, other, ops);

        var move = new Vec3(_dx.Value, _dy.Value, _dz.Value);
        var kept = isLine ? limit * Vec3.Dot(move, limit) : move - limit * Vec3.Dot(move, limit);
        if (_turn.Value != 0) ops.Add(new Op(axisPoint, axisDir, _turn.Value, Vec3.Zero));
        if (kept.Length > 1e-12) ops.Add(new Op(axisPoint, axisDir, 0, kept));
        ops.RemoveAll(p => Math.Abs(p.Deg) < 1e-9 && p.Shift.Length < 1e-9);
        if (ops.Count == 0) { Results.Add(new ResultLine("Relationship", note + ": already kept", 1)); return null; }

        var shape = body.Shape;
        foreach (var p in ops)
        {
            if (Math.Abs(p.Deg) > 1e-9) shape = Occ.Rotate(shape, p.Origin, p.Axis, p.Deg);
            if (p.Shift.Length > 1e-9) shape = Occ.Translate(shape, p.Shift);
        }
        Results.Add(new ResultLine("Relationship kept", note, 1));
        if ((move - kept).Length > 1e-9) Results.Add(new ResultLine("Typed move limited", $"{move.Length:0.###} mm asked, {kept.Length:0.###} mm kept ({(isLine ? "along the axis only" : "in the plane only")})", 2));
        var o = new CommandOutput { Label = "Move body", Icon = "move" };
        o.Replace.Add((body, shape));
        return o;
    }

    /// <summary>
    /// Work out the snap (rotations and shifts that satisfy the relationship) and the limit on later motion: a plane normal (isLine false: motion stays in that plane)
    /// or a line direction (isLine true: motion runs along it). Also returns the point the kept axis passes through after the snap.
    /// </summary>
    (Vec3 limit, Vec3 point, Vec3 axis, bool isLine, string note) Solve(SelItem mine, SelItem other, List<Op> ops)
    {
        var fa = S.ShapeOf(mine) ?? throw new FormatException("That face is gone.");
        var fb = S.ShapeOf(other) ?? throw new FormatException("That face is gone.");
        var ia = Occ.FaceInfoOf(fa); var ib = Occ.FaceInfoOf(fb);
        bool snap = _snap.Value;

        Vec3 PlanePoint(SelItem it, Shape f) => S.FaceFrame(it)?.point ?? throw new FormatException("Could not read that face.");
        (Vec3 o, Vec3 d) Axis(Shape f) => Occ.AxisOf(f) ?? throw new FormatException("That round face has no axis.");

        void Turn(Vec3 from, Vec3 to, Vec3 pivot)
        {
            if (!snap) return;
            var c = Vec3.Cross(from, to); double s = c.Length, d = Vec3.Dot(from, to);
            if (s < 1e-9) { if (d < 0) ops.Add(new Op(pivot, Xform.AnyPerpendicular(from), 180, Vec3.Zero)); return; }
            ops.Add(new Op(pivot, c * (1 / s), Math.Atan2(s, d) * 180 / Math.PI, Vec3.Zero));
        }
        void Shift(Vec3 v) { if (snap && v.Length > 1e-9) ops.Add(new Op(Vec3.Zero, Vec3.UnitZ, 0, v)); }

        switch (_rel.Index)
        {
            case 0:     // flush
            {
                if (ia.Type != "Plane" || ib.Type != "Plane") throw new FormatException("Faces flush needs two flat faces.");
                var n = ia.Normal.Normalized(); var m = ib.Normal.Normalized(); var P = PlanePoint(mine, fa); var Q = PlanePoint(other, fb);
                Turn(n, _flushMode.Index == 0 ? -m : m, P);
                Shift(m * Vec3.Dot(Q - P, m));
                return (m, snap ? P + m * Vec3.Dot(Q - P, m) : P, m, false, $"faces flush ({(_flushMode.Index == 0 ? "touching" : "aligned")})");
            }
            case 1:     // coaxial
            {
                if (ia.Type != "Cylinder" || ib.Type != "Cylinder") throw new FormatException("Holes / pins coaxial needs two cylindrical (round) faces.");
                var (A, a) = Axis(fa); var (B, b) = Axis(fb); a = a.Normalized(); b = b.Normalized();
                Turn(a, Vec3.Dot(a, b) >= 0 ? b : -b, A);
                var off = B - A; off -= b * Vec3.Dot(off, b);
                Shift(off);
                return (b, snap ? A + off : A, b, true, "holes / pins coaxial");
            }
            default:    // tangent
            {
                bool cylMine = ia.Type == "Cylinder" && ib.Type == "Plane";
                if (!cylMine && !(ia.Type == "Plane" && ib.Type == "Cylinder")) throw new FormatException("Tangent works between a cylinder and a flat face. Cylinder on cylinder, spheres and cones are not supported.");
                if (cylMine)
                {
                    var (A, a) = Axis(fa); a = a.Normalized(); var m = ib.Normal.Normalized(); var Q = PlanePoint(other, fb); double r = ia.R1;
                    var flat = a - m * Vec3.Dot(a, m); flat = flat.Length < 1e-9 ? Xform.AnyPerpendicular(m) : flat.Normalized();
                    Turn(a, flat, A);
                    double d = r - Vec3.Dot(A - Q, m);
                    Shift(m * d);
                    return (m, snap ? A + m * d : A, m, false, $"cylinder tangent to the flat face (radius {r:0.###} mm)");
                }
                else
                {
                    var n = ia.Normal.Normalized(); var P = PlanePoint(mine, fa); var (B, b) = Axis(fb); b = b.Normalized(); double r = ib.R1;
                    var nt = n - b * Vec3.Dot(n, b); nt = nt.Length < 1e-9 ? Xform.AnyPerpendicular(b) : nt.Normalized();
                    Turn(n, nt, P);
                    if (snap) n = nt;
                    double d = Vec3.Dot(B - P, n) - r;
                    Shift(n * d);
                    return (n, snap ? P + n * d : P, n, false, $"flat face tangent to the cylinder (radius {r:0.###} mm)");
                }
            }
        }
    }
}
