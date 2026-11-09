using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core;
using Prism.Kernel;

namespace Prism.Commands;

/// <summary>
/// Draw a curve on a surface the way the spline tool draws one: click a few points, and a smooth spline passes through them.
/// The points sit on the surface of any solid, surface body, freeform form, NURBS surface or mesh, and can be dragged or deleted
/// until you finish. The curve is laid on the surface, runs on over shared edges onto neighbouring faces, and may continue onto
/// another body that touches the first. The result is an ordinary curve that stays where it was drawn.
/// </summary>
public sealed class DrawOnSurfaceCommand : Command
{
    readonly record struct Spot(SceneObject Obj, Vec3 Point, int Tri);
    readonly List<Spot> _picks = [];
    int _selected = -1;                          // the point that Delete removes and that is being dragged
    SurfaceGraph? _graph; List<SceneObject> _parts = [];
    IReadOnlyList<SceneObject>? _graphSource;
    (List<Vec3> Points, List<bool> Pinned)? _curve;
    bool _curveValid;
    List<Vec3>? _preview;
    long _lastPreview;

    public DrawOnSurfaceCommand()
    {
        Add(new ActionParam { Label = "Clear points", Run = () => { _picks.Clear(); _selected = -1; Reset(); OnChanged(); } });
        Tool = new DrawOnSurfaceTool(this);
    }

    public override string Title => "Draw on Surface";
    public override string Icon => "curve-through";
    public override string Hint => "Click points on a surface, surface body, freeform form, NURBS surface or mesh. A smooth curve passes through them, even over the edge onto the next face. Drag a point to move it; Delete removes the selected point. Enter or double-click to finish.";
    public override string OkText => "Make curve";

    public int PickCount => _picks.Count;
    void Reset() { _curve = null; _curveValid = false; _preview = null; }

    /// <summary>The surface under the pointer, from any visible object that has a surface.</summary>
    public (SceneObject obj, Vec3 point)? Hit(CadSession s, double x, double y)
    {
        var vis = s.Objects.Where(o => o.Visible && !s.HiddenIds.Contains(o.Id) && !o.IsAnnotation && o.Kind is not (ObjKind.Curve or ObjKind.Sketch)).ToList();
        return Picking.RayFace(s.Camera, vis, x, y) is { } h ? (h.obj, h.point) : null;
    }

