using System;
using Prism.Core;
using Prism.Kernel;

namespace Prism.Commands;

/// <summary>Chooses the plane a new sketch lives on, then switches the app into sketch mode.</summary>
public class CreateSketchCommand : Command
{
    readonly ChoiceParam _plane;
    readonly NumberParam _offset;
    readonly SelectionSlot _face;
    readonly BoolParam _lookNormal;
    readonly bool _spatial;
    public CreateSketchCommand(bool spatial = false)
    {
        _spatial = spatial;
        _face = Slot(new SelectionSlot { Label = "Plane or flat face (optional)", Filter = SelFilter.Face | SelFilter.Plane, Optional = true });
        _plane = Choice("Base plane", 0, "XY (top)", "XZ (front)", "YZ (side)");
        _offset = Num("Offset", 0);
        Add(new ActionParam { Label = "New construction plane…", Run = () => S.SuspendFor(() => new ConstructionPlaneCommand()), HelpText = "Open the construction plane options. When you finish or cancel there, you come back here with the new plane already picked." });
        _lookNormal = Check("Look normal when starting", true);
    }
    public override string Title => "Create Sketch";
    public override string Hint => "Choose a plane or flat face to start. Draw with constraints on any plane; G moves and rotates the drawing gizmo, E edits curves, and N looks normal. Add drawing planes in the sketch panel to continue in 3D.";
    public override string OkText => "Start sketch";
    public override bool CommitOnPick => true;

    public override Plane3? WorkPlane => ResolvePlane();

    protected override void OnBegin()
    {
        // Orthographic is left alone: the user may already be looking at the plane they want.
        // A perspective view that is nearly straight-on gets a tilt so the planes are easy to pick.
        if (S.Ortho) return;
        var cam = S.Camera;
        double q = Math.PI / 2;
        bool nearAxis = Math.Abs(Math.Round(cam.Yaw / q) * q - cam.Yaw) < 0.09 && (Math.Abs(cam.Pitch) < 0.09 || Math.Abs(Math.Round(cam.Pitch / q) * q - cam.Pitch) < 0.09);
        if (nearAxis) S.AnimateView(-35 * Math.PI / 180, 28 * Math.PI / 180);
    }

    /// <summary>The grid stays on the plane before the offset, so raising the offset does not make the grid cells swell towards the camera.</summary>
    public override Plane3? GridPlane
    {
        get
        {
            var p = ResolveBase();
            return new Plane3(p.Origin, p.U, p.V);
        }
    }

    Plane3 ResolveBase()
    {
        if (_face.Items.Count > 0 && S.PlaneOf(_face.Items[0]) is { } picked) return picked;
        return _plane.Index == 0 ? Plane3.XY : _plane.Index == 1 ? Plane3.XZ : Plane3.YZ;
    }

    /// <summary>The plane under the pointer (an origin or construction plane) with the offset applied, as a preview of where the sketch would start; else the plane the options name.</summary>
    public override void BuildOverlay(CadSession s, Overlay o)
    {
        if (_face.Items.Count > 0 && _face.Items[0].Kind == SelKind.Face) return;      // a flat face is used exactly: nothing to preview
        Plane3 basePlane; double hu, hv; string name;
        if (s.Hover is { Kind: SelKind.Origin or SelKind.Object } hov && hov.Kind == SelKind.Origin ? hov.Index < 3 : s.Hover is { Kind: SelKind.Object } && s.Find(s.Hover!.Value.Obj) is { Kind: ObjKind.Plane })
        {
            var item = s.Hover!.Value;
            if (s.PlaneOf(item) is not { } hp) return;
            basePlane = hp;
            double scale = Prism.Core.AppSettings.Current.PlaneScale;
            if (item.Kind == SelKind.Origin) { hu = hv = s.OriginSize() * scale; name = new[] { "XY", "XZ", "YZ" }[item.Index]; }
            else { var obj = s.Find(item.Obj)!; (hu, hv) = CadSession.PlaneHalfSize(obj); hu *= scale; hv *= scale; name = obj.Name; }
        }
        else
        {
            basePlane = ResolveBase(); hu = hv = s.OriginSize() * Prism.Core.AppSettings.Current.PlaneScale;
            name = _face.Items.Count > 0 ? "plane" : new[] { "XY", "XZ", "YZ" }[_plane.Index];
        }
        var pl = new Plane3(basePlane.Origin + basePlane.Normal * _offset.Value, basePlane.U, basePlane.V);
        Vec3 P(double u, double v) => pl.PointAt(u * hu, v * hv);
        const uint tint = 0xFFC53D;
        uint fill = (0x40u << 24) | tint;
        o.AddTri(P(-1, -1), P(1, -1), P(1, 1), fill); o.AddTri(P(-1, -1), P(1, 1), P(-1, 1), fill);
        o.AddPolyline([P(-1, -1), P(1, -1), P(1, 1), P(-1, 1)], (0xFFu << 24) | tint, 2.4f, true);
        o.AddLabel(P(0.9, 0.9), _offset.Value == 0 ? name : $"{name} + {_offset.Value:0.##} mm", true);
    }

    Plane3 ResolvePlane()
    {
        if (_face.Items.Count > 0 && S.PlaneOf(_face.Items[0]) is { } picked)
        {
            // origin planes and construction planes can still be nudged by the offset; a flat face is used exactly
            return _face.Items[0].Kind == SelKind.Face ? picked : new Plane3(picked.Origin + picked.Normal * _offset.Value, picked.U, picked.V);
        }
        var p = _plane.Index == 0 ? Plane3.XY : _plane.Index == 1 ? Plane3.XZ : Plane3.YZ;
        // XZ: +Y is behind the screen when looking from the front, so flip the offset direction to feel natural.
        return new Plane3(p.Origin + p.Normal * _offset.Value, p.U, p.V);
    }

    protected override void UpdateVisibility()
    {
        bool onFace = _face.Items.Count > 0 && _face.Items[0].Kind == SelKind.Face;
        bool onOrigin = _face.Items.Count > 0 && _face.Items[0].Kind != SelKind.Face;
        _plane.IsVisible = !onFace && !onOrigin;
        _offset.IsVisible = !onFace;
        _lookNormal.IsVisible = !_spatial;     // a 3D sketch stays in perspective instead of turning to face its plane
    }

    protected override CommandOutput? Build()
    {
        var plane = ResolvePlane();
        var o = new CommandOutput { NoOp = true, After = s => { s.EnterSketch(plane, alignView: _lookNormal.Value); if (_spatial) s.Sketch?.SetDrawingMode(s, flat: true, adjustView: false); } };   // plain Create Sketch starts as a 2D sketch
        return o;
    }
}
