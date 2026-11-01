using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core.Sketching;
using Prism.Kernel;

namespace Prism.Core;

public enum SnapKind { Grid, Point, Entity, External, Projected, Gizmo, Midpoint, Track, Center }
/// <summary>Where a click would land. <c>Note</c> says what it landed on when that is geometry outside this sketch (for example "Sketch 1 · center").</summary>
public readonly record struct SnapInfo(double U, double V, SnapKind Kind, int Id, string? Note = null, int Id2 = -1, double Depth = 0);

public static class GridMath
{
    /// <summary>A 1-2-5 grid spacing that keeps minor lines roughly 22+ pixels apart.</summary>
    public static double Spacing(Camera cam)
    {
        double target = cam.PixelSize * 22;
        double mag = Math.Pow(10, Math.Floor(Math.Log10(target)));
        foreach (var m in new[] { 1.0, 2.0, 5.0, 10.0 })
            if (mag * m >= target) return mag * m;
        return mag * 10;
    }
}

/// <summary>
/// The sketch being edited: a parametric model (points, curves, constraints, dimensions), the active tool, the current selection
/// and the picking / snapping / drawing helpers the tools share.
/// </summary>
public sealed partial class SketchContext
{
    /// <summary>Stable visual reference captured when entering this editing session; drawing planes may change independently.</summary>
    public Plane3 GridPlane { get; }
    public Plane3 Plane => _sections[_activeSection].Plane;
    public SketchModel Model { get => _sections[_activeSection].Model; private set => _sections[_activeSection].Model = value; }
    public string? SuggestedName { get; set; }
    public Guid? EditingId { get; }
    public SketchTool? Tool { get; private set; }
    public SolveResult Result { get; private set; }
    /// <summary>New curves are created as construction geometry (reference only; not part of the profile).</summary>
    public bool Construction { get; set; }
    public HashSet<int> SelEntities { get; } = [];
    public HashSet<int> SelPoints { get; } = [];
    public int HoverEntity { get; set; } = -1;
    public int HoverPoint { get; set; } = -1;
    public SnapInfo? Cursor { get; set; }
    readonly Stack<SpaceSnapshot> _undo = new();
    readonly List<(SkConstraint c, Vec3 pos)> _dimLabels = [];
    readonly List<(SkConstraint c, Vec3 anchor, int n)> _glyphs = [];
    /// <summary>Constraints blamed for the last refused change; their symbols show red until the next edit.</summary>
    public HashSet<int> Blamed { get; } = [];
    /// <summary>The constraint whose symbol was clicked (Delete removes it); -1 for none.</summary>
    public int SelConstraint { get; set; } = -1;
    /// <summary>Runs on the model just before the next edit (lets a tool move geometry into place before the solver takes over).</summary>
    public Action? PreChange { get; set; }

    public SketchContext(Plane3 plane, SketchModel? model = null, Guid? editing = null)
    {
        GridPlane = plane;
        InitializeSpace(plane, model); EditingId = editing;
        Result = SketchSolver.Solve(Model);
    }

    public bool HasContent => _sections.Any(section => section.Model.Entities.Count > 0) || Datums.Count > 0;

    public bool EntityVisible(SkEntity e) => !e.Construction || Model.ShowConstruction;
    public bool PointVisible(int id) => id == 0 || Model.ShowConstruction || Model.Entities.Any(e => !e.Construction && Model.PointsOf(e).Contains(id)) && !TextFitPoint(id);

    /// <summary>The fit points inside a letter's curve are not shown or snapped to (only the corners and curve ends are), so text stays clean.</summary>
    bool TextFitPoint(int id) => Model.Entities.Any(e => e.Type == EntType.Spline && e.Group != null && e.Group.StartsWith("Text ", StringComparison.Ordinal) && e.Pts.IndexOf(id) is var i && i > 0 && (e.Closed || i < e.Pts.Count - 1));

    bool ConstraintVisible(SkConstraint c)
    {
        var points = SketchModel.RefIsPoint(c.Type, c.Refs.Length);
        return c.Refs.Select((id, i) => points[i] ? PointVisible(id) : Model.EntityOrNull(id) is { } e && EntityVisible(e)).All(v => v);
    }

    // ---------------------------------------------------------------- tools, undo, solving
    public void SetTool(CadSession s, SketchTool? t)
    {
        t ??= new SelectTool();
        GizmoEditing = false;
        GeometryEditing = t is SketchGeometryEditTool or SplineEditTool || Is3DEnabled && t is SelectTool;
        if(!GeometryEditing)_planarGizmoEnabled=false;
        if(!HasGeometrySelection)ClearGizmoTarget();
        DrawingAxis = 0; _lastPlacement = null; ClearTracking();
        _axisPreview=null;_inferredVisualAxis=-1;
        Tool?.Deactivate(s);
        Hint = null;
        Tool = t;
        s.Say(t?.Prompt ?? "Pick a sketch tool.");
        t?.Activate(s);
        if (t is SketchGeometryEditTool { SplinePoints:false } && Is3DEnabled) GizmoEditing = true;
        s.RaiseState();
    }

    public void Push() => PushSnapshot(Model.Clone());

    public bool TryUndo(CadSession s)
    {
        if (_undo.Count == 0) return false;
        Tool?.Deactivate(s);
        var snapshot = _undo.Pop(); _sections = snapshot.Sections; _activeSection = snapshot.Active; GizmoSize = snapshot.Size; GizmoStretch = snapshot.Stretch;
        _gizmoFrame = snapshot.Gizmo; GizmoPlaneIndex = snapshot.GizmoPlane; ActiveDatumId = snapshot.Datum; GeometryScale=snapshot.TargetScale;
        ClearPlaneSelection();
        Solve(s); s.Say("Undid last sketch step");
        return true;
    }

    public SolveResult Solve(CadSession s, IEnumerable<(int pid, double tx, double ty)>? drags = null, (int eid, double r)? radius = null)
    {
        Result = SketchSolver.Solve(Model, drags, radius);
        s.NotifySketchChanged();
        return Result;
    }

    /// <summary>
    /// Make a change to the model in one undoable step. The sketch is re-solved; if the result conflicts with existing
    /// constraints the change is rolled back and the user is told.
    /// </summary>
    public bool Edit(CadSession s, string what, Action change)
    {
        var before = Model.Clone();
        Blamed.Clear();
        PreChange?.Invoke();
        change();
        var attempt = Model.Clone();
        var r = Solve(s);
        if (r.Converged && !Collapsed(before, Model)) { PushSnapshot(before); return true; }
        // the solver took the shortest way out and flattened a line (Vertical on a horizontal line): when nothing the user set forbids
        // turning the line, start again with it turned a quarter turn so it swings round instead
        var squashed = r.Converged ? CollapsedEntities(before, Model) : [];
        if (squashed.Count > 0 && squashed.All(id => !ShapeConstraints(before, before.E(id)).Any()))
        {
            var retry = attempt.Clone();
            foreach (var id in squashed)
            {
                var e = retry.E(id); if (e.Type != EntType.Line) continue;
                int moving = Math.Max(e.A, e.B), anchor = e.A == moving ? e.B : e.A;
                var pm = retry.P(moving); var pa = retry.P(anchor);
                double dx = pm.X - pa.X, dy = pm.Y - pa.Y; pm.X = pa.X - dy; pm.Y = pa.Y + dx;
            }
            Model = retry;
            r = Solve(s);
            if (r.Converged && !Collapsed(before, Model)) { PushSnapshot(before); return true; }
        }
        Refuse(s, before, attempt, what);
        return false;
    }

