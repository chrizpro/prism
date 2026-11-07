using System;
using Prism.Core.Sketching;
using Prism.Kernel;

namespace Prism.Core;

/// <summary>
/// Standalone datum placement outside sketch mode. Each click drops a Point, Axis or Plane
/// datum on the picked geometry — faces, edges, other datums — or on the work plane where
/// nothing was hit, so reference geometry can be prepared anywhere in the viewport. The tool
/// stays open so datums can be placed repeatedly; Enter places one at the pointer, and Esc or a right click finishes.
/// Unlike <see cref="Prism.Commands.DatumGizmoTool"/> the placements are real document datums:
    /// they are drawn, listed, picked and saved like sketch datums.
/// </summary>
public sealed class DatumPickTool : IViewportTool
{
    public DatumPickTool(SketchDatumKind kind) => Kind = kind;
    public SketchDatumKind Kind { get; }

    public string Prompt => Kind switch
    {
        SketchDatumKind.Point => "Point: click a face, edge or empty space to place a datum point — keep clicking to add more.",
        SketchDatumKind.Axis => "Axis: click an edge (or a face for its normal); empty space uses the work-plane normal.",
        SketchDatumKind.Gizmo => "Gizmo: click to place a reference frame — on a face to align with it, or on empty space.",
        _ => "Plane: click a face or another plane to match it; empty space uses the work plane.",
    } + "  Enter places one at the pointer. Esc or right-click finishes.";

    /// <summary>Orientation override chosen in the Datum panel before clicking; 0 keeps what the pick gives.</summary>
    public int Orientation { get; set; }
    /// <summary>Extra rotation about the world axes (degrees), applied after <see cref="Orientation"/>.</summary>
    public Vec3 Rotation { get; set; }
    public string[] OrientationChoices => Kind == SketchDatumKind.Axis
        ? ["From the pick", "Along world X", "Along world Y", "Along world Z"]
        : ["From the pick", "XY (top)", "XZ (front)", "YZ (side)"];

    public bool PointerDown(CadSession s, PointerInfo p)
    {
        if (!p.Left) { s.SetFreeTool(null); return true; }
        Place(s, p);
        return true;
    }

    void Place(CadSession s, PointerInfo p)
    {
        var hit = s.Pick(p.X, p.Y, SelFilter.Any);
        var datum = Orient(Create(s, p, hit));
        s.AddModelDatum(datum);
        s.Say($"{datum.Kind} datum '{datum.Name}' placed — keep clicking, Esc or right-click when done.");
        if (s.LastModelDatum() is { } placed)
        {
            s.SetSelection(placed, false);      // binds the gizmo to it: rotate handles for an axis or plane, move handles for a gizmo
        }
    }

    SketchDatum Orient(SketchDatum d)
    {
        if (Kind == SketchDatumKind.Point) return d;
        var f = d.Frame;
        if (Orientation > 0)
        {
            if (Kind == SketchDatumKind.Axis)
            {
                var dir = Orientation == 1 ? Vec3.UnitX : Orientation == 2 ? Vec3.UnitY : Vec3.UnitZ;
                f = new(f.Origin, dir, Perp(dir));
            }
            else
            {
                var (u, v) = Orientation == 1 ? (Vec3.UnitX, Vec3.UnitY) : Orientation == 2 ? (Vec3.UnitX, Vec3.UnitZ) : (Vec3.UnitY, Vec3.UnitZ);
                f = new(f.Origin, u, v);
            }
        }
        if (Rotation.X != 0 || Rotation.Y != 0 || Rotation.Z != 0)
        {
            Vec3 R(Vec3 v) => Spin(Spin(Spin(v, Vec3.UnitX, Rotation.X), Vec3.UnitY, Rotation.Y), Vec3.UnitZ, Rotation.Z);
            f = new(f.Origin, R(f.U), R(f.V));
        }
        d.Frame = f;
        return d;
    }

    static Vec3 Spin(Vec3 v, Vec3 axis, double degrees)
    {
        double a = degrees * Math.PI / 180, c = Math.Cos(a), sn = Math.Sin(a);
        return v * c + Vec3.Cross(axis, v) * sn + axis * (Vec3.Dot(axis, v) * (1 - c));
    }

    PointerInfo? _last;
    public void PointerMove(CadSession s, PointerInfo p) => _last = p;
    public void PointerUp(CadSession s, PointerInfo p) { }

    public bool Key(CadSession s, string key)
    {
        if (key is not ("Escape" or "Enter")) return false;
        if (key == "Enter" && _last is { } at) { Place(s, at); return true; }
        s.SetFreeTool(null);
        return true;
    }

    public void BuildOverlay(CadSession s, Overlay o) { }
    public void Deactivate(CadSession s) { }

