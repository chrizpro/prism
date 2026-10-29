using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using Prism.Core;
using Prism.Kernel;
using Prism.Rendering;
using Prism.Commands;

namespace Prism.ViewModels;

public sealed record StudioField(string Label, string Value);
public sealed partial class ReferenceRow
{
    [ObservableProperty] public partial string? SectionHeader { get; set; }
    [ObservableProperty] public partial bool IsCurrentReference { get; set; }
    public bool HasSectionHeader => !string.IsNullOrEmpty(SectionHeader);
    partial void OnSectionHeaderChanged(string? value) => OnPropertyChanged(nameof(HasSectionHeader));
}

public sealed partial class MainViewModel
{
    public ObservableCollection<StudioIssue> VisibleDiagnosticIssues { get; } = [];
    public ObservableCollection<StudioIssue> OtherDiagnosticIssues { get; } = [];
    public ObservableCollection<StudioField> IssueFields { get; } = [];
    [ObservableProperty] public partial StudioPreviewSource? IssuePreview { get; set; }
    [ObservableProperty] public partial string IssuePreviewCaption { get; set; } = "No geometry available";
    [ObservableProperty] public partial string DiagnosticSearch { get; set; } = "";
    [ObservableProperty] public partial int DiagnosticErrorCount { get; set; }
    [ObservableProperty] public partial int DiagnosticWarningCount { get; set; }
    [ObservableProperty] public partial string LastDiagnosticCheck { get; set; } = "Not checked yet";
    public string StudioIcon => StudioPanel == "diagnostics" ? "curvature" : StudioPanel == "references" ? "origin-plane" : "dimension";
    public bool HasSelectedIssue => SelectedStudioIssue != null;
    public bool HasIssuePreview => IssuePreview != null;
    public bool HasNoIssues => !DiagnosticsStale && DiagnosticIssues.Count == 0;
    public string DiagnosticFreshness => LastDiagnosticCheck == "Not checked yet" ? "Run checks to inspect this document" : "Geometry changed · run checks to refresh";
    public string IssueEditorLabel => SelectedStudioIssue?.Feature != null ? "Edit feature" : Session.Find(SelectedStudioIssue?.ObjectId ?? Guid.Empty)?.SketchData != null ? "Open sketch" : "Open editor";
    public int ReferenceCount => StudioReferences.Count;
    public int ParameterCount => DocumentParameterRows.Count;
    public int FeatureCount => StudioFeatures.Count;
    public string IssueCountLabel => $"Needs attention ({VisibleDiagnosticIssues.Count})";
    partial void OnDiagnosticSearchChanged(string value) => FilterIssues();
    partial void OnDiagnosticsStaleChanged(bool value) { OnPropertyChanged(nameof(HasNoIssues)); OnPropertyChanged(nameof(DiagnosticFreshness)); }
    partial void OnLastDiagnosticCheckChanged(string value) => OnPropertyChanged(nameof(DiagnosticFreshness));
    partial void OnSelectedStudioIssueChanged(StudioIssue? value)
    {
        RefreshOtherIssues();
        OnPropertyChanged(nameof(HasSelectedIssue)); IssueFields.Clear(); IssuePreview = null;
        OnPropertyChanged(nameof(IssueEditorLabel));
        IssuePreviewCaption = "No geometry available for this issue";
        if (value == null) { OnPropertyChanged(nameof(HasIssuePreview)); return; }
        try
        {
            var picks = value.Inputs ?? value.Feature?.Recipe?.Slots.SelectMany(s => s).ToList() ?? [];
            var state = value.Feature?.Parent?.After ?? Session.Objects;
            bool failed = Session.RebuildIssue is { } failure && failure.At == Session.Current && failure.FailedStep == value.Feature;
            if (failed) state = Session.RebuildIssue!.At.After;
            var obj = state.FirstOrDefault(o => o.Id == value.ObjectId) ?? state.FirstOrDefault(o => picks.Any(p => p.Obj == o.Id));
            if (obj != null)
            {
                var edges = picks.Where(p => p.Obj == obj.Id && p.Kind == SelKind.Edge).Select(p => p.Index).ToArray();
                var faces = picks.Where(p => p.Obj == obj.Id && p.Kind == SelKind.Face).Select(p => p.Index).ToArray();
                IssuePreviewCaption = edges.Length > 0 ? "Selected edges · input geometry" : faces.Length > 0 ? "Selected faces · input geometry" : "Affected object · overview";
                IssuePreview = new(obj.Mesh, edges, faces, value.IsError, IssuePreviewCaption);
            }
            IssueFields.Add(new("Feature", value.Feature?.Label ?? value.Title));
            if (value.Feature?.Recipe is { } recipe)
            {
                var command = recipe.Make();
                var first = command.Params.Select((p, i) => (p, i)).FirstOrDefault(x => x.p is NumberParam);
                if (first.p is NumberParam number && recipe.Values[first.i] is double n)
                    IssueFields.Add(new(number.Label, $"{n:0.###} {number.Unit}"));
            }
            IssueFields.Add(new("Branch", Session.HistoryPath().Past.LastOrDefault(n => n.Label.StartsWith("Branch: "))?.Pin ?? "Current design"));
        }
        catch (Exception e) when (e is not OutOfMemoryException) { IssuePreviewCaption = "Input geometry unavailable"; }
        OnPropertyChanged(nameof(HasIssuePreview));
    }
    void UpdateDiagnosticPresentation()
    {
        DiagnosticErrorCount = DiagnosticIssues.Count(i => i.IsError); DiagnosticWarningCount = DiagnosticIssues.Count - DiagnosticErrorCount;
        LastDiagnosticCheck = $"Current branch checked at {DateTime.Now:HH:mm:ss}";
        FilterIssues(); OnPropertyChanged(nameof(HasNoIssues));
    }
    void FilterIssues()
    {
        VisibleDiagnosticIssues.Clear();
        foreach (var issue in DiagnosticIssues.OrderByDescending(i => i.IsError))
            if ((issue.DisplayTitle + " " + issue.Detail).Contains(DiagnosticSearch, StringComparison.OrdinalIgnoreCase)) VisibleDiagnosticIssues.Add(issue);
        if (!VisibleDiagnosticIssues.Contains(SelectedStudioIssue!)) SelectedStudioIssue = VisibleDiagnosticIssues.FirstOrDefault();
        RefreshOtherIssues();
        OnPropertyChanged(nameof(IssueCountLabel));
    }
    void RefreshOtherIssues()
    {
        OtherDiagnosticIssues.Clear();
        foreach (var issue in VisibleDiagnosticIssues.Where(i => i != SelectedStudioIssue)) OtherDiagnosticIssues.Add(issue);
    }

