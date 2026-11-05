using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Prism.Core.Drawing;

namespace Prism.Automation;

public sealed partial class AutomationApi
{
    void RegisterDrawingDocument()
    {
        Add("drawing_document", "Edit the Drawing workspace document using the same model and history as the desktop editor. Actions: state, new_sheet, sheet_settings, add_view, suggest, accept, move, update, parts, delete_annotation, export. New views have no automatic annotations. View kinds: base, section, detail; projected orientations: projected:above/below/left/right with parent_id. Suggestions are previewed until accepted. PDF can include all sheets.",
            Schema(["action"], ("action", "string", "Operation"), ("sheet_id", "string", "Sheet identity; defaults to first sheet"), ("view_id", "string", "View identity"),
                ("parent_id", "string", "Parent view identity"), ("orientation", "string", "front/top/right/back/left/bottom/iso or projected:above/below/left/right"),
                ("kind", "string", "base/section/detail"), ("sources", "array", "Source body names or IDs; required for a base view"),
                ("x", "number", "Paper X or move delta in mm"), ("y", "number", "Paper Y or move delta in mm"), ("scale", "number", "Positive view scale"),
                ("vertical", "boolean", "Vertical section cutting line"), ("cut", "number", "Section coordinate in model mm"), ("radius", "number", "Detail radius in model mm"),
                ("ratio", "number", "Detail magnification"), ("category", "string", "Overall size / Diameters / Center marks / Hole positions"),
                ("suggestions", "array", "Suggestion IDs from the most recent preview"), ("annotation_id", "string", "Annotation identity"),
                ("settings", "object", "Sheet settings: name/title/number/material/notes/units/format/scale/generalTolerance"),
                ("path", "string", "Export file path .pdf/.svg/.dxf"), ("all", "boolean", "Export all sheets to PDF")), a =>
            {
                string action = Str(a, "action");
                if (action == "new_sheet") { S.NewDrawingSheet(); _vm.SetWorkspace("drawing"); }
                if (action is not ("state" or "new_sheet"))
                {
                    var sheet = a["sheet_id"] != null ? S.Drawings.Sheets.Single(s => s.Id == Guid.Parse(Str(a, "sheet_id"))) : S.Drawings.Sheets.FirstOrDefault() ?? throw new InvalidOperationException("Create a drawing sheet first.");
                    DraftView View(string field = "view_id") => sheet.Views.Single(v => v.Id == Guid.Parse(Str(a, field)));
                    void Edit(string label, Action<DraftSheet> edit) => S.EditDrawing(label, d => edit(d.Sheets.Single(s => s.Id == sheet.Id)));
                    switch (action)
                    {
                        case "sheet_settings":
                            var settings = (JsonObject?)a["settings"] ?? throw new ArgumentException("settings required");
                            Edit("Edit drawing sheet", s =>
                            {
                                foreach (var property in settings)
                                    switch (property.Key)
                                    {
                                        case "name": s.Name = property.Value!.GetValue<string>(); break;
                                        case "title": s.Title = property.Value!.GetValue<string>(); break;
                                        case "number": s.Number = property.Value!.GetValue<string>(); break;
                                        case "material": s.Material = property.Value!.GetValue<string>(); break;
                                        case "notes": s.Notes = property.Value!.GetValue<string>(); break;
                                        case "generalTolerance": s.GeneralTolerance = property.Value!.GetValue<string>(); break;
                                        case "units": s.Units = property.Value!.GetValue<string>(); if (s.Units is not ("in" or "mm")) throw new ArgumentException("Use mm or in."); break;
                                        case "format": s.Format = property.Value!.GetValue<string>(); if (!SheetFormat.All.ContainsKey(s.Format)) throw new ArgumentException("Unknown sheet format."); break;
                                        case "scale": s.Scale = property.Value!.GetValue<double>(); if (s.Scale <= 0 || s.Scale > 100) throw new ArgumentException("Scale must be positive and at most 100."); break;
                                        default: throw new ArgumentException("Unknown sheet setting: " + property.Key);
                                    }
                            }); break;
                        case "add_view":
                            var parent = a["parent_id"] != null ? View("parent_id") : null;
                            var sources = parent?.Sources ?? ((JsonArray?)a["sources"])?.Select(n => FindObject(n!.GetValue<string>()).Id).ToList() ?? [];
                            var view = S.GenerateDrawingView(sheet, Str(a, "orientation", "front"), sources, parent, Str(a, "kind", "base"), Bool(a, "vertical", true), a["cut"]?.GetValue<double>(), null, Dbl(a, "radius", 10), Dbl(a, "ratio", 2));
                            if (a["x"] != null) view.X = Dbl(a, "x", 0); if (a["y"] != null) view.Y = Dbl(a, "y", 0);
                            if (a["scale"] != null) { view.Scale = Dbl(a, "scale", 1); if (view.Scale <= 0 || view.Scale > 100) throw new ArgumentException("Scale must be positive and at most 100."); }
                            Edit("Place drawing view", s => { s.Views.Add(view); s.MoveView(view.Id, 0, 0); }); break;
                        case "suggest":
                            var source = View(); if (S.DrawingViewIsStale(source)) throw new InvalidOperationException("Update the drawing before suggesting dimensions.");
                            _drawingSuggestions = DrawingSuggestions.Create(sheet, source, Str(a, "category", "Overall size"));
                            return JsonSerializer.SerializeToNode(_drawingSuggestions, DrawingDocument.JsonOptions);
                        case "accept":
                            var ids = ((JsonArray?)a["suggestions"])?.Select(n => Guid.Parse(n!.GetValue<string>())).ToHashSet();
                            var accepted = _drawingSuggestions.Where(s => (ids == null || ids.Contains(s.Id)) && sheet.Views.Any(v => v.Id == s.ViewId)).ToList();
                            S.AcceptDrawingSuggestions(sheet.Id, accepted);
                            _drawingSuggestions.RemoveAll(s => accepted.Any(x => x.Id == s.Id)); break;
                        case "move":
                            Guid item = Guid.Parse(Str(a, a["annotation_id"] != null ? "annotation_id" : "view_id"));
                            Edit("Move drawing item", s => { if (s.Views.Any(v => v.Id == item)) s.MoveView(item, Dbl(a, "x", 0), Dbl(a, "y", 0)); else s.Annotations.Single(n => n.Id == item).Label += new SheetPoint(Dbl(a, "x", 0), Dbl(a, "y", 0)); }); break;
                        case "update": S.UpdateDrawing(sheet.Id); break;
                        case "parts": Edit("Add parts list", s => { s.ShowParts = true; foreach (var id in s.Views.SelectMany(v => v.Sources).Distinct()) if (s.Parts.All(p => p.SourceId != id) && S.Find(id) is { } body) s.Parts.Add(new() { SourceId = id, Name = body.Name, Quantity = 1, Material = body.Material?.Name ?? "" }); }); break;
                        case "delete_annotation": Edit("Delete annotation", s => s.Annotations.RemoveAll(n => n.Id == Guid.Parse(Str(a, "annotation_id")))); break;
                        case "export":
                            string path = Str(a, "path"); var sheets = Bool(a, "all", false) ? S.Drawings.Sheets : [sheet];
                            if (Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase)) File.WriteAllBytes(path, DrawingOutput.PdfPages(sheets.Select(s => ((IReadOnlyList<Prim>)DrawingSheet.ComposeDraft(s).Primitives, s.Width, s.Height)).ToList()));
                            else { if (sheets.Count != 1) throw new ArgumentException("All-sheet export requires PDF."); DrawingOutput.Save(path, DrawingSheet.ComposeDraft(sheet).Primitives, sheet.Width, sheet.Height); }
                            break;
                        default: throw new ArgumentException("Unknown drawing action: " + action);
                    }
                }
                return JsonSerializer.SerializeToNode(S.Drawings, DrawingDocument.JsonOptions);
            });
    }
    List<DraftAnnotation> _drawingSuggestions = [];
}
