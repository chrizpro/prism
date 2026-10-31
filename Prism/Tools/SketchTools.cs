using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core.Sketching;
using Prism.Kernel;

namespace Prism.Core;

/// <summary>Base for every tool that works inside a sketch. Tools talk to the model through <see cref="SketchContext"/>.</summary>
public abstract class SketchTool : IViewportTool
{
    /// <summary>Tool family, shared by its modes (used to highlight the ribbon button).</summary>
    public abstract string Name { get; }
    public virtual string Mode => "";
    public abstract string Prompt { get; }

    // ---- options: one ribbon tool per family, with its variants chosen in the viewport options bar
    /// <summary>The remembered option per tool family, so re-selecting a tool brings back the last variant used.</summary>
    public static readonly Dictionary<string, int> Remembered = [];
    public static int Recall(string family) => Remembered.GetValueOrDefault(family);
    public virtual IReadOnlyList<ToolOption> Options => [];
    public virtual int OptionIndex => 0;
    protected virtual void ApplyOption(int index) { }
    /// <summary>A small numeric stepper next to the options (e.g. polygon sides); null when the tool has none.</summary>
    public virtual string? StepperLabel => null;
    public virtual int StepperValue => 0;
    public virtual void Step(CadSession s, int delta) { }

    // ---- a tool can show its own controls in the sketch panel (selection summary, toggles, a distance, Apply...)
    public virtual string PanelTitle => Name;
    public virtual string PanelInputTitle => "Geometry";
    public virtual string PanelSettingsTitle => "Settings";
    public virtual string PanelActionTitle => "Apply";
    public virtual IReadOnlyList<ToolControl>? Panel => [new(ToolControlKind.Info,"step",Prompt)];
    public string SectionFor(ToolControl control) => control.Section.Length > 0 ? control.Section
        : control.Kind == ToolControlKind.Info || control.Id.StartsWith("draw-") ? PanelInputTitle
        : control.Kind == ToolControlKind.Button && control.Id != "rotate" && control.Id != "flip" ? PanelActionTitle : PanelSettingsTitle;
    public virtual void PanelSet(CadSession s, string id, object? value) { }

    public void SetOption(CadSession s, int index)
    {
        if (index < 0 || index >= Options.Count) return;
        if(this is ClickTool pending)pending.PreservePending(s);
        ApplyOption(index);
        Remembered[Name] = index;
        ResetState(s);
        s.Say(Prompt);
        s.RaiseState();
        s.Invalidate();
    }
    protected virtual void ResetState(CadSession s) { }

    protected static SketchContext Sk(CadSession s) => s.Sketch!;
    protected SnapInfo? Hot;

    public virtual void Activate(CadSession s) { }
    public virtual void Deactivate(CadSession s) { Sk(s).Cursor = null; Sk(s).HoverEntity = Sk(s).HoverPoint = -1; }
    public abstract bool PointerDown(CadSession s, PointerInfo p);
    public virtual void PointerMove(CadSession s, PointerInfo p)
    {
        var sk = Sk(s);
        Hot = sk.Snap(s, p.X, p.Y, !p.Alt);
        sk.Cursor = Hot;
        sk.Hint = null;
        if (Hot is { } h) s.Cursor3 = sk.Plane.PointAt(h.U, h.V);
        s.Invalidate();
    }
    public virtual void PointerUp(CadSession s, PointerInfo p) { }
    public virtual bool Key(CadSession s, string key) => false;
    public virtual void BuildOverlay(CadSession s, Overlay o) { }

    // ---- helpers shared by drawing tools
    protected static Vec3 W(SketchContext sk, double u, double v) => sk.Plane.PointAt(u, v);
    protected static double Dist(double ax, double ay, double bx, double by) => Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));
    protected static double Norm(double a) { while (a < 0) a += 2 * Math.PI; while (a >= 2 * Math.PI) a -= 2 * Math.PI; return a; }

    /// <summary>Circle through three points; null if they are collinear.</summary>
    protected static (double cx, double cy, double r)? Circum(double ax, double ay, double bx, double by, double cx, double cy)
    {
        double d = 2 * (ax * (by - cy) + bx * (cy - ay) + cx * (ay - by));
        if (Math.Abs(d) < 1e-9) return null;
        double ux = ((ax * ax + ay * ay) * (by - cy) + (bx * bx + by * by) * (cy - ay) + (cx * cx + cy * cy) * (ay - by)) / d;
        double uy = ((ax * ax + ay * ay) * (cx - bx) + (bx * bx + by * by) * (ax - cx) + (cx * cx + cy * cy) * (bx - ax)) / d;
        return (ux, uy, Dist(ux, uy, ax, ay));
    }

    protected static void Ring(Overlay o, SketchContext sk, double cx, double cy, double r, uint col, float w = 2f)
    {
        var pts = Enumerable.Range(0, 73).Select(i => { double a = i * 2 * Math.PI / 72; return W(sk, cx + r * Math.Cos(a), cy + r * Math.Sin(a)); }).ToList();
        o.AddPolyline(pts, col, w);
    }

    protected static void ArcPreview(Overlay o, SketchContext sk, double cx, double cy, double r, double a0, double sweep, uint col, float w = 2f)
    {
        int n = Math.Max(6, (int)(Math.Abs(sweep) / (2 * Math.PI) * 72));
        var pts = Enumerable.Range(0, n + 1).Select(i => { double a = a0 + sweep * i / n; return W(sk, cx + r * Math.Cos(a), cy + r * Math.Sin(a)); }).ToList();
        o.AddPolyline(pts, col, w);
    }

    protected const uint Rubber = 0xFF7AA7E8;
}

/// <summary>Shared click handling for tools that collect snapped points.</summary>
public abstract class ClickTool : SketchTool
{
    public override string PanelInputTitle => "Placement and dimensions";
    public override string PanelActionTitle => "Place geometry";
    protected virtual string PlacementAction => "Place point";
    protected readonly List<SnapInfo> Clicks = [];
    public Vec3? LastPlacedWorld(CadSession s) => Clicks.Count > 0 ? Sk(s).Plane.PointAt(Clicks[^1].U, Clicks[^1].V) : null;
    protected override void ResetState(CadSession s) { PreservePending(s); Clicks.Clear(); ResetTyping(); }

    // ---- typed entry: type the length / angle / width the tool is asking for, Tab moves between fields, Enter places it
    protected readonly record struct TypedField(string Label, string Unit);
    DateTime _lastPanelUpdate;
    CadSession? _panelSession;
    public override void Activate(CadSession s){_panelSession=s;}
    public override IReadOnlyList<ToolControl>? Panel
    {
        get
        {
            var controls=new List<ToolControl>{new(ToolControlKind.Info,"step",Prompt)};
            if(_panelSession is { } session&&TypedFields(session) is { } fields)
            {
                var live=Hot is { } hot?LiveValues(session,hot):null;
                for(int i=0;i<fields.Length;i++)
                {
                    double value=double.TryParse(_buf[i],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var parsed)?parsed:live!=null&&i<live.Length?live[i]:0;
                    controls.Add(new(ToolControlKind.Number,"draw-"+i,fields[i].Label,Number:value,Unit:fields[i].Unit));
                }
                controls.Add(new(ToolControlKind.Button,"place",PlacementAction,Primary:true));
            }
            return controls;
        }
    }
    public override void PanelSet(CadSession s,string id,object? value)
    {
        if(id.StartsWith("draw-")&&int.TryParse(id[5..],out int index)&&index>=0&&index<_buf.Length&&value is double number)
        {_buf[index]=number.ToString(System.Globalization.CultureInfo.InvariantCulture);_typing=true;UpdateTypedPreview(s);s.Invalidate();return;}
        if(id=="place"&&TypedFields(s) is { } fields)CommitTyped(s,fields);
        s.RaiseState();s.Invalidate();
    }
    protected bool Shift;
    readonly string[] _buf = ["", "", ""];
    int _field; bool _typing;
    protected bool Typing => _typing;
    /// <summary>The numbers that can be typed at this step; null when nothing can be typed yet.</summary>
    protected virtual TypedField[]? TypedFields(CadSession s) => null;
    /// <summary>The same numbers read from the pointer position (shown live while drawing).</summary>
    protected virtual double[]? LiveValues(CadSession s, SnapInfo h) => null;
    /// <summary>The point that typed values stand for. Entries are NaN where the user typed nothing (the pointer decides those).</summary>
    protected virtual SnapInfo? TypedPoint(CadSession s, SnapInfo h, double[] vals) => null;

    void UpdateTypedPreview(CadSession s)
    {
        if(!_typing||Hot is not { } h||TypedFields(s) is not { } fields)return;
        var values=fields.Select((_,i)=>double.TryParse(_buf[i],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var number)?number:double.NaN).ToArray();
        if(TypedPoint(s,h,values) is { } point){Hot=point;Sk(s).Cursor=point;s.Cursor3=Sk(s).Plane.PointAt(point.U,point.V);}
    }

    void ResetTyping() { _typing = false; _field = 0; for (int i = 0; i < _buf.Length; i++) _buf[i] = ""; }

    protected static (double len, double deg) Polar((double u, double v) o, SnapInfo h)
    {
        double dx = h.U - o.u, dy = h.V - o.v, a = Math.Atan2(dy, dx) * 180 / Math.PI; if (a < 0) a += 360; if (a >= 359.95) a = 0;
        return (Math.Sqrt(dx * dx + dy * dy), a);
    }