    /// <summary>True when the change squashed a curve that had a size down to nothing (for example Horizontal + Vertical on one line): the solver "solves" that, but nobody wants it.</summary>
    public static bool Collapsed(SketchModel before, SketchModel after) => CollapsedEntities(before, after).Count > 0;

    public static List<int> CollapsedEntities(SketchModel before, SketchModel after)
    {
        var res = new List<int>();
        foreach (var e in after.Entities)
        {
            if (before.EntityOrNull(e.Id) is not { } b) continue;
            switch (e.Type)
            {
                case EntType.Line:
                {
                    double L(SketchModel m, SkEntity x) { var p = m.P(x.A); var q = m.P(x.B); return Math.Sqrt((p.X - q.X) * (p.X - q.X) + (p.Y - q.Y) * (p.Y - q.Y)); }
                    if (L(before, b) > 1e-4 && L(after, e) < 1e-6) res.Add(e.Id);
                    break;
                }
                case EntType.Circle or EntType.Ellipse:
                    if (b.R > 1e-4 && e.R < 1e-6) res.Add(e.Id);
                    break;
            }
        }
        return res;
    }

    /// <summary>The older constraints that decide a curve's direction or size (what a collapse is blamed on).</summary>
    static IEnumerable<SkConstraint> ShapeConstraints(SketchModel m, SkEntity e)
    {
        var pts = m.PointsOf(e).ToList();
        foreach (var c in m.Constraints.Where(c => !c.Hidden))
        {
            var ip = SketchModel.RefIsPoint(c.Type, c.Refs.Length);
            bool onEntity = Enumerable.Range(0, c.Refs.Length).Any(i => !ip[i] && c.Refs[i] == e.Id);
            bool pointPair = c.Type is CType.Horizontal or CType.Vertical && c.Refs.Length == 2 && c.Refs.All(pts.Contains);
            bool shape = c.Type is CType.Horizontal or CType.Vertical or CType.Parallel or CType.Perpendicular or CType.Collinear or CType.Angle or CType.Radius or CType.Diameter or CType.Equal or CType.LineLength or CType.Tangent;
            if ((onEntity && shape) || pointPair) yield return c;
        }
    }

    /// <summary>Roll back a change the solver could not satisfy, and say which existing constraints are in the way (first constraint wins).</summary>
    void Refuse(CadSession s, SketchModel before, SketchModel failed, string what)
    {
        var culprits = new List<SkConstraint>();
        var old = before.Constraints.Where(c => !c.Hidden).ToList();
        var squashed = CollapsedEntities(before, Model);
        if (squashed.Count > 0)
        {
            // the solver "solved" it by squashing a curve flat: blame the older constraints that fix that curve's direction or size
            foreach (var id in squashed) culprits.AddRange(ShapeConstraints(before, before.E(id)));
            culprits = culprits.DistinctBy(c => c.Id).ToList();
        }
        else if (old.Count <= 80)
            foreach (var c in old)
            {
                var test = failed.Clone(); var tc = test.Constraints.FirstOrDefault(x => x.Id == c.Id); if (tc == null) continue;
                test.Constraints.Remove(tc);
                if (SketchSolver.Solve(test).Converged && !Collapsed(before, test)) culprits.Add(c);
            }
        Model = before; Solve(s);
        Blamed.Clear(); foreach (var c in culprits) Blamed.Add(c.Id);
        string names = culprits.Count == 0 ? "" : string.Join(", ", culprits.Select(c => SketchModel.Describe(c.Type).ToLowerInvariant()).Distinct());
        s.Say(culprits.Count == 0
            ? $"{what} conflicts with the existing constraints, so it was not applied."
            : $"{what} conflicts with the {names} constraint. The first constraint wins: click the red symbol and press Delete to remove it, then try again.", true);
        s.NotifySketchChanged();
    }

    /// <summary>The constraint symbol under the pointer, if any.</summary>
    Vec3 GlyphPos(CadSession s, Vec3 anchor, int n)
    {
        double ps = s.Camera.PixelSizeAt(anchor);
        return anchor + Plane.U * (ps * (14 + n * 17)) + Plane.V * (ps * 14);
    }

    public IEnumerable<string> GlyphDebug(CadSession s) => _glyphs.Select(g => { var q = s.Camera.Project(GlyphPos(s, g.anchor, g.n)); return $"{g.c.Id}@{q?.x:0.#},{q?.y:0.#}"; });

    public SkConstraint? PickGlyph(CadSession s, double px, double py)
    {
        foreach (var (c, anchor, n) in _glyphs)
        {
            var q = s.Camera.Project(GlyphPos(s, anchor, n)); if (q == null) continue;
            if (Math.Abs(q.Value.x - px) < 11 && Math.Abs(q.Value.y - py) < 11) return c;
        }
        return null;
    }

    /// <summary>Select a constraint by its symbol: it lights up and Delete removes it.</summary>
    public void SelectConstraint(CadSession s, SkConstraint c)
    {
        SelEntities.Clear(); SelPoints.Clear(); SelConstraint = c.Id; Blamed.Clear();
        s.Say($"{SketchModel.Describe(c.Type)} constraint selected. Press Delete to remove it.");
        s.NotifySketchChanged(); s.Invalidate();
    }

    /// <summary>Remove one constraint.</summary>
    public void RemoveConstraint(CadSession s, SkConstraint c)
    {
        var before = Model.Clone();
        Blamed.Clear();
        var cc = Model.Constraints.FirstOrDefault(x => x.Id == c.Id); if (cc == null) return;
        Model.Remove(cc);
        Solve(s); PushSnapshot(before);
        s.Say($"Removed the {SketchModel.Describe(c.Type).ToLowerInvariant()} constraint.");
    }

