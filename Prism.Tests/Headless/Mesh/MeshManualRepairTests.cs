using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Prism.Meshing;
using Xunit;
using static Prism.HeadlessTests.Mesh.MeshToolTests;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Mesh;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/mesh-and-reverse-engineer/README.md">Local mesh editing guide.</seealso>
[Trait("Feature", "Mesh")]
[Trait("Layer", "Headless")]
public sealed class MeshManualRepairTests
{
    static TriMesh OpenBox()
    {
        var box = BoxMesh(Vec3.Zero, 20, 20, 20);
        return new TriMesh(box.V, box.T[12..]);
    }

    static void Click(CadSession session, Command command, Vec3 world, bool shift = false)
    {
        var screen = session.Camera.Project(world);
        Assert.NotNull(screen);
        Assert.True(command.Tool!.PointerDown(session, new PointerInfo(screen.Value.x, screen.Value.y, true, shift, false, false)));
    }

    [AvaloniaTheory(DisplayName = "MSH-H128 | Pointer-drawn faces stitch one opening and Undo restores both rims")]
    [Trait("TestId", "MSH-H128")]
    [InlineData(false)]
    [InlineData(true)]
    public void DrawFacesAppliesOneMeshEdit(bool drag)
    {
        var session = new CadSession();
        var box = AddMesh(session, OpenBox());
        session.Camera.SetView(0, 89.999);
        session.ZoomExtents();
        var command = Begin(session, () => new MeshDrawFacesCommand());
        Click(session, command, new(10, 0, 20), shift: true);
        if (drag)
        {
            var end = session.Camera.Project(new Vec3(10, 20, 20))!.Value;
            command.Tool!.PointerUp(session, new PointerInfo(end.x, end.y, true, true, false, false));
        }
        else
        {
            Click(session, command, new(0, 20, 20));
            Click(session, command, new(20, 20, 20));
            Assert.True(command.Tool!.Key(session, "Enter"));
        }
        Assert.True(command.Error == null, command.Error);
        Assert.Equal("4", Result(command, "Joined edges"));
        Assert.Equal(8, session.Find(box.Id)!.Tri!.TriCount);
        var overlay = new Overlay();
        command.BuildOverlay(session, overlay);
        Assert.Equal(2, overlay.Tris.Count);
        session.CommitCommand();
        Assert.Null(session.ActiveCommand);
        var updated = session.Find(box.Id)!.Tri!;
        Assert.Equal(10, updated.TriCount);
        var rim = Assert.Single(updated.Topology().HoleLoops);
        Assert.All(rim, i => Assert.Equal(0, updated.P(i).Z));
        session.Undo();
        Assert.Equal(2, session.Find(box.Id)!.Tri!.Topology().HoleLoops.Count);
    }

    [AvaloniaFact(DisplayName = "MSH-H134 | A closed mesh shows the purple draft and requires a replacement region to apply")]
    [Trait("TestId", "MSH-H134")]
    public void DraftRemainsVisibleWithoutStitchBoundary()
    {
        var session = new CadSession();
        var box = AddMesh(session, BoxMesh(Vec3.Zero, 20, 20, 20));
        session.Camera.SetView(0, 90);
        session.ZoomExtents();
        var command = Begin(session, () => new MeshDrawFacesCommand());
        Click(session, command, new(2, 2, 20));
        Click(session, command, new(18, 2, 20));
        Click(session, command, new(10, 18, 20));
        var overlay = new Overlay();
        command.Tool!.BuildOverlay(session, overlay);
        uint fill = Assert.Single(overlay.Tris).Color;
        Assert.True(command.Tool.Key(session, "Enter"));
        overlay.Clear();
        command.BuildOverlay(session, overlay);
        Assert.Equal(fill, Assert.Single(overlay.Tris).Color);
        session.CommitCommand();
        Assert.Same(command, session.ActiveCommand);
        Assert.Same(box, session.Find(box.Id));
        session.CancelCommand();

        session.SetSelection(new SelItem(box.Id, SelKind.MeshFace, 2), false);
        session.StartCommand(() => new MeshDrawFacesCommand());
        var replace = Assert.IsType<MeshDrawFacesCommand>(session.ActiveCommand);
        Assert.Equal("Replace selected region", replace.Params.OfType<ChoiceParam>()
            .Single(p => p.Label == "Patch boundary").Selected);
        Click(session, replace, new(0, 0, 20));
        Click(session, replace, new(20, 0, 20));
        Click(session, replace, new(20, 20, 20));
        Assert.True(replace.Tool!.Key(session, "Enter"));
        Assert.Equal("3", Result(replace, "Joined edges"));
        session.CommitCommand();
        Assert.Null(session.ActiveCommand);
        Assert.Equal(0, session.Find(box.Id)!.Tri!.Topology().BoundaryEdges);
    }

