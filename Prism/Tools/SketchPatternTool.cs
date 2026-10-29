using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core.Sketching;
using Prism.Kernel;

namespace Prism.Core;

/// <summary>
/// Repeat sketch curves along a direction or around a point. Pick the curves (click, Shift-click for more, or select them beforehand),
/// then place the pattern with the mouse; + / - change the number of copies.
/// </summary>
public sealed class SketchPatternTool(bool circular) : SketchTool
{
    int _count = Remembered.GetValueOrDefault("SketchPatternCount", 3);
    readonly List<SnapInfo> _clicks = [];

    public override string Name => "Pattern";
    public override string Mode => _curve ? "Along curve" : circular ? "Circular" : "Linear";
    static readonly ToolOption[] Opts = [new("Linear", "Copies spaced along a direction", "pattern-linear"), new("Circular", "Copies spread around a center", "pattern-circular"), new("Along a curve", "Copies spaced along a line, arc or spline, turning with it", "pattern-curve")];
    public override IReadOnlyList<ToolOption> Options => Opts;
    public override int OptionIndex => _curve ? 2 : circular ? 1 : 0;
    protected override void ApplyOption(int index) { circular = index == 1; _curve = index == 2; }
    protected override void ResetState(CadSession s) { _selecting=true;_clicks.Clear(); _path = -1; }

    bool _curve, _follow = true, _shift;
    int _path = -1;
    int _lock;                      // 0 free, 1 horizontal, 2 vertical (linear pattern)

    public static SketchPatternTool Make(int option) => new(option == 1) { _curve = option == 2 };

    /// <summary>The second linear click, kept on the horizontal or vertical line through the first when locked (H / V keys, or hold Shift for the nearer axis).</summary>
    (double u, double v) Locked(SnapInfo a, double u, double v)
    {
        int l = _lock != 0 ? _lock : _shift ? (Math.Abs(u - a.U) >= Math.Abs(v - a.V) ? 1 : 2) : 0;
        return l == 1 ? (u, a.V) : l == 2 ? (a.U, v) : (u, v);
    }
    public override string? StepperLabel => "Total";
    public override int StepperValue => _count;
    public override void Step(CadSession s, int delta) { _count = Math.Clamp(_count + delta, 2, 99); Remembered["SketchPatternCount"] = _count; s.Say(Prompt); s.RaiseState(); s.Invalidate(); }

    public override string Prompt
    {
        get
        {
            string total = $"Total copies including the original: {_count} (press + / - to change).";
            if (!_selecting && _picked && _curve) return _path < 0 ? $"Curve pattern: click the curve to follow. {total}" : $"Click or press Enter to place the copies. R = {(_follow ? "turn with the curve (on)" : "keep orientation (off)")}. {total}";
            if (!_selecting && _picked && !circular) return (_clicks.Count == 0 ? $"Linear pattern: click a start point. {total}" : "Click where the first copy goes; the spacing is the distance between the two clicks.") + $" H / V lock horizontal or vertical{(_lock == 1 ? " (horizontal on)" : _lock == 2 ? " (vertical on)" : "")}; hold Shift for the nearer axis.";
            if (!_selecting && _picked) return circular
                ? (_clicks.Count == 0 ? $"Circular pattern: click the center. {total}" : "Click where the next copy goes (sets the angle between copies), or press Enter for a full circle.")
                : (_clicks.Count == 0 ? $"Linear pattern: click a start point. {total}" : "Click where the first copy goes; the spacing is the distance between the two clicks.");
            return "Pattern: click the curves to repeat (Shift adds more). Then press Enter or Done selecting to place. Esc starts over.";
        }
    }