    /// <summary>Delete the selected curves (and curves that use selected points).</summary>
    public bool DeleteSelected(CadSession s)
    {
        if (SelConstraint >= 0 && SelEntities.Count + SelPoints.Count == 0)
        {
            var c = Model.Constraints.FirstOrDefault(x => x.Id == SelConstraint && !x.Hidden); SelConstraint = -1;
            if (c != null) { RemoveConstraint(s, c); return true; }
        }
        if (SelEntities.Count + SelPoints.Count == 0) { s.Say("Select something to delete first.", true); return false; }
        if (SelEntities.Count == 0 && SelPoints.All(pid => Model.Entities.Where(e => e.Type == EntType.Spline && e.Pts.Contains(pid)).Any() && !Model.Entities.Any(e => e.Type != EntType.Spline && Model.PointsOf(e).Contains(pid))))
        {
            // points of a spline: take them out of the curve (the whole spline goes only when too few remain)
            bool removedAny = false;
            var ok = Edit(s, "Deleting that", () =>
            {
                foreach (var pid in SelPoints.ToList())
                    foreach (var e in Model.Entities.Where(en => en.Type == EntType.Spline && en.Pts.Contains(pid)).ToList())
                    {
                        if (SplineOps.Remove(Model, e, pid)) removedAny = true;
                        else { Model.RemoveEntity(e.Id); removedAny = true; }
                    }
                SelPoints.Clear();
            });
            return ok && removedAny;
        }
        if(Tool is ClickTool drawing)drawing.ForgetDeletedInput(s);
        return Edit(s, "Deleting that", () =>
        {
            foreach (var pid in SelPoints.ToList())
                foreach (var e in Model.Entities.Where(en => Model.PointsOf(en).Contains(pid)).ToList()) SelEntities.Add(e.Id);
            foreach (var id in SelEntities.ToList()) Model.RemoveEntity(id);
            // Remove an orphaned center-rectangle control diagonal after its boundary is deleted.
            foreach(var e in Model.Entities.Where(e=>e.Construction&&e.Group?.StartsWith("Rectangle ",StringComparison.Ordinal)==true).ToList())
                if(!Model.Entities.Any(other=>other.Group==e.Group&&other.Id!=e.Id)&&Model.Constraints.Any(c=>c.Type==CType.Midpoint&&c.Refs.Length==2&&c.Refs[1]==e.Id))Model.RemoveEntity(e.Id);
            foreach(int point in SelPoints.ToList())Model.DropPointIfUnshared(point);
            SelEntities.Clear(); SelPoints.Clear();
            Model.PruneUnusedPoints(Model.Points.Select(p=>p.Id).ToList());
        });
    }

    /// <summary>Flip whether new curves are drawn as construction (reference) geometry. Shown as a pressed button and a note on the status line.</summary>
    public void ToggleConstructionMode(CadSession s)
    {
        Construction = !Construction;
        s.Say(Construction ? "Construction mode ON: new curves are reference geometry (they do not become part of the profile). Press X or click Construction to go back to normal drawing." : "Construction mode off: new curves are normal geometry.");
        s.NotifySketchChanged(); s.RaiseState(); s.Invalidate();
    }

    /// <summary>Turn the selected curves into (or back from) construction geometry.</summary>
    public void ToggleConstruction(CadSession s)
    {
        if (SelEntities.Count == 0) { s.Say("Select curves first, then Make Construction. To draw new curves as construction, use the Construction button (or press X).", true); return; }
        Edit(s, "That change", () =>
        {
            bool to = !SelEntities.Select(id => Model.E(id)).All(e => e.Construction);
            foreach (var id in SelEntities) Model.E(id).Construction = to;
        });
    }

    public void PushSnapshot(SketchModel before) { _undo.Push(CaptureSpace(before)); if (_undo.Count > 80) { var keep = _undo.Take(60).Reverse().ToArray(); _undo.Clear(); foreach (var k in keep) _undo.Push(k); } }
    public void Restore(CadSession s, SketchModel m) { Model = m; Solve(s); }

    /// <summary>Apply a model change; if the result conflicts, roll it back and say why.</summary>
    public bool Commit(CadSession s, SketchModel before, string what)
    {
        var attempt = Model.Clone();
        var r = Solve(s);
        if (r.Converged && !Collapsed(before, Model)) { Blamed.Clear(); return true; }
        Refuse(s, before, attempt, what);
        return false;
    }

    // ---------------------------------------------------------------- snapping & picking
    public (double u, double v)? PlanePoint(CadSession s, double px, double py)
    {
        var (o, d) = s.Camera.Ray(px, py);
        var hit = Plane.Intersect(o, d);
        return hit == null ? null : Plane.Project(hit.Value);
    }

    double PixelSize(CadSession s, double u, double v) => s.Camera.PixelSizeAt(Plane.PointAt(u, v));

    public (double u, double v, double dist) ClosestOn(SkEntity e, double u, double v)
    {
        switch (e.Type)
        {
            case EntType.Line:
            {
                var a = Model.P(e.A); var b = Model.P(e.B);
                double dx = b.X - a.X, dy = b.Y - a.Y, l2 = dx * dx + dy * dy;
                double t = l2 < 1e-12 ? 0 : Math.Clamp(((u - a.X) * dx + (v - a.Y) * dy) / l2, 0, 1);
                double qx = a.X + dx * t, qy = a.Y + dy * t;
                return (qx, qy, Math.Sqrt((qx - u) * (qx - u) + (qy - v) * (qy - v)));
            }
            case EntType.Circle:
            {
                var c = Model.P(e.A);
                double dx = u - c.X, dy = v - c.Y, d = Math.Max(Math.Sqrt(dx * dx + dy * dy), 1e-12);
                return (c.X + dx / d * e.R, c.Y + dy / d * e.R, Math.Abs(d - e.R));
            }
            case EntType.Arc:
            {
                var (cx, cy, r, a0, sw) = Model.ArcGeometry(e);
                double ang = Math.Atan2(v - cy, u - cx), rel = ang - a0; while (rel < 0) rel += 2 * Math.PI;
                if (rel <= sw)
                {
                    double d = Math.Sqrt((u - cx) * (u - cx) + (v - cy) * (v - cy));
                    return (cx + Math.Cos(ang) * r, cy + Math.Sin(ang) * r, Math.Abs(d - r));
                }
                var s0 = Model.P(e.B); var s1 = Model.P(e.C);
                double d0 = Math.Sqrt((u - s0.X) * (u - s0.X) + (v - s0.Y) * (v - s0.Y)), d1 = Math.Sqrt((u - s1.X) * (u - s1.X) + (v - s1.Y) * (v - s1.Y));
                return d0 < d1 ? (s0.X, s0.Y, d0) : (s1.X, s1.Y, d1);
            }
            case EntType.Ellipse:
            {
                var smp = Enumerable.Range(0, 129).Select(i => Model.EllipsePoint(e, i * 2 * Math.PI / 128)).ToList();
                double best = double.MaxValue; (double, double) bp = (u, v);
                for (int i = 0; i + 1 < smp.Count; i++)
                {
                    var (ax, ay) = smp[i]; var (bx, by) = smp[i + 1];
                    double dx = bx - ax, dy = by - ay, l2 = dx * dx + dy * dy;
                    double t = l2 < 1e-12 ? 0 : Math.Clamp(((u - ax) * dx + (v - ay) * dy) / l2, 0, 1);
                    double qx = ax + dx * t, qy = ay + dy * t, d = Math.Sqrt((qx - u) * (qx - u) + (qy - v) * (qy - v));
                    if (d < best) { best = d; bp = (qx, qy); }
                }
                return (bp.Item1, bp.Item2, best);
            }
            case EntType.Spline:
            {
                var smp = Model.SplineSamples(e);
                double best = double.MaxValue; (double, double) bp = (u, v);
                for (int i = 0; i + 1 < smp.Count; i++)
                {
                    var (ax, ay) = smp[i]; var (bx, by) = smp[i + 1];
                    double dx = bx - ax, dy = by - ay, l2 = dx * dx + dy * dy;
                    double t = l2 < 1e-12 ? 0 : Math.Clamp(((u - ax) * dx + (v - ay) * dy) / l2, 0, 1);
                    double qx = ax + dx * t, qy = ay + dy * t, d = Math.Sqrt((qx - u) * (qx - u) + (qy - v) * (qy - v));
                    if (d < best) { best = d; bp = (qx, qy); }
                }
                return (bp.Item1, bp.Item2, best);
            }
            default: return (u, v, double.MaxValue);
        }
    }

