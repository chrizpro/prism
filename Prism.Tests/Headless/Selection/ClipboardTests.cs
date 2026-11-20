using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Selection;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/interface/selection/README.md#copy-paste-and-alt-drag-copy">Copy, paste and Alt-drag copy.</seealso>
[Trait("Feature", "Selection")]
[Trait("Layer", "Headless")]
public class ClipboardTests
{
    [AvaloniaFact(DisplayName = "SEL-H028 | Copy and Paste add an independent copy in the same place and select it")]
    [Trait("TestId", "SEL-H028")]
    public void CopyAndPaste()
    {
        var session = new CadSession();
        var body = CreateBoxBody(session, new Vec3(5, 5, 0), 10, 10, 10);
        session.Update(body.Id, o => o.WithColor(0xFF336699), checkpoint: false);
        Assert.False(session.CanCopy);
        session.SelectObjects([body.Id]);
        Assert.True(session.CanCopy);
        Assert.True(session.CopySelection());

        session.SelectObjects([]);                                  // the copy stands on its own: edits to the original change nothing
        session.Update(body.Id, o => o.WithShape(Occ.Translate(o.Shape, new Vec3(100, 0, 0))), checkpoint: false);
        Assert.True(session.CanPaste);
        var pasted = Assert.Single(session.Paste());
        Assert.Equal(2, session.Objects.Count);
        Assert.NotEqual(body.Id, pasted.Id);
        Assert.Equal(0xFF336699u, pasted.Color);
        Assert.Equal(10, pasted.Shape.Props().Size.X, 6);
        Assert.Equal(5, pasted.Mesh.Min.X, 6);                       // where it was when copied, not where the original went
        Assert.Equal([SelItem.Of(pasted)], session.Selection);

        session.Undo();                                             // one undo step takes the paste away
        Assert.Single(session.Objects);
        Assert.Contains(Prism.Core.Hotkeys.Actions, h => h.Id == "edit.copy" && h.Default == "Ctrl+C");
        Assert.Contains(Prism.Core.Hotkeys.Actions, h => h.Id == "edit.paste" && h.Default == "Ctrl+V");
    }

    [AvaloniaFact(DisplayName = "SEL-H060 | Copying a face pastes a new surface of that face and leaves the body alone")]
    [Trait("TestId", "SEL-H060")]
    public void CopyFacePastesSurface()
    {
        var session = new CadSession();
        var body = CreateBoxBody(session, Vec3.Zero, 10, 10, 10);
        int top = Enumerable.Range(0, body.Shape.Count(SubKind.Face)).First(i => Occ.FaceInfoOf(body.Shape.Sub(SubKind.Face, i)).Normal.Z > 0.99);
        session.SetSelection(new SelItem(body.Id, SelKind.Face, top), false);
        Assert.True(session.CopySelection());
        var pasted = Assert.Single(session.Paste());
        Assert.Equal(ObjKind.Surface, pasted.Kind);
        Assert.Equal(100, pasted.Shape.Props().Area, 6);
        Assert.Equal(10, pasted.Mesh.Max.Z, 6);                         // where the face is on the body
        Assert.Equal(1000, session.Find(body.Id)!.Shape.Props().Volume, 6);

        session.Undo();                                                 // one undo step removes only the new surface
        Assert.Single(session.Objects);
        Assert.Equal(body.Id, session.Objects[0].Id);
    }

    [AvaloniaFact(DisplayName = "SEL-H061 | Copying edges pastes a new curve, and two faces of one body paste as one joined surface")]
    [Trait("TestId", "SEL-H061")]
    public void CopyEdgesAndFaces()
    {
        var session = new CadSession();
        var body = CreateBoxBody(session, Vec3.Zero, 10, 10, 10);
        int Face(Func<Vec3, bool> pick) => Enumerable.Range(0, body.Shape.Count(SubKind.Face)).First(i => pick(Occ.FaceInfoOf(body.Shape.Sub(SubKind.Face, i)).Normal));
        session.SetSelection(new SelItem(body.Id, SelKind.Face, Face(n => n.Z > 0.99)), false);
        session.SetSelection(new SelItem(body.Id, SelKind.Face, Face(n => n.X > 0.99)), true);
        Assert.True(session.CopySelection());
        var surface = Assert.Single(session.Paste());
        Assert.Equal(200, surface.Shape.Props().Area, 6);
        Assert.Equal(1, surface.Shape.Count(SubKind.Shell));            // sewn along the edge they share

        session.SetSelection(new SelItem(body.Id, SelKind.Edge, 0), false);
        Assert.True(session.CopySelection());
        var curve = Assert.Single(session.Paste());
        Assert.Equal(ObjKind.Curve, curve.Kind);
        Assert.Equal(10, curve.Shape.Props().Size.Length > 0 ? new[] { curve.Shape.Props().Size.X, curve.Shape.Props().Size.Y, curve.Shape.Props().Size.Z }.Max() : 0, 6);
    }

