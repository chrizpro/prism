using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core.Sketching;
using Prism.Kernel;
using Prism.Meshing;

namespace Prism.Core;

/// <summary>
/// Fit editable sketch geometry to a span of the measured mesh cut (Mesh guide, MSH-P04).
/// Pick a start and an end on the orange trace: the sketch curves lying along that span are replaced by one fitted line, arc or
/// spline that keeps the existing end points, so the profile stays connected. A curve selected beforehand is replaced instead.
/// Ends that meet a neighbouring line or arc smoothly get a tangent constraint.
/// </summary>
public sealed class FitMeshSectionTool : SketchTool
{
    enum CurveKind { Line, Arc, Circle, Spline }
    readonly record struct TracePick(MeshSectionTrace Trace, int Segment, double Fraction, MeshSectionSample Sample, int? Point)
    {
        public double Index => Segment + Fraction;
    }

    static readonly ToolOption[] Kinds = [
        new("Line", "Fit a straight span.", "line", "Curve"),
        new("Arc", "Fit a circular arc to a span.", "arc", "Curve"),
        new("Circle", "Fit a circle to a closed outline.", "circle", "Curve"),
        new("Spline", "Fit a smooth curve to a span.", "spline", "Curve")
    ];

    /// <summary>Largest angle (degrees) between two curves meeting at a point for the fit to make them tangent.</summary>
    public const double TangentAngle = 10;
    const double SnapPixels = 12;

    CurveKind _kind = (CurveKind)Recall("Fit Section Curve");
    TracePick? _start, _hover;
    bool _otherWay, _tangent = true;
    int? _replace;
    string _last = "";
    readonly Dictionary<MeshSectionTrace, double> _tolerance = [];

    public override string Name => "Fit Section Curve";
    public override IReadOnlyList<ToolOption> Options => Kinds;
    public override int OptionIndex => (int)_kind;
    public override string Prompt => _kind == CurveKind.Circle ? "Click a closed measured outline."
        : _start == null ? "Click the start of the span on the orange measured outline. Clicking near a curve's end point keeps it."
        : "Click the end of the span on the same outline.";
    public override IReadOnlyList<ToolControl> Panel =>
    [
        new(ToolControlKind.Info, "target", _replace.HasValue ? "Replacing the selected curve." : "Curves along the picked span are replaced by one fitted curve."),
        new(ToolControlKind.Toggle, "other-way", "Use longer span on a closed outline", On: _otherWay, Enabled: _kind != CurveKind.Circle),
        new(ToolControlKind.Toggle, "tangent", "Make tangent to neighbouring curves", On: _tangent, Enabled: _kind is CurveKind.Line or CurveKind.Arc),
        .. _last.Length > 0 ? new[] { new ToolControl(ToolControlKind.Info, "last", _last) } : [],
    ];

    /// <summary>Report of the last fit, for scripts and tests.</summary>
    public string LastResult => _last;

    public override void Activate(CadSession s) => _replace = Sk(s).SelEntities is { Count: 1 } selection ? selection.Single() : null;
    public override void Deactivate(CadSession s) { ResetState(s); base.Deactivate(s); }
    protected override void ApplyOption(int index) => _kind = (CurveKind)index;
    protected override void ResetState(CadSession s) { _start = _hover = null; s.Invalidate(); }

    public override void PanelSet(CadSession s, string id, object? value)
    {
        if (id == "other-way" && value is bool on) { _otherWay = on; s.Invalidate(); }
        if (id == "tangent" && value is bool t) { _tangent = t; s.Invalidate(); }
    }

    public override void PointerMove(CadSession s, PointerInfo p)
    {
        _hover = FindTrace(s, p);
        s.Invalidate();
    }