    protected static SnapInfo PolarPoint((double u, double v) o, SnapInfo h, double len, double deg)
    {
        var (l, a) = Polar(o, h);
        if (double.IsNaN(len)) len = l; if (double.IsNaN(deg)) deg = a;
        double r = deg * Math.PI / 180;
        return new SnapInfo(o.u + Math.Abs(len) * Math.Cos(r), o.v + Math.Abs(len) * Math.Sin(r), SnapKind.Grid, -1);
    }

    /// <summary>The unit direction (u, v) left of the line a-&gt;b, and the pointer's signed distance from that line.</summary>
    protected static ((double u, double v) n, double side) Side(SnapInfo a, SnapInfo b, SnapInfo h)
    {
        double dx = b.U - a.U, dy = b.V - a.V, l = Math.Max(Math.Sqrt(dx * dx + dy * dy), 1e-12);
        double nx = -dy / l, ny = dx / l;
        return ((nx, ny), (h.U - a.U) * nx + (h.V - a.V) * ny);
    }

    protected static double Typed(double[] v, int i, double fallback) => i < v.Length && !double.IsNaN(v[i]) ? v[i] : fallback;

    string FieldText(TypedField f, int i, double[]? live)
    {
        string val = _buf[i].Length > 0 ? _buf[i] : live != null && i < live.Length ? live[i].ToString(f.Unit == "°" ? "0.#" : "0.##", System.Globalization.CultureInfo.InvariantCulture) : "";
        bool active = _typing && i == _field;
        return $"{(active ? "▶ " : "")}{f.Label} {val}{(active ? "▏" : "")}{f.Unit}";
    }

    /// <summary>The length / angle read-out beside the pointer. Turns into an entry box once the user starts typing.</summary>
    public void BuildHud(CadSession s, Overlay o)
    {
        if (TypedFields(s) is not { } fields || Hot is not { } h) return;
        var live = LiveValues(s, h);
        var sk = Sk(s); var pt = W(sk, h.U, h.V);
        string text = string.Join("   ·   ", fields.Select((f, i) => FieldText(f, i, live)));
        if (ExtraHud(s) is { } extra) text += "   ·   " + extra;
        double ps = s.Camera.PixelSizeAt(pt);
        var at = pt + sk.Plane.U * (ps * (14 + 3.4 * text.Length)) + sk.Plane.V * (-ps * 24);
        o.AddLabel(at, text, true, _typing ? 5 : 4);
    }

    protected virtual string? ExtraHud(CadSession s) => null;

    void CommitTyped(CadSession s, TypedField[] fields)
    {
        var sk = Sk(s);
        var vals = new double[fields.Length];
        for (int i = 0; i < vals.Length; i++)
            vals[i] = double.TryParse(_buf[i].Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : double.NaN;
        if (Hot is not { } h) { s.Say("Move the pointer into the sketch first.", true); return; }
        var pt = TypedPoint(s, h, vals);
        if (pt is not { } p) {s.Say("Those values cannot form this drawing step. Adjust the values in the right panel.",true);return;}
        ResetTyping();
        Hot = p; sk.Cursor = p;
        Click(s, sk, p);
        sk.RecordPlacement(s, sk.Plane.PointAt(p.U, p.V));
        s.Invalidate();
    }

    bool TypingKey(CadSession s, string key)
    {
        if (TypedFields(s) is not { } fields) { if (_typing) ResetTyping(); return false; }
        if (_field >= fields.Length) _field = 0;
        if (key.StartsWith("Char:") && key.Length == 6)
        {
            char c = key[5];
            if (char.IsDigit(c) || c is '.' or ',') { _buf[_field] += c == ',' ? '.' : c; _typing = true; s.Invalidate(); return true; }
            return false;
        }
        switch (key)
        {
            case "Minus" when _buf[_field].Length == 0 && fields[_field].Unit == "°": _buf[_field] = "-"; _typing = true; s.Invalidate(); return true;
            case "Tab" when fields.Length > 1: _field = (_field + 1) % fields.Length; _typing = true; s.Invalidate(); return true;
        }
        if (!_typing) return false;
        switch (key)
        {
            case "Back": if (_buf[_field].Length > 0) _buf[_field] = _buf[_field][..^1]; else if (_buf.All(b => b.Length == 0)) _typing = false; s.Invalidate(); return true;
            case "Escape": ResetTyping(); s.Invalidate(); return true;
            case "Enter": CommitTyped(s, fields); return true;
        }
        return false;
    }

    public override bool PointerDown(CadSession s, PointerInfo p)
    {
        if (!p.Left) return false;
        var sk = Sk(s);
        _panelSession=s;Shift = SketchShortcuts.ShiftHeld(p);
        if(_typing&&TypedFields(s) is { } typedFields){CommitTyped(s,typedFields);s.RaiseState();return true;}
        ResetTyping();
        sk.TrackFrom = Clicks.Count > 0 ? (Clicks[^1].U, Clicks[^1].V) : null;
        if (sk.Snap(s, p.X, p.Y, !p.Alt) is not { } raw) return true;
        var sn = Constrain(s, raw);
        Hot = sn; sk.Cursor = sn;
        if (p.Clicks >= 2 && DoubleClickFinishes && Clicks.Count > 1) { Finish(s); return true; }
        Click(s, sk, sn);
        sk.RecordPlacement(s, sk.Plane.PointAt(sn.U, sn.V));
        s.RaiseState();s.Invalidate();
        return true;
    }

    public override void PointerMove(CadSession s, PointerInfo p)
    {
        var sk = Sk(s);
        _panelSession=s;Shift = SketchShortcuts.ShiftHeld(p);
        sk.TrackFrom = Clicks.Count > 0 ? (Clicks[^1].U, Clicks[^1].V) : null;
        var sn = sk.Snap(s, p.X, p.Y, !p.Alt);
        Hot = sn is { } a ? Constrain(s, a) : null;
        UpdateTypedPreview(s);
        sk.Cursor = Hot;
        sk.Hint = Hot is { } hinted ? Inference(s, hinted) : null;
        if (Hot is { } h) s.Cursor3 = sk.Plane.PointAt(h.U, h.V);
        if(!_typing&&DateTime.UtcNow-_lastPanelUpdate>TimeSpan.FromMilliseconds(90)){_lastPanelUpdate=DateTime.UtcNow;s.RaiseState();}
        s.Invalidate();
    }

    /// <summary>Feed a point as if the user had clicked it (used by scripting). Points on existing model points snap to them.</summary>
    public virtual void Feed(CadSession s, double u, double v)
    {
        var sk = Sk(s);
        int pid = -1;
        foreach (var p in sk.Model.Points) if (Dist(p.X, p.Y, u, v) < 1e-6) { pid = p.Id; break; }
        var sn = pid >= 0 ? new SnapInfo(u, v, SnapKind.Point, pid) : new SnapInfo(u, v, SnapKind.Grid, -1);
        Click(s, sk, sn);
        sk.RecordPlacement(s, sk.Plane.PointAt(sn.U, sn.V));
    }
    /// <summary>Complete a multi-click shape that does not finish by itself (lines, splines).</summary>
    public virtual void FinishFeed(CadSession s, bool closed = false) { if (Clicks.Count > 0) Finish(s); }

    /// <summary>Tools can nudge the snap (e.g. horizontal/vertical inference).</summary>
    protected SnapInfo LockDirection(SnapInfo sn,SnapInfo anchor)
    {
        bool horizontal=Math.Abs(sn.U-anchor.U)>=Math.Abs(sn.V-anchor.V);
        double u=horizontal?sn.U:anchor.U,v=horizontal?anchor.V:sn.V;
        return Math.Abs(sn.U-u)<1e-7&&Math.Abs(sn.V-v)<1e-7?sn:new(u,v,SnapKind.Grid,-1);
    }
    protected virtual SnapInfo Constrain(CadSession s, SnapInfo sn)
        => Shift&&Clicks.Count>0?LockDirection(sn,Clicks[^1]):sn;
    /// <summary>What the next click would add besides the curve itself ("Horizontal", "Concentric"...), shown beside the pointer.</summary>
    protected virtual string? Inference(CadSession s, SnapInfo hot) => null;

    /// <summary>A new round curve centred on an existing center (in this sketch or a visible one) is concentric with it.</summary>
    protected static string? CenterInference(SketchContext sk, SnapInfo hot)
    {
        if (hot.Kind == SnapKind.Point && sk.RoundAtCenter(hot.Id) is { } round) return $"Concentric with {round.Type.ToString().ToLowerInvariant()}";
        if (hot.Kind is SnapKind.External or SnapKind.Projected && hot.Note is { } note && note.EndsWith("· center", StringComparison.Ordinal)) return "Concentric with " + note[..^"· center".Length].Trim();
        return null;
    }

    /// <summary>Pull a free radius to the radius of a nearby circle or arc, so the two stay equal. Returns the curve it matched.</summary>
    protected static (SnapInfo point, SkEntity match)? EqualRadius(CadSession s, SnapInfo center, SnapInfo sn)
    {
        if (sn.Kind != SnapKind.Grid) return null;
        var sk = Sk(s); double r = Dist(center.U, center.V, sn.U, sn.V);
        double ps = s.Camera.PixelSizeAt(sk.Plane.PointAt(center.U, center.V));
        if (r < 6 * ps || sk.RoundOfRadius(r, 6 * ps) is not { } match) return null;
        double target = sk.Model.Radius(match);
        return (new SnapInfo(center.U + (sn.U - center.U) / r * target, center.V + (sn.V - center.V) / r * target, SnapKind.Grid, -1), match);
    }
    protected virtual bool DoubleClickFinishes => false;
    protected abstract void Click(CadSession s, SketchContext sk, SnapInfo sn);
    protected virtual void Finish(CadSession s) { PreservePending(s); Clicks.Clear(); }
    protected virtual void Cancel(CadSession s) { PreservePending(s); Clicks.Clear(); s.Invalidate(); }

    public override bool Key(CadSession s, string key)
    {
        if (TypingKey(s, key)) {UpdateTypedPreview(s);s.RaiseState();return true;}
        switch (key)
        {
            case "Escape": if (Clicks.Count > 0) { Cancel(s); return true; } return false;
            case "Enter": if (Clicks.Count > 0) { Finish(s); return true; } return false;
            case "RightClick": if (Clicks.Count > 1) Finish(s); else Cancel(s); return true;
            case "Back": if (Clicks.Count > 0) { Clicks.RemoveAt(Clicks.Count - 1); s.Invalidate(); return true; } return false;
        }
        return false;
    }

    public void ForgetDeletedInput(CadSession s) { Clicks.Clear();ResetTyping();ResetState(s); }

    public virtual void PreservePending(CadSession s)
    {
        if(Clicks.Count==0||s.Sketch is not { } sk)return;
        var pending=Clicks.ToList();
        var loose=pending.Where(sn=>!sk.Model.Points.Any(p=>Math.Abs(p.X-sn.U)<1e-7&&Math.Abs(p.Y-sn.V)<1e-7&&sk.Model.Entities.Any(e=>sk.Model.PointsOf(e).Contains(p.Id)))).ToList();
        if(loose.Count==0)return;
        sk.Edit(s,"Keeping placed points",()=>
        {
            foreach(var sn in loose)
            {
                int id=sk.PointFor(sn);
                if(!sk.Model.Entities.Any(e=>sk.Model.PointsOf(e).Contains(id)))sk.Model.AddPointEntity(id,sk.Construction);
            }
        });
    }
    public override void Deactivate(CadSession s) { PreservePending(s);Clicks.Clear(); ResetTyping(); base.Deactivate(s); }
}

// =============================================================== Line
public sealed class LineTool(int variant = 0) : ClickTool
{
    bool _mid = variant == 1;      // draw from the midpoint outwards
    static readonly ToolOption[] Opts = [new("Chain", "Click point after point to draw connected lines", "line"), new("Midpoint", "Click the middle of the line, then one end", "line-mid")];
    public override IReadOnlyList<ToolOption> Options => Opts;
    public override int OptionIndex => _mid ? 1 : 0;
    protected override void ApplyOption(int index) => _mid = index == 1;
    protected override void ResetState(CadSession s) { _from = -1; _first = null; _chainStart = -1; _inferH = _inferV = false; _refLine = -1; base.ResetState(s); }

