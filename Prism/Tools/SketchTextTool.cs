using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core.Sketching;
using Prism.Kernel;

namespace Prism.Core;

/// <summary>
/// Text, done in the viewport: drag a box (its height is the letter height) or just click, then type right there and watch the text appear.
/// Drag the dot on the frame's corner to resize it, drag inside the frame to move it, and click away or press Ctrl+Enter to place it.
/// The panel offers the same things for exact numbers (text, font, bold, height, spacing). Placed text becomes clean sketch curves: straight
/// edges are lines and every smooth curve of a letter is one spline.
/// </summary>
public sealed class SketchTextTool : SketchTool
{
    enum Phase { Idle, Boxing, Editing, Scaling, Moving }

    public override string Name => "Text";
    public override string Prompt => _phase switch
    {
        Phase.Idle => "Text: drag a box for the letter height, or click, then type. The words appear as you type.",
        Phase.Boxing => "Text: release to start typing.",
        _ => "Type your text. Enter starts a new line. Drag the corner dot to resize, drag the frame to move. Click away or press Ctrl+Enter to place; Esc cancels.",
    };
    public override string PanelInputTitle => "Text";
    public override string PanelSettingsTitle => "Font and size";
    public override string PanelActionTitle => "Place text";

    static string _text = "", _font = TextOutline.Fonts[0];
    static double _height = 10, _spacing;
    static bool _bold;

    Phase _phase;
    (double u, double v) _origin, _anchor, _grab;
    double _scaleStartHeight, _scaleStartDist;
    (string key, List<List<(double u, double v)>> poly, double x0, double y0, double x1, double y1)? _cache;

    public bool Editing => _phase is Phase.Editing or Phase.Scaling or Phase.Moving;

    public override IReadOnlyList<ToolControl>? Panel =>
    [
        new(ToolControlKind.Info, "step", Prompt),
        new(ToolControlKind.Text, "text", "Text", Text: _text, Section: PanelInputTitle),
        new(ToolControlKind.Button, "font", "Font: " + _font + " (click for the next)", Text: "Only fonts installed on this computer can be used.", Section: PanelSettingsTitle),
        new(ToolControlKind.Toggle, "bold", "Bold", On: _bold, Section: PanelSettingsTitle),
        new(ToolControlKind.Number, "height", "Letter height", Number: Math.Round(_height, 3), Unit: "mm", Section: PanelSettingsTitle),
        new(ToolControlKind.Number, "spacing", "Extra letter spacing", Number: _spacing, Unit: "mm", Section: PanelSettingsTitle),
        new(ToolControlKind.Button, "place", "Place text (Ctrl+Enter)", Primary: true, Enabled: Editing && _text.Trim().Length > 0, Section: PanelActionTitle),
    ];

    public override void PanelSet(CadSession s, string id, object? value)
    {
        switch (id)
        {
            case "text" when value is string t: _text = t.Replace("\r", ""); break;
            case "font": _font = TextOutline.Fonts[(Array.IndexOf(TextOutline.Fonts, _font) + 1) % TextOutline.Fonts.Length]; break;
            case "bold" when value is bool b: _bold = b; break;
            case "height" when value is double h && h > 0: _height = h; break;
            case "spacing" when value is double sp: _spacing = sp; break;
            case "place": Place(s); break;
        }
        s.RaiseState(); s.Invalidate();
    }

    protected override void ResetState(CadSession s) { _phase = Phase.Idle; _text = ""; }
    public override void Activate(CadSession s) { _phase = Phase.Idle; s.Say(Prompt); }
    public override void Deactivate(CadSession s) { if (Editing && _text.Trim().Length > 0) Place(s); _phase = Phase.Idle; base.Deactivate(s); }

