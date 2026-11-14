using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Surfacing;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/surface-and-nurbs/README.md#1-surface-blending-and-fillets">0.4 modeling gaps, item 1 (BLD-P2).</seealso>
[Trait("Feature", "Surfacing")]
[Trait("Layer", "Headless")]
public class MatchToEdgeTests
{
    /// <summary>A cubic-by-linear patch whose four rows run along X from x0, at height z0 rising by rise.</summary>
    static SceneObject Strip(CadSession session, double x0, double z0, double rise)
    {
        var grid = new List<Vec3>();
        for (int row = 0; row < 4; row++)
        {
            double t = row / 3.0;
            grid.Add(new(x0 + 10 * t, 0, z0 + rise * t));
            grid.Add(new(x0 + 10 * t, 10, z0 + rise * t));
        }
        return session.AddObject(Occ.NurbsSurface([.. grid], 4, 2, 3, 1), "Source", ObjKind.Surface);
    }

    [AvaloniaTheory(DisplayName = "SRF-H036 | Match Surface fits a patch to the edge of a solid's top face at every continuity")]
    [InlineData("Position (G0)")]
    [InlineData("Tangent (G1)")]
    [InlineData("Curvature (G2)")]
    [Trait("TestId", "SRF-H036")]
    public void MatchesTheEdgeOfASolidFace(string continuity)
    {
        var session = new CadSession();
        var box = CreateBoxBody(session, new(0, 0, 0), 10, 10, 10);
        var source = Strip(session, 12, 10.5, 3);
        var command = Begin(session, () => new MatchSurfaceCommand());
        Pick(command, "Surface to change", source);
        Pick(command, "Reference surface or face", FaceFacing(box, new(0, 0, 1)));
        SetChoice(command, "Source boundary", "U min");
        SetChoice(command, "Reference boundary", "Nearest edge (any face)");
        SetChoice(command, "Continuity", continuity);
        var (old, matched) = Assert.Single(BuildOutput(command).Replace);
        Assert.Equal(source.Id, old.Id);
        AssertClose(10, matched.Props().Min.X, 1e-6, "Seam moved onto the box edge (mm)");
        Assert.Contains("Fitted", Result(command, "Method"));
        AssertClose(0, double.Parse(Result(command, "Sampled gap").Replace(" mm", ""), System.Globalization.CultureInfo.InvariantCulture), 1e-6, "Gap (mm)");
        if (continuity != "Position (G0)")
            AssertClose(0, double.Parse(Result(command, "Sampled normal angle").Replace("°", ""), System.Globalization.CultureInfo.InvariantCulture), 1e-6, "Normal angle (degrees)");
    }

    [AvaloniaFact(DisplayName = "SRF-H037 | Match Surface follows a curved edge and continues the flat face at G2")]
    [Trait("TestId", "SRF-H037")]
    public void FollowsACircularEdge()
    {
        var session = new CadSession();
        var cylinder = session.AddObject(CylinderAt(new(0, 0, 0), 10, 10), "Drum", ObjKind.Solid);
        // four rows leave the rim outward and upward; the four columns follow an arc of the rim
        var grid = new List<Vec3>();
        for (int row = 0; row < 4; row++)
            for (int col = 0; col < 4; col++)
            {
                double angle = -0.4 + 0.8 * col / 3.0, radius = 11 + 1.5 * row;
                grid.Add(new(radius * Math.Cos(angle), radius * Math.Sin(angle), 10.8 + 0.7 * row));
            }
        using var source = Occ.NurbsSurface([.. grid], 4, 4, 3, 3);
        using var reference = cylinder.Shape.Sub(SubKind.Face, FaceFacing(cylinder, new(0, 0, 1)).Index);
        var fit = Occ.MatchSurfaceToEdge(source, 0, reference, 2);
        using var result = fit.Result;
        Assert.True(fit.Gap < 1e-3, $"Gap {fit.Gap} mm");
        Assert.True(fit.NormalAngle < 0.05, $"Normal angle {fit.NormalAngle}°");
        Assert.True(fit.CurvatureDifference < 1e-3, $"Curvature difference {fit.CurvatureDifference} /mm");
        Assert.True(fit.Rows > 4, "The seam needed extra control points to follow the circle.");
        // the matched edge lies on the rim: radius 10, height 10
        Assert.True(result.Props().Min.Z >= 9.999, $"Lowest point {result.Props().Min.Z}");
    }

    [AvaloniaFact(DisplayName = "SRF-H038 | Match Surface explains when no reference edge runs along the boundary")]
    [Trait("TestId", "SRF-H038")]
    public void RefusesAFarAwayReference()
    {
        var session = new CadSession();
        var box = CreateBoxBody(session, new(0, 0, 0), 10, 10, 10);
        var source = Strip(session, 60, 10, 0);
        var command = Begin(session, () => new MatchSurfaceCommand());
        Pick(command, "Surface to change", source);
        Pick(command, "Reference surface or face", FaceFacing(box, new(0, 0, 1)));
        SetChoice(command, "Source boundary", "U min");
        SetChoice(command, "Reference boundary", "Nearest edge (any face)");
        AssertBuildFails(command, "No edge of the reference");
    }
}
