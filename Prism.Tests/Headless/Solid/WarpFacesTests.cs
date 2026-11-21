using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Solid;

/// <summary>Warp limited to chosen faces of a solid: the outline of each face stays put and the other faces are untouched. Direction approved 2026-11-16.</summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/modeling/direct-modeling/README.md">Direct modeling guide.</seealso>
[Trait("Feature", "Solid")]
[Trait("Layer", "Headless")]
public class WarpFacesTests
{
    const string FacesSlot = "Faces to warp (instead of the whole body)";

    static WarpCommand BendFace(CadSession session, SceneObject body, int face, double angle)
    {
        var command = Begin(session, () => new WarpCommand());
        Pick(command, FacesSlot, new SelItem(body.Id, SelKind.Face, face));
        SetChoice(command, "Warp", "Bend");
        SetChoice(command, "Along the axis", "X");
        SetChoice(command, "Bend toward", "+Z");
        SetNumber(command, "Angle", angle);
        return command;
    }

    static int Face(SceneObject body, Func<Vec3, bool> pick) =>
        Enumerable.Range(0, body.Shape.Count(SubKind.Face)).First(i => pick(Occ.FaceInfoOf(body.Shape.Sub(SubKind.Face, i)).Normal));

    [AvaloniaFact(DisplayName = "WRP-H010 | Bending only the top face of a box gives a valid solid with the same width and depth")]
    [Trait("TestId", "WRP-H010")]
    public void OnlyTheChosenFaceMoves()
    {
        var session = new CadSession();
        var body = CreateBoxBody(session, Vec3.Zero, 40, 40, 20);
        var command = BendFace(session, body, Face(body, n => n.Z > 0.99), 40);
        var output = command.TryBuild();
        Assert.True(output != null, command.Error);
        using var result = Assert.Single(output.Replace).New;
        AssertValidSolid(result);
        var props = result.Props();
        AssertNear(40, props.Size.X, "Width (mm)");
        AssertNear(40, props.Size.Y, "Depth (mm)");
        Assert.True(Math.Abs(props.Volume - 32000) > 1, "the bent face changes the volume");
        Assert.NotEqual(20, props.Max.Z, 2);                                // the face really did move off its plane
        Assert.Equal(0, props.Min.Z, 6);                                    // the bottom face is untouched
    }

    [AvaloniaFact(DisplayName = "WRP-H011 | Warping single faces refuses curved faces and faces that are not four-sided, leaving the body alone")]
    [Trait("TestId", "WRP-H011")]
    public void UnsupportedFacesAreRefused()
    {
        var session = new CadSession();
        using var cylinder = Occ.Cylinder(10, 20);
        var round = session.AddObject(Occ.Translate(cylinder, Vec3.Zero), "Round", ObjKind.Solid);
        int side = Enumerable.Range(0, round.Shape.Count(SubKind.Face)).First(i => Occ.FaceInfoOf(round.Shape.Sub(SubKind.Face, i)).Type != "Plane");
        var curved = BendFace(session, round, side, 30);
        Assert.Null(curved.TryBuild());
        Assert.Contains("flat faces", curved.Error);
        int cap = Enumerable.Range(0, round.Shape.Count(SubKind.Face)).First(i => Occ.FaceInfoOf(round.Shape.Sub(SubKind.Face, i)).Type == "Plane");
        var disc = BendFace(session, round, cap, 30);
        Assert.Null(disc.TryBuild());
        Assert.Contains("four sides", disc.Error);
    }

    [AvaloniaFact(DisplayName = "WRP-H012 | Without picking faces, Warp still warps the whole body as before")]
    [Trait("TestId", "WRP-H012")]
    public void WholeBodyWarpIsUnchanged()
    {
        var session = new CadSession();
        var body = CreateBoxBody(session, Vec3.Zero, 40, 40, 20);
        var command = Begin(session, () => new WarpCommand());
        Pick(command, "What to warp", body);
        SetChoice(command, "Warp", "Stretch (lengthen or shorten)");
        SetChoice(command, "Along the axis", "X");
        SetNumber(command, "Amount", 50);
        var output = command.TryBuild();
        Assert.True(output != null, command.Error);
        using var result = Assert.Single(output.Replace).New;
        AssertNear(60, result.Props().Size.X, "Stretched width (mm)");
    }
}
