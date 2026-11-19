using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Core.Sketching;
using Prism.Kernel;
using Prism.Meshing;
using Xunit;
using static Prism.HeadlessTests.Mesh.MeshToolTests;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Mesh;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/mesh-and-reverse-engineer/README.md">Mesh and Section Sketch guide.</seealso>
[Trait("Feature", "Mesh")]
[Trait("Layer", "Headless")]
public sealed class MeshSectionAndSelectionTests
{
    static PointerInfo Screen(CadSession session, Vec3 target)
    {
        var p = session.Camera.Project(target)!.Value;
        return new PointerInfo(p.x, p.y, true, false, false, false);
    }

    [AvaloniaFact(DisplayName = "MSH-H130 | Apply two translated/tilted sections, then Done without creating a duplicate")]
    [Trait("TestId", "MSH-H130")]
    public void MovableSectionAppliesAtEachPose()
    {
        var session = new CadSession();
        var mesh = AddMesh(session, BoxMesh(Vec3.Zero, 50.3, 30, 20));
        var command = Begin(session, () => new MeshSectionSketchPanelCommand());
        Assert.Equal("Done", command.OkText);
        Assert.NotNull(command.Gizmo);
        var apply = command.Params.OfType<ActionParam>().Single(a => a.Label == "Apply section");
        SetNumber(command, "Move Z", 40);
        apply.Run();
        Assert.DoesNotContain(session.Objects, o => o.Kind == ObjKind.Sketch);
        SetNumber(command, "Move Z", -4);
        apply.Run();
        var first = Assert.Single(session.Objects, o => o.Kind == ObjKind.Sketch);
        AssertClose(6, first.SketchPlane!.Value.Origin.Z, 1e-8, "first section height");
        Assert.NotEmpty(first.SketchData!.MeshSectionTraces);
        Assert.Same(command, session.ActiveCommand);
        SetNumber(command, "Move Z", 3);
        SetNumber(command, "Rotate X", 15);
        apply.Run();
        var second = session.Objects.Last(o => o.Kind == ObjKind.Sketch);
        Assert.NotEqual(first.Id, second.Id);
        AssertClose(13, second.SketchPlane!.Value.Origin.Z, 1e-8, "second section height");
        AssertClose(Math.Cos(Math.PI / 12), second.SketchPlane.Value.Normal.Z, 1e-8, "tilted normal");
        session.CommitCommand();
        Assert.Null(session.ActiveCommand);
        Assert.Equal(2, session.Objects.Count(o => o.Kind == ObjKind.Sketch));
        session.Undo();
        Assert.Null(session.Find(second.Id));
        Assert.NotNull(session.Find(first.Id));
        Assert.Same(mesh, session.Find(mesh.Id));
    }