    bool _picked,_selecting=true;
    public override IReadOnlyList<ToolControl>? Panel =>
    [new(ToolControlKind.Info,"stage",_selecting?"Select source curves (Shift adds). Then choose Done selecting.":Prompt),
     new(ToolControlKind.Number,"count","Total copies",Number:_count),
     new(ToolControlKind.Button,"selection",_selecting?"Done selecting (Enter)":"Change selection")];
    public override void PanelSet(CadSession s,string id,object? value)
    {
        if(id=="count"&&value is double count){_count=Math.Clamp((int)count,2,99);Remembered["SketchPatternCount"]=_count;s.Invalidate();return;}
        if(id=="selection")
        {if(_selecting&&Sk(s).SelEntities.Count==0){s.Say("Select source geometry first.",true);return;}_selecting=!_selecting;_picked=Sk(s).SelEntities.Count>0;_clicks.Clear();_path=-1;}
        s.Say(Prompt);s.RaiseState();s.Invalidate();
    }

    public override void Activate(CadSession s) { _selecting=true;_picked = Sk(s).SelEntities.Count > 0; _clicks.Clear(); s.Say(Prompt); }
    public override void Deactivate(CadSession s) { _clicks.Clear(); base.Deactivate(s); }

    public override bool Key(CadSession s, string key)
    {
        var sk = Sk(s);
        switch (key)
        {
            case "Char:H": case "Char:h": if (circular || _curve) return false; _lock = _lock == 1 ? 0 : 1; s.Say(Prompt); s.Invalidate(); return true;
            case "Char:V": case "Char:v": if (circular || _curve) return false; _lock = _lock == 2 ? 0 : 2; s.Say(Prompt); s.Invalidate(); return true;
            case "Char:R": case "Char:r": if (!_curve) return false; _follow = !_follow; s.Say(Prompt); s.Invalidate(); return true;
            case "Plus": Step(s, 1); return true;
            case "Minus": Step(s, -1); return true;
            case "Escape":
                if (_clicks.Count > 0) { _clicks.Clear(); s.Say(Prompt); s.Invalidate(); return true; }
                if (_picked) { _picked = false; _selecting = true; _path = -1; sk.SelEntities.Clear(); s.NotifySketchChanged(); s.Say(Prompt); s.Invalidate(); return true; }
                return false;
            case "Enter":
                if(_selecting){PanelSet(s,"selection",null);return true;}
                if (circular && _picked && _clicks.Count == 1) { Commit(s, sk, 2 * Math.PI / _count); return true; }
                if (_curve && _picked && _path >= 0) { CommitCurve(s, sk); return true; }
                return false;
            case "RightClick": return Key(s, "Escape") || true;
        }
        return false;
    }

    public override bool PointerDown(CadSession s, PointerInfo p)
    {
        if (!p.Left) return false;
        var sk = Sk(s);
        // choosing the curves to repeat
        if (_selecting)
        {
            int eid = sk.PickEntity(s, p.X, p.Y);
            if (eid >= 0)
            {
                if (!sk.SelEntities.Remove(eid)) sk.SelEntities.Add(eid);
                _picked = sk.SelEntities.Count > 0; _clicks.Clear();
                s.NotifySketchChanged(); s.Say(Prompt); s.Invalidate();
            }
            return true;
        }
        if (_curve)
        {
            if (_path >= 0) { CommitCurve(s, sk); return true; }
            int pe = sk.PickEntity(s, p.X, p.Y);
            if (pe < 0) return true;
            if (sk.SelEntities.Contains(pe)) { s.Say("Pick a curve that is not one of the curves being repeated.", true); return true; }
            _path = pe; s.Say(Prompt); s.Invalidate(); return true;
        }
        if (sk.Snap(s, p.X, p.Y, !p.Alt) is not { } sn) return true;
        _shift = p.Shift;
        if (!circular && _clicks.Count == 1) { var (lu, lv) = Locked(_clicks[0], sn.U, sn.V); sn = new SnapInfo(lu, lv, SnapKind.Grid, -1); }
        _clicks.Add(sn);
        if (_clicks.Count == 1) { s.Say(Prompt); s.Invalidate(); return true; }
        var a = _clicks[0];
        if (circular)
        {
            var (cu, cv) = Centroid(sk);
            double step = Norm(Math.Atan2(sn.V - a.V, sn.U - a.U) - Math.Atan2(cv - a.V, cu - a.U));
            if (step < 1e-6) { s.Say("Click away from the first copy to set the angle.", true); _clicks.RemoveAt(1); return true; }
            Commit(s, sk, step);
        }
        else
        {
            if (Dist(a.U, a.V, sn.U, sn.V) < 1e-6) { s.Say("Click a different point to set the spacing.", true); _clicks.RemoveAt(1); return true; }
            Commit(s, sk, 0, sn.U - a.U, sn.V - a.V);
        }
        return true;
    }

