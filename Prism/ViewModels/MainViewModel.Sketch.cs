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
public sealed class ToolOptionItem
{
    public int Index { get; init; }
    public string Label { get; init; } = "";
    public string Tip { get; init; } = "";
    public string Icon { get; init; } = "";
    public bool IsSelected { get; init; }
}

/// <summary>One control in a sketch tool's panel (the Offset tool's distance, its Apply button...).</summary>
public sealed partial class ToolControlRow : ObservableObject
{
    public string Id { get; init; } = "";
    public ToolControlKind Kind { get; init; }
    public string Label { get; init; } = "";
    public string Text { get; init; } = "";
    public string Unit { get; init; } = "";
    public bool Primary { get; init; }
    public bool Enabled { get; init; } = true;
    public bool IsInfo => Kind == ToolControlKind.Info;
    public bool IsToggle => Kind == ToolControlKind.Toggle;
    public bool IsNumber => Kind == ToolControlKind.Number;
    public bool IsButton => Kind == ToolControlKind.Button;
    public bool HasText => Text.Length > 0;
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

    void RefreshToolPanel(SketchTool? t)
    {
        ToolPanel.Clear();
        var controls = t?.Panel;
        HasToolPanel = controls is { Count: > 0 };
        if (t == null || controls == null) return;
        ToolPanelTitle = t.PanelTitle; ToolPanelHint = t.Prompt;
        foreach (var c in controls)
        {
            if(c.Kind==ToolControlKind.Info&&c.Label==t.Prompt)continue;
            var row = new ToolControlRow { Id = c.Id, Kind = c.Kind, Label = c.Label, Text = c.Text, Unit = c.Unit, Primary = c.Primary, Enabled = c.Enabled, On = c.On, NumberText = double.IsFinite(c.Number) ? c.Number.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) : "" };
            if (c.Kind == ToolControlKind.Toggle) row.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ToolControlRow.On)) SetToolControl(row.Id, row.On); };
            ToolPanel.Add(row);
        }
    }

    public void SetToolControl(string id, object? value) => Session.Sketch?.Tool?.PanelSet(Session, id, value);

    void RefreshToolOptions()
    {
        RefreshToolPanel(Session.Sketch?.Tool);
        ToolOptions.Clear();
        var t = Session.Sketch?.Tool;
        var opts = t?.Options;
        HasToolOptions = opts is { Count: > 0 };
        HasStepper = t?.StepperLabel != null;
        if (t == null || !HasToolOptions) return;
        ToolOptionsTitle = t.Name.ToUpperInvariant();
        for (int i = 0; i < opts!.Count; i++)
            ToolOptions.Add(new ToolOptionItem { Index = i, Label = opts[i].Label, Tip = opts[i].Tip, Icon = opts[i].Icon, IsSelected = i == t.OptionIndex });
        if (HasStepper) { StepperLabel = t.StepperLabel!; StepperText = t.StepperValue.ToString(); }
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
