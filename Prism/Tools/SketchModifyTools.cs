using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core.Sketching;
using Prism.Kernel;

namespace Prism.Core;

/// <summary>Shared maths for the sketch modify tools (extend, offset, fillet, chamfer).</summary>
static class SketchGeo
{
    public static double Norm(double a) { while (a < 0) a += 2 * Math.PI; while (a >= 2 * Math.PI) a -= 2 * Math.PI; return a; }
    public static bool Modifiable(SkEntity e) => e.Type is EntType.Line or EntType.Arc or EntType.Circle;
    public static bool HasConstraint(SketchModel m, CType t, int ent) => m.Constraints.Any(c => c.Type == t && c.Refs.Length == 1 && c.Refs[0] == ent);
    public static void Attach(SketchModel m, int point, int cutter)
    {
        if (m.EntityOrNull(cutter)?.Type is EntType.Line or EntType.Circle or EntType.Arc or EntType.Ellipse) m.Add(CType.PointOnEntity, 0, 0, false, point, cutter);
    }
    public static IEnumerable<(double x, double y)> ArcPoints(double cx, double cy, double r, double a0, double sweep)
    {
        int n = Math.Max(8, (int)(Math.Abs(sweep) / (2 * Math.PI) * 96));
        for (int i = 0; i <= n; i++) { double a = a0 + sweep * i / n; yield return (cx + r * Math.Cos(a), cy + r * Math.Sin(a)); }
    }
}

// =====================================================================================================  EXTEND
public sealed class ExtendTool : SketchTool
{
    public override string Name => "Extend";
    public override string Prompt => "Extend: click near the end of a line or arc you want to lengthen. It grows until it meets the next curve.";

    sealed record Plan(int Entity, bool AtStart, double X, double Y, int Cutter, (double x, double y) From);
    Plan? _hover;

    static Plan? MakePlan(SketchContext sk, int eid, double u, double v)
    {
        var m = sk.Model; var e = m.EntityOrNull(eid);
        if (e == null || e.Type is not (EntType.Line or EntType.Arc)) return null;
        var others = m.Entities.Where(o => o.Id != eid && o.Type != EntType.Point).ToList();
        if (e.Type == EntType.Line)
        {
            var a = m.P(e.A); var b = m.P(e.B);
            double dx = b.X - a.X, dy = b.Y - a.Y, len = Math.Sqrt(dx * dx + dy * dy); if (len < 1e-9) return null;
            double t = ((u - a.X) * dx + (v - a.Y) * dy) / (len * len);
            bool start = t < 0.5;
            var (ex, ey) = start ? (a.X, a.Y) : (b.X, b.Y);
            double ux = start ? -dx / len : dx / len, uy = start ? -dy / len : dy / len;
            var ray = new SketchCross.Seg(ex, ey, ex + ux * 1e5, ey + uy * 1e5, -1);
            double best = double.MaxValue; Plan? plan = null;
            foreach (var o in others)
                foreach (var prim in SketchCross.Prims(m, o))
                    foreach (var (x, y) in SketchCross.Hit(ray, prim))
                    {
                        double d = (x - ex) * ux + (y - ey) * uy;
                        if (d > 1e-6 && d < best) { best = d; plan = new Plan(eid, start, x, y, o.Id, (ex, ey)); }
                    }
            return plan;
        }
        else
        {
            var (cx, cy, r, a0, sw) = m.ArcGeometry(e);
            double rel = SketchGeo.Norm(Math.Atan2(v - cy, u - cx) - a0);
            bool start = sw > 0 && rel > sw ? (2 * Math.PI - rel) < (rel - sw) : rel < sw / 2;
            double endAng = start ? a0 : a0 + sw;
            var full = new SketchCross.Arcp(cx, cy, r, 0, 2 * Math.PI, -1);
            double best = double.MaxValue; Plan? plan = null;
            foreach (var o in others)
                foreach (var prim in SketchCross.Prims(m, o))
                    foreach (var (x, y) in SketchCross.Hit(full, prim))
                    {
                        double ang = Math.Atan2(y - cy, x - cx);
                        double d = start ? SketchGeo.Norm(endAng - ang) : SketchGeo.Norm(ang - endAng);       // how far round the circle the extension runs
                        if (d > 1e-6 && d < best && d < 2 * Math.PI - sw - 1e-6) { best = d; plan = new Plan(eid, start, x, y, o.Id, (cx + r * Math.Cos(endAng), cy + r * Math.Sin(endAng))); }
                    }
            return plan;
        }
    }