    public ObservableCollection<StudioField> ReferenceFields { get; } = [];
    public ObservableCollection<FeatureChoice> ReferenceUseFeatures { get; } = [];
    [ObservableProperty] public partial StudioPreviewSource? ReferencePreview { get; set; }
    public bool HasReferenceSelection => SelectedStudioReference != null;
    public string ReferenceName => SelectedStudioReference?.Name ?? "Select a reference";
    public bool HasReferenceUses => ReferenceUseFeatures.Count > 0;
    void UpdateReferencePresentation()
    {
        string? lastGroup = null;
        foreach (var row in StudioReferences)
        {
            string group = row.Kind == "Origin" ? "Origin" : row.Kind == "Sketch" ? "Sketches" : row.Kind == "Image" ? "Images" : row.Kind == "Plane" ? "Construction" : "Curves";
            row.SectionHeader = group != lastGroup ? group : null; lastGroup = group;
            row.IsCurrentReference = row == SelectedStudioReference;
        }
        ReferencePreview = null; ReferenceFields.Clear(); ReferenceUseFeatures.Clear();
        if (SelectedStudioReference is { } selected)
        {
            ReferenceFields.Add(new("Type", selected.Kind == "Origin" ? "Origin reference" : selected.Kind));
            var obj = Session.Find(selected.Id);
            var plane = selected.OriginIndex is >= 0 and < 3 ? CadSession.OriginPlane(selected.OriginIndex) : obj?.SketchPlane;
            if (plane is { } p)
            {
                ReferenceFields.Add(new("Origin", $"{p.Origin.X:0.##}, {p.Origin.Y:0.##}, {p.Origin.Z:0.##} mm"));
                ReferenceFields.Add(new("Normal", $"{p.Normal.X:0.##}, {p.Normal.Y:0.##}, {p.Normal.Z:0.##}"));
                if (obj == null) ReferencePreview = new(PlanePreview(p), [0, 1, 2, 3], [], false, selected.Name, 0xFF62CADD);
            }
            try
            {
                if (obj != null) ReferencePreview = new(obj.Mesh, [], [], false, selected.Name, 0xFF62CADD);
                foreach (var n in Session.HistoryPath().Past.Where(n => n.Recipe?.Slots.SelectMany(s => s).Any(p => selected.OriginIndex >= 0 ? p.Kind == SelKind.Origin && p.Index == selected.OriginIndex : p.Obj == selected.Id) == true))
                    ReferenceUseFeatures.Add(new(n));
            }
            catch (Exception e) when (e is not OutOfMemoryException) { }
        }
        OnPropertyChanged(nameof(HasReferenceSelection)); OnPropertyChanged(nameof(ReferenceName)); OnPropertyChanged(nameof(HasReferenceUses)); OnPropertyChanged(nameof(ReferenceCount));
    }
    static MeshData PlanePreview(Plane3 plane)
    {
        var mesh = new MeshData(4, 2, 4) { FaceCount = 1, EdgeCount = 4, Indices = [0, 1, 2, 0, 2, 3], TriFace = [0, 0], SegEdge = [0, 1, 2, 3] };
        Vec3[] points = [plane.PointAt(-12, -12), plane.PointAt(12, -12), plane.PointAt(12, 12), plane.PointAt(-12, 12)];
        for (int i = 0; i < 4; i++)
        {
            var p = points[i]; var q = points[(i + 1) % 4]; var n = plane.Normal;
            mesh.Positions[i * 3] = (float)p.X; mesh.Positions[i * 3 + 1] = (float)p.Y; mesh.Positions[i * 3 + 2] = (float)p.Z;
            mesh.Normals[i * 3] = (float)n.X; mesh.Normals[i * 3 + 1] = (float)n.Y; mesh.Normals[i * 3 + 2] = (float)n.Z;
            float[] edge = [(float)p.X, (float)p.Y, (float)p.Z, (float)q.X, (float)q.Y, (float)q.Z]; Array.Copy(edge, 0, mesh.EdgePoints, i * 6, 6);
        }
        mesh.Min = new(points.Min(p => p.X), points.Min(p => p.Y), points.Min(p => p.Z)); mesh.Max = new(points.Max(p => p.X), points.Max(p => p.Y), points.Max(p => p.Z)); return mesh;
    }

