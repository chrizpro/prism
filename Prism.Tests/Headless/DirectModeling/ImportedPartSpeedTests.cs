using Avalonia.Headless.XUnit;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.DirectModeling;

/// <summary>
/// Imported parts with hundreds of curved faces (a pegboard with filleted slots, say) took seconds per click because every panel
/// refresh, preview and display mesh measured the whole body again. A shape now keeps its measurements, and a moved copy inherits
/// them. These checks make sure the kept and inherited numbers are the ones a fresh measurement gives.
/// </summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/direct-modeling/README.md">Direct Modeling guide.</seealso>
[Trait("Feature", "DirectModeling")]
[Trait("Layer", "Headless")]
public sealed class ImportedPartSpeedTests(Xunit.Abstractions.ITestOutputHelper log)
{
    static Shape SlottedPlate()
    {
        using var plate = BoxAt(new Vec3(0, 0, 0), 60, 40, 5);
        Shape part = Occ.Translate(plate, Vec3.Zero);
        for (int i = 0; i < 3; i++)
        {
            using var hole = CylinderAt(new Vec3(12 + 18 * i, 20, -1), 3, 7);
            var cut = Occ.Cut(part, hole); part.Dispose(); part = cut;
        }
        var edges = Enumerable.Range(0, part.Count(SubKind.Edge)).ToArray();
        var filleted = Occ.Fillet(part, edges, 0.5); part.Dispose();
        return filleted;
    }

    static void AssertSameProps(ShapeProps expected, ShapeProps actual)
    {
        AssertClose(expected.Volume, actual.Volume, 1e-6 * Math.Abs(expected.Volume) + 1e-9, "volume");
        AssertClose(expected.Area, actual.Area, 1e-6 * Math.Abs(expected.Area) + 1e-9, "area");
        foreach (var (e, a) in new[] { (expected.Min, actual.Min), (expected.Max, actual.Max) })
            Assert.True((e - a).Length < 1e-5, $"box corner {a} should be {e}");
    }

    [AvaloniaFact(DisplayName = "DIR-H098 | A moved body keeps volume and area and its box moves with it, matching a fresh measurement")]
    [Trait("TestId", "DIR-H098")]
    public void MovedCopyInheritsMeasurements()
    {
        using var part = SlottedPlate();
        var before = part.Props();
        AssertSameProps(before, part.Props());          // asked twice, same answer

        var by = new Vec3(12.5, -3, 7);
        using var moved = Occ.Translate(part, by);
        using var fresh = Occ.FromBrep(moved.ToBrep());  // a new handle measures from scratch
        AssertSameProps(fresh.Props(), moved.Props());
        AssertClose(before.Volume, moved.Props().Volume, 1e-9, "volume after the move");
        Assert.True((moved.Props().Min - (before.Min + by)).Length < 1e-9);
    }

    [AvaloniaFact(DisplayName = "DIR-H099 | Moving an imported body with Move updates its box in the panel numbers and keeps it valid")]
    [Trait("TestId", "DIR-H099")]
    public void MoveCommandOnImportedBody()
    {
        var session = new CadSession();
        var body = session.AddObject(SlottedPlate(), "Slotted plate", ObjKind.Solid);
        var before = body.Shape!.Props();
        var move = StartCommand(session, body, () => new Prism.Commands.TransformCommand(Prism.Commands.TransformCommand.Kind.Move));
        SetNumber(move, "Δ X", 25);
        session.CommitCommand();
        var after = session.Find(body.Id)!.Shape!;
        using var fresh = Occ.FromBrep(after.ToBrep());
        AssertSameProps(fresh.Props(), after.Props());
        AssertClose(before.Min.X + 25, after.Props().Min.X, 1e-6, "moved box");
        Assert.True(after.IsValid);
    }

    /// <summary>Times a real imported part. Set PRISM_SPEED_STEP to a STEP file to run it; otherwise it does nothing.</summary>
    [AvaloniaFact(DisplayName = "DIR-H100 | Speed probe for an imported STEP part (runs only with PRISM_SPEED_STEP set)")]
    [Trait("TestId", "DIR-H100")]
    public void ImportedStepSpeedProbe()
    {
        if (Environment.GetEnvironmentVariable("PRISM_SPEED_STEP") is not { Length: > 0 } path) return;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        void Step(string what) { log.WriteLine($"{clock.Elapsed.TotalSeconds,7:0.00} s  {what}"); clock.Restart(); }
        var session = new CadSession();
        session.ImportStep(path);
        var body = session.Objects.Single(o => o.Kind == ObjKind.Solid);
        Step($"import, {body.Shape!.Count(SubKind.Face)} faces");
        body.Shape.Props(); Step("first measurement");
        body.Shape.Props(); Step("second measurement");
        _ = body.Mesh.TriangleCount; Step("display mesh");
        var move = StartCommand(session, body, () => new Prism.Commands.TransformCommand(Prism.Commands.TransformCommand.Kind.Move));
        SetNumber(move, "Δ X", 10);
        Step("Move preview");
        session.CommitCommand(); Step("Move commit");
        session.Find(body.Id)!.Shape!.Props(); Step("measure moved body");
        _ = session.Find(body.Id)!.Mesh.TriangleCount; Step("moved display mesh");
        _ = session.Find(body.Id)!.Shape!.IsValid; Step("validity check");

        // the largest upward flat face (the board's top), raised 1 mm; then one slot wall's slot deleted
        int Largest(Func<FaceInfo, bool> pick) => Enumerable.Range(0, session.Find(body.Id)!.Shape!.Count(SubKind.Face))
            .Where(i => { using var f = session.Find(body.Id)!.Shape!.Sub(SubKind.Face, i); return pick(Occ.FaceInfoOf(f)); })
            .OrderByDescending(i => { using var f = session.Find(body.Id)!.Shape!.Sub(SubKind.Face, i); return f.Props().Area; }).First();
        int top = Largest(f => f.Normal.Z > 0.99); Step("find top face");
        session.SelectItems([]);
        session.StartCommand(() => new Prism.Commands.MoveFaceCommand());
        Pick(session.ActiveCommand!, "Flat faces to move", new SelItem(body.Id, SelKind.Face, top));
        SetNumber(session.ActiveCommand!, "Move Z", 1); Step("Move Face preview");
        session.CommitCommand(); Step($"Move Face commit, {session.Find(body.Id)!.Shape!.Count(SubKind.Face)} faces");
        session.Find(body.Id)!.Shape!.Props(); Step("measure");
        _ = session.Find(body.Id)!.Mesh.TriangleCount; Step("display mesh");
        _ = session.Find(body.Id)!.Shape!.IsValid; Step("validity check");
    }
}
