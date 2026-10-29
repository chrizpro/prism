using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core.Sketching;
using Prism.Kernel;

namespace Prism.Core;

// =============================================================== Arc
public enum ArcMode { ThreePoint, CenterStartEnd, Tangent }

/// <summary>
/// Three-point arcs:  start, end, then pull the curve out to the side you want (the chord is on screen the whole time).
/// Center: center, start, then sweep round to the end: the direction follows how the pointer travels, so it never flips by itself.
/// Tangent: from the end of an existing line or arc.
/// </summary>
public sealed class ArcTool(ArcMode mode) : ClickTool
{
    protected override SnapInfo Constrain(CadSession s,SnapInfo sn)
        =>Shift&&Clicks.Count>0&&(Clicks.Count==1||mode==ArcMode.CenterStartEnd)?LockDirection(sn,Clicks[0]):sn;
    public override string Name => "Arc";
    public override string Mode => mode switch { ArcMode.ThreePoint => "3-point", ArcMode.CenterStartEnd => "Center", _ => "Tangent" };
    static readonly ToolOption[] Opts = [new("3-Point", "Start, end, then pull the arc out to the side you want", "arc"), new("Center", "Center, start, then sweep round to the end", "arc-center"), new("Tangent", "Continue tangent from the end of a line or arc", "arc-tangent")];
    public override IReadOnlyList<ToolOption> Options => Opts;
    public override int OptionIndex => (int)mode;
    protected override void ApplyOption(int index) => mode = (ArcMode)index;
    public override string Prompt => mode switch
    {
        ArcMode.ThreePoint => "Arc (3-point): click the start, click the end, then move out to bend the arc and click. Type a radius for an exact one.",
        ArcMode.CenterStartEnd => "Arc (center): click the center, click the start, then sweep round to the end and click. Type the sweep angle for an exact one.",
        _ => "Arc (tangent): click the end of a line or arc, then click where the new arc ends.",
    };

    // sweep tracking for the Center mode
    double _sweep, _lastRaw; bool _sweepInit;
    protected override void ResetState(CadSession s) { _sweepInit = false; _sweep = 0; base.ResetState(s); }
    static double Wrap(double a) { while (a > Math.PI) a -= 2 * Math.PI; while (a <= -Math.PI) a += 2 * Math.PI; return a; }

    public override void PointerMove(CadSession s, PointerInfo p)
    {
        base.PointerMove(s, p);
        if (mode != ArcMode.CenterStartEnd || Clicks.Count != 2 || Hot is not { } h) return;
        var c = Clicks[0]; double a0 = Math.Atan2(Clicks[1].V - c.V, Clicks[1].U - c.U), raw = Math.Atan2(h.V - c.V, h.U - c.U);
        if (!_sweepInit) { _sweep = Wrap(raw - a0); _sweepInit = true; }
        else _sweep += Wrap(raw - _lastRaw);
        _lastRaw = raw;
        _sweep = Math.Clamp(_sweep, -2 * Math.PI + 1e-3, 2 * Math.PI - 1e-3);
    }

    // ---- typed entry
    protected override TypedField[]? TypedFields(CadSession s) => mode switch
    {
        ArcMode.CenterStartEnd => Clicks.Count == 1 ? [new("Radius", " mm"), new("Angle", "°")] : Clicks.Count == 2 ? [new("Sweep", "°")] : null,
        ArcMode.ThreePoint => Clicks.Count == 2 ? [new("Radius", " mm")] : null,
        _ => null,
    };

    protected override double[]? LiveValues(CadSession s, SnapInfo h)
    {
        if (Clicks.Count == 0) return null;
        if (mode == ArcMode.CenterStartEnd)
        {
            var c = Clicks[0];
            if (Clicks.Count == 1) { var (l, g) = Polar((c.U, c.V), h); return [l, g]; }
            return [Math.Abs(_sweep) * 180 / Math.PI];
        }
        if (mode == ArcMode.ThreePoint && Clicks.Count == 2 && Circum(Clicks[0].U, Clicks[0].V, Clicks[1].U, Clicks[1].V, h.U, h.V) is { } k) return [k.r];
        return null;
    }