    public override void PointerMove(CadSession s, PointerInfo p)
    {
        var sk = Sk(s); _shift = p.Shift;
        if (!_selecting && _picked) { Hot = sk.Snap(s, p.X, p.Y, !p.Alt); sk.Cursor = Hot; if (Hot is { } h) s.Cursor3 = sk.Plane.PointAt(h.U, h.V); }
        else
        {
            int he = sk.PickEntity(s, p.X, p.Y); if (he != sk.HoverEntity) sk.HoverEntity = he;
            sk.Cursor = null;
            if (sk.PlanePoint(s, p.X, p.Y) is { } w) s.Cursor3 = sk.Plane.PointAt(w.u, w.v);
        }
        s.Invalidate();
    }

    static (double u, double v) Centroid(SketchContext sk)
    {
        var pts = sk.SelEntities.SelectMany(id => sk.Model.PointsOf(sk.Model.E(id))).Distinct().Select(i => sk.Model.P(i)).ToList();
        return pts.Count == 0 ? (0, 0) : (pts.Average(q => q.X), pts.Average(q => q.Y));
    }

    // ------------------------------------------------------------ copying
    /// <summary>The placement of copy k (1..count-1) as a transform of sketch coordinates.</summary>
    Func<double, double, (double, double)> Placement(int k, double step, double dx, double dy, double cu, double cv)
    {
        if (!circular) return (u, v) => (u + dx * k, v + dy * k);
        double a = step * k, c = Math.Cos(a), sn = Math.Sin(a);
        return (u, v) => (cu + (u - cu) * c - (v - cv) * sn, cv + (u - cu) * sn + (v - cv) * c);
    }

    /// <summary>Scripted pattern. Linear: (a,b) is the offset between copies. Circular: (a,b) is the center and c the angle between copies in radians (0 = evenly around a full circle).</summary>
    public static bool Run(CadSession s, int[] ids, bool circular, int count, double a, double b, double c = 0)
    {
        var sk = s.Sketch ?? throw new InvalidOperationException("No sketch is open.");
        var t = new SketchPatternTool(circular) { _count = Math.Clamp(count, 2, 99) };
        sk.SelEntities.Clear(); foreach (var id in ids) sk.SelEntities.Add(id);
        if (circular) { t._clicks.Add(new SnapInfo(a, b, SnapKind.Grid, -1)); t.Commit(s, sk, c > 1e-9 ? c : 2 * Math.PI / t._count); }
        else t.Commit(s, sk, 0, a, b);
        return true;
    }

    void Commit(CadSession s, SketchContext sk, double step, double dx = 0, double dy = 0)
    {
        double cu = _clicks.Count > 0 ? _clicks[0].U : 0, cv = _clicks.Count > 0 ? _clicks[0].V : 0;
        var sel = sk.SelEntities.ToList();
        int n = _count;
        bool ok = sk.Edit(s, "That pattern", () =>
        {
            for (int k = 1; k < n; k++) CopyOnce(sk.Model, sel, Placement(k, step, dx, dy, cu, cv));
        });
        _clicks.Clear();
        if (ok) { s.Say($"Pattern made: {n} copies in total."); sk.SelEntities.Clear(); _picked = false; _selecting = true; s.NotifySketchChanged(); }
        s.Invalidate();
    }

