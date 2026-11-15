using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Prism.Core.Data;

namespace Prism.Views;

/// <summary>
/// A spreadsheet grid drawn in one piece: click a cell to select it, drag or Shift+click to select a block, type to replace a cell, F2 or double-click to edit it,
/// arrows, Tab and Enter to move, Ctrl+C / X / V for blocks (formulas move with the cells), Delete to clear, Ctrl+Z / Y to undo and redo, Ctrl+D to fill down.
/// The column letters and row numbers stay in view while the grid scrolls.
/// </summary>
public sealed class SheetGrid : Control
{
    public const double CellWidth = 96, CellHeight = 26, HeadWidth = 44, HeadHeight = 26;
    static readonly CultureInfo Ci = CultureInfo.InvariantCulture;
    static readonly Typeface Mono = new("Cascadia Mono, Consolas, monospace");
    static readonly Typeface Sans = new("Segoe UI, Inter, sans-serif");

    DataSheet? _sheet;
    int _ar, _ac, _cr, _cc;                  // the corner the selection started from, and the cell that is active
    bool _editing, _dragging;
    readonly TextBox _editor;
    ScrollViewer? _scroll;
    readonly List<List<List<string>>> _undo = [], _redo = [];
    string _clipText = ""; List<List<string>>? _clipRaw; int _clipRow, _clipCol;

    /// <summary>The cells changed (typing, pasting, clearing, sorting...). The panel saves them with the document.</summary>
    public event Action? Edited;
    /// <summary>The selection moved or changed size.</summary>
    public event Action? SelectionChanged;

    public SheetGrid()
    {
        Focusable = true;
        ClipToBounds = true;
        _editor = new TextBox
        {
            Classes = { "cell" }, IsVisible = false, Width = CellWidth, Height = CellHeight, MinHeight = 0, Padding = new Thickness(6, 0), BorderThickness = new Thickness(1.5),
            VerticalContentAlignment = VerticalAlignment.Center, FontSize = 12.5, FontFamily = Mono.FontFamily, AcceptsReturn = false, AcceptsTab = false,
        };
        _editor.KeyDown += OnEditorKey;
        _editor.LostFocus += (_, _) => EndEdit(true, false);
        VisualChildren.Add(_editor); LogicalChildren.Add(_editor);
    }

    public DataSheet? Sheet
    {
        get => _sheet;
        set { if (ReferenceEquals(_sheet, value)) { InvalidateAll(); return; } _sheet = value; _cr = _cc = _ar = _ac = 0; _editing = false; _editor.IsVisible = false; _undo.Clear(); _redo.Clear(); InvalidateAll(); SelectionChanged?.Invoke(); }
    }

    public int Row => _cr;
    public int Column => _cc;
    public (int r0, int c0, int r1, int c1) Selection => (Math.Min(_ar, _cr), Math.Min(_ac, _cc), Math.Max(_ar, _cr), Math.Max(_ac, _cc));
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public bool IsEditing => _editing;

    int ShownRows => _sheet == null ? 0 : Math.Max(Math.Max(_sheet.RowCount + 20, 40), _cr + 15);
    int ShownColumns => _sheet == null ? 0 : Math.Max(Math.Max(_sheet.ColumnCount + 2, 8), _cc + 4);

    void InvalidateAll() { InvalidateMeasure(); InvalidateArrange(); InvalidateVisual(); }

    // ------------------------------------------------------------------ layout
    protected override Size MeasureOverride(Size availableSize)
    {
        _editor.Measure(new Size(CellWidth, CellHeight));
        return new Size(HeadWidth + ShownColumns * CellWidth, HeadHeight + ShownRows * CellHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _editor.Arrange(CellRect(_cr, _cc));
        return finalSize;
    }

    static Rect CellRect(int r, int c) => new(HeadWidth + c * CellWidth, HeadHeight + r * CellHeight, CellWidth, CellHeight);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _scroll = this.FindAncestorOfType<ScrollViewer>();
        if (_scroll != null) _scroll.ScrollChanged += (_, _) => InvalidateVisual();
    }

    // ------------------------------------------------------------------ drawing
    IBrush Brush(string key, IBrush fallback) => this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : fallback;

