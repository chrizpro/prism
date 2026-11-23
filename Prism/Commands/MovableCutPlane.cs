using System;
using Prism.Core;
using Prism.Kernel;

namespace Prism.Commands;

/// <summary>
/// The optional cutting plane of Split Body and Split Face (owner request 2026-11-23): a checkbox shows a plane through the picked body or face that the
/// shared gizmo moves and turns in the viewport, so a cut needs no plane drawn first. Its position is in world coordinates, so Apply keeps it where it is
/// for the next body or face; it starts at the centre of the first one picked.
/// </summary>
public sealed class MovableCutPlane
{
    public const string Section = "Cutting plane";
    public BoolParam On { get; }
    readonly NumberParam _x, _y, _z, _rx, _ry, _rz;

    public MovableCutPlane(BoolParam on, NumberParam x, NumberParam y, NumberParam z, NumberParam rx, NumberParam ry, NumberParam rz)
    {
        On = on; _x = x; _y = y; _z = z; _rx = rx; _ry = ry; _rz = rz;
        On.HelpText = "Show a plane you move and turn with the gizmo in the viewport, and cut with it instead of a picked tool. Apply keeps the plane where it is for the next cut.";
        foreach (var p in new[] { _x, _y, _z, _rx, _ry, _rz }) p.Section = Section;
    }

    /// <summary>Show the position and turn numbers only while the plane is in use.</summary>
    public void UpdateVisibility() { foreach (var p in new[] { _x, _y, _z, _rx, _ry, _rz }) p.IsVisible = On.Value; }

    /// <summary>Put the plane through <paramref name="centre"/> the first time something is picked (a plane that was moved, or kept by Apply, stays where it is).</summary>
    public void PlaceAt(Vec3 centre)
    {
        if (Math.Abs(_x.Value) + Math.Abs(_y.Value) + Math.Abs(_z.Value) > 1e-9) return;
        _x.Value = centre.X; _y.Value = centre.Y; _z.Value = centre.Z;
    }

    Vec3 Turn(Vec3 axis) => DirectHelpers.RotateAbout(DirectHelpers.RotateAbout(DirectHelpers.RotateAbout(axis, Vec3.UnitX, _rx.Value), Vec3.UnitY, _ry.Value), Vec3.UnitZ, _rz.Value);

    /// <summary>The plane: horizontal (XY) at no turn, through the gizmo's position.</summary>
    public Plane3 Frame => new(new Vec3(_x.Value, _y.Value, _z.Value), Turn(Vec3.UnitX), Turn(Vec3.UnitY));

    public GizmoSpec Gizmo => GizmoSpec.At(Frame.Origin, _x, _y, _z, _rx, _ry, _rz) with { CombinedByDefault = true };

    /// <summary>A square face on the plane, <paramref name="half"/> from its centre to each side: big enough to cross whatever it cuts.</summary>
    public Shape Square(double half)
    {
        var pl = Frame;
        return Occ.Profile([Occ.Polyline([pl.PointAt(-half, -half), pl.PointAt(half, -half), pl.PointAt(half, half), pl.PointAt(-half, half)], true)], true);
    }

    /// <summary>The plane drawn as a see-through square with an outline.</summary>
    public void Draw(Overlay o, double half)
    {
        var pl = Frame;
        Vec3 a = pl.PointAt(-half, -half), b = pl.PointAt(half, -half), c = pl.PointAt(half, half), d = pl.PointAt(-half, half);
        o.AddTri(a, b, c, 0x1835B6FF);
        o.AddTri(a, c, d, 0x1835B6FF);
        o.AddPolyline([a, b, c, d, a], 0xC035B6FF, 1.5f, false);
    }
}