    /// <summary>The fit tolerance for a trace: four times its measured noise (see <see cref="LineArcFit.NoiseSigma"/>).</summary>
    double Tolerance(MeshSectionTrace trace)
    {
        if (_tolerance.TryGetValue(trace, out var t)) return t;
        var pts = trace.Points.Select(q => (q.X, q.Y)).ToList();
        return _tolerance[trace] = LineArcFit.ToleranceFromNoise(LineArcFit.NoiseSigma(pts, trace.Closed), LineArcFit.Size(pts));
    }

    /// <summary>Millimetres per screen pixel at a sketch position.</summary>
    static double PixelSize(CadSession s, SketchContext sk, double x, double y)
    {
        if (s.Camera.Project(W(sk, x, y)) is not { } a || s.Camera.Project(W(sk, x + 1, y)) is not { } b) return 0.1;
        double px = Math.Sqrt((a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y));
        return px > 1e-9 ? 1 / px : 0.1;
    }

    /// <summary>End points of drawn (non-construction) curves.</summary>
    static IEnumerable<int> Endpoints(SketchModel model) =>
        model.Entities.Where(e => !e.Construction).SelectMany(model.EndpointsOf).Distinct();

    TracePick? FindTrace(CadSession s, PointerInfo p)
    {
        var sk = Sk(s);
        double best = 10;
        TracePick? result = null;
        foreach (var trace in sk.Model.MeshSectionTraces)
        {
            if (_start is { } start && !ReferenceEquals(start.Trace, trace)) continue;
            if (_kind == CurveKind.Circle && !trace.Closed) continue;
            var points = trace.Points;
            for (int i = 0; i < points.Length - (trace.Closed ? 0 : 1); i++)
            {
                var a = points[i]; var b = points[(i + 1) % points.Length];
                if (s.Camera.Project(W(sk, a.X, a.Y)) is not { } pa || s.Camera.Project(W(sk, b.X, b.Y)) is not { } pb) continue;
                var (fraction, distance) = Picking.SegDist(p.X, p.Y, pa.x, pa.y, pb.x, pb.y);
                if (distance >= best) continue;
                best = distance;
                result = new TracePick(trace, i, fraction, new(a.X + (b.X - a.X) * fraction, a.Y + (b.Y - a.Y) * fraction), null);
            }
        }
        if (result is not { } hit || _kind == CurveKind.Circle) return result;

        // an existing curve end close to the pointer wins: the span then starts exactly there and the new curve shares that point
        int? snap = null; double near = SnapPixels;
        foreach (int id in Endpoints(sk.Model))
        {
            var q = sk.Model.P(id);
            if (s.Camera.Project(W(sk, q.X, q.Y)) is not { } sp) continue;
            double d = Math.Sqrt((sp.x - p.X) * (sp.x - p.X) + (sp.y - p.Y) * (sp.y - p.Y));
            if (d < near) { near = d; snap = id; }
        }
        if (snap is not int pid) return hit;
        var at = sk.Model.P(pid);
        var onTrace = Nearest(hit.Trace, at.X, at.Y);
        double tol = Math.Max(Tolerance(hit.Trace) * 4, SnapPixels * PixelSize(s, sk, at.X, at.Y));
        return Dist(onTrace.Sample.X, onTrace.Sample.Y, at.X, at.Y) <= tol ? onTrace with { Point = pid } : hit;
    }

    static TracePick Nearest(MeshSectionTrace trace, double x, double y)
    {
        var points = trace.Points;
        TracePick best = new(trace, 0, 0, points[0], null); double bd = double.MaxValue;
        for (int i = 0; i < points.Length - (trace.Closed ? 0 : 1); i++)
        {
            var a = points[i]; var b = points[(i + 1) % points.Length];
            double dx = b.X - a.X, dy = b.Y - a.Y, l2 = dx * dx + dy * dy;
            double f = l2 < 1e-24 ? 0 : Math.Clamp(((x - a.X) * dx + (y - a.Y) * dy) / l2, 0, 1);
            double px = a.X + dx * f, py = a.Y + dy * f, d = Dist(px, py, x, y);
            if (d < bd) { bd = d; best = new(trace, i, f, new(px, py), null); }
        }
        return best;
    }