    SketchDatum Create(CadSession s, PointerInfo p, PickResult? hit) => Kind switch
    {
        SketchDatumKind.Point => new() { Kind = SketchDatumKind.Point, Frame = new(PointAt(s, p, hit), Vec3.UnitX, Vec3.UnitY) },
        SketchDatumKind.Axis => AxisDatum(s, p, hit),
        SketchDatumKind.Gizmo => GizmoDatum(s, p, hit),
        _ => PlaneDatum(s, p, hit),
    };

    static SketchDatum GizmoDatum(CadSession s, PointerInfo p, PickResult? hit)
    {
        var d = PlaneDatum(s, p, hit);      // a gizmo is a frame: it aligns the way a plane would
        d.Kind = SketchDatumKind.Gizmo;
        return d;
    }

    /// <summary>
    /// Where an un-picked click lands: the grid the user sees under the cursor. In a Front or Side view that is the plane
    /// being looked at; the ground plane is edge-on there, so its ray never meets it.
    /// </summary>
    static Vec3 PointAt(CadSession s, PointerInfo p, PickResult? hit)
    {
        // Origin-axis picks report the world origin (they exist for selecting the axis, not for a position),
        // so a click near a drawn axis must not teleport the datum to (0,0,0) — land it on the grid.
        if (hit is { } h && h.Item is not { Kind: SelKind.Origin, Index: >= 3 }) return h.Point;
        var (origin, dir) = s.Camera.Ray(p.X, p.Y);
        return s.DisplayGridPlane().Intersect(origin, dir) ?? s.WorkPlane.Intersect(origin, dir) ?? s.ViewportCursor ?? s.WorkPlane.Origin;
    }

    static Vec3 Perp(Vec3 direction)
    {
        var side = Math.Abs(direction.Z) < .9 ? Vec3.Cross(Vec3.UnitZ, direction) : Vec3.Cross(Vec3.UnitY, direction);
        return side.Length < 1e-9 ? Vec3.UnitX : side.Normalized();
    }

    static SketchDatum AxisDatum(CadSession s, PointerInfo p, PickResult? hit)
    {
        Vec3 origin, dir;
        var item = hit?.Item;
        if (item is { Kind: SelKind.Datum } it && s.DatumOf(it) is { } d)
            (origin, dir) = d.Kind == SketchDatumKind.Axis ? (d.Frame.Origin, d.Frame.U) : (d.Frame.Origin, d.Frame.Normal);
        else if (item is { Kind: SelKind.Edge } e && s.EdgePoints(e) is { Count: >= 2 } pts && (pts[^1] - pts[0]) is { Length: > 1e-9 } along)
            (origin, dir) = (pts[0], along.Normalized());
        else if (item is { Kind: SelKind.Origin, Index: >= 3 } oa)
            (origin, dir) = (PointAt(s, p, null), CadSession.OriginAxis(oa.Index).dir);
        else if (item is { Kind: SelKind.Object } ob && s.Find(ob.Obj) is { Kind: ObjKind.Curve } curve
            && curve.Mesh.EdgePoints is { Length: >= 6 } cp
            && (new Vec3(cp[^3], cp[^2], cp[^1]) - new Vec3(cp[0], cp[1], cp[2])) is { Length: > 1e-9 } calong)
            (origin, dir) = (hit.GetValueOrDefault().Point, calong.Normalized());   // a whole curve picked as an object still gives its chord direction
        else if (hit is { Normal.Length: > 1e-9 } h)
            (origin, dir) = (h.Point, h.Normal.Normalized());
        else (origin, dir) = (PointAt(s, p, null), s.DisplayGridPlane().Normal);
        return new() { Kind = SketchDatumKind.Axis, Frame = new(origin, dir, Perp(dir)) };
    }

    static SketchDatum PlaneDatum(CadSession s, PointerInfo p, PickResult? hit)
    {
        var item = hit?.Item;
        if (item is { Kind: SelKind.Datum } it && s.DatumOf(it) is { Kind: SketchDatumKind.Plane or SketchDatumKind.Gizmo } d)
            return new() { Kind = SketchDatumKind.Plane, Frame = new(hit.GetValueOrDefault().Point, d.Frame.U, d.Frame.V) };
        if (item is { Kind: SelKind.Origin, Index: < 3 } op)
        {
            var pl = CadSession.OriginPlane(op.Index);
            return new() { Kind = SketchDatumKind.Plane, Frame = new(PointAt(s, p, hit), pl.U, pl.V) };
        }
        if (item is { } pi && s.Find(pi.Obj)?.SketchPlane is { } cp)
            return new() { Kind = SketchDatumKind.Plane, Frame = new(hit.GetValueOrDefault().Point, cp.U, cp.V) };
        if (hit is { Normal.Length: > 1e-9 } h)
        {
            var n = h.Normal.Normalized(); var u = Perp(n);
            return new() { Kind = SketchDatumKind.Plane, Frame = new(h.Point, u, Vec3.Cross(n, u).Normalized()) };
        }
        var grid = s.DisplayGridPlane();
        return new() { Kind = SketchDatumKind.Plane, Frame = new(PointAt(s, p, null), grid.U, grid.V) };
    }
}