    static void Apply(SketchContext sk, Plan plan)
    {
        var m = sk.Model; var e = m.E(plan.Entity); bool cons = e.Construction;
        int q = m.AddPoint(plan.X, plan.Y); SketchGeo.Attach(m, q, plan.Cutter);
        if (e.Type == EntType.Line)
        {
            bool h = SketchGeo.HasConstraint(m, CType.Horizontal, e.Id), vt = SketchGeo.HasConstraint(m, CType.Vertical, e.Id);
            int l = plan.AtStart ? m.AddLine(q, e.B, cons) : m.AddLine(e.A, q, cons);
            if (h) m.Add(CType.Horizontal, refs: l); if (vt) m.Add(CType.Vertical, refs: l);
        }
        else
        {
            if (plan.AtStart) m.AddArc(e.A, q, e.C, cons); else m.AddArc(e.A, e.B, q, cons);
        }
        m.RemoveEntity(e.Id);
    }

    public override bool PointerDown(CadSession s, PointerInfo p)
    {
        if (!p.Left) return false;
        var sk = Sk(s);
        int eid = sk.PickEntity(s, p.X, p.Y, 9);
        if (eid < 0 || sk.PlanePoint(s, p.X, p.Y) is not { } uv) return true;
        var e = sk.Model.EntityOrNull(eid);
        if (e == null || e.Type is not (EntType.Line or EntType.Arc)) { s.Say("Extend works on lines and arcs.", true); return true; }
        var plan = MakePlan(sk, eid, uv.u, uv.v);
        if (plan == null) { s.Say("Nothing ahead of that end to extend to.", true); return true; }
        sk.Edit(s, "That extension", () => { sk.SelEntities.Clear(); sk.SelPoints.Clear(); Apply(sk, plan); });
        _hover = null; s.Invalidate();
        return true;
    }

    public override void PointerMove(CadSession s, PointerInfo p)
    {
        var sk = Sk(s);
        int eid = sk.PickEntity(s, p.X, p.Y, 9);
        _hover = eid >= 0 && sk.PlanePoint(s, p.X, p.Y) is { } uv ? MakePlan(sk, eid, uv.u, uv.v) : null;
        sk.HoverEntity = -1; sk.Cursor = null;
        if (sk.PlanePoint(s, p.X, p.Y) is { } w) s.Cursor3 = sk.Plane.PointAt(w.u, w.v);
        s.Invalidate();
    }

    public override bool Key(CadSession s, string key) => false;

    public override void BuildOverlay(CadSession s, Overlay o)
    {
        if (_hover is not { } plan) return;
        var sk = Sk(s); const uint green = 0xFF3FCF8E;
        o.AddPolyline([W(sk, plan.From.x, plan.From.y), W(sk, plan.X, plan.Y)], green, 4.5f);
        o.AddDot(W(sk, plan.X, plan.Y), green, 8);
    }
}

// =====================================================================================================  FILLET / CHAMFER
public sealed class FilletTool(bool chamfer) : SketchTool
{
    public override string Name => chamfer ? "Chamfer" : "Fillet";
    static double _radius = 5, _distance = 3;
    double Size { get => chamfer ? _distance : _radius; set { if (chamfer) _distance = value; else _radius = value; } }
    string _typed = "";
    int _first = -1,_second=-1,_hover=-1; double _fu, _fv,_su,_sv,_hu,_hv,_downX,_downY,_startSize;
    bool _dragging;
    public override IReadOnlyList<ToolControl>? Panel => [new(ToolControlKind.Info,"step",Prompt),new(ToolControlKind.Number,"size",chamfer?"Distance":"Radius",Number:Size,Unit:"mm"),new(ToolControlKind.Button,"apply","Apply",Enabled:_second>=0)];
    public override void PanelSet(CadSession s,string id,object? value)
    {if(id=="size"&&value is double number&&number>0){Size=number;s.Invalidate();return;}if(id=="apply")CommitPicked(s);s.RaiseState();s.Invalidate();}
    void CommitPicked(CadSession s)
    {if(_second<0)return;try{Apply(s,Sk(s),_first,_fu,_fv,_second,_su,_sv);Reset(s);}catch(InvalidOperationException ex){s.Say(ex.Message,true);}}
    public override void PointerUp(CadSession s,PointerInfo p){if(_dragging){_dragging=false;CommitPicked(s);}}


    public override string Prompt => (_first < 0 ? $"{Name}: type a {(chamfer ? "distance" : "radius")} (now {Size:0.##} mm) and press Enter, then click the first line." : $"{Name}: click the second line.") + " Esc starts over.";

    void Reset(CadSession s) { _first = _second = -1; _dragging=false;_typed = ""; Sk(s).SelEntities.Clear(); s.Say(Prompt); s.RaiseState();s.Invalidate(); }