    List<MeshSectionSample> Span(TracePick a, TracePick b)
    {
        var trace = a.Trace;
        var points = trace.Points;
        if (a.Index > b.Index) (a, b) = (b, a);
        List<MeshSectionSample> Along(TracePick from, TracePick to, bool wrap)
        {
            var samples = new List<MeshSectionSample> { from.Sample };
            for (int i = (int)Math.Floor(from.Index) + 1; i < to.Index + (wrap ? points.Length : 0); i++)
                if (points[i % points.Length] != samples[^1]) samples.Add(points[i % points.Length]);
            if (to.Sample != samples[^1]) samples.Add(to.Sample);
            return samples;
        }

        var direct = Along(a, b, false);
        if (!trace.Closed) return direct;
        var around = Along(b, a, true);
        static double Length(List<MeshSectionSample> samples) => samples.Zip(samples.Skip(1)).Sum(pair =>
            Dist(pair.First.X, pair.First.Y, pair.Second.X, pair.Second.Y));
        return (Length(direct) <= Length(around)) != _otherWay ? direct : around;
    }

    public override bool PointerDown(CadSession s, PointerInfo p)
    {
        if (!p.Left) return false;
        if (FindTrace(s, p) is not { } hit) { s.Say(Prompt, true); return true; }
        if (_kind == CurveKind.Circle) Fit(s, [.. hit.Trace.Points], hit.Trace, null, null);
        else if (_start is { } first)
        {
            var span = Span(first, hit);
            // Span orders the picks along the trace; keep each snapped point with its own end
            var (startPoint, endPoint) = Same(span[0], first.Sample) ? (first.Point, hit.Point) : (hit.Point, first.Point);
            if (Fit(s, span, first.Trace, startPoint, endPoint)) _start = null;
        }
        else { _start = hit; s.Say(Prompt); }
        s.RaiseState();
        s.Invalidate();
        return true;
    }

    static bool Same(MeshSectionSample a, MeshSectionSample b) => Math.Abs(a.X - b.X) < 1e-12 && Math.Abs(a.Y - b.Y) < 1e-12;

