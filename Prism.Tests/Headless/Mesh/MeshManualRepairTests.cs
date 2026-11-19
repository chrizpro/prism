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
}
