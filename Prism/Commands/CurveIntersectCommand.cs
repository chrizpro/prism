using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core;
using Prism.Kernel;

namespace Prism.Commands;

/// <summary>Intersection of the normal projection surfaces of two planar curves.</summary>
public sealed class CurveIntersectCommand : Command
{
    readonly SelectionSlot _first, _second;
    readonly BoolParam _separate, _guides;
    readonly List<(Vec3 point, Vec3 first, Vec3 second)> _preview = [];
    Plane3 _firstPlane, _secondPlane;

    public CurveIntersectCommand()
    {
        const SelFilter curves = SelFilter.AnyCurve | SelFilter.Edge;
        _first = Slot(new SelectionSlot { Label = "First planar curve or sketch", Filter = curves });
        _second = Slot(new SelectionSlot { Label = "Second planar curve or sketch", Filter = curves });
        _separate = Check("Separate disconnected curves", false);
        _guides = Check("Show projection guides", true);
    }

    public override string Title => "Curve Intersect";
    public override string Icon => "section";
    public override string OkText => "Create curve";
    public override string Hint => "Select curves on two different, nonparallel planes. Each curve projects along its plane normal; their shared 3D curve becomes a sweep rail, loft guide or freeform reference. Sources are kept.";

    Plane3 Support(SelItem item, Shape curve)
    {
        // A line has no unique plane; its parent sketch supplies the intended support.
        var plane = S.PlaneOf(item) ?? S.Find(item.Obj)?.SketchPlane;
        if (plane == null) throw new FormatException("A source must be planar. For a straight curve, select a line from a planar sketch so its projection direction is defined.");
        var mesh = curve.Tessellate();
        double tolerance = Math.Max(1e-5, curve.Props().Size.Length * 1e-6);
        for (int i = 0; i + 2 < mesh.EdgePoints.Length; i += 3)
        {
            var point = new Vec3(mesh.EdgePoints[i], mesh.EdgePoints[i + 1], mesh.EdgePoints[i + 2]);
            if (Math.Abs(Vec3.Dot(point - plane.Value.Origin, plane.Value.Normal)) > tolerance)
                throw new FormatException("The selected geometry spans multiple planes. Pick an individual planar edge instead of the entire sketch.");
        }
        return plane.Value;
    }

    protected override CommandOutput? Build()
    {
        Results.Clear(); _preview.Clear();
        if (!Complete) return null;
        var first = _first.Items[0]; var second = _second.Items[0];
        if (first == second) throw new FormatException("Select two different curves.");
        using var a = SurfaceUtil.CurveShape(S, first);
        using var b = SurfaceUtil.CurveShape(S, second);
        _firstPlane = Support(first, a); _secondPlane = Support(second, b);
        double sine = Vec3.Cross(_firstPlane.Normal, _secondPlane.Normal).Length;
        if (sine < 1e-5) throw new FormatException("The source planes are parallel or nearly parallel. Choose curves on intersecting planes.");

        // Bound the normal extrusion in both directions using source extents and the
        // angle between supports. This also covers offset and oblique plane pairs.
        var ap = a.Props(); var bp = b.Props();
        double span = Math.Max(1, ap.Size.Length + bp.Size.Length + ap.Center.DistanceTo(bp.Center)) * 2 / sine;
        using var shiftedA = Occ.Translate(a, -_firstPlane.Normal * span);
        using var shiftedB = Occ.Translate(b, -_secondPlane.Normal * span);
        using var surfaceA = Occ.Extrude(shiftedA, _firstPlane.Normal * (span * 2));
        using var surfaceB = Occ.Extrude(shiftedB, _secondPlane.Normal * (span * 2));
        using var cut = Occ.Intersect(surfaceA, surfaceB);
        if (cut.Count(SubKind.Edge) == 0)
            throw new FormatException("The projected curves have no shared curve over their drawn extents. Extend or reposition the source curves.");
        var joined = Occ.Profile([cut], false);
        if (!joined.IsValid || joined.Count(SubKind.Edge) == 0)
            throw new FormatException("The projected intersection could not form a valid curve.");
        int wires = joined.Count(SubKind.Wire);
        var mesh = joined.Tessellate();
        int points = mesh.EdgePoints.Length / 3, stride = Math.Max(1, points / 12);
        for (int i = 0; i < points; i += stride)
        {
            var p = new Vec3(mesh.EdgePoints[i * 3], mesh.EdgePoints[i * 3 + 1], mesh.EdgePoints[i * 3 + 2]);
            var pa = p - _firstPlane.Normal * Vec3.Dot(p - _firstPlane.Origin, _firstPlane.Normal);
            var pb = p - _secondPlane.Normal * Vec3.Dot(p - _secondPlane.Origin, _secondPlane.Normal);
            _preview.Add((p, pa, pb));
        }
        Results.Add(new ResultLine("Connected curves", wires.ToString(), 1));
        Results.Add(new ResultLine("Sources", "Kept unchanged"));
        var output = new CommandOutput { Label = "Curve Intersect", Icon = Icon };
        if (_separate.Value && wires > 1)
        {
            for (int i = 0; i < wires; i++) output.Add.Add(new(joined.Sub(SubKind.Wire, i), "Curve intersect", ObjKind.Curve));
            joined.Dispose();
        }
        else output.Add.Add(new(joined, "Curve intersect", ObjKind.Curve));
        return output;
    }

    public override void BuildOverlay(CadSession s, Overlay o)
    {
        if (!_guides.Value || !PreviewEnabled || _preview.Count == 0) return;
        void Guide(Vec3 a, Vec3 b, uint color)
        { for (int i = 0; i < 12; i += 2) o.AddLine(Vec3.Lerp(a, b, i / 12d), Vec3.Lerp(a, b, (i + 1) / 12d), color, 1); }
        foreach (var (point, first, second) in _preview)
        { Guide(first, point, 0xB0F6B85B); Guide(second, point, 0xB0B697FF); }
        void Source(SelItem item, uint color)
        {
            var mesh = s.Find(item.Obj)?.Mesh; if (mesh == null) return;
            for (int i = 0; i < mesh.SegEdge.Length; i++)
            {
                if (item.Kind == SelKind.Edge && mesh.SegEdge[i] != item.Index) continue;
                int j = i * 6;
                o.AddLine(new(mesh.EdgePoints[j], mesh.EdgePoints[j + 1], mesh.EdgePoints[j + 2]),
                    new(mesh.EdgePoints[j + 3], mesh.EdgePoints[j + 4], mesh.EdgePoints[j + 5]), color, 3);
            }
        }
        Source(_first.Items[0], 0xFFF6B85B); Source(_second.Items[0], 0xFFB697FF);
        var mid = _preview[_preview.Count / 2];
        o.AddLabel(mid.first, "First curve", true);
        o.AddLabel(mid.second, "Second curve", true);
        o.AddLabel(mid.point, "3D intersection", true);
    }
}
