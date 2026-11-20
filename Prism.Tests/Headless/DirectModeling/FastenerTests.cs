using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.DirectModeling;

/// <summary>Bolts, screws and nuts recognised in the holes of another body, and carried along when Edit Feature changes the hole.</summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/direct-modeling/README.md">Direct Modeling guide, "Recognise more".</seealso>
[Trait("Feature", "DirectModeling")]
[Trait("Layer", "Headless")]
public sealed class FastenerTests
{
    /// <summary>A hexagonal prism (across flats <paramref name="af"/>) on the Z axis through (x, y), from z0 to z1.</summary>
    static Shape Hex(double x, double y, double af, double z0, double z1)
    {
        double R = af / Math.Sqrt(3);
        var pts = Enumerable.Range(0, 6).Select(i => new Vec3(x + R * Math.Cos(i * Math.PI / 3), y + R * Math.Sin(i * Math.PI / 3), z0)).ToList();
        using var wire = Occ.Polyline(pts, true); using var face = Occ.Profile([wire], true);
        return Occ.Extrude(face, new Vec3(0, 0, z1 - z0));
    }

    static Shape HexBolt(double x, double y)
    {
        using var shank = CylinderAt(new Vec3(x, y, -15), 3, 25);   // M6 × 25 under the head
        using var head = Hex(x, y, 10, 10, 14);
        return Occ.Fuse(shank, head);
    }

    static Shape Nut(double x, double y)
    {
        using var hex = Hex(x, y, 10, -10, -5);
        using var bore = CylinderAt(new Vec3(x, y, -11), 3, 7);
        return Occ.Cut(hex, bore);
    }

    static Shape SocketScrew(double x, double y)
    {
        using var shank = CylinderAt(new Vec3(x, y, -10), 3, 14);   // up to the counterbore floor at z = 4
        using var head = CylinderAt(new Vec3(x, y, 4), 5, 6);
        using var both = Occ.Fuse(shank, head);
        using var key = Hex(x, y, 5, 7, 11);
        return Occ.Cut(both, key);
    }

    /// <summary>The plate (Ø6.6 holes at x = 20 and 50, an M6 counterbore at x = 80) and its fasteners, written to STEP and read back as separate bodies.</summary>
    static (CadSession session, SceneObject plate) Assembly()
    {
        using var block = BoxAt(new Vec3(0, 0, 0), 100, 40, 10);
        using var h1 = CylinderAt(new Vec3(20, 20, -1), 3.3, 12); using var h2 = CylinderAt(new Vec3(50, 20, -1), 3.3, 12);
        using var h3 = CylinderAt(new Vec3(80, 20, -1), 3.3, 12); using var cb = CylinderAt(new Vec3(80, 20, 4), 5.5, 7);
        using var c1 = Occ.Cut(block, h1); using var c2 = Occ.Cut(c1, h2); using var c3 = Occ.Cut(c2, h3); using var plate = Occ.Cut(c3, cb);
        using var bolt1 = HexBolt(20, 20); using var nut = Nut(20, 20); using var bolt2 = HexBolt(50, 20); using var screw = SocketScrew(80, 20);
        var path = Path.Combine(Path.GetTempPath(), $"fasteners-{Guid.NewGuid():N}.step");
        try
        {
            Occ.ExportStep([plate, bolt1, nut, bolt2, screw], path);
            var session = new CadSession(); session.ImportStep(path);
            var plateObj = session.Objects.Where(o => o.Kind == ObjKind.Solid).OrderByDescending(o => o.Shape!.Props().Volume).First();
            return (session, plateObj);
        }
        finally { File.Delete(path); }
    }

