using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core;
using Prism.Core.Cam;
using Prism.Kernel;

namespace Prism.Commands;

/// <summary>
/// Add a flange to an edge of a sheet-metal body. Pick the thin end face of the sheet along the edge: the flange runs the whole length of that edge, with a bend of the
/// chosen angle and inside radius and then a straight leg. It is the same bend Fold makes, so a plate with an edge flange equals the same part folded from a flat sketch.
/// </summary>
public sealed class EdgeFlangeCommand : Command
{
    readonly SelectionSlot _face;
    readonly NumberParam _length, _angle, _r;
    readonly ChoiceParam _dir;

    static double _lastR = 2;

    public EdgeFlangeCommand()
    {
        _face = Slot(new SelectionSlot { Label = "Thin end face of the sheet", Filter = SelFilter.Face });
        _length = Num("Flange length", 20, 0.01, 1e5, 5);
        _angle = Num("Bend angle", 90, 0.1, 180, 5, "°");
        _r = Num("Bend radius (inside)", _lastR, 0, 1000, 0.5);
        _dir = Choice("Fold toward", 0, "Up (the upper face of the sheet)", "Down (the lower face)");
    }

    public override string Title => "Edge Flange";
    public override string Icon => "edge-flange";
    public override string Hint => "Pick the thin end face of a sheet-metal body along the edge you want to flange. Set the length of the straight leg, the bend angle and the inside radius: a bend and a flange are added along the whole edge. Up and Down choose which face of the sheet the flange folds toward.";

    protected override CommandOutput? Build()
    {
        if (_face.Items.Count == 0) return null;
        Results.Clear();
        _lastR = _r.Value;
        var item = _face.Items[0];
        var body = DirectHelpers.BodyOf(S, item) ?? throw new InvalidOperationException("Pick a face of a solid body.");
        var face = S.ShapeOf(item) ?? throw new InvalidOperationException("The face is gone.");
        var frame = S.FaceFrame(item) ?? throw new InvalidOperationException("Pick a flat end face.");
        if (!Occ.FaceInfoOf(face).Type.Contains("lane", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Pick a flat end face: the sheet's edge must be straight.");
        var u = frame.normal.Normalized();                                    // out of the metal, across the edge

        // the end face is a rectangle: its long side runs along the edge, its short side is the thickness
        var pts = CamGeom.Loops(face, 0.01).SelectMany(l => l.pts).ToList();
        if (pts.Count < 4) throw new InvalidOperationException("Pick the thin end face of the sheet.");
        var longest = pts.Zip(pts.Skip(1).Append(pts[0]), (a, b) => (a, b)).MaxBy(e => e.a.DistanceTo(e.b));
        var d = (longest.b - longest.a).Normalized();
        d -= u * Vec3.Dot(d, u); d = d.Normalized();
        var across = Vec3.Cross(u, d).Normalized();
        double w = pts.Max(p => Vec3.Dot(p, d)) - pts.Min(p => Vec3.Dot(p, d)), t = pts.Max(p => Vec3.Dot(p, across)) - pts.Min(p => Vec3.Dot(p, across));
        if (w < 2 * t || t < 1e-6 || Math.Abs(Occ.FaceInfoOf(face).Area - w * t) > 1e-3 * w * t)
            throw new InvalidOperationException("Pick the thin end face of the sheet: a rectangle much longer than it is thick, along the straight edge to flange.");
        var centre = face.Props().Center;

        // "Up" is the upper face when the sheet is level, and a steady side otherwise
        var n = across;
        if (Math.Abs(n.Z) > 0.3) { if (n.Z < 0) n = -n; }
        else if (Vec3.Dot(n, Vec3.Cross(Vec3.UnitZ, u)) < 0) n = -n;
        int s = _dir.Index == 0 ? 1 : -1;
        double theta = _angle.Value * Math.PI / 180, r = Math.Max(_r.Value, 1e-3), leg = _length.Value;

        // cross-section in the (u, n) plane at the start of the edge: the metal runs n in [0, t] and ends at u = 0; the bend starts there
        var origin = centre - d * (w / 2) - n * (t / 2);
        double zIn = s > 0 ? t : 0;
        var cen = origin + n * (zIn + s * r);
        Vec3 a0 = n * (-s), a1 = a0 * Math.Cos(theta) + u * Math.Sin(theta), am = a0 * Math.Cos(theta / 2) + u * Math.Sin(theta / 2);
        var along = u * Math.Cos(theta) - a0 * Math.Sin(theta);                // direction the flange leaves the bend
        double ri = r, ro = r + t;
        var edges = new List<Shape>
        {
            Occ.Line(cen + a0 * ri, cen + a0 * ro), Occ.Arc3(cen + a0 * ro, cen + am * ro, cen + a1 * ro),
            Occ.Line(cen + a1 * ro, cen + a1 * ro + along * leg), Occ.Line(cen + a1 * ro + along * leg, cen + a1 * ri + along * leg),
            Occ.Line(cen + a1 * ri + along * leg, cen + a1 * ri), Occ.Arc3(cen + a1 * ri, cen + am * ri, cen + a0 * ri),
        };
        var flange = Occ.Extrude(Occ.Profile(edges, true), d * w);
        var joined = Occ.Fuse(body.Shape, flange);
        if (!joined.IsValid || joined.Count(SubKind.Solid) != 1)
            throw new InvalidOperationException("The flange would run into the rest of the part or did not join it. Use a smaller angle or length, or the other direction.");

        Results.Add(new ResultLine("Flange", $"{w:0.###} mm wide, {Math.Abs(_angle.Value):0.##}° bend, inside radius {_r.Value:0.###} mm, leg {leg:0.###} mm"));
        Results.Add(new ResultLine("Sheet", $"{t:0.###} mm thick"));
        var o = new CommandOutput { Label = "Edge flange", Icon = "edge-flange" };
        o.Replace.Add((body, joined));
        return o;
    }
}
