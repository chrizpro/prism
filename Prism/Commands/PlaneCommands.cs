using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Prism.Core;
using Prism.Kernel;

namespace Prism.Commands;

/// <summary>
/// Construction planes: offset, angled, midway, normal to a curve, or through three points. Any method can make several planes at once
/// (a count with spacing, or a list of values), which is what lofts, sweeps and stacks of sketches need.
/// </summary>
public sealed class ConstructionPlaneCommand : Command
{
    enum Method { Offset, Angle, Midplane, NormalToCurve, ThreePoints, TangentFace, TwoEdges, FlatCurve }

    readonly ChoiceParam _method;
    readonly SelectionSlot _base, _second, _hinge, _curve, _tanFace, _edge1, _edge2, _flat;
    readonly NumberParam _distance, _angle, _size, _pos;
    readonly IntParam _count;
    readonly TextParam _list;
    readonly BoolParam _bothSides;
    readonly NumberParam[] _pt = new NumberParam[9];

    public ConstructionPlaneCommand()
    {
        _method = Choice("Method", 0, "Offset from a plane", "At an angle to a plane", "Midway between two planes", "Normal to a curve", "Through three points", "Tangent to a round face", "Through two straight edges", "Through a flat curve or sketch");
        _base = Slot(new SelectionSlot { Label = "Plane or flat face", Filter = SelFilter.Face | SelFilter.Plane });
        _second = Slot(new SelectionSlot { Label = "Second plane or face", Filter = SelFilter.Face | SelFilter.Plane, IsVisible = false });
        _hinge = Slot(new SelectionSlot { Label = "Hinge line (straight edge or axis)", Filter = SelFilter.Edge | SelFilter.Axis, IsVisible = false });
        _curve = Slot(new SelectionSlot { Label = "Curve or edge", Filter = SelFilter.AnyCurve | SelFilter.Edge, IsVisible = false });
        _tanFace = Slot(new SelectionSlot { Label = "Round face (hole, boss or fillet)", Filter = SelFilter.Face, IsVisible = false });
        _edge1 = Slot(new SelectionSlot { Label = "First straight edge", Filter = SelFilter.Edge | SelFilter.CurveObj, IsVisible = false });
        _edge2 = Slot(new SelectionSlot { Label = "Second straight edge", Filter = SelFilter.Edge | SelFilter.CurveObj, IsVisible = false });
        _flat = Slot(new SelectionSlot { Label = "Flat curve or sketch", Filter = SelFilter.AnyCurve | SelFilter.Edge, IsVisible = false });
        _distance = Num("Distance", 10);
        _angle = Num("Angle", 45, -360, 360, 5, "°");
        _count = Add(new IntParam { Label = "Number of planes", Min = 1, Max = 100, Value = 1 });
        _list = Text("…or a list of values", "", "e.g. 5, 12.5, 30  (overrides the number and spacing)");
        _pos = Num("Position along the curve", 0, 0, 100, 1, "%");
        _bothSides = Check("Also on the other side", false);
        _size = Num("Plane size (0 = automatic)", 0, 0, 1e6);
        string[] names = ["Point 1 X", "Point 1 Y", "Point 1 Z", "Point 2 X", "Point 2 Y", "Point 2 Z", "Point 3 X", "Point 3 Y", "Point 3 Z"];
        double[] def = [0, 0, 0, 50, 0, 0, 0, 50, 30];
        for (int i = 0; i < 9; i++) { _pt[i] = Num(names[i], def[i]); _pt[i].IsVisible = false; }
    }

    public override string Title => "Construction Plane";
    public override string Icon => "origin-plane";
    public override string OkText => "Create plane(s)";
    public override string Hint => (Method)_method.Index switch
    {
        Method.Offset => "Pick a plane or flat face, then the distance. Raise 'Number of planes' for a stack spaced by that distance, or type a list of distances.",
        Method.Angle => "Pick a plane or face and the straight edge or axis it turns around, then the angle. Several angles can be listed.",
        Method.Midplane => "Pick two parallel planes or faces: the new plane lies halfway between them.",
        Method.TangentFace => "Pick a cylindrical face. The plane touches it along a line, at the angle round the axis you give (a list or a count makes several).",
        Method.TwoEdges => "Pick two straight edges that lie in one flat sheet (parallel, or crossing). The plane contains both.",
        Method.FlatCurve => "Pick a flat curve or sketch: the plane is the one it lies in.",
        Method.NormalToCurve => "Pick a curve or edge. Planes are placed across it, square to the curve, spread evenly along its length (or at the percentages you list). Ideal sections for a loft.",
        _ => "Type the coordinates of three points; the plane passes through all of them.",
    };