    // ------------------------------------------------------------ pattern along a curve
    /// <summary>The path sampled as a polyline in sketch coordinates, with the cumulative length at every point.</summary>
    (List<(double u, double v)> pts, List<double> cum, bool closed)? PathSamples(SketchContext sk)
    {
        var poly = sk.Model.Polylines(sk.Plane).FirstOrDefault(t => t.ent.Id == _path);
        if (poly.pts == null || poly.pts.Count < 2) return null;
        var pts = poly.pts.Select(q => sk.Plane.Project(q)).ToList();
        var cum = new List<double> { 0 };
        for (int i = 1; i < pts.Count; i++) cum.Add(cum[^1] + Dist(pts[i - 1].u, pts[i - 1].v, pts[i].u, pts[i].v));
        return (pts, cum, Dist(pts[0].u, pts[0].v, pts[^1].u, pts[^1].v) < 1e-6);
    }

    static (double u, double v, double ang) At((List<(double u, double v)> pts, List<double> cum, bool closed) p, double s)
    {
        double L = p.cum[^1]; if (p.closed) { s %= L; if (s < 0) s += L; } else s = Math.Clamp(s, 0, L);
        int i = 1; while (i < p.cum.Count - 1 && p.cum[i] < s) i++;
        double seg = Math.Max(p.cum[i] - p.cum[i - 1], 1e-12), t = (s - p.cum[i - 1]) / seg;
        var a = p.pts[i - 1]; var b = p.pts[i];
        return (a.u + (b.u - a.u) * t, a.v + (b.v - a.v) * t, Math.Atan2(b.v - a.v, b.u - a.u));
    }

    /// <summary>Transforms for copies 1..count-1 spaced along the path, starting from where the originals sit on it.</summary>
    List<Func<double, double, (double, double)>>? CurveTransforms(SketchContext sk)
    {
        if (PathSamples(sk) is not { } path) return null;
        var (cu, cv) = Centroid(sk);
        double s0 = 0, best = double.MaxValue;
        for (int i = 0; i < path.pts.Count; i++) { double d = Dist(path.pts[i].u, path.pts[i].v, cu, cv); if (d < best) { best = d; s0 = path.cum[i]; } }
        var start = At(path, s0);
        double L = path.cum[^1], spacing = path.closed ? L / _count : Math.Max(L - s0, 1e-9) / (_count - 1);
        var list = new List<Func<double, double, (double, double)>>();
        for (int k = 1; k < _count; k++)
        {
            var q = At(path, s0 + spacing * k);
            double tx = q.u - start.u, ty = q.v - start.v, da = _follow ? q.ang - start.ang : 0, c = Math.Cos(da), sn = Math.Sin(da);
            list.Add((u, v) => (cu + (u - cu) * c - (v - cv) * sn + tx, cv + (u - cu) * sn + (v - cv) * c + ty));
        }
        return list;
    }

    void CommitCurve(CadSession s, SketchContext sk)
    {
        var ts = CurveTransforms(sk);
        if (ts == null) { s.Say("Could not read that curve.", true); return; }
        var sel = sk.SelEntities.ToList();
        bool ok = sk.Edit(s, "That pattern", () => { foreach (var T in ts) CopyOnce(sk.Model, sel, T); });
        _path = -1; _clicks.Clear();
        if (ok) { s.Say($"Pattern made: {_count} copies in total along the curve."); sk.SelEntities.Clear(); _picked = false; _selecting = true; s.NotifySketchChanged(); }
        s.Invalidate();
    }

