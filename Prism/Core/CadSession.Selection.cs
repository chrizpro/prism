using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Prism.Kernel;

namespace Prism.Core;

/// <summary>Selecting in the viewport: rectangle and lasso, invert, and picking through to the faces behind (X-ray).</summary>
public sealed partial class CadSession
{
    /// <summary>What a drag in empty space does: nothing special (click), draw a rectangle, or draw a lasso.</summary>
    [ObservableProperty] public partial SelectMethod Method { get; set; } = SelectMethod.Click;

    RegionSelect.Region? _region; (double x, double y) _regionStart; PointerInfo _regionDown; bool _regionMoved;
    public bool DrawingRegion => _region != null && _regionMoved;

    /// <summary>Start a rectangle or lasso when the pointer goes down, if one is chosen. A tiny drag falls back to an ordinary click.</summary>
    bool TryBeginRegion(PointerInfo p)
    {
        if (Method == SelectMethod.Click || !p.Left || Sketch != null || ActiveTool != null) return false;
        BeginRegion(p, Method == SelectMethod.Rectangle, Method == SelectMethod.Paint);
        return true;
    }

    void BeginRegion(PointerInfo p, bool box, bool stroke)
    {
        _regionDown = p; _regionStart = (p.X, p.Y); _regionMoved = false;
        _region = new RegionSelect.Region { IsBox = box, Stroke = stroke };
        _region.Points.Add((p.X, p.Y));
    }

    /// <summary>Fusion behaviour: every left press is a candidate drag-rectangle even when no drag method is armed. A release without moving still falls back to a normal click, so picks — including origin planes — work exactly as before, while any real drag box-selects.</summary>
    bool TryBeginClickDrag(PointerInfo p)
    {
        if (Method != SelectMethod.Click || !p.Left || Sketch != null || ActiveTool != null) return false;
        BeginRegion(p, box: true, stroke: false);
        return true;
    }

    Action<RegionSelect.Region, PointerInfo>? _regionDone;
    /// <summary>A tool with its own kind of selection (control points) borrows the rectangle or lasso chosen on the ribbon and gets the finished shape back.</summary>
    public void BeginToolRegion(PointerInfo p, Action<RegionSelect.Region, PointerInfo> done)
    {
        if (Method == SelectMethod.Click) return;
        _regionDone = done; _regionDown = p; _regionStart = (p.X, p.Y); _regionMoved = false;
        _region = new RegionSelect.Region { IsBox = Method == SelectMethod.Rectangle };   // a borrowed region keeps box/lasso shape; Stroke only belongs to session picking
        _region.Points.Add((p.X, p.Y));
    }

    bool RegionMove(PointerInfo p)
    {
        if (_region == null) return false;
        if (!_regionMoved && Math.Abs(p.X - _regionStart.x) + Math.Abs(p.Y - _regionStart.y) < 4) return true;
        _regionMoved = true;
        if (_region.IsBox) { _region.Points.Clear(); _region.Points.Add(_regionStart); _region.Points.Add((p.X, p.Y)); }
        else _region.Points.Add((p.X, p.Y));
        Invalidate();
        return true;
    }

    bool RegionUp(PointerInfo p)
    {
        if (_region == null) return false;
        var region = _region; bool moved = _regionMoved; var done = _regionDone; _region = null; _regionMoved = false; _regionDone = null;
        if (!moved) { if (done == null) ClickAt(_regionDown); return true; }
        RegionMovePoint(region, p);
        if (done != null) done(region, p); else ApplyRegion(region, p);
        Invalidate();
        return true;
    }

    void RegionMovePoint(RegionSelect.Region region, PointerInfo p)
    {
        if (!region.IsBox) return;
        var (sx, sy) = _regionStart;
        region.Points.Clear(); region.Points.Add((Math.Min(sx, p.X), Math.Min(sy, p.Y))); region.Points.Add((Math.Max(sx, p.X), Math.Max(sy, p.Y)));
        region.Crossing = p.X < sx;
    }