    int _from = -1;                 // model point the next segment starts from
    int _chainStart = -1;           // first point of this chain (to detect closing)
    SnapInfo? _first;               // first click before any geometry exists
    bool _inferH, _inferV;
    int _refLine = -1; bool _refPerp;      // an existing line the pointer is aligned with (parallel or perpendicular)
    public override string Name => "Line";
    public override string Mode => _mid ? "Midpoint" : "Chain";
    public override string Prompt => _mid ? "Line (midpoint): click where the middle of the line goes, then click one end; the other end is mirrored. Type the full length (Tab for the angle) and press Enter. Hold Alt for no snapping."
        : "Line: click the start, then each next point. Click the start again to close; right-click or Esc to finish. Hold Alt for no snapping. Type a length (Tab for the angle) and press Enter to place a point exactly.";

    (double u, double v)? Origin(CadSession s)
    {
        if (_from >= 0 && Sk(s).Model.PointOrNull(_from) is { } p) return (p.X, p.Y);
        return _first is { } f ? (f.U, f.V) : null;
    }

    protected override TypedField[]? TypedFields(CadSession s) => Origin(s) == null ? null : [new("Length", " mm"), new("Angle", "°")];
    protected override double[]? LiveValues(CadSession s, SnapInfo h) { if (Origin(s) is not { } o) return null; var (l, a) = Polar(o, h); return [_mid ? 2 * l : l, a]; }
    protected override SnapInfo? TypedPoint(CadSession s, SnapInfo h, double[] v)
    {
        if (Origin(s) is not { } o) return null;
        var p = PolarPoint(o, h, _mid && !double.IsNaN(v[0]) ? v[0] / 2 : v[0], v[1]);
        if (Dist(o.u, o.v, p.U, p.V) < 1e-6) { s.Say("A line needs a length above zero.", true); return null; }
        // a typed horizontal or vertical angle also becomes a constraint
        _inferH = _inferV = false; _refLine = -1;
        if (Math.Abs(p.V - o.v) < 1e-7 * Math.Max(1, Math.Abs(p.U - o.u))) { _inferH = true; p = p with { V = o.v }; }
        else if (Math.Abs(p.U - o.u) < 1e-7 * Math.Max(1, Math.Abs(p.V - o.v))) { _inferV = true; p = p with { U = o.u }; }
        return p;
    }

    protected override SnapInfo Constrain(CadSession s, SnapInfo sn)
    {
        _inferH = _inferV = false; _refLine = -1;
        if (Origin(s) is not { } o) return sn;
        if(Shift)
        {
            if(Math.Abs(sn.U-o.u)>=Math.Abs(sn.V-o.v)){_inferH=true;return Math.Abs(sn.V-o.v)<1e-7?sn:new(sn.U,o.v,SnapKind.Grid,-1);}
            _inferV=true;return Math.Abs(sn.U-o.u)<1e-7?sn:new(o.u,sn.V,SnapKind.Grid,-1);
        }
        if(sn.Kind!=SnapKind.Grid)return sn;
        double ps = s.Camera.PixelSizeAt(Sk(s).Plane.PointAt(o.u, o.v));
        if (Math.Abs(sn.V - o.v) < 6 * ps) { _inferH = true; return sn with { V = o.v }; }
        if (Math.Abs(sn.U - o.u) < 6 * ps) { _inferV = true; return sn with { U = o.u }; }
        return _mid ? sn : AlignWithLine(s, o, sn, ps);
    }

    /// <summary>Within a few degrees of parallel or perpendicular to an existing line: turn the new line exactly that way and remember to constrain it.</summary>
    SnapInfo AlignWithLine(CadSession s, (double u, double v) o, SnapInfo sn, double ps)
    {
        var sk = Sk(s);
        double dx = sn.U - o.u, dy = sn.V - o.v, len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 20 * ps) return sn;
        const double tolerance = 0.052;       // sin 3 degrees
        double best = tolerance; double nx = 0, ny = 0;
        foreach (var e in sk.Model.Entities)
        {
            if (e.Type != EntType.Line || !sk.EntityVisible(e)) continue;
            var a = sk.Model.P(e.A); var b = sk.Model.P(e.B);
            double ex = b.X - a.X, ey = b.Y - a.Y, el = Math.Sqrt(ex * ex + ey * ey);
            if (el < 1e-9 || Math.Abs(ex) < 1e-9 * el || Math.Abs(ey) < 1e-9 * el) continue;       // horizontal and vertical lines are already inferred
            ex /= el; ey /= el;
            double along = (dx * ex + dy * ey) / len, across = (-dx * ey + dy * ex) / len;
            bool touchesStart = e.A == _from || e.B == _from;
            if (!touchesStart && Math.Abs(across) < best)
            { best = Math.Abs(across); _refLine = e.Id; _refPerp = false; (nx, ny) = (Math.Sign(along) * ex, Math.Sign(along) * ey); }
            if (Math.Abs(along) < best)
            { best = Math.Abs(along); _refLine = e.Id; _refPerp = true; (nx, ny) = (Math.Sign(across) * -ey, Math.Sign(across) * ex); }
        }
        return _refLine < 0 ? sn : new SnapInfo(o.u + nx * len, o.v + ny * len, SnapKind.Grid, -1);
    }

    protected override string? Inference(CadSession s, SnapInfo hot)
        => _inferH ? "Horizontal" : _inferV ? "Vertical" : _refLine >= 0 ? (_refPerp ? "Perpendicular" : "Parallel") : null;