    [AvaloniaTheory(DisplayName = "MSH-H129 | Fill Hole uses only the rim picked in the viewport")]
    [Trait("TestId", "MSH-H129")]
    [InlineData("Flat")]
    [InlineData("Smooth")]
    public void FillHoleLeavesTheOtherRimOpen(string style)
    {
        var session = new CadSession();
        var box = AddMesh(session, OpenBox());
        session.Camera.SetView(0, 89.999);
        session.ZoomExtents();
        var command = Begin(session, () => new MeshBoundaryCommand());
        SetChoice(command, "Patch", style);
        Click(session, command, new Vec3(10, 0, 20));
        session.CommitCommand();
        Assert.Null(session.ActiveCommand);
        var result = session.Find(box.Id)!.Tri!;
        var rim = Assert.Single(result.Topology().HoleLoops);
        Assert.All(rim, i => Assert.Equal(0, result.P(i).Z));
    }

    [AvaloniaTheory(DisplayName = "MSH-H136 | Fill applies two toggled openings together and one Undo restores the original")]
    [Trait("TestId", "MSH-H136")]
    [InlineData("Flat")]
    [InlineData("Smooth")]
    public void FillSeveralOpenings(string style)
    {
        var session = new CadSession();
        var boxes = Enumerable.Range(0, 3).Select(i => BoxMesh(new Vec3(i * 30, 0, 0), 20, 20, 20)).ToArray();
        var open = new TriMesh(boxes.SelectMany(box => box.V).ToArray(),
            boxes.SelectMany((box, i) => box.T.Take(6).Concat(box.T.Skip(12)).Select(v => v + i * 8)).ToArray());
        var source = AddMesh(session, open);
        session.Camera.SetView(0, 90);
        session.Camera.Orthographic = true;
        session.ZoomExtents();

        var command = Begin(session, () => new MeshBoundaryCommand());
        SetChoice(command, "Patch", style);
        var left = new Vec3(10, 10, 20);
        var middleRim = new Vec3(40, 0, 20);
        Click(session, command, left);
        Click(session, command, middleRim);
        Assert.Equal("2", Result(command, "Selected openings"));
        Click(session, command, left);
        Assert.Equal("1", Result(command, "Selected openings"));
        command.Params.OfType<ActionParam>().Single(p => p.Label == "Clear picks").Run();
        Assert.DoesNotContain(command.Results, r => r.Label == "Selected openings");
        Click(session, command, left);
        Click(session, command, middleRim);
        Assert.Equal("2", Result(command, "Selected openings"));
        Assert.Same(open, session.Find(source.Id)!.Tri);

        session.CommitCommand();
        Assert.Null(session.ActiveCommand);
        var filled = session.Find(source.Id)!.Tri!;
        var remaining = Assert.Single(filled.Topology().HoleLoops);
        Assert.All(remaining, vertex => Assert.InRange(filled.P(vertex).X, 60, 80));
        session.Undo();
        Assert.Same(open, session.Find(source.Id)!.Tri);
        Assert.Equal(3, session.Find(source.Id)!.Tri!.Topology().HoleLoops.Count);
    }

    [AvaloniaFact(DisplayName = "MSH-H139 | One invalid patch blocks the entire Fill Hole batch")]
    [Trait("TestId", "MSH-H139")]
    public void InvalidPatchBlocksBatch()
    {
        var session = new CadSession();
        var first = BoxMesh(Vec3.Zero, 20, 20, 20);
        var second = BoxMesh(new Vec3(30, 0, 0), 20, 20, 20);
        var open = new TriMesh(
            [.. first.V, .. second.V, 34, 10, 18, 46, 10, 18, 40, 10, 24],
            [.. first.T.Take(6), .. first.T.Skip(12),
                .. second.T.Take(6).Concat(second.T.Skip(12)).Select(i => i + 8), 16, 17, 18]);
        var source = AddMesh(session, open);
        session.Camera.SetView(0, 90);
        session.Camera.Orthographic = true;
        session.ZoomExtents();
        var command = Begin(session, () => new MeshBoundaryCommand());
        Click(session, command, new Vec3(10, 10, 20));
        Assert.Null(command.Error);
        Click(session, command, new Vec3(40, 0, 20));
        Assert.Equal("2", Result(command, "Selected openings"));
        Assert.NotNull(command.Error);
        session.CommitCommand();
        Assert.Same(command, session.ActiveCommand);
        Assert.Same(open, session.Find(source.Id)!.Tri);
    }

