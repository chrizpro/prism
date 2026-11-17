using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.DirectModeling;

/// <summary>Sculpt Face: a flat four-sided face of a solid reshaped in place with its outline fixed. Direction approved 2026-11-16.</summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/modeling/direct-modeling/README.md">Direct modeling guide.</seealso>
[Trait("Feature", "DirectModeling")]
[Trait("Layer", "Headless")]
public class SculptFaceTests
{
    const string FaceSlot = "Flat face to sculpt";

    static int FaceFacing(SceneObject body, Func<Vec3, bool> pick) =>
        Enumerable.Range(0, body.Shape.Count(SubKind.Face)).First(i => pick(Occ.FaceInfoOf(body.Shape.Sub(SubKind.Face, i)).Normal));

    static SculptFaceCommand Start(CadSession session, SceneObject body, int face, double height, double? offset = null)
    {
        var command = Begin(session, () => new SculptFaceCommand());
        Pick(command, FaceSlot, new SelItem(body.Id, SelKind.Face, face));
        command.ConfigureInputs(() =>
        {
            SetNumber(command, "Height (negative pushes in)", height);
            if (offset is { } o) SetNumber(command, "Move the bump across", o);
        });
        return command;
    }

    [AvaloniaFact(DisplayName = "DIR-H072 | Sculpt Face raises a bump on a box top: a valid, taller solid with the same outline")]
    [Trait("TestId", "DIR-H072")]
    public void BumpKeepsOutline()
    {
        var session = new CadSession();
        var body = CreateBoxBody(session, Vec3.Zero, 40, 40, 20);
        var output = Start(session, body, FaceFacing(body, n => n.Z > 0.99), 4).TryBuild();
        Assert.NotNull(output);
        using var result = Assert.Single(output.Replace).New;
        AssertValidSolid(result);
        var props = result.Props();
        Assert.InRange(props.Volume, 32000 + 1, 32000 + 40 * 40 * 4);
        Assert.InRange(props.Max.Z, 21, 24.5);
        AssertNear(40, props.Size.X, "Width (mm)");
        AssertNear(40, props.Size.Y, "Depth (mm)");
    }

    [AvaloniaFact(DisplayName = "DIR-H073 | A negative height pushes the face in and removes volume")]
    [Trait("TestId", "DIR-H073")]
    public void NegativeHeightDents()
    {
        var session = new CadSession();
        var body = CreateBoxBody(session, Vec3.Zero, 40, 40, 20);
        var output = Start(session, body, FaceFacing(body, n => n.X > 0.99), -3).TryBuild();
        Assert.NotNull(output);
        using var result = Assert.Single(output.Replace).New;
        AssertValidSolid(result);
        Assert.InRange(result.Props().Volume, 32000 - 40 * 20 * 3, 32000 - 1);
        AssertNear(40, result.Props().Size.X, "Width stays (mm)");
    }

    [AvaloniaFact(DisplayName = "DIR-H074 | Moving the bump across the face moves where the surface is highest")]
    [Trait("TestId", "DIR-H074")]
    public void BumpCanBeMoved()
    {
        var session = new CadSession();
        var body = CreateBoxBody(session, Vec3.Zero, 40, 40, 20);
        int top = FaceFacing(body, n => n.Z > 0.99);
        // "across" on a top face runs along Y: the centre of gravity slides with the bump, in opposite directions for opposite offsets
        double Cog(double offset)
        {
            using var result = Assert.Single(Start(session, body, top, 4, offset).TryBuild()!.Replace).New;
            return Occ.Mass(result).Cog.Y;
        }
        double low = Cog(-8), high = Cog(8);
        Assert.True(Math.Abs(low - high) > 0.05, $"the bump did not move (centre of gravity Y {low:0.###} and {high:0.###})");
    }