    bool Fit(CadSession s, List<MeshSectionSample> samples, MeshSectionTrace trace, int? startPoint, int? endPoint)
    {
        var sk = Sk(s);
        var model = sk.Model;
        double tol = Tolerance(trace);
        var previous = _replace is int id ? model.EntityOrNull(id) : null;
        var ends = previous == null ? [] : model.EndpointsOf(previous).ToArray();
        if (previous != null)
        {
            if (HasCurveConstraint(model, previous.Id) || previous.Type is not (EntType.Line or EntType.Arc or EntType.Circle or EntType.Spline) ||
                (_kind == CurveKind.Circle ? previous.Type != EntType.Circle : ends.Length != 2))
            {
                s.Say("Choose a compatible curve without curve constraints, or clear the selection before opening Fit Section Curve.", true);
                return false;
            }
        }
        if (samples.Count < (_kind == CurveKind.Line ? 2 : 3))
        {
            s.Say("Choose a longer span on the measured section.", true);
            return false;
        }
        var pts = samples.Select(q => (x: q.X, y: q.Y)).ToList();

        sk.Push();
        int removed = 0, kept = 0;
        if (previous == null)
        {
            // curves lying along the span (or along the whole outline for a circle) give way to the fitted one
            var along = pts.ToList();
            if (_kind == CurveKind.Circle && trace.Closed) along.Add(along[0]);
            double spanLength = along.Zip(along.Skip(1)).Sum(q => Dist(q.First.x, q.First.y, q.Second.x, q.Second.y));
            double reach = Math.Max(Math.Max(tol * 4, 0.05), spanLength * 0.005);
            foreach (var e in model.Entities.Where(e => !e.Construction && e.Type is EntType.Line or EntType.Arc or EntType.Spline or EntType.Circle).ToList())
            {
                var curve = Samples(model, e);
                if (curve.Count == 0 || curve.Any(q => DistToPolyline(q, along) > reach)) continue;
                if (HasCurveConstraint(model, e.Id)) { kept++; continue; }
                model.RemoveEntity(e.Id);
                removed++;
            }
        }

        var a = pts[0]; var b = pts[^1];
        int? fixedA = null, fixedB = null;
        if (ends.Length == 2)
        {
            var p = model.P(ends[0]); var q = model.P(ends[1]);
            if (Dist(p.X, p.Y, b.x, b.y) + Dist(q.X, q.Y, a.x, a.y) < Dist(p.X, p.Y, a.x, a.y) + Dist(q.X, q.Y, b.x, b.y))
            {
                pts.Reverse();
                (a, b) = (b, a);
            }
            fixedA = ends[0]; fixedB = ends[1];
        }
        else if (_kind != CurveKind.Circle)
        {
            double pixel = PixelSize(s, sk, a.x, a.y);
            fixedA = Join(model, startPoint, a, Math.Max(tol * 4, SnapPixels * pixel));
            fixedB = Join(model, endPoint, b, Math.Max(tol * 4, SnapPixels * pixel));
            if (fixedA == fixedB) fixedB = null;
        }
        if (fixedA is int fa) a = (model.P(fa).X, model.P(fa).Y);
        if (fixedB is int fb) b = (model.P(fb).X, model.P(fb).Y);

        int Point((double x, double y) q) => model.AddPoint(q.x, q.y);
        int created;
        switch (_kind)
        {
            case CurveKind.Line:
            {
                if (fixedA == null || fixedB == null)
                {
                    // total least squares line through the span; free ends are projected onto it
                    var (c, d) = LineFit(pts);
                    if (fixedA == null) a = Project(a, c, d);
                    if (fixedB == null) b = Project(b, c, d);
                }
                created = model.AddLine(fixedA ?? Point(a), fixedB ?? Point(b));
                break;
            }
            case CurveKind.Circle:
            {
                if (LineArcFit.FitCircleFast(pts) is not { } full) return Fail(s, sk, "The outline does not define a circle.");
                created = model.AddCircle(model.AddPoint(full.cx, full.cy), full.r);
                break;
            }
            case CurveKind.Arc:
            {
                if (LineArcFit.FitArcThroughEnds(a, b, pts) is not { } arc) return Fail(s, sk, "The selected span does not define a circular curve.");
                int start = fixedA ?? Point(a), end = fixedB ?? Point(b), centre = model.AddPoint(arc.cx, arc.cy);
                var mid = pts[pts.Count / 2];
                double cross = (mid.x - a.x) * (b.y - mid.y) - (mid.y - a.y) * (b.x - mid.x);
                if (cross < 0) (start, end) = (end, start);
                created = model.AddArc(centre, start, end);
                break;
            }
            default:
            {
                var interior = SplineStations(pts, a, b, tol);
                var ids = new List<int> { fixedA ?? Point(a) };
                ids.AddRange(interior.Select(Point));
                ids.Add(fixedB ?? Point(b));
                created = model.AddSpline(ids, closed: false, control: false);
                break;
            }
        }
        if (previous != null) model.RemoveEntity(previous.Id);

        int tangents = 0;
        if (_tangent && _kind is CurveKind.Line or CurveKind.Arc)
            foreach (int end in model.EndpointsOf(model.E(created)).ToList())
                tangents += MakeTangent(model, created, end);

        _replace = null;
        sk.SelEntities.Clear();
        sk.Solve(s);
        s.NotifySketchChanged();

        double deviation = samples.Count == 0 ? 0 : Samples(model, model.E(created)) is { Count: > 1 } fitted
            ? pts.Max(q => DistToPolyline(q, fitted)) : 0;
        var parts = new List<string> { $"Largest deviation {deviation:0.###} mm (tolerance {tol:0.###} mm)" };
        if (removed > 0) parts.Add($"replaced {removed} curve{(removed == 1 ? "" : "s")}");
        if (kept > 0) parts.Add($"kept {kept} constrained curve{(kept == 1 ? "" : "s")}");
        if (tangents > 0) parts.Add($"{tangents} tangent constraint{(tangents == 1 ? "" : "s")}");
        _last = string.Join("; ", parts) + ".";
        s.Say("Curve fitted. " + _last + " Use the sketch's Dimension tool to set exact values.");
        return true;
    }

