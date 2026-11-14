using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Solid;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/surface-and-nurbs/README.md#4-solid-dress-up-depth">0.4 modeling gaps, item 4.</seealso>
[Trait("Feature", "Solid")]
[Trait("Layer", "Headless")]
public class DressUpDepthTests
{
    const double Side = 10;

    static (CadSession session, SceneObject box) Box()
    {
        var session = new CadSession();
        return (session, CreateBoxBody(session, Vec3.Zero, Side, Side, Side));
    }

    /// <summary>The edge of the box along X on the front-bottom (y = 0, z = 0).</summary>
    static SelItem FrontBottom(SceneObject box) =>
        Assert.Single(EdgesWhere(box, e => e.Min.Y < 1e-6 && e.Max.Y < 1e-6 && e.Min.Z < 1e-6 && e.Max.Z < 1e-6 && e.Max.X - e.Min.X > 1));

    static double RemovedVolume(string style, Action<ChamferCommand>? set = null)
    {
        var (session, box) = Box();
        var command = StartCommand(session, box, () => new ChamferCommand());
        command.ConfigureInputs(() => Pick(command, "Edges", FrontBottom(box)));
        SetChoice(command, "Style", style);
        set?.Invoke(command);
        var shape = Assert.Single(BuildOutput(command).Replace).Item2;
        Assert.True(shape.IsValid);
        return Side * Side * Side - shape.Props().Volume;
    }

    [AvaloniaTheory(DisplayName = "DRS-H001 | Chamfer can cut two distances or a distance and an angle")]
    [InlineData("Equal distance", 0.0, 0.5 * 3 * 3 * Side)]
    [InlineData("Two distances", 0.0, 0.5 * 3 * 1.5 * Side)]
    [InlineData("Distance and angle", 30.0, 0.5 * 3 * 1.7320508075688772 * Side)]
    [Trait("TestId", "DRS-H001")]
    public void ChamferStylesRemoveTheExpectedVolume(string style, double angle, double expected) =>
        AssertClose(expected, RemovedVolume(style, c =>
        {
            SetNumber(c, "Distance", 3);
            if (style == "Two distances") SetNumber(c, "Second distance", 1.5);
            if (style == "Distance and angle") SetNumber(c, "Angle", angle);
        }), 1e-3, "Removed volume (mm³)");

    [AvaloniaFact(DisplayName = "DRS-H002 | Swapping the two sides of a two-distance chamfer puts the long cut on the other face")]
    [Trait("TestId", "DRS-H002")]
    public void SwapMovesTheChamferToTheOtherFace()
    {
        // area of the box's front face (y = 0) after a 4 / 1 chamfer: 100 minus the leg that lies on it, times the 10 mm length
        double FrontArea(bool swap)
        {
            var (session, box) = Box();
            var command = StartCommand(session, box, () => new ChamferCommand());
            command.ConfigureInputs(() => Pick(command, "Edges", FrontBottom(box)));
            SetChoice(command, "Style", "Two distances");
            SetNumber(command, "Distance", 4); SetNumber(command, "Second distance", 1);
            command.Params.OfType<BoolParam>().Single(p => p.Label == "Swap the two sides").Value = swap;
            var shape = Assert.Single(BuildOutput(command).Replace).Item2;
            return Enumerable.Range(0, shape.Count(SubKind.Face)).Select(i => shape.Sub(SubKind.Face, i).Props())
                .Where(p => p.Max.Y < 1e-6).Select(p => p.Area).Single();
        }
        double plain = FrontArea(false), swapped = FrontArea(true);
        Assert.Equal(new[] { Side * (Side - 4), Side * (Side - 1) }.OrderBy(x => x), new[] { plain, swapped }.OrderBy(x => x).Select(x => Math.Round(x, 6)));
    }

    [AvaloniaFact(DisplayName = "DRS-H003 | Fillet takes a radius per edge, and an unlisted edge uses the radius")]
    [Trait("TestId", "DRS-H003")]
    public void FilletRadiusPerEdge()
    {
        double Removed(string perEdge)
        {
            var (session, box) = Box();
            var edges = EdgesWhere(box, e => e.Max.X - e.Min.X > 1 && (e.Min.Y < 1e-6 || e.Max.Y > Side - 1e-6) && (e.Min.Z < 1e-6 || e.Max.Z > Side - 1e-6)).Take(2).ToArray();
            Assert.Equal(2, edges.Length);
            var command = StartCommand(session, box, () => new FilletCommand());
            command.ConfigureInputs(() => Pick(command, "Edges", edges));
            SetNumber(command, "Radius", 1);
            if (perEdge != "") SetText(command, "Radius per edge (optional)", perEdge);
            var shape = Assert.Single(BuildOutput(command).Replace).Item2;
            Assert.True(shape.IsValid);
            return Side * Side * Side - shape.Props().Volume;
        }
        double quarter(double r) => (1 - Math.PI / 4) * r * r * Side;
        AssertClose(2 * quarter(1), Removed(""), 1e-3, "Same radius on both edges (mm³)");
        AssertClose(quarter(2) + quarter(1), Removed("2"), 1e-3, "First edge 2 mm, second falls back to Radius (mm³)");
        AssertClose(quarter(2) + quarter(3), Removed("2, 3"), 1e-3, "Each edge its own radius (mm³)");
    }

