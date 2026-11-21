using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Solid;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/solid/README.md#threads">Threads (THR-P1).</seealso>
[Trait("Feature", "Solid")]
[Trait("Layer", "Headless")]
public class ThreadTests
{
    static SelItem Round(SceneObject body) =>
        new(body.Id, SelKind.Face, Enumerable.Range(0, body.Shape.Count(SubKind.Face)).First(i =>
        {
            using var face = body.Shape.Sub(SubKind.Face, i);
            return Occ.FaceInfoOf(face).Type == "Cylinder";
        }));

    static (CadSession session, SceneObject pin) Pin(double radius, double height)
    {
        var session = new CadSession();
        return (session, session.AddObject(CylinderAt(new(0, 0, 0), radius, height), "Pin", ObjKind.Solid));
    }

    static ThreadCommand Start(CadSession session, SceneObject body, Action<ThreadCommand>? set = null)
    {
        var command = Begin(session, () => new ThreadCommand());
        Pick(command, "Round faces to thread", Round(body));
        set?.Invoke(command);
        return command;
    }

    /// <summary>Volume taken out of an M10 x 1.5 pin by one full-length thread: the groove's section times the length of its path, turn by turn.</summary>
    static double ExpectedRemoved(double radius, double pitch, double length)
    {
        double halfRoot = .4375 * pitch - Math.Tan(Math.PI / 6) * .613435 * pitch, top = .875 * pitch, bottom = 2 * halfRoot, height = .613435 * pitch;
        double area = (top + bottom) / 2 * height, fromTop = height * (2 * top + bottom) / (3 * (top + bottom));
        return area * 2 * Math.PI * (radius - fromTop) * length / pitch;
    }

    [AvaloniaFact(DisplayName = "SOL-H160 | Thread on a Ø10 pin cuts an M10 x 1.5 external thread that takes the groove's volume off and leaves the pin Ø10")]
    [Trait("TestId", "SOL-H160")]
    public void ExternalThreadOnAPin()
    {
        var (session, pin) = Pin(5, 20);
        var before = pin.Shape.Props();
        var command = Start(session, pin);
        var (_, result) = Assert.Single(BuildOutput(command).Replace);
        Assert.True(result.IsValid);
        var removed = before.Volume - result.Props().Volume;
        var expected = ExpectedRemoved(5, 1.5, 20);
        Assert.InRange(removed, expected * .94, expected * 1.06);
        // the bounding box of the threaded faces is padded a little by their spline control points, so this is a loose check that nothing grew past Ø10
        AssertClose(10, result.Props().Size.X, 0.06, "Crest diameter (mm)");
        Assert.Contains("external", Result(command, "Thread"));
        Assert.Contains("M10 × 1.5", Result(command, "Thread"));
    }

    [AvaloniaFact(DisplayName = "SOL-H161 | A thread length shorter than the face threads only that much, and left-hand cuts the same amount")]
    [Trait("TestId", "SOL-H161")]
    public void LengthAndHandedness()
    {
        double Removed(Action<ThreadCommand> set)
        {
            var (session, pin) = Pin(5, 20);
            var before = pin.Shape.Props().Volume;
            return before - Assert.Single(BuildOutput(Start(session, pin, set)).Replace).Item2.Props().Volume;
        }
        double full = Removed(_ => { });
        AssertClose(.5, Removed(c => SetNumber(c, "Thread length (0 = whole face)", 10)) / full, 0.08, "Half the length cuts half (ratio)");
        AssertClose(1, Removed(c => c.Params.OfType<BoolParam>().Single(p => p.Label == "Left-hand thread").Value = true) / full, 0.02, "Left-hand cuts the same (ratio)");
    }

    [AvaloniaFact(DisplayName = "SOL-H162 | Thread on a hole drilled to the M6 tap size matches the Hole tool's own threaded M6 hole")]
    [Trait("TestId", "SOL-H162")]
    public void InternalThreadMatchesTheHoleTool()
    {
        // the Hole tool: a threaded M6 x 1 hole straight through
        double hole;
        {
            var session = new CadSession();
            var block = CreateBoxBody(session, new(-10, -10, 0), 20, 20, 10);
            var command = Begin(session, () => new HoleCommand());
            Pick(command, "Body or entry face", FaceFacing(block, new(0, 0, 1)));
            SetChoice(command, "Hole type", "Threaded (modeled)");
            SetChoice(command, "Metric size", "M6 × 1");
            hole = Assert.Single(BuildOutput(command).Replace).Item2.Props().Volume;
        }
        // the same hole drilled plain at the tap size, then threaded by the Thread tool
        double threaded;
        {
            var session = new CadSession();
            var block = CreateBoxBody(session, new(-10, -10, 0), 20, 20, 10);
            var drill = Begin(session, () => new HoleCommand());
            Pick(drill, "Body or entry face", FaceFacing(block, new(0, 0, 1)));
            SetNumber(drill, "Diameter", 6 - 1.082532);
            var drilled = Assert.Single(BuildOutput(drill).Replace).Item2;
            session.CommitCommand();
            var body = session.Objects.Single(o => o.Kind == ObjKind.Solid);
            var thread = Start(session, body);
            var result = Assert.Single(BuildOutput(thread).Replace).Item2;
            threaded = result.Props().Volume;
            Assert.Contains("internal", Result(thread, "Thread"));
            Assert.Contains("M6 × 1", Result(thread, "Thread"));
            Assert.True(drilled.Props().Volume > threaded);
        }
        AssertClose(hole, threaded, hole * .005, "Volume next to the Hole tool's threaded hole (mm³)");
    }

    [AvaloniaFact(DisplayName = "SOL-H163 | Thread refuses a flat face and a diameter with no standard pitch, with a message")]
    [Trait("TestId", "SOL-H163")]
    public void RefusesWhatItCannotThread()
    {
        var (session, pin) = Pin(5, 20);
        var flat = Begin(session, () => new ThreadCommand());
        Pick(flat, "Round faces to thread", FaceFacing(pin, new(0, 0, 1)));
        AssertBuildFails(flat, "round");

        var (odd, strange) = Pin(3.65, 20);
        AssertBuildFails(Start(odd, strange), "no standard coarse");
        var custom = Start(odd, strange, c => { SetChoice(c, "Pitch", "Custom"); SetNumber(c, "Custom pitch", 1); });
        Assert.True(custom.TryBuild() != null, custom.Error);
    }
}
