using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Prism.Commands;
using Prism.Core;
using Prism.Core.Sketching;
using Prism.Kernel;

namespace Prism.ViewModels;

public sealed record StudioIssue(string Title, string Detail, bool IsError, Guid? ObjectId = null, HistoryNode? Feature = null, List<SelItem>? Inputs = null)
{
    public string Severity => IsError ? "Error" : "Warning";
    public string Symbol => IsError ? "!" : "△";
    public string DisplayTitle => IsError && Feature != null && Inputs?.Count > 0 ? Title + " could not rebuild" : Title;
    public Avalonia.Media.IBrush IndicatorBrush => new Avalonia.Media.SolidColorBrush(IsError ? 0xFFF75F64u : 0xFFEEB75Au);
    public bool CanLocate => ObjectId.HasValue;
    public bool CanEdit => Feature?.Recipe != null || ObjectId.HasValue;
}
public sealed partial class ReferenceRow : ObservableObject
{
    public Guid Id { get; init; }
    public int OriginIndex { get; init; } = -1;
    public string Name { get; init; } = "";
    public string Kind { get; init; } = "";
    public string Icon { get; init; } = "origin-plane";
    [ObservableProperty] public partial bool Visible { get; set; }
    public Action<bool>? VisibilityChanged { get; set; }
    partial void OnVisibleChanged(bool value) => VisibilityChanged?.Invoke(value);
}
public sealed record FeatureChoice(HistoryNode Node)
{
    public string Title => $"{Node.Label} · step {Node.Id}";
}
public sealed partial class ParameterDraft : ObservableObject
{
    public string Name { get; init; } = "";
    [ObservableProperty] public partial string Formula { get; set; } = "";
    [ObservableProperty] public partial bool Pinned { get; set; }
    [ObservableProperty] public partial string Result { get; set; } = "";
}
/// <summary>One dimension of a sketch in the Parameters panel. Formulas here see the sketch's own named dimensions, document parameters and earlier sketches (sketch1.width).</summary>
public sealed partial class SketchDimDraft : ObservableObject
{
    public Guid ObjectId { get; init; }
    public int Section { get; init; }
    public int ConstraintId { get; init; }
    public string SketchName { get; init; } = "";
    public string SketchKey { get; init; } = "";
    public string Kind { get; init; } = "";
    public string Unit { get; init; } = "";
    public bool ShowHeader { get; init; }
    public string Header => $"{SketchName}  ·  {SketchKey}.*";
    public string OriginalName { get; init; } = "";
    public string OriginalFormula { get; init; } = "";
    [ObservableProperty] public partial string Name { get; set; } = "";
    [ObservableProperty] public partial string Formula { get; set; } = "";
    [ObservableProperty] public partial string Result { get; set; } = "";
    /// <summary>How other formulas refer to this dimension.</summary>
    public string Reference => string.IsNullOrWhiteSpace(Name) ? "unnamed: give it a name to use it elsewhere" : $"{SketchKey}.{Name.Trim()}";
    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(Reference));
    public bool Changed => (Name ?? "").Trim() != OriginalName || (Formula ?? "").Trim() != OriginalFormula;
}
public sealed partial class FeatureValueRow : ObservableObject
{
    public int Index { get; init; }
    public string Label { get; init; } = "";
    public string Unit { get; init; } = "";
    [ObservableProperty] public partial string Formula { get; set; } = "";
}

public sealed partial class MainViewModel
{
    [ObservableProperty] public partial string? StudioPanel { get; set; }
    [ObservableProperty] public partial bool ShowDiagnosticsStudio { get; set; }
    [ObservableProperty] public partial bool ShowReferencesStudio { get; set; }
    [ObservableProperty] public partial bool ShowParametersStudio { get; set; }
    [ObservableProperty] public partial string StudioMessage { get; set; } = "";
    public bool AnyStudioVisible => ShowDiagnosticsStudio || ShowReferencesStudio || ShowParametersStudio;
    public bool ShowScriptEditor => RightPanelContentId == "script";
    bool _refreshingStudios;
    CadSession? _studioSession;
    object? _parameterSnapshot;