    protected override SnapInfo? TypedPoint(CadSession s, SnapInfo h, double[] v)
    {
        if (Clicks.Count == 0) return null;
        if (mode == ArcMode.CenterStartEnd)
        {
            var c = Clicks[0];
            if (Clicks.Count == 1) return PolarPoint((c.U, c.V), h, v[0], Typed(v, 1, double.NaN));
            double deg = Typed(v, 0, Math.Abs(_sweep) * 180 / Math.PI); if (deg < 0.01 || deg > 359.99) return null;
            double a0 = Math.Atan2(Clicks[1].V - c.V, Clicks[1].U - c.U), r = Dist(c.U, c.V, Clicks[1].U, Clicks[1].V);
            _sweep = (_sweep < 0 ? -1 : 1) * deg * Math.PI / 180; _sweepInit = true;
            return new SnapInfo(c.U + r * Math.Cos(a0 + _sweep), c.V + r * Math.Sin(a0 + _sweep), SnapKind.Grid, -1);
        }
        if (mode == ArcMode.ThreePoint && Clicks.Count == 2)
        {
            var (n, side) = Side(Clicks[0], Clicks[1], h);
            double chord = Dist(Clicks[0].U, Clicks[0].V, Clicks[1].U, Clicks[1].V), half = chord / 2;
            double R = Math.Max(Typed(v, 0, double.NaN), half + 1e-6); if (double.IsNaN(R)) return null;
            double sag = R - Math.Sqrt(Math.Max(R * R - half * half, 0));             // the shallow arc
            double sg = side < 0 ? -1 : 1;
            double mx = (Clicks[0].U + Clicks[1].U) / 2 + n.u * sag * sg, my = (Clicks[0].V + Clicks[1].V) / 2 + n.v * sag * sg;
            return new SnapInfo(mx, my, SnapKind.Grid, -1);
        }
        return null;
    }

    /// <summary>Tangent mode: the start must be an existing end of a line/arc.</summary>
    (int entity, double dirU, double dirV)? TangentSource(SketchContext sk, int pointId)
    {
        foreach (var e in sk.Model.Entities)
        {
            if (e.Type == EntType.Line && (e.A == pointId || e.B == pointId))
            {
                var a = sk.Model.P(e.A); var b = sk.Model.P(e.B);
                double dx = e.B == pointId ? b.X - a.X : a.X - b.X, dy = e.B == pointId ? b.Y - a.Y : a.Y - b.Y, l = Math.Max(Dist(0, 0, dx, dy), 1e-9);
                return (e.Id, dx / l, dy / l);
            }
            if (e.Type == EntType.Arc && (e.B == pointId || e.C == pointId))
            {
                var c = sk.Model.P(e.A); var p = sk.Model.P(pointId);
                double rx = p.X - c.X, ry = p.Y - c.Y, l = Math.Max(Dist(0, 0, rx, ry), 1e-9);
                double tx = -ry / l, ty = rx / l; if (e.B == pointId) { tx = -tx; ty = -ty; }   // leave the arc backwards at its start, forwards at its end
                return (e.Id, tx, ty);
            }
        }
        return null;
    }