    public ObservableCollection<ParameterDraft> VisibleParameterRows { get; } = [];
    public ObservableCollection<ParameterDraft> PinnedParameterRows { get; } = [];
    public ObservableCollection<FeatureChoice> ParameterUseFeatures { get; } = [];
    [ObservableProperty] public partial ParameterDraft? SelectedParameterDraft { get; set; }
    [ObservableProperty] public partial string ParameterSearch { get; set; } = "";
    [ObservableProperty] public partial bool AddingParameter { get; set; }
    public bool HasParameterSelection => SelectedParameterDraft != null;
    public bool HasPinnedParameters => PinnedParameterRows.Count > 0;
    public string ParameterCountLabel => $"Document parameters ({DocumentParameterRows.Count})";
    partial void OnParameterSearchChanged(string value) => UpdateParameterPresentation();
    partial void OnSelectedParameterDraftChanged(ParameterDraft? value)
    {
        OnPropertyChanged(nameof(HasParameterSelection)); ParameterUseFeatures.Clear();
        if (value != null)
            foreach (var node in Session.HistoryPath().Past.Where(n => n.Recipe?.Expressions?.Values.Any(f => Regex.IsMatch(f, @"\b" + Regex.Escape(value.Name) + @"\b", RegexOptions.IgnoreCase)) == true)) ParameterUseFeatures.Add(new(node));
    }
    bool _refreshingParameterPresentation;
    void UpdateParameterPresentation()
    {
        if (_refreshingParameterPresentation) return;
        _refreshingParameterPresentation = true;
        try
        {
            var keep = SelectedParameterDraft;
            VisibleParameterRows.Clear(); PinnedParameterRows.Clear();
            foreach (var row in DocumentParameterRows)
            {
                if (row.Pinned) PinnedParameterRows.Add(row);
                if ((row.Name + " " + row.Formula).Contains(ParameterSearch, StringComparison.OrdinalIgnoreCase)) VisibleParameterRows.Add(row);
            }
            SelectedParameterDraft = keep != null && VisibleParameterRows.Contains(keep) ? keep : VisibleParameterRows.FirstOrDefault();
            OnPropertyChanged(nameof(HasPinnedParameters)); OnPropertyChanged(nameof(ParameterCountLabel)); OnPropertyChanged(nameof(ParameterCount));
        }
        finally { _refreshingParameterPresentation = false; }
    }
    void UpdateStudioCounts()
    {
        OnPropertyChanged(nameof(ReferenceCount)); OnPropertyChanged(nameof(FeatureCount));
        if (StudioPanel == "parameters") { UpdateParameterPresentation(); OnSelectedParameterDraftChanged(SelectedParameterDraft); }
    }
    public void RefreshParameterTable() { UpdateParameterPresentation(); PreviewDocumentParameters(); }
}