    protected override void Click(CadSession s, SketchContext sk, SnapInfo sn)
    {
        if (_from < 0 && _first == null) { _first = sn; Clicks.Add(sn); return; }
        if (_mid)
        {
            var mid = _first!.Value; _first = null; Clicks.Clear();
            if (Dist(mid.U, mid.V, sn.U, sn.V) < 1e-6) return;
            bool h = _inferH, v = _inferV; _inferH = _inferV = false;
            sk.Edit(s, "That line", () =>
            {
                int m = sk.PointFor(mid), a = sk.PointFor(sn), b = sk.Model.AddPoint(2 * mid.U - sn.U, 2 * mid.V - sn.V);
                int line = sk.Model.AddLine(a, b, sk.Construction);
                sk.Model.Add(CType.Midpoint, 0, 0, false, m, line);      // the first click stays the middle however the line is edited
                if (h) sk.Model.Add(CType.Horizontal, refs: line); else if (v) sk.Model.Add(CType.Vertical, refs: line);
            });
            return;
        }
        bool inferH = _inferH, inferV = _inferV, perpendicular = _refPerp; int reference = _refLine;
        int next = -1, startPt = -1;
        bool Draw(bool aligned) => sk.Edit(s, "That line", () =>
        {
            int a = _from >= 0 ? _from : sk.PointFor(_first!.Value);
            int b = sk.PointFor(sn);
            if (a == b) return;
            startPt = a;
            int line = sk.Model.AddLine(a, b, sk.Construction);
            if (inferH) sk.Model.Add(CType.Horizontal, refs: line);
            else if (inferV) sk.Model.Add(CType.Vertical, refs: line);
            else if (aligned && reference >= 0) sk.Model.Add(perpendicular ? CType.Perpendicular : CType.Parallel, refs: [line, reference]);
            next = b;
        });
        // if the alignment cannot be honoured (it fights another constraint) the line is still drawn, just without it
        bool ok = Draw(true) || reference >= 0 && Draw(false);
        _refLine = -1;
        if (!ok || next < 0) return;
        if (_chainStart < 0) _chainStart = startPt;
        bool closed = sn.Kind == SnapKind.Point && sk.Model.CoincidentGroup(sn.Id).Contains(_chainStart) && Clicks.Count >= 2;
        _from = next;
        Clicks.Add(sn);
        if (closed) Finish(s);
    }

    public override void Feed(CadSession s, double u, double v)
    {
        // exact alignment with the previous point becomes a horizontal/vertical constraint
        _inferH = _inferV = false;
        if (Origin(s) is { } o) { _inferH = Math.Abs(v - o.v) < 1e-9 && Math.Abs(u - o.u) > 1e-9; _inferV = Math.Abs(u - o.u) < 1e-9 && Math.Abs(v - o.v) > 1e-9; }
        base.Feed(s, u, v);
    }

    protected override void Finish(CadSession s) { _from = -1; _first = null; _chainStart = -1; _inferH = _inferV = false; base.Finish(s); s.Invalidate(); }
    protected override void Cancel(CadSession s) { _from = -1; _first = null; _chainStart = -1; base.Cancel(s); }

    public override void BuildOverlay(CadSession s, Overlay o)
    {
        var sk = Sk(s);
        if (Hot is not { } h || Origin(s) is not { } f) return;
        if (_mid)
        {
            o.AddLine(W(sk, 2 * f.u - h.U, 2 * f.v - h.V), W(sk, h.U, h.V), Rubber, 2f);
            o.AddDot(W(sk, f.u, f.v), 0xFFFF8A1F, 8);
            if (_inferH) o.AddLine(W(sk, f.u - 1e4, f.v), W(sk, f.u + 1e4, f.v), 0x60FFC53D, 1.2f);
            if (_inferV) o.AddLine(W(sk, f.u, f.v - 1e4), W(sk, f.u, f.v + 1e4), 0x60FFC53D, 1.2f);
            return;
        }
        o.AddLine(W(sk, f.u, f.v), W(sk, h.U, h.V), Rubber, 2f);
        if (_inferH) o.AddLine(W(sk, f.u - 1e4, f.v), W(sk, f.u + 1e4, f.v), 0x60FFC53D, 1.2f);
        if (_inferV) o.AddLine(W(sk, f.u, f.v - 1e4), W(sk, f.u, f.v + 1e4), 0x60FFC53D, 1.2f);
        if (_refLine >= 0 && sk.Model.EntityOrNull(_refLine) is { } reference)       // light up the line this one is aligned with
            o.AddLine(W(sk, sk.Model.P(reference.A).X, sk.Model.P(reference.A).Y), W(sk, sk.Model.P(reference.B).X, sk.Model.P(reference.B).Y), 0xB0FFC53D, 3f);
    }
}

// =============================================================== Rectangle
/// <summary>One choice in a tool's options bar.</summary>
public sealed record ToolOption(string Label, string Tip, string Icon);

public enum ToolControlKind { Info, Toggle, Number, Button, Text }

/// <summary>One control in a tool's panel. Toggle uses On, Number uses Number and Unit, Button may be Primary.</summary>
public sealed record ToolControl(ToolControlKind Kind, string Id, string Label, string Text = "", bool On = false, double Number = 0, string Unit = "", bool Primary = false, bool Enabled = true, string Section = "");

public enum RectMode { TwoCorner, Center, ThreePoint }

public sealed class RectTool(RectMode mode) : ClickTool
{
    protected override string PlacementAction => mode == RectMode.ThreePoint && Clicks.Count == 1 ? "Set base side" : "Create rectangle";
    public override string Name => "Rectangle";
    public override string Mode => mode switch { RectMode.TwoCorner => "2-point", RectMode.Center => "Center", _ => "3-point" };
    static readonly ToolOption[] Opts = [new("2-Point", "Two opposite corners", "rect"), new("Center", "Center, then a corner", "rect-center"), new("3-Point", "Two points for one side (any angle), then the width", "rect-3pt")];
    public override IReadOnlyList<ToolOption> Options => Opts;
    public override int OptionIndex => (int)mode;
    protected override void ApplyOption(int index) => mode = (RectMode)index;
    public override string Prompt => mode switch
    {
        RectMode.TwoCorner => "Rectangle (2-point): click one corner, then the opposite corner. Type the width, Tab, the height, then Enter.",
        RectMode.Center => "Rectangle (center): click the center, then a corner.",
        _ => "Rectangle (3-point): click two corners of one side (any angle; press H or V to lock that side horizontal or vertical), then the width.",
    };

    double _angle;
    bool _rotating;double _rotateU,_rotateV;
    double Orientation => Shift ? 0 : _angle;
    public override IReadOnlyList<ToolControl>? Panel => mode==RectMode.ThreePoint?base.Panel:[..base.Panel!,new(ToolControlKind.Number,"orientation","Orientation",Number:_angle,Unit:"degrees",Text:"Rotate the rectangle on its sketch plane. R rotates the live preview."),new(ToolControlKind.Button,"rotate","Rotate preview (R)",Enabled:Clicks.Count==1)];
    public override void PanelSet(CadSession s,string id,object? value)
    { if(id=="orientation"&&value is double angle&&double.IsFinite(angle)){_angle=angle;s.Invalidate();}else if(id=="rotate")ToggleRotation(s);else base.PanelSet(s,id,value); }
    SnapInfo Turn(SnapInfo p,SnapInfo origin,double degrees)
    {
        double t=degrees*Math.PI/180,x=p.U-origin.U,y=p.V-origin.V;
        return new(origin.U+x*Math.Cos(t)-y*Math.Sin(t),origin.V+x*Math.Sin(t)+y*Math.Cos(t),SnapKind.Grid,-1);
    }
    void ToggleRotation(CadSession s)
    {
        if(mode==RectMode.ThreePoint||Clicks.Count!=1||Hot is not { } h)return;
        var local=Turn(h,Clicks[0],-Orientation);_rotateU=local.U-Clicks[0].U;_rotateV=local.V-Clicks[0].V;
        _rotating=!_rotating;s.Say(_rotating?"Move to orient the rectangle, then click to place it.":Prompt);s.RaiseState();s.Invalidate();
    }
    int _placedAxis;
    int _axis;      // 0 free, 1 first side horizontal, 2 first side vertical (3-point rectangle)
    protected override void ResetState(CadSession s) { _rotating=false;_axis = _placedAxis = 0; base.ResetState(s); }
    protected override string? ExtraHud(CadSession s) => mode == RectMode.ThreePoint && Clicks.Count == 1 ? (_axis == 1 ? "🔒 horizontal (H)" : _axis == 2 ? "🔒 vertical (V)" : "H / V = lock side") : null;

    public override bool Key(CadSession s, string key)
    {
        if(key=="Char:R"&&mode!=RectMode.ThreePoint&&Clicks.Count==1){ToggleRotation(s);return true;}
        if (key is "Char:H" or "Char:V" && mode == RectMode.ThreePoint)
        {
            int want = key == "Char:H" ? 1 : 2;
            _axis = _axis == want ? 0 : want;
            s.Say(_axis == 0 ? "Side unlocked: draw at any angle." : _axis == 1 ? "First side locked horizontal. Press H again to unlock." : "First side locked vertical. Press V again to unlock.");
            s.Invalidate(); return true;
        }
        return base.Key(s, key);
    }

    protected override SnapInfo Constrain(CadSession s, SnapInfo sn)
    {
        if(_rotating&&Clicks.Count==1)
        {
            _angle=Math.Atan2(sn.V-Clicks[0].V,sn.U-Clicks[0].U)*180/Math.PI;
            return Turn(new(Clicks[0].U+_rotateU,Clicks[0].V+_rotateV,SnapKind.Grid,-1),Clicks[0],Orientation);
        }
        if(mode!=RectMode.ThreePoint&&Shift&&Clicks.Count==1)
        {
            var center=Clicks[0];double du=sn.U-center.U,dv=sn.V-center.V,side=Math.Max(Math.Abs(du),Math.Abs(dv));
            double u=center.U+(du<0?-side:side),v=center.V+(dv<0?-side:side);
            return Math.Abs(sn.U-u)<1e-7&&Math.Abs(sn.V-v)<1e-7?sn:new(u,v,SnapKind.Grid,-1);
        }
        if (mode != RectMode.ThreePoint || Clicks.Count != 1) return sn;
        var a = Clicks[0];
        int axis=Shift?(Math.Abs(sn.U-a.U)>=Math.Abs(sn.V-a.V)?1:2):_axis;
        if(axis==0)return sn;
        return axis == 1 ? new SnapInfo(sn.U, a.V, SnapKind.Grid, -1) : new SnapInfo(a.U, sn.V, SnapKind.Grid, -1);
    }