    public override bool PointerDown(CadSession s, PointerInfo p)
    {
        if (!p.Left) return false;
        var sk = Sk(s);
        int eid = sk.PickEntity(s, p.X, p.Y, 9);
        var e = eid >= 0 ? sk.Model.EntityOrNull(eid) : null;
        if (e == null || sk.PlanePoint(s, p.X, p.Y) is not { } uv) return true;
        if (e.Type != EntType.Line) { s.Say($"{Name} works on two lines.", true); return true; }
        if (_first < 0) { _first = eid; _fu = uv.u; _fv = uv.v; sk.SelEntities.Clear(); sk.SelEntities.Add(eid); s.Say(Prompt); s.Invalidate(); return true; }
        if (eid == _first) { s.Say("Pick a different line.", true); return true; }
        _second=eid;_su=uv.u;_sv=uv.v;_downX=p.X;_downY=p.Y;_startSize=Size;_dragging=true;
        sk.SelEntities.Add(eid);s.RaiseState();s.Invalidate();
        return true;
    }

    void Apply(CadSession s, SketchContext sk, int id1, double u1, double v1, int id2, double u2, double v2,Overlay? preview=null)
    {
        var m = sk.Model; var l1 = m.E(id1); var l2 = m.E(id2);
        var a1 = m.P(l1.A); var b1 = m.P(l1.B); var a2 = m.P(l2.A); var b2 = m.P(l2.B);
        double d1x = b1.X - a1.X, d1y = b1.Y - a1.Y, d2x = b2.X - a2.X, d2y = b2.Y - a2.Y;
        double den = d1x * d2y - d1y * d2x;
        if (Math.Abs(den) < 1e-9 * Math.Max(1, Math.Abs(d1x * d1x + d1y * d1y))) throw new InvalidOperationException("Those lines are parallel, so they have no corner.");
        double t = ((a2.X - a1.X) * d2y - (a2.Y - a1.Y) * d2x) / den;
        double xx = a1.X + d1x * t, xy = a1.Y + d1y * t;                                   // where the lines would meet
        // the part of each line that stays is the side the user clicked on
        (double ux, double uy, int far, int near) Side(SkEntity l, SkPoint a, SkPoint b, double cu, double cv)
        {
            double dax = a.X - xx, day = a.Y - xy, dbx = b.X - xx, dby = b.Y - xy;
            double sa = dax * (cu - xx) + day * (cv - xy), sb = dbx * (cu - xx) + dby * (cv - xy);
            bool keepA = Math.Abs(sa) > Math.Abs(sb) ? sa > 0 : sb < 0 && sa >= sb;
            var far = Math.Sqrt(dax * dax + day * day) > Math.Sqrt(dbx * dbx + dby * dby) ? a : b;       // the end farther from the corner survives
            double fx = far.X - xx, fy = far.Y - xy, fl = Math.Sqrt(fx * fx + fy * fy);
            if (fl < 1e-9) throw new InvalidOperationException("That line has no length beyond the corner.");
            return (fx / fl, fy / fl, far.Id, far.Id == a.Id ? b.Id : a.Id);
        }
        var s1 = Side(l1, a1, b1, u1, v1); var s2 = Side(l2, a2, b2, u2, v2);
        double cos = s1.ux * s2.ux + s1.uy * s2.uy, theta = Math.Acos(Math.Clamp(cos, -1, 1));
        if (theta < 1e-3 || theta > Math.PI - 1e-3) throw new InvalidOperationException("Those lines are too close to straight for a corner.");
        double size = Size, tl = chamfer ? size : size / Math.Tan(theta / 2);                // distance from the corner to each tangent point
        var f1 = m.P(s1.far); var f2 = m.P(s2.far);
        double len1 = Math.Sqrt((f1.X - xx) * (f1.X - xx) + (f1.Y - xy) * (f1.Y - xy)), len2 = Math.Sqrt((f2.X - xx) * (f2.X - xx) + (f2.Y - xy) * (f2.Y - xy));
        if (tl >= len1 - 1e-6 || tl >= len2 - 1e-6) throw new InvalidOperationException($"The {(chamfer ? "distance" : "radius")} is too large for these lines. Try something smaller than {Math.Min(len1, len2) * (chamfer ? 1 : Math.Tan(theta / 2)):0.##} mm.");
        double t1x = xx + s1.ux * tl, t1y = xy + s1.uy * tl, t2x = xx + s2.ux * tl, t2y = xy + s2.uy * tl;
        if(preview!=null)
        {
            const uint color=0xFF3FCF8E;
            Vec3 W(double u,double v)=>sk.Plane.PointAt(u,v);
            preview.AddLine(W(f1.X,f1.Y),W(t1x,t1y),color,3);preview.AddLine(W(f2.X,f2.Y),W(t2x,t2y),color,3);
            if(chamfer)preview.AddLine(W(t1x,t1y),W(t2x,t2y),color,3);
            else
            {
                double bx=s1.ux+s2.ux,by=s1.uy+s2.uy,bl=Math.Sqrt(bx*bx+by*by),cd=size/Math.Sin(theta/2),cx=xx+bx/bl*cd,cy=xy+by/bl*cd;
                double start=Math.Atan2(t1y-cy,t1x-cx),end=Math.Atan2(t2y-cy,t2x-cx),sweep=end-start;
                while(sweep>Math.PI)sweep-=2*Math.PI;while(sweep< -Math.PI)sweep+=2*Math.PI;
                preview.AddPolyline(SketchGeo.ArcPoints(cx,cy,size,start,sweep).Select(q=>W(q.x,q.y)).ToList(),color,3);
            }
            return;
        }
        sk.Edit(s, chamfer ? "That chamfer" : "That fillet", () =>
        {
            sk.SelEntities.Clear(); sk.SelPoints.Clear();
            bool c1 = l1.Construction, h1 = SketchGeo.HasConstraint(m, CType.Horizontal, id1), vt1 = SketchGeo.HasConstraint(m, CType.Vertical, id1);
            bool h2 = SketchGeo.HasConstraint(m, CType.Horizontal, id2), vt2 = SketchGeo.HasConstraint(m, CType.Vertical, id2);
            int p1 = m.AddPoint(t1x, t1y), p2 = m.AddPoint(t2x, t2y);
            int n1 = m.AddLine(s1.far, p1, c1), n2 = m.AddLine(s2.far, p2, l2.Construction);
            if (h1) m.Add(CType.Horizontal, refs: n1); if (vt1) m.Add(CType.Vertical, refs: n1);
            if (h2) m.Add(CType.Horizontal, refs: n2); if (vt2) m.Add(CType.Vertical, refs: n2);
            if (chamfer) { m.AddLine(p1, p2, c1); }
            else
            {
                double bx = s1.ux + s2.ux, by = s1.uy + s2.uy, bl = Math.Sqrt(bx * bx + by * by);
                double cd = size / Math.Sin(theta / 2);
                int center = m.AddPoint(xx + bx / bl * cd, xy + by / bl * cd);
                double cross = (t1x - (xx + bx / bl * cd)) * (t2y - (xy + by / bl * cd)) - (t1y - (xy + by / bl * cd)) * (t2x - (xx + bx / bl * cd));
                int arc = cross > 0 ? m.AddArc(center, p1, p2, c1) : m.AddArc(center, p2, p1, c1);
                m.Add(CType.Tangent, 1, 0, false, n1, arc); m.Add(CType.Tangent, 1, 0, false, n2, arc);
            }
            m.RemoveEntity(id1); m.RemoveEntity(id2);
        });
    }