    /// <summary>The faces, edges or bodies in a region, replacing the selection (Shift adds, Ctrl takes away).</summary>
    public void ApplyRegion(RegionSelect.Region region, PointerInfo mods)
    {
        var slot = ActiveCommand?.ActiveSlot;
        var filter = ActiveFilter;
        var mode = slot != null ? (filter.HasFlag(SelFilter.Face) ? SelMode.Face : filter.HasFlag(SelFilter.Edge) ? SelMode.Edge : SelMode.Object) : Mode;
        var items = RegionSelect.Collect(this, region, mode, XRay).Where(i => Matches(i, filter)).ToList();
        if (items.Count == 0) { if (!mods.Shift && !mods.Ctrl && slot == null) ClearSelection(); Say("Nothing there."); return; }
        if (slot != null && ActiveCommand is { } cmd)
        {
            if (mods.Ctrl) { cmd.ConfigureInputs(() => { foreach (var i in items) slot.Items.Remove(i); }); cmd.OnChanged(); }
            else foreach (var i in items) if (!slot.Items.Contains(i)) cmd.Pick(i, true);
            Say($"{items.Count} picked");
            return;
        }
        if (mods.Ctrl) { _selection.RemoveAll(items.Contains); }
        else
        {
            if (!mods.Shift) _selection.Clear();
            foreach (var i in items) if (!_selection.Contains(i)) _selection.Add(i);
        }
        SelectionChanged?.Invoke(); Invalidate();
        Say($"{items.Count} selected" + (XRay ? " (through the part)" : ""));
        if (_selection is [{ Kind: SelKind.Datum } only]) ShowDatumOrientation(only);      // one datum caught by a drag is treated like one clicked
    }

    /// <summary>Draws the rectangle or the lasso trail while it is being dragged.</summary>
    void BuildRegionOverlay(Overlay o)
    {
        if (_region == null || !_regionMoved) return;
        if (_region.IsBox) { var (x0, y0) = _region.Points[0]; var (x1, y1) = _region.Points[^1]; o.AddRect(x0, y0, x1, y1); }
        else foreach (var (x, y) in _region.Points) o.AddRect(x - 1.5, y - 1.5, x + 1.5, y + 1.5);
    }

    /// <summary>
    /// What a datum or construction plane looks like on screen, as a few key points and the lines between them, so a rectangle or lasso can
    /// select it like a click does. A filled shape's first four points are its corners in order.
    /// </summary>
    internal sealed record ReferenceShape(SelItem Item, Vec3[] Points, (int a, int b)[] Lines, bool Filled, bool CheckDepth);

    internal IEnumerable<ReferenceShape> ReferenceShapes()
    {
        var cam = Camera;
        foreach (var obj in Objects)
        {
            if (!obj.Visible || HiddenIds.Contains(obj.Id)) continue;
            if (obj.Kind == ObjKind.Plane)
            {
                if (!PlaneShown(obj) || obj.SketchPlane is not { } pl) continue;
                var (hu, hv) = obj.HasBrep ? PlaneHalfSize(obj) : (1, 1); hu *= PlaneScale; hv *= PlaneScale;
                yield return new(SelItem.Of(obj), [pl.PointAt(-hu, -hv), pl.PointAt(hu, -hv), pl.PointAt(hu, hv), pl.PointAt(-hu, hv), pl.Origin], Box, true, false);
            }
            if (obj.SketchData is not { } model) continue;
            for (int i = 0; i < model.Datums.Count; i++)
            {
                var d = model.Datums[i]; var f = d.Frame; var item = new SelItem(obj.Id, SelKind.Datum, i);
                double size = cam.PixelSizeAt(f.Origin) * 38 * d.Scale;      // the size DrawDatumReferences draws
                switch (d.Kind)
                {
                    case Sketching.SketchDatumKind.Point: yield return new(item, [f.Origin], [], false, true); break;
                    case Sketching.SketchDatumKind.Axis: yield return new(item, [f.Origin - f.U * size, f.Origin + f.U * size], [(0, 1)], false, true); break;
                    case Sketching.SketchDatumKind.Plane:
                        yield return new(item, [f.PointAt(-size, -size), f.PointAt(size, -size), f.PointAt(size, size), f.PointAt(-size, size), f.Origin], Box, true, true); break;
                    default:
                        yield return new(item, [f.Origin, f.Origin + f.U * size, f.Origin + f.V * size, f.Origin + f.Normal * size], [(0, 1), (0, 2), (0, 3)], false, true); break;
                }
            }
        }
    }
    static readonly (int a, int b)[] Box = [(0, 1), (1, 2), (2, 3), (3, 0)];