    FormattedText Text(string text, IBrush brush, double width, bool right, Typeface face, double size = 12.5) =>
        new(text, Ci, FlowDirection.LeftToRight, face, size, brush)
        { MaxTextWidth = Math.Max(1, width), MaxTextHeight = CellHeight, Trimming = TextTrimming.CharacterEllipsis, TextAlignment = right ? TextAlignment.Right : TextAlignment.Left };

    public override void Render(DrawingContext context)
    {
        var ink = Brush("Ink", Brushes.Black); var faint = Brush("InkFaint", Brushes.Gray); var line = Brush("Line", Brushes.LightGray);
        var panelAlt = Brush("PanelAlt", Brushes.WhiteSmoke); var bg = Brush("AppBg", Brushes.White);
        var accent = Brush("Accent", Brushes.DodgerBlue); var danger = Brush("Danger", Brushes.Red);
        var linePen = new Pen(line, 1);
        context.DrawRectangle(bg, null, new Rect(Bounds.Size));
        if (_sheet == null) return;

        var offset = _scroll?.Offset ?? default;
        var view = _scroll?.Viewport ?? Bounds.Size;
        double left = offset.X, top = offset.Y, right = left + view.Width, bottom = top + view.Height;
        int r0 = Math.Max(0, (int)((top - HeadHeight) / CellHeight)), r1 = Math.Min(ShownRows - 1, (int)((bottom - HeadHeight) / CellHeight));
        int c0 = Math.Max(0, (int)((left - HeadWidth) / CellWidth)), c1 = Math.Min(ShownColumns - 1, (int)((right - HeadWidth) / CellWidth));
        var (sr0, sc0, sr1, sc1) = Selection;
        IBrush selFill = accent is ISolidColorBrush sb ? new SolidColorBrush(sb.Color, 0.16) : Brushes.Transparent;
        IBrush selHead = accent is ISolidColorBrush sh ? new SolidColorBrush(sh.Color, 0.28) : panelAlt;

        // cells
        for (int r = r0; r <= r1; r++)
        {
            double y = HeadHeight + r * CellHeight;
            for (int c = c0; c <= c1; c++)
            {
                double x = HeadWidth + c * CellWidth;
                var rect = new Rect(x, y, CellWidth, CellHeight);
                if (r >= sr0 && r <= sr1 && c >= sc0 && c <= sc1) context.DrawRectangle(selFill, null, rect);
                if (_editing && r == _cr && c == _cc) continue;
                string raw = _sheet.Raw(r, c);
                if (raw.Length == 0) continue;
                string shown = _sheet.Display(r, c);
                bool formula = raw.StartsWith('='), isError = formula && shown.StartsWith('#');
                bool number = double.TryParse(shown, NumberStyles.Float, Ci, out _);
                var brush = isError ? danger : formula ? accent : ink;
                using (context.PushClip(rect.Deflate(new Thickness(1, 0))))
                {
                    var ft = Text(shown, brush, CellWidth - 12, number || formula && !isError && number, Mono);
                    context.DrawText(ft, new Point(x + 6, y + (CellHeight - ft.Height) / 2));
                }
            }
        }
        // grid lines
        for (int r = r0; r <= r1 + 1; r++) context.DrawLine(linePen, new Point(left, HeadHeight + r * CellHeight), new Point(right, HeadHeight + r * CellHeight));
        for (int c = c0; c <= c1 + 1; c++) context.DrawLine(linePen, new Point(HeadWidth + c * CellWidth, top), new Point(HeadWidth + c * CellWidth, bottom));

        // the active cell
        if (_cr >= r0 && _cr <= r1 + 1 && _cc >= c0 && _cc <= c1 + 1) context.DrawRectangle(null, new Pen(accent, 2), CellRect(_cr, _cc).Deflate(1));

        // sticky headers
        double headTop = top, headLeft = left;
        context.DrawRectangle(panelAlt, null, new Rect(left, headTop, view.Width, HeadHeight));
        context.DrawRectangle(panelAlt, null, new Rect(headLeft, top, HeadWidth, view.Height));
        for (int c = c0; c <= c1; c++)
        {
            double x = HeadWidth + c * CellWidth;
            if (c >= sc0 && c <= sc1) context.DrawRectangle(selHead, null, new Rect(x, headTop, CellWidth, HeadHeight));
            var ft = Text(DataSheet.ColumnName(c), faint, CellWidth, false, Sans, 11.5); ft.TextAlignment = TextAlignment.Center;
            context.DrawText(ft, new Point(x, headTop + (HeadHeight - ft.Height) / 2));
            context.DrawLine(linePen, new Point(x, headTop), new Point(x, headTop + HeadHeight));
        }
        for (int r = r0; r <= r1; r++)
        {
            double y = HeadHeight + r * CellHeight;
            if (r >= sr0 && r <= sr1) context.DrawRectangle(selHead, null, new Rect(headLeft, y, HeadWidth, CellHeight));
            var ft = Text((r + 1).ToString(Ci), faint, HeadWidth, false, Sans, 11.5); ft.TextAlignment = TextAlignment.Center;
            context.DrawText(ft, new Point(headLeft, y + (CellHeight - ft.Height) / 2));
            context.DrawLine(linePen, new Point(headLeft, y + CellHeight), new Point(headLeft + HeadWidth, y + CellHeight));
        }
        context.DrawRectangle(panelAlt, null, new Rect(headLeft, headTop, HeadWidth, HeadHeight));
        context.DrawLine(linePen, new Point(left, headTop + HeadHeight), new Point(right, headTop + HeadHeight));
        context.DrawLine(linePen, new Point(headLeft + HeadWidth, top), new Point(headLeft + HeadWidth, bottom));
    }

