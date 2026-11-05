using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Prism.Controls;
using Prism.Core;
using Prism.Core.Drawing;
using Prism.ViewModels;

namespace Prism.Views;

public partial class DrawingWorkspace : UserControl
{
    public static readonly StyledProperty<CadSession?> SessionProperty = AvaloniaProperty.Register<DrawingWorkspace, CadSession?>(nameof(Session));
    public CadSession? Session { get => GetValue(SessionProperty); set => SetValue(SessionProperty, value); }
    Guid? _sheetId, _selected;
    readonly Dictionary<string, Control> _fields = [];
    readonly List<DrawingPick> _picks = [];
    List<DraftAnnotation> _suggestions = [];
    DraftView? _placing;
    string? _viewAction;
    bool _refreshing, _confirmDelete;
    Guid? _confirmDeleteId;
    readonly Dictionary<CadSession, Guid?> _activeSheets = [];
    DraftSheet? Sheet => Session?.Drawings.Sheets.FirstOrDefault(s => s.Id == _sheetId);
    DraftView? SelectedView => Sheet?.Views.FirstOrDefault(v => v.Id == _selected)
        ?? (Sheet?.Annotations.FirstOrDefault(a => a.Id == _selected) is { } a ? Sheet.Views.FirstOrDefault(v => v.Id == a.ViewId) : null);
    DraftAnnotation? SelectedAnnotation => Sheet?.Annotations.FirstOrDefault(a => a.Id == _selected);

