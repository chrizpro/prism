using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Prism.Core.Drawing;

namespace Prism.Controls;

public sealed record DrawingPick(Guid ViewId, SheetPoint Model, string Signature, DrawingStroke Stroke);

/// <summary>A paper-space editor. All display overlays are kept outside the exported composition.</summary>
public sealed class DrawingCanvas : Control
{
    public DraftSheet? Sheet { get; private set; }
    public DrawingComposition? Composition { get; private set; }
    public Guid? SelectedId { get; set; }
    public string Tool { get; set; } = "select";
    public List<SheetPoint> Pending { get; } = [];
    public event Action<SheetPoint, DrawingPick?>? Picked;
    public event Action<Guid?>? SelectionChanged;
    public event Action<Guid, SheetPoint>? Moved;
    public event Action? DeleteRequested;
    public event Action? EscapeRequested;
    public event Action? ZoomChanged;
    public double Zoom { get; private set; } = 2;
    Point _origin = new(35, 35), _press, _panStart;
    Guid? _drag;
    bool _panning, _fitted, _autoFit = true;
    SheetPoint _dragDelta;
    DrawingPick? _hover;
    List<DrawingElement> _suggestionElements = [];
    DrawingElement? _placement;
    DraftView? _placementView;
    SheetPoint _cursorPaper;
    double? _guideX, _guideY;
    readonly Dictionary<Guid, SheetPoint> _previewMotion = [];
    public bool Snap { get; set; } = true;
    public DrawingCanvas()
    {
        Focusable = true; ClipToBounds = true; Cursor = new Cursor(StandardCursorType.Arrow);
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        SizeChanged += (_, _) => { if ((!_fitted || _autoFit) && Sheet != null) Fit(); };
    }
    public void CancelInteraction()
    {
        _drag = null; _panning = false; _dragDelta = default; _previewMotion.Clear(); _guideX = null; _guideY = null; InvalidateVisual();
    }
    public void SetSheet(DraftSheet? sheet, DrawingComposition? composition, bool fit = false)
    {
        bool changed = sheet?.Id != Sheet?.Id;
        Sheet = sheet; Composition = composition; _hover = null;
        if (changed || fit) { _fitted = false; Fit(); }
        InvalidateVisual();
    }
    public void PreviewSuggestions(IEnumerable<DraftAnnotation> annotations)
    {
        _suggestionElements.Clear();
        if (Sheet != null)
        {
            var copy = DrawingDocument.Clone(Sheet); var suggestions = annotations.ToList(); var ids = suggestions.Select(a => a.Id).ToHashSet();
            copy.Annotations.AddRange(suggestions.Where(a => copy.Annotations.All(old => old.Id != a.Id)));
            _suggestionElements = DrawingSheet.ComposeDraft(copy).Elements.Where(e => ids.Contains(e.Id)).ToList();
        }
        InvalidateVisual();
    }
    public void PreviewPlacement(DraftView? view)
    {
        _placementView = view; _placement = null;
        if (view != null && Sheet != null)
        {
            var copy = DrawingDocument.Clone(Sheet); copy.Views.Add(view);
            _placement = DrawingSheet.ComposeDraft(copy).Elements.Single(e => e.Id == view.Id);
        }
        InvalidateVisual();
    }
    public void Fit()
    {
        if (Sheet == null || Bounds.Width < 10 || Bounds.Height < 10) return;
        Zoom = Math.Max(.1, Math.Min((Bounds.Width - 64) / Sheet.Width, (Bounds.Height - 64) / Sheet.Height));
        _origin = new((Bounds.Width - Sheet.Width * Zoom) / 2, (Bounds.Height - Sheet.Height * Zoom) / 2);
        _fitted = true; _autoFit = true; ZoomChanged?.Invoke(); InvalidateVisual();
    }
    public void SetZoom(double zoom)
    {
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2); ZoomAt(center, zoom);
    }
    void ZoomAt(Point pixel, double zoom)
    {
        var paper = Paper(pixel); Zoom = Math.Clamp(zoom, .1, 30);
        _origin = new(pixel.X - paper.X * Zoom, pixel.Y - paper.Y * Zoom);
        _fitted = true; _autoFit = false; ZoomChanged?.Invoke(); InvalidateVisual();
    }
    SheetPoint Paper(Point p) => new((p.X - _origin.X) / Zoom, (p.Y - _origin.Y) / Zoom);
    static readonly IBrush Ink = new SolidColorBrush(Color.Parse("#202830"));
    static readonly IBrush Accent = new SolidColorBrush(Color.Parse("#536FC0"));
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        bool dark = ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
        context.FillRectangle(new SolidColorBrush(Color.Parse(dark ? "#282E36" : "#DDE3E8")), Bounds.WithX(0).WithY(0));
        if (Sheet == null)
        {
            var title = Text("Your drawing starts with a sheet", 21, dark ? Brushes.White : Ink);
            context.DrawText(title, new Point(Math.Max(20, (Bounds.Width - title.Width) / 2), Bounds.Height * .4));
            var help = Text("Create a sheet, choose model bodies, then place a view.", 13, dark ? Brushes.LightGray : Ink);
            context.DrawText(help, new Point(Math.Max(20, (Bounds.Width - help.Width) / 2), Bounds.Height * .4 + 38)); return;
        }
        context.FillRectangle(new SolidColorBrush(Color.FromArgb(35, 0, 0, 0)), new Rect(_origin.X + 5, _origin.Y + 6, Sheet.Width * Zoom, Sheet.Height * Zoom));
        using (context.PushTransform(Matrix.CreateScale(Zoom, Zoom) * Matrix.CreateTranslation(_origin.X, _origin.Y)))
        {
            context.FillRectangle(Brushes.White, new Rect(0, 0, Sheet.Width, Sheet.Height));
            foreach (var element in Composition?.Elements ?? [])
            {
                var delta = _previewMotion.GetValueOrDefault(element.Id, element.Id == _drag ? _dragDelta : default);
                using (context.PushTransform(Matrix.CreateTranslation(delta.X, delta.Y)))
                    foreach (var primitive in element.Primitives) DrawPrimitive(context, primitive);
                if (element.Id == SelectedId)
                {
                    var rect = element.Bounds.Translate(new Vector(delta.X, delta.Y)).Inflate(2);
                    context.DrawRectangle(null, new Pen(Accent, .5 / Zoom, new DashStyle([5, 3], 0)), rect);
                    context.FillRectangle(Accent, new Rect(rect.Right - 1.5 / Zoom, rect.Bottom - 1.5 / Zoom, 3 / Zoom, 3 / Zoom));
                }
            }
            using (context.PushOpacity(.4))
            {
                foreach (var element in _suggestionElements) foreach (var primitive in element.Primitives) DrawPrimitive(context, primitive);
                if (_placement != null && _placementView is { } placed)
                {
                    var position = _cursorPaper;
                    if (placed.ParentId is { } parent && placed.Aligned && Sheet.Views.FirstOrDefault(v => v.Id == parent) is { } pv)
                        position = placed.AlignmentAxis == "x" ? new(pv.X, position.Y) : placed.AlignmentAxis == "y" ? new(position.X, pv.Y) : position;
                    using (context.PushTransform(Matrix.CreateTranslation(position.X - placed.X, position.Y - placed.Y)))
                        foreach (var primitive in _placement.Primitives) DrawPrimitive(context, primitive);
                }
            }
            if (_guideX is { } gx) context.DrawLine(new Pen(Accent, .5 / Zoom, new DashStyle([4, 4], 0)), new Point(gx, 0), new Point(gx, Sheet.Height));
            if (_guideY is { } gy) context.DrawLine(new Pen(Accent, .5 / Zoom, new DashStyle([4, 4], 0)), new Point(0, gy), new Point(Sheet.Width, gy));
            if (_hover != null && Tool != "select")
            {
                var view = Sheet.Views.First(v => v.Id == _hover.ViewId); var p = view.ToPaper(_hover.Model);
                context.DrawEllipse(Brushes.White, new Pen(Accent, 1 / Zoom), new Point(p.X, p.Y), 4 / Zoom, 4 / Zoom);
            }
            foreach (var p in Pending) context.DrawEllipse(Accent, null, new Point(p.X, p.Y), 3 / Zoom, 3 / Zoom);
        }
    }
    static FormattedText Text(string s, double size, IBrush color, bool bold = false) => new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Arial", FontStyle.Normal, bold ? FontWeight.Bold : FontWeight.Normal), size, color);
    static void DrawPrimitive(DrawingContext c, Prim p)
    {
        if (p is TextP t)
        {
            var text = Text(t.S, t.Size, Ink, t.Bold);
            double shift = t.Anchor == "middle" ? -text.Width / 2 : t.Anchor == "end" ? -text.Width : 0;
            using (c.PushTransform(Matrix.CreateRotation(-t.Rot * Math.PI / 180) * Matrix.CreateTranslation(t.X, t.Y))) c.DrawText(text, new Point(shift, -t.Size));
            return;
        }
        var points = p is PolyP poly ? poly.Pts : p is FillP fill ? fill.Pts : [];
        if (points.Length < 2) return;
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(new Point(points[0].x, points[0].y), p is FillP);
            foreach (var point in points.Skip(1)) g.LineTo(new Point(point.x, point.y));
            g.EndFigure(p is FillP || p is PolyP { Closed: true });
        }
        if (p is FillP) { c.DrawGeometry(Ink, null, geometry); return; }
        var style = ((PolyP)p).Style;
        double weight = DrawingOutput.Weight(style);
        IDashStyle? dash = style switch { LineStyle.Hidden => new DashStyle([2.4 / weight, 1.2 / weight], 0), LineStyle.Center => new DashStyle([9 / weight, 1.5 / weight, 1.5 / weight, 1.5 / weight], 0), LineStyle.Cut => new DashStyle([10 / weight, 2 / weight, 2 / weight, 2 / weight], 0), _ => null };
        c.DrawGeometry(null, new Pen(style == LineStyle.Hidden ? Brushes.DimGray : Ink, weight, dash), geometry);
    }
    public DrawingPick? FindPick(SheetPoint paper)
    {
        if (Sheet == null) return null;
        DrawingPick? best = null; double distance = 9 / Zoom;
        foreach (var v in Sheet.Views.Where(v => !v.Unresolved))
            foreach (var stroke in v.Strokes.Where(s => v.HiddenLines || s.Kind is not (3 or 4)))
            {
                // Endpoints, circle centers and nearest polyline points provide visible, repeatable geometry picks.
                var points = new List<SheetPoint> { stroke.Points[0], stroke.Points[^1] };
                if (stroke.TryCircle(out var center, out _)) points.Add(center);
                for (int i = 0; i + 1 < stroke.Points.Count; i++)
                {
                    var a = v.ToPaper(stroke.Points[i]); var b = v.ToPaper(stroke.Points[i + 1]);
                    double dx = b.X - a.X, dy = b.Y - a.Y, len = dx * dx + dy * dy;
                    double t = len < 1e-12 ? 0 : Math.Clamp(((paper.X - a.X) * dx + (paper.Y - a.Y) * dy) / len, 0, 1);
                    points.Add(v.ToModel(new(a.X + t * dx, a.Y + t * dy)));
                }
                foreach (var point in points)
                {
                    double d = v.ToPaper(point).Distance(paper);
                    // Prefer true endpoints/centers inside the snap radius over a sliding segment point.
                    bool anchor = point == stroke.Points[0] || point == stroke.Points[^1] || stroke.TryCircle(out var c, out _) && point.Distance(c) < 1e-6;
                    double score = d + (anchor ? -3 / Zoom : 0);
                    if (d < 9 / Zoom && score < distance) { distance = score; best = new(v.Id, point, stroke.Signature, stroke); }
                }
            }
        return best;
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e); Focus(); var prop = e.GetCurrentPoint(this).Properties; _press = e.GetPosition(this);
        if (prop.IsMiddleButtonPressed || prop.IsRightButtonPressed) { _panning = true; _autoFit = false; _panStart = _origin; e.Pointer.Capture(this); e.Handled = true; return; }
        if (!prop.IsLeftButtonPressed) return;
        var paper = Paper(_press);
        if (Tool != "select") { Picked?.Invoke(paper, FindPick(paper)); e.Handled = true; return; }
        var elements = Composition?.Elements;
        var hit = elements?.Where(x => x.Kind == "annotation" && x.Bounds.Inflate(2 / Zoom).Contains(new Point(paper.X, paper.Y))).LastOrDefault()
            ?? elements?.Where(x => x.Kind == "view" && x.Bounds.Contains(new Point(paper.X, paper.Y))).LastOrDefault();
        SelectedId = hit?.Id; SelectionChanged?.Invoke(SelectedId); _drag = SelectedId; _dragDelta = default;
        if (_drag != null) e.Pointer.Capture(this);
        InvalidateVisual(); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e); var pos = e.GetPosition(this); _cursorPaper = Paper(pos);
        if (_panning) { _origin = _panStart + (pos - _press); InvalidateVisual(); return; }
        if (_drag != null)
        {
            double dx = (pos.X - _press.X) / Zoom, dy = (pos.Y - _press.Y) / Zoom;
            _dragDelta = Snap && !e.KeyModifiers.HasFlag(KeyModifiers.Alt) ? new(Math.Round(dx), Math.Round(dy)) : new(dx, dy);
            _previewMotion.Clear(); _guideX = null; _guideY = null;
            if (Sheet?.Views.FirstOrDefault(v => v.Id == _drag) is { } moving)
            {
                if (Snap && !e.KeyModifiers.HasFlag(KeyModifiers.Alt))
                    foreach (var other in Sheet.Views.Where(v => v.Id != moving.Id && v.ParentId != moving.Id))
                    {
                        if (Math.Abs(moving.X + _dragDelta.X - other.X) < 2) { _guideX = other.X; _dragDelta = new(other.X - moving.X, _dragDelta.Y); }
                        if (Math.Abs(moving.Y + _dragDelta.Y - other.Y) < 2) { _guideY = other.Y; _dragDelta = new(_dragDelta.X, other.Y - moving.Y); }
                    }
                var preview = DrawingDocument.Clone(Sheet); preview.MoveView(moving.Id, _dragDelta.X, _dragDelta.Y);
                foreach (var v in preview.Views)
                {
                    var original = Sheet.Views.Single(o => o.Id == v.Id); var delta = new SheetPoint(v.X - original.X, v.Y - original.Y);
                    _previewMotion[v.Id] = delta; foreach (var a in Sheet.Annotations.Where(a => a.ViewId == v.Id)) _previewMotion[a.Id] = delta;
                }
            }
            InvalidateVisual(); return;
        }
        _hover = Tool == "select" ? null : FindPick(Paper(pos)); InvalidateVisual();
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e); var id = _drag; var delta = _dragDelta;
        _drag = null; _panning = false; _dragDelta = default; _previewMotion.Clear(); _guideX = null; _guideY = null; e.Pointer.Capture(null);
        if (id.HasValue && Math.Abs(delta.X) + Math.Abs(delta.Y) > .05) Moved?.Invoke(id.Value, delta);
        InvalidateVisual();
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) { _drag = null; _panning = false; _dragDelta = default; _previewMotion.Clear(); _guideX = null; _guideY = null; InvalidateVisual(); base.OnPointerCaptureLost(e); }
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e) { ZoomAt(e.GetPosition(this), Zoom * Math.Pow(1.15, e.Delta.Y)); e.Handled = true; }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Delete) { DeleteRequested?.Invoke(); e.Handled = true; }
        if (e.Key == Key.Escape) { CancelInteraction(); EscapeRequested?.Invoke(); e.Handled = true; }
        if (e.Key == Key.F) { Fit(); e.Handled = true; }
    }
}
