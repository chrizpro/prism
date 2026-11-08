using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Prism.Core.Drawing;
using Prism.Kernel;

namespace Prism.Core;

public sealed partial class CadSession
{
    public DrawingDocument Drawings { get; private set; } = new();
    public event Action? DrawingsChanged;
    public void EditDrawing(string label, Action<DrawingDocument> edit)
    {
        if (Sketch != null || ActiveCommand != null) throw new InvalidOperationException("Finish the active operation before editing the drawing.");
        // Copy before editing: a failed edit leaves both the current document and its history untouched.
        var next = DrawingDocument.Clone(Drawings); edit(next);
        string json = next.Serialize();
        if (json == Drawings.Serialize()) return;
        Drawings = next;
        var node = new HistoryNode { Id = _nextNodeId++, Parent = Current, Label = label, Icon = "drawing", After = Objects,
            Parameters = DocumentParameters, DrawingState = json };
        Current.Children.Add(node); Current.Preferred = node; Current = node;
        Dirty = true; TimelineChanged?.Invoke(); DrawingsChanged?.Invoke();
    }
    void RestoreDrawings(HistoryNode node)
    {
        var state = node;
        while (state.DrawingState == null && state.Parent != null) state = state.Parent;
        Drawings = DrawingDocument.Deserialize(state.DrawingState); DrawingsChanged?.Invoke();
    }
    public DraftSheet NewDrawingSheet()
    {
        var sheet = new DraftSheet { Name = $"Sheet {Drawings.Sheets.Count + 1}", Title = PartInfo.Description.Length > 0 ? PartInfo.Description : "Part drawing", Number = PartInfo.PartNumber, Material = PartInfo.Material };
        EditDrawing("Add drawing sheet", d => d.Sheets.Add(sheet)); return sheet;
    }
    public void AcceptDrawingSuggestions(Guid sheetId, IEnumerable<DraftAnnotation> suggestions)
    {
        var sheet = Drawings.Sheets.Single(s => s.Id == sheetId); var accepted = suggestions.ToList();
        foreach (var a in accepted)
        {
            var view = sheet.Views.SingleOrDefault(v => v.Id == a.ViewId) ?? throw new InvalidOperationException("Suggestion view no longer exists. Generate suggestions again.");
            var signatures = view.Strokes.Select(s => s.Signature).ToHashSet();
            if (DrawingViewIsStale(view) || view.Unresolved || new[] { a.RefA, a.RefB, a.RefC }.Any(r => r != null && !signatures.Contains(r)))
                throw new InvalidOperationException("The source changed. Update the drawing and generate suggestions again.");
        }
        EditDrawing("Accept drawing suggestions", d =>
        {
            var target = d.Sheets.Single(s => s.Id == sheetId);
            foreach (var a in accepted) if (!target.Annotations.Any(old => old.SuggestionKey == a.SuggestionKey)) target.Annotations.Add(DrawingDocument.Clone(a));
        });
    }
    readonly Dictionary<SceneObject, string> _drawingRevisions = new();
    string DrawingRevision(SceneObject o)
    {
        if (_drawingRevisions.TryGetValue(o, out var revision)) return revision;
        // OCCT's Checked flag changes when a shape is validated/read, without changing geometry.
        // Normalize that bit in standalone B-rep topology-flag records and signed zero tokens, which
        // OCCT can flip while rebuilding coordinate systems on read. Retain nonzero geometry data.
        string brep = Regex.Replace(o.Shape.ToBrep().Replace("\r\n", "\n"), @"(?m)^([01]{2})[01]([01]{4})$", "${1}0${2}");
        brep = Regex.Replace(brep, @"(?<!\S)-0(?=\s|$)", "0");
        revision = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(brep)));
        if (_drawingRevisions.Count > 128) _drawingRevisions.Clear();
        _drawingRevisions[o] = revision; return revision;
    }
    public bool DrawingViewIsStale(DraftView view) => view.Sources.Any(id => Find(id) is not { HasBrep: true } o || !view.SourceRevisions.TryGetValue(id, out var rev) || rev != DrawingRevision(o));

    public DraftView GenerateDrawingView(DraftSheet sheet, string orientation, IEnumerable<Guid> sources, DraftView? parent = null,
        string kind = "base", bool vertical = true, double? cut = null, SheetPoint? detailCenter = null, double detailRadius = 10, double detailRatio = 2)
    {
        if (parent != null && (parent.Unresolved || DrawingViewIsStale(parent))) throw new InvalidOperationException("Update the parent drawing view before creating a dependent view.");
        var ids = sources.Distinct().ToList();
        var bodies = ids.Select(id => Find(id) ?? throw new InvalidOperationException("A source body is missing. Choose model bodies again.")).ToList();
        if (bodies.Count == 0) throw new InvalidOperationException("Choose at least one model body.");
        if (bodies.Any(o => o.IsReference)) throw new InvalidOperationException("A reference part (for example the part inside a mold) is not part of the file, so it cannot be a drawing source.");
        if (bodies.Any(o => !o.HasBrep || o.IsAnnotation || o.Kind is not (ObjKind.Solid or ObjKind.Surface or ObjKind.Form))) throw new InvalidOperationException("Choose solid, surface or Freeform bodies.");
        // HLR/section algorithms can add OCCT curve/cache data to their input topology. Work on a deep
        // copy so preparing a drawing never mutates the source model or its persisted revision.
        string brep;
        if (bodies.Count == 1) brep = bodies[0].Shape.ToBrep();
        else { using var compound = Occ.Compound(bodies.Select(o => o.Shape).ToList()); brep = compound.ToBrep(); }
        using var shape = Occ.FromBrep(brep);
        var result = DrawingSheet.GenerateDraftGeometry(shape, orientation, parent, kind, vertical, cut, detailCenter, detailRadius, detailRatio);
        result.Sources = ids; result.SourceRevisions = bodies.ToDictionary(o => o.Id, DrawingRevision);
        result.Scale = kind == "detail" ? (parent?.Scale ?? sheet.Scale) * detailRatio : parent?.Scale ?? sheet.Scale;
        result.X = sheet.Width * .35; result.Y = sheet.Height * .35;
        result.ParentId = parent?.Id;
        result.Caption = ((char)('A' + sheet.Views.Count(v => v.Kind != "base"))).ToString();
        result.Name = kind == "section" ? $"Section {result.Caption}-{result.Caption}" : kind == "detail" ? $"Detail {result.Caption}" : orientation.StartsWith("projected:") ? "Projected " + orientation[10..] : char.ToUpperInvariant(orientation[0]) + orientation[1..];
        if (parent != null)
        {
            double gapX = (parent.MaxX - parent.MinX) * parent.Scale / 2 + (result.MaxX - result.MinX) * result.Scale / 2 + 30;
            double gapY = (parent.MaxY - parent.MinY) * parent.Scale / 2 + (result.MaxY - result.MinY) * result.Scale / 2 + 30;
            result.X = parent.X + gapX; result.Y = parent.Y;
            if (kind == "base")
            {
                double projection = sheet.ThirdAngle ? 1 : -1;
                if (orientation is "projected:above" or "projected:below") { result.AlignmentAxis = "x"; result.X = parent.X; result.Y = parent.Y + (orientation == "projected:above" ? -1 : 1) * gapY * projection; }
                else if (orientation is "projected:left" or "projected:right") { result.AlignmentAxis = "y"; result.X = parent.X + (orientation == "projected:left" ? -1 : 1) * gapX * projection; }
                else if (orientation == "iso") { result.Y -= gapY; result.Aligned = false; }
            }
        }
        return result;
    }

    public void UpdateDrawing(Guid sheetId)
    {
        var sheet = Drawings.Sheets.Single(s => s.Id == sheetId);
        var updated = DrawingDocument.Clone(sheet);
        foreach (var v in updated.Views)
        {
            if (v.Sources.Any(id => Find(id) == null)) { v.Unresolved = true; foreach (var a in updated.Annotations.Where(a => a.ViewId == v.Id)) a.Unresolved = true; continue; }
            try
            {
                var parent = v.ParentId is { } id ? updated.Views.Single(p => p.Id == id) : null;
                if (parent?.Unresolved == true) { v.Unresolved = true; foreach (var a in updated.Annotations.Where(a => a.ViewId == v.Id)) a.Unresolved = true; continue; }
                var next = GenerateDrawingView(updated, v.Orientation, v.Sources, parent, v.Kind, v.CutVertical, v.CutOffset, v.DetailCenter, v.DetailRadius, v.DetailRatio);
                v.Strokes = next.Strokes; v.HatchLoops = next.HatchLoops; v.Direction = next.Direction; v.XDirection = next.XDirection;
                v.MinX = next.MinX; v.MinY = next.MinY; v.MaxX = next.MaxX; v.MaxY = next.MaxY;
                v.SourceRevisions = next.SourceRevisions; v.Unresolved = false;
                var signatures = v.Strokes.Select(s => s.Signature).ToHashSet();
                foreach (var a in updated.Annotations.Where(a => a.ViewId == v.Id))
                    a.Unresolved = new[] { a.RefA, a.RefB, a.RefC }.Any(r => r != null && !signatures.Contains(r));
            }
            catch (Exception e) { v.Unresolved = true; foreach (var a in updated.Annotations.Where(a => a.ViewId == v.Id)) a.Unresolved = true; Say($"{v.Name}: {e.Message}", true); }
        }
        EditDrawing("Update drawing", d => d.Sheets[d.Sheets.FindIndex(s => s.Id == sheetId)] = updated);
    }
    DrawingDocument DrawingDocumentFromFile(string? json)
    {
        var drawing = DrawingDocument.Deserialize(json);
        // Files without drawing history establish their loaded drawing as the history root baseline.
        if (!AllNodes().Any(n => n.DrawingState != null)) Root.DrawingState = drawing.Serialize();
        return drawing;
    }
}
