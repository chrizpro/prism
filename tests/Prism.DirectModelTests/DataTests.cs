using System.IO.Compression;
using Prism.Commands;
using Prism.Core;
using Prism.Core.Data;
using Prism.Core.Sketching;
using Prism.Kernel;
using Prism.Simulation;
using Prism.ViewModels;

/// <summary>Tables: reading pasted and imported data, formulas, saving with the document, and the tools that read a sheet. Also construction curves drawn dashed.</summary>
static class DataTests
{
    static string Xlsx(string path)
    {
        File.Delete(path);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Add(string name, string xml) { using var w = new StreamWriter(zip.CreateEntry(name).Open()); w.Write(xml); }
        const string main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        Add("xl/workbook.xml", $"<workbook xmlns=\"{main}\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Cam lift\" sheetId=\"1\" r:id=\"rId1\"/><sheet name=\"Holes\" sheetId=\"2\" r:id=\"rId2\"/></sheets></workbook>");
        Add("xl/_rels/workbook.xml.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Target=\"worksheets/sheet1.xml\"/><Relationship Id=\"rId2\" Target=\"worksheets/sheet2.xml\"/></Relationships>");
        Add("xl/sharedStrings.xml", $"<sst xmlns=\"{main}\"><si><t>time</t></si><si><t>lift</t></si><si><r><t>Hole </t></r><r><t>X</t></r></si></sst>");
        Add("xl/worksheets/sheet1.xml", $"<worksheet xmlns=\"{main}\"><sheetData><row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c><c r=\"B1\" t=\"s\"><v>1</v></c></row><row r=\"2\"><c r=\"A2\"><v>0</v></c><c r=\"B2\"><v>0</v></c></row><row r=\"3\"><c r=\"A3\"><v>0.5</v></c><c r=\"B3\"><v>20</v></c></row><row r=\"4\"><c r=\"A4\"><v>1</v></c><c r=\"B4\"><v>0</v></c></row></sheetData></worksheet>");
        Add("xl/worksheets/sheet2.xml", $"<worksheet xmlns=\"{main}\"><sheetData><row r=\"1\"><c r=\"A1\" t=\"s\"><v>2</v></c></row><row r=\"2\"><c r=\"A2\"><v>30</v></c><c r=\"B2\"><v>0</v></c><c r=\"C2\"><v>0</v></c></row><row r=\"3\"><c r=\"A3\"><v>60</v></c><c r=\"B3\"><v>0</v></c><c r=\"C3\"><v>0</v></c></row><row r=\"4\" ><c r=\"A4\"><v>90</v></c><c r=\"B4\"><v>10</v></c><c r=\"C4\"><v>0</v></c></row></sheetData></worksheet>");
        return path;
    }

