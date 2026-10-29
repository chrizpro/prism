using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Prism.Core.Sketching;
using Prism.Kernel;

namespace Prism.Core;

/// <summary>
/// Offset: copy lines, arcs and circles a set distance to one side. Select one curve, a connected chain of them, or several things
/// (Shift adds more, or select them first with the Select tool). The side is where the pointer is; the panel shows the choices and takes an exact distance.
/// </summary>
public sealed class OffsetTool : SketchTool
{
    public override string Name => "Offset";
    public override string Prompt => _selecting
        ? "Offset: select curves (Shift-click adds or removes). Selected curves are highlighted. Press Enter when selection is complete; then move to a side and type a distance."
        : "Offset: move to the side you want and click, or type a distance in the panel and press Apply. Shift-click adds more curves; Esc starts over.";

    sealed record Item(SkEntity E, bool Fwd, int StartPt, int EndPt);
    sealed class Chain { public List<Item> Items = []; public bool Closed; }

    readonly List<Chain> _chains = [];
    bool _selecting = true;
    bool _useChain = true, _flip;
    double _mag;                       // distance shown in the panel (always positive; the side is the pointer's)
    bool _magTyped;                    // the user typed it: stop following the pointer
    string _typed = "";
    (double u, double v)? _cur;        // last pointer position on the sketch plane

    protected override void ResetState(CadSession s) { _chains.Clear(); _typed = ""; _mag = 0; _magTyped = false; _flip = false; _selecting = true; _cur = null; }
    void Reset(CadSession s) { ResetState(s); Sk(s).SelEntities.Clear(); s.Say(Prompt); s.RaiseState(); s.Invalidate(); }

    public override void Activate(CadSession s)
    {
        var sk = Sk(s);
        foreach (var id in sk.SelEntities.ToList())
            if (sk.Model.EntityOrNull(id) is { } e && SketchGeo.Modifiable(e)) AddSeed(sk, e);
        RefreshSelection(sk);
        s.Say(Prompt);
    }

    // ------------------------------------------------------------ the panel
    public override string PanelTitle => "OFFSET";
    public override IReadOnlyList<ToolControl>? Panel
    {
        get
        {
            int curves = _chains.Sum(c => c.Items.Count);
            string info = _chains.Count == 0 ? "Nothing selected yet" : $"{curves} curve{(curves == 1 ? "" : "s")} selected" + (_chains.Count > 1 ? $" in {_chains.Count} groups" : "");
            return
            [
                new(ToolControlKind.Info, "sel", info),
                new(ToolControlKind.Toggle, "chain", "Chain selection", Text: "Pick everything connected to the curve you click. Turn off to offset just that one curve.", On: _useChain),
                new(ToolControlKind.Button, "selection", _selecting ? "Done selecting (Enter)" : "Change selection", Primary: _selecting, Enabled: _chains.Count > 0),
                new(ToolControlKind.Number, "dist", "Distance", Number: Math.Round(_mag, 3), Unit: "mm"),
                new(ToolControlKind.Button, "flip", "Flip side", Text: "Offset to the other side of the curve.", Enabled: _chains.Count > 0),
                new(ToolControlKind.Button, "apply", "Apply offset", Primary: !_selecting, Enabled: !_selecting && _chains.Count > 0 && _mag > 1e-6),
                new(ToolControlKind.Button, "clear", "Clear selection", Enabled: _chains.Count > 0),
            ];
        }
    }

    public override void PanelSet(CadSession s, string id, object? value)
    {
        var sk = Sk(s);
        switch (id)
        {
            case "chain": _useChain = value is true; break;
            case "dist":
                if (value is double d && d > 1e-9) { _mag = d; _magTyped = true; _typed = ""; }
                break;
            case "flip": _flip = !_flip; break;
            case "clear": Reset(s); return;
            case "selection": if (_chains.Count > 0) { _selecting = !_selecting; _mag = 0; _magTyped = false; } break;
            case "apply":
                if (_selecting) { s.Say("Finish selecting curves with Enter before setting the offset.", true); return; }
                if (_chains.Count == 0) { s.Say("Select a curve first.", true); return; }
                if (_mag < 1e-6) { s.Say("Type a distance, or move the pointer to the side you want and click.", true); return; }
                Apply(s, sk); return;
        }
        s.RaiseState(); s.Invalidate();
    }