    bool Fail(CadSession s, SketchContext sk, string message)
    {
        sk.TryUndo(s);
        s.Say(message, true);
        return false;
    }

    static bool HasCurveConstraint(SketchModel model, int entity) => model.Constraints.Any(c =>
    {
        var pointRefs = SketchModel.RefIsPoint(c.Type, c.Refs.Length);
        return !c.Hidden && c.Refs.Where((_, i) => !pointRefs[i]).Contains(entity);
    });

    /// <summary>The existing point a span end should join: the one the user clicked, or a remaining curve end within reach.</summary>
    static int? Join(SketchModel model, int? clicked, (double x, double y) at, double reach)
    {
        var live = Endpoints(model).ToHashSet();
        if (clicked is int c && live.Contains(c)) return c;
        int? best = null; double bd = reach;
        foreach (int id in live)
        {
            var p = model.P(id); double d = Dist(p.X, p.Y, at.x, at.y);
            if (d < bd) { bd = d; best = id; }
        }
        return best;
    }

    /// <summary>Adds a tangent constraint between the new curve and each line or arc that continues smoothly through the point.</summary>
    internal static int MakeTangent(SketchModel model, int created, int point)
    {
        var mine = model.E(created);
        var dir = Outgoing(model, mine, point);
        if (dir == null) return 0;
        int added = 0;
        foreach (var other in model.Entities.Where(e => e.Id != created && !e.Construction && model.EndpointsOf(e).Contains(point)).ToList())
        {
            if (mine.Type == EntType.Line && other.Type == EntType.Line) continue;
            if (other.Type is not (EntType.Line or EntType.Arc)) continue;
            if (Outgoing(model, other, point) is not { } od) continue;
            // smooth when the two curves leave the point in opposite directions
            double cos = -(dir.Value.x * od.x + dir.Value.y * od.y);
            if (cos < Math.Cos(TangentAngle * Math.PI / 180)) continue;
            if (model.Constraints.Any(c => c.Type == CType.Tangent && c.Refs.Contains(created) && c.Refs.Contains(other.Id))) continue;
            double sense = 1;
            if (mine.Type == EntType.Arc && other.Type == EntType.Arc)
            {
                var ca = model.P(mine.A); var cb = model.P(other.A);
                double d = Dist(ca.X, ca.Y, cb.X, cb.Y), ra = Radius(model, mine), rb = Radius(model, other);
                sense = Math.Abs(d - (ra + rb)) <= Math.Abs(d - Math.Abs(ra - rb)) ? 1 : -1;
            }
            model.Add(CType.Tangent, sense, refs: [created, other.Id]);
            added++;
        }
        return added;
    }

    static double Radius(SketchModel model, SkEntity arc) { var c = model.P(arc.A); var s = model.P(arc.B); return Dist(c.X, c.Y, s.X, s.Y); }

    /// <summary>Unit direction in which a line or arc leaves one of its end points.</summary>
    static (double x, double y)? Outgoing(SketchModel model, SkEntity e, int point)
    {
        var p = model.P(point);
        (double, double)? Unit(double x, double y) { double l = Math.Sqrt(x * x + y * y); return l < 1e-12 ? null : (x / l, y / l); }
        switch (e.Type)
        {
            case EntType.Line:
            {
                var o = model.P(e.A == point ? e.B : e.A);
                return Unit(o.X - p.X, o.Y - p.Y);
            }
            case EntType.Arc:
            {
                // arcs run counter-clockwise from B to C
                var c = model.P(e.A); double rx = p.X - c.X, ry = p.Y - c.Y;
                return point == e.B ? Unit(-ry, rx) : point == e.C ? Unit(ry, -rx) : null;
            }
            default: return null;
        }
    }