    partial void OnStudioPanelChanged(string? value) { OnPropertyChanged(nameof(StudioTitle)); OnPropertyChanged(nameof(StudioIcon)); UpdatePanels(); RefreshStudios(); }
    public string StudioTitle => StudioPanel switch { "diagnostics" => "Diagnostics", "references" => "References", "parameters" => "Parameters", _ => "Studio" };
    public void OpenStudio(string? panel)
    {
        StudioMessage = "";
        if (string.IsNullOrEmpty(panel)) CloseRightPanel();
        else OpenRightPanel(panel);
        RefreshStudios();
    }

    public ObservableCollection<StudioIssue> DiagnosticIssues { get; } = [];
    public ObservableCollection<string> DiagnosticPassed { get; } = [];
    public ObservableCollection<string> StudioActivity { get; } = [];
    [ObservableProperty] public partial string DiagnosticSummary { get; set; } = "Run checks to inspect the current document.";
    [ObservableProperty] public partial bool DiagnosticsStale { get; set; } = true;
    [ObservableProperty] public partial StudioIssue? SelectedStudioIssue { get; set; }

    public void InvalidateStudios()
    {
        DiagnosticsStale = true;
        RefreshStudios();
    }
    public void RefreshStudioActivity()
    {
        if (StudioPanel != "diagnostics") return;
        StudioActivity.Clear();
        foreach (var log in Session.Log.TakeLast(30).Reverse())
            StudioActivity.Add($"{log.Time:HH:mm:ss}  {(log.IsError ? "Error: " : "")}{log.Message}");
    }
    public void RunStudioChecks()
    {
        DiagnosticIssues.Clear(); DiagnosticPassed.Clear();
        int valid = 0, sketches = 0;
        foreach (var obj in Session.Objects)
        {
            try
            {
                if (obj.SketchData != null)
                {
                    var result = SketchSolver.Solve(obj.SketchData.Clone()); sketches++;
                    if (!result.Converged || result.Dof > 0 || result.Redundant)
                        DiagnosticIssues.Add(new(obj.Name, result.Status, !result.Converged, obj.Id));
                }
                else if (!obj.IsAnnotation && obj.HasBrep && obj.Form == null)
                {
                    if (obj.Shape.IsValid) valid++;
                    else DiagnosticIssues.Add(new(obj.Name, "Invalid B-rep geometry. Locate the object and inspect its creating feature.", true, obj.Id, Session.FeatureNodeFor(obj.Id)));
                }
            }
            catch (Exception e) when (e is not OutOfMemoryException) { DiagnosticIssues.Add(new(obj.Name, e.Message, true, obj.Id)); }
        }
        foreach (var step in Session.HistoryPath().Past.Where(n => n.Recipe != null && n.Parent != null))
        {
            var before = step.Parent!.After.Select(o => o.Id).ToHashSet();
            if (step.Recipe!.Slots.SelectMany(x => x).Any(p => p.Obj != Guid.Empty && !before.Contains(p.Obj)))
                DiagnosticIssues.Add(new(step.Label, "A captured feature input is missing from its source state.", true, Feature: step));
        }
        if (Session.RebuildIssue is { } failure && failure.At == Session.Current)
            DiagnosticIssues.Add(new(failure.FailedStep.Label, failure.Reason, true, failure.Inputs?.Where(i => i.Obj != Guid.Empty).Select(i => (Guid?)i.Obj).FirstOrDefault(), failure.FailedStep, failure.Inputs));
        if (Session.ActiveCommand is { Error: { } error } command)
            DiagnosticIssues.Add(new(command.Title, error, true));
        DiagnosticPassed.Add($"{valid} B-rep objects passed validity checks");
        DiagnosticPassed.Add($"{sketches} sketch definitions evaluated on copies");
        DiagnosticSummary = $"{DiagnosticIssues.Count(i => i.IsError)} errors · {DiagnosticIssues.Count(i => !i.IsError)} warnings";
        UpdateDiagnosticPresentation();
        var selectedIssue = VisibleDiagnosticIssues.FirstOrDefault();
        bool sameIssue = Equals(SelectedStudioIssue, selectedIssue);
        SelectedStudioIssue = selectedIssue;
        if (sameIssue) OnSelectedStudioIssueChanged(selectedIssue);
        DiagnosticsStale = false; RefreshStudioActivity();
    }
    public void LocateStudioIssue(StudioIssue issue)
    {
        if (issue.ObjectId is { } id && Session.Find(id) is { } obj)
        { Session.SelectItems([SelItem.Of(obj)]); Session.ZoomToObjects([id]); }
    }
    public void EditStudioIssue(StudioIssue issue)
    {
        if (Session.RebuildIssue is { } failure && failure.At == Session.Current && failure.FailedStep == issue.Feature && failure.AttemptedRecipe != null)
        { Session.EditFailedFeature(); return; }
        if (issue.Feature is { } node && node.Recipe != null) Session.EditFeature(node);
        else if (issue.ObjectId is { } id && Session.Find(id) is { } obj)
        {
            if (obj.SketchData != null) Session.EditSketch(obj);
            else if (Session.FeatureNodeFor(id) is { } feature) Session.EditFeature(feature);
            else Session.Say("This object has no editable feature definition. Use Locate to inspect it.", true);
        }
    }