    [AvaloniaFact(DisplayName = "MSH-H137 | Fill Hole skips an opening occluded by a visible surface")]
    [Trait("TestId", "MSH-H137")]
    public void FillHoleSkipsCoveredOpening()
    {
        var session = new CadSession();
        var tube = AddMesh(session, OpenBox(), "Tube");
        AddMesh(session, new TriMesh([-2, -2, 30, 22, -2, 30, 22, 22, 30, -2, 22, 30], [0, 1, 2, 0, 2, 3]), "Cover");
        session.SelectObjects([tube.Id]);
        session.Camera.SetView(0, 90);
        session.Camera.Orthographic = true;
        session.ZoomExtents();
        session.StartCommand(() => new MeshBoundaryCommand());
        var command = Assert.IsType<MeshBoundaryCommand>(session.ActiveCommand);
        Click(session, command, new Vec3(10, 10, 20));
        Click(session, command, new Vec3(10, 0, 20));
        Assert.DoesNotContain(command.Results, r => r.Label == "Selected openings");
        session.CommitCommand();
        Assert.Same(command, session.ActiveCommand);
        Assert.Same(tube, session.Find(tube.Id));
    }

    [AvaloniaFact(DisplayName = "MSH-H138 | Draw Faces tints the visible triangle and snaps to its exact vertex or edge")]
    [Trait("TestId", "MSH-H138")]
    public void DrawFacesSnapTargets()
    {
        var session = new CadSession();
        var visible = AddMesh(session, new TriMesh([0, 0, 10, 40, 0, 10, 40, 40, 10, 0, 40, 10], [0, 1, 2, 0, 2, 3]), "Front");
        var hidden = AddMesh(session, new TriMesh([10, 15, 0, 20, 15, 0, 10, 25, 0], [0, 1, 2]), "Behind");
        session.Camera.SetView(0, 90);
        session.Camera.Orthographic = true;
        session.ZoomExtents();
        session.SelectObjects([visible.Id]);
        session.StartCommand(() => new MeshDrawFacesCommand());
        var command = Assert.IsType<MeshDrawFacesCommand>(session.ActiveCommand);

        (Overlay Face, Overlay Snap) Hover(Vec3 world)
        {
            var screen = session.Camera.Project(world)!.Value;
            command.Tool!.PointerMove(session, new PointerInfo(screen.x, screen.y, false, false, false, false));
            var face = new Overlay();
            var snap = new Overlay();
            command.BuildOverlay(session, face);
            command.Tool.BuildOverlay(session, snap);
            return (face, snap);
        }
        void AssertPoint(Vec3 expected, Vec3 actual) =>
            Assert.InRange(expected.DistanceTo(actual), 0, 1e-4);

        var surface = Hover(new Vec3(10, 15, 10));
        var face = Assert.Single(surface.Face.Tris);
        Assert.All(new[] { face.A, face.B, face.C }, p => Assert.Equal(10, p.Z));
        var surfaceMarker = Assert.Single(surface.Snap.Dots);
        AssertPoint(new Vec3(10, 15, 10), surfaceMarker.P);

        var edge = Hover(new Vec3(20.2, 19.8, 10));
        var edgeMarker = Assert.Single(edge.Snap.Dots);
        AssertPoint(new Vec3(20, 20, 10), edgeMarker.P);
        Assert.Single(edge.Snap.Lines, line => line.Color == edgeMarker.Color);
        Assert.NotEqual(surfaceMarker.Color, edgeMarker.Color);

        var vertex = Hover(new Vec3(.3, .3, 10));
        var vertexMarker = Assert.Single(vertex.Snap.Dots);
        Assert.Equal(new Vec3(0, 0, 10), vertexMarker.P);
        Assert.NotEqual(edgeMarker.Color, vertexMarker.Color);
        Click(session, command, new Vec3(.3, .3, 10));
        var afterClick = new Overlay();
        command.Tool!.BuildOverlay(session, afterClick);
        Assert.Contains(afterClick.Dots, dot => dot.P == vertexMarker.P && dot.Size == 7);

        session.CancelCommand();
        session.SelectObjects([hidden.Id]);
        session.StartCommand(() => new MeshDrawFacesCommand());
        command = Assert.IsType<MeshDrawFacesCommand>(session.ActiveCommand);
        var covered = Hover(new Vec3(10, 15, 10));
        Assert.Empty(covered.Face.Tris);
        Assert.Equal(surfaceMarker.Color, Assert.Single(covered.Snap.Dots).Color);

        session.CancelCommand();
        session.SelectObjects([visible.Id]);
        session.StartCommand(() => new MeshDrawFacesCommand());
        command = Assert.IsType<MeshDrawFacesCommand>(session.ActiveCommand);
        SetToggle(command, "Snap to mesh", false);
        var plane = Hover(new Vec3(20, .3, 10));
        AssertPoint(new Vec3(20, .3, 0), Assert.Single(plane.Snap.Dots).P);
        command.Tool!.PointerExit(session);
        plane.Face.Clear();
        command.BuildOverlay(session, plane.Face);
        Assert.Empty(plane.Face.Tris);
    }

}