    /// <summary>The index of the placed point within a few pixels of the pointer, or -1.</summary>
    public int PointAt(CadSession s, double x, double y)
    {
        int best = -1; double bestD = 11;
        for (int i = 0; i < _picks.Count; i++)
        {
            if (s.Camera.Project(_picks[i].Point) is not { } q) continue;
            double d = Math.Sqrt((q.x - x) * (q.x - x) + (q.y - y) * (q.y - y));
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    /// <summary>Weld every visible surface into one mesh, rebuilt when the scene changes. The fine tessellation is used so the curve sees a surface close to the true one.</summary>
    SurfaceGraph Graph(CadSession s)
    {
        if (_graph != null && ReferenceEquals(_graphSource, s.Objects)) return _graph;
        _parts = s.Objects.Where(o => o.Visible && !s.HiddenIds.Contains(o.Id) && !o.IsAnnotation && o.Kind is not (ObjKind.Curve or ObjKind.Sketch) && o.FineMesh.TriangleCount > 0).ToList();
        _graph = new SurfaceGraph(_parts.Select(o => (o.FineMesh.Positions, o.FineMesh.Indices)).ToList());
        _graphSource = s.Objects;
        return _graph;
    }

    Spot? Locate(CadSession s, SceneObject obj, Vec3 point, int nearTri = -1)
    {
        var graph = Graph(s);
        int part = _parts.FindIndex(o => o.Id == obj.Id);
        if (part < 0) return null;
        int tri = nearTri >= 0 ? graph.NearestTriangle(point, nearTri, out var on) : graph.FindTriangle(point, part, out on);
        return tri < 0 ? null : new Spot(obj, on, tri);
    }

    /// <summary>The curve through the given points, or null (with the reason said) when it cannot be laid on connected surface.</summary>
    (List<Vec3> Points, List<bool> Pinned)? Lay(CadSession s, IReadOnlyList<Spot> spots) =>
        spots.Count < 2 ? null : Graph(s).Trace(spots.Select(p => p.Point).ToList(), spots.Select(p => p.Tri).ToList());

    public void AddPoint(CadSession s, SceneObject obj, Vec3 point)
    {
        if (Locate(s, obj, point) is not { } spot) { s.Say("There is no surface there.", true); return; }
        var next = _picks.Append(spot).ToList();
        if (next.Count >= 2 && Lay(s, next) == null)
        {
            s.Say("That spot is not joined to the last point by surface: the curve would have to cross empty space. Pick a spot on a connected face or body.", true);
            return;
        }
        _picks.Add(spot); _selected = _picks.Count - 1; Reset();
        s.Say(_picks.Count == 1 ? "First point set. Click more points. Drag a point to move it; Enter or double-click finishes." : $"{_picks.Count} points. Drag a point to move it; Delete removes the selected one.");
        OnChanged(); s.Invalidate();
    }

    public void Select(CadSession s, int index) { _selected = index; s.Invalidate(); }

    /// <summary>Move a placed point to a new spot on a surface. Refused (and the point stays) if the curve could no longer be laid.</summary>
    public void MovePoint(CadSession s, int index, SceneObject obj, Vec3 point)
    {
        if (index < 0 || index >= _picks.Count) return;
        var old = _picks[index];
        var moved = Locate(s, obj, point, obj.Id == old.Obj.Id ? old.Tri : -1);
        if (moved == null) return;
        var next = _picks.ToList(); next[index] = moved.Value;
        if (next.Count >= 2 && Lay(s, next) == null) return;
        _picks[index] = moved.Value; Reset(); s.Invalidate();
    }

    public void RemoveSelected(CadSession s)
    {
        if (_picks.Count == 0) return;
        int at = _selected >= 0 && _selected < _picks.Count ? _selected : _picks.Count - 1;
        var next = _picks.ToList(); next.RemoveAt(at);
        if (next.Count >= 2 && Lay(s, next) == null) { s.Say("Without that point the curve would cross empty space.", true); return; }
        _picks.RemoveAt(at); _selected = _picks.Count - 1; Reset(); OnChanged(); s.Invalidate();
    }

    public override bool TryUndo()
    {
        if (_picks.Count == 0) return false;
        _selected = _picks.Count - 1;
        RemoveSelected(S);
        return true;
    }

    /// <summary>Show where the curve would go if the next click were at this point.</summary>
    public void Preview(CadSession s, SceneObject? obj, Vec3 point)
    {
        long now = Environment.TickCount64;                // finding the triangle walks the mesh, so do it a few times a second at most
        if (now - _lastPreview < 60) return;
        _lastPreview = now;
        _preview = null;
        if (obj != null && _picks.Count > 0 && Locate(s, obj, point) is { } spot)
            _preview = Lay(s, _picks.Append(spot).ToList())?.Points;
        s.Invalidate();
    }

    public void ClearPreview(CadSession s) { if (_preview != null) { _preview = null; s.Invalidate(); } }

    (List<Vec3> Points, List<bool> Pinned)? Curve()
    {
        if (!_curveValid && _graph != null && _picks.Count >= 2)
        {
            _curve = _graph.Trace(_picks.Select(p => p.Point).ToList(), _picks.Select(p => p.Tri).ToList());
            _curveValid = true;
        }
        return _curve;
    }

    public override void BuildOverlay(CadSession s, Overlay o)
    {
        if (Curve() is { } curve) o.AddPolyline(curve.Points, 0xFF35B6FF, 3);
        if (_preview != null) o.AddPolyline(_preview, 0xAA35B6FF, 2);
        for (int i = 0; i < _picks.Count; i++) o.AddDot(_picks[i].Point, i == _selected ? 0xFFFFB020 : 0xFFFFFFFF, i == _selected ? 12 : 9);
    }

    public override void OnEnd() { _graph = null; _graphSource = null; Reset(); }

    protected override CommandOutput? Build()
    {
        if (_picks.Count < 2 || Curve() is not { } curve) return null;
        var shape = Fit(curve.Points, curve.Pinned);

        // On an exact solid or surface body, pull the curve onto the true surface: the display mesh is only an approximation of it.
        string note = "";
        if (_picks.Select(p => p.Obj.Id).Distinct().Count() == 1 && _picks[0].Obj is { Form: null, Tri: null, HasBrep: true } body)
        {
            var snapped = OntoSurface(shape, body);
            note = snapped != null ? " (placed on the exact surface)" : " (follows the displayed surface; the exact-surface step was not possible)";
            if (snapped != null) shape = snapped;
        }
        Results.Clear();
        Results.Add(new ResultLine("Points", _picks.Count.ToString()));
        Results.Add(new ResultLine("Length", $"{SurfaceGraph.Length(curve.Points):0.###} mm" + note));
        return NurbsUtil.Curves([shape], "Curve on surface");
    }

    /// <summary>
    /// A smooth spline through the laid-down samples: the clicked points exactly, and between them every third sample, so the
    /// spline stays calm instead of copying every tiny wobble of the mesh. Two points make a straight line.
    /// </summary>
    static Shape Fit(List<Vec3> pts, List<bool> pinned)
    {
        var kept = new List<Vec3>(); var keptPinned = new List<bool>();
        for (int i = 0; i < pts.Count; i++)
            if (i == 0 || i == pts.Count - 1 || pinned[i] || i % 3 == 0) { kept.Add(pts[i]); keptPinned.Add(i == 0 || i == pts.Count - 1 || pinned[i]); }
        double gap = 1.5 * SurfaceGraph.Length(pts) / Math.Max(pts.Count - 1, 1);
        for (int j = kept.Count - 1; j >= 0; j--)
            if (!keptPinned[j] && (j > 0 && (kept[j] - kept[j - 1]).Length < gap || j + 1 < kept.Count && (kept[j] - kept[j + 1]).Length < gap)) { kept.RemoveAt(j); keptPinned.RemoveAt(j); }
        for (int j = kept.Count - 1; j > 0; j--) if ((kept[j] - kept[j - 1]).Length < 1e-9) { kept.RemoveAt(j); keptPinned.RemoveAt(j); }
        return kept.Count < 3 ? Occ.Line(kept[0], kept[^1]) : Occ.Spline(kept);
    }

    /// <summary>Pull a curve onto the nearest points of the body's exact surface; null when that fails or changes the curve too much.</summary>
    static Shape? OntoSurface(Shape curve, SceneObject body)
    {
        try
        {
            var projected = Occ.ProjectCurve(curve, body.Shape, Vec3.Zero, true);
            if (projected.Count(SubKind.Edge) == 0) return null;
            double before = SampledLength(curve), after = SampledLength(projected);
            return before > 1e-9 && Math.Abs(after - before) <= 0.03 * before ? projected : null;
        }
        catch (KernelException) { return null; }
    }

    static double SampledLength(Shape s)
    {
        double len = 0;
        foreach (var edge in Occ.CurveSamples(s))
            for (int i = 1; i < edge.Length; i++) len += (edge[i].p - edge[i - 1].p).Length;
        return len;
    }
}

/// <summary>Clicks place points on whatever surface is under the pointer; dragging a placed point moves it.</summary>
public sealed class DrawOnSurfaceTool(DrawOnSurfaceCommand cmd) : IViewportTool
{
    int _drag = -1;

    public string Prompt => "Draw on Surface: click points on a surface. Drag a point to move it, Delete removes the selected point, Enter or double-click finishes, Esc cancels.";

    public bool PointerDown(CadSession s, PointerInfo p)
    {
        if (!p.Left) return false;
        if (p.Clicks >= 2 && cmd.PickCount >= 2) { s.CommitCommand(); return true; }
        int at = cmd.PointAt(s, p.X, p.Y);
        if (at >= 0) { _drag = at; cmd.Select(s, at); return true; }
        if (cmd.Hit(s, p.X, p.Y) is { } hit) cmd.AddPoint(s, hit.obj, hit.point);
        return true;
    }

    public void PointerMove(CadSession s, PointerInfo p)
    {
        if (_drag >= 0)
        {
            if (cmd.Hit(s, p.X, p.Y) is { } over) cmd.MovePoint(s, _drag, over.obj, over.point);
            return;
        }
        if (cmd.PickCount == 0) return;
        if (cmd.PointAt(s, p.X, p.Y) >= 0) { cmd.ClearPreview(s); return; }
        if (cmd.Hit(s, p.X, p.Y) is { } hit) cmd.Preview(s, hit.obj, hit.point); else cmd.ClearPreview(s);
    }

    public void PointerUp(CadSession s, PointerInfo p) => _drag = -1;

    public bool Key(CadSession s, string key)
    {
        switch (key)
        {
            case "Enter": if (cmd.PickCount >= 2) s.CommitCommand(); else s.Say("Click at least two points on the surface first.", true); return true;
            case "Back": case "Delete": cmd.RemoveSelected(s); return true;
            default: return false;
        }
    }

    public void BuildOverlay(CadSession s, Overlay o) { }
    public void Deactivate(CadSession s) { _drag = -1; }
}
