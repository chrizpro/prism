using System;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Prism.Commands;
using Prism.Core;
using Prism.Core.Data;
using Prism.ViewModels;

namespace Prism.Views;

/// <summary>The Data panel: the document's tables, edited like a small spreadsheet. Select cells, type values and formulas, copy and paste blocks, sort, fill down, undo, and see the numbers as a picture.</summary>
public partial class DataPanel : UserControl
{
    CadSession? _session;
    DataSheet? _sheet;
    bool _own;               // a change this panel made itself: its cells are already right, so they are not rebuilt (that would drop the selection)

    public DataPanel()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Bind();
        NameBox.LostFocus += (_, _) => CommitName();
        NameBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { CommitName(); e.Handled = true; } };
        Grid.Edited += () => { if (_session == null) return; _own = true; _session.RaiseData(); _own = false; };
        Grid.SelectionChanged += UpdateStatus;
        FormulaBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Grid.SetActive(FormulaBox.Text ?? ""); Grid.Focus(); e.Handled = true; }
            else if (e.Key == Key.Escape) { UpdateStatus(); Grid.Focus(); e.Handled = true; }
        };
        ActualThemeVariantChanged += (_, _) => { RefreshSheetTabs(); Grid.InvalidateVisual(); Plot.InvalidateVisual(); };
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
    public void Select(DataSheet? sheet) { _sheet = sheet; Grid.Sheet = null; Rebuild(); }

    void Rebuild()
    {
        var sheets = _session?.DataSheets;
        if (sheets != null && (_sheet == null || !sheets.Contains(_sheet))) _sheet = sheets.FirstOrDefault();
        RefreshSheetTabs();
        NameBox.Text = _sheet?.Name ?? "";
        NameBox.IsEnabled = _sheet != null;
        ReferenceText.Text = _sheet == null ? "" : "read it in a tool as @" + _sheet.Name;
        Grid.Sheet = _sheet;
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

    /// <summary>The formula bar, the picture, the buttons that need something to act on, and the line of numbers about the selection.</summary>
    void UpdateStatus()
    {
        UndoButton.IsEnabled = Grid.CanUndo; RedoButton.IsEnabled = Grid.CanRedo;
        FormulaBox.IsEnabled = _sheet != null;
        if (_sheet == null)
        {
            CellName.Text = ""; FormulaBox.Text = ""; Plot.Show(null, default);
            StatusText.Text = "No sheet yet. Press + to start one, or Open… to bring in a file.";
            return;
        }
        var (r0, c0, r1, c1) = Grid.Selection;
        string where = DataSheet.ColumnName(Grid.Column) + (Grid.Row + 1);
        bool block = r1 > r0 || c1 > c0;
        CellName.Text = block ? $"{DataSheet.ColumnName(c0)}{r0 + 1}:{DataSheet.ColumnName(c1)}{r1 + 1}" : where;
        if (!FormulaBox.IsKeyboardFocusWithin) FormulaBox.Text = _sheet.Raw(Grid.Row, Grid.Column);
        Plot.Show(_sheet, Grid.Selection);
        string stats = "";
        if (block && _sheet.Stats(r0, c0, r1, c1) is { } s) stats = $"   ·   {s.count} numbers: sum {DataSheet.Format(s.sum)}, average {DataSheet.Format(s.sum / s.count)}, smallest {DataSheet.Format(s.min)}, largest {DataSheet.Format(s.max)}";
        string raw = _sheet.Raw(Grid.Row, Grid.Column);
        string cell = raw.StartsWith('=') ? $"{where}  =  {_sheet.Display(Grid.Row, Grid.Column, true)}" : $"{where} selected";
        StatusText.Text = $"{_sheet.Name}:  {_sheet.RowCount} rows × {_sheet.ColumnCount} columns   ·   {cell}{stats}\nClick a cell and type. Enter, Tab and the arrows move; Shift extends the selection; Ctrl+C, X, V, Z and D copy, cut, paste, undo and fill down. Formulas start with =, such as =A1*2 or =SUM(B1:B9).";
    }

    // ------------------------------------------------------------------ toolbar
    void CommitName() { if (_sheet != null && _session != null && NameBox.Text is { } n && n.Trim() != _sheet.Name) { _own = true; _session.RenameSheet(_sheet, n); _own = false; Rebuild(); } }

    async void OnOpen(object? sender, RoutedEventArgs e) { if (Vm != null) Vm.ImportTable(); await System.Threading.Tasks.Task.CompletedTask; }

    async void OnPaste(object? sender, RoutedEventArgs e)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        string? text = clipboard == null ? null : await clipboard.GetTextAsync();
        if (!string.IsNullOrEmpty(text)) PasteText(text, Grid.Row, Grid.Column);
    }

    /// <summary>Paste cells at a position: a block copied from Excel fills a block of cells. One value goes in the cell.</summary>
    public void PasteText(string text, int row, int col)
    {
        if (_session == null) return;
        _sheet ??= _session.NewSheet();
        if (!ReferenceEquals(Grid.Sheet, _sheet)) Grid.Sheet = _sheet;
        Grid.Select(row, col);
        Grid.Paste(text);
        Rebuild();
    }

    async void OnCopy(object? sender, RoutedEventArgs e)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (_sheet != null && clipboard != null)
        {
            var (r0, c0, r1, c1) = Grid.Selection;
            await clipboard.SetTextAsync(Grid.CopySelection());
            _session?.Say(r1 > r0 || c1 > c0 ? "Copied the selected cells." : $"Copied {_sheet.Name}'s cell.");
        }
    }

    async void OnSave(object? sender, RoutedEventArgs e)
    {
        if (_sheet == null || Vm?.PickSaveFile == null) return;
        var path = await Vm.PickSaveFile("Save the table", _sheet.Name + ".csv", "csv");
        if (path == null) return;
        try { File.WriteAllText(path, _sheet.ToCsv(), new UTF8Encoding(true)); _session?.Say("Saved " + Path.GetFileName(path)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _session?.Say("Could not save: " + ex.Message, true); }
    }

    void OnUndo(object? sender, RoutedEventArgs e) { Grid.Undo(); Grid.Focus(); }
    void OnRedo(object? sender, RoutedEventArgs e) { Grid.Redo(); Grid.Focus(); }
    void OnAddRow(object? sender, RoutedEventArgs e) { int r = Grid.Selection.r0, n = Grid.Selection.r1 - r + 1; Grid.Apply(s => { for (int i = 0; i < n; i++) s.InsertRow(r); }); }
    void OnAddColumn(object? sender, RoutedEventArgs e) { int c = Grid.Selection.c0, n = Grid.Selection.c1 - c + 1; Grid.Apply(s => { for (int i = 0; i < n; i++) s.InsertColumn(c); }); }
    void OnDeleteRow(object? sender, RoutedEventArgs e) { var (r0, _, r1, _) = Grid.Selection; Grid.Apply(s => { for (int r = r1; r >= r0; r--) s.DeleteRow(r); }); }
    void OnDeleteColumn(object? sender, RoutedEventArgs e) { var (_, c0, _, c1) = Grid.Selection; Grid.Apply(s => { for (int c = c1; c >= c0; c--) s.DeleteColumn(c); }); }
    void OnSortUp(object? sender, RoutedEventArgs e) => Grid.Sort(true);
    void OnSortDown(object? sender, RoutedEventArgs e) => Grid.Sort(false);
    void OnFillDown(object? sender, RoutedEventArgs e) { Grid.FillDown(); Grid.Focus(); }

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
