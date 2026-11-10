using System;
using System.Linq;
using Prism.Core;
using Prism.Core.Cam;
using Prism.Kernel;

namespace Prism.Commands;

/// <summary>Finish top-accessible walls by following a fresh horizontal section at each height.</summary>
public sealed class ConstantZFinishCommand : CncOperationCommand
{
    // Offset the centerline slightly farther out to account for linearizing the offset curves.
    const double Margin = 0.05, ChordTolerance = 0.01;
    const int MaxLevels = 200, MaxSegments = 30_000;
    readonly SelectionSlot _model;
    readonly NumberParam _top, _bottom;

    public ConstantZFinishCommand()
    {
        _model = Slot(new SelectionSlot { Label = "Model to finish", Filter = SelFilter.Solid });
        _top = Num("Finish top Z", 0, -1e5, 1e5, .25);
        _bottom = Num("Finish bottom Z", 0, -1e5, 1e5, .25);
        StepDown.Value = .5;
        Depth.IsVisible = false;
    }

    public override string Title => "CNC Constant-Z Finish";
    public override string Hint => "Finish steep walls with a flat end mill after roughing. Set the Z range and depth per pass. The tool follows a new horizontal contour at each height and retracts between contours. Review stock, entry moves and fixture clearance before posting.";

    public override void BuildOverlay(CadSession session, Overlay overlay)
    {
        if (PreviewEnabled && Complete && Error == null) base.BuildOverlay(session, overlay);
    }

    protected override void OnBegin()
    {
        base.OnBegin();
        if (S.CamSetup is not { } setup || S.Find(setup.BodyId) is not { } model) return;
        if (_model.Items.Count == 0) _model.Items.Add(SelItem.Of(model));
        var bounds = model.Shape.Props();
        _top.Value = bounds.Max.Z;
        _bottom.Value = bounds.Min.Z + Math.Min(.01, bounds.Size.Z / 4);
    }

    protected override CommandOutput? Build()
    {
        Results.Clear();
        var setup = S.CamSetup ?? throw new InvalidOperationException("Create a CAM setup before finishing.");
        if (!Complete) return null;
        var model = S.Find(_model.Items[0].Obj) ?? throw new InvalidOperationException("Pick the setup solid.");
        if (model.Kind != ObjKind.Solid) throw new InvalidOperationException("Pick the setup solid.");
        setup.Validate(model);
        if (ToolPreset.Index > 0 && CamToolLibrary.Tools[ToolPreset.Index - 1].Ball)
            throw new InvalidOperationException("Constant-Z Finish requires a flat end mill.");
        if (!new[] { Dia.Value, TopZ.Value, Clear.Value, SafeZ, StepDown.Value, _top.Value, _bottom.Value }.All(double.IsFinite)
            || TopZ.Value < setup.StockMax[2] || Clear.Value <= 0)
            throw new InvalidOperationException("Use finite settings and keep the clearance above the setup stock.");
        var bounds = model.Shape.Props();
        double top = _top.Value, bottom = _bottom.Value;
        if (top <= bottom || top > bounds.Max.Z + 1e-6 || bottom <= bounds.Min.Z || bottom < setup.StockMin[2])
            throw new InvalidOperationException("Set a descending finish range inside the model, above its bottom face.");
        double levels = Math.Ceiling((top - bottom) / StepDown.Value);
        if (levels > MaxLevels)
            throw new InvalidOperationException($"Use a smaller finish range or a larger depth per pass (maximum {MaxLevels} levels).");

        // Kernel booleans can update their operands. Keep the saved setup's source shape untouched.
        using var shape = Occ.Copy(model.Shape);
        Start();
        int loops = 0, skipped = 0, segments = 0;
        for (int level = 1; level <= levels; level++)
        {
            double z = level == levels ? bottom : top - level * StepDown.Value;
            using var section = Occ.Section(shape, new Vec3(0, 0, z), Vec3.UnitZ);
            using var profile = Occ.Profile([section]);
            int faces = profile.Count(SubKind.Face);
            if (faces == 0) { skipped++; continue; }

            // A flat cutter must clear the model at every height above its tip, including during entry.
            using var box = Occ.Box(bounds.Size.X + 2, bounds.Size.Y + 2, SafeZ - z);
            using var slab = Occ.Translate(box, new Vec3(bounds.Center.X, bounds.Center.Y, z));
            using var above = Occ.Common(shape, slab);
            if (above.Count(SubKind.Solid) == 0) { skipped++; continue; }

            for (int i = 0; i < faces; i++)
            {
                using var face = profile.Sub(SubKind.Face, i);
                Shape offset;
                try { offset = Occ.OffsetPlanar(face, Radius + Margin); }
                catch (KernelException) { skipped++; continue; }
                using (offset)
                {
                    int wires = offset.Count(SubKind.Wire);
                    if (wires == 0) skipped++;
                    for (int j = 0; j < wires; j++)
                    {
                        using var wire = offset.Sub(SubKind.Wire, j);
                        using var curtain = Occ.Extrude(wire, Vec3.UnitZ * (SafeZ - z));
                        using var intersection = Occ.Common(above, curtain);
                        // Check the exact curve first and reserve clearance for chord error and posting.
                        if (intersection.Count(SubKind.Vertex) > 0
                            || Occ.Distance(above, curtain).Distance < Radius + 2 * ChordTolerance)
                        { skipped++; continue; }

                        var paths = CamGeom.Loops(wire, ChordTolerance);
                        if (paths.Count != 1 || !paths[0].closed || paths[0].pts.Count < 3)
                        { skipped++; continue; }
                        var loop = paths[0].pts;
                        segments += loop.Count;
                        if (segments > MaxSegments)
                            throw new InvalidOperationException("Too many finish segments. Reduce the height range or increase depth per pass.");

                        Pass();
                        CutLoop(loop, z, z, 0, 0, false);
                        Go(Last.x, Last.y, SafeZ, MoveKind.Rapid);
                        loops++;
                    }
                }
            }
        }
        if (loops == 0)
            throw new InvalidOperationException("No closed contour clears the model from above with this tool and height range.");
        Results.Add(new("Finish contours", loops.ToString()));
        if (skipped > 0) Results.Add(new("Skipped contours or levels", $"{skipped} · check tool size, height range and access from above.", 2));
        return Output("CNC Constant-Z finish", Seal("Constant-Z finish"));
    }
}