    // ------------------------------------------------------------ invert
    /// <summary>
    /// Select what is not selected. Faces and edges invert within the bodies they belong to (so you can pick a few faces to leave out, invert,
    /// and have the rest). With nothing selected it selects everything of the current kind.
    /// </summary>
    public void InvertSelection()
    {
        if (ActiveCommand is Commands.EditFormCommand form) { form.SelectOp("invert"); return; }
        if (ActiveCommand is Commands.EditPointsCommand points) { points.InvertPoints(); return; }
        var slot = ActiveCommand?.ActiveSlot; var filter = ActiveFilter;
        var mode = slot != null ? (filter.HasFlag(SelFilter.Face) ? SelMode.Face : filter.HasFlag(SelFilter.Edge) ? SelMode.Edge : SelMode.Object) : Mode;
        IEnumerable<SelItem> current = slot != null ? slot.Items : _selection;
        var have = current.ToHashSet();
        var bodies = Objects.Where(o => o.Visible && !HiddenIds.Contains(o.Id) && !o.IsAnnotation && o.HasBrep && o.Kind is ObjKind.Solid or ObjKind.Surface).ToList();
        var next = new List<SelItem>();
        if (mode == SelMode.Object)
        {
            var objs = Objects.Where(o => o.Visible && !HiddenIds.Contains(o.Id) && !o.IsAnnotation && o.Kind != ObjKind.Sketch);
            foreach (var o in objs) { var it = SelItem.Of(o); if (!have.Contains(it)) next.Add(it); }
            foreach (var shape in ReferenceShapes()) if (shape.Item.Kind == SelKind.Datum && !have.Contains(shape.Item)) next.Add(shape.Item);
        }
        else
        {
            var kind = mode == SelMode.Face ? SelKind.Face : SelKind.Edge;
            var touched = have.Where(i => i.Kind == kind).Select(i => i.Obj).ToHashSet();
            foreach (var o in bodies.Where(b => touched.Count == 0 || touched.Contains(b.Id)))
            {
                int n = kind == SelKind.Face ? o.Mesh.FaceCount : o.Mesh.EdgeCount;
                for (int i = 0; i < n; i++) { var it = new SelItem(o.Id, kind, i); if (!have.Contains(it)) next.Add(it); }
            }
        }
        if (slot != null && ActiveCommand is { } cmd)
        {
            cmd.ConfigureInputs(() => { slot.Items.Clear(); foreach (var i in next) if (Matches(i, slot.PickFilter)) slot.TryAdd(i); });
            cmd.OnChanged();
        }
        else { _selection.Clear(); _selection.AddRange(next); SelectionChanged?.Invoke(); }
        Say(next.Count == 0 ? "Nothing left to select" : $"Inverted: {next.Count} selected");
        Invalidate();
    }

    // ------------------------------------------------------------ pick through (X-ray)
    (double x, double y) _cycleAt; int _cycle = -1;

