using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Prism.Core;
using Prism.Core.Drawing;
using Prism.Kernel;

namespace Prism.Commands;

/// <summary>Make an engineering drawing of the model: standard views, dimensions, hole callouts, sections, a title block and a bill of materials, saved as PDF, SVG or DXF.</summary>
public sealed class DrawingCommand : Command
{
    readonly SelectionSlot _bodies;
    readonly ChoiceParam _sheet, _format, _units, _projection, _section, _sectionParent;
    readonly BoolParam _front, _top, _right, _iso, _back, _left, _bottom, _dims, _holes, _center, _locate;
    readonly TextParam _title, _number, _material, _drawn, _company, _notes, _path;
    readonly NumberParam _scale, _sectionAt; readonly BoolParam _sectionMid;

    public DrawingCommand()
    {
        _bodies = Slot(new SelectionSlot { Label = "Bodies (none = all)", Filter = SelFilter.Solid | SelFilter.SurfaceObj | SelFilter.Form, Multi = true, Optional = true });
        _sheet = Choice("Sheet size", 1, SheetFormat.All.Keys.ToArray());
        _format = Choice("Save as", 0, "PDF", "SVG", "DXF");
        _projection = Choice("Projection", 0, "Third angle (US, Canada)", "First angle (ISO, Europe)");
        _units = Choice("Dimension units", 0, "Millimetres", "Inches");
        _scale = Num("Scale (0 = automatic)", 0, 0, 100, 0.1, "");
        _front = Check("Front view", true); _top = Check("Top view", true); _right = Check("Right view", true); _iso = Check("Isometric view", true);
        _back = Check("Back view", false); _left = Check("Left view", false); _bottom = Check("Bottom view", false);
        _section = Choice("Section view", 0, "None", "Vertical cut", "Horizontal cut");
        _sectionParent = Choice("Section through the", 0, "front", "top", "right");
        _sectionMid = Check("Cut through the middle of the part", true); _sectionAt = Num("Section position", 0, -1e6, 1e6, 1);
        _dims = Check("Overall dimensions", true); _holes = Check("Hole callouts", true); _center = Check("Centerlines", true); _locate = Check("Hole position dimensions", true);
        _title = Add(new TextParam { Label = "Title" }); _number = Add(new TextParam { Label = "Part number" }); _material = Add(new TextParam { Label = "Material" });
        _drawn = Add(new TextParam { Label = "Drawn by" }); _company = Add(new TextParam { Label = "Company" }); _notes = Add(new TextParam { Label = "Notes (separate lines with ;)" });
        _path = Add(new TextParam { Label = "File (blank = Documents\\Prism\\Drawings)" });
        Add(new ActionParam { Label = "Create drawing", Run = Create });
    }

    protected override void OnBegin()
    {
        // the title block starts from what the part says about itself
        var info = S.PartInfo;
        if (_number.Value.Length == 0) _number.Value = info.PartNumber;
        if (_material.Value.Length == 0) _material.Value = info.Material;
        if (_title.Value.Length == 0) _title.Value = info.Description;
    }

    public override string Title => "Drawing";
    public override string OkText => "Close";
    public override string Hint => "Pick the sheet, views and options, then press Create drawing. The drawing is saved and opened, with hidden lines, dimensions, hole callouts, centerlines and a title block. Sections, details, balloons, GD&T frames and notes can be added through the MCP drawing_export tool.";

    protected override CommandOutput? Build() { Results.Clear(); return new CommandOutput { NoOp = true }; }

    void Create()
    {
        var objs = _bodies.Items.Count > 0
            ? _bodies.Items.Select(i => S.Find(i.Obj)).Where(o => o != null).Select(o => o!).ToList()
            : S.Objects.Where(o => o.Visible && !o.IsAnnotation && o.Kind is ObjKind.Solid or ObjKind.Surface or ObjKind.Form).ToList();
        if (objs.Count == 0) throw new InvalidOperationException("There is nothing to draw: create a solid, surface or Freeform body first.");
        var shapes = objs.Select(o => o.Shape).ToList();
        var shape = shapes.Count == 1 ? shapes[0] : Occ.Compound(shapes);
        var views = new List<string>();
        if (_front.Value) views.Add("front"); if (_top.Value) views.Add("top"); if (_right.Value) views.Add("right"); if (_back.Value) views.Add("back");
        if (_left.Value) views.Add("left"); if (_bottom.Value) views.Add("bottom"); if (_iso.Value) views.Add("iso");
        if (views.Count == 0) throw new InvalidOperationException("Turn on at least one view.");
        var fmt = SheetFormat.All[SheetFormat.All.Keys.ToArray()[_sheet.Index]];
        var opt = new DrawingOptions
        {
            Format = fmt, Views = views, Scale = _scale.Value > 0 ? _scale.Value : null, ThirdAngle = _projection.Index == 0, Units = _units.Index == 1 ? "in" : "mm",
            Dimensions = _dims.Value, HoleCallouts = _holes.Value, Centerlines = _center.Value, LocatingDimensions = _locate.Value,
            Title = new TitleBlock { Title = _title.Value.Length > 0 ? _title.Value : objs[0].Name, Number = _number.Value, Material = _material.Value.Length > 0 ? _material.Value : objs[0].Material?.Name ?? "", Drawn = _drawn.Value, Company = _company.Value },
            Notes = _notes.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
        };
        if (_section.Index > 0)
        {
            var parent = new[] { "front", "top", "right" }[_sectionParent.Index];
            if (!views.Contains(parent)) views.Add(parent);
            opt.Sections.Add(new SectionSpec { Parent = parent, Direction = _section.Index == 1 ? "vertical" : "horizontal", Offset = _sectionMid.Value ? double.NaN : _sectionAt.Value });
        }
        var bom = objs.GroupBy(o => o.Name).Select(g => new BomItem { Name = g.Key, Qty = g.Count(), Material = g.First().Material?.Name ?? "" }).ToList();
        var d = DrawingSheet.Create(shape, bom, opt);
        string ext = new[] { ".pdf", ".svg", ".dxf" }[_format.Index];
        string path = _path.Value.Trim();
        if (path.Length == 0)
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Prism", "Drawings");
            Directory.CreateDirectory(dir);
            string safe = string.Concat(opt.Title.Title.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or ' ' ? c : '_'));
            path = Path.Combine(dir, safe + ext);
        }
        else if (Path.GetExtension(path).Length == 0) path += ext;
        DrawingOutput.Save(path, d.Prims, fmt.W, fmt.H);
        Results.Add(new ResultLine("Saved", path));
        Results.Add(new ResultLine("Scale", d.ScaleText));
        Results.Add(new ResultLine("Dimensions", d.DimensionCount.ToString()));
        if (d.Warnings.Count > 0) Results.Add(new ResultLine("Note", string.Join("  ", d.Warnings)));
        S.Say($"Drawing saved to {path} ({fmt.Name}, scale {d.ScaleText}).");
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
    }
}