    protected override TypedField[]? TypedFields(CadSession s)
    {
        if (Clicks.Count == 0) return null;
        if (mode != RectMode.ThreePoint) return [new("Width", " mm"), new("Height", " mm")];
        return Clicks.Count == 1 ? [new("Length", " mm"), new("Angle", "°")] : [new("Width", " mm")];
    }

    protected override double[]? LiveValues(CadSession s, SnapInfo h)
    {
        if (Clicks.Count == 0) return null; var a = Clicks[0];
        if(mode!=RectMode.ThreePoint)h=Turn(h,a,-Orientation);
        if (mode == RectMode.TwoCorner) return [Math.Abs(h.U - a.U), Math.Abs(h.V - a.V)];
        if (mode == RectMode.Center) return [2 * Math.Abs(h.U - a.U), 2 * Math.Abs(h.V - a.V)];
        if (Clicks.Count == 1) { var (l, g) = Polar((a.U, a.V), h); return [l, g]; }
        return [Math.Abs(Side(a, Clicks[1], h).side)];
    }

    protected override SnapInfo? TypedPoint(CadSession s,SnapInfo h,double[] v)
    {
        if(mode==RectMode.ThreePoint||Clicks.Count==0)return TypedLocal(s,h,v);
        var result=TypedLocal(s,Turn(h,Clicks[0],-Orientation),v);
        return result is { } point?Turn(point,Clicks[0],Orientation):null;
    }
    SnapInfo? TypedLocal(CadSession s, SnapInfo h, double[] v)
    {
        if (Clicks.Count == 0) return null; var a = Clicks[0];
        double sx = h.U < a.U ? -1 : 1, sy = h.V < a.V ? -1 : 1;
        if (mode == RectMode.TwoCorner) return new SnapInfo(a.U + sx * Typed(v, 0, Math.Abs(h.U - a.U)), a.V + sy * Typed(v, 1, Math.Abs(h.V - a.V)), SnapKind.Grid, -1);
        if (mode == RectMode.Center) return new SnapInfo(a.U + sx * Typed(v, 0, 2 * Math.Abs(h.U - a.U)) / 2, a.V + sy * Typed(v, 1, 2 * Math.Abs(h.V - a.V)) / 2, SnapKind.Grid, -1);
        if (Clicks.Count == 1)
        {
            var pp = PolarPoint((a.U, a.V), h, v[0], v[1]);
            return _axis == 1 ? pp with { V = a.V } : _axis == 2 ? pp with { U = a.U } : pp;
        }
        var (n, side) = Side(a, Clicks[1], h); double w = Typed(v, 0, Math.Abs(side)) * (side < 0 ? -1 : 1);
        return new SnapInfo(a.U + n.u * w, a.V + n.v * w, SnapKind.Grid, -1);
    }

    protected override void Click(CadSession s, SketchContext sk, SnapInfo sn)
    {
        if(mode==RectMode.ThreePoint&&Clicks.Count==1)
            _placedAxis=Shift?(Math.Abs(sn.U-Clicks[0].U)>=Math.Abs(sn.V-Clicks[0].V)?1:2):_axis;
        Clicks.Add(sn);
        int need = mode == RectMode.ThreePoint ? 3 : 2;
        if (Clicks.Count < need) return;
        var c = Clicks.ToList(); Clicks.Clear();
        int lockedSide=_placedAxis;_placedAxis=0;_rotating=false;
        double angle=mode==RectMode.ThreePoint?0:Orientation;
        var originalClicks=c.ToList();
        if(Math.Abs(angle)>1e-9)c[1]=Turn(c[1],c[0],-angle);
        bool degenerate=mode==RectMode.ThreePoint
            ? Dist(c[0].U,c[0].V,c[1].U,c[1].V)<1e-7 || Math.Abs((c[1].U-c[0].U)*(c[2].V-c[0].V)-(c[1].V-c[0].V)*(c[2].U-c[0].U))<1e-7
            : Math.Abs(c[1].U-c[0].U)<1e-7 || Math.Abs(c[1].V-c[0].V)<1e-7;
        if(degenerate){Clicks.AddRange(originalClicks);PreservePending(s);Clicks.Clear();s.Say("A rectangle needs two nonzero sides. Placed points have been kept.",true);return;}
        sk.Edit(s, "That rectangle", () =>
        {
            var m = sk.Model; var rect = new List<int>();
            if (mode == RectMode.TwoCorner)
            {
                int a = sk.PointFor(c[0]), cc = Math.Abs(angle)>1e-9 ? m.AddPoint(c[1].U,c[1].V) : sk.PointFor(c[1]);
                int b = m.AddPoint(c[1].U, c[0].V), d = m.AddPoint(c[0].U, c[1].V);
                int l1 = m.AddLine(a, b, sk.Construction), l2 = m.AddLine(b, cc, sk.Construction), l3 = m.AddLine(cc, d, sk.Construction), l4 = m.AddLine(d, a, sk.Construction); rect.AddRange([l1, l2, l3, l4]);
                if(Math.Abs(angle)<1e-9){m.Add(CType.Horizontal, refs: l1);m.Add(CType.Horizontal, refs: l3);m.Add(CType.Vertical, refs: l2);m.Add(CType.Vertical, refs: l4);}
                else {m.Add(CType.Parallel,refs:[l1,l3]);m.Add(CType.Parallel,refs:[l2,l4]);m.Add(CType.Perpendicular,refs:[l1,l2]);}
            }
            else if (mode == RectMode.Center)
            {
                double hw = Math.Abs(c[1].U - c[0].U), hh = Math.Abs(c[1].V - c[0].V);
                int ctr = sk.PointFor(c[0]);
                int a = m.AddPoint(c[0].U - hw, c[0].V - hh), b = m.AddPoint(c[0].U + hw, c[0].V - hh), cc = m.AddPoint(c[0].U + hw, c[0].V + hh), d = m.AddPoint(c[0].U - hw, c[0].V + hh);
                int l1 = m.AddLine(a, b, sk.Construction), l2 = m.AddLine(b, cc, sk.Construction), l3 = m.AddLine(cc, d, sk.Construction), l4 = m.AddLine(d, a, sk.Construction); rect.AddRange([l1, l2, l3, l4]);
                if(Math.Abs(angle)<1e-9){m.Add(CType.Horizontal, refs: l1);m.Add(CType.Horizontal, refs: l3);m.Add(CType.Vertical, refs: l2);m.Add(CType.Vertical, refs: l4);}
                else {m.Add(CType.Parallel,refs:[l1,l3]);m.Add(CType.Parallel,refs:[l2,l4]);m.Add(CType.Perpendicular,refs:[l1,l2]);}
                int diag = m.AddLine(a, cc, true);                 // construction diagonal keeps the rectangle centred on the click
                rect.Add(diag);
                m.Add(CType.Midpoint, 0, 0, false, ctr, diag);
            }
            else
            {
                double dx = c[1].U - c[0].U, dy = c[1].V - c[0].V, len = Math.Max(Dist(c[0].U, c[0].V, c[1].U, c[1].V), 1e-9);
                double nx = -dy / len, ny = dx / len;
                double w = (c[2].U - c[0].U) * nx + (c[2].V - c[0].V) * ny;       // signed width from the third click
                int a = sk.PointFor(c[0]), b = sk.PointFor(c[1]);
                int cc = m.AddPoint(c[1].U + nx * w, c[1].V + ny * w), d = m.AddPoint(c[0].U + nx * w, c[0].V + ny * w);
                int l1 = m.AddLine(a, b, sk.Construction), l2 = m.AddLine(b, cc, sk.Construction), l3 = m.AddLine(cc, d, sk.Construction), l4 = m.AddLine(d, a, sk.Construction); rect.AddRange([l1, l2, l3, l4]);
                m.Add(CType.Perpendicular, refs: [l1, l2]); m.Add(CType.Perpendicular, refs: [l2, l3]); m.Add(CType.Perpendicular, refs: [l3, l4]);
                if (lockedSide == 1) m.Add(CType.Horizontal, refs: l1); else if (lockedSide == 2) m.Add(CType.Vertical, refs: l1);
            }
            if(Math.Abs(angle)>1e-9)
                foreach(int id in rect.SelectMany(id=>m.PointsOf(m.E(id))).Distinct())
                {var p=m.P(id);var rotated=Turn(new(p.X,p.Y,SnapKind.Grid,-1),c[0],angle);p.X=rotated.U;p.Y=rotated.V;}
            if(Shift&&mode!=RectMode.ThreePoint)m.Add(CType.Equal,refs:[rect[0],rect[1]]);
            m.NewGroup("Rectangle", rect);
        });
    }

