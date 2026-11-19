using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Prism.Meshing;
using Xunit;
using static Prism.HeadlessTests.Mesh.MeshToolTests;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Mesh;

/// <summary>Draw Faces follows the rim and fits new faces to the surface (MSH-P03).</summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/mesh-and-reverse-engineer/README.md">Mesh guide, Drawing a repair.</seealso>
[Trait("Feature", "Mesh")]
[Trait("Layer", "Headless")]
public sealed class MeshDrawFacesRimTests
{
    /// <summary>A closed cylinder of radius 10 and height 40 with even triangles, minus the triangles the filter selects.</summary>
    static TriMesh Cylinder(Func<Vec3, bool> remove)
    {
        using var solid = Occ.Cylinder(10, 40);
        var mesh = MeshRemesh.Remesh(MeshOps.Weld(MeshAnalysis.FromMeshData(solid.Tessellate(0.005)), 1e-6), new MeshRemesh.Options(1.2, 6));
        var keep = Enumerable.Range(0, mesh.TriCount).Where(t =>
        {
            var (a, b, c) = mesh.Tri(t);
            return !remove((mesh.P(a) + mesh.P(b) + mesh.P(c)) / 3);
        });
        return mesh.Subset(keep);
    }

    /// <summary>Three rim vertices spread evenly along the only open loop.</summary>
    static Vec3[] ThreeRimCorners(TriMesh mesh)
    {
        var next = MeshLocalEdit.Boundary(mesh).ToDictionary(e => e.A, e => e.B);
        var loop = new List<int> { next.Keys.First() };
        while (next[loop[^1]] != loop[0]) loop.Add(next[loop[^1]]);
        return [mesh.P(loop[0]), mesh.P(loop[loop.Count / 3]), mesh.P(loop[2 * loop.Count / 3])];
    }

    static void Click(CadSession session, Command command, Vec3 world)
    {
        var screen = session.Camera.Project(world)!.Value;
        Assert.True(command.Tool!.PointerDown(session, new PointerInfo(screen.x, screen.y, true, false, false, false)));
    }

    static (CadSession, SceneObject, Command) Draw(TriMesh mesh, double yaw, double pitch, bool fit)
    {
        var session = new CadSession();
        var obj = AddMesh(session, mesh);
        session.Camera.SetView(yaw, pitch);
        session.ZoomExtents();
        var command = Begin(session, () => new MeshDrawFacesCommand());
        command.Params.OfType<BoolParam>().Single(p => p.Label == "Fit to surface").Value = fit;
        foreach (var corner in ThreeRimCorners(mesh)) Click(session, command, corner);
        Assert.True(command.Tool!.Key(session, "Enter"));
        BuildOutput(command);
        return (session, obj, command);
    }

    [AvaloniaFact(DisplayName = "MSH-H151 | Three corners on a rim follow it and stitch every rim edge; Undo restores the opening")]
    [Trait("TestId", "MSH-H151")]
    public void FollowRimStitchesWholeOpening()
    {
        var tube = Cylinder(c => c.Z > 39.99);
        int rimEdges = MeshLocalEdit.Boundary(tube).Length;
        Assert.True(rimEdges >= 12, $"{rimEdges} rim edges");
        var (session, obj, command) = Draw(tube, 0, 89.9, fit: false);
        Assert.True(command.Error == null, command.Error);
        Assert.Equal(rimEdges.ToString(), Result(command, "Joined edges"));
        Assert.Equal("0", Result(command, "Remaining open edges"));
        session.CommitCommand();
        Assert.Equal(0, session.Find(obj.Id)!.Tri!.Topology().BoundaryEdges);
        session.Undo();
        Assert.Equal(rimEdges, session.Find(obj.Id)!.Tri!.Topology().BoundaryEdges);
    }

    [AvaloniaFact(DisplayName = "MSH-H152 | Fit to surface bends a patch in a cylinder wall onto the cylinder instead of the flat chord")]
    [Trait("TestId", "MSH-H152")]
    public void FitToSurfaceFollowsCylinder()
    {
        var holed = Cylinder(c => c.X > 0 && Math.Abs(c.Y) < 4 && Math.Abs(c.Z - 20) < 4);
        var (session, obj, command) = Draw(holed, 90, 0, fit: true);
        Assert.True(command.Error == null, command.Error);
        Assert.Equal("0", Result(command, "Remaining open edges"));
        session.CommitCommand();
        var patched = session.Find(obj.Id)!.Tri!;
        Assert.Equal(0, patched.Topology().BoundaryEdges);
        var added = Enumerable.Range(holed.VertexCount, patched.VertexCount - holed.VertexCount).Select(patched.P).ToList();
        Assert.NotEmpty(added);
        // a flat patch across this 8 mm wide opening sags up to 0.84 mm inside the radius
        double worst = added.Max(p => Math.Abs(Math.Sqrt(p.X * p.X + p.Y * p.Y) - 10));
        Assert.True(worst < 0.15, $"largest radial error of the new points {worst:0.###} mm");
    }
}
