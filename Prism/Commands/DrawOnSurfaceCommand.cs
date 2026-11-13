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
    bool _placing = true;
    bool _curveClosed;
    readonly ChoiceParam _result;
    readonly BoolParam _closed;

    public DrawOnSurfaceCommand()
    {
        _result = Choice("Result", 0, "Curve", "Split the face along the curve");
        _result.HelpText = "Split divides the face you drew on into two faces along the curve. An end that stops short of an edge is run on to it.";
        _closed = Check("Closed loop", false);
        Add(new ActionParam { Label = "Add more points", Run = () => StartPlacing(S) });
        Add(new ActionParam { Label = "Clear points", Run = () => { _picks.Clear(); _selected = -1; Reset(); OnChanged(); } });
        Tool = new DrawOnSurfaceTool(this);
    }

    public override string Title => "Draw on Surface";
    public override string Icon => "curve-through";
    public override string Hint => "Click points on a surface, surface body, freeform form, NURBS surface or mesh. A smooth curve passes through them, even over the edge onto the next face. Click the first point again (or tick Closed loop) to join the curve into a closed loop. Drag a point to move it; Delete removes the selected point. Esc stops adding points so you can adjust them, then Enter or Make curve finishes. Under Result choose Split to divide the face along the curve instead of keeping the curve.";
    public override string OkText => _result.Index == 1 ? "Split" : "Make curve";

    bool Closed => _closed.Value && _picks.Count >= 3;
    public bool IsClosed => Closed;

    /// <summary>Join the last point back to the first, so the curve is a closed loop (a circle, an outline). Clicking the first point does this.</summary>
    public void CloseLoop(CadSession s)
    {
        if (_picks.Count < 3) { s.Say("A closed loop needs at least three points.", true); return; }
        if (Lay(s, [.. _picks, _picks[0]]) == null) { s.Say("The last point is not joined to the first by surface: the loop would have to cross empty space.", true); return; }
        ConfigureInputs(() => _closed.Value = true);
        Reset(); _placing = false; _preview = null; OnChanged(); s.Invalidate();
        s.Say("Loop closed. Drag a point to adjust it, then press Enter or Make curve. Untick Closed loop to open it again.");
    }
    /// <summary>The clicked points in order, with the first one again at the end for a closed loop.</summary>
    List<Spot> Route() => Closed ? [.. _picks, _picks[0]] : _picks;

    public int PickCount => _picks.Count;

    /// <summary>True while clicks add points. After Esc the points can still be dragged and deleted, but clicks no longer add any.</summary>
    public bool Placing => _placing;

    public void StopPlacing(CadSession s)
    {
        _placing = false; _preview = null; s.Invalidate();
        s.Say("Adding points stopped. Drag a point to adjust it, then press Enter or Make curve. Esc again cancels.");
    }

    public void StartPlacing(CadSession s)
    {
        _placing = true; s.Invalidate();
        s.Say("Click to add points. Esc stops adding points.");
    }
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
        if (_curveClosed != Closed) { _curveClosed = Closed; _curveValid = false; }
        if (!_curveValid && _graph != null && _picks.Count >= 2)
        {
            var route = Route();
            _curve = _graph.Trace(route.Select(p => p.Point).ToList(), route.Select(p => p.Tri).ToList());
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
        bool closed = Closed;
        var shape = Fit(curve.Points, curve.Pinned, closed);

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
        return _result.Index == 1 ? SplitAlong(shape, curve.Points, closed) : NurbsUtil.Curves([shape], "Curve on surface");
    }

    /// <summary>
    /// Divide the faces the curve was drawn on. An open curve has to reach an edge at both ends to divide anything, and clicks
    /// rarely land exactly on an edge, so each end is run on: straight to the edge when one is close, otherwise along the
    /// curve's own direction. The extension is laid on the same faces only, so it stops at their border.
    /// </summary>
    CommandOutput SplitAlong(Shape curve, List<Vec3> laid, bool closed)
    {
        if (_picks.Select(p => p.Obj.Id).Distinct().Count() != 1 || _picks[0].Obj is not { Form: null, Tri: null, HasBrep: true, Kind: ObjKind.Surface or ObjKind.Solid } body)
            throw new FormatException("Splitting works on one surface or solid body. Draw the whole curve on the same body, or choose Curve under Result.");
        double diag = body.Shape.Props().Size.Length;
        int faceCount = body.Shape.Count(SubKind.Face);
        var faces = new List<Shape>();
        for (int i = 0; i < faceCount; i++)
        {
            var face = body.Shape.Sub(SubKind.Face, i);
            if (Occ.Distance(face, curve).Distance <= Math.Max(0.02, 0.003 * diag)) faces.Add(face);
        }
        if (faces.Count == 0) throw new FormatException("The curve is not on a face of that body.");
        var parts = new List<Shape> { curve };
        if (!closed)
        {
            var borders = Occ.Compound(faces.SelectMany(f => Enumerable.Range(0, f.Count(SubKind.Edge)).Select(i => f.Sub(SubKind.Edge, i))).ToList());
            double length = SurfaceGraph.Length(laid);
            foreach (var (end, next) in new[] { (laid[0], laid[1]), (laid[^1], laid[^2]) })
            {
                using var at = Occ.Vertex(end);
                var edge = Occ.Distance(at, borders); var along = (end - next).Normalized();
                bool near = edge.Distance > 1e-6 && edge.Distance <= 0.25 * length;
                var reach = near ? end + (edge.B - end).Normalized() * (edge.Distance + 0.005 * diag) : end + along * (edge.Distance <= 1e-6 ? 0.005 * diag : diag);
                parts.Add(Occ.Line(end, reach));
            }
        }
        Shape pieces;
        try
        {
            var onFaces = Occ.ProjectCurve(Occ.Compound(parts), Occ.Compound(faces), Vec3.Zero, true);
            pieces = Occ.Split(body.Shape, onFaces);
            if (pieces.Count(SubKind.Face) <= faceCount) pieces = SplitSurfaceCommand.SplitByEach(body.Shape, onFaces, SubKind.Edge);
        }
        catch (KernelException) { throw new FormatException(NoSplit(closed)); }
        int after = pieces.Count(SubKind.Face);
        if (after <= faceCount || body.Kind == ObjKind.Solid && pieces.Count(SubKind.Solid) != 1) throw new FormatException(NoSplit(closed));
        var result = body.Kind == ObjKind.Solid ? pieces.Sub(SubKind.Solid, 0)
            : Occ.Sew(Enumerable.Range(0, after).Select(i => pieces.Sub(SubKind.Face, i)).ToList(), 0.01, false);
        Results.Add(new ResultLine("Faces", $"{faceCount}  →  {after}"));
        var o = new CommandOutput { Label = "Split face", Icon = "splitbody" };
        o.Replace.Add((body, result));
        return o;
    }

    static string NoSplit(bool closed) => closed
        ? "The loop does not divide the face. Keep the whole loop on the body."
        : "The curve does not divide the face. Start and end it near an edge of the face, or turn on Closed loop to cut out a region.";

    /// <summary>
    /// A smooth spline through the laid-down samples: the clicked points exactly, and between them every third sample, so the
    /// spline stays calm instead of copying every tiny wobble of the mesh. Two points make a straight line.
    /// </summary>
    static Shape Fit(List<Vec3> pts, List<bool> pinned, bool closed = false)
    {
        var kept = new List<Vec3>(); var keptPinned = new List<bool>();
        for (int i = 0; i < pts.Count; i++)
            if (i == 0 || i == pts.Count - 1 || pinned[i] || i % 3 == 0) { kept.Add(pts[i]); keptPinned.Add(i == 0 || i == pts.Count - 1 || pinned[i]); }
        double gap = 1.5 * SurfaceGraph.Length(pts) / Math.Max(pts.Count - 1, 1);
        for (int j = kept.Count - 1; j >= 0; j--)
            if (!keptPinned[j] && (j > 0 && (kept[j] - kept[j - 1]).Length < gap || j + 1 < kept.Count && (kept[j] - kept[j + 1]).Length < gap)) { kept.RemoveAt(j); keptPinned.RemoveAt(j); }
        for (int j = kept.Count - 1; j > 0; j--) if ((kept[j] - kept[j - 1]).Length < 1e-9) { kept.RemoveAt(j); keptPinned.RemoveAt(j); }
        if (closed && kept.Count >= 4) return Occ.Spline(kept.Take(kept.Count - 1).ToList(), true);
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
    bool _closing, _moved;
    double _downX, _downY;

    public string Prompt => cmd.Placing ? "Draw on Surface: click points on a surface; click the first point again to close the loop. Drag a point to move it, Delete removes the selected point. Esc stops adding points; Enter finishes." : "Draw on Surface: drag a point to adjust it, Delete removes it. Enter or Make curve finishes, Esc cancels.";

    public bool PointerDown(CadSession s, PointerInfo p)
    {
        if (!p.Left) return false;
        if (p.Clicks >= 2 && cmd.PickCount >= 2) { s.CommitCommand(); return true; }
        int at = cmd.PointAt(s, p.X, p.Y);
        if (at >= 0)
        {
            _drag = at; cmd.Select(s, at); _downX = p.X; _downY = p.Y; _moved = false;
            // pressing the first point and letting go without dragging closes the loop
            _closing = at == 0 && cmd.Placing && cmd.PickCount >= 3 && !cmd.IsClosed;
            return true;
        }
        if (cmd.Placing && cmd.Hit(s, p.X, p.Y) is { } hit) cmd.AddPoint(s, hit.obj, hit.point);
        return true;
    }

    public void PointerMove(CadSession s, PointerInfo p)
    {
        if (_drag >= 0)
        {
            if (Math.Abs(p.X - _downX) + Math.Abs(p.Y - _downY) > 4) _moved = true;
            if (!_moved) return;
            if (cmd.Hit(s, p.X, p.Y) is { } over) cmd.MovePoint(s, _drag, over.obj, over.point);
            return;
        }
        if (cmd.PickCount == 0 || !cmd.Placing) return;
        if (cmd.PointAt(s, p.X, p.Y) >= 0) { cmd.ClearPreview(s); return; }
        if (cmd.Hit(s, p.X, p.Y) is { } hit) cmd.Preview(s, hit.obj, hit.point); else cmd.ClearPreview(s);
    }

    public void PointerUp(CadSession s, PointerInfo p)
    {
        bool close = _closing && !_moved && _drag == 0;
        _drag = -1; _closing = false;
        if (close) cmd.CloseLoop(s);
    }

    public bool Key(CadSession s, string key)
    {
        switch (key)
        {
            case "Enter": if (cmd.PickCount >= 2) s.CommitCommand(); else s.Say("Click at least two points on the surface first.", true); return true;
            case "Escape":
                // the first Esc leaves the point-placing tool but keeps the curve, so it can be adjusted and finished; with nothing to keep it cancels as usual
                if (cmd.Placing && cmd.PickCount >= 2) { cmd.StopPlacing(s); return true; }
                return false;
            case "Back": case "Delete": cmd.RemoveSelected(s); return true;
            default: return false;
        }
    }

    public void BuildOverlay(CadSession s, Overlay o) { }
    public void Deactivate(CadSession s) { _drag = -1; }
}
