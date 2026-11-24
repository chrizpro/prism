using Avalonia.Headless.XUnit;
using Prism.Core;
using Prism.Kernel;
using Prism.Ribbon;
using Prism.ViewModels;
using Xunit;

namespace Prism.HeadlessTests.Selection;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/interface/selection/README.md#more-selection-tools-owner-approved-2026-11-24">More selection tools.</seealso>
[Trait("Feature", "Selection")]
[Trait("Layer", "Headless")]
public class SelectMoreTests
{
    /// <summary>A 40 x 30 x 20 mm box (x 0..40, y 0..30, z 0..20) with the top edge at y = 30 filleted at 2 mm and a 6 mm hole through the top at (28, 15).</summary>
    static SceneObject Part(CadSession s)
    {
        using var raw = Occ.Box(40, 30, 20);
        using var box = Occ.Translate(raw, -raw.Props().Min);
        int edge = Enumerable.Range(0, box.Count(SubKind.Edge)).Single(i =>
        {
            using var e = box.Sub(SubKind.Edge, i); var info = Occ.EdgeInfoOf(e);
            return Math.Abs(info.Start.Y - 30) < 1e-6 && Math.Abs(info.End.Y - 30) < 1e-6 && Math.Abs(info.Start.Z - 20) < 1e-6 && Math.Abs(info.End.Z - 20) < 1e-6;
        });
        using var filleted = Occ.Fillet(box, [edge], 2);
        using var cyl = Occ.Cylinder(3, 40);
        using var drill = Occ.Translate(cyl, new Vec3(28, 15, -10) - new Vec3((cyl.Props().Min.X + cyl.Props().Max.X) / 2, (cyl.Props().Min.Y + cyl.Props().Max.Y) / 2, 0));
        return s.AddObject(Occ.Cut(filleted, drill), "Part", ObjKind.Solid);
    }

    static int Face(SceneObject o, Func<FaceInfo, Vec3, bool> match)
    {
        for (int i = 0; i < o.Shape.Count(SubKind.Face); i++)
        {
            using var f = o.Shape.Sub(SubKind.Face, i);
            if (match(Occ.FaceInfoOf(f), f.Props().Center)) return i;
        }
        throw new InvalidOperationException("no such face");
    }

    static int Top(SceneObject o) => Face(o, (i, c) => i.Type == "Plane" && Math.Abs(c.Z - 20) < 1e-6);
    static int HoleWall(SceneObject o) => Face(o, (i, _) => i.Type == "Cylinder" && Math.Abs(i.R1 - 3) < 1e-6);
    static int Fillet(SceneObject o) => Face(o, (i, _) => i.Type == "Cylinder" && Math.Abs(i.R1 - 2) < 1e-6);
    static SelItem F(SceneObject o, int i) => new(o.Id, SelKind.Face, i);
    static HashSet<SelItem> Sel(CadSession s) => s.Selection.ToHashSet();

    [AvaloniaFact(DisplayName = "SEL-H070 | The Select group's More dropdown holds every selection tool, and the old Select Same split is gone")]
    [Trait("TestId", "SEL-H070")]
    public void MoreDropdownHoldsTheTools()
    {
        var vm = new MainViewModel();
        var group = vm.Tabs.Single(t => t.Title == "Solid").Groups.Single(g => g.Title == "Select");
        var more = group.Items.OfType<RibbonButton>().Single(b => b.Label == "More");
        Assert.Equal(["Grow", "Shrink", "Tangent", "Whole Feature", "Same Feature", "Same Radius", "Fillets Up To", "Coplanar", "Coaxial", "Same Color", "Linked", "To Faces", "To Edges", "To Vertices", "Reselect Last", "Save Selection Set"],
            more.Menu!.Select(b => b.Label).ToArray());
        Assert.DoesNotContain(group.Items.OfType<RibbonButton>(), b => b.Label is "Adjacent" or "Same Color");
    }

    [AvaloniaFact(DisplayName = "SEL-H071 | Grow adds the faces around the top; Shrink takes them away again")]
    [Trait("TestId", "SEL-H071")]
    public void GrowAndShrinkFaces()
    {
        var s = new MainViewModel().Session; var part = Part(s); s.Mode = SelMode.Face;
        int top = Top(part);
        s.SetSelection(F(part, top), false);
        s.SelectMore(CadSession.MoreSelect.Grow);
        // top + three flat sides + the fillet + the hole wall
        Assert.Equal(6, s.Selection.Count);
        Assert.Contains(F(part, Fillet(part)), s.Selection);
        Assert.Contains(F(part, HoleWall(part)), s.Selection);
        s.SelectMore(CadSession.MoreSelect.Shrink);
        Assert.Equal([F(part, top)], s.Selection);
    }