    // ------------------------------------------------------------------ changing cells (every change can be undone)
    /// <summary>Change the sheet. The cells are remembered first, so Ctrl+Z puts them back.</summary>
    public void Apply(Action<DataSheet> change)
    {
        if (_sheet == null) return;
        _undo.Add(_sheet.Snapshot()); if (_undo.Count > 100) _undo.RemoveAt(0);
        _redo.Clear();
        change(_sheet);
        Changed();
    }

    void Changed() { Edited?.Invoke(); InvalidateAll(); SelectionChanged?.Invoke(); }

    public void Undo()
    {
        if (_sheet == null || _undo.Count == 0) return;
        _redo.Add(_sheet.Snapshot()); _sheet.Rows = _undo[^1]; _undo.RemoveAt(_undo.Count - 1); Changed();
    }

    public void Redo()
    {
        if (_sheet == null || _redo.Count == 0) return;
        _undo.Add(_sheet.Snapshot()); _sheet.Rows = _redo[^1]; _redo.RemoveAt(_redo.Count - 1); Changed();
    }

    /// <summary>Put text in the active cell (the formula bar uses this).</summary>
    public void SetActive(string text) { if (_sheet != null && text.Trim() != _sheet.Raw(_cr, _cc)) Apply(s => s.Set(_cr, _cc, text.Trim())); }

    public void ClearSelection()
    {
        var (r0, c0, r1, c1) = Selection;
        Apply(s => { for (int r = r0; r <= r1; r++) for (int c = c0; c <= c1; c++) if (s.Raw(r, c).Length > 0) s.Set(r, c, ""); });
    }

    public void FillDown() { var (r0, c0, r1, c1) = Selection; if (r1 > r0) Apply(s => s.FillDown(r0, c0, r1, c1)); }

    /// <summary>Sort the rows by the active column. A first row of names is left where it is.</summary>
    public void Sort(bool ascending)
    {
        if (_sheet == null) return;
        int col = _cc; bool header = _sheet.RowCount > 1 && _sheet.Number(0, col) == null && Enumerable.Range(1, _sheet.RowCount - 1).Any(r => _sheet.Number(r, col) != null);
        Apply(s => s.SortBy(col, ascending, header ? 1 : 0));
    }

    // ------------------------------------------------------------------ selection
    public void Select(int row, int column, bool extend = false)
    {
        _cr = Math.Max(0, row); _cc = Math.Max(0, column);
        if (!extend) { _ar = _cr; _ac = _cc; }
        InvalidateMeasure(); InvalidateArrange(); InvalidateVisual();
        RaiseEvent(new RequestBringIntoViewEventArgs { RoutedEvent = RequestBringIntoViewEvent, TargetObject = this, TargetRect = CellRect(_cr, _cc).Inflate(new Thickness(HeadWidth, HeadHeight, 0, 0)) });
        SelectionChanged?.Invoke();
    }

