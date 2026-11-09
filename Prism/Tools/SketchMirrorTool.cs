using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core.Sketching;
using Prism.Kernel;

namespace Prism.Core;

/// <summary>
/// Mirror sketch curves across a line. Pick the curves (click, Shift adds more, or select them first), press Enter, then click the line to mirror across.
/// In live mode the copy is tied to the original with symmetry constraints, so changing one changes the other.
/// </summary>
public sealed class SketchMirrorTool(int variant = 0) : SketchTool
{
    bool _copyOnly = variant == 1;
    bool _picked;
    int _hoverLine = -1;

    public override string Name => "Mirror";
    public override string Mode => _copyOnly ? "Copy" : "Live";
    static readonly ToolOption[] Opts = [new("Live", "The copy stays symmetric: change one side and the other follows", "mirror"), new("Copy", "Just copy the curves, no link", "mirror")];
    public override IReadOnlyList<ToolOption> Options => Opts;
    public override int OptionIndex => _copyOnly ? 1 : 0;
    protected override void ApplyOption(int index) => _copyOnly = index == 1;
    protected override void ResetState(CadSession s) { _sel = Sk(s).SelEntities.Count; _picked = _sel > 0; }

    public override string Prompt => _picked
        ? "Mirror: click the line to mirror across (a normal or construction line)."
        : "Mirror: click the curves to mirror (Shift adds more), then press Enter. Or select them first.";

    public override void Activate(CadSession s) { _sel = Sk(s).SelEntities.Count; _picked = _sel > 0; s.Say(Prompt); }

    // ------------------------------------------------------------ the panel: the same two steps as Offset (curves, then the line)
    public override string PanelInputTitle => "1. Select curves";
    public override string PanelSettingsTitle => "2. Mirror line";
    public override IReadOnlyList<ToolControl>? Panel
    {
        get
        {
            int n = _sel;
            string info = n == 0 ? "Nothing selected yet. Click curves (Shift adds more), or select them before choosing Mirror." : $"{n} curve{(n == 1 ? "" : "s")} selected";
            return
            [
                new(ToolControlKind.Info, "sel", info, Section: PanelInputTitle),
                new(ToolControlKind.Button, "selection", _picked ? "Change selection" : "Done selecting (Enter)", Primary: !_picked, Enabled: n > 0, Section: PanelInputTitle),
                new(ToolControlKind.Button, "clear", "Clear selection", Enabled: n > 0, Section: PanelInputTitle),
                new(ToolControlKind.Info, "line", _picked ? "Click the line to mirror across. A normal or construction line works; move over one to preview." : "Finish selecting first, then click the mirror line.", Section: PanelSettingsTitle),
            ];
        }
    }
    int _sel;                                    // how many curves are selected, kept for the panel

    public override void PanelSet(CadSession s, string id, object? value)
    {
        var sk = Sk(s);
        switch (id)
        {
            case "selection":
                if (_picked) { _picked = false; _hoverLine = -1; }
                else if (sk.SelEntities.Count > 0) _picked = true;
                break;
            case "clear": sk.SelEntities.Clear(); _picked = false; _hoverLine = -1; s.NotifySketchChanged(); break;
        }
        _sel = sk.SelEntities.Count;
        s.Say(Prompt); s.RaiseState(); s.Invalidate();
    }
    public override void Deactivate(CadSession s) { _hoverLine = -1; base.Deactivate(s); }

    public override bool Key(CadSession s, string key)
    {
        var sk = Sk(s);
        _sel = sk.SelEntities.Count;
        if (key == "Enter" && !_picked && sk.SelEntities.Count > 0) { _picked = true; s.Say(Prompt); s.RaiseState(); s.Invalidate(); return true; }
        if (key == "Escape")
        {
            if (_picked) { _picked = false; sk.SelEntities.Clear(); s.NotifySketchChanged(); s.Say(Prompt); s.Invalidate(); return true; }
            return false;
        }
        return false;
    }

    public override bool PointerDown(CadSession s, PointerInfo p)
    {
        if (!p.Left) return false;
        var sk = Sk(s);
        int eid = sk.PickEntity(s, p.X, p.Y, 9);
        _sel = sk.SelEntities.Count;
        if (!_picked)
        {
            if (eid >= 0) { if (!sk.SelEntities.Remove(eid)) sk.SelEntities.Add(eid); _sel = sk.SelEntities.Count; s.NotifySketchChanged(); s.RaiseState(); s.Invalidate(); }
            return true;
        }
        // choosing the mirror line
        if (eid < 0 || sk.Model.EntityOrNull(eid) is not { Type: EntType.Line } line) { s.Say("Click a line to mirror across.", true); return true; }
        if (sk.SelEntities.Contains(eid) && sk.SelEntities.Count == 1) { s.Say("Pick a different line from the one being mirrored.", true); return true; }
        var ids = sk.SelEntities.Where(i => i != eid).ToList();
        if (ids.Count == 0) { s.Say("Select some curves to mirror first.", true); return true; }
        bool ok = sk.Edit(s, "That mirror", () => Mirror(sk.Model, ids, line, !_copyOnly));
        if (ok) { s.Say($"Mirrored {ids.Count} curve(s)."); sk.SelEntities.Clear(); _picked = false; _sel = 0; s.NotifySketchChanged(); s.RaiseState(); }
        s.Invalidate();
        return true;
    }