    protected override void Click(CadSession s, SketchContext sk, SnapInfo sn)
    {
        if (mode == ArcMode.Tangent && Clicks.Count == 0 && (sn.Kind != SnapKind.Point || TangentSource(sk, sn.Id) == null))
        { s.Say("Start a tangent arc from the end of an existing line or arc.", true); return; }
        Clicks.Add(sn);
        if (mode == ArcMode.CenterStartEnd && Clicks.Count == 2) { _sweepInit = false; return; }
        if (Clicks.Count < (mode == ArcMode.Tangent ? 2 : 3)) return;
        var c = Clicks.ToList(); Clicks.Clear();

        if (mode == ArcMode.ThreePoint)
        {
            // start, end, then a point the arc passes through
            if (Circum(c[0].U, c[0].V, c[1].U, c[1].V, c[2].U, c[2].V) is not { } k) { s.Say("Those three points are in a line.", true); return; }
            double a0 = Math.Atan2(c[0].V - k.cy, c[0].U - k.cx), am = Math.Atan2(c[2].V - k.cy, c[2].U - k.cx), a1 = Math.Atan2(c[1].V - k.cy, c[1].U - k.cx);
            bool ccw = Norm(am - a0) < Norm(a1 - a0);                 // counter-clockwise start->end must pass the third point; otherwise swap
            var sSnap = ccw ? c[0] : c[1]; var eSnap = ccw ? c[1] : c[0];
            sk.Edit(s, "That arc", () =>
            {
                int ctr = sk.Model.AddPoint(k.cx, k.cy);
                int sp = sk.PointFor(sSnap), ep = sk.PointFor(eSnap);
                int arc = sk.Model.AddArc(ctr, sp, ep, sk.Construction);
                if (c[2].Kind == SnapKind.Point) sk.Model.Add(CType.PointOnEntity, 0, 0, false, c[2].Id, arc);
            });
        }
        else if (mode == ArcMode.CenterStartEnd)
        {
            double r = Dist(c[0].U, c[0].V, c[1].U, c[1].V); if (r < 1e-6) { _sweepInit = false; return; }
            double a0 = Math.Atan2(c[1].V - c[0].V, c[1].U - c[0].U), a1 = Math.Atan2(c[2].V - c[0].V, c[2].U - c[0].U);
            double sweep = _sweepInit ? _sweep : (Norm(a1 - a0) <= Math.PI ? Norm(a1 - a0) : -Norm(a0 - a1));      // scripts: the short way round
            _sweepInit = false;
            if (Math.Abs(sweep) < 1e-4) return;
            double aEnd = a0 + sweep;
            var endPt = new SnapInfo(c[0].U + r * Math.Cos(aEnd), c[0].V + r * Math.Sin(aEnd), SnapKind.Grid, -1);
            if (c[2].Kind == SnapKind.Point && Math.Abs(Wrap(Math.Atan2(c[2].V - c[0].V, c[2].U - c[0].U) - aEnd)) < 1e-6) endPt = c[2];
            bool ccw = sweep > 0;
            var startSnap = ccw ? c[1] : endPt; var endSnap = ccw ? endPt : c[1];
            sk.Edit(s, "That arc", () =>
            {
                int ctr = sk.PointFor(c[0]);
                int sp = sk.PointFor(startSnap), ep = sk.PointFor(endSnap);
                sk.Model.AddArc(ctr, sp, ep, sk.Construction);
            });
        }
        else
        {
            if (TangentSource(sk, c[0].Id) is not { } src) return;
            if (TangentCenter(c[0], src.dirU, src.dirV, c[1]) is not { } cen) { s.Say("The end point is in line with the tangent; pick a point off to the side.", true); return; }
            double tcx = -(c[0].V - cen.v), tcy = c[0].U - cen.u;          // counter-clockwise tangent at the start
            bool ccw = tcx * src.dirU + tcy * src.dirV > 0;
            sk.Edit(s, "That tangent arc", () =>
            {
                int ctr = sk.Model.AddPoint(cen.u, cen.v);
                int sp = c[0].Id, ep = sk.PointFor(c[1]);
                int arc = ccw ? sk.Model.AddArc(ctr, sp, ep, sk.Construction) : sk.Model.AddArc(ctr, ep, sp, sk.Construction);
                sk.Model.Add(CType.Tangent, 1, 0, false, src.entity, arc);
            });
        }
    }

    static (double u, double v)? TangentCenter(SnapInfo start, double dirU, double dirV, SnapInfo end)
    {
        double nx = -dirV, ny = dirU, ex = end.U - start.U, ey = end.V - start.V;
        double den = 2 * (nx * ex + ny * ey);
        if (Math.Abs(den) < 1e-9) return null;
        double t = (ex * ex + ey * ey) / den;
        return (start.U + nx * t, start.V + ny * t);
    }