    public override void PointerMove(CadSession s, PointerInfo p)
    {
        var sk = Sk(s); sk.Cursor = null;
        if(_dragging&&p.Left){Size=Math.Max(.01,_startSize+(p.X-_downX-p.Y+_downY)*s.Camera.PixelSize/2);s.RaiseState();s.Invalidate();return;}
        int e = sk.PickEntity(s, p.X, p.Y, 9);
        _hover=e;
        if(sk.PlanePoint(s,p.X,p.Y) is { } point){_hu=point.u;_hv=point.v;}
        sk.HoverEntity = e >= 0 && sk.Model.EntityOrNull(e)?.Type == EntType.Line ? e : -1;
        if (sk.PlanePoint(s, p.X, p.Y) is { } w) s.Cursor3 = sk.Plane.PointAt(w.u, w.v);
        s.Invalidate();
    }

    public override bool Key(CadSession s, string key)
    {
        if (key.StartsWith("Char:") && key.Length == 6 && (char.IsDigit(key[5]) || key[5] == '.' || key[5] == ',')) { _typed += key[5] == ',' ? '.' : key[5]; s.Say($"{(chamfer ? "Distance" : "Radius")}: {_typed}_  (Enter to set)"); return true; }
        switch (key)
        {
            case "Back": if (_typed.Length == 0) return false; _typed = _typed[..^1]; s.Say($"{(chamfer ? "Distance" : "Radius")}: {_typed}_  (Enter to set)"); return true;
            case "Enter":
                if (_typed.Length == 0) return false;
                if (double.TryParse(_typed, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) && v > 0) { Size = v; _typed = ""; s.Say(Prompt); }
                return true;
            case "Escape": if (_first < 0 && _typed.Length == 0) return false; Reset(s); return true;
        }
        return false;
    }

    public override void BuildOverlay(CadSession s, Overlay o)
    {
        int second=_second>=0?_second:_hover;if(_first<0||second<0||second==_first||Sk(s).Model.EntityOrNull(second)?.Type!=EntType.Line)return;
        try{Apply(s,Sk(s),_first,_fu,_fv,second,_second>=0?_su:_hu,_second>=0?_sv:_hv,o);}catch(InvalidOperationException){}
    }
}
