using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core.Sketching;
using Prism.Kernel;

namespace Prism.Core;

/// <summary>Freehand annotations kept out of sketch solving and profile geometry.</summary>
public sealed class GreasePencilTool : SketchTool
{
    readonly List<GreasePoint> _points = [];
    double _lastX, _lastY;
    int _width = 3;
    static readonly (string Id, string Name, uint Color)[] Palette =
    [
        ("red", "Red", 0xFFEF4444),
        ("blue", "Blue", 0xFF3B82F6),
        ("green", "Green", 0xFF22C55E),
        ("yellow", "Yellow", 0xFFFACC15),
        ("purple", "Purple", 0xFFA855F7)
    ];
    uint _color = 0xFFFACC15;
    bool _viewPlane;
    Plane3 _plane;
    public override string Name => "Grease pencil";
    public override string Prompt => "In orthographic view, drag to sketch a brainstorming mark. Escape cancels the current stroke; Ctrl+Z undoes a completed stroke.";
    public override string? StepperLabel => "Stroke width";
    public override int StepperValue => _width;
    public override void Step(CadSession s, int delta) { _width = Math.Clamp(_width + delta, 1, 12); s.RaiseState(); s.Invalidate(); }
    public override IReadOnlyList<ToolControl>? Panel =>
    [
        new(ToolControlKind.Info, "step", Prompt),
        .. Palette.Select(color => new ToolControl(ToolControlKind.Toggle, "color-" + color.Id, color.Name, On: _color == color.Color, Section: "Color")),
        new(ToolControlKind.Button, "orthographic", "Use orthographic view", Section: PanelSettingsTitle),
        new(ToolControlKind.Toggle, "view-plane", "Draw on view-facing plane", On: _viewPlane, Text: "Off: active sketch plane. On: plane facing the view through the active plane origin. Each stroke stays on its starting plane."),
        new(ToolControlKind.Info, "annotation", "Saved with this sketch. Marks do not snap, constrain, or form modeling profiles."),
        new(ToolControlKind.Button, "remove-last", "Remove last mark")
    ];

    public override void PanelSet(CadSession s, string id, object? value)
    {
        if (id.StartsWith("color-", StringComparison.Ordinal))
        {
            var color = Palette.FirstOrDefault(color => "color-" + color.Id == id);
            if (value is true && color.Id != null) { _points.Clear(); _color = color.Color; }
            s.RaiseState(); s.Invalidate(); return;
        }
        if (id == "orthographic") { _points.Clear(); s.SketchProjection(perspective: false); return; }
        if (id == "view-plane") { _points.Clear(); _viewPlane = value is true; s.RaiseState(); s.Invalidate(); return; }
        if (id != "remove-last" || Sk(s).Model.GreaseStrokes.Count == 0) return;
        _points.Clear();
        var sk = Sk(s);
        sk.Push();
        sk.Model.GreaseStrokes.RemoveAt(sk.Model.GreaseStrokes.Count - 1);
        s.NotifySketchChanged(); s.Invalidate();
    }

    public override bool PointerDown(CadSession s, PointerInfo p)
    {
        if (!p.Left || p.Alt) return false;
        if (!s.Camera.Orthographic) { s.Say("Switch to orthographic view to draw grease pencil marks."); return false; }
        _points.Clear();
        _plane = _viewPlane ? new Plane3(Sk(s).Plane.Origin, s.Camera.Right, s.Camera.Up) : Sk(s).Plane;
        Sample(s, p, true);
        return _points.Count > 0;
    }

    void Sample(CadSession s, PointerInfo p, bool force)
    {
        if (!force && Math.Pow(p.X - _lastX, 2) + Math.Pow(p.Y - _lastY, 2) < 4) return;
        if (!s.Camera.Orthographic) { _points.Clear(); return; }
        var (origin, direction) = s.Camera.Ray(p.X, p.Y);
        if (_plane.Intersect(origin, direction) is not { } world) return;
        var uv = Sk(s).Plane.Project(world);
        double depth = Vec3.Dot(world - Sk(s).Plane.Origin, Sk(s).Plane.Normal);
        if (!double.IsFinite(uv.u) || !double.IsFinite(uv.v) || !double.IsFinite(depth)) return;
        var point = new GreasePoint(uv.u, uv.v, depth);
        if (_points.Count == 0 || _points[^1] != point) _points.Add(point);
        _lastX = p.X; _lastY = p.Y;
        s.Invalidate();
    }

    public override void PointerMove(CadSession s, PointerInfo p)
    {
        if (_points.Count > 0 && p.Left) Sample(s, p, false);
    }

    public override void PointerUp(CadSession s, PointerInfo p)
    {
        if (_points.Count == 0) return;
        Sample(s, p, true);
        if (_points.Count > 1)
        {
            var sk = Sk(s);
            sk.Push();
            sk.Model.GreaseStrokes.Add(new GreaseStroke { Points = [.. _points], Width = _width, Color = _color });
            s.NotifySketchChanged();
        }
        _points.Clear(); s.Invalidate();
    }

    public override bool Key(CadSession s, string key)
    {
        if (key != "Escape" || _points.Count == 0) return false;
        _points.Clear(); s.Invalidate(); return true;
    }

    public override void Deactivate(CadSession s) { _points.Clear(); base.Deactivate(s); }
    public override void BuildOverlay(CadSession s, Overlay o)
    {
        if (_points.Count > 1)
            o.AddPolyline(_points.Select(p => W(Sk(s), p.U, p.V) + Sk(s).Plane.Normal * p.Depth).ToList(), _color, _width);
    }
}