    public override void BuildOverlay(CadSession s, Overlay o)
    {
        var sk = Sk(s);
        if (Clicks.Count == 0 || Hot is not { } h) return;
        foreach (var c in Clicks) o.AddDot(W(sk, c.U, c.V), 0xFFFF8A1F, 8);
        if (mode == ArcMode.ThreePoint)
        {
            var a = Clicks[0];
            if (Clicks.Count == 1) { o.AddLine(W(sk, a.U, a.V), W(sk, h.U, h.V), Rubber, 1.5f); o.AddLabel(W(sk, h.U, h.V), $"{Dist(a.U, a.V, h.U, h.V):0.##} mm"); return; }
            var b = Clicks[1];
            o.AddLine(W(sk, a.U, a.V), W(sk, b.U, b.V), 0x607AA7E8, 1.2f);                          // the chord stays on screen
            if (Circum(a.U, a.V, b.U, b.V, h.U, h.V) is { } k)
            {
                double a0 = Math.Atan2(a.V - k.cy, a.U - k.cx), am = Math.Atan2(h.V - k.cy, h.U - k.cx), a1 = Math.Atan2(b.V - k.cy, b.U - k.cx);
                bool ccw = Norm(am - a0) < Norm(a1 - a0);
                double sw = ccw ? Norm(a1 - a0) : -Norm(a0 - a1);
                ArcPreview(o, sk, k.cx, k.cy, k.r, a0, sw, Rubber);
                o.AddLine(W(sk, k.cx, k.cy), W(sk, a.U, a.V), 0x307AA7E8, 1f); o.AddLine(W(sk, k.cx, k.cy), W(sk, b.U, b.V), 0x307AA7E8, 1f);
                o.AddLabel(W(sk, h.U, h.V), $"R {k.r:0.##}   {Math.Abs(sw) * 180 / Math.PI:0.#}°");
            }
        }
        else if (mode == ArcMode.CenterStartEnd)
        {
            var c = Clicks[0];
            if (Clicks.Count == 1) { o.AddLine(W(sk, c.U, c.V), W(sk, h.U, h.V), Rubber, 1.5f); Ring(o, sk, c.U, c.V, Dist(c.U, c.V, h.U, h.V), 0x407AA7E8, 1f); return; }
            double r = Dist(c.U, c.V, Clicks[1].U, Clicks[1].V);
            double a0 = Math.Atan2(Clicks[1].V - c.V, Clicks[1].U - c.U);
            Ring(o, sk, c.U, c.V, r, 0x30 << 24 | 0x7AA7E8, 1f);
            if (!_sweepInit) { o.AddLine(W(sk, c.U, c.V), W(sk, Clicks[1].U, Clicks[1].V), 0x907AA7E8, 1.2f); return; }
            ArcPreview(o, sk, c.U, c.V, r, a0, _sweep, Rubber);
            o.AddLine(W(sk, c.U, c.V), W(sk, Clicks[1].U, Clicks[1].V), 0x907AA7E8, 1.2f);
            o.AddLine(W(sk, c.U, c.V), W(sk, c.U + r * Math.Cos(a0 + _sweep), c.V + r * Math.Sin(a0 + _sweep)), 0x907AA7E8, 1.2f);
            o.AddLabel(W(sk, h.U, h.V), $"R {r:0.##}   {Math.Abs(_sweep) * 180 / Math.PI:0.#}°");
        }
        else if (TangentSource(sk, Clicks[0].Id) is { } src && TangentCenter(Clicks[0], src.dirU, src.dirV, h) is { } cc)
        {
            double r = Dist(cc.u, cc.v, Clicks[0].U, Clicks[0].V);
            double a0 = Math.Atan2(Clicks[0].V - cc.v, Clicks[0].U - cc.u), a1 = Math.Atan2(h.V - cc.v, h.U - cc.u);
            double tcx = -(Clicks[0].V - cc.v), tcy = Clicks[0].U - cc.u;
            bool ccw = tcx * src.dirU + tcy * src.dirV > 0;
            ArcPreview(o, sk, cc.u, cc.v, r, a0, ccw ? Norm(a1 - a0) : -Norm(a0 - a1), Rubber);
            o.AddLabel(W(sk, h.U, h.V), $"R {r:0.##}");
        }
    }
}

// =============================================================== Ellipse
public enum EllipseMode { Center, TwoPoint, ThreePoint }

