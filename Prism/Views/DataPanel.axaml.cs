using System;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Prism.Commands;
using Prism.Core;
using Prism.Core.Data;
using Prism.ViewModels;

namespace Prism.Views;

/// <summary>The Data panel: the document's tables, edited like a small spreadsheet. Paste from Excel, open a CSV, text or Excel file, type values and formulas.</summary>
public partial class DataPanel : UserControl
{
    const double CellWidth = 96, CellHeight = 26, NumberWidth = 44;
    CadSession? _session;
    DataSheet? _sheet;
    int _row, _col;
    bool _own;               // a change this panel made itself: its cells are already right, so they are not rebuilt (that would drop the caret)

    public DataPanel()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Bind();
        NameBox.LostFocus += (_, _) => CommitName();
        NameBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { CommitName(); e.Handled = true; } };
        RowList.AddHandler(ScrollViewer.ScrollChangedEvent, (_, e) => { if (e.Source is ScrollViewer sv && ReferenceEquals(sv.GetVisualParent(), RowList) | sv.GetVisualAncestors().Contains(RowList)) HeaderScroll.Offset = new Vector(sv.Offset.X, 0); });
        ActualThemeVariantChanged += (_, _) => RefreshSheetTabs();
    }

    MainViewModel? Vm => DataContext as MainViewModel;

    /// <summary>The sheet on show.</summary>
    public DataSheet? Sheet => _sheet;

    void Bind()
    {
        if (Vm == null) return;
        Vm.SessionSwapped += (_, s) => Attach(s);
        Attach(Vm.Session);
    }

    void Attach(CadSession session)
    {
        if (ReferenceEquals(session, _session)) { Rebuild(); return; }
        if (_session != null) _session.DataChanged -= OnDataChanged;
        _session = session;
        session.DataChanged += OnDataChanged;
        _sheet = null;
        Rebuild();
    }

    void OnDataChanged() { if (!_own) Dispatcher.UIThread.Post(Rebuild); }

    /// <summary>Show a sheet.</summary>
    public void Select(DataSheet? sheet) { _sheet = sheet; _row = _col = 0; Rebuild(); }

    // ------------------------------------------------------------------ drawing
    int ShownRows => _sheet == null ? 0 : Math.Max(_sheet.RowCount + 20, 40);
    int ShownColumns => _sheet == null ? 0 : Math.Max(_sheet.ColumnCount + 2, 8);

    IBrush Brush(string key) => this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : Brushes.Gray;

    void Rebuild()
    {
        var sheets = _session?.DataSheets;
        if (sheets != null && (_sheet == null || !sheets.Contains(_sheet))) _sheet = sheets.FirstOrDefault();
        RefreshSheetTabs();
        NameBox.Text = _sheet?.Name ?? "";
        NameBox.IsEnabled = _sheet != null;
        ReferenceText.Text = _sheet == null ? "" : "read it in a tool as @" + _sheet.Name;
        HeaderRow.Children.Clear();
        if (_sheet != null)
        {
            HeaderRow.Children.Add(new Border { Width = NumberWidth, Height = CellHeight, BorderBrush = Brush("Line"), BorderThickness = new Thickness(0, 0, 1, 1) });
            for (int c = 0; c < ShownColumns; c++)
                HeaderRow.Children.Add(new Border
                {
                    Width = CellWidth, Height = CellHeight, BorderBrush = Brush("Line"), BorderThickness = new Thickness(0, 0, 1, 1),
                    Child = new TextBlock { Text = DataSheet.ColumnName(c), Classes = { "faint" }, FontSize = 11.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                });
        }
        RowList.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<int>((r, _) => BuildRow(r), true);
        RowList.ItemsSource = _sheet == null ? Array.Empty<int>() : Enumerable.Range(0, ShownRows).ToList();
        UpdateStatus();
    }

    void RefreshSheetTabs()
    {
        SheetBar.Children.Clear();
        if (_session == null) return;
        foreach (var sheet in _session.DataSheets)
        {
            var b = new Button { Content = sheet.Name, Classes = { "seg" }, Margin = new Thickness(0, 0, 4, 4) };
            b.Classes.Set("on", ReferenceEquals(sheet, _sheet));
            var picked = sheet;
            b.Click += (_, _) => Select(picked);
            SheetBar.Children.Add(b);
        }
        var add = new Button { Content = "+", Classes = { "ghost" }, Padding = new Thickness(10, 4), Margin = new Thickness(0, 0, 4, 4) };
        ToolTip.SetTip(add, "A new, empty sheet");
        add.Click += (_, _) => { if (_session == null) return; _own = true; var s = _session.NewSheet(); _own = false; Select(s); };
        SheetBar.Children.Add(add);
    }

    Control BuildRow(int r)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Border
        {
            Width = NumberWidth, Height = CellHeight, Background = Brush("PanelAlt"), BorderBrush = Brush("Line"), BorderThickness = new Thickness(0, 0, 1, 1),
            Child = new TextBlock { Text = (r + 1).ToString(), Classes = { "faint" }, FontSize = 11.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        });
        for (int c = 0; c < ShownColumns; c++) row.Children.Add(BuildCell(r, c));
        return row;
    }

    Control BuildCell(int r, int c)
    {
        var tb = new TextBox
        {
            Classes = { "cell" }, Tag = c, Width = CellWidth, Height = CellHeight, MinHeight = 0, Padding = new Thickness(6, 0), BorderThickness = new Thickness(0),
            VerticalContentAlignment = VerticalAlignment.Center, FontSize = 12.5, FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace"),
            AcceptsReturn = false, AcceptsTab = false,
        };
        Show(tb, r, c);
        tb.GotFocus += (_, _) => { _row = r; _col = c; tb.Text = _sheet?.Raw(r, c) ?? ""; UpdateStatus(); };
        tb.LostFocus += (_, _) => Commit(tb, r, c);
        tb.KeyDown += (_, e) => OnCellKey(tb, r, c, e);
        return new Border { Width = CellWidth, Height = CellHeight, BorderBrush = Brush("Line"), BorderThickness = new Thickness(0, 0, 1, 1), Child = tb };
    }

    /// <summary>What a cell shows when it is not being edited. A formula's result is tinted, and an error is red.</summary>
    void Show(TextBox tb, int r, int c)
    {
        if (_sheet == null) return;
        string raw = _sheet.Raw(r, c), shown = _sheet.Display(r, c);
        tb.Text = shown;
        tb.Foreground = shown.StartsWith('#') && raw.StartsWith('=') ? Brush("Danger") : raw.StartsWith('=') ? Brush("Accent") : Brush("Ink");
    }

    void Commit(TextBox tb, int r, int c)
    {
        if (_sheet == null || _session == null) return;
        string text = tb.Text ?? "";
        if (text != _sheet.Raw(r, c) && (tb.IsFocused || tb.IsKeyboardFocusWithin || text != _sheet.Display(r, c)))
        {
            // the box held the raw text while it had focus; it only differs from the sheet when the user changed it
            if (tb.Text != _sheet.Display(r, c) || _sheet.Raw(r, c).Length == 0)
            {
                _sheet.Set(r, c, text.Trim());
                _own = true; _session.RaiseData(); _own = false;
            }
        }
        Show(tb, r, c);
        if (_sheet.Rows.Any(row => row.Any(x => x.StartsWith('=')))) RefreshDisplays(tb);
        UpdateStatus();
    }

    /// <summary>After an edit, the cells that hold formulas may have new results.</summary>
    void RefreshDisplays(TextBox except)
    {
        foreach (var container in RowList.GetRealizedContainers().OfType<ListBoxItem>())
        {
            if (container.Content is not int r) continue;
            foreach (var cell in container.GetVisualDescendants().OfType<TextBox>())
                if (!ReferenceEquals(cell, except) && !cell.IsFocused && cell.Tag is int c) Show(cell, r, c);
        }
    }

    void UpdateStatus()
    {
        if (_sheet == null) { StatusText.Text = "No sheet yet. Press + to start one, or Open… to bring in a file."; return; }
        string where = DataSheet.ColumnName(_col) + (_row + 1);
        string raw = _sheet.Raw(_row, _col);
        string cell = raw.StartsWith('=') ? $"{where}:  {raw}  =  {_sheet.Display(_row, _col, true)}" : $"{where}  selected";
        StatusText.Text = $"{_sheet.Name}:  {_sheet.RowCount} rows × {_sheet.ColumnCount} columns   ·   {cell}\nFormulas start with =, such as =A1*2 or =SUM(B1:B9).";
    }

    // ------------------------------------------------------------------ typing
    void OnCellKey(TextBox tb, int r, int c, KeyEventArgs e)
    {
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (e.Key == Key.Enter) { MoveTo(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? r - 1 : r + 1, c); e.Handled = true; }
        else if (e.Key == Key.Down && !ctrl) { MoveTo(r + 1, c); e.Handled = true; }
        else if (e.Key == Key.Up && !ctrl) { MoveTo(r - 1, c); e.Handled = true; }
        else if (e.Key == Key.Escape) { tb.Text = _sheet?.Raw(r, c) ?? ""; e.Handled = true; }
        else if (e.Key == Key.V && ctrl) { e.Handled = true; _ = PasteAsync(r, c); }
    }

    void MoveTo(int r, int c)
    {
        if (_sheet == null || r < 0 || r >= ShownRows) return;
        RowList.ScrollIntoView(r);
        Dispatcher.UIThread.Post(() =>
        {
            var item = RowList.ContainerFromIndex(r);
            var cell = item?.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(t => t.Tag is int tc && tc == c);
            cell?.Focus();
        }, DispatcherPriority.Background);
    }

    // ------------------------------------------------------------------ toolbar
    void CommitName() { if (_sheet != null && _session != null && NameBox.Text is { } n && n.Trim() != _sheet.Name) { _own = true; _session.RenameSheet(_sheet, n); _own = false; Rebuild(); } }

    async void OnOpen(object? sender, RoutedEventArgs e) { if (Vm != null) Vm.ImportTable(); await System.Threading.Tasks.Task.CompletedTask; }

    async void OnPaste(object? sender, RoutedEventArgs e) => await PasteAsync(_row, _col);

    /// <summary>Paste cells at a position: a block copied from Excel fills a block of cells. One value goes in the cell.</summary>
    public async System.Threading.Tasks.Task PasteAsync(int row, int col)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        string? text = clipboard == null ? null : await clipboard.GetTextAsync();
        if (!string.IsNullOrEmpty(text)) PasteText(text, row, col);
    }

    public void PasteText(string text, int row, int col)
    {
        if (_session == null) return;
        _sheet ??= _session.NewSheet();
        var rows = DataSheet.Parse(text);
        if (rows.Count == 0) return;
        if (_sheet.IsEmpty && (rows.Count > 1 || rows[0].Count > 1)) _sheet.Rows = rows;
        else for (int i = 0; i < rows.Count; i++) for (int j = 0; j < rows[i].Count; j++) _sheet.Set(row + i, col + j, rows[i][j]);
        _session.RaiseData(); Rebuild();
    }

    async void OnCopy(object? sender, RoutedEventArgs e)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (_sheet != null && clipboard != null) { await clipboard.SetTextAsync(_sheet.ToTsv()); _session?.Say($"Copied {_sheet.Name}."); }
    }

    async void OnSave(object? sender, RoutedEventArgs e)
    {
        if (_sheet == null || Vm?.PickSaveFile == null) return;
        var path = await Vm.PickSaveFile("Save the table", _sheet.Name + ".csv", "csv");
        if (path == null) return;
        try { File.WriteAllText(path, _sheet.ToCsv(), new UTF8Encoding(true)); _session?.Say("Saved " + Path.GetFileName(path)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _session?.Say("Could not save: " + ex.Message, true); }
    }

    void Edit(Action<DataSheet> change) { if (_sheet == null || _session == null) return; change(_sheet); _session.RaiseData(); Rebuild(); }
    void OnAddRow(object? sender, RoutedEventArgs e) => Edit(s => s.InsertRow(_row));
    void OnAddColumn(object? sender, RoutedEventArgs e) => Edit(s => s.InsertColumn(_col));
    void OnDeleteRow(object? sender, RoutedEventArgs e) => Edit(s => s.DeleteRow(_row));
    void OnDeleteColumn(object? sender, RoutedEventArgs e) => Edit(s => s.DeleteColumn(_col));

    void OnDeleteSheet(object? sender, RoutedEventArgs e)
    {
        if (_sheet == null || _session == null) return;
        _session.RemoveSheet(_sheet); _sheet = null; Rebuild();
    }

    void OnMakeCurve(object? sender, RoutedEventArgs e)
    {
        if (_sheet == null || _session == null) return;
        if (_sheet.NumericRows(2).Count < 2) { _session.Say("This sheet needs at least two rows of numbers (x, y, z) to make a curve.", true); return; }
        _session.StartCommand(() => new EquationCurveCommand(true));
        (_session.ActiveCommand as EquationCurveCommand)?.UseSheet(_sheet.Name);
    }
}
