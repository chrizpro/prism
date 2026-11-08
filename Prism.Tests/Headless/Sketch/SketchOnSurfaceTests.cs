using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Prism.Automation;
using Prism.Core;
using Prism.Kernel;
using Prism.ViewModels;
using Xunit;

namespace Prism.HeadlessTests.Sketch;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/sketches/README.md">Sketches.</seealso>
[Trait("Feature", "Sketch")]
[Trait("Layer", "Headless")]
public class SketchOnSurfaceTests
{
    [AvaloniaFact(DisplayName = "SKT-H020 | Curves projected onto a curved face land on the face")]
    [Trait("TestId", "SKT-H020")]
    public void ProjectedCurvesLieOnTheSurface()
    {
        var s = new CadSession();
        var sphere = s.AddObject(Occ.Sphere(20), "Ball", ObjKind.Solid);
        using var ring = Occ.Circle(new Vec3(0, 0, 30), Vec3.UnitZ, 6);
        using var projected = s.ProjectOntoFace(ring, sphere.Id, 0, -Vec3.UnitZ);
        var m = projected.Tessellate();
        Assert.True(m.EdgePoints.Length >= 9);
        for (int i = 0; i + 2 < m.EdgePoints.Length; i += 3)
            Assert.InRange(new Vec3(m.EdgePoints[i], m.EdgePoints[i + 1], m.EdgePoints[i + 2]).Length, 19.9, 20.1);
        Assert.Throws<InvalidOperationException>(() => s.ProjectOntoFace(Occ.Circle(new Vec3(100, 0, 30), Vec3.UnitZ, 3), sphere.Id, 0, -Vec3.UnitZ));
    }

    [AvaloniaFact(DisplayName = "SKT-H021 | Starting a sketch on a curved face arms projection onto it; a flat face does not")]
    [Trait("TestId", "SKT-H021")]
    public async Task CurvedFaceArmsProjection()
    {
        var vm = new MainViewModel();
        var api = new AutomationApi(vm, null);
        var s = vm.Session;
        s.AddObject(Occ.Sphere(20), "Ball", ObjKind.Solid);
        await api.CallAsync("run_command", (JsonObject)JsonNode.Parse("{\"name\":\"create_sketch\",\"slots\":{\"Plane or flat face\":[{\"object\":\"Ball 1\",\"face\":1}]}}")!);
        Assert.NotNull(s.Sketch);
        Assert.NotNull(s.SketchSurface);
        s.ExitSketch(false);
        Assert.Null(s.SketchSurface);
        var box = s.AddObject(Occ.Box(10, 10, 10), "Block", ObjKind.Solid);
        await api.CallAsync("run_command", (JsonObject)JsonNode.Parse("{\"name\":\"create_sketch\",\"slots\":{\"Plane or flat face\":[{\"object\":\"Block 1\",\"face\":1}]}}")!);
        Assert.NotNull(s.Sketch);
        Assert.Null(s.SketchSurface);
        s.ExitSketch(false);
    }
}
