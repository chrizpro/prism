using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;

namespace Prism.Ribbon;

/// <summary>
/// Large buttons span the full height; small buttons stack three to a column, or two by two for a four-button group.
/// </summary>
public sealed class RibbonGroupPanel : Panel
{
    public const double RowHeight = 28;
    public const double ContentHeight = RowHeight * 3;   // 84
    const double Gap = 2;

    static bool IsSmall(Control c) => c.DataContext is RibbonButton { Size: RibbonSize.Small };
    bool FourSmall => Children.Count==4 && System.Linq.Enumerable.All(Children,c=>c.IsVisible && IsSmall(c));

    protected override Size MeasureOverride(Size availableSize)
    {
        double x = 0;
        foreach (var column in Columns())
        {
            double w = 0;
            foreach (var c in column)
            {
                c.Measure(new Size(double.PositiveInfinity, column.Count == 1 && !IsSmall(c) ? ContentHeight : RowHeight));
                w = Math.Max(w, c.DesiredSize.Width);
            }
            x += w + Gap;
        }
        return new Size(Math.Max(x - Gap, 0), ContentHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0;
        foreach (var column in Columns())
        {
            double w = 0;
            foreach (var c in column) w = Math.Max(w, c.DesiredSize.Width);
            if (column.Count == 1 && !IsSmall(column[0]))
                column[0].Arrange(new Rect(x, 0, w, ContentHeight));
            else
            {
                double y = FourSmall ? RowHeight*.5 : 0;
                foreach (var c in column) { c.Arrange(new Rect(x, y, w, RowHeight)); y += RowHeight; }
            }
            x += w + Gap;
        }
        return new Size(Math.Max(x - Gap, 0), ContentHeight);
    }

    List<List<Control>> Columns()
    {
        var cols = new List<List<Control>>();
        List<Control>? cur = null;
        foreach (var child in Children)
        {
            if (!child.IsVisible) continue;
            if (IsSmall(child))
            {
                if (cur == null || cur.Count >= (FourSmall?2:3) || !IsSmall(cur[0])) { cur = []; cols.Add(cur); }
                cur.Add(child);
            }
            else { cols.Add([child]); cur = null; }
        }
        return cols;
    }
}