    public override void PointerMove(CadSession s, PointerInfo p)
    {
        var sk = Sk(s);
        sk.Cursor = null;
        int e = sk.PickEntity(s, p.X, p.Y, 9);
        if (_picked) _hoverLine = e >= 0 && sk.Model.EntityOrNull(e)?.Type == EntType.Line ? e : -1;
        else { if (e != sk.HoverEntity) sk.HoverEntity = e; }
        if (sk.PlanePoint(s, p.X, p.Y) is { } w) s.Cursor3 = sk.Plane.PointAt(w.u, w.v);
        s.Invalidate();
    }

    // ------------------------------------------------------------ the mirroring itself
    /// <summary>Reflect the curves across a line; points on the line are shared, others get a mirror partner.</summary>
    public static void Mirror(SketchModel m, List<int> entityIds, SkEntity axis, bool live)
    {
        var a = m.P(axis.A); var b = m.P(axis.B);
        double dx = b.X - a.X, dy = b.Y - a.Y, len = Math.Max(Math.Sqrt(dx * dx + dy * dy), 1e-12);
        double ux = dx / len, uy = dy / len;
        (double x, double y) Reflect(double x, double y)
        {
            double rx = x - a.X, ry = y - a.Y, along = rx * ux + ry * uy;
            double px = a.X + ux * along, py = a.Y + uy * along;
            return (2 * px - x, 2 * py - y);
        }
        double DistToLine(double x, double y) => Math.Abs((x - a.X) * uy - (y - a.Y) * ux);

        var map = new Dictionary<int, int>();
        int P(int pid)
        {
            if (map.TryGetValue(pid, out int np)) return np;
            var q = m.P(pid);
            if (pid == axis.A || pid == axis.B || DistToLine(q.X, q.Y) < 1e-6) { map[pid] = pid; return pid; }     // on the axis: stays itself
            var (rx, ry) = Reflect(q.X, q.Y);
            np = m.AddPoint(rx, ry); map[pid] = np;
            if (live) m.Add(CType.Symmetric, 0, 0, false, pid, np, axis.Id);
            return np;
        }
        foreach (int id in entityIds)
        {
            var e = m.E(id);
            switch (e.Type)
            {
                case EntType.Line: m.AddLine(P(e.A), P(e.B), e.Construction); break;
                case EntType.Circle:
                {
                    int c = m.AddCircle(P(e.A), e.R, e.Construction);
                    if (live) m.Add(CType.Equal, 0, 0, false, id, c);
                    break;
                }
                case EntType.Arc: m.AddArc(P(e.A), P(e.C), P(e.B), e.Construction); break;     // mirroring turns an arc around, so its ends swap
                case EntType.Ellipse: m.AddEllipse(P(e.A), P(e.B), e.R, e.Construction); break;
                case EntType.Spline: m.AddSpline(e.Pts.Select(P).ToList(), e.Closed, e.ControlPoints, e.Construction); break;
                case EntType.Point: m.AddPointEntity(P(e.A), e.Construction); break;
            }
        }
    }

    public override void BuildOverlay(CadSession s, Overlay o)
    {
        var sk = Sk(s);
        if (!_picked || _hoverLine < 0 || sk.Model.EntityOrNull(_hoverLine) is not { Type: EntType.Line } line) return;
        var m = sk.Model; var a = m.P(line.A); var b = m.P(line.B);
        o.AddLine(sk.Plane.PointAt(a.X, a.Y), sk.Plane.PointAt(b.X, b.Y), 0xFFFFC53D, 3.5f);
        double dx = b.X - a.X, dy = b.Y - a.Y, l = Math.Max(Math.Sqrt(dx * dx + dy * dy), 1e-12), ux = dx / l, uy = dy / l;
        var sel = sk.SelEntities.ToHashSet();
        foreach (var (e, pts) in m.Polylines(sk.Plane))
        {
            if (!sel.Contains(e.Id)) continue;
            o.AddPolyline(pts.Select(q =>
            {
                var (pu, pv) = sk.Plane.Project(q);
                double rx = pu - a.X, ry = pv - a.Y, al = rx * ux + ry * uy, px = a.X + ux * al, py = a.Y + uy * al;
                return sk.Plane.PointAt(2 * px - pu, 2 * py - pv);
            }).ToList(), Rubber, 2f);
        }
    }
}
