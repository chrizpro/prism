using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Prism.Core;
using Prism.Core.Sketching;

namespace Prism.ViewModels;

/// <summary>One line of the sketch panel: a constraint or a dimension, with its geometry named.</summary>
public sealed class ConstraintRow
{
    public int Id { get; init; }
    public string Glyph { get; init; } = "";
    public string Title { get; init; } = "";
    public string Detail { get; init; } = "";
    public string Value { get; init; } = "";
    public bool IsDimension { get; init; }
    public bool HasValue => Value.Length > 0;
    /// <summary>A folder row for a shape (rectangle, slot, polygon): its own constraints sit underneath when it is open.</summary>
    public bool IsHeader { get; init; }
    public bool IsDetail => !IsHeader;
    public string GroupName { get; init; } = "";
    public Avalonia.Thickness Indent { get; init; }
}

/// <summary>One variant of the active drawing tool, shown in the viewport options bar.</summary>
public sealed partial class ToolOptionItem : ObservableObject
{
    public int Index { get; init; }
    public string Label { get; init; } = "";
    public string Tip { get; init; } = "";
    public string Icon { get; init; } = "";
    [ObservableProperty] public partial bool IsSelected { get; set; }
}

/// <summary>One control in a sketch tool's panel (the Offset tool's distance, its Apply button...).</summary>
public sealed partial class ToolControlRow : ObservableObject
{
    public string Id { get; init; } = "";
    public ToolControlKind Kind { get; init; }
    [ObservableProperty] public partial string Label { get; set; } = "";
    [ObservableProperty] public partial string Text { get; set; } = "";
    [ObservableProperty] public partial string Unit { get; set; } = "";
    [ObservableProperty] public partial bool Primary { get; set; } = false;
    [ObservableProperty] public partial bool Enabled { get; set; } = true;
    public bool IsInfo => Kind == ToolControlKind.Info;
    public bool IsToggle => Kind == ToolControlKind.Toggle;
    public bool IsNumber => Kind == ToolControlKind.Number;
    public bool IsButton => Kind == ToolControlKind.Button;
    public bool HasText => Text.Length > 0;
    public SketchTool? Owner { get; init; }
    public bool IsEditing { get; set; }
    [ObservableProperty] public partial string Error { get; set; } = "";
    public bool HasError => Error.Length > 0;
    [ObservableProperty] public partial bool BlockedByValidation { get; set; }
    public bool CanRun => Enabled && !BlockedByValidation;
    partial void OnEnabledChanged(bool value) => OnPropertyChanged(nameof(CanRun));
    partial void OnBlockedByValidationChanged(bool value) => OnPropertyChanged(nameof(CanRun));
    partial void OnErrorChanged(string value) => OnPropertyChanged(nameof(HasError));
    partial void OnTextChanged(string value) => OnPropertyChanged(nameof(HasText));
    [ObservableProperty] public partial bool On { get; set; }
    [ObservableProperty] public partial string NumberText { get; set; } = "";
}

/// <summary>Sketch-mode state shown in the UI: solver status, constraint list and the value-entry prompt.</summary>
public sealed partial class MainViewModel
{
    public ObservableCollection<ConstraintRow> SketchConstraints { get; } = [];
    [ObservableProperty] public partial bool ShowSketchPanel { get; set; }
    [ObservableProperty] public partial bool ShowProperties { get; set; } = true;
    [ObservableProperty] public partial string SketchSummary { get; set; } = "";
    [ObservableProperty] public partial string SketchStateText { get; set; } = "";
    [ObservableProperty] public partial bool SketchFull { get; set; }
    [ObservableProperty] public partial bool SketchBad { get; set; }
    [ObservableProperty] public partial bool SketchFree { get; set; }

    [ObservableProperty] public partial bool InputOpen { get; set; }
    [ObservableProperty] public partial string InputPrompt { get; set; } = "";
    [ObservableProperty] public partial string InputText { get; set; } = "";
    CadSession.InputRequest? _input;

    public ObservableCollection<ToolOptionItem> ToolOptions { get; } = [];
    [ObservableProperty] public partial bool HasToolOptions { get; set; }
    [ObservableProperty] public partial string ToolOptionsTitle { get; set; } = "";
    [ObservableProperty] public partial bool HasStepper { get; set; }
    [ObservableProperty] public partial string StepperLabel { get; set; } = "";
    [ObservableProperty] public partial string StepperText { get; set; } = "";