/// <summary>
/// Ellipses three ways. Center: center, one axis end, the other radius. 2-point: two opposite corners of the box it fits in.
/// 3-point: the two ends of the long axis, then a point for the other radius. Shift (or L) locks the proportions into a circle.
/// </summary>
public sealed class EllipseTool(EllipseMode mode, bool circleLock = false) : ClickTool
{
    protected override SnapInfo Constrain(CadSession s,SnapInfo sn)=>sn;
    public override string Name => "Ellipse";
    public override string Mode => mode switch { EllipseMode.Center => "Ellipse (center)", EllipseMode.TwoPoint => "Ellipse (2-point)", _ => "Ellipse (3-point)" };
    static readonly ToolOption[] Opts = [new("Center", "Center, the end of one axis, then the other radius", "ellipse"), new("2-Point", "Two opposite corners of the box the ellipse fits in", "rect"), new("3-Point", "The two ends of the long axis, then a point for the other radius", "ellipse")];
    public override IReadOnlyList<ToolOption> Options => Opts;
    public override int OptionIndex => (int)mode;
    protected override void ApplyOption(int index) => mode = (EllipseMode)index;
    public override string Prompt => Locked ? mode switch
    {
        EllipseMode.Center => "Ellipse (circle locked): click the center, then a radius endpoint. L unlocks the proportions.",
        EllipseMode.TwoPoint => "Ellipse (circle locked): click the two opposite diameter endpoints. L unlocks the proportions.",
        _ => "Ellipse (circle locked): click three points on the circumference. L unlocks the proportions.",
    } : mode switch
    {
        EllipseMode.Center => "Ellipse (center): click the center, the end of one axis (sets size and tilt), then a point for the other radius. Hold Shift or press L for a circle.",
        EllipseMode.TwoPoint => "Ellipse (2-point): click two opposite corners of the box it fits in. Hold Shift or press L for a circle.",
        _ => "Ellipse (3-point): click the two ends of the long axis, then a point for the other radius. Hold Shift or press L for a circle through three points.",
    };

    bool _lock = circleLock;
    public bool CircleLocked => _lock;
    bool Locked => _lock || Shift;
    protected override void ResetState(CadSession s) { base.ResetState(s); }
    public override IReadOnlyList<ToolControl>? Panel => [..base.Panel!,new(ToolControlKind.Toggle, "circle", "Lock proportions to a circle", Text: "Hold Shift temporarily, or press L to toggle. Center: center and radius. 2-point: diameter ends. 3-point: three points on the circle.", On: _lock)];
    public override void PanelSet(CadSession s, string id, object? value) { if (id == "circle") { _lock = value is true; s.RaiseState(); s.Invalidate(); } else base.PanelSet(s,id,value); }

    public override bool Key(CadSession s, string key)
    {
        if (key is "Char:L" or "Char:l")
        {
            _lock = !_lock;
            s.Say(_lock ? "Proportions locked: it is a circle. Press L again to unlock." : "Proportions unlocked: draw any ellipse.");
            s.RaiseState(); s.Invalidate(); return true;
        }
        return base.Key(s, key);
    }
    protected override string? ExtraHud(CadSession s) => Locked ? "Circle proportions locked (L)" : "L or Shift: circle proportions";

    static double MinorRadius(SnapInfo ctr, SnapInfo axis, double hu, double hv)
    {
        double dx = axis.U - ctr.U, dy = axis.V - ctr.V, l = Math.Max(Dist(0, 0, dx, dy), 1e-12);
        return Math.Abs((hu - ctr.U) * -dy / l + (hv - ctr.V) * dx / l);
    }

    /// <summary>Centre, the end of the long axis, minor radius, for the shape being drawn (null until enough is known).</summary>
    (double cx, double cy, double ex, double ey, double minor)? Fit(IReadOnlyList<SnapInfo> c, double hu, double hv)
    {
        switch (mode)
        {
            case EllipseMode.Center:
            {
                if (c.Count == 0) return null;
                if (c.Count == 1) return null;
                double minor = Locked ? Dist(c[0].U, c[0].V, c[1].U, c[1].V) : MinorRadius(c[0], c[1], hu, hv);
                return (c[0].U, c[0].V, c[1].U, c[1].V, minor);
            }
            case EllipseMode.TwoPoint:
            {
                if (c.Count == 0) return null;
                double x0 = c[0].U, y0 = c[0].V, w = hu - x0, h = hv - y0;
                if (Locked) { double m = Math.Max(Math.Abs(w), Math.Abs(h)); w = Math.Sign(w == 0 ? 1 : w) * m; h = Math.Sign(h == 0 ? 1 : h) * m; }
                double cx = x0 + w / 2, cy = y0 + h / 2, ax = Math.Abs(w) / 2, bx = Math.Abs(h) / 2;
                return ax >= bx ? (cx, cy, cx + ax, cy, bx) : (cx, cy, cx, cy + bx, ax);
            }
            default:
            {
                if (c.Count < 2) return null;
                double cx = (c[0].U + c[1].U) / 2, cy = (c[0].V + c[1].V) / 2;
                var ctr = new SnapInfo(cx, cy, SnapKind.Grid, -1);
                return (cx, cy, c[1].U, c[1].V, MinorRadius(ctr, c[1], hu, hv));
            }
        }
    }