    [AvaloniaFact(DisplayName = "SEL-H072 | Tangent from the fillet takes it and the two flat faces it blends into")]
    [Trait("TestId", "SEL-H072")]
    public void TangentFaces()
    {
        var s = new MainViewModel().Session; var part = Part(s); s.Mode = SelMode.Face;
        s.SetSelection(F(part, Fillet(part)), false);
        s.SelectMore(CadSession.MoreSelect.Tangent);
        Assert.Equal(3, s.Selection.Count);
        Assert.Contains(F(part, Top(part)), s.Selection);
        Assert.Contains(F(part, Face(part, (i, c) => i.Type == "Plane" && Math.Abs(c.Y - 30) < 1e-6)), s.Selection);
    }

    [AvaloniaFact(DisplayName = "SEL-H073 | Whole Feature, Same Radius and Fillets Up To tell the hole from the fillet")]
    [Trait("TestId", "SEL-H073")]
    public void FeatureAndRadius()
    {
        var s = new MainViewModel().Session; var part = Part(s); s.Mode = SelMode.Face;
        int hole = HoleWall(part), fillet = Fillet(part);
        s.SetSelection(F(part, hole), false);
        s.SelectMore(CadSession.MoreSelect.Feature);
        Assert.Contains(F(part, hole), s.Selection);
        Assert.DoesNotContain(F(part, fillet), s.Selection);

        s.SetSelection(F(part, hole), false);
        s.SelectMore(CadSession.MoreSelect.SameRadius);
        Assert.Equal([F(part, hole)], s.Selection);

        s.SetSelection(F(part, fillet), false);
        s.SelectMore(CadSession.MoreSelect.FilletsUpTo);
        Assert.Equal([F(part, fillet)], s.Selection);
    }

    [AvaloniaFact(DisplayName = "SEL-H074 | Coplanar takes the bottoms of two boxes on one plane; Coaxial takes a pin on the hole's axis")]
    [Trait("TestId", "SEL-H074")]
    public void CoplanarAndCoaxial()
    {
        var s = new MainViewModel().Session; var part = Part(s); s.Mode = SelMode.Face;
        var other = Support.ModelingScenario.CreateBoxBody(s, new Vec3(60, 0, 0), 10, 10, 10);
        using (var c = Occ.Cylinder(5, 10)) s.AddObject(Occ.Translate(c, new Vec3(28, 15, 30) - new Vec3((c.Props().Min.X + c.Props().Max.X) / 2, (c.Props().Min.Y + c.Props().Max.Y) / 2, 0)), "Pin", ObjKind.Solid);
        var pin = s.Objects.Last();

        s.SetSelection(F(part, Face(part, (i, c) => i.Type == "Plane" && Math.Abs(c.Z) < 1e-6)), false);
        s.SelectMore(CadSession.MoreSelect.Coplanar);
        Assert.Equal(2, s.Selection.Count);
        Assert.Contains(F(other, Face(other, (i, c) => i.Type == "Plane" && Math.Abs(c.Z) < 1e-6)), s.Selection);

        s.SetSelection(F(part, HoleWall(part)), false);
        s.SelectMore(CadSession.MoreSelect.Coaxial);
        Assert.Equal(2, s.Selection.Count);
        Assert.Contains(F(pin, Face(pin, (i, _) => i.Type == "Cylinder")), s.Selection);
    }

    [AvaloniaFact(DisplayName = "SEL-H075 | To Edges gives the border of the top face; To Vertices its corners; To Faces the faces beside an edge")]
    [Trait("TestId", "SEL-H075")]
    public void ConvertBetweenLevels()
    {
        var s = new MainViewModel().Session; s.Mode = SelMode.Face;
        var box = Support.ModelingScenario.CreateBoxBody(s, Vector0, 10, 10, 10);
        int top = Face(box, (i, c) => i.Type == "Plane" && Math.Abs(c.Z - 10) < 1e-6);
        s.SetSelection(F(box, top), false);
        s.SelectMore(CadSession.MoreSelect.ToEdges);
        Assert.Equal(SelMode.Edge, s.Mode);
        Assert.Equal(4, s.Selection.Count);
        Assert.All(s.Selection, e => Assert.All(s.EdgePoints(e), p => Assert.Equal(10, p.Z, 5)));

        s.Mode = SelMode.Face; s.SetSelection(F(box, top), false);
        s.SelectMore(CadSession.MoreSelect.ToVertices);
        Assert.Equal(SelMode.Vertex, s.Mode);
        Assert.Equal(4, s.Selection.Count);
        Assert.All(s.Selection, v => Assert.Equal(10, s.VertexPoint(v)!.Value.Z, 5));

        s.Mode = SelMode.Edge;
        var edge = FirstTopEdge(s, box);
        s.SetSelection(edge, false);
        s.SelectMore(CadSession.MoreSelect.ToFaces);
        Assert.Equal(SelMode.Face, s.Mode);
        Assert.Equal(2, s.Selection.Count);
        Assert.Contains(F(box, top), s.Selection);
    }