    // ------------------------------------------------------------ chains
    static (int a, int b) Ends(SkEntity e) => e.Type == EntType.Line ? (e.A, e.B) : e.Type == EntType.Arc ? (e.B, e.C) : (-1, -1);

    static Chain BuildChain(SketchModel m, SkEntity seed, bool connected)
    {
        var chain = new Chain();
        if (seed.Type == EntType.Circle) { chain.Items.Add(new Item(seed, true, -1, -1)); return chain; }
        var (sa, sb) = Ends(seed);
        var items = chain.Items; items.Add(new Item(seed, true, sa, sb));
        if (!connected) return chain;
        var used = new HashSet<int> { seed.Id };
        bool Grow(bool forward)
        {
            var tip = forward ? items[^1].EndPt : items[0].StartPt;
            var cand = m.Entities.Where(o => !used.Contains(o.Id) && o.Type is EntType.Line or EntType.Arc && (Ends(o).a == tip || Ends(o).b == tip)).ToList();
            var cnt = m.Entities.Count(o => o.Type is EntType.Line or EntType.Arc && (Ends(o).a == tip || Ends(o).b == tip));
            if (cand.Count != 1 || cnt != 2) return false;
            var o = cand[0]; var (a, b) = Ends(o);
            used.Add(o.Id);
            if (forward) items.Add(a == tip ? new Item(o, true, a, b) : new Item(o, false, b, a));
            else items.Insert(0, b == tip ? new Item(o, true, a, b) : new Item(o, false, b, a));
            return true;
        }
        while (Grow(true)) { if (items[^1].EndPt == items[0].StartPt) { chain.Closed = true; break; } }
        if (!chain.Closed) while (Grow(false)) { }
        if (items.Count > 1 && items[^1].EndPt == items[0].StartPt) chain.Closed = true;
        return chain;
    }

    /// <summary>Add the chain (or single curve) a curve belongs to, unless it is already in. Returns false if it was removed instead.</summary>
    bool ToggleSeed(SketchContext sk, SkEntity e)
    {
        var have = _chains.FirstOrDefault(c => c.Items.Any(i => i.E.Id == e.Id));
        if (have != null) { _chains.Remove(have); return false; }
        AddSeed(sk, e); return true;
    }

    void AddSeed(SketchContext sk, SkEntity e)
    {
        if (_chains.Any(c => c.Items.Any(i => i.E.Id == e.Id))) return;
        var chain = BuildChain(sk.Model, e, _useChain);
        chain.Items.RemoveAll(i => _chains.Any(c => c.Items.Any(x => x.E.Id == i.E.Id)));          // a curve is only offset once
        if (chain.Items.Count > 0) _chains.Add(chain);
    }

    void RefreshSelection(SketchContext sk)
    {
        sk.SelEntities.Clear(); sk.SelPoints.Clear();
        foreach (var c in _chains) foreach (var it in c.Items) sk.SelEntities.Add(it.E.Id);
    }

    // ------------------------------------------------------------ geometry
    abstract record Curve;
    sealed record Ln(double Px, double Py, double Dx, double Dy) : Curve;                   // infinite line through P with unit direction
    sealed record Cr(double Cx, double Cy, double R) : Curve;

    static (double x, double y) Pt(SketchModel m, int id) { var p = m.P(id); return (p.X, p.Y); }

    /// <summary>Offset curves and their trimmed endpoints for distance d (positive = left of travel).</summary>
    sealed record Result(List<(SkEntity e, Curve c, (double x, double y) s, (double x, double y) t)> Parts, bool Closed);

