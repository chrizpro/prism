using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Kernel;

namespace Prism.Core;

/// <summary>Screen-space brush that erases whole annotation strokes in one undoable gesture.</summary>
public sealed class GreaseEraserTool : SketchTool
{
    const double Radius = 12;
    bool _down, _checkpoint, _hasCursor;
    double _x, _y;
    public override string Name => "Grease eraser";
    public override string Prompt => "In orthographic view, click or drag over grease pencil marks to erase whole strokes. Ctrl+Z restores an erase gesture.";

    public override bool PointerDown(CadSession s, PointerInfo p)
    {
        if (!p.Left || p.Alt) return false;
        if (!s.Camera.Orthographic) { s.Say("Switch to orthographic view to erase grease pencil marks."); return false; }
        _down = true; _checkpoint = false;
        _x = p.X; _y = p.Y; _hasCursor = true;
        Erase(s, p.X, p.Y);
        return true;
    }

    public override void PointerMove(CadSession s, PointerInfo p)
    {
        if (_down && p.Left && s.Camera.Orthographic) Erase(s, p.X, p.Y);
        else if (!s.Camera.Orthographic) _down = false;
        _x = p.X; _y = p.Y; _hasCursor = true;
        s.Invalidate();
    }

    void Erase(CadSession s, double x, double y)
    {
        var sk = Sk(s);
        var hits = new List<int>();
        for (int i = 0; i < sk.Model.GreaseStrokes.Count; i++)
        {
            var stroke = sk.Model.GreaseStrokes[i];
            for (int j = 1; j < stroke.Points.Count; j++)
            {
                Vec3 World(int n) { var p = stroke.Points[n]; return sk.Plane.PointAt(p.U, p.V) + sk.Plane.Normal * p.Depth; }
                if (s.Camera.Project(World(j - 1)) is not { } a || s.Camera.Project(World(j)) is not { } b) continue;
                double radius = Radius + stroke.Width / 2;
                if (SegmentsNear((_x, _y), (x, y), (a.x, a.y), (b.x, b.y), radius)) { hits.Add(i); break; }
            }
        }
        if (hits.Count == 0) return;
        if (!_checkpoint) { sk.Push(); _checkpoint = true; }
        foreach (int i in hits.AsEnumerable().Reverse()) sk.Model.GreaseStrokes.RemoveAt(i);
        s.NotifySketchChanged(); s.Invalidate();
    }

    static bool SegmentsNear((double x, double y) a, (double x, double y) b, (double x, double y) c, (double x, double y) d, double radius)
    {
        static double Cross((double x, double y) p, (double x, double y) q, (double x, double y) r) => (q.x - p.x) * (r.y - p.y) - (q.y - p.y) * (r.x - p.x);
        static double Distance2((double x, double y) p, (double x, double y) q, (double x, double y) r)
        {
            double dx = r.x - q.x, dy = r.y - q.y, length = dx * dx + dy * dy;
            double t = length == 0 ? 0 : Math.Clamp(((p.x - q.x) * dx + (p.y - q.y) * dy) / length, 0, 1);
            return Math.Pow(p.x - q.x - t * dx, 2) + Math.Pow(p.y - q.y - t * dy, 2);
        }
        if (Cross(a, b, c) * Cross(a, b, d) < 0 && Cross(c, d, a) * Cross(c, d, b) < 0) return true;
        double limit = radius * radius;
        return Distance2(a, c, d) <= limit || Distance2(b, c, d) <= limit || Distance2(c, a, b) <= limit || Distance2(d, a, b) <= limit;
    }

    public override void PointerUp(CadSession s, PointerInfo p)
    {
        if (_down && s.Camera.Orthographic) Erase(s, p.X, p.Y);
        _down = false; s.Invalidate();
    }
    public override void Deactivate(CadSession s) { _down = false; _checkpoint = false; _hasCursor = false; base.Deactivate(s); }
    public override void BuildOverlay(CadSession s, Overlay o)
    {
        if (!_hasCursor || !s.Camera.Orthographic) return;
        var plane = new Plane3(s.Camera.Target, s.Camera.Right, s.Camera.Up);
        var (origin, direction) = s.Camera.Ray(_x, _y);
        if (plane.Intersect(origin, direction) is not { } center) return;
        double radius = Radius * s.Camera.PixelSizeAt(center);
        o.AddPolyline(Enumerable.Range(0, 33).Select(i => center + plane.U * (radius * Math.Cos(i * Math.PI / 16)) + plane.V * (radius * Math.Sin(i * Math.PI / 16))).ToList(), 0xFFE6A84A, 1.5f);
    }
}
