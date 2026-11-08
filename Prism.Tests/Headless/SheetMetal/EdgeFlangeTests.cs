using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.SheetMetal;

/// <summary>
/// Characterizes Edge Flange on a 27 × 40 × 2 mm plate (x 0..27, y 0..40, z 0..2). With a 3 mm inside radius and a 17 mm leg, a 90° flange up gives the same bracket
/// as Fold makes from a 17 + 27 mm blank, so the volume and size come from straight legs plus a bend sector.
/// </summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/sheet-metal/README.md">Living guide.</seealso>
[Trait("Feature", "SheetMetal")]
[Trait("Layer", "Headless")]
public class EdgeFlangeTests
{
    const double T = 2, R = 3;

    static (CadSession session, SceneObject plate, SelItem endFace) Plate()
    {
        var session = new CadSession();
        var plate = CreateBoxBody(session, Vec3.Zero, 27, 40, T);
        return (session, plate, FaceFacing(plate, Vec3.UnitX));
    }

    /// <summary>The planar face whose outward normal is closest to <paramref name="direction"/>.</summary>
    static SelItem FaceFacing(SceneObject body, Vec3 direction)
    {
        int best = Enumerable.Range(0, body.Shape.Count(SubKind.Face)).MaxBy(i => { using var f = body.Shape.Sub(SubKind.Face, i); return Vec3.Dot(Occ.FaceInfoOf(f).Normal.Normalized(), direction); });
        return new SelItem(body.Id, SelKind.Face, best);
    }

    static EdgeFlangeCommand Flange(CadSession session, SelItem face, double length = 17, double angle = 90, double radius = R, string? dir = null)
    {
        var command = Begin(session, () => new EdgeFlangeCommand());
        Pick(command, "Thin end face of the sheet", face);
        SetNumber(command, "Flange length", length);
        SetNumber(command, "Bend angle", angle);
        SetNumber(command, "Bend radius (inside)", radius);
        if (dir != null) SetChoice(command, "Fold toward", dir);
        return command;
    }

    static Shape Result(CommandOutput output)
    {
        var (_, shape) = Assert.Single(output.Replace);
        AssertValidSolid(shape);
        return shape;
    }

    [AvaloniaFact(DisplayName = "EFL-H001 | A 90° edge flange up on a plate makes the bracket Fold makes from the same blank")]
    [Trait("TestId", "EFL-H001")]
    public void FlangeUp()
    {
        var (session, _, end) = Plate();
        var shape = Result(BuildOutput(Flange(session, end)));
        double section = 17 * T + 27 * T + Math.PI / 4 * ((R + T) * (R + T) - R * R);
        AssertClose(section * 40, shape.Props().Volume, 1e-3, "Bracket volume (mm³)");
        // outside of the flange at x = 27 + r + t; its top at the 2 mm inside face plus r plus the leg
        AssertBounds(shape, Vec3.Zero, new Vec3(27 + R + T, 40, T + R + 17));
    }

    [AvaloniaFact(DisplayName = "EFL-H002 | Fold toward Down bends the flange below the plate")]
    [Trait("TestId", "EFL-H002")]
    public void FlangeDown()
    {
        var (session, _, end) = Plate();
        var shape = Result(BuildOutput(Flange(session, end, dir: "Down (the lower face)")));
        AssertBounds(shape, new Vec3(0, 0, -(R + 17)), new Vec3(27 + R + T, 40, T));
        double section = 17 * T + 27 * T + Math.PI / 4 * ((R + T) * (R + T) - R * R);
        AssertClose(section * 40, shape.Props().Volume, 1e-3, "Bracket volume (mm³)");
    }

    [AvaloniaFact(DisplayName = "EFL-H003 | An edge flange on a Flange bracket adds exactly the bend and leg it should")]
    [Trait("TestId", "EFL-H003")]
    public void FlangeOnABracket()
    {
        var session = new CadSession();
        var model = new Prism.Core.Sketching.SketchModel();
        var ids = new[] { model.AddPoint(0, 20), model.AddPoint(0, 0), model.AddPoint(30, 0) };
        model.AddLine(ids[0], ids[1]); model.AddLine(ids[1], ids[2]);
        session.EnterSketch(Plane3.XY, model); session.ExitSketch(keep: true);
        var flange = Begin(session, () => new ContourFlangeCommand());
        Pick(flange, "Profile sketch (straight lines)", session.Objects.Last(o => o.Kind == ObjKind.Sketch));
        SetNumber(flange, "Thickness", T); SetNumber(flange, "Bend radius (inside)", 2); SetNumber(flange, "Width", 30);
        session.CommitCommand();
        var bracket = session.Objects.Last(o => o.Kind == ObjKind.Solid);
        double before = bracket.Shape.Props().Volume;
        // the free end of the leg along +X is a 30 mm wide, 2 mm thick face
        var command = Flange(session, FaceFacing(bracket, Vec3.UnitX), length: 10, radius: 2);
        var shape = Result(BuildOutput(command));
        AssertClose(before + 30 * (10 * T + Math.PI / 4 * ((2 + T) * (2 + T) - 2 * 2)), shape.Props().Volume, 1e-3, "Volume with the extra flange (mm³)");
    }

    [AvaloniaFact(DisplayName = "EFL-H004 | Edge Flange refuses a face that is not the thin end of a sheet")]
    [Trait("TestId", "EFL-H004")]
    public void RefusesWrongFace()
    {
        var (session, plate, _) = Plate();
        AssertBuildFails(Flange(session, FaceFacing(plate, Vec3.UnitZ)), "thin end face");
    }
}