    public ObservableCollection<ToolControlRow> ToolPanel { get; } = [];
    [ObservableProperty] public partial bool HasToolPanel { get; set; }
    [ObservableProperty] public partial string ToolPanelTitle { get; set; } = "";
    [ObservableProperty] public partial string ToolPanelHint { get; set; } = "";

    public ObservableCollection<SketchToolSection> ToolSections { get; } = [];
    SketchTool? _presentedTool;
    bool _syncingToolPanel;

    void RefreshToolPanel(SketchTool? tool)
    {
        if (!ReferenceEquals(tool, _presentedTool)) { ToolPanel.Clear(); ToolSections.Clear(); ToolOptions.Clear(); _presentedTool = tool; }
        HasToolPanel = tool != null;
        ToolPanelTitle = tool?.PanelTitle ?? "Sketch"; ToolPanelHint = tool?.Prompt ?? "";
        if (tool == null) return;
        var controls = (tool.Panel ?? []).Where(c => !(c.Kind == ToolControlKind.Info && c.Label == tool.Prompt)).ToList();
        var wanted = new List<ToolControlRow>();
        foreach (var c in controls)
        {
            var row = ToolPanel.FirstOrDefault(r => r.Id == c.Id && r.Kind == c.Kind);
            if (row == null)
            {
                row = new ToolControlRow { Id = c.Id, Kind = c.Kind, Owner = tool };
                var boundRow = row;
                row.PropertyChanged += (_, e) =>
                {
                    if (!_syncingToolPanel && e.PropertyName == nameof(ToolControlRow.On) && boundRow.IsToggle && ReferenceEquals(Session.Sketch?.Tool, boundRow.Owner)) SetToolControl(boundRow.Id, boundRow.On);
                    if (!_syncingToolPanel && e.PropertyName == nameof(ToolControlRow.HasError)) RefreshToolActionState();
                };
            }
            row.Label = c.Label; row.Text = c.Text; row.Unit = c.Unit.Trim() == "degrees" ? "°" : c.Unit.Trim();
            row.Primary = c.Primary; row.Enabled = c.Enabled; row.On = c.On;
            if (!row.IsEditing && !row.HasError) row.NumberText = double.IsFinite(c.Number) ? c.Number.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) : "";
            wanted.Add(row);
        }
        SyncCollection(ToolPanel, wanted);
        RefreshToolActionState();
        var groups = new List<(string title, List<ToolControlRow> rows)> { (tool.PanelInputTitle, []) };
        for (int i = 0; i < controls.Count; i++)
        {
            string title = tool.SectionFor(controls[i]);
            int group = groups.FindIndex(g => g.title == title);
            if (group < 0) { groups.Add((title, [])); group = groups.Count - 1; }
            groups[group].rows.Add(wanted[i]);
        }
        groups = groups.OrderBy(g => g.title == tool.PanelInputTitle ? 0 : g.title == tool.PanelSettingsTitle ? 1 : g.title == tool.PanelActionTitle ? 3 : 2).ToList();
        var sections = new List<SketchToolSection>();
        if (ToolOptions.Count > 0)
        {
            var method = ToolSections.FirstOrDefault(g => g.Title == "Method") ?? new SketchToolSection("Method");
            SyncCollection(method.Options, ToolOptions); sections.Add(method);
        }
        foreach (var group in groups)
        {
            var section = ToolSections.FirstOrDefault(g => g.Title == group.title) ?? new SketchToolSection(group.title);
            section.Guidance = group.title == tool.PanelInputTitle ? tool.Prompt : "";
            SyncCollection(section.Controls, group.rows); sections.Add(section);
        }
        for (int i = 0; i < sections.Count; i++) sections[i].Number = (i + 1).ToString();
        SyncCollection(ToolSections, sections);
    }

    static void SyncCollection<T>(ObservableCollection<T> collection, IEnumerable<T> source)
    {
        var wanted = source.ToList();
        for (int i = collection.Count - 1; i >= 0; i--) if (!wanted.Contains(collection[i])) collection.RemoveAt(i);
        for (int i = 0; i < wanted.Count; i++)
        {
            int old = collection.IndexOf(wanted[i]);
            if (old < 0) collection.Insert(i, wanted[i]); else if (old != i) collection.Move(old, i);
        }
    }

    public void SetToolControl(string id, object? value)
    {
        if (id is "place" or "apply" && ToolPanel.Any(r => r.HasError)) { Session.Say("Correct the highlighted values before applying.", true); return; }
        Session.Sketch?.Tool?.PanelSet(Session, id, value);
        RefreshToolOptions();
    }
    void RefreshToolActionState()
    {
        bool invalid = ToolPanel.Any(r => r.HasError);
        foreach (var row in ToolPanel.Where(r => r.IsButton)) row.BlockedByValidation = invalid && row.Id is "place" or "apply";
    }
    public bool CommitToolNumber(ToolControlRow row)
    {
        if (!ReferenceEquals(row.Owner, Session.Sketch?.Tool)) return false;
        bool parsed = double.TryParse(row.NumberText.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value);
        if (!parsed || !double.IsFinite(value)) { row.Error = "Enter a finite number."; return false; }
        if (value <= 0 && row.Id != "orientation" && !row.Id.StartsWith("draw-")) { row.Error = "Enter a value greater than zero."; return false; }
        row.Error = ""; SetToolControl(row.Id, value); return true;
    }

    void RefreshToolOptions()
    {
        if (_syncingToolPanel) return;
        _syncingToolPanel = true;
        try
        {
            var tool = Session.Sketch?.Tool;
            if (!ReferenceEquals(tool, _presentedTool)) { ToolPanel.Clear(); ToolSections.Clear(); ToolOptions.Clear(); _presentedTool = tool; }
            var options = tool?.Options ?? [];
            HasToolOptions = options.Count > 0; HasStepper = tool?.StepperLabel != null;
            ToolOptionsTitle = tool?.Name ?? "";
            if (ToolOptions.Count != options.Count || ToolOptions.Where((o, i) => o.Label != options[i].Label).Any())
            {
                ToolOptions.Clear();
                for (int i = 0; i < options.Count; i++) ToolOptions.Add(new ToolOptionItem { Index = i, Label = options[i].Label, Tip = options[i].Tip, Icon = options[i].Icon });
            }
            for (int i = 0; i < ToolOptions.Count; i++) ToolOptions[i].IsSelected = i == tool!.OptionIndex;
            if (HasStepper) { StepperLabel = tool!.StepperLabel!; StepperText = tool.StepperValue.ToString(); }
            RefreshToolPanel(tool);
        }
        finally { _syncingToolPanel = false; }
    }

    public void PickToolOption(int index) => Session.Sketch?.Tool?.SetOption(Session, index);
    public void StepTool(int delta) => Session.Sketch?.Tool?.Step(Session, delta);

    public void CommitInput() { var r = _input; _input = null; InputOpen = false; r?.Done(InputText); }
    public void CancelInput() { if (!InputOpen) return; var r = _input; _input = null; InputOpen = false; r?.Done(null); }

    void UpdatePanels()
    {
        SyncRightPanels();
    }

    void RebuildSketchPanel()
    {
        RefreshSketchSpace();
        RebuildBrowser();
        SketchConstraints.Clear();
        var sk = Session.Sketch;
        if (sk == null) { SketchSummary = ""; SketchStateText = ""; SketchFull = SketchBad = SketchFree = false; return; }
        var m = sk.Model; var r = sk.Result;
        string Name(bool pt, int id)
        {
            if (pt) return id == 0 ? "Origin" : $"Point {id}";
            var e = m.EntityOrNull(id);
            return e == null ? $"#{id}" : $"{e.Type} {id}";
        }
        // shapes made by one tool (a rectangle's four lines...) fold into a single row; what is theirs sits underneath
        var groupNames = m.Entities.Where(e => e.Group != null).Select(e => e.Group!).Distinct().ToList();
        var members = groupNames.ToDictionary(g => g, g => m.Entities.Where(e => e.Group == g).ToList());
        var gPoints = groupNames.ToDictionary(g => g, g => members[g].SelectMany(e => new[] { e.A, e.B, e.C }.Concat(e.Pts)).Where(x => x >= 0).ToHashSet());
        string? OwnerOf(SkConstraint c)
        {
            if (c.IsDimension) return null;                                             // dimensions stay in view: they are the numbers you care about
            var ip = SketchModel.RefIsPoint(c.Type, c.Refs.Length);
            foreach (var g in groupNames)
                if (c.Refs.Select((id, i) => ip[i] ? gPoints[g].Contains(id) : members[g].Any(e => e.Id == id)).All(x => x)) return g;
            return null;
        }
        var owned = new Dictionary<string, List<SkConstraint>>();
        var loose = new List<SkConstraint>();
        foreach (var c in m.Constraints.Where(c => !c.Hidden))
        {
            var g = OwnerOf(c);
            if (g == null) loose.Add(c); else { if (!owned.TryGetValue(g, out var l)) owned[g] = l = []; l.Add(c); }
        }
        foreach (var g in groupNames)
        {
            bool open = _openGroups.Contains(g);
            var ents = members[g]; int nc = owned.GetValueOrDefault(g)?.Count ?? 0;
            var parts = new List<string>();
            int lines = ents.Count(e => e.Type == EntType.Line), arcs = ents.Count(e => e.Type == EntType.Arc), circles = ents.Count(e => e.Type == EntType.Circle);
            if (lines > 0) parts.Add($"{lines} line{(lines == 1 ? "" : "s")}"); if (arcs > 0) parts.Add($"{arcs} arc{(arcs == 1 ? "" : "s")}"); if (circles > 0) parts.Add($"{circles} circle{(circles == 1 ? "" : "s")}");
            parts.Add($"{nc} constraint{(nc == 1 ? "" : "s")}");
            SketchConstraints.Add(new ConstraintRow { Id = -1, IsHeader = true, GroupName = g, Glyph = open ? "▾" : "▸", Title = g, Detail = string.Join(" · ", parts) });
            if (open && owned.TryGetValue(g, out var rows))
                foreach (var c in rows) SketchConstraints.Add(MakeRow(c, Name, new Avalonia.Thickness(22, 0, 0, 0)));
        }
        foreach (var c in loose) SketchConstraints.Add(MakeRow(c, Name, default));
        int curves = m.Entities.Count(e => !e.Construction), cons = m.Entities.Count(e => e.Construction);
        SketchSummary = $"Plane {sk.ActivePlane + 1} of {sk.DrawingPlanes.Count} · {curves} curve{(curves == 1 ? "" : "s")}" + (cons > 0 ? $" · {cons} construction" : "") + $" · {m.Constraints.Count(c => !c.Hidden && !c.IsDimension)} constraints · {m.Constraints.Count(c => c.IsDimension)} dimensions";
        SketchStateText = m.Entities.Count == 0 ? "Ready to draw" : r.Status;
        SketchBad = !r.Converged; SketchFull = m.Entities.Count > 0 && r.FullyConstrained; SketchFree = r.Converged && !SketchFull;
    }

    readonly HashSet<string> _openGroups = [];

    static ConstraintRow MakeRow(SkConstraint c, Func<bool, int, string> name, Avalonia.Thickness indent)
    {
        {
            var ip = SketchModel.RefIsPoint(c.Type, c.Refs.Length);
            string value = c.IsDimension ? (c.Type == CType.Angle ? $"{Math.Abs(c.Value):0.##}°" : c.Type is CType.Radius ? $"R {c.Value:0.##}" : c.Type is CType.Diameter ? $"Ø {c.Value:0.##}" : $"{Math.Abs(c.Value):0.##} mm") : "";
            return new ConstraintRow
            {
                Id = c.Id, IsDimension = c.IsDimension, Glyph = c.IsDimension ? "↔" : SketchModel.Glyph(c.Type), Title = SketchModel.Describe(c.Type), Indent = indent,
                Detail = string.Join(" · ", c.Refs.Select((id, i) => name(ip[i], id))), Value = value,
            };
        }
    }

    /// <summary>Open or close a shape's row, and light up its curves in the sketch.</summary>
    public void ToggleSketchGroup(string group)
    {
        var sk = Session.Sketch; if (sk == null) return;
        if (!_openGroups.Remove(group)) _openGroups.Add(group);
        sk.SelEntities.Clear(); sk.SelPoints.Clear();
        foreach (var e in sk.Model.Entities.Where(e => e.Group == group)) sk.SelEntities.Add(e.Id);
        RebuildSketchPanel(); Session.Invalidate();
    }

    public void DeleteConstraint(ConstraintRow row)
    {
        var sk = Session.Sketch; if (sk == null) return;
        var c = sk.Model.Constraints.FirstOrDefault(x => x.Id == row.Id); if (c == null) return;
        sk.Edit(Session, "Removing that", () => sk.Model.Remove(sk.Model.Constraints.First(x => x.Id == row.Id)));
    }

    public void EditConstraintValue(ConstraintRow row)
    {
        var sk = Session.Sketch; if (sk == null || !row.IsDimension) return;
        var c = sk.Model.Constraints.FirstOrDefault(x => x.Id == row.Id); if (c != null) sk.EditDimension(Session, c);
    }
}