    static Result? Compute(SketchModel m, Chain chain, double d)
    {
        var items = chain.Items;
        var curves = new List<Curve>(); var naive = new List<((double, double) s, (double, double) t)>();
        foreach (var it in items)
        {
            var e = it.E;
            if (e.Type == EntType.Circle)
            {
                var c = Pt(m, e.A); double r = e.R - d; if (r < 1e-6) return null;
                curves.Add(new Cr(c.x, c.y, r)); naive.Add((c, c)); continue;
            }
            if (e.Type == EntType.Line)
            {
                var a = Pt(m, it.StartPt); var b = Pt(m, it.EndPt); double dx = b.x - a.x, dy = b.y - a.y, l = Math.Sqrt(dx * dx + dy * dy); if (l < 1e-9) return null;
                dx /= l; dy /= l; double nx = -dy * d, ny = dx * d;
                curves.Add(new Ln(a.x + nx, a.y + ny, dx, dy)); naive.Add(((a.x + nx, a.y + ny), (b.x + nx, b.y + ny)));
            }
            else
            {
                var (cx, cy, r, _, _) = m.ArcGeometry(e);
                double nr = it.Fwd ? r - d : r + d; if (nr < 1e-6) return null;
                curves.Add(new Cr(cx, cy, nr));
                (double, double) Rad(int pid) { var p = Pt(m, pid); double ang = Math.Atan2(p.y - cy, p.x - cx); return (cx + nr * Math.Cos(ang), cy + nr * Math.Sin(ang)); }
                naive.Add((Rad(it.StartPt), Rad(it.EndPt)));
            }
        }
        int n = items.Count; var ends = new (double x, double y)[n][];
        for (int i = 0; i < n; i++) ends[i] = [naive[i].s, naive[i].t];
        for (int i = 0; i < n; i++)
        {
            int j = i + 1; if (j == n) { if (!chain.Closed) break; j = 0; }
            if (items[i].E.Type == EntType.Circle) break;
            var joint = Pt(m, items[i].EndPt);
            var hit = Join(curves[i], curves[j], naive[i].t, joint);
            if (hit is { } h) { ends[i][1] = h; ends[j][0] = h; }
            else { var mid = ((naive[i].t.Item1 + naive[j].s.Item1) / 2, (naive[i].t.Item2 + naive[j].s.Item2) / 2); ends[i][1] = mid; ends[j][0] = mid; }
        }
        var parts = new List<(SkEntity, Curve, (double, double), (double, double))>();
        for (int i = 0; i < n; i++) parts.Add((items[i].E, curves[i], ends[i][0], ends[i][1]));
        return new Result(parts, chain.Closed);
    }

    /// <summary>Where two offset curves meet near the old joint (lines and circles are treated as unbounded).</summary>
    static (double x, double y)? Join(Curve a, Curve b, (double x, double y) near, (double x, double y) oldJoint)
    {
        var hits = new List<(double x, double y)>();
        switch (a, b)
        {
            case (Ln l1, Ln l2):
            {
                double den = l1.Dx * l2.Dy - l1.Dy * l2.Dx; if (Math.Abs(den) < 1e-9) return null;
                double t = ((l2.Px - l1.Px) * l2.Dy - (l2.Py - l1.Py) * l2.Dx) / den; hits.Add((l1.Px + l1.Dx * t, l1.Py + l1.Dy * t)); break;
            }
            case (Ln l, Cr c): LineCircle(l, c, hits); break;
            case (Cr c, Ln l): LineCircle(l, c, hits); break;
            case (Cr c1, Cr c2): CircleCircle(c1, c2, hits); break;
        }
        if (hits.Count == 0) return null;
        return hits.OrderBy(h => (h.x - near.x) * (h.x - near.x) + (h.y - near.y) * (h.y - near.y)).First();
    }

    static void LineCircle(Ln l, Cr c, List<(double, double)> r)
    {
        double fx = l.Px - c.Cx, fy = l.Py - c.Cy, b = 2 * (fx * l.Dx + fy * l.Dy), k = fx * fx + fy * fy - c.R * c.R, disc = b * b - 4 * k;
        if (disc < 0) return;
        double sq = Math.Sqrt(disc);
        foreach (var t in new[] { (-b - sq) / 2, (-b + sq) / 2 }) r.Add((l.Px + l.Dx * t, l.Py + l.Dy * t));
    }

    static void CircleCircle(Cr p, Cr q, List<(double, double)> r)
    {
        double dx = q.Cx - p.Cx, dy = q.Cy - p.Cy, d = Math.Sqrt(dx * dx + dy * dy);
        if (d < 1e-9 || d > p.R + q.R + 1e-7 || d < Math.Abs(p.R - q.R) - 1e-7) return;
        double a = (p.R * p.R - q.R * q.R + d * d) / (2 * d), h2 = p.R * p.R - a * a, h = h2 > 0 ? Math.Sqrt(h2) : 0;
        double mx = p.Cx + dx * a / d, my = p.Cy + dy * a / d;
        r.Add((mx - dy * h / d, my + dx * h / d)); r.Add((mx + dy * h / d, my - dx * h / d));
    }