    public override void BuildOverlay(CadSession s, Overlay o)
    {
        var sk = Sk(s);
        if (Clicks.Count == 0 || Hot is not { } h) return;
        var a = Clicks[0];
        double angle=mode==RectMode.ThreePoint?0:Orientation;
        if(mode!=RectMode.ThreePoint)h=Turn(h,a,-angle);
        Prism.Kernel.Vec3 Preview(double u,double v){var p=Turn(new(u,v,SnapKind.Grid,-1),a,angle);return W(sk,p.U,p.V);}
        if (mode == RectMode.TwoCorner)
        {
            o.AddPolyline([Preview(a.U,a.V),Preview(h.U,a.V),Preview(h.U,h.V),Preview(a.U,h.V)], Rubber, 2f, true);
        }
        else if (mode == RectMode.Center)
        {
            double hw = Math.Abs(h.U - a.U), hh = Math.Abs(h.V - a.V);
            o.AddPolyline([Preview(a.U-hw,a.V-hh),Preview(a.U+hw,a.V-hh),Preview(a.U+hw,a.V+hh),Preview(a.U-hw,a.V+hh)], Rubber, 2f, true);
        }
        else if (Clicks.Count == 1) o.AddLine(W(sk, a.U, a.V), W(sk, h.U, h.V), Rubber, 2f);
        else
        {
            var b = Clicks[1];
            double dx = b.U - a.U, dy = b.V - a.V, len = Math.Max(Dist(a.U, a.V, b.U, b.V), 1e-9), nx = -dy / len, ny = dx / len;
            double w = (h.U - a.U) * nx + (h.V - a.V) * ny;
            o.AddPolyline([W(sk, a.U, a.V), W(sk, b.U, b.V), W(sk, b.U + nx * w, b.V + ny * w), W(sk, a.U + nx * w, a.V + ny * w)], Rubber, 2f, true);
        }
    }
}

// =============================================================== Circle
public enum CircleMode { Ellipse, CenterRadius, TwoPoint, ThreePoint }

public sealed class CircleTool(CircleMode mode) : ClickTool
{
    protected override string PlacementAction => mode == CircleMode.Ellipse && !Locked ? Clicks.Count == 1 ? "Set major axis" : "Create ellipse"
        : mode == CircleMode.ThreePoint && Clicks.Count == 1 ? "Set second point" : "Create circle";
    int _equalTo = -1;      // a circle or arc the radius being dragged out matches
    protected override SnapInfo Constrain(CadSession s,SnapInfo sn)
    {
        _equalTo = -1;
        bool radiusStep = Clicks.Count == 1 && (mode == CircleMode.CenterRadius || mode == CircleMode.Ellipse && Locked);
        if (!radiusStep || EqualRadius(s, Clicks[0], sn) is not { } equal) return sn;
        _equalTo = equal.match.Id; return equal.point;
    }
    protected override string? Inference(CadSession s, SnapInfo hot)
        => Clicks.Count == 0 && mode is CircleMode.CenterRadius or CircleMode.Ellipse ? CenterInference(Sk(s), hot) : _equalTo >= 0 ? "Equal radius" : null;
    public override string Name => mode == CircleMode.Ellipse ? "Ellipse" : "Circle";
    public override string Mode => mode switch { CircleMode.CenterRadius => "Circle (center)", CircleMode.TwoPoint => "Circle (2-point)", CircleMode.ThreePoint => "Circle (3-point)", _ => _lock ? "Circle (locked)" : "Ellipse" };
    static readonly ToolOption[] Opts = [new("Ellipse", "Center, the end of one axis, then the other radius. Shift or L locks it into a circle", "ellipse"), new("Circle", "Center, then a point on the circle", "circle"), new("2-Point", "The two ends of a diameter", "circle-2pt"), new("3-Point", "Three points on the circle", "circle-3pt")];
    bool _lock;
    /// <summary>Proportions locked: the ellipse becomes a circle (hold Shift or press L).</summary>
    bool Locked => _lock || Shift;
    protected override void ResetState(CadSession s) { _lock = false; base.ResetState(s); }

    public override bool Key(CadSession s, string key)
    {
        if (key is "Char:L" or "Char:l" && mode == CircleMode.Ellipse)
        {
            _lock = !_lock;
            s.Say(_lock ? "Proportions locked: the ellipse is a circle. Press L again to unlock." : "Proportions unlocked: draw any ellipse.");
            s.RaiseState(); s.Invalidate(); return true;
        }
        return base.Key(s, key);
    }

    protected override string? ExtraHud(CadSession s) => mode == CircleMode.Ellipse ? (Locked ? "🔒 circle (L)" : "L = lock to circle") : null;

    protected override TypedField[]? TypedFields(CadSession s)
    {
        if (Clicks.Count == 0) return null;
        switch (mode)
        {
            case CircleMode.Ellipse:
                if (Clicks.Count == 1) return [new(Locked ? "Radius" : "Major radius", " mm"), new("Angle", "°")];
                return [new(Locked ? "Radius" : "Minor radius", " mm")];
            case CircleMode.CenterRadius: return [new("Radius", " mm")];
            case CircleMode.TwoPoint: return Clicks.Count == 1 ? [new("Diameter", " mm"), new("Angle", "°")] : null;
            default: return null;
        }
    }

    protected override double[]? LiveValues(CadSession s, SnapInfo h)
    {
        if (Clicks.Count == 0) return null; var a = Clicks[0];
        var (l, g) = Polar((a.U, a.V), h);
        switch (mode)
        {
            case CircleMode.Ellipse: return Clicks.Count == 1 ? [l, g] : [Locked ? Dist(a.U, a.V, Clicks[1].U, Clicks[1].V) : Math.Abs(Side(a, Clicks[1], h).side)];
            case CircleMode.CenterRadius: return [l];
            default: return [l, g];
        }
    }

    protected override SnapInfo? TypedPoint(CadSession s, SnapInfo h, double[] v)
    {
        if (Clicks.Count == 0) return null; var a = Clicks[0];
        if (mode == CircleMode.Ellipse && Clicks.Count == 2)
        {
            var (n, side) = Side(a, Clicks[1], h); double m = Typed(v, 0, Math.Abs(side)) * (side < 0 ? -1 : 1);
            return new SnapInfo(a.U + n.u * m, a.V + n.v * m, SnapKind.Grid, -1);
        }
        return PolarPoint((a.U, a.V), h, v[0], Typed(v, 1, double.NaN));
    }
    public override string Prompt => mode switch
    {
        CircleMode.CenterRadius => "Circle (center): click the center, then a point on the circle. Type a radius and press Enter for an exact size.",
        CircleMode.TwoPoint => "Circle (2-point): click two opposite points of the diameter.",
        CircleMode.Ellipse => "Ellipse: click the center, the end of one axis (sets size and tilt), then a point for the other radius. Hold Shift or press L to lock the proportions into a circle. Type numbers for exact sizes.",
        _ => "Circle (3-point): click three points on the circle.",
    };

    protected override void Click(CadSession s, SketchContext sk, SnapInfo sn)
    {
        Clicks.Add(sn);
        bool circleNow = mode == CircleMode.Ellipse && Locked;       // locked proportions: the first axis end is already the radius
        int need = mode is CircleMode.ThreePoint or CircleMode.Ellipse ? 3 : 2;
        if (circleNow) need = 2;
        if (Clicks.Count < need) return;
        var c = Clicks.ToList(); Clicks.Clear();
        var kind = circleNow ? CircleMode.CenterRadius : mode;
        if (kind == CircleMode.Ellipse)
        {
            double maj = Dist(c[0].U, c[0].V, c[1].U, c[1].V), minor = MinorRadius(c[0], c[1], c[2]);
            if (maj < 1e-6 || minor < 1e-6) { s.Say("Both radii of an ellipse must be above zero.", true); return; }
            sk.Edit(s, "That ellipse", () =>
            {
                int ctr = sk.PointFor(c[0]), end = sk.PointFor(c[1]);
                sk.Model.AddEllipse(ctr, end, minor, sk.Construction);
            });
            return;
        }
        double cx, cy, r;
        if (kind == CircleMode.CenterRadius) { cx = c[0].U; cy = c[0].V; r = Dist(cx, cy, c[1].U, c[1].V); }
        else if (kind == CircleMode.TwoPoint) { cx = (c[0].U + c[1].U) / 2; cy = (c[0].V + c[1].V) / 2; r = Dist(c[0].U, c[0].V, c[1].U, c[1].V) / 2; }
        else
        {
            if (Circum(c[0].U, c[0].V, c[1].U, c[1].V, c[2].U, c[2].V) is not { } k) { s.Say("Those three points are in a line.", true); return; }
            (cx, cy, r) = k;
        }
        if (r < 1e-6) return;
        int equalTo = kind == CircleMode.CenterRadius ? _equalTo : -1; _equalTo = -1;
        var concentric = kind == CircleMode.CenterRadius && c[0].Kind == SnapKind.Point ? sk.RoundAtCenter(c[0].Id) : null;
        bool Draw(bool withEqual) => sk.Edit(s, "That circle", () =>
        {
            int ctr = kind == CircleMode.CenterRadius ? sk.PointFor(c[0]) : sk.Model.AddPoint(cx, cy);
            int circle = sk.Model.AddCircle(ctr, r, sk.Construction);
            // points the user snapped onto existing geometry stay attached to the circle
            if (kind == CircleMode.ThreePoint) foreach (var click in c) { if (click.Kind == SnapKind.Point) sk.Model.Add(CType.PointOnEntity, 0, 0, false, click.Id, circle); }
            if (kind == CircleMode.CenterRadius && c[1].Kind == SnapKind.Point) sk.Model.Add(CType.PointOnEntity, 0, 0, false, c[1].Id, circle);
            if (withEqual && equalTo >= 0) sk.Model.Add(CType.Equal, refs: [circle, equalTo]);
        });
        if (!(Draw(true) || equalTo >= 0 && Draw(false))) return;
        // the center is the existing one, so the two share it: say so, since nothing new appears in the constraint list
        if (concentric != null) s.Say($"Concentric with the {concentric.Type.ToString().ToLowerInvariant()}: they share one center.");
        else if (equalTo >= 0) s.Say("Equal radius: the new circle follows the size of the one it matched.");
    }