    protected override TypedField[]? TypedFields(CadSession s)
    {
        if (Clicks.Count == 0) return null;
        return mode switch
        {
            EllipseMode.Center => Clicks.Count == 1 ? [new(Locked ? "Radius" : "Major radius", " mm"), new("Angle", "°")] : [new(Locked ? "Radius" : "Minor radius", " mm")],
            EllipseMode.TwoPoint => Locked ? [new("Diameter", " mm"), new("Angle", "°")] : [new("Width", " mm"), new("Height", " mm")],
            _ => Clicks.Count == 1 ? [new("Long axis", " mm"), new("Angle", "°")] : [new("Minor radius", " mm")],
        };
    }

    protected override double[]? LiveValues(CadSession s, SnapInfo h)
    {
        if (Clicks.Count == 0) return null; var a = Clicks[0];
        switch (mode)
        {
            case EllipseMode.Center: { var (l, g) = Polar((a.U, a.V), h); return Clicks.Count == 1 ? [l, g] : [Fit(Clicks, h.U, h.V)?.minor ?? 0]; }
            case EllipseMode.TwoPoint: { var (l,g)=Polar((a.U,a.V),h);return Locked?[l,g]:[Math.Abs(h.U-a.U),Math.Abs(h.V-a.V)]; }
            default: { var (l, g) = Polar((a.U, a.V), h); return Clicks.Count == 1 ? [l, g] : [Fit(Clicks, h.U, h.V)?.minor ?? 0]; }
        }
    }

    protected override SnapInfo? TypedPoint(CadSession s, SnapInfo h, double[] v)
    {
        if (Clicks.Count == 0) return null; var a = Clicks[0];
        if (mode == EllipseMode.TwoPoint)
        {
            if(Locked)return PolarPoint((a.U,a.V),h,v[0],Typed(v,1,double.NaN));
            double w = Typed(v, 0, Math.Abs(h.U - a.U)) * (h.U < a.U ? -1 : 1), hh = Typed(v, 1, Math.Abs(h.V - a.V)) * (h.V < a.V ? -1 : 1);
            return new SnapInfo(a.U + w, a.V + hh, SnapKind.Grid, -1);
        }
        if (Clicks.Count == 2)
        {
            var (cx, cy) = mode == EllipseMode.ThreePoint ? ((Clicks[0].U + Clicks[1].U) / 2, (Clicks[0].V + Clicks[1].V) / 2) : (a.U, a.V);
            var ctr = new SnapInfo(cx, cy, SnapKind.Grid, -1);
            double dx = Clicks[1].U - cx, dy = Clicks[1].V - cy, l = Math.Max(Dist(0, 0, dx, dy), 1e-12);
            double side = (h.U - cx) * -dy / l + (h.V - cy) * dx / l, m = Typed(v, 0, Math.Abs(side)) * (side < 0 ? -1 : 1);
            return new SnapInfo(cx + -dy / l * m, cy + dx / l * m, SnapKind.Grid, -1);
        }
        return PolarPoint((a.U, a.V), h, v[0], Typed(v, 1, double.NaN));
    }