    void SelectBlock(int r0, int c0, int r1, int c1) { _ar = r0; _ac = c0; _cr = r1; _cc = c1; InvalidateVisual(); SelectionChanged?.Invoke(); }
    int LastRow => Math.Max((_sheet?.RowCount ?? 1) - 1, 0);
    int LastColumn => Math.Max((_sheet?.ColumnCount ?? 1) - 1, 0);

    (int row, int col)? CellAt(Point p, out bool colHeader, out bool rowHeader)
    {
        var offset = _scroll?.Offset ?? default;
        colHeader = p.Y < offset.Y + HeadHeight; rowHeader = p.X < offset.X + HeadWidth;
        int c = (int)Math.Floor((p.X - HeadWidth) / CellWidth), r = (int)Math.Floor((p.Y - HeadHeight) / CellHeight);
        return (Math.Clamp(r, 0, ShownRows - 1), Math.Clamp(c, 0, ShownColumns - 1));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_sheet == null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        EndEdit(true, false);
        Focus();
        var (row, col) = CellAt(e.GetPosition(this), out bool colHeader, out bool rowHeader) ?? (0, 0);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (colHeader && rowHeader) SelectBlock(0, 0, LastRow, LastColumn);
        else if (colHeader) { if (shift) SelectBlock(0, _ac, LastRow, col); else SelectBlock(0, col, LastRow, col); _cr = 0; _cc = col; }
        else if (rowHeader) { if (shift) SelectBlock(_ar, 0, row, LastColumn); else SelectBlock(row, 0, row, LastColumn); _cr = row; _cc = 0; }
        else
        {
            Select(row, col, shift);
            if (e.ClickCount == 2) { BeginEdit(null); return; }
            _dragging = true; e.Pointer.Capture(this);
        }
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging) return;
        if (CellAt(e.GetPosition(this), out _, out _) is { } cell && (cell.row != _cr || cell.col != _cc)) Select(cell.row, cell.col, true);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragging) { _dragging = false; e.Pointer.Capture(null); }
    }

    // ------------------------------------------------------------------ editing in a cell
    void BeginEdit(string? replace)
    {
        if (_sheet == null || _editing) return;
        _editing = true;
        _editor.Text = replace ?? _sheet.Raw(_cr, _cc);
        _editor.IsVisible = true;
        InvalidateArrange(); InvalidateVisual();
        _editor.Focus();
        _editor.CaretIndex = _editor.Text?.Length ?? 0;
        _editor.ClearSelection();
    }

    void EndEdit(bool commit, bool refocus)
    {
        if (!_editing) return;
        _editing = false;
        string text = (_editor.Text ?? "").Trim();
        _editor.IsVisible = false;
        if (commit && _sheet != null && text != _sheet.Raw(_cr, _cc)) { int r = _cr, c = _cc; Apply(s => s.Set(r, c, text)); }
        if (refocus) Focus();
        InvalidateVisual();
    }

    void OnEditorKey(object? sender, KeyEventArgs e)
    {
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        switch (e.Key)
        {
            case Key.Enter: EndEdit(true, true); Select(_cr + (shift ? -1 : 1), _cc); e.Handled = true; break;
            case Key.Tab: EndEdit(true, true); Select(_cr, _cc + (shift ? -1 : 1)); e.Handled = true; break;
            case Key.Escape: EndEdit(false, true); e.Handled = true; break;
            case Key.Down: EndEdit(true, true); Select(_cr + 1, _cc); e.Handled = true; break;
            case Key.Up: EndEdit(true, true); Select(_cr - 1, _cc); e.Handled = true; break;
        }
    }

    // ------------------------------------------------------------------ keyboard
    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (_sheet == null || _editing || string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0])) return;
        BeginEdit(e.Text);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_sheet == null || _editing) return;
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control), shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        bool handled = true;
        switch (e.Key)
        {
            case Key.Down: Select(ctrl ? LastRow : _cr + 1, _cc, shift); break;
            case Key.Up: Select(ctrl ? 0 : _cr - 1, _cc, shift); break;
            case Key.Left: Select(_cr, ctrl ? 0 : _cc - 1, shift); break;
            case Key.Right: Select(_cr, ctrl ? LastColumn : _cc + 1, shift); break;
            case Key.PageDown: Select(_cr + 15, _cc, shift); break;
            case Key.PageUp: Select(_cr - 15, _cc, shift); break;
            case Key.Home: Select(ctrl ? 0 : _cr, 0, shift); break;
            case Key.End: Select(ctrl ? LastRow : _cr, LastColumn, shift); break;
            case Key.Enter: Select(_cr + (shift ? -1 : 1), _cc); break;
            case Key.Tab: Select(_cr, _cc + (shift ? -1 : 1)); break;
            case Key.F2: BeginEdit(null); break;
            case Key.Delete or Key.Back: ClearSelection(); break;
            case Key.A when ctrl: SelectBlock(0, 0, LastRow, LastColumn); break;
            case Key.C when ctrl: _ = CopyAsync(false); break;
            case Key.X when ctrl: _ = CopyAsync(true); break;
            case Key.V when ctrl: _ = PasteAsync(); break;
            case Key.Z when ctrl && shift: Redo(); break;
            case Key.Z when ctrl: Undo(); break;
            case Key.Y when ctrl: Redo(); break;
            case Key.D when ctrl: FillDown(); break;
            default: handled = false; break;
        }
        e.Handled = handled;
    }

    // ------------------------------------------------------------------ the clipboard
    /// <summary>The selected cells as text a spreadsheet understands. A copy made here also remembers its formulas, so pasting it into this grid moves them with the cells.</summary>
    public string CopySelection()
    {
        if (_sheet == null) return "";
        var (r0, c0, r1, c1) = Selection;
        var sb = new StringBuilder();
        _clipRaw = [];
        for (int r = r0; r <= r1; r++)
        {
            var raw = new List<string>(); var shown = new List<string>();
            for (int c = c0; c <= c1; c++) { raw.Add(_sheet.Raw(r, c)); shown.Add(_sheet.Display(r, c, true)); }
            _clipRaw.Add(raw); sb.Append(string.Join("\t", shown)); if (r < r1) sb.Append('\n');
        }
        _clipText = sb.ToString(); _clipRow = r0; _clipCol = c0;
        return _clipText;
    }

    async System.Threading.Tasks.Task CopyAsync(bool cut)
    {
        string text = CopySelection();
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null) await clipboard.SetTextAsync(text);
        if (cut) ClearSelection();
    }

    async System.Threading.Tasks.Task PasteAsync()
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        string? text = clipboard == null ? null : await clipboard.GetTextAsync();
        if (!string.IsNullOrEmpty(text)) Paste(text);
    }

    /// <summary>Paste at the top-left of the selection. Text copied from this grid keeps its formulas (moved with the cells); anything else is split into cells.</summary>
    public void Paste(string text)
    {
        if (_sheet == null) return;
        var (r0, c0, _, _) = Selection;
        bool own = _clipRaw != null && text.Replace("\r\n", "\n").TrimEnd('\n') == _clipText;
        var block = own ? _clipRaw! : DataSheet.Parse(text);
        if (block.Count == 0) return;
        int dr = r0 - _clipRow, dc = c0 - _clipCol;
        Apply(s =>
        {
            if (s.IsEmpty && !own && (block.Count > 1 || block[0].Count > 1)) { s.Rows = block.Select(r => new List<string>(r)).ToList(); return; }
            for (int i = 0; i < block.Count; i++) for (int j = 0; j < block[i].Count; j++) s.Set(r0 + i, c0 + j, own ? DataSheet.MoveFormula(block[i][j], dr, dc) : block[i][j]);
        });
        SelectBlock(r0, c0, r0 + block.Count - 1, c0 + Math.Max(block.Max(r => r.Count), 1) - 1);
    }
}

