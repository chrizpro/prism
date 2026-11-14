using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Surfacing;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/surface-and-nurbs/README.md#1-surface-blending-and-fillets-lead-item">0.4 modeling gaps, item 1 (BLD-P3).</seealso>
[Trait("Feature", "Surfacing")]
[Trait("Layer", "Headless")]
public class FilletAcrossSurfacesTests
{
    static SceneObject Sheet(CadSession session, params Vec3[] corners)
    {
        using var boundary = Occ.Polyline(corners, true);
        return session.AddObject(Occ.Profile([boundary], true), "Sheet", ObjKind.Surface);
    }

    /// <summary>The 10 mm edge of a sheet that runs along X at the given y and z.</summary>
    static SelItem EdgeAlongY(SceneObject sheet, double x, double z) =>
        Assert.Single(EdgesWhere(sheet, e => Math.Abs(e.Min.X - x) < 1e-6 && Math.Abs(e.Max.X - x) < 1e-6 && Math.Abs(e.Min.Z - z) < 1e-6 && Math.Abs(e.Max.Z - z) < 1e-6 && e.Max.Y - e.Min.Y > 5));

    static (CommandOutput output, SceneObject floor, SceneObject wall) Round(double wallX, string corners = "Fillet (round)")
    {
        var session = new CadSession();
        var floor = Sheet(session, new(0, 0, 0), new(10, 0, 0), new(10, 10, 0), new(0, 10, 0));
        var wall = Sheet(session, new(wallX, 0, 0), new(wallX, 10, 0), new(wallX, 10, 10), new(wallX, 0, 10));
        var command = Begin(session, () => new CurveFilletCommand(false));
        Pick(command, "Flat curve or sketch, or edges of a surface", EdgeAlongY(floor, 10, 0), EdgeAlongY(wall, wallX, 0));
        SetChoice(command, "Corners", corners);
        SetNumber(command, "Fillet radius", 2);
        SetNumber(command, "Chamfer distance", 2);
        return (BuildOutput(command), floor, wall);
    }

    [AvaloniaFact(DisplayName = "SRF-H033 | Fillet rounds the corner between two unsewn surfaces that meet, and joins them into one")]
    [Trait("TestId", "SRF-H033")]
    public void FilletBetweenTwoSeparateSurfaces()
    {
        var (output, floor, wall) = Round(10);
        Assert.Contains(floor, output.Remove); Assert.Contains(wall, output.Remove);
        var surface = Assert.Single(output.Add).Shape!;
        Assert.True(surface.IsValid);
        // each sheet loses a 2 mm strip (80 mm² left of 100) and a quarter cylinder of radius 2 and length 10 joins them
        AssertClose(80 + 80 + Math.PI / 2 * 2 * 10, surface.Props().Area, 0.05, "Rounded surface area (mm²)");
    }

    [AvaloniaFact(DisplayName = "SRF-H034 | Chamfer cuts the same corner with a flat strip")]
    [Trait("TestId", "SRF-H034")]
    public void ChamferBetweenTwoSeparateSurfaces()
    {
        var (output, _, _) = Round(10, "Chamfer (cut)");
        var surface = Assert.Single(output.Add).Shape!;
        Assert.True(surface.IsValid);
        AssertClose(80 + 80 + Math.Sqrt(8) * 10, surface.Props().Area, 0.05, "Chamfered surface area (mm²)");
    }

    [AvaloniaFact(DisplayName = "SRF-H035 | Edges of two surfaces that do not touch are refused with a message")]
    [Trait("TestId", "SRF-H035")]
    public void FilletRefusesSurfacesThatDoNotMeet()
    {
        var session = new CadSession();
        var floor = Sheet(session, new(0, 0, 0), new(10, 0, 0), new(10, 10, 0), new(0, 10, 0));
        var wall = Sheet(session, new(15, 0, 0), new(15, 10, 0), new(15, 10, 10), new(15, 0, 10));
        var command = Begin(session, () => new CurveFilletCommand(false));
        Pick(command, "Flat curve or sketch, or edges of a surface", EdgeAlongY(floor, 10, 0), EdgeAlongY(wall, 15, 0));
        AssertBuildFails(command, "do not meet");
    }
}