    protected override void Click(CadSession s, SketchContext sk, SnapInfo sn)
    {
        Clicks.Add(sn);
        int need = mode == EllipseMode.TwoPoint ? 2 : mode == EllipseMode.Center && Locked ? 2 : 3;
        if (Clicks.Count < need) return;
        var c = Clicks.ToList(); Clicks.Clear();
        double hu = c[^1].U, hv = c[^1].V;
        if (Locked)
        {
            double cx = c[0].U, cy = c[0].V, r = Dist(c[0].U, c[0].V, c[1].U, c[1].V);
            if (mode == EllipseMode.TwoPoint) { cx = (c[0].U + c[1].U) / 2; cy = (c[0].V + c[1].V) / 2; r /= 2; }
            else if (mode == EllipseMode.ThreePoint)
            {
                if (Circum(c[0].U,c[0].V,c[1].U,c[1].V,c[2].U,c[2].V) is not { } circle) { s.Say("Choose three points that are not on one line.", true); return; }
                (cx,cy,r) = circle;
            }
            if (r < 1e-6) return;
            sk.Edit(s, "That circle", () => sk.Model.AddCircle(mode == EllipseMode.Center ? sk.PointFor(c[0]) : sk.Model.AddPoint(cx,cy), r, sk.Construction));
            return;
        }
        if (Fit(mode == EllipseMode.TwoPoint ? [c[0]] : c, hu, hv) is not { } sh || Dist(sh.cx, sh.cy, sh.ex, sh.ey) < 1e-6 || sh.minor < 1e-6) { s.Say("Both radii of an ellipse must be above zero.", true); return; }
        sk.Edit(s, "That ellipse", () =>
        {
            int ctr = mode == EllipseMode.Center ? sk.PointFor(c[0]) : sk.Model.AddPoint(sh.cx, sh.cy);
            int end = mode == EllipseMode.ThreePoint ? sk.PointFor(c[1]) : mode == EllipseMode.Center ? sk.PointFor(c[1]) : sk.Model.AddPoint(sh.ex, sh.ey);
            sk.Model.AddEllipse(ctr, end, sh.minor, sk.Construction);
        });
    }

    static List<Vec3> EllipsePts(SketchContext sk, double cx, double cy, double a, double b, double rot)
    {
        double cr = Math.Cos(rot), sr = Math.Sin(rot);
        return Enumerable.Range(0, 97).Select(i => { double t = i * 2 * Math.PI / 96; double x = a * Math.Cos(t), y = b * Math.Sin(t); return W(sk, cx + x * cr - y * sr, cy + x * sr + y * cr); }).ToList();
    }

    public override void BuildOverlay(CadSession s, Overlay o)
    {
        var sk = Sk(s);
        if (Clicks.Count == 0 || Hot is not { } h) return;
        foreach (var c in Clicks) o.AddDot(W(sk, c.U, c.V), 0xFFFF8A1F, 8);
        var a = Clicks[0];
        if (Locked && mode == EllipseMode.TwoPoint)
        { Ring(o,sk,(a.U+h.U)/2,(a.V+h.V)/2,Dist(a.U,a.V,h.U,h.V)/2,Rubber); o.AddLine(W(sk,a.U,a.V),W(sk,h.U,h.V),0x607AA7E8); return; }
        if (Locked && mode == EllipseMode.ThreePoint && Clicks.Count == 2 && Circum(a.U,a.V,Clicks[1].U,Clicks[1].V,h.U,h.V) is { } circle)
        { Ring(o,sk,circle.cx,circle.cy,circle.r,Rubber); return; }
        if (mode == EllipseMode.TwoPoint)
        {
            o.AddPolyline([W(sk, a.U, a.V), W(sk, h.U, a.V), W(sk, h.U, h.V), W(sk, a.U, h.V), W(sk, a.U, a.V)], 0x407AA7E8, 1f);          // the box it fits in
        }
        else if (Clicks.Count == 1)
        {
            o.AddLine(W(sk, a.U, a.V), W(sk, h.U, h.V), Rubber, 1.5f);
            if (mode == EllipseMode.Center && Locked) Ring(o, sk, a.U, a.V, Dist(a.U, a.V, h.U, h.V), Rubber);
            return;
        }
        if (Fit(Clicks, h.U, h.V) is not { } sh) return;
        double maj = Dist(sh.cx, sh.cy, sh.ex, sh.ey);
        if (maj < 1e-6 || sh.minor < 1e-6) return;
        o.AddPolyline(EllipsePts(sk, sh.cx, sh.cy, maj, sh.minor, Math.Atan2(sh.ey - sh.cy, sh.ex - sh.cx)), Rubber, 2f);
        o.AddLine(W(sk, sh.cx - (sh.ex - sh.cx), sh.cy - (sh.ey - sh.cy)), W(sk, sh.ex, sh.ey), 0x607AA7E8, 1.2f);
        o.AddLabel(W(sk, h.U, h.V), $"{maj:0.##} × {sh.minor:0.##} mm (radii)");
    }
}
