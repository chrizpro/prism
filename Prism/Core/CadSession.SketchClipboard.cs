using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core.Sketching;

namespace Prism.Core;

/// <summary>
/// Copy and paste of sketch curves while sketching (owner request 2026-11-19, the "Sketch entities" row of the copy and paste table): the selected
/// curves, their points, and the constraints and dimensions that only refer to copied pieces. Pasting adds them to the open sketch shifted a little
/// so they can be seen, and selects them so Transform (G) can place them.
/// </summary>
public sealed partial class CadSession
{
    sealed record SketchClip(List<SkPoint> Points, List<SkEntity> Entities, List<SkConstraint> Constraints, double Size);
    static SketchClip? _sketchClip;

    bool CopySketchSelection()
    {
        var sk = Sketch!; var m = sk.Model;
        var ents = m.Entities.Where(e => sk.SelEntities.Contains(e.Id)).ToList();
        if (ents.Count == 0) { Say("Select sketch curves to copy.", true); return false; }
        var pointIds = ents.SelectMany(e => m.PointsOf(e)).ToHashSet();
        var ids = pointIds.Concat(ents.Select(e => e.Id)).ToHashSet();
        var pts = m.Points.Where(p => pointIds.Contains(p.Id)).Select(p => new SkPoint { Id = p.Id, X = p.X, Y = p.Y, Z = p.Z }).ToList();
        var copies = ents.Select(e => new SkEntity { Id = e.Id, Type = e.Type, Construction = e.Construction, A = e.A, B = e.B, C = e.C, R = e.R, Pts = [.. e.Pts], Closed = e.Closed, ControlPoints = e.ControlPoints }).ToList();
        var cons = m.Constraints.Where(c => c.Type != CType.Fixed && c.Refs.Length > 0 && c.Refs.All(ids.Contains))
            .Select(c => new SkConstraint { Type = c.Type, Refs = [.. c.Refs], Value = c.Value, V2 = c.V2, LabelU = c.LabelU, LabelV = c.LabelV }).ToList();
        double size = pts.Count == 0 ? 10 : Math.Max(pts.Max(p => p.X) - pts.Min(p => p.X), pts.Max(p => p.Y) - pts.Min(p => p.Y));
        _sketchClip = new SketchClip(pts, copies, cons, size);
        Say($"Copied {ents.Count} sketch curve(s) and {cons.Count} constraint(s) between them. Paste with Ctrl+V while sketching.");
        return true;
    }

    void PasteSketch()
    {
        var sk = Sketch!; var clip = _sketchClip!;
        double d = Math.Max(1, clip.Size * 0.1);   // shifted a little so the copy is visible next to the original
        var pasted = new List<int>();
        sk.Edit(this, "Paste", () =>
        {
            var m = sk.Model; var map = new Dictionary<int, int>();
            foreach (var p in clip.Points) { int id = m.AddPoint(p.X + d, p.Y - d); m.P(id).Z = p.Z; map[p.Id] = id; }
            foreach (var e in clip.Entities)
            {
                int id = m.NextId++;
                m.Entities.Add(new SkEntity { Id = id, Type = e.Type, Construction = e.Construction, A = e.A >= 0 ? map[e.A] : -1, B = e.B >= 0 ? map[e.B] : -1, C = e.C >= 0 ? map[e.C] : -1,
                    R = e.R, Pts = e.Pts.Select(x => map[x]).ToList(), Closed = e.Closed, ControlPoints = e.ControlPoints });
                map[e.Id] = id; pasted.Add(id);
            }
            foreach (var c in clip.Constraints)
            {
                var added = m.Add(c.Type, c.Value, c.V2, false, c.Refs.Select(r => map[r]).ToArray());
                if (c.LabelU is { } u) added.LabelU = u + d;
                if (c.LabelV is { } v) added.LabelV = v - d;
            }
        });
        sk.SelEntities.Clear(); sk.SelPoints.Clear(); foreach (var id in pasted) sk.SelEntities.Add(id);
        NotifySketchChanged(); Invalidate();
        Say($"Pasted {pasted.Count} sketch curve(s). Press G to move them into place.");
    }
}