    public ObservableCollection<ReferenceRow> StudioReferences { get; } = [];
    public ObservableCollection<string> ReferenceUsage { get; } = [];
    [ObservableProperty] public partial ReferenceRow? SelectedStudioReference { get; set; }
    [ObservableProperty] public partial string ReferenceSearch { get; set; } = "";
    [ObservableProperty] public partial bool VisibleReferencesOnly { get; set; }
    [ObservableProperty] public partial string ReferenceDetail { get; set; } = "Select a reference to inspect its uses.";
    partial void OnReferenceSearchChanged(string value) => RefreshStudios();
    partial void OnVisibleReferencesOnlyChanged(bool value) => RefreshStudios();
    partial void OnSelectedStudioReferenceChanged(ReferenceRow? value) { DescribeReference(); UpdateReferencePresentation(); }
    void RebuildStudioReferences()
    {
        var old = SelectedStudioReference;
        StudioReferences.Clear();
        bool Match(string name, bool visible) => (!VisibleReferencesOnly || visible) && name.Contains(ReferenceSearch, StringComparison.OrdinalIgnoreCase);
        for (int i = 0; i < CadSession.OriginNames.Length; i++)
        {
            int index = i;
            if (Match(CadSession.OriginNames[i], Session.OriginVisible[i]))
                StudioReferences.Add(new() { OriginIndex = i, Name = CadSession.OriginNames[i], Icon = i < 3 ? "origin-plane" : i < 6 ? "origin-axis" : "origin-point", Kind = "Origin", Visible = Session.OriginVisible[i], VisibilityChanged = on => { Session.SetOriginVisible(index, on); RefreshStudios(); } });
        }
        foreach (var obj in Session.Objects.Where(o => o.Kind is ObjKind.Sketch or ObjKind.Plane or ObjKind.Curve).OrderBy(o => o.Kind == ObjKind.Plane ? 0 : o.Kind == ObjKind.Sketch ? 1 : 2))
        {
            if (!Match(obj.Name, obj.Visible)) continue;
            StudioReferences.Add(new() { Id = obj.Id, Name = obj.Name, Kind = obj.Asm is Core.Assembly.ImageInfo ? "Image" : obj.Kind.ToString(), Icon = obj.Kind == ObjKind.Sketch ? "sketch" : "origin-plane", Visible = obj.Visible, VisibilityChanged = on => ToggleVisible(obj.Id, on) });
        }
        SelectedStudioReference = StudioReferences.FirstOrDefault(r => old != null && r.Id == old.Id && r.OriginIndex == old.OriginIndex);
        DescribeReference(); UpdateReferencePresentation();
    }
    void DescribeReference()
    {
        ReferenceUsage.Clear(); var row = SelectedStudioReference;
        if (row == null) { ReferenceDetail = "Select a reference to inspect its uses."; return; }
        ReferenceDetail = row.Kind + " · " + row.Name;
        if (row.OriginIndex >= 0) ReferenceDetail += "\nFixed document origin reference";
        else if (Session.Find(row.Id)?.SketchPlane is { } p)
            ReferenceDetail += $"\nOrigin {p.Origin.X:0.###}, {p.Origin.Y:0.###}, {p.Origin.Z:0.###} mm\nNormal {p.Normal.X:0.###}, {p.Normal.Y:0.###}, {p.Normal.Z:0.###}";
        foreach (var node in Session.HistoryPath().Past.Where(n => n.Recipe != null))
            if (node.Recipe!.Slots.SelectMany(s => s).Any(p => row.OriginIndex >= 0 ? p.Kind == SelKind.Origin && p.Index == row.OriginIndex : p.Obj == row.Id))
                ReferenceUsage.Add($"{node.Label} · step {node.Id}");
        if (ReferenceUsage.Count == 0) ReferenceUsage.Add("No captured feature uses on the current branch.");
    }
    public void LocateStudioReference()
    {
        if (SelectedStudioReference is not { } row) return;
        if (row.OriginIndex >= 0) { Session.SetOriginVisible(row.OriginIndex, true); Session.SelectItems([new(Guid.Empty, SelKind.Origin, row.OriginIndex)]); }
        else if (Session.Find(row.Id) is { } obj) { Session.SelectItems([SelItem.Of(obj)]); Session.ZoomToObjects([row.Id]); }
    }
    public void EditStudioReference()
    {
        if (SelectedStudioReference is not { } row || row.OriginIndex >= 0) { Session.Say("Origin references are fixed."); return; }
        if (Session.Find(row.Id) is { SketchData: not null } sketch) Session.EditSketch(sketch);
        else if (Session.FeatureNodeFor(row.Id) is { } feature) Session.EditFeature(feature);
        else Session.Say("This reference has no editable feature definition.", true);
    }

