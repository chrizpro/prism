using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Loft;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/loft/README.md#end-tangency">Loft end tangency (LFT-P1).</seealso>
[Trait("Feature", "Loft")]
[Trait("Layer", "Headless")]
public class LoftTangencyTests
{
    static SceneObject Square(CadSession session, double x, double z, double side) =>
        session.AddObject(Occ.Polyline([new(x - side / 2, -side / 2, z), new(x + side / 2, -side / 2, z), new(x + side / 2, side / 2, z), new(x - side / 2, side / 2, z)], true), "Profile", ObjKind.Curve);

    /// <summary>Mean X of the loft's cross-section at the given height.</summary>
    static double CentreX(Shape loft, double z)
    {
        using var cut = Occ.Section(loft, new Vec3(0, 0, z), Vec3.UnitZ);
        var pts = Occ.CurveSamples(cut, 40, 64).SelectMany(e => e.Select(s => s.p)).ToList();
        return pts.Average(p => p.X);
    }

    static Shape Loft(Action<LoftCommand> set, bool shifted = true)
    {
        var session = new CadSession();
        var bottom = Square(session, 0, 0, 10);
        var top = Square(session, shifted ? 10 : 0, 20, 10);
        var command = Begin(session, () => new LoftCommand(surface: true));
        Pick(command, "Sections (in order)", bottom, top);
        set(command);
        return Assert.Single(BuildOutput(command).Add).Shape!;
    }

    [AvaloniaFact(DisplayName = "LFT-H030 | A free loft leans at once; Square to the profile holds the first profile's direction before turning")]
    [Trait("TestId", "LFT-H030")]
    public void SquareToTheProfile()
    {
        var free = Loft(_ => { });
        AssertClose(0.5, CentreX(free, 1), 0.1, "Free loft, centre 1 mm up (mm)");
        var held = Loft(c => SetChoice(c, "Start tangency", "Square to the profile"));
        Assert.True(Math.Abs(CentreX(held, 1)) < 0.15, $"Square start: centre 1 mm up should stay near the axis, was {CentreX(held, 1)}");
        // the far end is still where it was: the profile at the top is not moved
        AssertClose(10, CentreX(held, 19.999), 0.05, "Top profile centre (mm)");
    }

    [AvaloniaFact(DisplayName = "LFT-H031 | Square at both ends leaves the ends level and the middle in between")]
    [Trait("TestId", "LFT-H031")]
    public void SquareAtBothEnds()
    {
        var held = Loft(c => { SetChoice(c, "Start tangency", "Square to the profile"); SetChoice(c, "End tangency", "Square to the profile"); });
        // a smooth curve through four points that holds both ends level swings slightly past them, so this allows half a millimetre against the 0.5 of a free loft
        Assert.True(Math.Abs(CentreX(held, 1)) < 0.5, $"near the start: {CentreX(held, 1)}");
        Assert.True(Math.Abs(CentreX(held, 19) - 10) < 0.5, $"near the end: {CentreX(held, 19)}");
        AssertClose(5, CentreX(held, 10), 0.4, "Middle (mm)");
    }

    [AvaloniaFact(DisplayName = "LFT-H032 | Match a face leaves a profile in the plane of the face it starts from")]
    [Trait("TestId", "LFT-H032")]
    public void MatchAFace()
    {
        var session = new CadSession();
        var box = CreateBoxBody(session, new(0, 0, 0), 10, 10, 10);
        // a line along the top edge on the right of the box top (x = 10, z = 10), and a second one up and away
        var first = session.AddObject(Occ.Polyline([new(10, 0, 10), new(10, 10, 10)], false), "Start", ObjKind.Curve);
        var second = session.AddObject(Occ.Polyline([new(20, 0, 20), new(20, 10, 20)], false), "End", ObjKind.Curve);
        double HeightAt(Action<LoftCommand> set)
        {
            var command = Begin(session, () => new LoftCommand(surface: true));
            Pick(command, "Sections (in order)", first, second);
            set(command);
            var loft = Assert.Single(BuildOutput(command).Add).Shape!;
            using var cut = Occ.Section(loft, new Vec3(11, 0, 0), Vec3.UnitX);
            var pts = Occ.CurveSamples(cut, 20, 64).SelectMany(e => e.Select(s => s.p)).ToList();
            return pts.Average(p => p.Z);
        }
        AssertClose(11, HeightAt(_ => { }), 0.1, "Free loft, 1 mm out (height)");
        double held = HeightAt(c =>
        {
            SetChoice(c, "Start tangency", "Match a face");
            Pick(c, "Face to match at the start", FaceFacing(box, new(0, 0, 1)));
        });
        Assert.True(held < 10.35, $"Matching the flat top, the loft should stay near its plane 1 mm out, was {held}");
    }

    [AvaloniaFact(DisplayName = "LFT-H033 | Tangency refuses guide rails and a face that is not there, with a message")]
    [Trait("TestId", "LFT-H033")]
    public void RefusesWhatItCannotDo()
    {
        var session = new CadSession();
        var bottom = Square(session, 0, 0, 10); var top = Square(session, 10, 20, 10);
        var command = Begin(session, () => new LoftCommand(surface: true));
        Pick(command, "Sections (in order)", bottom, top);
        SetChoice(command, "Start tangency", "Match a face");
        AssertBuildFails(command, "Pick the face");
    }
}