    [AvaloniaFact(DisplayName = "MSH-H131 | Refit a measured span, drive it to 50 mm, and keep the 50.3 mm raw reference through save and reopen")]
    [Trait("TestId", "MSH-H131")]
    public void SectionFitAndNominalDimensionKeepRawTrace()
    {
        var session = new CadSession();
        AddMesh(session, BoxMesh(Vec3.Zero, 50.3, 30, 20));
        var command = Begin(session, () => new MeshSectionSketchPanelCommand());
        command.Params.OfType<ActionParam>().Single().Run();
        session.CommitCommand();
        var sketch = Assert.Single(session.Objects, o => o.Kind == ObjKind.Sketch);
        Assert.True(session.EditSketch(sketch));
        var sk = session.Sketch!;
        session.Camera.SetView(0, 90);
        session.Camera.Orthographic = true;
        session.Camera.Target = sk.Plane.Origin;
        session.Camera.Distance = 80;
        var model = sk.Model;
        var trace = Assert.Single(model.MeshSectionTraces);
        double measured = trace.Points.Max(p => p.X) - trace.Points.Min(p => p.X);
        AssertClose(50.3, measured, 1e-4, "raw width");
        var line = model.Entities.First(e => e.Type == EntType.Line && Math.Abs(model.P(e.A).X - model.P(e.B).X) > 50);
        var a = model.P(line.A); var b = model.P(line.B);
        int[] ends = [line.A, line.B];
        sk.SelEntities.Add(line.Id);
        var tool = new FitMeshSectionTool();
        sk.SetTool(session, tool);
        tool.SetOption(session, 0);
        Assert.True(tool.PointerDown(session, Screen(session, sk.Plane.PointAt(a.X * .9 + b.X * .1, a.Y))));
        Assert.True(tool.PointerDown(session, Screen(session, sk.Plane.PointAt(b.X * .9 + a.X * .1, b.Y))));
        Assert.Null(model.EntityOrNull(line.Id));
        var replacement = model.Entities.Last();
        Assert.Equal(ends, new[] { replacement.A, replacement.B });
        model.Add(CType.Distance, 50, dimension: true, refs: [replacement.A, replacement.B]);
        Assert.True(sk.Solve(session).Converged);
        var p0 = model.P(replacement.A); var p1 = model.P(replacement.B);
        AssertClose(50, Math.Sqrt(Math.Pow(p1.X - p0.X, 2) + Math.Pow(p1.Y - p0.Y, 2)), 1e-4, "nominal length");
        session.ExitSketch(true);

        string path = Path.Combine(Path.GetTempPath(), $"prism-section-{Guid.NewGuid():N}.prism");
        try
        {
            session.Save(path);
            var reopened = new CadSession();
            reopened.Open(path);
            var saved = Assert.Single(reopened.Objects, o => o.Kind == ObjKind.Sketch).SketchData!;
            Assert.Equal(trace.Points, Assert.Single(saved.MeshSectionTraces).Points);
            Assert.Contains(saved.Constraints, c => c.IsDimension && c.Value == 50);
        }
        finally { File.Delete(path); }
    }

    [AvaloniaFact(DisplayName = "MSH-H132 | Shared Face mode picks one colored mesh triangle; Delete and Undo preserve its mesh")]
    [Trait("TestId", "MSH-H132")]
    public void SelectAndDeleteOneMeshTriangle()
    {
        var session = new CadSession();
        var tri = new TriMesh([0, 0, 0, 20, 0, 0, 20, 20, 0, 0, 20, 0], [0, 1, 2, 0, 2, 3],
            faceColors: [0xffff0000, 0xff00ff00]);
        var mesh = AddMesh(session, tri);
        session.Camera.SetView(0, 90);
        session.Camera.Orthographic = true;
        session.Camera.Target = new Vec3(10, 10, 0);
        session.Camera.Distance = 50;
        var screen = Screen(session, new Vec3(5, 15, 0));
        session.Mode = SelMode.Face;
        var picked = session.Pick(screen.X, screen.Y, session.ActiveFilter)!.Value.Item;
        Assert.Equal(new SelItem(mesh.Id, SelKind.MeshFace, 1), picked);
        Assert.Null(session.Pick(screen.X, screen.Y, SelFilter.Face));
        Assert.Equal(SelItem.Of(mesh), session.Pick(screen.X, screen.Y, SelFilter.MeshObj)!.Value.Item);
        var rectangle = RegionSelect.Region.Box(screen.X + 6, screen.Y - 6, screen.X - 6, screen.Y + 6);
        session.ApplyRegion(rectangle, screen);
        Assert.Equal(picked, Assert.Single(session.Selection));
        session.DeleteSelection();
        var updated = session.Find(mesh.Id)!;
        Assert.Equal(mesh.Name, updated.Name);
        Assert.Equal(1, updated.Tri!.TriCount);
        Assert.Equal(new uint[] { 0xffff0000 }, updated.Tri.FaceColors);
        Assert.Empty(session.Selection);
        session.Undo();
        Assert.Equal(2, session.Find(mesh.Id)!.Tri!.TriCount);
    }
}
