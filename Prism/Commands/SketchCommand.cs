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
    public CreateSketchCommand(bool spatial = false)
    {
        _face = Slot(new SelectionSlot { Label = "Plane or flat face (optional)", Filter = SelFilter.Face | SelFilter.Plane, Optional = true });
        _plane = Choice("Base plane", 0, "XY (top)", "XZ (front)", "YZ (side)");
        _offset = Num("Offset", 0);
        _lookNormal = Check("Look normal when starting", false);
    }
    public override string Title => "Create Sketch";
    public override string Hint => "Choose a plane or flat face to start. Draw with constraints on any plane; G moves and rotates the drawing gizmo, E edits curves, and N looks normal. Add drawing planes in the sketch panel to continue in 3D.";
    public override string OkText => "Start sketch";
    public override bool CommitOnPick => true;

    public override Plane3? WorkPlane => ResolvePlane();

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
    }

    protected override CommandOutput? Build()
    {
        var plane = ResolvePlane();
        var o = new CommandOutput { After = s => s.EnterSketch(plane, alignView: _lookNormal.Value) };
        return o;
    }
}
