using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Kernel;

namespace Prism.Core;

/// <summary>Transient screen-space-styled geometry drawn on top of the model (rubber bands, control nets, snap markers…).</summary>
public sealed class Overlay
{
    public readonly record struct Line(Vec3 A, Vec3 B, uint Color, float Width);
    public readonly record struct Dot(Vec3 P, uint Color, float Size);
    public readonly record struct Label(Vec3 P, string Text, bool Pill, int Style = 0);   // Style: 0 note, 1 dimension, 2 constraint glyph, 3 conflicting glyph, 4 live measurement, 5 value being typed, 6 selected glyph

    public readonly record struct Tri(Vec3 A, Vec3 B, Vec3 C, uint Color);
    public readonly record struct Rect(double X0, double Y0, double X1, double Y1);
    public List<Tri> Tris { get; } = [];
    public List<Rect> Rects { get; } = [];
    /// <summary>Screen-space (pixel) outlines, drawn dashed and lightly filled, such as a lasso selection loop.</summary>
    public List<IReadOnlyList<(double x, double y)>> ScreenPaths { get; } = [];
    public void AddScreenPath(IReadOnlyList<(double x, double y)> pts) { if (pts.Count > 1) ScreenPaths.Add(pts.ToArray()); }
    public void AddTri(Vec3 a, Vec3 b, Vec3 c, uint color) => Tris.Add(new(a, b, c, color));
    /// <summary>A screen-space rectangle (pixels) such as a selection box.</summary>
    public void AddRect(double x0, double y0, double x1, double y1) => Rects.Add(new(x0, y0, x1, y1));
    public List<Line> Lines { get; } = [];
    public List<Dot> Dots { get; } = [];
    public List<Label> Labels { get; } = [];

    public void AddLine(Vec3 a, Vec3 b, uint color, float width = 2) => Lines.Add(new(a, b, color, width));
    public void AddPolyline(IReadOnlyList<Vec3> pts, uint color, float width = 2, bool closed = false)
    {
        for (int i = 0; i + 1 < pts.Count; i++) AddLine(pts[i], pts[i + 1], color, width);
        if (closed && pts.Count > 2) AddLine(pts[^1], pts[0], color, width);
    }
    public void AddDot(Vec3 p, uint color, float size = 7) => Dots.Add(new(p, color, size));
    public void AddLabel(Vec3 p, string text, bool pill = true, int style = 0) => Labels.Add(new(p, text, pill, style));
    public void Clear() { Lines.Clear(); Dots.Clear(); Labels.Clear(); Tris.Clear(); Rects.Clear(); ScreenPaths.Clear(); }
}

public readonly record struct PointerInfo(double X, double Y, bool Left, bool Shift, bool Ctrl, bool Alt, int Clicks = 1);

/// <summary>An interactive behaviour that owns the left mouse button in the viewport (sketching, point editing, sculpting…).</summary>
public interface IViewportTool
{
    string Prompt { get; }
    /// <summary>True if the tool handled the press (otherwise the viewport falls back to selection).</summary>
    bool PointerDown(CadSession s, PointerInfo p);
    void PointerMove(CadSession s, PointerInfo p);
    void PointerUp(CadSession s, PointerInfo p);
    /// <summary>Return true if consumed.</summary>
    bool Key(CadSession s, string key);
    void BuildOverlay(CadSession s, Overlay o);
    /// <summary>Called when the tool is replaced or cancelled.</summary>
    void Deactivate(CadSession s);
}