    [AvaloniaFact(DisplayName = "SEL-H029 | Alt-dragging a gizmo arrow in Move leaves the original and moves a copy")]
    [Trait("TestId", "SEL-H029")]
    public void AltDragCopies()
    {
        foreach (bool alt in new[] { false, true })
        {
            var session = new CadSession();
            var body = CreateBoxBody(session, new Vec3(-5, -5, 0), 10, 10, 10);
            session.Camera.Width = 800; session.Camera.Height = 600; session.Camera.SetView(0, 0); session.Camera.Fit(new(-10, -10, 0), new(30, 10, 10));
            session.ShowDatumGizmo(Vec3.Zero);
            var gizmo = Assert.IsType<DatumGizmoTool>(session.Datum);
            gizmo.MoveMode = true;
            gizmo.BindTargets(session, [SelItem.Of(body)]);
            var origin = gizmo.Frame.Origin;
            double length = GizmoVisual.Length(session, origin);
            var grab = session.Camera.Project(origin + Vec3.UnitX * length * 0.7)!.Value;
            var start = session.Camera.Project(origin + Vec3.UnitX * length * 0.7)!.Value;
            var end = session.Camera.Project(origin + Vec3.UnitX * (length * 0.7 + 20))!.Value;
            session.PointerDown(new PointerInfo(grab.x, grab.y, true, false, false, alt));
            session.PointerMove(new PointerInfo(end.x, end.y, true, false, false, alt));
            session.PointerUp(new PointerInfo(end.x, end.y, false, false, false, alt));

            if (!alt) { Assert.Single(session.Objects); Assert.True(session.Find(body.Id)!.Mesh.Min.X > -4.9); continue; }
            Assert.Equal(2, session.Objects.Count);
            Assert.Equal(-5, session.Find(body.Id)!.Mesh.Min.X, 3);   // the original stayed
            var copy = Assert.Single(session.Objects, o => o.Id != body.Id);
            Assert.True(copy.Mesh.Min.X > -4, $"the copy was dragged along X: {copy.Mesh.Min.X}");
            Assert.Equal([SelItem.Of(copy)], session.Selection);
        }
    }

    [AvaloniaFact(DisplayName = "SEL-H062 | While sketching, Copy and Paste duplicate the selected curves with the constraints between them, shifted and selected")]
    [Trait("TestId", "SEL-H062")]
    public void SketchCurvesCopyAndPaste()
    {
        var session = new CadSession();
        session.EnterSketch(Plane3.XY, alignView: true);
        var sk = session.Sketch!; var m = sk.Model;
        int a = m.AddPoint(0, 0), b = m.AddPoint(20, 0), c = m.AddPoint(20, 10);
        int l1 = m.AddLine(a, b), l2 = m.AddLine(b, c);
        m.Add(Prism.Core.Sketching.CType.Horizontal, refs: l1);
        m.Add(Prism.Core.Sketching.CType.LineLength, 20, 0, true, l1);
        int circle = m.AddCircle(m.AddPoint(40, 0), 5);
        sk.SelEntities.Add(l1); sk.SelEntities.Add(l2);
        Assert.True(session.CanCopy);
        Assert.True(session.CopySelection());
        Assert.True(session.CanPaste);
        session.Paste();
        Assert.Equal(4, m.Entities.Count(e => e.Type == Prism.Core.Sketching.EntType.Line));
        Assert.Single(m.Entities, e => e.Type == Prism.Core.Sketching.EntType.Circle && e.Id == circle);
        Assert.Equal(2, sk.SelEntities.Count);
        Assert.DoesNotContain(l1, sk.SelEntities);
        Assert.Equal(2, m.Constraints.Count(k => k.Type == Prism.Core.Sketching.CType.Horizontal));
        Assert.Equal(2, m.Constraints.Count(k => k.Type == Prism.Core.Sketching.CType.LineLength));
        var copy = m.E(sk.SelEntities.First());
        Assert.True(m.PointsOf(copy).All(p => m.P(p).X > 0.5 || m.P(p).Y < -0.5), "the copy is shifted off the original");
    }

    [AvaloniaFact(DisplayName = "SEL-H063 | Copying vertices pastes points, and copying mesh triangles pastes a new mesh of just those triangles")]
    [Trait("TestId", "SEL-H063")]
    public void VerticesAndMeshTriangles()
    {
        var session = new CadSession();
        var body = CreateBoxBody(session, new Vec3(0, 0, 0), 10, 10, 10);
        session.SelectItems([new SelItem(body.Id, SelKind.Vertex, 0), new SelItem(body.Id, SelKind.Vertex, 1)]);
        Assert.True(session.CopySelection());
        var points = Assert.Single(session.Paste());
        Assert.Equal(ObjKind.Curve, points.Kind);
        Assert.Equal(2, points.Shape!.Count(SubKind.Vertex));

        var mesh = session.AddObject(null, "Scan", ObjKind.Mesh);
        session.Update(mesh.Id, o => o.WithTri(Prism.Meshing.MeshAnalysis.FromMeshData(body.Mesh)), checkpoint: false);
        mesh = session.Find(mesh.Id)!;
        int total = mesh.Tri!.TriCount;
        session.SelectItems([new SelItem(mesh.Id, SelKind.MeshFace, 0), new SelItem(mesh.Id, SelKind.MeshFace, 1), new SelItem(mesh.Id, SelKind.MeshFace, 2)]);
        Assert.True(session.CanCopy);
        Assert.True(session.CopySelection());
        var piece = Assert.Single(session.Paste());
        Assert.Equal(ObjKind.Mesh, piece.Kind);
        Assert.Equal(3, piece.Tri!.TriCount);
        Assert.Equal(total, session.Find(mesh.Id)!.Tri!.TriCount);
    }
}