    /// <summary>2D points along a sketch curve.</summary>
    static List<(double x, double y)> Samples(SketchModel model, SkEntity e)
    {
        var res = new List<(double x, double y)>();
        switch (e.Type)
        {
            case EntType.Line:
            {
                var a = model.P(e.A); var b = model.P(e.B);
                for (int i = 0; i <= 8; i++) res.Add((a.X + (b.X - a.X) * i / 8, a.Y + (b.Y - a.Y) * i / 8));
                break;
            }
            case EntType.Circle:
            {
                var c = model.P(e.A);
                for (int i = 0; i <= 64; i++) res.Add((c.X + e.R * Math.Cos(i * Math.PI / 32), c.Y + e.R * Math.Sin(i * Math.PI / 32)));
                break;
            }
            case EntType.Arc:
            {
                var c = model.P(e.A); var s = model.P(e.B); var t = model.P(e.C);
                double r = Dist(c.X, c.Y, s.X, s.Y), a0 = Math.Atan2(s.Y - c.Y, s.X - c.X), a1 = Math.Atan2(t.Y - c.Y, t.X - c.X);
                double sweep = a1 - a0; while (sweep <= 0) sweep += 2 * Math.PI;
                for (int i = 0; i <= 32; i++) res.Add((c.X + r * Math.Cos(a0 + sweep * i / 32), c.Y + r * Math.Sin(a0 + sweep * i / 32)));
                break;
            }
            case EntType.Spline:
                res.AddRange(model.SplineSamples(e));
                break;
        }
        return res;
    }

    static double DistToPolyline((double x, double y) p, List<(double x, double y)> line)
    {
        double best = double.MaxValue;
        for (int i = 0; i + 1 < line.Count; i++)
        {
            var a = line[i]; var b = line[i + 1];
            double dx = b.x - a.x, dy = b.y - a.y, l2 = dx * dx + dy * dy;
            double f = l2 < 1e-24 ? 0 : Math.Clamp(((p.x - a.x) * dx + (p.y - a.y) * dy) / l2, 0, 1);
            best = Math.Min(best, Dist(p.x, p.y, a.x + dx * f, a.y + dy * f));
        }
        return line.Count == 1 ? Dist(p.x, p.y, line[0].x, line[0].y) : best;
    }

    static ((double x, double y) c, (double x, double y) d) LineFit(List<(double x, double y)> pts)
    {
        double mx = pts.Average(p => p.x), my = pts.Average(p => p.y), sxx = 0, sxy = 0, syy = 0;
        foreach (var p in pts) { double dx = p.x - mx, dy = p.y - my; sxx += dx * dx; sxy += dx * dy; syy += dy * dy; }
        double th = 0.5 * Math.Atan2(2 * sxy, sxx - syy);
        return ((mx, my), (Math.Cos(th), Math.Sin(th)));
    }

    static (double x, double y) Project((double x, double y) p, (double x, double y) c, (double x, double y) d)
    {
        double t = (p.x - c.x) * d.x + (p.y - c.y) * d.y;
        return (c.x + d.x * t, c.y + d.y * t);
    }