    /// <summary>Signed distance of a point from the chain's first curve: positive on the left of its direction of travel.</summary>
    static double SideDistance(SketchModel m, Chain chain, double u, double v)
    {
        var it = chain.Items[0]; var e = it.E;
        if (e.Type == EntType.Line)
        {
            var a = Pt(m, it.StartPt); var b = Pt(m, it.EndPt); double dx = b.x - a.x, dy = b.y - a.y, l = Math.Sqrt(dx * dx + dy * dy);
            return (dx * (v - a.y) - dy * (u - a.x)) / l;
        }
        var (cx, cy, r) = e.Type == EntType.Circle ? (m.P(e.A).X, m.P(e.A).Y, e.R) : (m.ArcGeometry(e).cx, m.ArcGeometry(e).cy, m.ArcGeometry(e).r);
        double dist = Math.Sqrt((u - cx) * (u - cx) + (v - cy) * (v - cy));
        return e.Type == EntType.Arc && !it.Fwd ? dist - r : r - dist;           // left of a counter-clockwise curve is inside
    }

    /// <summary>The signed offset for each chain: the same distance, toward the side the pointer is on (flipped by the panel button).</summary>
    double[] Distances(SketchModel m)
    {
        var d = new double[_chains.Count];
        for (int i = 0; i < d.Length; i++)
        {
            double side = _cur is { } c ? SideDistance(m, _chains[i], c.u, c.v) : 1;
            d[i] = _mag * (side < 0 ? -1 : 1) * (_flip ? -1 : 1);
        }
        return d;
    }

    // ------------------------------------------------------------ applying
    void Apply(CadSession s, SketchContext sk)
    {
        var ds = Distances(sk.Model);
        var results = new List<(Chain chain, Result res)>();
        for (int i = 0; i < _chains.Count; i++)
        {
            var res = Compute(sk.Model, _chains[i], ds[i]);
            if (res == null) { s.Say("That offset collapses the shape. Try a smaller distance or the other side.", true); return; }
            results.Add((_chains[i], res));
        }
        bool committed = sk.Edit(s, "That offset", () =>
        {
            var m = sk.Model; sk.SelEntities.Clear(); sk.SelPoints.Clear();
            var pointAt = new Dictionary<(long, long), int>();
            int P((double x, double y) p) { var key = ((long)Math.Round(p.x * 1e6), (long)Math.Round(p.y * 1e6)); if (!pointAt.TryGetValue(key, out int id)) pointAt[key] = id = m.AddPoint(p.x, p.y); return id; }
            foreach (var (chain, res) in results)
                for (int i = 0; i < res.Parts.Count; i++)
                {
                    var (e, c, st, en) = res.Parts[i]; bool cons = e.Construction;
                    if (e.Type == EntType.Circle) { m.AddCircle(e.A, ((Cr)c).R, cons); continue; }
                    if (e.Type == EntType.Line)
                    {
                        int l = m.AddLine(P(st), P(en), cons);
                        m.Add(CType.Parallel, 0, 0, false, l, e.Id);
                    }
                    else
                    {
                        bool fwd = chain.Items[i].Fwd;
                        if (fwd) m.AddArc(e.A, P(st), P(en), cons); else m.AddArc(e.A, P(en), P(st), cons);        // an arc always runs counter-clockwise from its start
                    }
                }
        });
        if (committed) Reset(s);
    }

    // ------------------------------------------------------------ input
    public override bool PointerDown(CadSession s, PointerInfo p)
    {
        if (!p.Left) return false;
        var sk = Sk(s);
        if (_selecting || _chains.Count == 0 || p.Shift)
        {
            int eid = sk.PickEntity(s, p.X, p.Y, 9);
            var e = eid >= 0 ? sk.Model.EntityOrNull(eid) : null;
            if (e == null) return true;
            if (!SketchGeo.Modifiable(e)) { s.Say("Offset works on lines, arcs and circles.", true); return true; }
            if (!_selecting) { _selecting = true; _mag = 0; _magTyped = false; }
            if (!p.Shift) _chains.Clear();
            ToggleSeed(sk, e); RefreshSelection(sk);
            s.Say(Prompt); s.RaiseState(); s.Invalidate();
            return true;
        }
        if (sk.PlanePoint(s, p.X, p.Y) is not { } uv) return true;
        _cur = (uv.u, uv.v);
        if (!_magTyped) _mag = Math.Abs(SideDistance(sk.Model, _chains[0], uv.u, uv.v));
        if (_mag < 1e-6) { s.Say("Move away from the curve to set a distance.", true); return true; }
        Apply(s, sk);
        return true;
    }