    [AvaloniaFact(DisplayName = "DIR-H077 | Sculpt Face offers viewport handles for height, radius and the two ways of sliding the bump")]
    [Trait("TestId", "DIR-H077")]
    public void ViewportHandlesMatchTheValues()
    {
        var session = new CadSession();
        var body = CreateBoxBody(session, Vec3.Zero, 40, 40, 20);
        var command = Start(session, body, FaceFacing(body, n => n.Z > 0.99), 4);
        var handles = command.Handles;
        Assert.Equal(4, handles.Count);
        var height = handles.Single(h => h.Param.Label.StartsWith("Height"));
        AssertNear(1, Math.Abs(height.Dir.Z), "Height handle points out of the face");
        AssertNear(20, height.Origin.Z, "Height handle starts on the face (mm)");
        AssertNear(20, height.Origin.X, "Height handle starts at the face centre (mm)");
        Assert.Contains(handles, h => h.Param.Label == "Bump radius");
        var across = handles.Single(h => h.Param.Label == "Move the bump across");
        var up = handles.Single(h => h.Param.Label == "Move the bump up");
        Assert.True(Math.Abs(Vec3.Dot(across.Dir, up.Dir)) < 1e-9, "the two slide handles are at right angles");
        AssertNear(0, Vec3.Dot(across.Dir, height.Dir), "Slide handles lie in the face");
        Assert.Empty(Begin(session, () => new SculptFaceCommand()).Handles);      // nothing picked, nothing to grab
    }

    [AvaloniaFact(DisplayName = "DIR-H076 | A turned box, a tilted face and a box with every edge filleted all gain the same bump")]
    [Trait("TestId", "DIR-H076")]
    public void OrientationAndFilletedEdgesDoNotMatter()
    {
        var session = new CadSession();
        using var box = Occ.Box(40, 40, 20);
        double Gain(Shape shape)
        {
            var body = session.AddObject(shape, "Body", ObjKind.Solid);
            int face = Enumerable.Range(0, body.Shape.Count(SubKind.Face)).OrderByDescending(i => Occ.FaceInfoOf(body.Shape.Sub(SubKind.Face, i)).Normal.Z).First();
            var output = Start(session, body, face, 4).TryBuild();
            Assert.NotNull(output);
            using var result = Assert.Single(output.Replace).New;
            AssertValidSolid(result);
            return result.Props().Volume - body.Shape.Props().Volume;
        }
        double plain = Gain(Occ.Translate(box, Vec3.Zero));
        Assert.True(plain > 100, "the bump adds volume");
        AssertNear(plain, Gain(Occ.Rotate(box, Vec3.Zero, Vec3.UnitZ, 30)), "Turned about Z: bump volume (mm³)");
        AssertNear(plain, Gain(Occ.Rotate(box, Vec3.Zero, Vec3.UnitX, 20)), "Tilted face: bump volume (mm³)");
        using var rounded = Occ.Fillet(box, Enumerable.Range(0, box.Count(SubKind.Edge)).ToArray(), 3);
        Assert.InRange(Gain(rounded), plain * 0.95, plain * 1.05);      // the filleted top is smaller, so the bump is cut off a little at its outline
    }

    [AvaloniaFact(DisplayName = "DIR-H075 | Curved faces and faces that are not four-sided are refused and the body is unchanged")]
    [Trait("TestId", "DIR-H075")]
    public void UnsupportedFacesAreRefused()
    {
        var session = new CadSession();
        using var cylinder = Occ.Cylinder(10, 20);
        var round = session.AddObject(Occ.Translate(cylinder, Vec3.Zero), "Round", ObjKind.Solid);
        int side = Enumerable.Range(0, round.Shape.Count(SubKind.Face)).First(i => Occ.FaceInfoOf(round.Shape.Sub(SubKind.Face, i)).Type != "Plane");
        var curved = Start(session, round, side, 3);
        Assert.Null(curved.TryBuild());
        Assert.Contains("flat faces", curved.Error);

        int cap = Enumerable.Range(0, round.Shape.Count(SubKind.Face)).First(i => Occ.FaceInfoOf(round.Shape.Sub(SubKind.Face, i)).Type == "Plane");
        var disc = Start(session, round, cap, 3);
        Assert.Null(disc.TryBuild());
        Assert.Contains("four sides", disc.Error);
        Assert.Equal(round.Shape.Props().Volume, session.Find(round.Id)!.Shape.Props().Volume, 6);
    }
}