    /// <summary>
    /// Interior points of a through-points spline that follows the samples within the tolerance with as few points as possible.
    /// Each candidate count starts from local averages (which cancel noise) and is then corrected a few times by the mean offset
    /// of the nearby samples from the curve, so the result is a least-squares fit rather than a curve through raw samples.
    /// </summary>
    internal static List<(double x, double y)> SplineStations(List<(double x, double y)> input, (double x, double y) a, (double x, double y) b, double tol)
    {
        int step = Math.Max(1, input.Count / 1500);
        var pts = input.Where((_, i) => i % step == 0).ToList();
        var s = new double[pts.Count];
        for (int i = 1; i < pts.Count; i++) s[i] = s[i - 1] + Dist(pts[i - 1].x, pts[i - 1].y, pts[i].x, pts[i].y);
        double length = s[^1];
        if (length < 1e-9) return [];
        List<(double x, double y)>? best = null; double bestDev = double.MaxValue;
        for (int m = 1; m <= 16; m++)
        {
            double gap = length / (m + 1);
            var stations = new List<(double x, double y)>();
            var windows = new List<int[]>();
            for (int j = 1; j <= m; j++)
            {
                double at = gap * j;
                var near = Enumerable.Range(0, pts.Count).Where(i => Math.Abs(s[i] - at) <= gap / 2).ToArray();
                if (near.Length == 0) near = [Enumerable.Range(0, pts.Count).MinBy(i => Math.Abs(s[i] - at))];
                windows.Add(near);
                stations.Add((near.Average(i => pts[i].x), near.Average(i => pts[i].y)));
            }
            double dev = 0;
            for (int pass = 0; pass < 4; pass++)
            {
                var curve = Curve(a, stations, b);
                if (curve == null) break;
                dev = pts.Max(q => DistToPolyline(q, curve));
                if (pass == 3) break;
                for (int j = 0; j < m; j++)
                {
                    double ox = 0, oy = 0;
                    foreach (int i in windows[j])
                    {
                        var c = Closest(pts[i], curve);
                        ox += pts[i].x - c.x; oy += pts[i].y - c.y;
                    }
                    stations[j] = (stations[j].x + ox / windows[j].Length, stations[j].y + oy / windows[j].Length);
                }
            }
            if (dev < bestDev) { bestDev = dev; best = [.. stations]; }
            if (dev <= tol) break;
        }
        return best ?? [];
    }

    static List<(double x, double y)>? Curve((double x, double y) a, List<(double x, double y)> stations, (double x, double y) b)
    {
        try
        {
            var through = new List<Vec3> { new(a.x, a.y, 0) };
            through.AddRange(stations.Select(q => new Vec3(q.x, q.y, 0)));
            through.Add(new Vec3(b.x, b.y, 0));
            using var spline = Occ.Spline(through, false);
            return Occ.CurveSamples(spline, 40 * (stations.Count + 1)).SelectMany(e => e).Select(q => (q.p.X, q.p.Y)).ToList();
        }
        catch (KernelException) { return null; }
    }

    static (double x, double y) Closest((double x, double y) p, List<(double x, double y)> line)
    {
        (double x, double y) best = line[0]; double bd = double.MaxValue;
        for (int i = 0; i + 1 < line.Count; i++)
        {
            var a = line[i]; var b = line[i + 1];
            double dx = b.x - a.x, dy = b.y - a.y, l2 = dx * dx + dy * dy;
            double f = l2 < 1e-24 ? 0 : Math.Clamp(((p.x - a.x) * dx + (p.y - a.y) * dy) / l2, 0, 1);
            var q = (a.x + dx * f, a.y + dy * f); double d = Dist(p.x, p.y, q.Item1, q.Item2);
            if (d < bd) { bd = d; best = q; }
        }
        return best;
    }

    public override void BuildOverlay(CadSession s, Overlay o)
    {
        if (_start is { } a) o.AddDot(W(Sk(s), a.Sample.X, a.Sample.Y), a.Point != null ? 0xFF36C978 : 0xFFFFCB81, 9);
        if (_hover is { } b)
        {
            o.AddDot(W(Sk(s), b.Sample.X, b.Sample.Y), b.Point != null ? 0xFF36C978 : 0xFFFFCB81, 7);
            if (_start is { } first && ReferenceEquals(first.Trace, b.Trace))
                o.AddPolyline(Span(first, b).Select(p => W(Sk(s), p.X, p.Y)).ToArray(), 0xFFFFCB81, 3.5f);
        }
    }
}