    public ObservableCollection<ParameterDraft> DocumentParameterRows { get; } = [];
    public ObservableCollection<FeatureChoice> StudioFeatures { get; } = [];
    public ObservableCollection<FeatureValueRow> FeatureValueRows { get; } = [];
    public ObservableCollection<SketchDimDraft> SketchDimRows { get; } = [];
    public bool HasSketchDims => SketchDimRows.Count > 0;
    List<(Guid, SketchModel)> _sketchSnapshot = [];
    List<(Guid, SketchModel)> CurrentSketchSnapshot() => Session.Objects.Where(o => o.Kind == ObjKind.Sketch && o.SketchData != null).Select(o => (o.Id, o.SketchData!)).ToList();
    void LoadSketchDimensions()
    {
        _sketchSnapshot = CurrentSketchSnapshot();
        SketchDimRows.Clear();
        foreach (var o in Session.Objects.Where(x => x.Kind == ObjKind.Sketch && x.SketchData != null))
        {
            bool header = true;
            foreach (var (section, c) in SketchParameters.Dimensions(o.SketchData!))
            {
                string value = Math.Abs(c.Value).ToString("0.####", CultureInfo.InvariantCulture);
                SketchDimRows.Add(new SketchDimDraft
                {
                    ObjectId = o.Id, Section = section, ConstraintId = c.Id, SketchName = o.Name, SketchKey = SketchParameters.ScopeKey(o.Name), ShowHeader = header,
                    Kind = SketchModel.Describe(c.Type) + (section > 0 ? $" · plane {section + 1}" : ""), Unit = c.Type == CType.Angle ? "°" : "mm",
                    OriginalName = c.Name ?? "", OriginalFormula = c.Formula ?? value, Name = c.Name ?? "", Formula = c.Formula ?? value, Result = value,
                });
                header = false;
            }
        }
        OnPropertyChanged(nameof(HasSketchDims));
    }
    /// <summary>The dimensions whose name or formula was changed in the panel.</summary>
    List<SketchDimEdit> SketchDimEdits() => SketchDimRows.Where(r => r.Changed).Select(r => new SketchDimEdit(r.ObjectId, r.Section, r.ConstraintId, (r.Name ?? "").Trim(), (r.Formula ?? "").Trim())).ToList();
    /// <summary>Show what every sketch dimension would become; returns the reason when a formula or a sketch does not work.</summary>
    string? PreviewSketchDimensions(IReadOnlyCollection<DocumentParameter> definitions)
    {
        if (SketchDimRows.Count == 0) return null;
        try
        {
            var values = Session.PreviewSketchDimensions(definitions, SketchDimEdits());
            foreach (var r in SketchDimRows) r.Result = values.TryGetValue((r.ObjectId, r.Section, r.ConstraintId), out var v) ? v.ToString("0.###", CultureInfo.InvariantCulture) : "—";
            return null;
        }
        catch (FormatException e) { foreach (var r in SketchDimRows) r.Result = "—"; return e.Message; }
    }
    [ObservableProperty] public partial FeatureChoice? SelectedStudioFeature { get; set; }
    [ObservableProperty] public partial string NewParameterName { get; set; } = "";
    [ObservableProperty] public partial string NewParameterFormula { get; set; } = "";
    partial void OnSelectedStudioFeatureChanged(FeatureChoice? value) => RebuildFeatureValues();
    void LoadDocumentParameters()
    {
        _parameterSnapshot = Session.DocumentParameters;
        SelectedParameterDraft = null;
        DocumentParameterRows.Clear();
        foreach (var p in Session.DocumentParameters) AddParameterDraft(p);
        LoadSketchDimensions();
        PreviewDocumentParameters(StudioPanel == "parameters");
    }
    public void AddStudioParameter()
    {
        if (string.IsNullOrWhiteSpace(NewParameterName)) { StudioMessage = "Enter a parameter name."; return; }
        var proposed = DocumentParameterRows.Select(p => new DocumentParameter(p.Name, p.Formula, p.Pinned))
            .Append(new(NewParameterName.Trim(), NewParameterFormula.Trim())).ToList();
        try { ParameterValues.Evaluate(proposed); }
        catch (FormatException e) { StudioMessage = e.Message; return; }
        AddParameterDraft(new(NewParameterName.Trim(), NewParameterFormula.Trim()));
        NewParameterName = NewParameterFormula = ""; AddingParameter = false; SelectedParameterDraft = DocumentParameterRows.LastOrDefault(); PreviewDocumentParameters();
    }
    void AddParameterDraft(DocumentParameter parameter)
    {
        var row = new ParameterDraft { Name = parameter.Name, Formula = parameter.Formula, Pinned = parameter.Pinned };
        row.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ParameterDraft.Formula))
            { row.Result = "—"; StudioMessage = "Draft changed. Evaluate or apply to update the result."; }
        };
        row.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ParameterDraft.Pinned)) UpdateParameterPresentation(); };
        DocumentParameterRows.Add(row); UpdateParameterPresentation();
    }
    public void PreviewDocumentParameters(bool announce = true)
    {
        try
        {
            var definitions = DocumentParameterRows.Select(p => new DocumentParameter(p.Name, p.Formula, p.Pinned)).ToList();
            var values = ParameterValues.Evaluate(definitions);
            foreach (var p in DocumentParameterRows) p.Result = values[p.Name].ToString("0.###", CultureInfo.InvariantCulture);
            string? sketchProblem = PreviewSketchDimensions(definitions);
            if (announce || sketchProblem != null) StudioMessage = sketchProblem ?? "Expressions valid. Apply to save and rebuild bound features and sketches.";
        }
        catch (FormatException e) { foreach (var p in DocumentParameterRows) p.Result = "—"; StudioMessage = e.Message; }
    }
    public void ApplyStudioParameters()
    {
        bool ok = Session.ApplyDocumentParameters(DocumentParameterRows.Select(p => new DocumentParameter(p.Name, p.Formula, p.Pinned)).ToList(), SketchDimEdits());
        StudioMessage = Session.Status;
        if (ok) { LoadDocumentParameters(); RebuildStudioFeatures(); StudioMessage = Session.Status; }
    }
    void RebuildStudioFeatures()
    {
        var features = Session.HistoryPath().Past.Where(n => n.Recipe != null).ToList();
        if (features.SequenceEqual(StudioFeatures.Select(f => f.Node))) return;
        var old = SelectedStudioFeature?.Node;
        StudioFeatures.Clear();
        foreach (var n in features)
            StudioFeatures.Add(new(n));
        SelectedStudioFeature = StudioFeatures.FirstOrDefault(f => f.Node == old) ?? StudioFeatures.LastOrDefault();
    }
    void RebuildFeatureValues()
    {
        FeatureValueRows.Clear();
        if (SelectedStudioFeature?.Node.Recipe is not { } recipe) return;
        var command = recipe.Make();
        for (int i = 0; i < command.Params.Count && i < recipe.Values.Count; i++)
            if (command.Params[i] is NumberParam number && recipe.Values[i] is double value)
                FeatureValueRows.Add(new() { Index = i, Label = number.Label, Unit = number.Unit, Formula = recipe.Expressions?.GetValueOrDefault(i) ?? value.ToString("R", CultureInfo.InvariantCulture) });
    }
    public void ApplyStudioFeatureValues()
    {
        if (SelectedStudioFeature?.Node is not { Recipe: { } recipe } node) return;
        if (Session.ActiveCommand != null || Session.Sketch != null) { StudioMessage = "Finish the active operation first."; return; }
        var rows = FeatureValueRows.ToList();
        if (rows.Count == 0) { StudioMessage = "Use the full feature editor for this operation's settings."; return; }
        try
        {
            var template = recipe.Make();
            var values = rows.Select(row => (row, value: Session.ParameterValue(row.Formula))).ToList();
            foreach (var (row, value) in values)
                if (template.Params[row.Index] is NumberParam number && (value < number.Min || value > number.Max))
                    throw new FormatException($"{row.Label} is outside the allowed range.");
            if (!Session.EditFeature(node)) { StudioMessage = Session.Status; return; }
            var command = Session.ActiveCommand!;
            command.ConfigureInputs(() =>
            {
                foreach (var (row, value) in values)
                    if (command.Params[row.Index] is NumberParam number) { number.Formula = row.Formula; number.Value = value; }
            });
            command.OnChanged(); Session.CommitCommand();
            if (Session.ActiveCommand != null) { string error = Session.Status; Session.CancelCommand(); Session.Say(error, true); }
            StudioMessage = Session.Status;
        }
        catch (FormatException e) { StudioMessage = e.Message; }
    }

    public void RefreshStudios()
    {
        if (_refreshingStudios) return;
        _refreshingStudios = true;
        try
        {
            if (_studioSession != Session)
            {
                _studioSession = Session; DiagnosticIssues.Clear(); DiagnosticPassed.Clear(); DiagnosticsStale = true;
                SelectedStudioIssue = null; SelectedStudioReference = null; SelectedStudioFeature = null; SelectedParameterDraft = null; LoadDocumentParameters();
            }
            else if (!ReferenceEquals(_parameterSnapshot, Session.DocumentParameters)) LoadDocumentParameters();
            if (StudioPanel == "parameters" && !_sketchSnapshot.SequenceEqual(CurrentSketchSnapshot())) LoadSketchDimensions();
            if (StudioPanel == "references") RebuildStudioReferences();
            if (StudioPanel == "parameters") RebuildStudioFeatures();
            RefreshStudioActivity(); UpdateStudioCounts();
        }
        finally { _refreshingStudios = false; }
    }
    public void RevertStudioParameters() { LoadDocumentParameters(); RebuildFeatureValues(); }
}