    [AvaloniaFact(DisplayName = "DRS-H004 | A radius per edge that is not a number is refused with a message")]
    [Trait("TestId", "DRS-H004")]
    public void FilletRadiusPerEdgeRejectsText()
    {
        var (session, box) = Box();
        var command = StartCommand(session, box, () => new FilletCommand());
        command.ConfigureInputs(() => Pick(command, "Edges", FrontBottom(box)));
        SetText(command, "Radius per edge (optional)", "two");
        AssertBuildFails(command, "not a radius");
    }

    // ------------------------------------------------------------------ Draft with a parting line

    /// <summary>A 20 x 20 x 10 base with a 16 x 16 x 10 block on top: eight walls, four below the z = 10 shoulder and four above it.</summary>
    static (CadSession session, SceneObject part, SelItem[] walls, SelItem shoulder) SteppedPart()
    {
        var session = new CadSession();
        using var lower = Occ.Box(20, 20, 10);
        using var upper = Occ.Translate(Occ.Box(16, 16, 10), new Vec3(0, 0, 10));
        var part = session.AddObject(Occ.Fuse(lower, upper), "Part", ObjKind.Solid);
        var faces = Enumerable.Range(0, part.Shape.Count(SubKind.Face)).Select(i => (i, info: Occ.FaceInfoOf(part.Shape.Sub(SubKind.Face, i)), props: part.Shape.Sub(SubKind.Face, i).Props())).ToList();
        var walls = faces.Where(f => Math.Abs(f.info.Normal.Z) < 0.01).Select(f => new SelItem(part.Id, SelKind.Face, f.i)).ToArray();
        var shoulder = faces.Single(f => f.info.Normal.Z > 0.99 && Math.Abs(f.props.Max.Z - 10) < 1e-6);
        Assert.Equal(8, walls.Length);
        return (session, part, walls, new SelItem(part.Id, SelKind.Face, shoulder.i));
    }

    static (double bottom, double top) DraftedEndAreas(string parting)
    {
        var (session, part, walls, shoulder) = SteppedPart();
        var command = StartCommand(session, part, () => new DraftFaceCommand());
        command.ConfigureInputs(() => { Pick(command, "Faces to draft", walls); Pick(command, "Neutral plane (a plane or flat face the walls hinge on)", shoulder); });
        SetNumber(command, "Draft angle", 3);
        SetChoice(command, "Parting line", parting);
        var shape = Assert.Single(BuildOutput(command).Replace).Item2;
        Assert.True(shape.IsValid);
        double Area(double z) => Enumerable.Range(0, shape.Count(SubKind.Face)).Select(i => shape.Sub(SubKind.Face, i).Props())
            .Where(p => Math.Abs(p.Min.Z - z) < 1e-6 && Math.Abs(p.Max.Z - z) < 1e-6).Sum(p => p.Area);
        return (Area(0), Area(20));
    }

    [AvaloniaFact(DisplayName = "DRS-H005 | Without a parting line every wall leans one way; with one, walls either side of the neutral plane lean opposite ways")]
    [Trait("TestId", "DRS-H005")]
    public void DraftPartingLineLeansWallsOppositeWays()
    {
        var oneWay = DraftedEndAreas("None (all walls lean one way)");
        var parted = DraftedEndAreas("At the neutral plane (walls lean opposite ways)");
        // one direction: every wall tilts the same way, so the top grows while the bottom shrinks (a single pull direction)
        Assert.True(oneWay.top > 16 * 16 + 1 && oneWay.bottom < 20 * 20 - 1, $"one way: bottom {oneWay.bottom:0.##}, top {oneWay.top:0.##}");
        // parting line: both ends shrink away from the shoulder, so the part comes out either side
        Assert.True(parted.top < 16 * 16 - 1 && parted.bottom < 20 * 20 - 1, $"parted: bottom {parted.bottom:0.##}, top {parted.top:0.##}");
    }

    [AvaloniaFact(DisplayName = "DRS-H006 | A wall lying on the parting line is refused with a message")]
    [Trait("TestId", "DRS-H006")]
    public void DraftPartingLineRefusesWallOnTheLine()
    {
        var session = new CadSession();
        var box = CreateBoxBody(session, Vec3.Zero, 10, 10, 10);
        var command = StartCommand(session, box, () => new DraftFaceCommand());
        var plane = session.AddObject(null, "Parting", ObjKind.Plane, plane: Plane3.XY with { Origin = new Vec3(0, 0, 5) });
        var wall = Enumerable.Range(0, box.Shape.Count(SubKind.Face)).Single(i => Vec3.Dot(Occ.FaceInfoOf(box.Shape.Sub(SubKind.Face, i)).Normal, Vec3.UnitX) > 0.99);
        command.ConfigureInputs(() => { Pick(command, "Faces to draft", new SelItem(box.Id, SelKind.Face, wall)); SetSelection(command, "Neutral plane (a plane or flat face the walls hinge on)", plane); });
        SetChoice(command, "Parting line", "At the neutral plane (walls lean opposite ways)");
        AssertBuildFails(command, "parting line");
    }
}
