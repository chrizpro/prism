using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Surfacing;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/surface-and-nurbs/README.md#rebuild-surface-of-several-faces">Rebuild Surface of several faces (RBD-P1).</seealso>
[Trait("Feature", "Surfacing")]
[Trait("Layer", "Headless")]
public class RebuildSeveralFacesTests
{
    static SceneObject Sheet(CadSession session, params Vec3[] corners)
    {
        using var boundary = Occ.Polyline(corners, true);
        return session.AddObject(Occ.Profile([boundary], true), "Sheet", ObjKind.Surface);
    }

    [AvaloniaFact(DisplayName = "SRF-H037 | Rebuild Surface rebuilds a sewn two-face corner one patch per face and joins them again")]
    [Trait("TestId", "SRF-H037")]
    public void TwoFaceCornerStaysJoined()
    {
        var session = new CadSession();
        var floor = Sheet(session, new(0, 0, 0), new(10, 0, 0), new(10, 10, 0), new(0, 10, 0));
        var wall = Sheet(session, new(10, 0, 0), new(10, 0, 10), new(10, 10, 10), new(10, 10, 0));
        var sew = Begin(session, () => new SewCommand());
        Pick(sew, "Surfaces", floor, wall);
        var corner = Assert.Single(BuildOutput(sew).Add).Shape!;
        Assert.Equal(2, corner.Count(SubKind.Face));
        var body = session.AddObject(corner, "Corner", ObjKind.Surface);

        var command = Begin(session, () => new RebuildSurfaceCommand());
        Pick(command, "Surface or face", body);
        SetInteger(command, "Points in U", 5); SetInteger(command, "Points in V", 5);
        var rebuilt = SingleSurface(BuildOutput(command));
        Assert.Equal(2, rebuilt.Count(SubKind.Face));
        AssertClose(200, rebuilt.Props().Area, 0.5, "Area (mm²)");
        Assert.Contains("2, joined to 2", Result(command, "Faces rebuilt"));
        var open = Result(command, "Open edges").Split("→", StringSplitOptions.TrimEntries);
        Assert.Equal(open[0], open[1]);
        Assert.Equal(Occ.FreeEdges(corner).Length, Occ.FreeEdges(rebuilt).Length);
    }

    static Shape SingleSurface(CommandOutput output) => Assert.Single(output.Add).Shape!;
}