    /// <summary>Distance of the third click from the line through the centre and the axis end.</summary>
    static double MinorRadius(SnapInfo ctr, SnapInfo axis, SnapInfo third)
    {
        double dx = axis.U - ctr.U, dy = axis.V - ctr.V, l = Math.Max(Dist(0, 0, dx, dy), 1e-12);
        return Math.Abs((third.U - ctr.U) * -dy / l + (third.V - ctr.V) * dx / l);
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
        if (mode == CircleMode.Ellipse)
        {
            var a = Clicks[0];
            if (Clicks.Count == 1)
            {
                o.AddLine(W(sk, a.U, a.V), W(sk, h.U, h.V), Rubber, 1.5f);
                if (Locked) Ring(o, sk, a.U, a.V, Dist(a.U, a.V, h.U, h.V), Rubber);
            }
            else
            {
                var b = Clicks[1]; double maj = Dist(a.U, a.V, b.U, b.V), minor = Locked ? maj : MinorRadius(a, b, h);
                o.AddPolyline(EllipsePts(sk, a.U, a.V, maj, Math.Max(minor, 1e-6), Math.Atan2(b.V - a.V, b.U - a.U)), Rubber, 2f);
                o.AddLine(W(sk, a.U, a.V), W(sk, b.U, b.V), 0x907AA7E8, 1.2f);
            }
            foreach (var c in Clicks) o.AddDot(W(sk, c.U, c.V), 0xFFFF8A1F, 8);
            return;
        }
        if (mode == CircleMode.CenterRadius)
        {
            var a = Clicks[0]; double r = Dist(a.U, a.V, h.U, h.V);
            Ring(o, sk, a.U, a.V, r, Rubber); o.AddLine(W(sk, a.U, a.V), W(sk, h.U, h.V), 0x907AA7E8, 1.2f);
        }
        else if (mode == CircleMode.TwoPoint)
        {
            var a = Clicks[0]; double r = Dist(a.U, a.V, h.U, h.V) / 2;
            Ring(o, sk, (a.U + h.U) / 2, (a.V + h.V) / 2, r, Rubber); o.AddLine(W(sk, a.U, a.V), W(sk, h.U, h.V), 0x907AA7E8, 1.2f);
        }
        else if (Clicks.Count == 1) o.AddLine(W(sk, Clicks[0].U, Clicks[0].V), W(sk, h.U, h.V), Rubber, 1.5f);
        else if (Circum(Clicks[0].U, Clicks[0].V, Clicks[1].U, Clicks[1].V, h.U, h.V) is { } k)
        {
            Ring(o, sk, k.cx, k.cy, k.r, Rubber);
            o.AddLabel(W(sk, h.U, h.V), $"R {k.r:0.##}");
        }
        foreach (var c in Clicks) o.AddDot(W(sk, c.U, c.V), 0xFFFF8A1F, 8);
    }
}

// =============================================================== Polygon
public sealed class PolygonTool(bool circumscribed) : ClickTool
{
    protected override string PlacementAction => "Create polygon";
    public override IReadOnlyList<ToolControl>? Panel => [..base.Panel!,new(ToolControlKind.Number,"sides","Sides",Number:_sides)];
    public override void PanelSet(CadSession s,string id,object? value){if(id=="sides"&&value is double count){_sides=Math.Clamp((int)count,3,64);s.Invalidate();}else base.PanelSet(s,id,value);}

    int _sides = Remembered.GetValueOrDefault("PolygonSides", 6);
    public int Sides { get => _sides; set => _sides = Math.Clamp(value, 3, 32); }
    public override string Name => "Polygon";
    public override string Mode => circumscribed ? "Circumscribed" : "Inscribed";
    static readonly ToolOption[] Opts = [new("Inscribed", "Corners lie on the guide circle", "polygon"), new("Circumscribed", "Sides touch the guide circle", "polygon-out")];
    public override IReadOnlyList<ToolOption> Options => Opts;
    public override int OptionIndex => circumscribed ? 1 : 0;
    protected override void ApplyOption(int index) => circumscribed = index == 1;
    public override string? StepperLabel => "Sides";
    public override int StepperValue => _sides;
    public override void Step(CadSession s, int delta) { _sides = Math.Clamp(_sides + delta, 3, 32); Remembered["PolygonSides"] = _sides; s.Say(Prompt); s.RaiseState(); s.Invalidate(); }
    public override string Prompt => $"Polygon ({Mode.ToLower()}): click the center, then a {(circumscribed ? "side midpoint" : "corner")}. Sides: {_sides}  (press + / - to change).";

    public override bool Key(CadSession s, string key)
    {
        if (key == "Plus" && !Typing) { Step(s, 1); return true; }
        if (key == "Minus" && !Typing) { Step(s, -1); return true; }
        return base.Key(s, key);
    }

    protected override TypedField[]? TypedFields(CadSession s) => Clicks.Count == 0 ? null : [new(circumscribed ? "Side distance" : "Radius", " mm"), new("Angle", "°")];
    protected override double[]? LiveValues(CadSession s, SnapInfo h) { if (Clicks.Count == 0) return null; var (l, g) = Polar((Clicks[0].U, Clicks[0].V), h); return [l, g]; }
    protected override SnapInfo? TypedPoint(CadSession s, SnapInfo h, double[] v) => Clicks.Count == 0 ? null : PolarPoint((Clicks[0].U, Clicks[0].V), h, v[0], v[1]);

    protected override void Click(CadSession s, SketchContext sk, SnapInfo sn)
    {
        Clicks.Add(sn);
        if (Clicks.Count < 2) return;
        var c = Clicks.ToList(); Clicks.Clear();
        double d = Dist(c[0].U, c[0].V, c[1].U, c[1].V); if (d < 1e-6) return;
        double rot = Math.Atan2(c[1].V - c[0].V, c[1].U - c[0].U);
        double R = circumscribed ? d / Math.Cos(Math.PI / _sides) : d;
        double start = circumscribed ? rot - Math.PI / _sides : rot;
        sk.Edit(s, "That polygon", () =>
        {
            var m = sk.Model; int ctr = sk.PointFor(c[0]);
            var pts = new List<int>();
            for (int i = 0; i < _sides; i++) { double a = start + 2 * Math.PI * i / _sides; pts.Add(m.AddPoint(c[0].U + R * Math.Cos(a), c[0].V + R * Math.Sin(a))); }
            var lines = new List<int>();
            for (int i = 0; i < _sides; i++) lines.Add(m.AddLine(pts[i], pts[(i + 1) % _sides], sk.Construction));
            int guide = m.AddCircle(ctr, circumscribed ? d : R, true);     // construction circle controls the size
            for (int i = 0; i < _sides; i++)
            {
                if (circumscribed) m.Add(CType.Tangent, 1, 0, false, lines[i], guide);
                else m.Add(CType.PointOnEntity, 0, 0, false, pts[i], guide);
                if (i > 0) m.Add(CType.Equal, refs: [lines[0], lines[i]]);
            }
            m.NewGroup("Polygon", [.. lines, guide]);
        });
    }

    public override void BuildOverlay(CadSession s, Overlay o)
    {
        var sk = Sk(s);
        if (Clicks.Count == 0 || Hot is not { } h) return;
        var a = Clicks[0]; double d = Dist(a.U, a.V, h.U, h.V);
        double rot = Math.Atan2(h.V - a.V, h.U - a.U);
        double R = circumscribed ? d / Math.Cos(Math.PI / _sides) : d, st = circumscribed ? rot - Math.PI / _sides : rot;
        var pts = Enumerable.Range(0, _sides).Select(i => W(sk, a.U + R * Math.Cos(st + 2 * Math.PI * i / _sides), a.V + R * Math.Sin(st + 2 * Math.PI * i / _sides))).ToList();
        o.AddPolyline(pts, Rubber, 2f, true);
        Ring(o, sk, a.U, a.V, circumscribed ? d : R, 0x507AA7E8, 1f);
        o.AddLabel(W(sk, h.U, h.V), $"{_sides} sides");
    }
}

// =============================================================== Slot (center to center)
public sealed class SlotTool : ClickTool
{
    protected override string PlacementAction => Clicks.Count == 1 ? "Set centerline" : "Create slot";
    protected override SnapInfo Constrain(CadSession s,SnapInfo sn)
        =>Shift&&Clicks.Count==1?LockDirection(sn,Clicks[0]):sn;
    public override string Name => "Slot";
    public override string Prompt => "Slot: click the first arc center, the second arc center, then a point that sets the width. Type numbers for exact sizes.";