    public static void Run(Action<bool, string> check)
    {
        // ---- reading text
        var excel = DataSheet.Parse("x\ty\tz\n1\t2\t3\n4\t5\t6\n");
        check(excel.Count == 3 && excel[1].SequenceEqual(["1", "2", "3"]), "cells copied from Excel (tab separated) read as cells");
        var csv = DataSheet.Parse("name,note\n\"a, b\",\"he said \"\"hi\"\"\"\n");
        check(csv[1][0] == "a, b" && csv[1][1] == "he said \"hi\"", "CSV reads quoted cells, including commas and quotes inside them");
        check(DataSheet.Parse("1;2;3\n4;5;6")[1].Count == 3 && DataSheet.Parse("1 2 3\n4  5   6")[1].SequenceEqual(["4", "5", "6"]), "semicolons and runs of spaces separate cells too");
        var sheet = DataSheet.FromText("Data", "time,lift\n0,0\n0.5,20\nnotes here\n1,0");
        check(sheet.NumericRows(2).Count == 3, "a row of names, or a note, is skipped when tools read the numbers");

        // ---- formulas
        var f = new DataSheet(); f.Set(0, 0, "10"); f.Set(1, 0, "20"); f.Set(2, 0, "30");
        f.Set(0, 1, "=A1*2"); f.Set(1, 1, "=SUM(A1:A3)"); f.Set(2, 1, "=B1+B2/4"); f.Set(0, 2, "=SQRT(A3-5)^2"); f.Set(1, 2, "=20*COS(RADIANS(60))");
        f.Set(3, 0, "=A1/0"); f.Set(3, 1, "=C9+1"); f.Set(3, 2, "=Q"); f.Set(4, 0, "=A5"); f.Set(5, 0, "=AVERAGE(A1:A3)+MAX(A1:A3)");
        bool Is(int r, int c, string shown) => f.Display(r, c) == shown;
        check(Is(0, 1, "20") && Is(1, 1, "60") && Is(2, 1, "35"), "formulas use cells and ranges: =A1*2, =SUM(A1:A3), =B1+B2/4");
        check(Is(0, 2, "25") && Math.Abs(double.Parse(f.Display(1, 2)) - 10) < 1e-9, "brackets, powers and functions: =SQRT(A3-5)^2 and =20*COS(RADIANS(60))");
        check(Is(3, 0, "#DIV/0!") && Is(3, 2, "#NAME?") && Is(4, 0, "#CYCLE!"), "errors show as errors: divide by zero, an unknown name, a cell that refers to itself");
        check(Is(5, 0, "50"), "AVERAGE and MAX read a range");
        check(f.Number(0, 1) == 20 && f.Number(4, 0) == null, "tools read a formula's result as a number, and an error as nothing");

        // ---- Excel files
        string xlsx = Xlsx(Path.Combine(Path.GetTempPath(), "prism-data-test.xlsx"));
        var book = DataSheet.FromFile(xlsx);
        check(book.Count == 2 && book[0].Name == "Cam lift" && book[1].Name == "Holes", "an Excel workbook brings in every sheet by name");
        check(book[0].Raw(0, 0) == "time" && book[0].NumericRows(2).Count == 3 && book[0].NumericRows(2)[1][1] == 20, "its shared strings and numbers read as cells");
        check(book[1].Raw(0, 0) == "Hole X", "text made of several runs reads as one string");
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "prism-data-test.csv"), "x,y,z\n0,0,0\n10,5,0\n20,0,0\n");
        check(DataSheet.FromFile(Path.Combine(Path.GetTempPath(), "prism-data-test.csv"))[0].NumericRows(3).Count == 3, "a CSV file reads the same way");
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "prism-data-test.xls"), "old");
        bool old = false; try { DataSheet.FromFile(Path.Combine(Path.GetTempPath(), "prism-data-test.xls")); } catch (InvalidDataException e) { old = e.Message.Contains(".xlsx"); }
        check(old, "an old .xls file says what to do instead of failing");

        // ---- in the document
        var vm = new MainViewModel(); var s = vm.Session;
        s.AddSheet(new DataSheet { Name = "Holes" }); s.AddSheet(new DataSheet { Name = "holes" });
        check(s.DataSheets.Select(x => x.Name).SequenceEqual(["Holes", "holes 2"]), "two sheets cannot share a name");
        s.DataSheets.Clear();
        var holes = s.AddSheet(DataSheet.FromText("Holes", "x,y,z\n10,0,0\n40,0,0\n70,10,0"));
        s.AddSheet(DataSheet.FromText("Cam", "time,lift\n0,0\n0.5,20\n1,0"));
        string file = Path.Combine(Path.GetTempPath(), "prism-data-doc.prism");
        s.Save(file);
        var again = new CadSession(); again.Open(file);
        check(again.DataSheets.Count == 2 && again.DataSheets[0].Name == "Holes" && again.DataSheets[0].NumericRows(3).Count == 3 && again.DataSheets[1].Raw(1, 1) == "0", "sheets are saved with the document and come back");
        again.NewDocument();
        check(again.DataSheets.Count == 0, "a new document starts without them");
        var p = vm.Tabs.Single(t => t.Title == "View").Buttons.Single(b => b.Label.Replace("\n", " ") == "Data Table");
        vm.ToggleDataPanel();
        check(vm.IsDataPanelOpen && vm.ShowDataEditor && p.Icon == "table", "the View tab's Data Table button opens the panel");
        vm.ToggleDataPanel();
        check(!vm.IsDataPanelOpen, "and closes it again");

        // ---- the tools read a sheet
        s.StartCommand(() => new EquationCurveCommand(true)); var curve = (EquationCurveCommand)s.ActiveCommand!;
        curve.UseSheet("Holes");
        var made = curve.TryBuild();
        check(made != null && made.Add.Count == 1 && made.Add[0].Shape!.Props().Max.X > 69.9, $"Curve from Data draws a curve through the rows of a sheet ({curve.Error})");
        s.CancelCommand();
        s.StartCommand(() => new EquationCurveCommand(true)); curve = (EquationCurveCommand)s.ActiveCommand!;
        curve.Params.OfType<TextParam>().First(x => x.Label.StartsWith("…or read")).Value = xlsx;
        check((curve.TryBuild() is { } xl) && xl.Add[0].Shape!.Props().Max.X > .99, "and from an Excel file named in the same place");
        curve.Params.OfType<TextParam>().First(x => x.Label.StartsWith("…or read")).Value = "@Nothing";
        check(curve.TryBuild() == null && (curve.Error ?? "").Contains("Nothing") && (curve.Error ?? "").Contains("Holes"), "an unknown sheet says which sheets there are");
        s.CancelCommand();

        var block = s.AddObject(Occ.Box(10, 10, 5), "Peg", ObjKind.Solid);
        s.StartCommand(() => new TablePatternCommand()); var pattern = s.ActiveCommand!;
        pattern.Slots[0].Items.Clear(); pattern.Slots[0].TryAdd(SelItem.Of(block));
        pattern.Params.OfType<TextParam>().First(x => x.Label == "Positions").Value = "@Holes";
        pattern.OnChanged();
        var copies = pattern.TryBuild();
        check(copies != null && copies.Add.Count + copies.Replace.Count >= 3, $"Table Pattern places a copy at each row of a sheet, skipping its header ({pattern.Error})");
        s.CancelCommand();

        var cam = DataSource.Expand(s, "@Cam");
        var (times, values) = MotionTable.Read(cam, false, 1);
        check(times.Length == 3 && values[1] == 20 && times[2] == 1, "a Motion table can read a sheet: time and position per row");

        // ---- the panel itself
        var panel = new Prism.Views.DataPanel { DataContext = vm };
        panel.Select(holes);
        check(panel.Sheet == holes, "the panel shows the selected sheet");
        panel.PasteText("a\tb\n1\t2\n3\t4", 0, 5);
        check(holes.Raw(0, 5) == "a" && holes.Raw(2, 6) == "4", "pasting a block from Excel fills cells from the selected one");
        var empty = s.NewSheet(); panel.Select(empty); panel.PasteText("p\tq\n1\t2", 0, 0);
        check(empty.RowCount == 2 && empty.Raw(1, 1) == "2", "pasting into an empty sheet fills it from the top");

        // ---- construction curves are dashed in the view and stay out of the sketch's shape
        var model = new SketchModel();
        int a = model.AddPoint(0, 0), b = model.AddPoint(60, 0), c2 = model.AddPoint(60, 10), d = model.AddPoint(0, 10);
        model.AddLine(a, b); model.AddLine(b, c2); model.AddLine(c2, d); model.AddLine(d, a);
        int diagonal = model.AddLine(a, c2); model.E(diagonal).Construction = true;
        var all = model.ToShapes(Plane3.XY, includeConstruction: true);
        var sk = new CadSession();
        var obj = sk.AddObject(Occ.Profile(all, faces: false), "Sketch", ObjKind.Sketch, plane: Plane3.XY, sketch: model);
        string saved = Path.Combine(Path.GetTempPath(), "prism-construction.prism");
        sk.Save(saved);
        var loaded = new CadSession(); loaded.Open(saved);
        var shape = loaded.Objects.Single(o => o.Kind == ObjKind.Sketch).Shape;
        check(obj.Shape.Count(SubKind.Edge) == 5 && shape.Count(SubKind.Edge) == 4, "an older file that stored a construction line inside the sketch loads with four edges, not five");
        loaded.Camera.Width = 800; loaded.Camera.Height = 600; loaded.ZoomExtents();
        var overlay = new Overlay(); loaded.BuildOverlay(overlay);
        check(overlay.Lines.Count > 8, $"the construction line is drawn dashed by the view ({overlay.Lines.Count} short segments)");
        loaded.StartCommand(() => new ExtrudeCommand()); var extrude = loaded.ActiveCommand!;
        extrude.Slots[0].Items.Clear(); extrude.Slots[0].TryAdd(SelItem.Of(loaded.Objects.Single(o => o.Kind == ObjKind.Sketch)));
        extrude.Params.OfType<NumberParam>().First(x => x.Label.StartsWith("Distance") || x.Label.StartsWith("Depth")).Value = 5;
        extrude.OnChanged();
        var solid = extrude.TryBuild();
        check(solid != null && Math.Abs(solid.Add.Sum(x => x.Shape!.Props().Volume) - 60 * 10 * 5) < 1, "and the sketch with its construction line extrudes as the whole rectangle");
        loaded.CancelCommand();
    }
}
