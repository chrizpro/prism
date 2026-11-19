using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core.Sketching;
using Prism.Kernel;
using Prism.Meshing;

namespace Prism.Core;

/// <summary>Fit editable sketch geometry to a span of the measured mesh cut.</summary>
public sealed class FitMeshSectionTool : SketchTool
{
    enum CurveKind { Line, Arc, Circle, Spline }
    readonly record struct TracePick(MeshSectionTrace Trace, int Segment, double Fraction, MeshSectionSample Sample)
    {
        public double Index => Segment + Fraction;
    }

    static readonly ToolOption[] Kinds = [
        new("Line", "Fit a straight span.", "line", "Curve"),
        new("Arc", "Fit a circular arc to a span.", "arc", "Curve"),
        new("Circle", "Fit a circle to a closed outline.", "circle", "Curve"),
        new("Spline", "Fit a smooth curve to a span.", "spline", "Curve")
    ];

    CurveKind _kind = (CurveKind)Recall("Fit Section Curve");
    TracePick? _start, _hover;
    bool _otherWay;
    int? _replace;

    public override string Name => "Fit Section Curve";
    public override IReadOnlyList<ToolOption> Options => Kinds;
    public override int OptionIndex => (int)_kind;
    public override string Prompt => _kind == CurveKind.Circle ? "Click a closed measured outline."
        : _start == null ? "Click the start of the span on the orange measured outline."
        : "Click the end of the span on the same outline.";
    public override IReadOnlyList<ToolControl> Panel =>
    [
        new(ToolControlKind.Info, "target", _replace.HasValue ? "Replacing the selected curve." : "Adding a curve. To replace one, use Edge to select it before opening this tool."),
        new(ToolControlKind.Toggle, "other-way", "Use longer span on a closed outline", On: _otherWay, Enabled: _kind != CurveKind.Circle)
    ];

    public override void Activate(CadSession s) => _replace = Sk(s).SelEntities is { Count: 1 } selection ? selection.Single() : null;
    public override void Deactivate(CadSession s) { ResetState(s); base.Deactivate(s); }
    protected override void ApplyOption(int index) => _kind = (CurveKind)index;
    protected override void ResetState(CadSession s) { _start = _hover = null; s.Invalidate(); }

    public override void PanelSet(CadSession s, string id, object? value)
    {
        if (id == "other-way" && value is bool on) { _otherWay = on; s.Invalidate(); }
    }

    public override void PointerMove(CadSession s, PointerInfo p)
    {
        _hover = FindTrace(s, p);
        s.Invalidate();
    }

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
                result = new TracePick(trace, i, fraction, new(a.X + (b.X - a.X) * fraction, a.Y + (b.Y - a.Y) * fraction));
            }
        }
        return result;
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
        if (_kind == CurveKind.Circle) Fit(s, [.. hit.Trace.Points]);
        else if (_start is { } first)
        {
            if (Fit(s, Span(first, hit))) _start = null;
        }
        else { _start = hit; s.Say(Prompt); }
        s.RaiseState();
        s.Invalidate();
        return true;
    }

    bool Fit(CadSession s, List<MeshSectionSample> samples)
    {
        var sk = Sk(s);
        var model = sk.Model;
        var previous = _replace is int id ? model.EntityOrNull(id) : null;
        var ends = previous == null ? [] : model.EndpointsOf(previous).ToArray();
        if (previous != null)
        {
            bool constrained = model.Constraints.Any(c =>
            {
                var pointRefs = SketchModel.RefIsPoint(c.Type, c.Refs.Length);
                return c.Refs.Where((_, i) => !pointRefs[i]).Contains(previous.Id);
            });
            if (constrained || previous.Type is not (EntType.Line or EntType.Arc or EntType.Circle or EntType.Spline) ||
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

        var a = samples[0]; var b = samples[^1];
        if (ends.Length == 2)
        {
            var p = model.P(ends[0]); var q = model.P(ends[1]);
            if (Dist(p.X, p.Y, b.X, b.Y) + Dist(q.X, q.Y, a.X, a.Y) < Dist(p.X, p.Y, a.X, a.Y) + Dist(q.X, q.Y, b.X, b.Y))
            {
                samples.Reverse();
                (a, b) = (b, a);
            }
            a = new(p.X, p.Y); b = new(q.X, q.Y);
        }

        var mid = samples[samples.Count / 2];
        var circle = _kind switch
        {
            CurveKind.Arc => Circum(a.X, a.Y, mid.X, mid.Y, b.X, b.Y),
            CurveKind.Circle => LineArcFit.FitCircleFast(samples.Select(q => (q.X, q.Y)).ToList()),
            _ => null
        };
        if (_kind is CurveKind.Arc or CurveKind.Circle && circle == null)
        {
            s.Say("The selected span does not define a circular curve.", true);
            return false;
        }

        sk.Push();
        int Point(MeshSectionSample q) => model.AddPoint(q.X, q.Y);
        int Start() => ends.Length == 2 ? ends[0] : Point(a);
        int End() => ends.Length == 2 ? ends[1] : Point(b);
        if (_kind == CurveKind.Line) model.AddLine(Start(), End());
        else if (_kind == CurveKind.Circle && circle is { } full)
            model.AddCircle(model.AddPoint(full.cx, full.cy), full.r);
        else if (_kind == CurveKind.Arc && circle is { } arc)
        {
            int start = Start(), end = End(), c = model.AddPoint(arc.cx, arc.cy);
            double cross = (mid.X - a.X) * (b.Y - mid.Y) - (mid.Y - a.Y) * (b.X - mid.X);
            if (cross < 0) (start, end) = (end, start);
            model.AddArc(c, start, end);
        }
        else
        {
            int divisions = Math.Min(12, samples.Count - 1);
            var ids = new List<int> { Start() };
            for (int i = 1; i < divisions; i++) ids.Add(Point(samples[i * (samples.Count - 1) / divisions]));
            ids.Add(End());
            model.AddSpline(ids, closed: false, control: false);
        }
        if (previous != null) model.RemoveEntity(previous.Id);
        _replace = null;
        sk.SelEntities.Clear();
        sk.Solve(s);
        s.NotifySketchChanged();
        s.Say("Curve fitted. Use the sketch’s Dimension tool to set exact values.");
        return true;
    }

    public override void BuildOverlay(CadSession s, Overlay o)
    {
        if (_start is { } a) o.AddDot(W(Sk(s), a.Sample.X, a.Sample.Y), 0xFFFFCB81, 9);
        if (_hover is { } b)
        {
            o.AddDot(W(Sk(s), b.Sample.X, b.Sample.Y), 0xFFFFCB81, 7);
            if (_start is { } first && ReferenceEquals(first.Trace, b.Trace))
                o.AddPolyline(Span(first, b).Select(p => W(Sk(s), p.X, p.Y)).ToArray(), 0xFFFFCB81, 3.5f);
        }
    }
}