    protected override void UpdateVisibility()
    {
        var m = (Method)_method.Index;
        _base.IsVisible = m is Method.Offset or Method.Angle or Method.Midplane;
        _second.IsVisible = m == Method.Midplane;
        _hinge.IsVisible = m == Method.Angle;
        _curve.IsVisible = m == Method.NormalToCurve;
        _tanFace.IsVisible = m == Method.TangentFace; _edge1.IsVisible = _edge2.IsVisible = m == Method.TwoEdges; _flat.IsVisible = m == Method.FlatCurve;
        _distance.IsVisible = m == Method.Offset;
        _angle.IsVisible = m is Method.Angle or Method.TangentFace;
        _count.IsVisible = m is Method.Offset or Method.Angle or Method.NormalToCurve or Method.TangentFace;
        _list.IsVisible = m is Method.Offset or Method.Angle or Method.NormalToCurve or Method.TangentFace;
        _list.Caption = m switch
        {
            Method.Offset => "…or a list of distances (mm)",
            Method.Angle or Method.TangentFace => "…or a list of angles (°)",
            Method.NormalToCurve => "…or positions along the curve (%)",
            _ => null,
        };
        _pos.IsVisible = m == Method.NormalToCurve && _count.Value == 1 && string.IsNullOrWhiteSpace(_list.Value);
        _bothSides.IsVisible = m is Method.Offset or Method.Angle;
        foreach (var p in _pt) p.IsVisible = m == Method.ThreePoints;
    }

    // ------------------------------------------------------------ helpers
    static Vec3 Rotate(Vec3 v, Vec3 axis, double ang)
    {
        double c = Math.Cos(ang), s = Math.Sin(ang);
        return v * c + Vec3.Cross(axis, v) * s + axis * (Vec3.Dot(axis, v) * (1 - c));
    }

    static Plane3 FromNormal(Vec3 origin, Vec3 n)
    {
        n = n.Normalized();
        var u = Math.Abs(n.Z) < 0.99 ? Vec3.Cross(Vec3.UnitZ, n).Normalized() : Vec3.UnitX;
        return new Plane3(origin, u, Vec3.Cross(n, u));
    }

