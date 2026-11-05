using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Core.Drawing;
using Prism.HeadlessTests.Support;
using Prism.Kernel;
using Xunit;

namespace Prism.HeadlessTests.Drawing;

/// <summary>Real OCCT projections, drawing history and file persistence; docs/drawing/README.md.</summary>
/// <seealso href="../../../docs/drawing/README.md"/>
[Trait("Feature", "Drawing"), Trait("Layer", "Headless")]
public class DrawingWorkspaceTests
{
    [AvaloniaFact(DisplayName = "DRW-H001 | Drawing sheets and view layout undo and reopen with model history")]
    [Trait("TestId", "DRW-H001")]
    public void DrawingHistoryAndPersistence()
    {
        var session = new CadSession(); var body = ModelingScenario.CreateBoxBody(session, Vec3.Zero, 60, 40, 20);
        var sheet = session.NewDrawingSheet();
        var view = session.GenerateDrawingView(sheet, "front", [body.Id]); view.X = 120; view.Y = 90;
        Assert.Equal(60, view.MaxX - view.MinX, 6); Assert.Equal(20, view.MaxY - view.MinY, 6);
        session.EditDrawing("Place front", d => d.Sheets[0].Views.Add(view));
        Assert.Empty(session.Drawings.Sheets[0].Annotations);
        var suggestions = DrawingSuggestions.Create(session.Drawings.Sheets[0], view, "Overall size");
        Assert.Equal(2, suggestions.Count);
        session.EditDrawing("Accept width", d => d.Sheets[0].Annotations.Add(suggestions[0]));
        session.EditDrawing("Move width", d => d.Sheets[0].Annotations[0].Label = new(14, 31));
        session.Undo(); Assert.NotEqual(new SheetPoint(14, 31), session.Drawings.Sheets[0].Annotations[0].Label);
        session.Redo(); Assert.Equal(new SheetPoint(14, 31), session.Drawings.Sheets[0].Annotations[0].Label);
        session.EditDrawing("Second sheet", d => d.Sheets.Add(d.Sheets[0].Duplicate()));
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".prism");
        try
        {
            session.Save(path); var reopened = new CadSession(); reopened.Open(path);
            Assert.Equal(2, reopened.Drawings.Sheets.Count); Assert.False(reopened.Dirty);
            var loaded = reopened.Drawings.Sheets[0]; Assert.Equal(body.Id, loaded.Views[0].Sources.Single());
            Assert.Equal(new SheetPoint(14, 31), loaded.Annotations[0].Label); Assert.Equal(60, loaded.Annotations[0].Measurement, 6);
            Assert.False(reopened.DrawingViewIsStale(loaded.Views[0]));
            reopened.Undo(); Assert.Single(reopened.Drawings.Sheets); reopened.Redo(); Assert.Equal(2, reopened.Drawings.Sheets.Count);
            reopened.NewDocument(); Assert.Empty(reopened.Drawings.Sheets); reopened.Open(path); Assert.Equal(2, reopened.Drawings.Sheets.Count);
        }
        finally { File.Delete(path); }
    }
    [AvaloniaFact(DisplayName = "DRW-H002 | Changed or missing source geometry never silently reattaches dimensions")]
    [Trait("TestId", "DRW-H002")]
    public void SourceUpdatesAreExplicitAndConservative()
    {
        var session = new CadSession(); var body = ModelingScenario.CreateBoxBody(session, Vec3.Zero, 60, 40, 20);
        var sheet = session.NewDrawingSheet(); var view = session.GenerateDrawingView(sheet, "front", [body.Id]);
        session.EditDrawing("Front", d => d.Sheets[0].Views.Add(view));
        var suggestions = DrawingSuggestions.Create(session.Drawings.Sheets[0], view, "Overall size");
        session.EditDrawing("Dimensions", d => d.Sheets[0].Annotations.AddRange(suggestions));
        using var bigger = Occ.Box(80, 40, 20); session.Update(body.Id, o => o.WithShape(bigger));
        Assert.True(session.DrawingViewIsStale(session.Drawings.Sheets[0].Views[0]));
        Assert.Equal(60, session.Drawings.Sheets[0].Annotations[0].Measurement, 6);
        session.UpdateDrawing(sheet.Id); var updated = session.Drawings.Sheets[0];
        Assert.Equal(80, updated.Views[0].MaxX - updated.Views[0].MinX, 6);
        Assert.Contains(updated.Annotations, a => a.Unresolved); Assert.Equal(view.X, updated.Views[0].X); Assert.Equal(view.Y, updated.Views[0].Y);
        Assert.False(session.DrawingViewIsStale(updated.Views[0]));
        var removal = new CommandOutput(); removal.Remove.Add(session.Find(body.Id)!); session.Apply(removal);
        session.UpdateDrawing(sheet.Id); Assert.True(session.Drawings.Sheets[0].Views[0].Unresolved);
        Assert.NotEmpty(DrawingSheet.ComposeDraft(session.Drawings.Sheets[0]).Primitives);
    }
    [AvaloniaFact(DisplayName = "DRW-H003 | Projected views use parent axes and first-angle layout")]
    [Trait("TestId", "DRW-H003")]
    public void ProjectionUsesParentOrientation()
    {
        var session = new CadSession(); var body = ModelingScenario.CreateBoxBody(session, Vec3.Zero, 60, 40, 20);
        var sheet = new DraftSheet(); var parent = session.GenerateDrawingView(sheet, "front", [body.Id]);
        var above = session.GenerateDrawingView(sheet, "projected:above", [body.Id], parent);
        Assert.Equal(60, above.MaxX - above.MinX, 6); Assert.Equal(40, above.MaxY - above.MinY, 6); Assert.True(above.Y < parent.Y);
        Assert.Equal(parent.X, above.X); Assert.Equal("x", above.AlignmentAxis);
        sheet.ThirdAngle = false; var first = session.GenerateDrawingView(sheet, "projected:above", [body.Id], parent);
        Assert.True(first.Y > parent.Y); Assert.Equal(above.Direction, first.Direction);
        var right = session.GenerateDrawingView(sheet, "right", [body.Id]);
        var rightAbove = session.GenerateDrawingView(sheet, "projected:above", [body.Id], right);
        Assert.Equal(40, rightAbove.MaxX - rightAbove.MinX, 6); Assert.Equal(60, rightAbove.MaxY - rightAbove.MinY, 6);
    }
    [AvaloniaFact(DisplayName = "DRW-H004 | Section and detail geometry, markers and hatching survive drawing serialization")]
    [Trait("TestId", "DRW-H004")]
    public void SectionsAndDetailsComposeAndReopen()
    {
        var session = new CadSession(); var body = ModelingScenario.CreateBoxBody(session, Vec3.Zero, 60, 40, 20);
        var sheet = new DraftSheet(); var front = session.GenerateDrawingView(sheet, "front", [body.Id]); sheet.Views.Add(front);
        var section = session.GenerateDrawingView(sheet, "front", [body.Id], front, "section", true, (front.MinX + front.MaxX) / 2);
        sheet.Views.Add(section); Assert.NotEmpty(section.HatchLoops); Assert.Equal(40, section.MaxX - section.MinX, 6);
        var detail = session.GenerateDrawingView(sheet, "front", [body.Id], front, "detail", detailCenter: new(front.MinX + 2, front.MinY + 2), detailRadius: 8, detailRatio: 3);
        sheet.Views.Add(detail); Assert.Equal(front.Scale * 3, detail.Scale, 8); Assert.NotEmpty(detail.Strokes);
        var reopened = DrawingDocument.Clone(sheet); var composition = DrawingSheet.ComposeDraft(reopened);
        Assert.Contains(composition.Primitives.OfType<PolyP>(), p => p.Style == LineStyle.Hatch);
        Assert.Contains(composition.Primitives.OfType<PolyP>(), p => p.Style == LineStyle.Cut);
        Assert.Equal(front.Id, reopened.Views[2].ParentId);
        Assert.Contains(composition.Primitives.OfType<TextP>(), p => p.S.Contains("Section"));
    }
    [AvaloniaFact(DisplayName = "DRW-H005 | Failed drawing edits leave current state and undo history untouched")]
    [Trait("TestId", "DRW-H005")]
    public void DrawingEditsAreAtomic()
    {
        var session = new CadSession(); session.NewDrawingSheet(); var current = session.Current; var json = session.Drawings.Serialize();
        Assert.Throws<InvalidOperationException>(() => session.EditDrawing("Failed edit", d => { d.Sheets.Clear(); throw new InvalidOperationException("stop"); }));
        Assert.Same(current, session.Current); Assert.Equal(json, session.Drawings.Serialize());
        session.Undo(); Assert.Empty(session.Drawings.Sheets); session.Redo(); Assert.Single(session.Drawings.Sheets);
    }
    [AvaloniaFact(DisplayName = "DRW-H006 | Grouped model and drawing edits retain one complete undo state")]
    [Trait("TestId", "DRW-H006")]
    public void GroupedModelAndDrawingHistory()
    {
        var session = new CadSession();
        session.BeginGroup();
        ModelingScenario.CreateBoxBody(session, Vec3.Zero, 20, 10, 5);
        session.NewDrawingSheet();
        ModelingScenario.CreateBoxBody(session, new(40, 0, 0), 10, 10, 5);
        session.EndGroup("Model and drawing");
        session.Undo(); Assert.Empty(session.Objects); Assert.Empty(session.Drawings.Sheets);
        session.Redo(); Assert.Equal(2, session.Objects.Count); Assert.Single(session.Drawings.Sheets);
    }
    [AvaloniaFact(DisplayName = "DRW-H007 | Suggestions from stale geometry cannot be accepted")]
    [Trait("TestId", "DRW-H007")]
    public void StaleSuggestionsAreRejected()
    {
        var session = new CadSession(); var body = ModelingScenario.CreateBoxBody(session, Vec3.Zero, 20, 10, 5);
        var sheet = session.NewDrawingSheet(); var front = session.GenerateDrawingView(sheet, "front", [body.Id]);
        session.EditDrawing("View", d => d.Sheets[0].Views.Add(front));
        var suggestions = DrawingSuggestions.Create(session.Drawings.Sheets[0], front, "Overall size");
        using var changed = Occ.Box(30, 10, 5); session.Update(body.Id, o => o.WithShape(changed));
        Assert.Throws<InvalidOperationException>(() => session.AcceptDrawingSuggestions(sheet.Id, suggestions));
        Assert.Empty(session.Drawings.Sheets[0].Annotations);
        session.UpdateDrawing(sheet.Id);
        Assert.Throws<InvalidOperationException>(() => session.AcceptDrawingSuggestions(sheet.Id, suggestions));
    }
    [AvaloniaFact(DisplayName = "DRW-H008 | HLR and sections leave a perforated source unchanged across save and reopen")]
    [Trait("TestId", "DRW-H008")]
    public void ProjectionDoesNotMutateSourceTopology()
    {
        var session = new CadSession(); using var initial = Occ.Box(100, 60, 8);
        Shape plate = Occ.Translate(initial, -initial.Props().Min);
        using var bore = Occ.Cylinder(3, 20);
        for (int i = 0; i < 8; i++)
        {
            using var moved = Occ.Translate(bore, new(8 + 12 * i, 12 + i % 2 * 32, -2));
            var cut = Occ.Cut(plate, moved); plate.Dispose(); plate = cut;
        }
        var body = session.AddObject(plate, "Perforated plate", ObjKind.Solid); string before = body.Shape.ToBrep();
        var sheet = session.NewDrawingSheet(); var top = session.GenerateDrawingView(sheet, "top", [body.Id]);
        session.EditDrawing("Top", d => d.Sheets[0].Views.Add(top));
        Assert.Equal(8, DrawingSuggestions.Create(session.Drawings.Sheets[0], top, "Diameters").Count);
        var front = session.GenerateDrawingView(sheet, "front", [body.Id]);
        var section = session.GenerateDrawingView(sheet, "front", [body.Id], front, "section", true, 50);
        session.EditDrawing("Section", d => d.Sheets[0].Views.AddRange([front, section]));
        Assert.Equal(before, body.Shape.ToBrep());
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".prism");
        try { session.Save(path); var loaded = new CadSession(); loaded.Open(path); Assert.All(loaded.Drawings.Sheets[0].Views, v => Assert.False(loaded.DrawingViewIsStale(v))); }
        finally { File.Delete(path); }
    }
}