    public SnapInfo? Snap(CadSession s, double px, double py, bool allow = true, ICollection<int>? excludePoints = null, ICollection<int>? excludeEntities = null)
    {
        _hoverSeen = false;
        var result = SnapCore(s, px, py, allow, excludePoints, excludeEntities);
        if (!_hoverSeen) _hoverKey = null;
        return result;
    }

    SnapInfo? SnapCore(CadSession s, double px, double py, bool allow, ICollection<int>? excludePoints, ICollection<int>? excludeEntities)
    {
        if (allow && Is3DEnabled && !IsDrawingFlat && _axisPreview is { } inferred)
        {
            if (Math.Abs(Vec3.Dot(inferred-Plane.Origin,Plane.Normal)) > 1e-6) return null;
            var snapped=Plane.Project(inferred); return new SnapInfo(snapped.u,snapped.v,SnapKind.Grid,-1);
        }
        if (PlanePoint(s, px, py) is not { } uv) return null;
        if (DrawingAxis != 0)
        {
            var anchor = Plane.Project(_lastPlacement ?? Plane.Origin);
            return new SnapInfo(DrawingAxis == 1 ? uv.u : anchor.u, DrawingAxis == 2 ? uv.v : anchor.v, SnapKind.Grid, -1);
        }
        double ps = PixelSize(s, uv.u, uv.v);
        if (allow)
        {
            int bestP = -1; double bd = 10 * ps;
            foreach (var p in Model.Points)
            {
                if (!PointVisible(p.Id)) continue;
                if (excludePoints?.Contains(p.Id) == true) continue;
                double d = Math.Sqrt((p.X - uv.u) * (p.X - uv.u) + (p.Y - uv.v) * (p.Y - uv.v));
                if (d < bd) { bd = d; bestP = p.Id; }
            }
            if (bestP >= 0) { var p = Model.P(bestP); Hover("p" + bestP, p.X, p.Y, bestP); return new SnapInfo(p.X, p.Y, SnapKind.Point, bestP); }
            if (Is3DEnabled && Math.Abs(Vec3.Dot(GizmoFrame.Origin - Plane.Origin, Plane.Normal)) < 1e-6 && s.Camera.Project(GizmoFrame.Origin) is { } gizmo && Math.Sqrt(Math.Pow(gizmo.x - px, 2) + Math.Pow(gizmo.y - py, 2)) < 9)
            { var local = Plane.Project(GizmoFrame.Origin); return new SnapInfo(local.u, local.v, SnapKind.Gizmo, -1); }
            // a visible sketch that is not being edited counts as part of this one: its endpoints, centers and midpoints snap
            if (SnapReferenceSketches(s, px, py, true) is { } referencePoint) return referencePoint;
            int bestM = -1; double bm = 9 * ps; (double, double) mid = (0, 0);
            foreach (var e in Model.Entities)
            {
                if (e.Type != EntType.Line || !EntityVisible(e) || excludeEntities?.Contains(e.Id) == true) continue;
                var a = Model.P(e.A); var b = Model.P(e.B);
                double mx = (a.X + b.X) / 2, my = (a.Y + b.Y) / 2, d = Math.Sqrt((mx - uv.u) * (mx - uv.u) + (my - uv.v) * (my - uv.v));
                if (d < bm) { bm = d; bestM = e.Id; mid = (mx, my); }
            }
            if (RectangleCenter(uv.u, uv.v, 9 * ps) is { } center) { Hover("c" + center.u.ToString("0.###") + "/" + center.v.ToString("0.###"), center.u, center.v, -1); return new SnapInfo(center.u, center.v, SnapKind.Center, -1, "Center"); }
            if (bestM >= 0) { Hover("m" + bestM, mid.Item1, mid.Item2, -1); return new SnapInfo(mid.Item1, mid.Item2, SnapKind.Midpoint, bestM); }
            if (TrackAcquired(uv.u, uv.v, ps, excludePoints) is { } tracked) return tracked;
            int bestE = -1; double be = 8 * ps; (double, double) bq = (0, 0);
            foreach (var e in Model.Entities)
            {
                if (!EntityVisible(e)) continue;
                if (e.Type == EntType.Point || excludeEntities?.Contains(e.Id) == true) continue;
                var (qu, qv, d) = ClosestOn(e, uv.u, uv.v);
                if (d < be) { be = d; bestE = e.Id; bq = (qu, qv); }
            }
            if (bestE >= 0) return new SnapInfo(bq.Item1, bq.Item2, SnapKind.Entity, bestE);
            if (TrackFrom is { } from)
            {
                bool nearH = Math.Abs(uv.v - from.v) < 7 * ps, nearV = Math.Abs(uv.u - from.u) < 7 * ps;
                if (nearH ^ nearV)
                {
                    TrackSegs.Add((from.u, from.v, nearH ? uv.u : from.u, nearH ? from.v : uv.v));
                    return new SnapInfo(nearH ? uv.u : from.u, nearH ? from.v : uv.v, SnapKind.Grid, -1, nearH ? "Horizontal" : "Vertical");
                }
            }
            if (SnapReferenceSketches(s, px, py, false) is { } referenceCurve) return referenceCurve;
            if (SnapOtherCurves && SnapExternal(s, px, py) is { } external) return external;
        }
        if (!s.SketchGridSnap) return new SnapInfo(uv.u, uv.v, SnapKind.Grid, -1);   // free placement
        double g = GridMath.Spacing(s.Camera);
        return new SnapInfo(Math.Round(uv.u / g) * g, Math.Round(uv.v / g) * g, SnapKind.Grid, -1);
    }

    /// <summary>Turn a snap into a model point, adding the constraint the snap implies (coincident / point on curve).</summary>
    // ---------------------------------------------------------------- alignment tracking: rest the pointer on a point to acquire it, then line up with it
    /// <summary>A spot the pointer rested on (a point, a line's midpoint, a rectangle's center): the sketch offers horizontal and vertical alignment with it.</summary>
    public sealed record Anchor(string Key, double U, double V, int PointId);
    public List<Anchor> Acquired { get; } = [];
    /// <summary>Where the active tool's last click was; the pointer also lines up with it.</summary>
    public (double u, double v)? TrackFrom { get; set; }
    /// <summary>Alignment lines the last snap used (drawn as a guide).</summary>
    public List<(double au, double av, double bu, double bv)> TrackSegs { get; } = [];
    string? _hoverKey; long _hoverSince; bool _hoverSeen;