    [AvaloniaFact(DisplayName = "DIR-H111 | Bolts, a socket screw and a nut are recognised in the plate's holes with their size and length")]
    [Trait("TestId", "DIR-H111")]
    public void FastenersAreRecognised()
    {
        var (session, _) = Assembly();
        Assert.Equal(5, session.Objects.Count(o => o.Kind == ObjKind.Solid));
        var found = FastenerFinder.Find(session.Objects).OrderBy(f => f.Origin.X).ToList();
        Assert.Equal(["hex bolt", "hex bolt", "socket screw"], found.Select(f => f.Kind));
        Assert.All(found, f => Assert.Equal("M6", f.Size));
        AssertClose(25, found[0].Length, 0.01, "bolt length under the head");
        Assert.Single(found[0].Nuts); Assert.Empty(found[1].Nuts);
        Assert.Equal("M6 × 25 hex bolt with a nut", found[0].Describe);
        Assert.Contains(FeatureFinder.Features(found[2].Holes[0].Body), f => f.Kind == FeatureKind.Counterbore);
    }

    static CommandOutput Edit(CadSession session, SceneObject plate, double x, Action<Command> set)
    {
        var hole = FeatureFinder.Features(plate).First(f => FastenerFinder.Bore(plate, f) is { } b && Math.Abs(b.Origin.X - x) < 1e-3);
        var command = StartCommand(session, plate, () => new FeatureEditCommand());
        command.ConfigureInputs(() => { Assert.True(command.Slots[0].TryAdd(new SelItem(plate.Id, SelKind.Face, hole.Face))); set(command); });
        var output = command.TryBuild();
        Assert.True(output != null, command.Error);
        return output;
    }

    [AvaloniaFact(DisplayName = "DIR-H112 | Moving a hole carries its bolt and nut, deleting it removes them, and resizing to the next metric size scales them across the axis")]
    [Trait("TestId", "DIR-H112")]
    public void HoleEditsCarryFasteners()
    {
        var (session, plate) = Assembly();
        var bolt1 = FastenerFinder.Find(session.Objects).OrderBy(f => f.Origin.X).First();
        var moved = Edit(session, plate, 20, c => { SetChoice(c, "Action", "Move"); SetNumber(c, "Move Y", 5); });
        Assert.Equal(3, moved.Replace.Count);   // the plate, the bolt and its nut
        var boltAfter = moved.Replace.Single(r => r.Old.Id == bolt1.Body.Id).New;
        AssertClose(bolt1.Body.Shape!.Props().Center.Y + 5, boltAfter.Props().Center.Y, 1e-6, "bolt moved with the hole");
        session.CancelCommand();

        var deleted = Edit(session, plate, 50, c => SetChoice(c, "Action", "Delete"));
        var bolt2 = FastenerFinder.Find(session.Objects).OrderBy(f => f.Origin.X).ElementAt(1);
        Assert.Equal([bolt2.Body.Id], deleted.Remove.Select(r => r.Id));
        session.CancelCommand();

        var resized = Edit(session, plate, 20, c => { SetChoice(c, "Action", "Resize"); SetNumber(c, "New diameter (0 = keep)", 9); });   // M6 clearance -> M8 clearance
        var shank = resized.Replace.Single(r => r.Old.Id == bolt1.Body.Id).New;
        var probe = session.AddObject(shank, "Scaled bolt", ObjKind.Solid);
        var r8 = FeatureFinder.Features(probe).Where(f => f.Round is { Hole: false }).Max(f => f.Round!.Radius);
        AssertClose(4, r8, 1e-4, "M8 shank radius");
        AssertClose(10, shank.Props().Max.Z - 4 * 8.0 / 6, 1e-4, "the bigger head still sits on the plate (top face at z = 10)");
        Assert.Contains(FastenerFinder.Find(session.Objects.Select(o => o.Id == bolt1.Body.Id ? o.WithShape(shank) : o)), x => x.Size == "M8");

        var noCarry = Edit(session, plate, 20, c => { SetChoice(c, "Action", "Move"); SetNumber(c, "Move Y", 5); c.Params.OfType<BoolParam>().Single(p => p.Label.StartsWith("Bolts")).Value = false; });
        Assert.Single(noCarry.Replace);
    }
}