    public DrawingWorkspace()
    {
        InitializeComponent();
        Paper.Picked += OnPick;
        Paper.SelectionChanged += id => Select(id);
        Paper.Moved += Move;
        Paper.DeleteRequested += DeleteSelected;
        Paper.EscapeRequested += CancelTool;
        Paper.ZoomChanged += UpdateStatus;
        AttachedToVisualTree += (_, _) => Refresh();
        AddHandler(KeyDownEvent, DrawingKeys, RoutingStrategies.Bubble);
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != SessionProperty) return;
        var old = change.GetOldValue<CadSession?>();
        if (old != null) _activeSheets[old] = _sheetId;
        Unhook(old); var next = change.GetNewValue<CadSession?>();
        if (next != null) { next.DrawingsChanged += Refresh; next.SceneChanged += OnSceneChanged; next.PropertyChanged += OnSessionProperty; }
        _sheetId = next != null ? _activeSheets.GetValueOrDefault(next) : null;
        _selected = null; _placing = null; _suggestions.Clear(); _picks.Clear(); Refresh();
    }
    void Unhook(CadSession? s) { if (s == null) return; s.DrawingsChanged -= Refresh; s.SceneChanged -= OnSceneChanged; s.PropertyChanged -= OnSessionProperty; }
    void OnSessionProperty(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(CadSession.FilePath)) UpdateStatus(); }
    void OnSceneChanged() => Refresh();
    void Guard(Action action)
    {
        try { action(); } catch (Exception e) { Instruction.Text = e.Message; Session?.Say(e.Message, true); }
    }
    void Edit(string label, Action<DraftSheet> edit)
    {
        if (Session == null || Sheet == null) throw new InvalidOperationException("Create a sheet first.");
        var id = Sheet.Id; Session.EditDrawing(label, d => edit(d.Sheets.Single(s => s.Id == id)));
    }
    void Refresh()
    {
        if (_refreshing || Paper == null) return;
        _refreshing = true;
        try
        {
            var sheets = Session?.Drawings.Sheets ?? [];
            if (!sheets.Any(s => s.Id == _sheetId)) _sheetId = sheets.FirstOrDefault()?.Id;
            SheetList.ItemsSource = sheets.ToList(); SheetList.SelectedItem = Sheet;
            SheetTabs.ItemsSource = sheets.Select(s => new SheetTabChoice(s, s.Id == _sheetId)).ToList();
            ViewList.ItemsSource = Sheet?.Views.ToList(); AnnotationList.ItemsSource = Sheet?.Annotations.ToList();
            var sources = SourceList.SelectedItems?.OfType<BodyChoice>().Select(x => x.Id).ToHashSet() ?? [];
            SourceList.ItemsSource = Session?.Objects.Where(o => o.HasBrep && o.Kind is ObjKind.Solid or ObjKind.Surface or ObjKind.Form).Select(o => new BodyChoice(o.Id, o.Name)).ToList();
            if (SourceList.ItemsSource is IEnumerable<BodyChoice> choices)
                foreach (var choice in choices.Where(c => sources.Contains(c.Id) || sources.Count == 0 && Session!.SelectedObjects.Any(o => o.Id == c.Id))) SourceList.SelectedItems?.Add(choice);
            if (Sheet?.Views.All(v => v.Id != _selected) != false && Sheet?.Annotations.All(a => a.Id != _selected) != false) _selected = null;
            var composition = Sheet == null ? null : DrawingSheet.ComposeDraft(Sheet);
            if (Sheet != null && Session != null)
                foreach (var v in Sheet.Views.Where(Session.DrawingViewIsStale)) composition!.Warnings.Insert(0, new(v.Id, $"{v.Name}: source changed — Update drawing"));
            Paper.SelectedId = _selected; Paper.SetSheet(Sheet, composition);
            Paper.PreviewSuggestions(_suggestions);
            WarningList.ItemsSource = composition?.Warnings;
            int warnings = composition?.Warnings.Count ?? 0;
            ReviewSummary.Text = Sheet == null ? "No sheet selected" : warnings == 0 ? "No layout or reference warnings. Review dimensions for manufacturing intent." : $"{warnings} review item(s). Select one to locate it.";
            EmptyActions.IsVisible = Sheet == null; ApplyButton.IsEnabled = Sheet != null;
            if (Sheet != null && Instruction.Text == "Create a sheet to start a drawing.") Instruction.Text = "Select a view or annotation to edit it. Use Views to place more geometry; Suggest offers optional dimensions.";
            ViewList.SelectedItem = Sheet?.Views.FirstOrDefault(v => v.Id == _selected);
            AnnotationList.SelectedItem = SelectedAnnotation;
            SuggestionsPane.IsVisible = _suggestions.Count > 0; SuggestionList.ItemsSource = _suggestions.ToList();
            BuildProperties(); UpdateStatus();
        }
        finally { _refreshing = false; }
    }
    sealed record BodyChoice(Guid Id, string Name) { public override string ToString() => Name; }
    sealed record SheetTabChoice(DraftSheet Sheet, bool IsCurrent) { public string Name => Sheet.Name; }
    void UpdateStatus()
    {
        if (SheetStatus == null) return;
        var sheets = Session?.Drawings.Sheets;
        SheetStatus.Text = Sheet is { } s ? $"{s.Name}  •  {sheets!.FindIndex(x => x.Id == s.Id) + 1}/{sheets.Count}  •  {s.Format}  •  {s.Units}  •  Scale {s.Scale:0.###}:1  •  Zoom {Paper.Zoom / (96 / 25.4) * 100:0}%" : "No drawing sheets";
    }
    void Select(Guid? id)
    {
        _selected = id; _confirmDelete = false; _confirmDeleteId = null;
        Paper.SelectedId = id; Paper.InvalidateVisual();
        _viewAction = null; BuildProperties();
        if (!_refreshing) { _refreshing = true; ViewList.SelectedItem = Sheet?.Views.FirstOrDefault(v => v.Id == id); AnnotationList.SelectedItem = SelectedAnnotation; _refreshing = false; }
    }
    void OnSheetSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || SheetList.SelectedItem is not DraftSheet s) return;
        _sheetId = s.Id; _selected = null; CancelTool(); _suggestions.Clear(); Refresh();
    }
    void OnSheetTab(object? sender, RoutedEventArgs e) { if (sender is Button { Tag: DraftSheet s }) { _sheetId = s.Id; _selected = null; _suggestions.Clear(); CancelTool(); Refresh(); } }
    void OnViewSelected(object? sender, SelectionChangedEventArgs e) { if (!_refreshing && ViewList.SelectedItem is DraftView v) Select(v.Id); }
    void OnAnnotationSelected(object? sender, SelectionChangedEventArgs e) { if (!_refreshing && AnnotationList.SelectedItem is DraftAnnotation a) Select(a.Id); }
    void OnWarningSelected(object? sender, SelectionChangedEventArgs e) { if (WarningList.SelectedItem is DrawingWarning w) Select(w.Id); }
    void OnNewSheet(object? sender, RoutedEventArgs e) => Guard(() => { var s = Session?.NewDrawingSheet(); if (s == null) return; _sheetId = s.Id; _selected = null; CancelTool(); Refresh(); });
    void OnNewDocument(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) vm.NewDocumentTab(); }
    void OnFit(object? sender, RoutedEventArgs e) => Paper.Fit();
    void OnActualSize(object? sender, RoutedEventArgs e) => Paper.SetZoom(96 / 25.4);
    void OnToggleBrowser(object? sender, RoutedEventArgs e) { BrowserPane.IsVisible = !BrowserPane.IsVisible; DrawingGrid.ColumnDefinitions[0].Width = new GridLength(BrowserPane.IsVisible ? 206 : 0); }
    void OnToggleProperties(object? sender, RoutedEventArgs e) { PropertyPane.IsVisible = !PropertyPane.IsVisible; DrawingGrid.ColumnDefinitions[2].Width = new GridLength(PropertyPane.IsVisible ? 272 : 0); }
    void OnSelectTool(object? sender, RoutedEventArgs e) => CancelTool();
    void CancelTool()
    {
        _picks.Clear(); _placing = null; _viewAction = null; Paper.Pending.Clear(); Paper.Tool = "select";
        Paper.PreviewPlacement(null); Paper.CancelInteraction(); _confirmDelete = false; _confirmDeleteId = null;
        Instruction.Text = "Select an item to edit it. Drag to position. Wheel to zoom; middle or right drag to pan. Alt bypasses placement snapping.";
        BuildProperties(); Paper.InvalidateVisual();
    }
    void DrawingKeys(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.Source is TextBox or NumericUpDown) return;
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (ctrl && e.Key == Key.Z) { Guard(() => Session?.Undo()); e.Handled = true; }
        else if (ctrl && e.Key == Key.Y) { Guard(() => Session?.Redo()); e.Handled = true; }
        else if (e.Key == Key.Escape) { CancelTool(); e.Handled = true; }
        else if (e.Key == Key.Delete) { DeleteSelected(); e.Handled = true; }
        // Keep modeling shortcuts out of the paper editor. File shortcuts still reach MainWindow.
        else if (!ctrl && !e.KeyModifiers.HasFlag(KeyModifiers.Alt)) e.Handled = true;
    }

    void OnMenu(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string menu } button) return;
        var items = new List<MenuItem>();
        void Item(string name, Action action) { var m = new MenuItem { Header = name }; m.Click += (_, _) => Guard(action); items.Add(m); }
        switch (menu)
        {
            case "sheet":
                Item("New sheet", () => OnNewSheet(null, e));
                Item("Sheet properties", () => { Select(null); BuildProperties(); });
                Item("Duplicate sheet", () => { if (Sheet is not { } s) return; var copy = s.Duplicate(); Session!.EditDrawing("Duplicate drawing sheet", d => d.Sheets.Add(copy)); _sheetId = copy.Id; Refresh(); });
                Item("Delete sheet", () => { if (Sheet is not { } s) return; if (!_confirmDelete || _confirmDeleteId != s.Id) { _confirmDelete = true; _confirmDeleteId = s.Id; Instruction.Text = $"Delete {s.Name}, its {s.Views.Count} views and {s.Annotations.Count} annotations? Choose Delete sheet again to confirm; Esc cancels."; return; } Session!.EditDrawing("Delete drawing sheet", d => d.Sheets.RemoveAll(x => x.Id == s.Id)); CancelTool(); }); break;
            case "views":
                Item("Base view…", () => BeginView("base")); Item("Projected view…", () => BeginView("projected"));
                Item("Isometric view…", () => BeginView("iso")); Item("Section view…", () => BeginView("section")); Item("Detail view…", () => BeginView("detail")); break;
            case "dimensions":
                foreach (var type in new[] { "horizontal", "vertical", "aligned", "angular", "diameter", "radius", "ordinate-x", "ordinate-y" }) { string t = type; Item(type, () => BeginTool(t)); } break;
            case "annotate":
                foreach (var type in new[] { "note", "leader", "hole", "center", "centerline", "balloon", "datum", "gdt", "finish" }) { string t = type; Item(type switch { "hole" => "Hole callout", "center" => "Center mark", "gdt" => "Feature control frame", "finish" => "Surface finish", _ => type }, () => BeginTool(t)); } break;
            case "tables":
                Item("Parts list (one row per body)", () => Edit("Add parts list", s => { s.ShowParts = true; var ids = s.Views.SelectMany(v => v.Sources).Distinct(); foreach (var id in ids) if (s.Parts.All(p => p.SourceId != id) && Session!.Find(id) is { } body) s.Parts.Add(new() { SourceId = id, Name = body.Name, Material = body.Material?.Name ?? "", Quantity = 1 }); }));
                Item("Circle position table", () => { var v = NeedView(); Edit("Add circle position table", s => s.Annotations.Add(DrawingSuggestions.HoleTable(s, v))); }); break;
            case "suggest":
                foreach (var category in new[] { "Overall size", "Diameters", "Center marks", "Hole positions" }) { string cat = category; Item(category, () => Suggest(cat)); } break;
            case "export":
                Item("PDF — all sheets…", () => Export("pdf", true)); Item("PDF — current sheet…", () => Export("pdf", false));
                Item("SVG — current sheet…", () => Export("svg", false)); Item("DXF — current sheet…", () => Export("dxf", false)); break;
        }
        var popup = new ContextMenu { ItemsSource = items }; popup.Open(button);
    }
    DraftView NeedView() => SelectedView ?? throw new InvalidOperationException("Select a drawing view first.");
    void BeginView(string kind)
    {
        if (Sheet == null) throw new InvalidOperationException("Create a sheet first.");
        if (kind is "projected" or "section" or "detail") NeedView();
        CancelTool(); _viewAction = kind; BuildProperties();
        Instruction.Text = "Set the view properties, choose Prepare view, then click the sheet to place it.";
    }
    void OnViewAction(object? sender, RoutedEventArgs e) => Guard(() => BeginView((sender as Button)?.Tag as string ?? "base"));
    void BeginTool(string type)
    {
        if (Sheet == null) throw new InvalidOperationException("Create a sheet first.");
        CancelTool(); Paper.Tool = type;
        Instruction.Text = type == "note" ? "Select a view, then click the sheet to place a note. Edit its text in Properties." : type is "diameter" or "radius" or "center" ? "Pick a circular edge, then place its label. Esc cancels." : type == "angular" ? "Pick arm point, vertex, then second arm point. Click again to place the angle." : "Pick geometry in one view, then click to place the annotation. Esc cancels.";
        Paper.Focus();
    }
    void OnPick(SheetPoint paper, DrawingPick? pick) => Guard(() =>
    {
        if (Sheet == null) return;
        if (_placing is { } placing)
        {
            placing.X = paper.X; placing.Y = paper.Y;
            Edit("Place drawing view", s => { s.Views.Add(placing); if (placing.ParentId != null && placing.Aligned) s.MoveView(placing.Id, 0, 0); });
            _selected = placing.Id; CancelTool(); Refresh(); return;
        }
        string type = Paper.Tool;
        if (type == "select") return;
        int required = type == "note" ? 0 : type == "angular" ? 3 : type is "horizontal" or "vertical" or "aligned" or "centerline" or "ordinate-x" or "ordinate-y" ? 2 : 1;
        if (_picks.Count < required)
        {
            if (pick == null) throw new InvalidOperationException("Pick a visible edge endpoint, circle center or point on a projected edge.");
            if (_picks.Count > 0 && _picks[0].ViewId != pick.ViewId) throw new InvalidOperationException("Pick geometry in the same drawing view.");
            if (Sheet.Views.Single(v => v.Id == pick.ViewId).Orientation == "iso" && type is "horizontal" or "vertical" or "aligned" or "angular" or "diameter" or "radius" or "ordinate-x" or "ordinate-y") throw new InvalidOperationException("Place dimensions on an orthographic, section or detail view.");
            if (type == "radius" && !pick.Stroke.TryArc(out _, out _)) throw new InvalidOperationException("Pick a circular edge or arc for a radius dimension.");
            if (type is "diameter" or "center" && !pick.Stroke.TryCircle(out _, out _)) throw new InvalidOperationException("Pick a complete circular edge for this annotation.");
            _picks.Add(pick); var v = Sheet.Views.Single(v => v.Id == pick.ViewId); Paper.Pending.Add(v.ToPaper(pick.Model));
            if (_picks.Count < required) { Instruction.Text = $"{_picks.Count}/{required} geometry picks. Pick the next point."; Paper.InvalidateVisual(); return; }
            if (type is not ("center" or "centerline")) { Instruction.Text = "Click the sheet to place the annotation."; Paper.InvalidateVisual(); return; }
        }
        var view = _picks.Count > 0 ? Sheet.Views.Single(v => v.Id == _picks[0].ViewId) : NeedView();
        var a = new DraftAnnotation { ViewId = view.Id, Type = type, Label = new(paper.X - view.X, paper.Y - view.Y), A = _picks.FirstOrDefault()?.Model ?? view.ToModel(paper), RefA = _picks.FirstOrDefault()?.Signature,
            B = _picks.Count > 1 ? _picks[1].Model : default, RefB = _picks.Count > 1 ? _picks[1].Signature : null,
            C = _picks.Count > 2 ? _picks[2].Model : default, RefC = _picks.Count > 2 ? _picks[2].Signature : null,
            Text = type is "note" or "leader" ? "Note" : type == "datum" ? "A" : type == "balloon" ? "1" : "" };
        if (type is "diameter" or "radius" or "center" or "hole")
        {
            if (type == "radius" ? _picks[0].Stroke.TryArc(out var center, out double r) : _picks[0].Stroke.TryCircle(out center, out r)) { a.A = center; a.Radius = r; if (type == "hole") a.Text = "Ø" + (2 * r / (Sheet.Units == "in" ? 25.4 : 1)).ToString("0.###", CultureInfo.InvariantCulture); }
            else if (type == "hole") a.Text = "Hole — enter specification";
        }
        if (a.IsDimension && type is not ("radius" or "diameter") && a.Measurement < 1e-8) throw new InvalidOperationException("The dimension needs distinct points and a non-zero measurement.");
        Edit("Add drawing annotation", s => s.Annotations.Add(a)); _selected = a.Id; CancelTool(); Refresh();
    });
    void Move(Guid id, SheetPoint delta) => Guard(() => Edit("Move drawing item", s =>
    {
        if (s.Views.Any(v => v.Id == id)) s.MoveView(id, delta.X, delta.Y);
        else if (s.Annotations.FirstOrDefault(a => a.Id == id) is { } a) a.Label += delta;
    }));
    void OnUpdate(object? sender, RoutedEventArgs e) => Guard(() => { if (Sheet != null) Session!.UpdateDrawing(Sheet.Id); });
    void OnDelete(object? sender, RoutedEventArgs e) => DeleteSelected();
    void DeleteSelected() => Guard(() =>
    {
        if (Sheet == null || !_selected.HasValue) return;
        Guid id = _selected.Value;
        if (Sheet.Views.Any(v => v.Id == id))
        {
            var dependents = Sheet.Dependents(id); int annotations = Sheet.Annotations.Count(a => dependents.Contains(a.ViewId));
            if (!_confirmDelete || _confirmDeleteId != id) { _confirmDelete = true; _confirmDeleteId = id; Instruction.Text = $"Delete {dependents.Count} view(s) and {annotations} attached annotation(s)? Press Delete again to confirm; Esc cancels."; return; }
            Edit("Delete drawing views", s => { s.Views.RemoveAll(v => dependents.Contains(v.Id)); s.Annotations.RemoveAll(a => dependents.Contains(a.ViewId)); });
        }
        else Edit("Delete annotation", s => s.Annotations.RemoveAll(a => a.Id == id));
        _selected = null; CancelTool(); Refresh();
    });
    void OnSheetProperties(object? sender, RoutedEventArgs e) { Select(null); BuildProperties(); }

    TextBox TextField(string id, string caption, string value, bool multiline = false)
    {
        PropertyFields.Children.Add(new TextBlock { Text = caption, Classes = { "caption" } });
        var box = new TextBox { Text = value, AcceptsReturn = multiline, MinHeight = multiline ? 68 : 0, TextWrapping = multiline ? Avalonia.Media.TextWrapping.Wrap : Avalonia.Media.TextWrapping.NoWrap };
        PropertyFields.Children.Add(box); _fields[id] = box; return box;
    }
    void NumberField(string id, string caption, double value, double min = -100000, double max = 100000)
    {
        PropertyFields.Children.Add(new TextBlock { Text = caption, Classes = { "caption" } });
        var n = new NumericUpDown { Value = (decimal)Math.Clamp(value, min, max), Minimum = (decimal)min, Maximum = (decimal)max, Increment = .1m, FormatString = "0.###" };
        PropertyFields.Children.Add(n); _fields[id] = n;
    }
    void ChoiceField(string id, string caption, string[] values, string value)
    {
        PropertyFields.Children.Add(new TextBlock { Text = caption, Classes = { "caption" } });
        var c = new ComboBox { ItemsSource = values, SelectedItem = value, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
        PropertyFields.Children.Add(c); _fields[id] = c;
    }
    void CheckField(string id, string caption, bool value)
    {
        var c = new CheckBox { Content = caption, IsChecked = value }; PropertyFields.Children.Add(c); _fields[id] = c;
    }
    string Txt(string id) => ((TextBox)_fields[id]).Text?.Trim() ?? "";
    string Choice(string id) => ((ComboBox)_fields[id]).SelectedItem as string ?? "";
    double Number(string id) => (double)(((NumericUpDown)_fields[id]).Value ?? 0);
    bool Check(string id) => ((CheckBox)_fields[id]).IsChecked == true;
    void BuildProperties()
    {
        if (PropertyFields == null) return;
        PropertyFields.Children.Clear(); _fields.Clear(); ApplyButton.Content = "Apply changes";
        ApplyButton.IsVisible = _suggestions.Count == 0;
        DeleteButton.IsVisible = _selected.HasValue && _suggestions.Count == 0;
        if (Sheet == null) { PropertyTitle.Text = "Drawing"; PropertyFields.Children.Add(new TextBlock { Text = "Create a sheet to choose its size, units and projection.", TextWrapping = Avalonia.Media.TextWrapping.Wrap }); return; }
        if (_suggestions.Count > 0) { PropertyTitle.Text = "Review suggestions"; PropertyFields.Children.Add(new TextBlock { Text = "Faint annotations on the sheet are previews. Accept only the information this drawing needs.", TextWrapping = Avalonia.Media.TextWrapping.Wrap }); return; }
        if (_viewAction != null)
        {
            PropertyTitle.Text = "Place " + _viewAction + " view"; ApplyButton.Content = "Prepare view";
            if (_viewAction == "base") ChoiceField("orientation", "Orientation", ["front", "top", "right", "back", "left", "bottom", "iso"], "front");
            if (_viewAction == "projected") ChoiceField("orientation", "Project from parent", ["above", "below", "left", "right"], "above");
            NumberField("scale", "Scale", SelectedView?.Scale ?? Sheet.Scale, .001, 100);
            if (_viewAction == "section") { CheckField("vertical", "Vertical cutting line", true); NumberField("cut", "Cut coordinate (model mm)", SelectedView is { } v ? (v.MinX + v.MaxX) / 2 : 0); }
            if (_viewAction == "detail")
            {
                var v = NeedView(); NumberField("cx", "Center X (view model mm)", (v.MinX + v.MaxX) / 2); NumberField("cy", "Center Y (view model mm)", (v.MinY + v.MaxY) / 2);
                NumberField("radius", "Detail radius (model mm)", 10, .1); NumberField("ratio", "Magnification", 2, .1, 100);
            }
            return;
        }
        if (SelectedAnnotation is { } a)
        {
            PropertyTitle.Text = "Annotation: " + a.Type;
            if (a.IsDimension) { var measured = DrawingDocument.Clone(a); measured.Text = ""; PropertyFields.Children.Add(new TextBlock { Text = "Measured: " + measured.ValueText(Sheet.Units) + (a.Text.Length > 0 ? " (text override active)" : ""), TextWrapping = Avalonia.Media.TextWrapping.Wrap }); }
            TextField("text", a.IsDimension ? "Text override (blank = measured)" : "Text", a.Text, !a.IsDimension);
            NumberField("precision", "Decimal places", a.Precision, 0, 6); TextField("tolerance", "Tolerance (optional)", a.Tolerance);
            NumberField("x", "Label X from view center (paper mm)", a.Label.X); NumberField("y", "Label Y from view center (paper mm)", a.Label.Y);
            if (a.Type == "gdt") { ChoiceField("symbol", "Characteristic", ["position", "flatness", "straightness", "circularity", "cylindricity", "perpendicularity", "parallelism", "angularity", "concentricity", "symmetry", "runout", "total_runout", "profile_line", "profile_surface"], a.Symbol); TextField("datums", "Datums (separate with |)", a.Datums); }
            if (a.Unresolved) PropertyFields.Children.Add(new TextBlock { Text = "Geometry reference unresolved. Delete and recreate this annotation from the correct geometry.", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        }
        else if (SelectedView is { } v)
        {
            PropertyTitle.Text = "View properties"; TextField("name", "Name", v.Name); NumberField("scale", "Scale", v.Scale, .001, 100);
            NumberField("x", "Center X (paper mm)", v.X); NumberField("y", "Center Y (paper mm)", v.Y);
            CheckField("hidden", "Show hidden lines", v.HiddenLines); CheckField("aligned", "Align projected view with parent", v.Aligned);
            if (v.Kind == "section") { CheckField("vertical", "Vertical cutting line", v.CutVertical); NumberField("cut", "Cut coordinate (model mm)", v.CutOffset); }
            if (v.Kind == "detail") { NumberField("cx", "Center X (view model mm)", v.DetailCenter.X); NumberField("cy", "Center Y (view model mm)", v.DetailCenter.Y); NumberField("radius", "Detail radius (model mm)", v.DetailRadius, .1); NumberField("ratio", "Magnification", v.DetailRatio, .1, 100); }
            PropertyFields.Children.Add(new TextBlock { Text = $"{v.Sources.Count} source body/bodies. Geometry updates are explicit.", TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 11 });
        }
        else
        {
            var s = Sheet; PropertyTitle.Text = "Sheet properties";
            TextField("name", "Sheet name", s.Name); ChoiceField("format", "Sheet size", SheetFormat.All.Keys.ToArray(), s.Format); CheckField("portrait", "Portrait orientation", s.Portrait);
            ChoiceField("units", "Dimension units", ["mm", "in"], s.Units); CheckField("third", "Third-angle projection", s.ThirdAngle); NumberField("scale", "Default view scale", s.Scale, .001, 100);
            TextField("title", "Drawing title", s.Title); TextField("number", "Part number", s.Number); TextField("material", "Material", s.Material);
            TextField("drawn", "Drawn by", s.DrawnBy); TextField("company", "Company", s.Company); TextField("revision", "Revision", s.Revision);
            TextField("date", "Date", s.Date); TextField("notes", "Notes", s.Notes, true); TextField("general", "General tolerance (blank = none)", s.GeneralTolerance);
            CheckField("parts", "Show parts list", s.ShowParts);
            for (int i = 0; i < s.Parts.Count; i++) { TextField("part" + i, $"Item {i + 1} — name (quantity 1)", s.Parts[i].Name); TextField("mat" + i, "Material", s.Parts[i].Material); }
        }
    }
    void OnApply(object? sender, RoutedEventArgs e) => Guard(() =>
    {
        if (Sheet == null) return;
        // Read fields before EditDrawing raises a refresh and replaces the controls.
        if (_viewAction is { } action)
        {
            var parent = action is "projected" or "section" or "detail" ? NeedView() : null;
            string orientation = action == "iso" ? "iso" : action is "section" or "detail" ? parent!.Orientation : action == "projected" ? "projected:" + Choice("orientation") : Choice("orientation");
            var sources = parent?.Sources ?? SourceList.SelectedItems?.OfType<BodyChoice>().Select(c => c.Id).ToList() ?? [];
            if (sources.Count == 0) throw new InvalidOperationException("Choose bodies in the model list before preparing a base view.");
            _placing = Session!.GenerateDrawingView(Sheet, orientation, sources, parent, action is "section" or "detail" ? action : "base",
                action != "section" || Check("vertical"), action == "section" ? Number("cut") : null,
                action == "detail" ? new SheetPoint(Number("cx"), Number("cy")) : null, action == "detail" ? Number("radius") : 10, action == "detail" ? Number("ratio") : 2);
            _placing.Scale = action == "detail" ? parent!.Scale * Number("ratio") : Number("scale");
            _viewAction = null; Paper.Tool = "place"; Paper.PreviewPlacement(_placing); Instruction.Text = "Click the sheet to place the prepared view. Esc cancels."; Paper.Focus(); return;
        }
        if (SelectedAnnotation is { } a)
        {
            string text = Txt("text"), tol = Txt("tolerance"), symbol = a.Type == "gdt" ? Choice("symbol") : a.Symbol, datums = a.Type == "gdt" ? Txt("datums") : a.Datums;
            double x = Number("x"), y = Number("y"); int precision = (int)Number("precision");
            Edit("Edit drawing annotation", s => { var item = s.Annotations.Single(n => n.Id == a.Id); item.Text = text; item.Tolerance = tol; item.Precision = precision; item.Label = new(x, y); item.Symbol = symbol; item.Datums = datums; });
        }
        else if (SelectedView is { } v)
        {
            var edited = DrawingDocument.Clone(v); edited.Name = Txt("name"); edited.Scale = Number("scale"); edited.X = Number("x"); edited.Y = Number("y"); edited.HiddenLines = Check("hidden"); edited.Aligned = Check("aligned");
            if (v.Kind == "section") { edited.CutVertical = Check("vertical"); edited.CutOffset = Number("cut"); }
            if (v.Kind == "detail") { edited.DetailCenter = new(Number("cx"), Number("cy")); edited.DetailRadius = Number("radius"); edited.DetailRatio = Number("ratio"); edited.Scale = Sheet.Views.Single(p => p.Id == edited.ParentId).Scale * edited.DetailRatio; }
            if (edited.Name.Length == 0) throw new InvalidOperationException("Enter a view name.");
            if (edited.Kind != "base")
            {
                var geometry = Session!.GenerateDrawingView(Sheet, edited.Orientation, edited.Sources, Sheet.Views.Single(p => p.Id == edited.ParentId), edited.Kind, edited.CutVertical, edited.CutOffset, edited.DetailCenter, edited.DetailRadius, edited.DetailRatio);
                edited.Strokes = geometry.Strokes; edited.HatchLoops = geometry.HatchLoops; edited.Direction = geometry.Direction; edited.XDirection = geometry.XDirection;
                edited.MinX = geometry.MinX; edited.MinY = geometry.MinY; edited.MaxX = geometry.MaxX; edited.MaxY = geometry.MaxY; edited.Unresolved = false;
            }
            Edit("Edit drawing view", s => { s.Views[s.Views.FindIndex(n => n.Id == v.Id)] = edited; s.SetViewScale(edited.Id, edited.Scale); s.MoveView(edited.Id, 0, 0);
                var signatures = edited.Strokes.Select(st => st.Signature).ToHashSet(); foreach (var an in s.Annotations.Where(an => an.ViewId == edited.Id)) an.Unresolved = new[] { an.RefA, an.RefB, an.RefC }.Any(r => r != null && !signatures.Contains(r)); });
        }
        else
        {
            var edited = DrawingDocument.Clone(Sheet); edited.Name = Txt("name"); edited.Format = Choice("format"); edited.Portrait = Check("portrait"); edited.Units = Choice("units"); edited.SetProjection(Check("third")); edited.Scale = Number("scale");
            edited.Title = Txt("title"); edited.Number = Txt("number"); edited.Material = Txt("material"); edited.DrawnBy = Txt("drawn"); edited.Company = Txt("company"); edited.Revision = Txt("revision"); edited.Date = Txt("date"); edited.Notes = Txt("notes"); edited.GeneralTolerance = Txt("general"); edited.ShowParts = Check("parts");
            for (int i = 0; i < edited.Parts.Count; i++) { edited.Parts[i].Name = Txt("part" + i); edited.Parts[i].Material = Txt("mat" + i); edited.Parts[i].Quantity = 1; }
            if (edited.Name.Length == 0 || Session!.Drawings.Sheets.Any(s => s.Id != edited.Id && s.Name == edited.Name)) throw new InvalidOperationException("Enter a unique sheet name.");
            Session.EditDrawing("Edit drawing sheet", d => d.Sheets[d.Sheets.FindIndex(s => s.Id == edited.Id)] = edited);
        }
    });
    void Suggest(string category)
    {
        var v = NeedView(); if (Session!.DrawingViewIsStale(v)) throw new InvalidOperationException("Update this drawing before generating suggestions.");
        _suggestions = DrawingSuggestions.Create(Sheet!, v, category); Refresh();
        Instruction.Text = _suggestions.Count == 0 ? "No new suggestions in this category." : "Suggestions are ready for review. Select the ones to keep or accept all.";
    }
    void Accept(IEnumerable<DraftAnnotation> selected)
    {
        var accepted = selected.ToList(); if (accepted.Count == 0) return;
        if (Sheet == null) return;
        Session!.AcceptDrawingSuggestions(Sheet.Id, accepted);
        var ids = accepted.Select(a => a.Id).ToHashSet(); _suggestions.RemoveAll(a => ids.Contains(a.Id)); Refresh();
    }
    void OnAcceptSelected(object? sender, RoutedEventArgs e) => Guard(() => Accept(SuggestionList.SelectedItems?.OfType<DraftAnnotation>() ?? []));
    void OnAcceptAll(object? sender, RoutedEventArgs e) => Guard(() => Accept(_suggestions));
    void OnDismissSuggestions(object? sender, RoutedEventArgs e) { _suggestions.Clear(); Refresh(); }
    async void Export(string extension, bool all)
    {
        try
        {
            if (Sheet == null || Session == null) throw new InvalidOperationException("Create a sheet first.");
            var sheets = (all ? Session.Drawings.Sheets : [Sheet]).Select(DrawingDocument.Clone).ToList();
            var provider = TopLevel.GetTopLevel(this)?.StorageProvider ?? throw new InvalidOperationException("File storage is unavailable.");
            var file = await provider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Export drawing", SuggestedFileName = Sheet.Name + "." + extension, DefaultExtension = extension, FileTypeChoices = [new FilePickerFileType(extension.ToUpperInvariant()) { Patterns = ["*." + extension] }] });
            if (file == null) return;
            var pages = sheets.Select(s => (Sheet: s, Composition: DrawingSheet.ComposeDraft(s))).ToList();
            int warnings = pages.Sum(p => p.Composition.Warnings.Count) + sheets.SelectMany(s => s.Views).Count(Session.DrawingViewIsStale);
            byte[] bytes = extension == "pdf" ? DrawingOutput.PdfPages(pages.Select(p => ((IReadOnlyList<Prim>)p.Composition.Primitives, p.Sheet.Width, p.Sheet.Height)).ToList())
                : System.Text.Encoding.UTF8.GetBytes(extension == "svg" ? DrawingOutput.Svg(pages[0].Composition.Primitives, sheets[0].Width, sheets[0].Height) : DrawingOutput.Dxf(pages[0].Composition.Primitives, sheets[0].Width, sheets[0].Height));
            await using var stream = await file.OpenWriteAsync(); stream.SetLength(0); await stream.WriteAsync(bytes);
            Instruction.Text = $"Exported {file.Name}. {warnings} layout/reference warning(s); review before manufacturing."; Session.Say(Instruction.Text);
        }
        catch (Exception ex) { Instruction.Text = ex.Message; Session?.Say(ex.Message, true); }
    }
}
