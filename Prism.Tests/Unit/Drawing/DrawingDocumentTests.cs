using System.Text;
using Prism.Core.Drawing;
using Xunit;

namespace Prism.UnitTests.Drawing;

/// <summary>Editable drawing behavior. Canonical specification: docs/drawing/README.md.</summary>
/// <seealso href="../../../docs/drawing/README.md"/>
[Trait("Feature", "Drawing"), Trait("Layer", "Unit")]
public class DrawingDocumentTests
{
    [Fact(DisplayName = "DRW-U001 | Dimensions measure model geometry independently of paper scale")]
    [Trait("TestId", "DRW-U001")]
    public void DimensionsDoNotMeasurePaper()
    {
        var sheet = Fixture(); var view = sheet.Views[0];
        var a = new DraftAnnotation { ViewId = view.Id, Type = "aligned", A = new(0, 0), B = new(15.24, 20.32), Label = new(40, 10), Precision = 3 };
        sheet.Annotations.Add(a);
        Assert.Equal("25.400", a.ValueText("mm")); Assert.Equal("1.000", a.ValueText("in"));
        view.Scale = .25; var small = DrawingSheet.ComposeDraft(sheet);
        view.Scale = 4; var large = DrawingSheet.ComposeDraft(sheet);
        Assert.Contains(small.Primitives.OfType<TextP>(), t => t.S == "25.400");
        Assert.Contains(large.Primitives.OfType<TextP>(), t => t.S == "25.400");
        a.Type = "horizontal"; Assert.Equal(15.24, a.Measurement, 8);
        a.Type = "vertical"; Assert.Equal(20.32, a.Measurement, 8);
        a.Type = "diameter"; a.Radius = 12.7; Assert.Equal("Ø1.000", a.ValueText("in"));
        a.Type = "radius"; Assert.Equal("R0.500", a.ValueText("in"));
        a.Type = "angular"; a.A = new(10, 0); a.B = new(0, 0); a.C = new(0, 10); Assert.Equal("90.000°", a.ValueText("in"));
    }
    [Fact(DisplayName = "DRW-U002 | Dense circular patterns offer every position and retain accepted placement")]
    [Trait("TestId", "DRW-U002")]
    public void DensePatternIsNotTruncatedOrDuplicated()
    {
        var sheet = Fixture(); var view = sheet.Views[0];
        for (int i = 0; i < 8; i++) view.Strokes.Add(Circle(5 + i * 6, 4 + i * 2, 1));
        var proposed = DrawingSuggestions.Create(sheet, view, "Hole positions");
        Assert.Equal(16, proposed.Count); Assert.Equal(8, proposed.Count(a => a.Type == "ordinate-x"));
        var accepted = proposed[0]; accepted.Label = new(-33, -17); sheet.Annotations.Add(accepted);
        var again = DrawingSuggestions.Create(sheet, view, "Hole positions");
        Assert.Equal(15, again.Count); Assert.DoesNotContain(again, a => a.SuggestionKey == accepted.SuggestionKey);
        Assert.Equal(new SheetPoint(-33, -17), sheet.Annotations.Single().Label);
        var table = DrawingSuggestions.HoleTable(sheet, view);
        Assert.Equal(10, table.Text.Split('\n').Length); Assert.Contains("8   X", table.Text);
    }
    [Fact(DisplayName = "DRW-U003 | Save data retains geometry references, sheet layout and explicit tolerances")]
    [Trait("TestId", "DRW-U003")]
    public void DrawingDataRoundtripsWithoutDefaultManufacturingInstructions()
    {
        var sheet = Fixture(); var view = sheet.Views[0];
        view.Sources.Add(Guid.NewGuid()); view.SourceRevisions[view.Sources[0]] = "revision";
        sheet.Annotations.Add(new() { ViewId = view.Id, Type = "diameter", A = new(4, 9), Radius = 2.5, RefA = view.Strokes[0].Signature, Label = new(20, -13), Tolerance = "±0.01", Precision = 3 });
        var document = new DrawingDocument { Sheets = [sheet, sheet.Duplicate()] };
        var loaded = DrawingDocument.Deserialize(document.Serialize());
        Assert.Equal(2, loaded.Sheets.Count); Assert.Equal(sheet.Views[0].Id, loaded.Sheets[0].Views[0].Id);
        var annotation = Assert.Single(loaded.Sheets[0].Annotations);
        Assert.Equal(new SheetPoint(4, 9), annotation.A); Assert.Equal(new SheetPoint(20, -13), annotation.Label);
        Assert.Equal(sheet.Views[0].Strokes[0].Signature, annotation.RefA); Assert.Equal("±0.01", annotation.Tolerance);
        string svg = DrawingOutput.Svg(DrawingSheet.ComposeDraft(loaded.Sheets[0]).Primitives, sheet.Width, sheet.Height);
        Assert.DoesNotContain("BREAK ALL SHARP EDGES", svg); Assert.DoesNotContain("LINEAR ±0.1", svg);
    }
    [Fact(DisplayName = "DRW-U004 | Projected views stay aligned and view motion carries annotation labels")]
    [Trait("TestId", "DRW-U004")]
    public void AlignmentAndIndependentPlacement()
    {
        var sheet = Fixture(); var parent = sheet.Views[0];
        var top = new DraftView { ParentId = parent.Id, AlignmentAxis = "x", X = parent.X, Y = 40 };
        var side = new DraftView { ParentId = parent.Id, AlignmentAxis = "y", X = 220, Y = parent.Y };
        sheet.Views.AddRange([top, side]);
        sheet.Annotations.Add(new() { ViewId = parent.Id, Type = "note", Label = new(12, 16), Text = "Inspect this face" });
        sheet.MoveView(parent.Id, 20, 30);
        Assert.Equal(parent.X, top.X); Assert.Equal(40, top.Y); Assert.Equal(parent.Y, side.Y); Assert.Equal(220, side.X);
        sheet.MoveView(top.Id, 20, 10); Assert.Equal(parent.X, top.X); Assert.Equal(50, top.Y);
        top.Aligned = false; sheet.MoveView(top.Id, 20, 0); Assert.Equal(parent.X + 20, top.X);
        var note = DrawingSheet.ComposeDraft(sheet).Elements.Single(e => e.Kind == "annotation").Primitives.OfType<TextP>().Single();
        Assert.Equal(parent.X + 12, note.X); Assert.Equal(parent.Y + 16, note.Y);
    }
    [Fact(DisplayName = "DRW-U005 | Duplicate sheet remaps dependent views and annotation identities")]
    [Trait("TestId", "DRW-U005")]
    public void DuplicatesHaveIndependentIdentity()
    {
        var sheet = Fixture(); var parent = sheet.Views[0]; var child = new DraftView { ParentId = parent.Id };
        sheet.Views.Add(child); sheet.Annotations.Add(new() { ViewId = child.Id, Text = "Child note" });
        var copy = sheet.Duplicate();
        Assert.NotEqual(sheet.Id, copy.Id); Assert.NotEqual(parent.Id, copy.Views[0].Id);
        Assert.Equal(copy.Views[0].Id, copy.Views[1].ParentId); Assert.Equal(copy.Views[1].Id, copy.Annotations[0].ViewId);
        Assert.Equal(2, copy.Dependents(copy.Views[0].Id).Count);
        copy.Views[0].Strokes[0].Points[0] = new(99, 99); Assert.Equal(new SheetPoint(0, 0), parent.Strokes[0].Points[0]);
    }
    [Fact(DisplayName = "DRW-U006 | Review selects overlapping, overridden and unresolved annotations")]
    [Trait("TestId", "DRW-U006")]
    public void ReviewIdentifiesActualItems()
    {
        var sheet = Fixture(); var v = sheet.Views[0];
        var first = new DraftAnnotation { ViewId = v.Id, Text = "Overlapping note", Label = new(20, 0), Unresolved = true };
        var second = new DraftAnnotation { ViewId = v.Id, Text = "Another note", Label = first.Label };
        var dimension = new DraftAnnotation { ViewId = v.Id, Type = "horizontal", A = new(0, 0), B = new(20, 0), Text = "OVERRIDE", Label = new(0, 20) };
        sheet.Annotations.AddRange([first, second, dimension]);
        var composition = DrawingSheet.ComposeDraft(sheet);
        Assert.Contains(composition.Warnings, w => w.Id == first.Id && w.Message.Contains("overlap"));
        Assert.Contains(composition.Warnings, w => w.Id == second.Id && w.Message.Contains("overlap"));
        Assert.Contains(composition.Warnings, w => w.Id == first.Id && w.Message.Contains("unresolved"));
        Assert.Contains(composition.Warnings, w => w.Id == dimension.Id && w.Message.Contains("override"));
        first.Label = new(-500, -500);
        Assert.Contains(DrawingSheet.ComposeDraft(sheet).Warnings, w => w.Id == first.Id && w.Message.Contains("outside"));
    }
    [Fact(DisplayName = "DRW-U007 | Multi-sheet PDF has separate physical page sizes and shared sheet content")]
    [Trait("TestId", "DRW-U007")]
    public void ExportUsesComposedPages()
    {
        var first = Fixture(); var second = Fixture(); second.Format = "Letter"; second.Title = "Second sheet";
        var a = DrawingSheet.ComposeDraft(first); var b = DrawingSheet.ComposeDraft(second);
        string pdf = Encoding.Latin1.GetString(DrawingOutput.PdfPages([((IReadOnlyList<Prim>)a.Primitives, first.Width, first.Height), ((IReadOnlyList<Prim>)b.Primitives, second.Width, second.Height)]));
        Assert.Contains("/Count 2", pdf); Assert.Contains("/MediaBox [0 0 1190.551 841.89]", pdf); Assert.Contains("/MediaBox [0 0 792 612]", pdf);
        Assert.Contains("(Second sheet)", pdf);
        string svg = DrawingOutput.Svg(b.Primitives, second.Width, second.Height); Assert.Contains("279.4mm", svg); Assert.Contains("Second sheet", svg);
        string dxf = DrawingOutput.Dxf(b.Primitives, second.Width, second.Height); Assert.Contains("Second sheet", dxf); Assert.Contains("$INSUNITS\n70\n4", dxf);
    }
    [Fact(DisplayName = "DRW-U008 | Circle recognition rejects noncircular closed outlines")]
    [Trait("TestId", "DRW-U008")]
    public void CircularAnnotationsRequireCircleGeometry()
    {
        var circle = Circle(12, -4, 5);
        Assert.True(circle.TryCircle(out var center, out var radius)); Assert.Equal(12, center.X, 7); Assert.Equal(-4, center.Y, 7); Assert.Equal(5, radius, 7);
        circle.Points[8] = new(50, 20); Assert.False(circle.TryCircle(out _, out _));
        var arc = Circle(12, -4, 5); arc.Points = arc.Points.Take(20).ToList();
        Assert.True(arc.TryArc(out _, out double arcRadius)); Assert.Equal(5, arcRadius, 7); Assert.False(arc.TryCircle(out _, out _));
        var unclassified = new DrawingStroke { Points = Circle(12, -4, 5).Points }; Assert.False(unclassified.TryCircle(out _, out _));
    }
    [Fact(DisplayName = "DRW-U009 | Changing projection flips aligned views and preserves detached placement")]
    [Trait("TestId", "DRW-U009")]
    public void ProjectionChangeRepositionsAlignedViews()
    {
        var sheet = Fixture(); var parent = sheet.Views[0];
        var above = new DraftView { ParentId = parent.Id, AlignmentAxis = "x", X = parent.X, Y = parent.Y - 50 };
        var right = new DraftView { ParentId = parent.Id, AlignmentAxis = "y", X = parent.X + 80, Y = parent.Y };
        var detached = new DraftView { ParentId = parent.Id, AlignmentAxis = "x", Aligned = false, X = 300, Y = 50 };
        sheet.Views.AddRange([above, right, detached]); sheet.SetProjection(false);
        Assert.Equal(parent.Y + 50, above.Y); Assert.Equal(parent.X - 80, right.X); Assert.Equal(300, detached.X); Assert.Equal(50, detached.Y);
        sheet.SetProjection(true); Assert.Equal(parent.Y - 50, above.Y); Assert.Equal(parent.X + 80, right.X);
    }
    [Fact(DisplayName = "DRW-U010 | Section cutting letters do not collide with the parent view caption")]
    [Trait("TestId", "DRW-U010")]
    public void SectionMarkerReservesCaptionSpace()
    {
        var sheet = Fixture(); var parent = sheet.Views[0];
        parent.Name = "Front";
        var section = new DraftView { Kind = "section", ParentId = parent.Id, Name = "Section A-A", Caption = "A", CutVertical = true, CutOffset = 30, X = 250, Y = 110, MinX = 0, MaxX = 20, MinY = 0, MaxY = 40 };
        sheet.Views.Add(section);
        var composition = DrawingSheet.ComposeDraft(sheet);
        var caption = composition.Elements.Single(e => e.Id == parent.Id).Primitives.OfType<TextP>().Single(t => t.S == "Front");
        foreach (var marker in composition.Elements.Single(e => e.Id == section.Id).Primitives.OfType<TextP>().Where(t => t.S == "A"))
            Assert.False(DrawingComposition.Bounds([caption]).Intersects(DrawingComposition.Bounds([marker])));
    }
    static DraftSheet Fixture() => new() { Title = "Bracket", Views = [new DraftView { X = 130, Y = 110, MinX = 0, MinY = 0, MaxX = 60, MaxY = 40,
        Strokes = [new DrawingStroke { Points = [new(0, 0), new(60, 0), new(60, 40), new(0, 40), new(0, 0)] }] }] };
    static DrawingStroke Circle(double x, double y, double radius) => new() { AnalyticRadius = radius, AnalyticCenter = new(x, y), Points = Enumerable.Range(0, 73).Select(i => new SheetPoint(x + radius * Math.Cos(i * Math.PI / 36), y + radius * Math.Sin(i * Math.PI / 36))).ToList() };
}
