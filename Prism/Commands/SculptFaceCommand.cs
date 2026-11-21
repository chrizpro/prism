using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core;
using Prism.Kernel;

namespace Prism.Commands;

/// <summary>
/// Sculpt one flat face of a solid in place (owner direction 2026-11-16): the face becomes a NURBS surface, a smooth bump is pulled or pushed in its
/// interior with the outline fixed, and the face goes back into the body. The neighbouring faces do not follow, so the join is a sharp edge.
/// </summary>
/// <remarks>Living requirements: docs/modeling/direct-modeling/README.md, "Sculpt Face and Warp on faces".</remarks>
public sealed class SculptFaceCommand : Command
{
    readonly SelectionSlot _face;
    readonly NumberParam _height, _radius, _across, _up;
    readonly ChoiceParam _falloff;
    readonly IntParam _detail;
    SelItem? _sized;

    public SculptFaceCommand()
    {
        _face = Slot(new SelectionSlot { Label = "Flat face to sculpt", Filter = SelFilter.Face, SmartPick = true });
        _height = Num("Height (negative pushes in)", 3, -100000, 100000, 0.5, "mm");
        _radius = Num("Bump radius", 15, 0.1, 100000, 1, "mm");
        _falloff = Choice("Falloff", 0, "Smooth", "Linear", "Sharp");
        _across = Num("Move the bump across", 0, -100000, 100000, 1, "mm");
        _up = Num("Move the bump up", 0, -100000, 100000, 1, "mm");
        _detail = Add(new IntParam { Label = "Detail (more points)", Value = 3, Min = 1, Max = 5 });
    }

    public override string Title => "Sculpt Face";
    public override string Icon => "sculpt";
    public override string OkText => "Sculpt";
    public override string Hint => "Pick a flat, four-sided face of a solid, then set the height and size of the bump. The face's outline stays where it is, so the neighbouring faces meet the new surface at a sharp edge. If the result would not be a valid solid, nothing changes.";

    protected override void UpdateVisibility()
    {
        // a sensible bump size for the face just picked: 40% of its smaller side
        if (_face.Items.Count == 0 || _face.Items[0] == _sized) return;
        _sized = _face.Items[0];
        try
        {
            if (S.ShapeOf(_sized.Value) is { } face)
            {
                var size = face.Props().Size; var sides = new[] { size.X, size.Y, size.Z }.OrderByDescending(x => x).Take(2).ToArray();
                _radius.Value = Math.Max(0.5, Math.Round(sides[1] * 0.4, 1));
            }
        }
        catch (KernelException) { }
    }

    /// <summary>The face's own centre and the three directions the bump is described in: out of the face, across it, and up it.</summary>
    static (Vec3 centre, Vec3 normal, Vec3 across, Vec3 up) Frame(Shape face, FaceInfo info)
    {
        var normal = info.Normal.Normalized();
        var reference = Math.Abs(normal.Z) < 0.9 ? Vec3.UnitZ : Vec3.UnitX;
        var across = Vec3.Cross(reference, normal).Normalized();
        return (face.Props().Center, normal, across, Vec3.Cross(normal, across));
    }

    /// <summary>Grab handles in the viewport: height along the face normal, radius across the face, and two arrows that slide the bump over the face.</summary>
    public override IReadOnlyList<HandleSpec> Handles
    {
        get
        {
            if (_face.Items.Count == 0) return [];
            try
            {
                if (S.ShapeOf(_face.Items[0]) is not { } face) return [];
                var info = Occ.FaceInfoOf(face);
                if (info.Type != "Plane") return [];
                var (centre0, normal, across, up) = Frame(face, info);
                var bump = centre0 + across * _across.Value + up * _up.Value;
                return
                [
                    new(bump, normal, _height),
                    new(bump, across, _radius),
                    new(centre0, across, _across),
                    new(centre0, up, _up),
                ];
            }
            catch (Exception e) when (e is KernelException or FormatException) { return []; }
        }
    }
    public override HandleSpec? Handle => Handles.Count > 0 ? Handles[0] : null;

    protected override CommandOutput? Build()
    {
        Results.Clear();
        if (_face.Items.Count == 0) return null;
        var item = _face.Items[0];
        var body = DirectHelpers.BodyOf(S, item) ?? throw new FormatException("Sculpt Face works on a face of a solid body.");
        var original = S.ShapeOf(item) ?? throw new FormatException("That face is gone.");
        var info = Occ.FaceInfoOf(original);
        if (info.Type != "Plane") throw new FormatException("Sculpt Face works on flat faces for now. Curved faces are not supported yet.");
        if (original.Count(SubKind.Edge) != 4 || original.Count(SubKind.Wire) != 1)
            throw new FormatException("Sculpt Face works on a flat face with four sides and no holes for now.");

        var (faceCentre, normal, across, up) = Frame(original, info);
        var centre = faceCentre + across * _across.Value + up * _up.Value;
        var falloff = (Falloff)_falloff.Index;

        Shape sculpted;
        try
        {
            var surface = Occ.ToNurbs(original);
            for (int i = 0; i < _detail.Value; i++) surface = Occ.RefineFace(surface);
            var net = Occ.FaceNet(surface);
            var points = net.Points.ToArray();
            // the outer ring of points is never touched: that is what keeps the outline where it was
            for (int r = 1; r < net.Rows - 1; r++)
                for (int c = 1; c < net.Cols - 1; c++)
                {
                    int k = r * net.Cols + c;
                    points[k] += normal * (_height.Value * FalloffMath.Weight(falloff, points[k].DistanceTo(centre), _radius.Value));
                }
            sculpted = Occ.SetFaceNet(surface, points);
        }
        catch (KernelException e) { throw new FormatException("The face could not be reshaped: " + e.Message); }

        var pieces = new List<Shape> { sculpted };
        for (int i = 0; i < body.Shape.Count(SubKind.Face); i++) if (i != item.Index) pieces.Add(body.Shape.Sub(SubKind.Face, i));
        Shape result;
        try { result = Occ.Sew(pieces, 0.01, true); }
        catch (KernelException e) { throw new FormatException("The sculpted face could not be joined back into the body: " + e.Message); }
        if (!result.HasSolids || !result.IsValid)
            throw new FormatException("The sculpted face does not close into a valid solid, so nothing was changed. Try a smaller height or radius; the face must be a plain flat rectangle for now.");

        double before = body.Shape.Props().Volume, after = result.Props().Volume;
        Results.Add(new ResultLine("Volume", $"{before:0.##}  →  {after:0.##} mm³"));
        Results.Add(new ResultLine("Outline", "kept where it was; the neighbouring faces meet the new surface at a sharp edge"));
        var output = new CommandOutput { Label = "Sculpt face", Icon = "sculpt" };
        output.Replace.Add((body, result));
        return output;
    }
}