    static readonly Vec3 Vector0 = Vec3.Zero;
    static double CenterX(Prism.Meshing.TriMesh tm, int t) => (tm.P(tm.T[3 * t]).X + tm.P(tm.T[3 * t + 1]).X + tm.P(tm.T[3 * t + 2]).X) / 3;
    static SelItem FirstTopEdge(CadSession s, SceneObject box) =>
        Enumerable.Range(0, box.Mesh.EdgeCount).Select(i => new SelItem(box.Id, SelKind.Edge, i)).First(e => s.EdgePoints(e).All(p => Math.Abs(p.Z - 10) < 1e-6));

    [AvaloniaFact(DisplayName = "SEL-H076 | Grow and Shrink on edges: a top edge grows by the four edges at its ends and shrinks back")]
    [Trait("TestId", "SEL-H076")]
    public void GrowEdges()
    {
        var s = new MainViewModel().Session; s.Mode = SelMode.Edge;
        var box = Support.ModelingScenario.CreateBoxBody(s, Vec3.Zero, 10, 10, 10);
        var edge = FirstTopEdge(s, box);
        s.SetSelection(edge, false);
        s.SelectMore(CadSession.MoreSelect.Grow);
        Assert.Equal(5, s.Selection.Count);
        s.SelectMore(CadSession.MoreSelect.Shrink);
        Assert.Equal([edge], s.Selection);
    }

    [AvaloniaFact(DisplayName = "SEL-H077 | Linked on a mesh of two spheres takes only the sphere the triangle is on")]
    [Trait("TestId", "SEL-H077")]
    public void LinkedMesh()
    {
        var s = new MainViewModel().Session; s.Mode = SelMode.Face;
        using var a = Occ.Sphere(5); using var b0 = Occ.Sphere(5); using var b = Occ.Translate(b0, new Vec3(30, 0, 0));
        using var both = Occ.Compound([a, b]);
        var mesh = s.AddObject(null, "Scan", ObjKind.Mesh);
        s.Update(mesh.Id, o => o.WithTri(Prism.Meshing.MeshAnalysis.FromMeshData(both.Tessellate())), checkpoint: false);
        var tm = s.Find(mesh.Id)!.Tri!;
        int seed = Enumerable.Range(0, tm.TriCount).First(t => CenterX(tm, t) < 10);
        s.SetSelection(new SelItem(mesh.Id, SelKind.MeshFace, seed), false);
        s.SelectMore(CadSession.MoreSelect.Linked);
        int left = Enumerable.Range(0, tm.TriCount).Count(t => CenterX(tm, t) < 15);
        Assert.Equal(left, s.Selection.Count);
        Assert.All(s.Selection, i => Assert.True(CenterX(tm, i.Index) < 15));
    }

    [AvaloniaFact(DisplayName = "SEL-H078 | Reselect Last puts back faces lost to a click in empty space, and again swaps back")]
    [Trait("TestId", "SEL-H078")]
    public void ReselectLast()
    {
        var s = new MainViewModel().Session; var part = Part(s); s.Mode = SelMode.Face;
        var faces = new[] { F(part, Top(part)), F(part, Fillet(part)) };
        s.SelectItems(faces);
        s.ClearSelection();
        Assert.True(s.CanReselect);
        Assert.True(s.ReselectLast());
        Assert.Equal(faces.ToHashSet(), Sel(s));
        s.SetSelection(F(part, HoleWall(part)), false);
        s.ReselectLast();
        Assert.Equal(faces.ToHashSet(), Sel(s));
        s.ReselectLast();
        Assert.Equal([F(part, HoleWall(part))], s.Selection);
    }