    List<double> Values(string what, double each)
    {
        var res = new List<double>();
        if (!string.IsNullOrWhiteSpace(_list.Value))
        {
            foreach (var tok in _list.Value.Split([',', ';', ' ', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                if (!double.TryParse(tok, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) throw new FormatException($"'{tok}' is not a number in the list of {what}.");
                res.Add(v);
            }
        }
        else for (int i = 1; i <= _count.Value; i++) res.Add(each * i);
        if (res.Count > 200) throw new FormatException("That would make more than 200 planes.");
        return res;
    }

    /// <summary>The ordered polyline of an edge or curve object.</summary>
    List<Vec3> CurvePoints(SelItem it)
    {
        var o = S.Find(it.Obj) ?? throw new InvalidOperationException("That curve is gone.");
        var m = o.Mesh; var E = m.EdgePoints; var pts = new List<Vec3>();
        for (int sg = 0; sg < m.SegEdge.Length; sg++)
        {
            if (it.Kind == SelKind.Edge && m.SegEdge[sg] != it.Index) continue;
            var a = new Vec3(E[6 * sg], E[6 * sg + 1], E[6 * sg + 2]); var b = new Vec3(E[6 * sg + 3], E[6 * sg + 4], E[6 * sg + 5]);
            if (pts.Count == 0 || pts[^1].DistanceTo(a) > 1e-6) pts.Add(a);
            pts.Add(b);
        }
        if (pts.Count < 2) throw new InvalidOperationException("Could not read that curve.");
        return pts;
    }

    (Vec3 origin, Vec3 dir) HingeLine()
    {
        var it = _hinge.Items[0];
        if (it.Kind == SelKind.Origin) return CadSession.OriginAxis(it.Index);
        var pts = CurvePoints(it);
        var a = pts[0]; var b = pts[^1];
        double chord = a.DistanceTo(b);
        if (chord < 1e-9) throw new InvalidOperationException("That edge has no direction.");
        var dir = (b - a) / chord;
        // a straight edge: every point sits on the chord
        if (pts.Any(p => Vec3.Cross(p - a, dir).Length > 1e-4 * Math.Max(1, chord))) throw new InvalidOperationException("Pick a straight edge (or an origin axis) as the hinge.");
        return (a, dir);
    }

    public override IReadOnlyList<HandleSpec> Handles
    {
        get
        {
            try
            {
                var m = (Method)_method.Index;
                if (m == Method.NormalToCurve)
                {
                    // a single plane slides along the curve: arrow from the start, % follows the arc length
                    if (_curve.Items.Count == 0 || _count.Value != 1 || !string.IsNullOrWhiteSpace(_list.Value)) return [];
                    var pts = CurvePoints(_curve.Items[0]); var cum = 0.0;
                    for (int i = 1; i < pts.Count; i++) cum += pts[i].DistanceTo(pts[i - 1]);
                    var tan = (pts[Math.Min(2, pts.Count - 1)] - pts[0]).Normalized();
                    return [new HandleSpec(pts[0], tan, _pos, 100.0 / Math.Max(cum, 1e-9))];
                }
                if (m is not (Method.Offset or Method.Angle) || _base.Items.Count == 0 || S.PlaneOf(_base.Items[0]) is not { } b) return [];
                var it = _base.Items[0];
                var at = b.Origin;
                if (it.Kind == SelKind.Face && S.ShapeOf(it) is { } sh) { var pr = sh.Props(); var c = (pr.Min + pr.Max) * 0.5; at = c - b.Normal * Vec3.Dot(c - b.Origin, b.Normal); }
                if (m == Method.Offset) return [new HandleSpec(at, b.Normal, _distance)];
                if (_hinge.Items.Count == 0) return [];
                var (ho, hd) = HingeLine(); hd = hd.Normalized();
                var foot = ho + hd * Vec3.Dot(at - ho, hd);
                var away = at - foot;
                if (away.Length < 1e-6) away = b.U;
                double radius = Math.Max(away.Length, 10);
                return [new HandleSpec(foot, hd, _angle, 1, away.Normalized(), radius)];
            }
            catch (Exception e) when (e is not OutOfMemoryException) { return []; }
        }
    }
    public override HandleSpec? Handle => Handles.Count > 0 ? Handles[0] : null;

    // ------------------------------------------------------------ build
    protected override CommandOutput? Build()
    {
        Results.Clear(); _preview.Clear();
        var m = (Method)_method.Index;
        var planes = new List<(Plane3 plane, string label)>();
        Plane3 Need(SelectionSlot slot) => slot.Items.Count > 0 && S.PlaneOf(slot.Items[0]) is { } p ? p : throw new InvalidOperationException("Pick a plane or a flat face.");

        switch (m)
        {
            case Method.Offset:
            {
                if (_base.Items.Count == 0) return null;
                var b = Need(_base); var n = b.Normal;
                foreach (var d in Values("distances", _distance.Value))
                {
                    planes.Add((new Plane3(b.Origin + n * d, b.U, b.V), $"{d:0.##} mm"));
                    if (_bothSides.Value && Math.Abs(d) > 1e-9) planes.Add((new Plane3(b.Origin - n * d, b.U, b.V), $"{-d:0.##} mm"));
                }
                break;
            }
            case Method.Angle:
            {
                if (_base.Items.Count == 0 || _hinge.Items.Count == 0) return null;
                var b = Need(_base); var (ho, hd) = HingeLine();
                foreach (var a in Values("angles", _angle.Value))
                {
                    foreach (var sgn in _bothSides.Value && Math.Abs(a) > 1e-9 ? new[] { 1.0, -1.0 } : [1.0])
                    {
                        double r = sgn * a * Math.PI / 180;
                        var org = ho + Rotate(b.Origin - ho, hd, r);
                        planes.Add((new Plane3(org, Rotate(b.U, hd, r), Rotate(b.V, hd, r)), $"{sgn * a:0.##}°"));
                    }
                }
                break;
            }
            case Method.Midplane:
            {
                if (_base.Items.Count == 0 || _second.Items.Count == 0) return null;
                var a = Need(_base); var b = Need(_second);
                if (Vec3.Cross(a.Normal, b.Normal).Length > 1e-4) throw new InvalidOperationException("The two planes are not parallel, so there is no single midway plane.");
                double gap = Vec3.Dot(b.Origin - a.Origin, a.Normal);
                // centre the new plane between the two pieces (not over some corner of the first one), then drop it onto the midway level
                Vec3 CenterOfItem(SelectionSlot sl, Plane3 pl)
                {
                    var it = sl.Items[0];
                    if (it.Kind != SelKind.Face || S.ShapeOf(it) is not { } sh) return pl.Origin;
                    var pr = sh.Props(); return (pr.Min + pr.Max) * 0.5;
                }
                var mid = (CenterOfItem(_base, a) + CenterOfItem(_second, b)) * 0.5;
                var onPlane = a.Origin + a.Normal * (gap / 2);
                var org = mid - a.Normal * Vec3.Dot(mid - onPlane, a.Normal);
                planes.Add((new Plane3(org, a.U, a.V), $"gap {Math.Abs(gap):0.##} mm"));
                break;
            }
            case Method.NormalToCurve:
            {
                if (_curve.Items.Count == 0) return null;
                var pts = CurvePoints(_curve.Items[0]);
                var cum = new List<double> { 0 };
                for (int i = 1; i < pts.Count; i++) cum.Add(cum[^1] + pts[i].DistanceTo(pts[i - 1]));
                double total = cum[^1];
                List<double> fr;
                if (!string.IsNullOrWhiteSpace(_list.Value)) fr = Values("positions", 1).Select(v => v / 100).ToList();
                else if (_count.Value == 1) fr = [_pos.Value / 100];
                else fr = Enumerable.Range(0, _count.Value).Select(i => (double)i / (_count.Value - 1)).ToList();
                foreach (var f in fr)
                {
                    double len = Math.Clamp(f, 0, 1) * total;
                    int k = cum.FindLastIndex(c => c <= len); k = Math.Clamp(k, 0, pts.Count - 2);
                    double segLen = Math.Max(cum[k + 1] - cum[k], 1e-12), t = (len - cum[k]) / segLen;
                    var p = pts[k] + (pts[k + 1] - pts[k]) * t;
                    // smooth the direction over the neighbouring segments so a polyline-sampled curve does not give a jittery tangent
                    int k0 = Math.Max(0, k - 1), k1 = Math.Min(pts.Count - 1, k + 2);
                    var tan = (pts[k1] - pts[k0]).Normalized();
                    planes.Add((FromNormal(p, tan), $"{f * 100:0.#}%"));
                }
                break;
            }
            case Method.TangentFace:
            {
                if (_tanFace.Items.Count == 0) return null;
                var sh = S.ShapeOf(_tanFace.Items[0]) ?? throw new InvalidOperationException("Pick a cylindrical face.");
                var cyls = Occ.Cylinders(sh);
                if (cyls.Count == 0) throw new InvalidOperationException("That face is not cylindrical. Pick a round hole, boss or fillet.");
                var c = cyls[0]; var ax = c.Axis.Normalized();
                var refd = Math.Abs(ax.Z) < 0.9 ? Vec3.Cross(Vec3.UnitZ, ax).Normalized() : Vec3.Cross(Vec3.UnitX, ax).Normalized();
                foreach (var a in Values("angles", _angle.Value))
                {
                    var dir = Rotate(refd, ax, a * Math.PI / 180);
                    planes.Add((FromNormal(c.Origin + ax * ((c.V0 + c.V1) / 2) + dir * c.Radius, dir), $"{a:0.#}Â° round the axis"));
                }
                break;
            }
            case Method.TwoEdges:
            {
                if (_edge1.Items.Count == 0 || _edge2.Items.Count == 0) return null;
                var A = CurvePoints(_edge1.Items[0]); var B = CurvePoints(_edge2.Items[0]);
                Vec3 a0 = A[0], a1 = A[^1], b0 = B[0], b1 = B[^1];
                var d1 = (a1 - a0).Normalized(); var d2 = (b1 - b0).Normalized();
                var n = Vec3.Cross(d1, d2); if (n.Length < 1e-4) n = Vec3.Cross(d1, b0 - a0);
                if (n.Length < 1e-9) throw new InvalidOperationException("Those two edges are on the same line, so they do not fix a plane.");
                n = n.Normalized();
                if (Math.Abs(Vec3.Dot(b0 - a0, n)) > 1e-3 * Math.Max(1, (b0 - a0).Length)) throw new InvalidOperationException("Those two edges do not lie in one plane.");
                planes.Add((FromNormal((a0 + a1 + b0 + b1) * 0.25, n), "two edges"));
                break;
            }
            case Method.FlatCurve:
            {
                if (_flat.Items.Count == 0) return null;
                var pts = CurvePoints(_flat.Items[0]);
                var cen = pts.Aggregate(Vec3.Zero, (acc, p) => acc + p) / pts.Count;
                var n = Vec3.Zero; for (int i = 0; i + 1 < pts.Count; i++) n += Vec3.Cross(pts[i] - cen, pts[i + 1] - cen);
                if (n.Length < 1e-9) throw new InvalidOperationException("That curve is straight, so it does not fix a plane.");
                n = n.Normalized();
                double extent = pts.Max(p => (p - cen).Length);
                if (pts.Any(p => Math.Abs(Vec3.Dot(p - cen, n)) > 1e-3 * Math.Max(1, extent))) throw new InvalidOperationException("That curve is not flat. Pick one that lies in a single plane.");
                planes.Add((FromNormal(cen, n), "through the curve"));
                break;
            }
            case Method.ThreePoints:
            {
                var P = Enumerable.Range(0, 3).Select(i => new Vec3(_pt[3 * i].Value, _pt[3 * i + 1].Value, _pt[3 * i + 2].Value)).ToArray();
                var n = Vec3.Cross(P[1] - P[0], P[2] - P[0]);
                if (n.Length < 1e-9) throw new InvalidOperationException("The three points are in a line, so they do not define a plane.");
                var u = (P[1] - P[0]).Normalized();
                var pl = new Plane3(P[0], u, Vec3.Cross(n.Normalized(), u));
                planes.Add((pl, "3 points"));
                break;
            }
        }
        if (planes.Count == 0) { _preview.Clear(); return null; }

        double half = _size.Value > 0 ? _size.Value / 2 : Math.Max(S.OriginSize() * 0.6, 40);
        _preview.Clear(); _preview.AddRange(planes.Select(p => p.plane)); _previewHalf = half;
        var o = new CommandOutput { Label = planes.Count == 1 ? "Construction plane" : $"{planes.Count} construction planes", Icon = "origin-plane" };
        foreach (var (pl, label) in planes)
        {
            var rect = Occ.Polyline([pl.PointAt(-half, -half), pl.PointAt(half, -half), pl.PointAt(half, half), pl.PointAt(-half, half)], true);
            o.Add.Add(new(rect, "Plane", ObjKind.Plane, Plane: pl));
            Results.Add(new ResultLine("Plane", label));
        }
        return o;
    }

    readonly List<Plane3> _preview = [];
    double _previewHalf = 40;

    public override void BuildOverlay(CadSession s, Overlay o)
    {
        // ghost rectangles for the planes about to be made
        foreach (var pl in _preview)
        {
            Vec3 P(double u, double v) => pl.PointAt(u * _previewHalf, v * _previewHalf);
            o.AddTri(P(-1, -1), P(1, -1), P(1, 1), 0x449B8CFF); o.AddTri(P(-1, -1), P(1, 1), P(-1, 1), 0x449B8CFF);
            o.AddPolyline([P(-1, -1), P(1, -1), P(1, 1), P(-1, 1)], 0xFFB6A9FF, 2f, true);
        }
    }
}

/// <summary>The curve(s) where two planes, faces or surfaces cross: plane-and-plane gives a straight line; a plane through a body gives its outline.</summary>
public sealed class IntersectCurvesCommand : Command
{
    const SelFilter Solids = SelFilter.Plane | SelFilter.Face | SelFilter.SurfaceObj | SelFilter.Solid | SelFilter.Form;
    const SelFilter Curves = SelFilter.AnyCurve | SelFilter.Edge;
    readonly SelectionSlot _a, _b;
    readonly ChoiceParam _between;
    readonly BoolParam _separate;
    CurveIntersectCommand? _curves;

    public IntersectCurvesCommand()
    {
        _a = Slot(new SelectionSlot { Label = "First", Filter = Solids });
        _b = Slot(new SelectionSlot { Label = "Second", Filter = Solids });
        _between = Choice("Between", 0, "Automatic (from what you pick)", "Two flat curves (projected)", "Planes, faces or bodies");
        _separate = Check("Make each curve its own object", false);
    }

    public override string Title => "Intersection Curve";
    public override string Icon => "section";
    public override string OkText => "Create curve";
    public override string Hint => "Pick two planes, flat or curved faces, or bodies: the curve where they cross is created. Two planes give a straight line, a plane through a body gives the outline of the cut. Or pick two flat curves (they are recognised on their own; Between can force either kind): each curve on its own plane is projected along that plane's normal and the shared 3D curve is created. Use it for sweep rails, loft guides and sketch references.";

    int _lastBetween;

    bool IsCurveItem(SelItem it) => it.Kind == SelKind.Edge || S.Find(it.Obj)?.Kind is ObjKind.Curve or ObjKind.Sketch;

    /// <summary>Two flat curves when asked for, or when the first thing picked is a curve; planes, faces and bodies otherwise.</summary>
    bool CurveMode => _between.Index == 1 || _between.Index == 0 && _a.Items.Count > 0 && IsCurveItem(_a.Items[0]);

    protected override void UpdateVisibility()
    {
        // Automatic: until something is picked either kind is welcome; the first pick decides what the second may be
        var filter = _between.Index == 1 ? Curves : _between.Index == 2 ? Solids : CurveMode ? Curves : _a.Items.Count > 0 ? Solids : Solids | Curves;
        if (_lastBetween != _between.Index) { _lastBetween = _between.Index; _a.Items.Clear(); _b.Items.Clear(); }
        _b.Filter = filter;
        _a.Filter = _between.Index == 0 ? Solids | Curves : filter;
    }

    public override void BuildOverlay(CadSession s, Overlay o) { if (CurveMode) _curves?.BuildOverlay(s, o); }

    Shape ShapeFor(SelectionSlot slot)
    {
        var it = slot.Items[0];
        // an infinite plane is stood in for by a very large square
        if (it.Kind == SelKind.Origin || (it.Kind == SelKind.Object && S.Find(it.Obj)?.Kind == ObjKind.Plane))
        {
            var pl = S.PlaneOf(it) ?? throw new FormatException("Pick a plane.");
            double h = Math.Max(S.OriginSize() * 6, 1000);
            return Occ.Profile([Occ.Polyline([pl.PointAt(-h, -h), pl.PointAt(h, -h), pl.PointAt(h, h), pl.PointAt(-h, h)], true)], true);
        }
        return S.ShapeOf(it) ?? throw new FormatException("Pick a plane, a face or a body.");
    }

    protected override CommandOutput? Build()
    {
        Results.Clear();
        if (_a.Items.Count == 0 || _b.Items.Count == 0) return null;
        if (CurveMode)
        {
            var curves = _curves = new CurveIntersectCommand();
            curves.Begin(S);
            curves.ConfigureInputs(() =>
            {
                curves.Slots[0].TryAdd(_a.Items[0]); curves.Slots[1].TryAdd(_b.Items[0]);
                curves.Params.OfType<BoolParam>().First(p => p.Label.StartsWith("Separate")).Value = _separate.Value;
            });
            var output = curves.TryBuild() ?? throw new FormatException(curves.Error ?? "Those curves have no shared curve.");
            foreach (var line in curves.Results) Results.Add(line);
            return output;
        }
        if (IsCurveItem(_b.Items[0])) throw new FormatException("Pick two curves, or two planes, faces or bodies. A curve and a surface do not make a curve.");
        var cut = Occ.Intersect(ShapeFor(_a), ShapeFor(_b));
        int edges = cut.Count(SubKind.Edge);
        if (edges == 0) throw new FormatException("Those two do not cross each other.");
        Results.Add(new ResultLine("Curves", edges.ToString()));
        var o = new CommandOutput { Label = "Intersection curve", Icon = "section" };
        if (_separate.Value && edges > 1) { for (int i = 0; i < edges; i++) o.Add.Add(new(cut.Sub(SubKind.Edge, i), "Intersection", ObjKind.Curve)); }
        else o.Add.Add(new(cut, "Intersection", ObjKind.Curve));
        return o;
    }
}