    void Hover(string key, double u, double v, int pointId)
    {
        _hoverSeen = true;
        long now = Environment.TickCount64;
        if (_hoverKey != key) { _hoverKey = key; _hoverSince = now; return; }
        if (now - _hoverSince < 220 || Acquired.Any(a => a.Key == key)) return;
        Acquired.Add(new Anchor(key, u, v, pointId));
        if (Acquired.Count > 4) Acquired.RemoveAt(0);
    }
    public void ClearTracking() { Acquired.Clear(); TrackSegs.Clear(); _hoverKey = null; TrackFrom = null; }

    (double u, double v) AnchorAt(Anchor a) => a.PointId >= 0 && Model.PointOrNull(a.PointId) is { } p ? (p.X, p.Y) : (a.U, a.V);

    /// <summary>The center of a rectangle (the middle of its box) when the pointer is near it.</summary>
    (double u, double v)? RectangleCenter(double u, double v, double tol)
    {
        foreach (var g in Model.Entities.Where(e => e.Type == EntType.Line && e.Group != null && e.Group.StartsWith("Rectangle ", StringComparison.Ordinal) && EntityVisible(e)).GroupBy(e => e.Group))
        {
            var pts = g.SelectMany(e => new[] { Model.P(e.A), Model.P(e.B) }).ToList();
            if (g.Count() < 4) continue;
            double cu = (pts.Min(q => q.X) + pts.Max(q => q.X)) / 2, cv = (pts.Min(q => q.Y) + pts.Max(q => q.Y)) / 2;
            if (Math.Abs(cu - u) < tol && Math.Abs(cv - v) < tol) return (cu, cv);
        }
        return null;
    }

    SnapInfo? TrackAcquired(double u, double v, double ps, ICollection<int>? exclude)
    {
        TrackSegs.Clear();
        if (Acquired.Count == 0) return null;
        double tol = 7 * ps, bh = tol, bv = tol; Anchor? h = null, vv = null;
        foreach (var a in Acquired)
        {
            if (a.PointId >= 0 && exclude?.Contains(a.PointId) == true) continue;
            var (au, av) = AnchorAt(a);
            if (Math.Abs(v - av) < bh) { bh = Math.Abs(v - av); h = a; }
            if (Math.Abs(u - au) < bv) { bv = Math.Abs(u - au); vv = a; }
        }
        if (h == null && vv == null) return null;
        double su = vv != null ? AnchorAt(vv).u : u, sv = h != null ? AnchorAt(h).v : v;
        if (h != null) { var a = AnchorAt(h); TrackSegs.Add((a.u, a.v, su, sv)); }
        if (vv != null) { var a = AnchorAt(vv); TrackSegs.Add((a.u, a.v, su, sv)); }
        string note = h != null && vv != null ? "Horizontal and vertical" : h != null ? "Horizontal" : "Vertical";
        return new SnapInfo(su, sv, SnapKind.Track, (h ?? vv)!.PointId, note, h != null && vv != null ? vv.PointId : -1);
    }

    public int PointFor(SnapInfo sn)
    {
        if (sn.Kind == SnapKind.Point) return sn.Id;
        int id = Model.AddPoint(sn.U, sn.V);
        if (Is3DEnabled) Model.P(id).Z = sn.Depth;
        if (sn.Kind == SnapKind.Track)
        {
            // only aligned-with-a-point snaps can become constraints; a midpoint or rectangle center has no point to refer to
            bool horizontal = sn.Note?.StartsWith("Horizontal", StringComparison.Ordinal) == true, both = sn.Note == "Horizontal and vertical";
            if (sn.Id >= 0) Model.Add(horizontal ? CType.Horizontal : CType.Vertical, refs: [id, sn.Id]);
            if (both && sn.Id2 >= 0) Model.Add(CType.Vertical, refs: [id, sn.Id2]);
            return id;
        }
        if (sn.Kind == SnapKind.Entity) Model.Add(CType.PointOnEntity, 0, 0, false, id, sn.Id);
        else if (sn.Kind == SnapKind.Midpoint) Model.Add(CType.Midpoint, 0, 0, false, id, sn.Id);
        return id;
    }

    // ---------------------------------------------------------------- inference: tell the user what the next click will do
    /// <summary>What the active tool expects the next click to mean ("Horizontal", "Concentric"...). Cleared on every pointer move.</summary>
    public string? Hint { get; set; }

    /// <summary>The circle, arc or ellipse centred on this point; a new round curve centred here is concentric with it.</summary>
    public SkEntity? RoundAtCenter(int pointId) =>
        Model.Entities.FirstOrDefault(e => e.Type is EntType.Circle or EntType.Arc or EntType.Ellipse && e.A == pointId && EntityVisible(e));

    /// <summary>A circle or arc whose radius is within <paramref name="tolerance"/> of <paramref name="r"/>; the nearest one.</summary>
    public SkEntity? RoundOfRadius(double r, double tolerance) =>
        Model.Entities.Where(e => e.Type is EntType.Circle or EntType.Arc && EntityVisible(e) && Math.Abs(Model.Radius(e) - r) < tolerance)
            .OrderBy(e => Math.Abs(Model.Radius(e) - r)).FirstOrDefault();

    /// <summary>A plain-words label for what a snap lands on.</summary>
    public string? DescribeSnap(SnapInfo sn) => sn.Kind switch
    {
        SnapKind.Point => sn.Id == 0 ? "Origin" : RoundAtCenter(sn.Id) != null ? "Center" :
            Model.Entities.Any(e => e.Type == EntType.Line && (e.A == sn.Id || e.B == sn.Id) || e.Type == EntType.Arc && (e.B == sn.Id || e.C == sn.Id)) ? "Endpoint" : "Point",
        SnapKind.Entity => "On curve",
        SnapKind.Midpoint => "Midpoint",
        SnapKind.Gizmo => "Gizmo origin",
        SnapKind.Track => sn.Note,
        SnapKind.Center => "Center",
        SnapKind.Grid when sn.Note != null => sn.Note,
        SnapKind.External => sn.Note ?? "Other curve snap",
        SnapKind.Projected => sn.Note ?? "Projected curve snap",
        _ => null,
    };

    public int PickPoint(CadSession s, double px, double py, double tolPx = 10)
    {
        var cam = s.Camera; int best = -1; double bd = tolPx;
        foreach (var p in Model.Points)
        {
            if (!PointVisible(p.Id)) continue;
            var q = cam.Project(SketchModel.To3(Plane, p)); if (q == null) continue;
            double d = Math.Sqrt((q.Value.x - px) * (q.Value.x - px) + (q.Value.y - py) * (q.Value.y - py));
            if (d < bd) { bd = d; best = p.Id; }
        }
        return best;
    }