    // ---------------------------------------------------------------- text shape
    /// <summary>The letter outlines at the origin (text, or a faint "Text" while nothing is typed yet) with their bounding box.</summary>
    (List<List<(double u, double v)>> poly, double x0, double y0, double x1, double y1, bool placeholder) Shape(CadSession s)
    {
        bool placeholder = _text.Trim().Length == 0;
        string text = placeholder ? "Text" : _text, key = $"{text}|{_font}|{_bold}|{_height:0.####}|{_spacing:0.####}";
        if (_cache is { } c && c.key == key) return (c.poly, c.x0, c.y0, c.x1, c.y1, placeholder);
        List<List<(double u, double v)>> poly = [];
        try { poly = TextOutline.Build(text, _font, _bold, _height, _spacing); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { s.Say(ex.Message, true); }
        double x0 = 0, y0 = 0, x1 = _height * 2, y1 = _height;
        if (poly.Count > 0)
        {
            x0 = poly.Min(p => p.Min(q => q.u)); x1 = poly.Max(p => p.Max(q => q.u));
            y0 = poly.Min(p => p.Min(q => q.v)); y1 = poly.Max(p => p.Max(q => q.v));
        }
        _cache = (key, poly, x0, y0, x1, y1);
        return (poly, x0, y0, x1, y1, placeholder);
    }

    (double u, double v) FrameCorner(CadSession s)
    {
        var sh = Shape(s); double pad = Math.Max(_height * 0.15, 0.3);
        return (_origin.u + sh.x1 + pad, _origin.v + sh.y1 + pad);
    }

    void Place(CadSession s)
    {
        var sk = Sk(s);
        if (_text.Trim().Length == 0) { _phase = Phase.Idle; return; }
        try
        {
            var contours = TextOutline.BuildClean(_text, _font, _bold, _height, _spacing);
            if (contours.Count == 0) { s.Say("None of those characters exist in this font.", true); return; }
            double ou = _origin.u, ov = _origin.v;
            sk.Edit(s, "That text", () =>
            {
                var m = sk.Model; var ids = new List<int>();
                foreach (var c in contours)
                {
                    var pts = c.Nodes.Select(n => m.AddPoint(ou + n.u, ov + n.v)).ToList();
                    foreach (var r in c.Runs)
                    {
                        if (!r.Curve) { ids.Add(m.AddLine(pts[r.Start], pts[r.End], sk.Construction)); continue; }
                        var fit = new List<int>();
                        if (r.Closed) { fit.Add(pts[r.Start]); foreach (var q in r.Inner) fit.Add(m.AddPoint(ou + q.u, ov + q.v)); }
                        else { fit.Add(pts[r.Start]); foreach (var q in r.Inner) fit.Add(m.AddPoint(ou + q.u, ov + q.v)); fit.Add(pts[r.End]); }
                        ids.Add(m.AddSpline(fit, r.Closed, false, sk.Construction));
                    }
                }
                m.NewGroup("Text", ids);
            });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { s.Say(ex.Message, true); return; }
        _phase = Phase.Idle; _text = "";
        s.Say(Prompt); s.RaiseState(); s.Invalidate();
    }

    // ---------------------------------------------------------------- pointer
    bool NearHandle(CadSession s, PointerInfo p)
    {
        var sk = Sk(s); var c = FrameCorner(s);
        return s.Camera.Project(sk.Plane.PointAt(c.u, c.v)) is { } h && Math.Abs(h.x - p.X) < 12 && Math.Abs(h.y - p.Y) < 12;
    }

    bool InsideFrame((double u, double v) at, CadSession s)
    {
        var sh = Shape(s); double pad = Math.Max(_height * 0.15, 0.3);
        return at.u >= _origin.u + sh.x0 - pad && at.u <= _origin.u + sh.x1 + pad && at.v >= _origin.v + sh.y0 - pad && at.v <= _origin.v + sh.y1 + pad;
    }

    public override bool PointerDown(CadSession s, PointerInfo p)
    {
        if (!p.Left) return false;
        var sk = Sk(s);
        if (Editing)
        {
            if (NearHandle(s, p))
            {
                var c = FrameCorner(s); _scaleStartHeight = _height;
                _scaleStartDist = Math.Max(Dist(_origin.u, _origin.v, c.u, c.v), 1e-6); _phase = Phase.Scaling; return true;
            }
            if (sk.PlanePoint(s, p.X, p.Y) is { } at && InsideFrame(at, s)) { _grab = (at.u - _origin.u, at.v - _origin.v); _phase = Phase.Moving; return true; }
            if (_text.Trim().Length > 0) Place(s); else _phase = Phase.Idle;     // clicking away places the text, then a new one can start
        }
        if (sk.Snap(s, p.X, p.Y, !p.Alt) is not { } sn) return true;
        _anchor = _origin = (sn.U, sn.V); _phase = Phase.Boxing; s.RaiseState(); s.Invalidate();
        return true;
    }

    public override void PointerMove(CadSession s, PointerInfo p)
    {
        var sk = Sk(s);
        var sn = sk.Snap(s, p.X, p.Y, !p.Alt);
        Hot = sn; sk.Cursor = _phase is Phase.Idle ? sn : null;
        if (sn is { } h) s.Cursor3 = sk.Plane.PointAt(h.U, h.V);
        switch (_phase)
        {
            case Phase.Idle: if (sn is { } i) _origin = (i.U, i.V); break;
            case Phase.Boxing when sn is { } b:
                _origin = (Math.Min(_anchor.u, b.U), Math.Min(_anchor.v, b.V));
                if (Math.Abs(b.V - _anchor.v) > 0.5) _height = Math.Round(Math.Abs(b.V - _anchor.v), 2);
                break;
            case Phase.Scaling when sk.PlanePoint(s, p.X, p.Y) is { } at:
                _height = Math.Clamp(Math.Round(_scaleStartHeight * Dist(_origin.u, _origin.v, at.u, at.v) / _scaleStartDist, 2), 0.2, 5000); s.RaiseState(); break;
            case Phase.Moving when sk.PlanePoint(s, p.X, p.Y) is { } mv: _origin = (mv.u - _grab.u, mv.v - _grab.v); break;
        }
        s.Invalidate();
    }

    public override void PointerUp(CadSession s, PointerInfo p)
    {
        if (_phase == Phase.Boxing) { _phase = Phase.Editing; _text = ""; s.Say(Prompt); s.RaiseState(); }
        else if (_phase is Phase.Scaling or Phase.Moving) { _phase = Phase.Editing; s.RaiseState(); }
        s.Invalidate();
    }

    // ---------------------------------------------------------------- keyboard (the window forwards typed characters while editing)
    public void TypeText(CadSession s, string typed)
    {
        if (!Editing) return;
        foreach (var ch in typed) if (!char.IsControl(ch)) _text += ch;
        s.RaiseState(); s.Invalidate();
    }

    public override bool Key(CadSession s, string key)
    {
        if (!Editing) return false;
        switch (key)
        {
            case "Back": if (_text.Length > 0) _text = _text[..^1]; break;
            case "Enter": _text += "\n"; break;
            case "CtrlEnter": Place(s); break;
            case "Escape": case "RightClick": _phase = Phase.Idle; _text = ""; break;
            default: return false;
        }
        s.RaiseState(); s.Invalidate();
        return true;
    }

    // ---------------------------------------------------------------- drawing
    public override void BuildOverlay(CadSession s, Overlay o)
    {
        var sk = Sk(s);
        if (_phase == Phase.Idle && Hot == null) return;
        var sh = Shape(s);
        uint col = sh.placeholder ? 0x907A9AC8 : 0xFFE8EEF8;
        foreach (var contour in sh.poly)
        {
            var pts = contour.Select(q => W(sk, _origin.u + q.u, _origin.v + q.v)).ToList();
            pts.Add(pts[0]);
            o.AddPolyline(pts, col, sh.placeholder ? 1.2f : 2f);
        }
        if (_phase == Phase.Idle) return;
        double pad = Math.Max(_height * 0.15, 0.3);
        double x0 = _origin.u + sh.x0 - pad, y0 = _origin.v + sh.y0 - pad, x1 = _origin.u + sh.x1 + pad, y1 = _origin.v + sh.y1 + pad;
        var frame = new List<Vec3> { W(sk, x0, y0), W(sk, x1, y0), W(sk, x1, y1), W(sk, x0, y1), W(sk, x0, y0) };
        o.AddPolyline(frame, Rubber, 1.4f);
        // the baseline origin and the resize handle
        o.AddDot(W(sk, _origin.u, _origin.v), Rubber, 6);
        o.AddDot(W(sk, x1, y1), 0xFFFFA630, 11);
        string size = $"{_height:0.##} mm";
        o.AddLabel(W(sk, (x0 + x1) / 2, y1 + pad * 2), Editing ? size + (sh.placeholder ? "  ·  start typing" : "  ·  Ctrl+Enter to place") : size, true, 4);
    }
}