    public override void PointerMove(CadSession s, PointerInfo p)
    {
        var sk = Sk(s);
        sk.Cursor = null;
        if (sk.PlanePoint(s, p.X, p.Y) is { } w)
        {
            s.Cursor3 = sk.Plane.PointAt(w.u, w.v);
            _cur = (w.u, w.v);
            if (!_selecting && _chains.Count > 0 && !_magTyped && _typed.Length == 0) { double nm = Math.Abs(SideDistance(sk.Model, _chains[0], w.u, w.v)); if (Math.Abs(nm - _mag) > 0.01) { _mag = nm; s.RaiseState(); } }
        }
        if (_selecting || _chains.Count == 0 || p.Shift) sk.HoverEntity = sk.PickEntity(s, p.X, p.Y, 9); else sk.HoverEntity = -1;
        s.Invalidate();
    }

    public override bool Key(CadSession s, string key)
    {
        if (key == "Enter" && _selecting)
        { if (_chains.Count > 0) { _selecting = false; s.Say("Selection complete. Move to the offset side, then click or type a distance and press Enter."); s.RaiseState(); s.Invalidate(); } else s.Say("Select at least one curve first."); return true; }
        if (_chains.Count == 0) return false;
        var sk = Sk(s);
        if (key.StartsWith("Char:") && key.Length == 6 && (char.IsDigit(key[5]) || key[5] == '.' || key[5] == ',')) { _typed += key[5] == ',' ? '.' : key[5]; ApplyTyped(); s.RaiseState(); s.Invalidate(); return true; }
        switch (key)
        {
            case "Back": if (_typed.Length == 0) return false; _typed = _typed[..^1]; ApplyTyped(); s.RaiseState(); s.Invalidate(); return true;
            case "Enter":
                if (_mag < 1e-6) return false;
                Apply(s, sk); return true;
            case "Escape": Reset(s); return true;
        }
        return false;
    }

    void ApplyTyped()
    {
        if (double.TryParse(_typed, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0) { _mag = v; _magTyped = true; }
        else if (_typed.Length == 0) _magTyped = false;
    }

    public override void BuildOverlay(CadSession s, Overlay o)
    {
        if (_chains.Count == 0) return;
        var sk = Sk(s); var m = sk.Model;
        if (_selecting)
        {
            var first = _chains[0].Items[0].E; var point = m.P(first.A);
            o.AddLabel(W(sk,point.X,point.Y), $"{_chains.Sum(c => c.Items.Count)} selected · Shift-click adds · Enter sets distance", true, 4); return;
        }
        if (_mag < 1e-6) return;
        var ds = Distances(m);
        const uint col = 0xFF3FCF8E;
        (double x, double y)? labelAt = null;
        for (int ci = 0; ci < _chains.Count; ci++)
        {
            var chain = _chains[ci];
            var res = Compute(m, chain, ds[ci]); if (res == null) continue;
            foreach (var (e, c, st, en) in res.Parts)
            {
                if (c is Cr cr && e.Type == EntType.Circle) { o.AddPolyline(SketchGeo.ArcPoints(cr.Cx, cr.Cy, cr.R, 0, 2 * Math.PI).Select(q => W(sk, q.x, q.y)).ToList(), col, 3.5f); labelAt ??= (cr.Cx + cr.R, cr.Cy); continue; }
                if (c is Cr arc)
                {
                    double a0 = Math.Atan2(st.y - arc.Cy, st.x - arc.Cx), a1 = Math.Atan2(en.y - arc.Cy, en.x - arc.Cx);
                    var it = chain.Items.First(x => x.E.Id == e.Id);
                    double sweep = it.Fwd ? SketchGeo.Norm(a1 - a0) : -SketchGeo.Norm(a0 - a1);
                    o.AddPolyline(SketchGeo.ArcPoints(arc.Cx, arc.Cy, arc.R, a0, sweep).Select(q => W(sk, q.x, q.y)).ToList(), col, 3.5f);
                }
                else o.AddPolyline([W(sk, st.x, st.y), W(sk, en.x, en.y)], col, 3.5f);
                labelAt ??= st;
            }
        }
        if (labelAt is { } la) o.AddLabel(W(sk, la.x, la.y), _typed.Length > 0 ? $"{_typed}_ mm  -  Enter" : $"{_mag:0.##} mm", true, 4);
    }
}