/// <summary>A small picture of the numbers selected in the grid, so a column can be checked at a glance: one column is plotted against its row, several columns against the first.</summary>
public sealed class SheetPlot : Control
{
    static readonly Typeface Sans = new("Segoe UI, Inter, sans-serif");
    DataSheet? _sheet; int _r0, _c0, _r1, _c1;

    public SheetPlot() { Height = 96; }

    public void Show(DataSheet? sheet, (int r0, int c0, int r1, int c1) selection)
    {
        _sheet = sheet; (_r0, _c0, _r1, _c1) = selection; InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        IBrush Res(string key, IBrush fallback) => this.TryFindResource(key, ActualThemeVariant, out var v) && v is IBrush b ? b : fallback;
        var line = Res("Line", Brushes.LightGray); var faint = Res("InkFaint", Brushes.Gray); var accent = Res("Accent", Brushes.DodgerBlue);
        context.DrawRectangle(Res("AppBg", Brushes.White), null, new Rect(Bounds.Size));
        if (_sheet == null) return;
        var rows = _r0 == _r1 && _c0 == _c1 ? Enumerable.Range(0, _sheet.RowCount) : Enumerable.Range(_r0, _r1 - _r0 + 1);
        int c0 = _c0, c1 = _r0 == _r1 && _c0 == _c1 ? _c0 : _c1;
        var data = rows.Where(r => Enumerable.Range(c0, c1 - c0 + 1).Any(c => _sheet.Number(r, c) != null)).ToList();
        var area = new Rect(36, 8, Math.Max(10, Bounds.Width - 46), Math.Max(10, Bounds.Height - 22));
        string hint = data.Count < 2 ? "Select a column or a block of numbers to see a picture of it." : "";
        if (hint.Length > 0)
        {
            var h = new FormattedText(hint, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Sans, 11.5, faint);
            context.DrawText(h, new Point(12, (Bounds.Height - h.Height) / 2));
            return;
        }
        bool twoOrMore = c1 > c0;
        int xc = twoOrMore ? c0 : -1;
        int first = twoOrMore ? c0 + 1 : c0;
        double? X(int r, int i) => xc >= 0 ? _sheet.Number(r, xc) : i;
        var xs = data.Select((r, i) => X(r, i)).ToList();
        double xmin = xs.Where(v => v != null).DefaultIfEmpty(0).Min()!.Value, xmax = xs.Where(v => v != null).DefaultIfEmpty(1).Max()!.Value;
        if (xmax - xmin < 1e-12) xmax = xmin + 1;
        var ys = new List<double>();
        for (int c = first; c <= c1; c++) foreach (var r in data) if (_sheet.Number(r, c) is { } v) ys.Add(v);
        if (ys.Count == 0) return;
        double ymin = ys.Min(), ymax = ys.Max();
        if (ymax - ymin < 1e-12) { ymin -= 1; ymax += 1; }
        context.DrawRectangle(null, new Pen(line, 1), area);
        void Label(string text, double x, double y, bool right)
        {
            var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Sans, 10, faint);
            context.DrawText(ft, new Point(right ? x - ft.Width : x, y));
        }
        Label(DataSheet.Format(ymax), area.Left - 4, area.Top - 4, true);
        Label(DataSheet.Format(ymin), area.Left - 4, area.Bottom - 10, true);
        Label(DataSheet.Format(xmin), area.Left, area.Bottom + 1, false);
        Label(DataSheet.Format(xmax), area.Right, area.Bottom + 1, true);
        IBrush[] colours = [accent, Brushes.Orange, Brushes.SeaGreen, Brushes.MediumPurple, Brushes.IndianRed, Brushes.Teal];
        for (int c = first; c <= c1; c++)
        {
            var pen = new Pen(colours[(c - first) % colours.Length], 1.6);
            Point? prev = null;
            for (int i = 0; i < data.Count; i++)
            {
                if (X(data[i], i) is not { } x || _sheet.Number(data[i], c) is not { } y) { prev = null; continue; }
                var p = new Point(area.Left + (x - xmin) / (xmax - xmin) * area.Width, area.Bottom - (y - ymin) / (ymax - ymin) * area.Height);
                if (prev is { } q) context.DrawLine(pen, q, p); else context.DrawEllipse(pen.Brush, null, p, 1.8, 1.8);
                prev = p;
            }
        }
    }
}