    public int PickEntity(CadSession s, double px, double py, double tolPx = 8)
    {
        var cam = s.Camera; int best = -1; double bd = tolPx;
        foreach (var (e, pts) in Model.Polylines(Plane))
        {
            if (!EntityVisible(e)) continue;
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                var a = cam.Project(pts[i]); var b = cam.Project(pts[i + 1]); if (a == null || b == null) continue;
                var (_, d) = Picking.SegDist(px, py, a.Value.x, a.Value.y, b.Value.x, b.Value.y);
                if (d < bd) { bd = d; best = e.Id; }
            }
        }
        foreach (var e in Model.Entities.Where(e => e.Type == EntType.Point && EntityVisible(e)))
        {
            var q = cam.Project(SketchModel.To3(Plane, Model.P(e.A))); if (q == null) continue;
            double d = Math.Sqrt((q.Value.x - px) * (q.Value.x - px) + (q.Value.y - py) * (q.Value.y - py));
            if (d < bd) { bd = d; best = e.Id; }
        }
        return best;
    }

    public SkConstraint? PickDimension(CadSession s, double px, double py)
    {
        foreach (var (c, pos) in _dimLabels)
        {
            var q = s.Camera.Project(pos); if (q == null) continue;
            if (Math.Abs(q.Value.x - px) < 36 && Math.Abs(q.Value.y - py) < 13) return c;
        }
        return null;
    }

    // ---------------------------------------------------------------- dimension editing
    public void EditDimension(CadSession s, SkConstraint c)
    {
        string fmt = c.Type == CType.Angle ? $"{c.Value:0.###}" : $"{c.Value:0.####}";
        s.RequestInput($"{SketchModel.Describe(c.Type)}" + (c.Type == CType.Angle ? " (degrees)" : " (mm)"), fmt, text =>
        {
            if (text == null) return;
            if (!double.TryParse(text.Trim().Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) || v <= 0 && c.Type != CType.Angle && c.Type != CType.HDistance && c.Type != CType.VDistance)
            { s.Say("That is not a valid dimension value.", true); return; }
            var before = Model.Clone(); Push();
            var cc = Model.Constraints.First(x => x.Id == c.Id);
            cc.Value = c.Type is CType.Angle or CType.HDistance or CType.VDistance ? (c.Value < 0 ? -v : v) : v;
            if (!Commit(s, before, "That dimension")) _undo.Pop();
            else s.Say($"{SketchModel.Describe(c.Type)} set to {v:0.###}");
        });
    }

    // ---------------------------------------------------------------- overlay
    static readonly uint ColFree = 0xFF4C94FF, ColFixed = 0xFFEAF0FA, ColConstr = 0xFFB79CFF, ColSel = 0xFFFFC53D, ColHover = 0xFF35B6FF, ColConflict = 0xFFFF6B6F;

    public void BuildOverlay(CadSession s, Overlay o)
    {
        BuildSpaceOverlay(s, o);
        var cam = s.Camera;
        var free = Result.FreeEntities;
        bool conflict = !Result.Converged;
        if (!conflict) BuildFill(o);
        foreach (var (e, pts) in Model.Polylines(Plane))
        {
            if (!EntityVisible(e)) continue;
            bool sel = SelEntities.Contains(e.Id), hov = HoverEntity == e.Id;
            uint col = conflict ? ColConflict : e.Construction ? ColConstr : free.Contains(e.Id) ? ColFree : ColFixed;
            if (sel) col = ColSel; else if (hov) col = ColHover;
            float w = sel ? 3.8f : e.Construction ? 1.5f : 2.5f;
            if (e.Construction && !sel) AddDashed(o, pts, col, w, cam.PixelSizeAt(pts[0]));
            else o.AddPolyline(pts, col, w);
            if (e.Type == EntType.Spline && e.ControlPoints) o.AddPolyline(e.Pts.Select(i => SketchModel.To3(Plane, Model.P(i))).ToList(), 0x906F86A6, 1.2f);
        }
        foreach (var p in Model.Points)
        {
            if (!PointVisible(p.Id)) continue;
            bool sel = SelPoints.Contains(p.Id), hov = HoverPoint == p.Id;
            var pos = SketchModel.To3(Plane, p);
            if (p.Id == 0) { o.AddDot(pos, 0xFFFFA630, 9); continue; }
            uint col = sel ? ColSel : hov ? ColHover : Result.FreePoints.Contains(p.Id) ? ColFree : ColFixed;
            o.AddDot(pos, col, sel || hov ? 11 : GeometryEditing ? 9 : 6);
        }
        // constraint glyphs
        var stack = new Dictionary<(int, bool), int>();
        _glyphs.Clear();
        foreach (var c in Model.Constraints.Where(c => !c.Hidden && !c.IsDimension && ConstraintVisible(c)))
        {
            // the symbol shows on every curve or point the constraint affects, not just the first
            for (int ri = 0; ri < c.Refs.Length; ri++)
            {
                var anchor = AnchorOfRef(c, ri); if (anchor == null) continue;
                var (key, pos) = anchor.Value;
                int n = stack.GetValueOrDefault(key); stack[key] = n + 1;
                double ps = cam.PixelSizeAt(pos);
                var off = Plane.U * (ps * (14 + n * 17)) + Plane.V * (ps * 14);
                o.AddLabel(pos + off, SketchModel.Glyph(c.Type), true, SelConstraint == c.Id ? 6 : Blamed.Contains(c.Id) ? 3 : 2);
                _glyphs.Add((c, pos, n));
            }
        }
        foreach (var acq in Acquired) { var at = AnchorAt(acq); o.AddDot(Plane.PointAt(at.u, at.v), 0xFF6FD6FF, 13); }
        foreach (var (au, av, bu, bv) in TrackSegs)
        {
            double len = Math.Max(Math.Sqrt((bu - au) * (bu - au) + (bv - av) * (bv - av)), 1e-9), ex = (bu - au) / len, ey = (bv - av) / len, ext = cam.PixelSizeAt(Plane.PointAt(bu, bv)) * 30;
            DashedLine(o, Plane.PointAt(au, av), Plane.PointAt(bu + ex * ext, bv + ey * ext), cam.PixelSizeAt(Plane.PointAt(bu, bv)) * 6, 0xFF6FD6FF, 1.3f);
        }
        BuildDimensions(s, o);
        if (ShowCurvature) BuildComb(s, o);
        if (Cursor is { } cur)
        {
            var cp = Plane.PointAt(cur.U, cur.V) + Plane.Normal * cur.Depth;
            o.AddDot(cp, cur.Kind == SnapKind.Point ? 0xFFFFA630 : cur.Kind == SnapKind.Entity ? 0xFF35B6FF : 0xFFFFFFFF, cur.Kind == SnapKind.Grid ? 6 : 11);
            if ((Hint ?? DescribeSnap(cur)) is { } text)
                o.AddLabel(cp + Plane.V * (cam.PixelSizeAt(cp) * 20), text, true, Hint != null ? 4 : 0);
        }
    }

    /// <summary>Show the curvature comb on splines, ellipses and arcs (and on the spline being drawn).</summary>
    public static bool ShowCurvature { get; set; } = false;
    string? _combKey; List<(Vec3 p, Vec3 k)[]> _comb = [];

    void BuildComb(CadSession s, Overlay o)
    {
        var curved = Model.Entities.Where(e => !e.Construction && e.Type is EntType.Spline or EntType.Ellipse or EntType.Arc).ToList();
        if (curved.Count == 0) return;
        string key = string.Join(";", curved.Select(e => $"{e.Id}:{e.R:0.####}:{e.Closed}:{e.ControlPoints}:" + string.Join(",", Model.PointsOf(e).Select(id => $"{Model.P(id).X:0.####}/{Model.P(id).Y:0.####}"))));
        if (key != _combKey)
        {
            _combKey = key; _comb = [];
            foreach (var e in curved)
            {
                try
                {
                    var one = Model.Clone(); one.Entities.RemoveAll(x => x.Id != e.Id);
                    foreach (var sh in one.ToShapes(Plane)) _comb.AddRange(Occ.CurveSamples(sh));
                }
                catch (Exception ex) when (ex is not OutOfMemoryException) { }
            }
        }
        CurvatureComb.Draw(o, s, _comb);
    }

    List<SketchFill.P2>? _fillKeyPts; List<double>? _fillKeyLift; List<(SketchFill.P2, SketchFill.P2, SketchFill.P2)> _fillTris = [];
    readonly System.Collections.Generic.Dictionary<(long, long), Vec3> _fillWorld = [];
    static (long, long) FillKey(SketchFill.P2 p) => ((long)Math.Round(p.X * 1e6), (long)Math.Round(p.Y * 1e6));

    /// <summary>A light tint over every closed profile, so you can see at a glance which shapes are closed.</summary>
    void BuildFill(Overlay o)
    {
        var curves = new List<List<SketchFill.P2>>(); var key = new List<SketchFill.P2>(); var lift = new List<double>();
        _fillWorld.Clear();
        foreach (var (e, pts) in Model.Polylines(Plane))
        {
            if (e.Construction || e.Type == EntType.Point) continue;
            var c = pts.Select(p => { var d = p - Plane.Origin; var q = new SketchFill.P2(Vec3.Dot(d, Plane.U), Vec3.Dot(d, Plane.V)); _fillWorld[FillKey(q)] = p; return q; }).ToList();
            curves.Add(c); key.Add(c[0]); key.Add(c[^1]); key.Add(c[c.Count / 2]);
            // a point lifted off the plane (Free 3D editing) must move the tint too, so the height is part of what is compared
            lift.Add(Vec3.Dot(pts[0] - Plane.Origin, Plane.Normal)); lift.Add(Vec3.Dot(pts[^1] - Plane.Origin, Plane.Normal)); lift.Add(Vec3.Dot(pts[pts.Count / 2] - Plane.Origin, Plane.Normal));
        }
        if (_fillKeyPts == null || !_fillKeyPts.SequenceEqual(key) || _fillKeyLift == null || !_fillKeyLift.SequenceEqual(lift))
        {
            _fillKeyPts = key; _fillKeyLift = lift;
            _fillTris = SketchFill.Triangulate(SketchFill.Loops(curves, 1e-4));
        }
        // the tint is worked out flat, then each corner goes back to where its curve really is, so it follows a shape lifted out of the plane
        Vec3 At(SketchFill.P2 p) => _fillWorld.TryGetValue(FillKey(p), out var world) ? world : Plane.PointAt(p.X, p.Y);
        foreach (var (a, b, c) in _fillTris) o.AddTri(At(a), At(b), At(c), 0x304C94FF);
    }

    /// <summary>A dashed line: the usual look of construction geometry.</summary>
    public static void AddDashed(Overlay o, List<Vec3> pts, uint col, float w, double ps)
    {
        double dash = ps * 8, gap = ps * 5, pos = 0; bool on = true; double left = dash;
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            var a = pts[i]; var b = pts[i + 1]; double len = (b - a).Length, at = 0;
            while (at < len - 1e-12)
            {
                double step = Math.Min(left, len - at);
                if (on) o.AddLine(a + (b - a) * (at / len), a + (b - a) * ((at + step) / len), col, w);
                at += step; left -= step; pos += step;
                if (left <= 1e-12) { on = !on; left = on ? dash : gap; }
            }
        }
    }

    ((int, bool), Vec3)? AnchorOfRef(SkConstraint c, int i)
    {
        var ip = SketchModel.RefIsPoint(c.Type, c.Refs.Length);
        {
            if (ip[i]) { var p = Model.PointOrNull(c.Refs[i]); if (p != null) return ((p.Id, true), Plane.PointAt(p.X, p.Y)); return null; }
            else
            {
                var e = Model.EntityOrNull(c.Refs[i]); if (e == null) return null;
                if (e.Type == EntType.Line) { var a = Model.P(e.A); var b = Model.P(e.B); return ((e.Id, false), Plane.PointAt((a.X + b.X) / 2, (a.Y + b.Y) / 2)); }
                if (e.Type is EntType.Circle or EntType.Arc or EntType.Ellipse) { var cp = Model.P(e.A); return ((e.Id, false), Plane.PointAt(cp.X, cp.Y)); }
                if (e.Type == EntType.Spline && e.Pts.Count > 0) { var sp = Model.P(e.Pts[e.Pts.Count / 2]); return ((e.Id, false), Plane.PointAt(sp.X, sp.Y)); }
            }
            return null;
        }
    }

    ((int, bool), Vec3)? AnchorOf(SkConstraint c)
    {
        var ip = SketchModel.RefIsPoint(c.Type, c.Refs.Length);
        for (int i = 0; i < c.Refs.Length; i++)
        {
            if (ip[i]) { var p = Model.PointOrNull(c.Refs[i]); if (p != null) return ((p.Id, true), Plane.PointAt(p.X, p.Y)); }
            else
            {
                var e = Model.EntityOrNull(c.Refs[i]); if (e == null) continue;
                if (e.Type == EntType.Line) { var a = Model.P(e.A); var b = Model.P(e.B); return ((e.Id, false), Plane.PointAt((a.X + b.X) / 2, (a.Y + b.Y) / 2)); }
                if (e.Type is EntType.Circle or EntType.Arc) { var cp = Model.P(e.A); return ((e.Id, false), Plane.PointAt(cp.X, cp.Y)); }
                if (e.Type == EntType.Spline && e.Pts.Count > 0) { var sp = Model.P(e.Pts[e.Pts.Count / 2]); return ((e.Id, false), Plane.PointAt(sp.X, sp.Y)); }
            }
        }
        return null;
    }

    public const uint DimColor = 0xFFFFFFFF;

    /// <summary>A solid arrowhead whose tip sits at (tu, tv) and points along (du, dv).</summary>
    public static void DimArrow(Overlay o, Plane3 pl, double ps, double tu, double tv, double du, double dv, uint col)
    {
        double len = 11 * ps, w = 3.4 * ps, nu = -dv, nv = du;
        double bu = tu - du * len, bv = tv - dv * len;
        o.AddTri(pl.PointAt(tu, tv), pl.PointAt(bu + nu * w, bv + nv * w), pl.PointAt(bu - nu * w, bv - nv * w), col);
    }

    /// <summary>A dashed guide line (guides are not geometry, so they must not look like it).</summary>
    public static void DashedLine(Overlay o, Vec3 a, Vec3 b, double dashLen, uint col, float width)
    {
        var d = b - a; double total = d.Length;
        if (total < 1e-9 || dashLen <= 0) return;
        var u = d * (1 / total);
        for (double t = 0; t < total; t += dashLen * 1.7) o.AddLine(a + u * t, a + u * Math.Min(t + dashLen, total), col, width);
    }

    void BuildDimensions(CadSession s, Overlay o)
    {
        _dimLabels.Clear();
        var cam = s.Camera; uint col = DimColor;
        foreach (var c in Model.Constraints.Where(c => c.IsDimension && c.Type != CType.Fixed && ConstraintVisible(c)))
        {
            Vec3 W(double x, double y) => Plane.PointAt(x, y);
            string text; Vec3 labelPos;
            switch (c.Type)
            {
                case CType.Distance: case CType.LineLength: case CType.HDistance: case CType.VDistance: case CType.PointLineDistance:
                {
                    int pa, pb;
                    if (c.Type == CType.LineLength) { var l = Model.E(c.Refs[0]); pa = l.A; pb = l.B; }
                    else if (c.Type == CType.PointLineDistance) { pa = c.Refs[0]; pb = -1; }
                    else { pa = c.Refs[0]; pb = c.Refs[1]; }
                    var A = Model.P(pa); double ax = A.X, ay = A.Y, bx, by;
                    if (pb < 0)
                    {
                        var l = Model.E(c.Refs[1]); var la = Model.P(l.A); var lb = Model.P(l.B);
                        double dx = lb.X - la.X, dy = lb.Y - la.Y, len = Math.Max(Math.Sqrt(dx * dx + dy * dy), 1e-9);
                        double t = ((ax - la.X) * dx + (ay - la.Y) * dy) / (len * len);
                        bx = la.X + dx * t; by = la.Y + dy * t;
                    }
                    else { var B = Model.P(pb); bx = B.X; by = B.Y; }
                    double ps = cam.PixelSizeAt(W(ax, ay));
                    double ux, uy;
                    if (c.Type == CType.HDistance) { ux = Math.Sign(bx - ax) == 0 ? 1 : Math.Sign(bx - ax); uy = 0; }
                    else if (c.Type == CType.VDistance) { ux = 0; uy = Math.Sign(by - ay) == 0 ? 1 : Math.Sign(by - ay); }
                    else { double dx = bx - ax, dy = by - ay, len = Math.Max(Math.Sqrt(dx * dx + dy * dy), 1e-9); ux = dx / len; uy = dy / len; }
                    double nx = -uy, ny = ux;
                    // measure along H/V dimensions from the projected end points
                    double a2x = ax, a2y = ay, b2x = bx, b2y = by;
                    if (c.Type == CType.HDistance) b2y = ay; else if (c.Type == CType.VDistance) b2x = ax;
                    double off = c.LabelU is { } lu && c.LabelV is { } lv ? (lu-a2x)*nx+(lv-a2y)*ny : 30 * ps;
                    double p1x = a2x + nx * off, p1y = a2y + ny * off, p2x = b2x + nx * off, p2y = b2y + ny * off;
                    o.AddLine(W(ax, ay), W(p1x + nx * 4 * ps, p1y + ny * 4 * ps), col, 1.2f);
                    o.AddLine(W(bx, by), W(p2x + nx * 4 * ps, p2y + ny * 4 * ps), col, 1.2f);
                    o.AddLine(W(p1x, p1y), W(p2x, p2y), col, 1.6f);
                    { double dl = Math.Max(Math.Sqrt((p2x - p1x) * (p2x - p1x) + (p2y - p1y) * (p2y - p1y)), 1e-9), dx = (p2x - p1x) / dl, dy = (p2y - p1y) / dl;
                      DimArrow(o, Plane, ps, p1x, p1y, -dx, -dy, col); DimArrow(o, Plane, ps, p2x, p2y, dx, dy, col); }
                    labelPos = W((p1x + p2x) / 2, (p1y + p2y) / 2);
                    text = $"{Math.Abs(c.Value):0.##}";
                    break;
                }
                case CType.Radius: case CType.Diameter:
                {
                    var e = Model.E(c.Refs[0]); var ctr = Model.P(e.A); double r = Model.Radius(e);
                    double ang = 0.7, ps = cam.PixelSizeAt(W(ctr.X, ctr.Y));
                    double ex = ctr.X + Math.Cos(ang) * r, ey = ctr.Y + Math.Sin(ang) * r;
                    if (c.Type == CType.Diameter) o.AddLine(W(ctr.X - Math.Cos(ang) * r, ctr.Y - Math.Sin(ang) * r), W(ex, ey), col, 1.6f);
                    else o.AddLine(W(ctr.X, ctr.Y), W(ex, ey), col, 1.6f);
                    DimArrow(o, Plane, ps, ex, ey, Math.Cos(ang), Math.Sin(ang), col);
                    if (c.Type == CType.Diameter) DimArrow(o, Plane, ps, ctr.X - Math.Cos(ang) * r, ctr.Y - Math.Sin(ang) * r, -Math.Cos(ang), -Math.Sin(ang), col);
                    labelPos = W(ex + Math.Cos(ang) * 22 * ps, ey + Math.Sin(ang) * 22 * ps);
                    if(c.LabelU is { } lu&&c.LabelV is { } lv)labelPos=W(lu,lv);
                    o.AddLine(W(ex, ey), labelPos, col, 1.2f);
                    text = (c.Type == CType.Diameter ? "Ø" : "R") + (c.Type == CType.Diameter ? $"{c.Value:0.##}" : $"{c.Value:0.##}");
                    break;
                }
                case CType.Angle:
                {
                    var la = Model.E(c.Refs[0]); var lb = Model.E(c.Refs[1]);
                    var a1 = Model.P(la.A); var a2 = Model.P(la.B); var b1 = Model.P(lb.A); var b2 = Model.P(lb.B);
                    double d1x = a2.X - a1.X, d1y = a2.Y - a1.Y, d2x = b2.X - b1.X, d2y = b2.Y - b1.Y;
                    double den = d1x * d2y - d1y * d2x, ix, iy;
                    if (Math.Abs(den) < 1e-9) { ix = (a1.X + b1.X) / 2; iy = (a1.Y + b1.Y) / 2; }
                    else { double t = ((b1.X - a1.X) * d2y - (b1.Y - a1.Y) * d2x) / den; ix = a1.X + d1x * t; iy = a1.Y + d1y * t; }
                    double ps = cam.PixelSizeAt(W(ix, iy));
                    double l1 = Math.Max(Math.Sqrt(d1x * d1x + d1y * d1y), 1e-9), l2 = Math.Max(Math.Sqrt(d2x * d2x + d2y * d2y), 1e-9);
                    double bxv = d1x / l1 + d2x / l2, byv = d1y / l1 + d2y / l2, bl = Math.Max(Math.Sqrt(bxv * bxv + byv * byv), 1e-9);
                    labelPos = W(ix + bxv / bl * 40 * ps, iy + byv / bl * 40 * ps);
                    if(c.LabelU is { } lu&&c.LabelV is { } lv)labelPos=W(lu,lv);
                    o.AddLine(W(ix, iy), labelPos, col, 1.2f);
                    text = $"{Math.Abs(c.Value):0.##}°";
                    break;
                }
                default: continue;
            }
            o.AddLabel(labelPos, text, true, 1);
            _dimLabels.Add((c, labelPos));
        }
    }
}