    void CopyOnce(SketchModel m, List<int> sel, Func<double, double, (double, double)> T)
    {
        var ents = sel.Select(m.E).ToList();
        var pmap = new Dictionary<int, int>(); var emap = new Dictionary<int, int>();
        int P(int pid)
        {
            if (pid == 0) { var (ox, oy) = T(0, 0); if (Math.Abs(ox) < 1e-9 && Math.Abs(oy) < 1e-9) return 0; }       // the origin only stays shared when the copy does not move it
            if (!pmap.TryGetValue(pid, out int np)) { var q = m.P(pid); var (x, y) = T(q.X, q.Y); np = m.AddPoint(x, y); pmap[pid] = np; }
            return np;
        }
        foreach (var e in ents)
        {
            int id = e.Type switch
            {
                EntType.Line => m.AddLine(P(e.A), P(e.B), e.Construction),
                EntType.Circle => m.AddCircle(P(e.A), e.R, e.Construction),
                EntType.Arc => m.AddArc(P(e.A), P(e.B), P(e.C), e.Construction),
                EntType.Ellipse => m.AddEllipse(P(e.A), P(e.B), e.R, e.Construction),
                EntType.Spline => m.AddSpline(e.Pts.Select(P).ToList(), e.Closed, e.ControlPoints, e.Construction),
                EntType.Point => m.AddPointEntity(P(e.A), e.Construction),
                _ => -1,
            };
            if (id >= 0) emap[e.Id] = id;
        }
        // relations among the copied geometry carry over; directions-based ones only survive a straight move
        foreach (var c in m.Constraints.ToList())
        {
            if (c.IsDimension || c.Hidden || c.Type == CType.Fixed) continue;
            if ((circular || _curve) && c.Type is CType.Horizontal or CType.Vertical or CType.Collinear) continue;
            if (c.Type == CType.Symmetric) continue;
            var ip = SketchModel.RefIsPoint(c.Type, c.Refs.Length);
            var refs = new int[c.Refs.Length]; bool all = true;
            for (int i = 0; i < refs.Length && all; i++)
            {
                if (ip[i]) { if (pmap.TryGetValue(c.Refs[i], out int np)) refs[i] = np; else all = false; }
                else { if (emap.TryGetValue(c.Refs[i], out int ne)) refs[i] = ne; else all = false; }
            }
            if (all) m.Add(c.Type, c.Value, c.V2, false, refs);
        }
    }

    public override void BuildOverlay(CadSession s, Overlay o)
    {
        var sk = Sk(s);
        if (_curve && _picked && _path >= 0 && CurveTransforms(sk) is { } cts)
        {
            var sels = sk.SelEntities.ToHashSet();
            var cpolys = sk.Model.Polylines(sk.Plane).Where(t => sels.Contains(t.ent.Id)).ToList();
            foreach (var T in cts)
                foreach (var (_, pts) in cpolys)
                    o.AddPolyline(pts.Select(q => { var (u, v) = sk.Plane.Project(q); var (x, y) = T(u, v); return W(sk, x, y); }).ToList(), Rubber, 1.8f);
            return;
        }
        if (!_picked || _clicks.Count == 0 || Hot is not { } h) return;
        if (!circular) { var (lu, lv) = Locked(_clicks[0], h.U, h.V); h = new SnapInfo(lu, lv, SnapKind.Grid, -1); }
        var a = _clicks[0]; var (cu, cv) = Centroid(sk);
        double step = 0, dx = 0, dy = 0;
        if (circular) step = _clicks.Count == 1 ? Norm(Math.Atan2(h.V - a.V, h.U - a.U) - Math.Atan2(cv - a.V, cu - a.U)) : 0;
        else { dx = h.U - a.U; dy = h.V - a.V; }
        if (circular && step < 1e-6) step = 2 * Math.PI / _count;
        if (circular && _clicks.Count == 1) o.AddLine(W(sk, a.U, a.V), W(sk, h.U, h.V), 0x907AA7E8, 1.2f);
        else if (!circular) o.AddLine(W(sk, a.U, a.V), W(sk, h.U, h.V), 0x907AA7E8, 1.2f);
        var selEnts = sk.SelEntities.ToHashSet();
        var polys = sk.Model.Polylines(sk.Plane).Where(t => selEnts.Contains(t.ent.Id)).ToList();
        for (int k = 1; k < _count; k++)
        {
            var T = Placement(k, step, dx, dy, a.U, a.V);
            foreach (var (_, pts) in polys)
                o.AddPolyline(pts.Select(q => { var (u, v) = sk.Plane.Project(q); var (x, y) = T(u, v); return W(sk, x, y); }).ToList(), Rubber, 1.8f);
        }
        o.AddLabel(W(sk, h.U, h.V), circular ? $"{_count} copies, {step * 180 / Math.PI:0.#}° apart" : $"{_count} copies, {Dist(a.U, a.V, h.U, h.V):0.##} mm apart");
    }
}
