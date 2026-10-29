using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Prism.Controls;

/// <summary>Connects the native tree rows without replacing their keyboard, selection, or disclosure behavior.</summary>
public sealed class BrowserBranchLines : Control
{
    public static readonly StyledProperty<TreeView?> TreeProperty = AvaloniaProperty.Register<BrowserBranchLines, TreeView?>(nameof(Tree));
    public static readonly StyledProperty<IBrush?> BranchBrushProperty = AvaloniaProperty.Register<BrowserBranchLines, IBrush?>(nameof(BranchBrush));
    public TreeView? Tree { get => GetValue(TreeProperty); set => SetValue(TreeProperty, value); }
    public IBrush? BranchBrush { get => GetValue(BranchBrushProperty); set => SetValue(BranchBrushProperty, value); }
    static BrowserBranchLines() => AffectsRender<BrowserBranchLines>(BranchBrushProperty, TreeProperty);
    public BrowserBranchLines()
    {
        IsHitTestVisible = false; ClipToBounds = true;
        AttachedToVisualTree += (_, _) => { if (Tree != null) Tree.LayoutUpdated += LayoutChanged; };
        DetachedFromVisualTree += (_, _) => { if (Tree != null) Tree.LayoutUpdated -= LayoutChanged; };
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != TreeProperty || !this.IsAttachedToVisualTree()) return;
        if (change.OldValue is TreeView old) old.LayoutUpdated -= LayoutChanged;
        if (change.NewValue is TreeView next) next.LayoutUpdated += LayoutChanged;
    }
    void LayoutChanged(object? sender, EventArgs e) => InvalidateVisual();
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Tree == null || BranchBrush == null) return;
        var rows = Tree.GetVisualDescendants().OfType<TreeViewItem>()
            .Where(item => item.IsEffectivelyVisible)
            .Select(item => (item, row: item.GetVisualDescendants().OfType<Grid>().FirstOrDefault(g => g.Name == "BrowserRow")))
            .Where(x => x.row != null)
            .Select(x => (x.item, row: x.row!, point: x.row!.TranslatePoint(default, this)))
            .Where(x => x.point != null).ToList();
        var childrenByParent = rows.Select(x => (row: x, parent: x.item.GetVisualAncestors().OfType<TreeViewItem>().FirstOrDefault()))
            .Where(x => x.parent != null).GroupBy(x => x.parent!).ToDictionary(g => g.Key, g => g.Select(x => x.row).ToList());
        foreach (var parent in rows.Where(x => x.item.IsExpanded))
        {
            if (!childrenByParent.TryGetValue(parent.item, out var children) || children.Count == 0) continue;
            double x = children[0].point!.Value.X - 23;
            double top = parent.point!.Value.Y + parent.row.Bounds.Height;
            double bottom = children[^1].point!.Value.Y + children[^1].row.Bounds.Height / 2;
            var pen = new Pen(BranchBrush, 1);
            context.DrawLine(pen, new Point(x, top), new Point(x, bottom));
            foreach (var child in children)
            {
                double y = child.point!.Value.Y + child.row.Bounds.Height / 2;
                context.DrawLine(pen, new Point(x, y), new Point(child.point.Value.X - 5, y));
            }
        }
    }
}