    [AvaloniaFact(DisplayName = "SEL-H079 | A saved selection set is listed in the browser, survives save and reopen, and selects its faces")]
    [Trait("TestId", "SEL-H079")]
    public void SelectionSetsAreSaved()
    {
        var vm = new MainViewModel(); var s = vm.Session; var part = Part(s); s.Mode = SelMode.Face;
        var faces = new[] { F(part, Top(part)), F(part, HoleWall(part)) };
        s.SelectItems(faces);
        Assert.Equal("Selection 1", s.SaveSelectionSet());
        Assert.Contains(vm.AllBrowserNodes(), n => n.IsSelectionSet && n.Title == "Selection 1");
        Assert.True(s.RenameSelectionSet("Selection 1", "Bolt faces"));
        var path = Path.Combine(Path.GetTempPath(), $"sel-sets-{Guid.NewGuid():N}.prism");
        try
        {
            s.Save(path);
            var other = new MainViewModel().Session;
            other.Open(path);
            other.Mode = SelMode.Object;
            Assert.Equal(2, other.UseSelectionSet("Bolt faces"));
            Assert.Equal(SelMode.Face, other.Mode);
            Assert.Equal(faces.ToHashSet(), other.Selection.ToHashSet());
            Assert.True(other.DeleteSelectionSet("Bolt faces"));
            Assert.Empty(other.SelectionSets);
            Assert.NotNull(other.Find(part.Id));
        }
        finally { File.Delete(path); }
    }

    [AvaloniaFact(DisplayName = "SEL-H080 | The shortest path between two opposite sides of a box is three faces")]
    [Trait("TestId", "SEL-H080")]
    public void ShortestFacePath()
    {
        var s = new MainViewModel().Session; s.Mode = SelMode.Face;
        var box = Support.ModelingScenario.CreateBoxBody(s, Vec3.Zero, 10, 10, 10);
        int left = Face(box, (i, c) => i.Type == "Plane" && Math.Abs(c.X) < 1e-6), right = Face(box, (i, c) => i.Type == "Plane" && Math.Abs(c.X - 10) < 1e-6);
        var path = s.ShortestPath(F(box, left), F(box, right))!;
        Assert.Equal(3, path.Count);
        Assert.Equal(F(box, left), path[0]); Assert.Equal(F(box, right), path[^1]);
    }

    [AvaloniaFact(DisplayName = "SEL-H081 | Double-clicking the top face selects it with the fillet and the side it blends into")]
    [Trait("TestId", "SEL-H081")]
    public void DoubleClickTakesSmoothFaces()
    {
        var s = new MainViewModel().Session; var part = Part(s); s.Mode = SelMode.Face;
        s.ZoomExtents();
        var at = s.Camera.Project(new Vec3(12, 12, 20))!.Value;
        Assert.Equal(F(part, Top(part)), s.Pick(at.x, at.y, s.ActiveFilter)!.Value.Item);
        s.PointerMove(new PointerInfo(at.x, at.y, false, false, false, false));
        s.PointerDown(new PointerInfo(at.x, at.y, true, false, false, false, 2));
        s.PointerUp(new PointerInfo(at.x, at.y, false, false, false, false, 2));
        Assert.Equal(3, s.Selection.Count);
        Assert.Contains(F(part, Fillet(part)), s.Selection);
    }

    [AvaloniaFact(DisplayName = "SEL-H082 | Ctrl+Shift-click picks the path from the last face; the status bar counts the selection")]
    [Trait("TestId", "SEL-H082")]
    public void CtrlShiftClickPathAndSummary()
    {
        var vm = new MainViewModel(); var s = vm.Session; s.Mode = SelMode.Face;
        var box = Support.ModelingScenario.CreateBoxBody(s, Vec3.Zero, 10, 10, 10);
        s.ZoomExtents();
        int top = Face(box, (i, c) => i.Type == "Plane" && Math.Abs(c.Z - 10) < 1e-6);
        // a visible side: whichever side face the camera sees at its centre
        var side = Enumerable.Range(0, box.Mesh.FaceCount).Select(f => (f, c: s.ShapeOf(F(box, f))!.Props().Center))
            .Where(x => Math.Abs(x.c.Z - 5) < 1e-6).Select(x => (x.f, p: s.Camera.Project(x.c)))
            .First(x => x.p is { } p && s.Pick(p.x, p.y, s.ActiveFilter)?.Item == F(box, x.f));
        s.SetSelection(F(box, Face(box, (i, c) => i.Type == "Plane" && Math.Abs(c.Z) < 1e-6)), false);    // the bottom, hidden
        var (x, y, _) = side.p!.Value;
        s.PointerMove(new PointerInfo(x, y, false, true, true, false));
        s.PointerDown(new PointerInfo(x, y, true, true, true, false));
        s.PointerUp(new PointerInfo(x, y, false, true, true, false));
        Assert.Equal(2, s.Selection.Count);                // bottom and the side share an edge
        Assert.Equal(F(box, side.f), s.Selection[^1]);
        Assert.Equal("2 faces on 1 body", vm.SelectionSummary);
        _ = top;
    }
}