    /// <summary>
    /// With X-ray on, Alt-click picks the face behind the one you just picked, and again for the next one back, then starts over at the front.
    /// </summary>
    SelItem? PickBehind(PointerInfo p)
    {
        var (o, d) = Camera.Ray(p.X, p.Y);
        var hits = new List<(double t, SelItem item)>();
        foreach (var obj in Objects.Where(x => x.Visible && !HiddenIds.Contains(x.Id) && !x.IsAnnotation && x.Kind is ObjKind.Solid or ObjKind.Surface))
        {
            var m = obj.Mesh; var P = m.Positions; var I = m.Indices;
            var seen = new HashSet<int>();
            for (int t = 0; t < I.Length; t += 3)
            {
                double? hit = Ray(o, d, P, I[t] * 3, I[t + 1] * 3, I[t + 2] * 3);
                if (hit is not { } h || h <= 0) continue;
                int face = m.TriFace[t / 3];
                if (Mode == SelMode.Face || ActiveFilter.HasFlag(SelFilter.Face)) { if (seen.Add(face)) hits.Add((h, new SelItem(obj.Id, SelKind.Face, face))); }
                else if (seen.Add(-1)) hits.Add((h, SelItem.Of(obj)));
            }
        }
        if (hits.Count == 0) return null;
        hits.Sort((a, b) => a.t.CompareTo(b.t));
        bool sameSpot = Math.Abs(p.X - _cycleAt.x) < 3 && Math.Abs(p.Y - _cycleAt.y) < 3;
        _cycle = sameSpot ? (_cycle + 1) % hits.Count : 0; _cycleAt = (p.X, p.Y);
        Say($"Through the part: {_cycle + 1} of {hits.Count} under the pointer. Alt-click again for the next one.");
        return hits[_cycle].item;
    }

    static double? Ray(Vec3 o, Vec3 d, float[] P, int a, int b, int c)
    {
        double e1x = P[b] - P[a], e1y = P[b + 1] - P[a + 1], e1z = P[b + 2] - P[a + 2], e2x = P[c] - P[a], e2y = P[c + 1] - P[a + 1], e2z = P[c + 2] - P[a + 2];
        double px = d.Y * e2z - d.Z * e2y, py = d.Z * e2x - d.X * e2z, pz = d.X * e2y - d.Y * e2x;
        double det = e1x * px + e1y * py + e1z * pz; if (Math.Abs(det) < 1e-12) return null;
        double inv = 1 / det, tx = o.X - P[a], ty = o.Y - P[a + 1], tz = o.Z - P[a + 2];
        double u = (tx * px + ty * py + tz * pz) * inv; if (u < 0 || u > 1) return null;
        double qx = ty * e1z - tz * e1y, qy = tz * e1x - tx * e1z, qz = tx * e1y - ty * e1x;
        double v = (d.X * qx + d.Y * qy + d.Z * qz) * inv; if (v < 0 || u + v > 1) return null;
        return (e2x * qx + e2y * qy + e2z * qz) * inv;
    }

    /// <summary>A plain click (also what a rectangle or lasso that was barely dragged does).</summary>
    void ClickAt(PointerInfo p)
    {
        if (XRay && p.Alt && Method == SelectMethod.Click && PickBehind(p) is { } behind)
        {
            if (ActiveCommand != null) { if (ActiveCommand.Pick(behind, p.Ctrl || p.Shift) && ActiveCommand.CommitOnPick) CommitCommand(); }
            else SetSelection(behind, p.Ctrl || p.Shift);
            return;
        }
        var hit = Pick(p.X, p.Y, ActiveFilter);
        if (ActiveCommand != null)
        {
            if (hit is { } r) { var cmd = ActiveCommand; if (cmd.PickPoint(r, p.Ctrl || p.Shift)) { } else if (cmd.Pick(r.Item, p.Ctrl || p.Shift) && cmd.CommitOnPick) CommitCommand(); }
            return;
        }
        if (hit == null) { if (!p.Ctrl && !p.Shift) ClearSelection(); return; }
        SetSelection(hit.Value.Item, p.Ctrl || p.Shift);
    }
}
