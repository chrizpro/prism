using System;
using System.Collections.Generic;
using System.Linq;

namespace Prism.Core.Drawing;

/// <summary>Optional, individually editable suggestions. Never called as a side effect of placing a view.</summary>
public static class DrawingSuggestions
{
    public static List<DraftAnnotation> Create(DraftSheet sheet, DraftView view, string category)
    {
        if (view.Unresolved) throw new InvalidOperationException("Resolve this view's source before generating suggestions.");
        var result = new List<DraftAnnotation>();
        void Add(DraftAnnotation a, string key)
        {
            a.ViewId = view.Id; a.SuggestionKey = view.Id + ":" + category + ":" + key;
            if (!sheet.Annotations.Any(old => old.SuggestionKey == a.SuggestionKey)) result.Add(a);
        }
        if (category == "Overall size")
        {
            var strokes = view.Strokes.Where(s => s.Kind is 0 or 1 or 2).ToList();
            var all = strokes.SelectMany(s => s.Points.Select(p => (Point: p, Ref: s.Signature))).ToList();
            if (all.Count == 0) return result;
            var left = all.MinBy(p => p.Point.X); var right = all.MaxBy(p => p.Point.X);
            var bottom = all.MinBy(p => p.Point.Y); var top = all.MaxBy(p => p.Point.Y);
            Add(new() { Type = "horizontal", A = left.Point, B = right.Point, RefA = left.Ref, RefB = right.Ref, Label = new(0, (view.MaxY - view.MinY) * view.Scale / 2 + 12) }, "width");
            Add(new() { Type = "vertical", A = bottom.Point, B = top.Point, RefA = bottom.Ref, RefB = top.Ref, Label = new((view.MaxX - view.MinX) * view.Scale / 2 + 12, 0) }, "height");
        }
        else
        {
            var circles = new List<(DrawingStroke Stroke, SheetPoint Center, double Radius)>();
            foreach (var stroke in view.Strokes.Where(s => s.Kind is 0 or 1 or 2))
                if (stroke.TryCircle(out var center, out double radius) && !circles.Any(c => c.Center.Distance(center) < .01 && Math.Abs(c.Radius - radius) < .01)) circles.Add((stroke, center, radius));
            int index = 0;
            foreach (var c in circles.OrderBy(c => c.Center.Y).ThenBy(c => c.Center.X))
            {
                var pt = view.ToPaper(c.Center); var rel = new SheetPoint(pt.X - view.X, pt.Y - view.Y);
                if (category == "Diameters") Add(new() { Type = "diameter", A = c.Center, Radius = c.Radius, RefA = c.Stroke.Signature, Label = rel + new SheetPoint(18, -12 - (index % 3) * 7) }, c.Stroke.Signature);
                if (category == "Center marks") Add(new() { Type = "center", A = c.Center, Radius = c.Radius, RefA = c.Stroke.Signature, Label = rel }, c.Stroke.Signature);
                if (category == "Hole positions")
                {
                    // Every position is offered. Coordinates use the view minimum as an explicit baseline.
                    var originStroke = view.Strokes.Where(s => s.Kind is 0 or 1 or 2).FirstOrDefault(s => s.Points.Any(p => Math.Abs(p.X - view.MinX) < .01 || Math.Abs(p.Y - view.MinY) < .01));
                    var origin = new SheetPoint(view.MinX, view.MinY);
                    Add(new() { Type = "ordinate-x", A = origin, B = c.Center, RefA = originStroke?.Signature, RefB = c.Stroke.Signature, Label = new(rel.X, -(view.MaxY - view.MinY) * view.Scale / 2 - 12 - index * 6) }, c.Stroke.Signature + ":x");
                    Add(new() { Type = "ordinate-y", A = origin, B = c.Center, RefA = originStroke?.Signature, RefB = c.Stroke.Signature, Label = new(-(view.MaxX - view.MinX) * view.Scale / 2 - 18 - index * 6, rel.Y) }, c.Stroke.Signature + ":y");
                }
                index++;
            }
        }
        // Try vacant text positions without moving any accepted annotation. Remaining conflicts stay visible as warnings.
        var trial = DrawingDocument.Clone(sheet);
        foreach (var a in result)
        {
            trial.Annotations.Add(a);
            if (a.Type is "horizontal" or "vertical" or "center") continue;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                var composition = DrawingSheet.ComposeDraft(trial);
                if (!composition.Warnings.Any(w => w.Id == a.Id && (w.Message.Contains("overlap") || w.Message.Contains("intersects")))) break;
                a.Label += new SheetPoint(0, 7);
            }
        }
        return result;
    }
    public static DraftAnnotation HoleTable(DraftSheet sheet, DraftView view)
    {
        var rows = new List<string> { $"Circle positions from view minimum ({sheet.Units})", "No hole depth / thread classification" };
        double unit = sheet.Units == "in" ? 25.4 : 1; int number = 1;
        foreach (var s in view.Strokes.Where(s => s.Kind is 0 or 1 or 2))
            if (s.TryCircle(out var c, out var r)) rows.Add(FormattableString.Invariant($"{number++}   X {(c.X - view.MinX) / unit:0.###}   Y {(c.Y - view.MinY) / unit:0.###}   Ø {2 * r / unit:0.###}"));
        return new DraftAnnotation { ViewId = view.Id, Type = "note", Text = string.Join('\n', rows), Label = new((view.MaxX - view.MinX) * view.Scale / 2 + 20, -(view.MaxY - view.MinY) * view.Scale / 2) };
    }
}