    protected override TypedField[]? TypedFields(CadSession s) => Clicks.Count == 0 ? null : Clicks.Count == 1 ? [new("Length", " mm"), new("Angle", "°")] : [new("Width", " mm")];
    protected override double[]? LiveValues(CadSession s, SnapInfo h)
    {
        if (Clicks.Count == 0) return null;
        if (Clicks.Count == 1) { var (l, g) = Polar((Clicks[0].U, Clicks[0].V), h); return [l, g]; }
        return [2 * Math.Abs(Side(Clicks[0], Clicks[1], h).side)];
    }
    protected override SnapInfo? TypedPoint(CadSession s, SnapInfo h, double[] v)
    {
        if (Clicks.Count == 0) return null; var a = Clicks[0];
        if (Clicks.Count == 1) return PolarPoint((a.U, a.V), h, v[0], v[1]);
        var (n, side) = Side(a, Clicks[1], h); double w = Typed(v, 0, 2 * Math.Abs(side)) / 2 * (side < 0 ? -1 : 1);
        return new SnapInfo(a.U + n.u * w, a.V + n.v * w, SnapKind.Grid, -1);
    }

    protected override void Click(CadSession s, SketchContext sk, SnapInfo sn)
    {
        Clicks.Add(sn);
        if (Clicks.Count < 3) return;
        var c = Clicks.ToList(); Clicks.Clear();
        double dx = c[1].U - c[0].U, dy = c[1].V - c[0].V, len = Dist(c[0].U, c[0].V, c[1].U, c[1].V);
        if (len < 1e-6) return;
        double ux = dx / len, uy = dy / len, nx = -uy, ny = ux;
        double r = Math.Abs((c[2].U - c[0].U) * nx + (c[2].V - c[0].V) * ny);
        if (r < 1e-6) return;
        sk.Edit(s, "That slot", () =>
        {
            var m = sk.Model;
            int c1 = sk.PointFor(c[0]), c2 = sk.PointFor(c[1]);
            int p1p = m.AddPoint(c[0].U + nx * r, c[0].V + ny * r), p1m = m.AddPoint(c[0].U - nx * r, c[0].V - ny * r);
            int p2p = m.AddPoint(c[1].U + nx * r, c[1].V + ny * r), p2m = m.AddPoint(c[1].U - nx * r, c[1].V - ny * r);
            int top = m.AddLine(p1p, p2p, sk.Construction), bottom = m.AddLine(p1m, p2m, sk.Construction);
            int arc2 = m.AddArc(c2, p2m, p2p, sk.Construction);       // counter-clockwise lower -> upper: bulges away from centre 1
            int arc1 = m.AddArc(c1, p1p, p1m, sk.Construction);       // counter-clockwise upper -> lower: bulges away from centre 2
            m.Add(CType.Tangent, 1, 0, false, top, arc1); m.Add(CType.Tangent, 1, 0, false, top, arc2);
            m.Add(CType.Tangent, 1, 0, false, bottom, arc1); m.Add(CType.Tangent, 1, 0, false, bottom, arc2);
            m.Add(CType.Equal, refs: [arc1, arc2]);
            m.NewGroup("Slot", [top, bottom, arc1, arc2]);
        });
    }

    public override void BuildOverlay(CadSession s, Overlay o)
    {
        var sk = Sk(s);
        if (Clicks.Count == 0 || Hot is not { } h) return;
        var a = Clicks[0];
        if (Clicks.Count == 1) { o.AddLine(W(sk, a.U, a.V), W(sk, h.U, h.V), Rubber, 1.5f); return; }
        var b = Clicks[1];
        double dx = b.U - a.U, dy = b.V - a.V, len = Math.Max(Dist(a.U, a.V, b.U, b.V), 1e-9), ux = dx / len, uy = dy / len, nx = -uy, ny = ux;
        double r = Math.Abs((h.U - a.U) * nx + (h.V - a.V) * ny);
        double ang = Math.Atan2(uy, ux);
        o.AddLine(W(sk, a.U + nx * r, a.V + ny * r), W(sk, b.U + nx * r, b.V + ny * r), Rubber, 2f);
        o.AddLine(W(sk, a.U - nx * r, a.V - ny * r), W(sk, b.U - nx * r, b.V - ny * r), Rubber, 2f);
        ArcPreview(o, sk, b.U, b.V, r, ang - Math.PI / 2, Math.PI, Rubber);
        ArcPreview(o, sk, a.U, a.V, r, ang + Math.PI / 2, Math.PI, Rubber);
        
    }
}

// =============================================================== Spline
public sealed class SplineTool(bool controlPoints) : ClickTool
{
    protected override string PlacementAction => "Add spline point";
    public override string Name => "Spline";
    public override string Mode => controlPoints ? "Control point" : "Fit point";
    static readonly ToolOption[] Opts = [new("Fit Points", "The curve passes through every point", "spline"), new("Control Points", "The curve is pulled toward the control polygon", "spline-control")];
    public override IReadOnlyList<ToolOption> Options => Opts;
    public override int OptionIndex => controlPoints ? 1 : 0;
    protected override void ApplyOption(int index) => controlPoints = index == 1;
    public override string Prompt => controlPoints
        ? "Spline (control points): click the control points. Right-click or Enter to finish."
        : "Spline (fit points): click points the curve passes through. Right-click or Enter to finish; click the first point to close.";
    protected override bool DoubleClickFinishes => true;

    protected override TypedField[]? TypedFields(CadSession s) => Clicks.Count == 0 ? null : [new("Length", " mm"), new("Angle", "°")];
    protected override double[]? LiveValues(CadSession s, SnapInfo h) { if (Clicks.Count == 0) return null; var (l, g) = Polar((Clicks[^1].U, Clicks[^1].V), h); return [l, g]; }
    protected override SnapInfo? TypedPoint(CadSession s, SnapInfo h, double[] v) => Clicks.Count == 0 ? null : PolarPoint((Clicks[^1].U, Clicks[^1].V), h, v[0], v[1]);

    protected override void Click(CadSession s, SketchContext sk, SnapInfo sn)
    {
        if (!controlPoints && Clicks.Count >= 3 && sn.Kind == SnapKind.Point && Clicks[0].Kind == SnapKind.Point && sn.Id == Clicks[0].Id) { Commit(s, true); return; }
        if (Clicks.Count > 0 && Dist(Clicks[^1].U, Clicks[^1].V, sn.U, sn.V) < 1e-6) return;
        Clicks.Add(sn);
    }

    public override void PreservePending(CadSession s)
    {
        if(Clicks.Count>=2)Commit(s,false);else base.PreservePending(s);
    }
    protected override void Finish(CadSession s) => Commit(s, false);
    public override void FinishFeed(CadSession s, bool closed = false) => Commit(s, closed, false);

    /// <param name="lastRepeatsFirst">True when closing by clicking the first point again (that last click is not a new point).</param>
    void Commit(CadSession s, bool closed, bool lastRepeatsFirst = true)
    {
        var sk = Sk(s);
        if(Clicks.Count<2) { base.PreservePending(s);Clicks.Clear();s.Invalidate();return; }
        var c = Clicks.ToList(); Clicks.Clear();
        if (closed && lastRepeatsFirst && c.Count > 0) c.RemoveAt(c.Count - 1);
        if (c.Count < 2) { s.Invalidate(); return; }
        sk.Edit(s, "That spline", () =>
        {
            var ids = c.Select(x => sk.PointFor(x)).ToList();
            sk.Model.AddSpline(ids, closed && !controlPoints, controlPoints, sk.Construction);
        });
    }

    public override void BuildOverlay(CadSession s, Overlay o)
    {
        var sk = Sk(s);
        foreach (var c in Clicks) o.AddDot(W(sk, c.U, c.V), 0xFF4C94FF, 8);
        if (Clicks.Count == 0 || Hot is not { } h) return;
        var all = Clicks.Select(c => (c.U, c.V)).ToList(); all.Add((h.U, h.V));
        if (all.Count < 3) { o.AddLine(W(sk, all[0].U, all[0].V), W(sk, all[1].U, all[1].V), Rubber, 2f); return; }
        var tmp = new SketchModel(); var ids = all.Select(p => tmp.AddPoint(p.U, p.V)).ToList();
        int eid = tmp.AddSpline(ids, false, controlPoints);
        var e = tmp.Entities.First(x => x.Id == eid);
        o.AddPolyline(tmp.SplineSamples(e).Select(q => W(sk, q.x, q.y)).ToList(), Rubber, 2f);
        if (SketchContext.ShowCurvature && all.Count >= 3)
        {
            // curvature of the real curve the kernel will make (not just the preview polyline), so you can fair it while you place points
            try
            {
                var p3 = all.Select(p => W(sk, p.U, p.V)).ToList();
                var shape = controlPoints ? Occ.NurbsCurve(p3, Math.Min(3, p3.Count - 1)) : Occ.Spline(p3);
                CurvatureComb.Draw(o, s, Occ.CurveSamples(shape));
            }
            catch (Exception ex) when (ex is not OutOfMemoryException) { }
        }
        if (controlPoints) o.AddPolyline(all.Select(p => W(sk, p.U, p.V)).ToList(), 0x906F86A6, 1.2f);
    }
}

// =============================================================== Point
public sealed class PointTool : ClickTool
{
    public override string Name => "Point";
    public override string Prompt => "Point: click to place a reference point.";
    protected override void Click(CadSession s, SketchContext sk, SnapInfo sn)
    {
        sk.Edit(s, "That point", () => { int p = sk.PointFor(sn); sk.Model.AddPointEntity(p); });
    }
}
