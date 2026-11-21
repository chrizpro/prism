using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Core.Sketching;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.DirectModeling;

/// <summary>
/// The feature-recognition demo (Direct Modeling guide, "Demo plan"): a mounting plate made in code and written to STEP with its bolts, read back
/// with no history, recognised, then revised feature by feature and baked. Each step asserts its effect. Set PRISM_GALLERY_OUT to a folder to save
/// the document as imported ("mounting-plate-imported") and as revised ("mounting-plate-revised").
/// </summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/direct-modeling/README.md">Direct Modeling guide.</seealso>
[Trait("Feature", "DirectModeling")]
[Trait("Layer", "Headless")]
public sealed class RecognitionDemoStudy(Xunit.Abstractions.ITestOutputHelper log)
{
    static Shape Hex(double x, double y, double af, double z0, double z1)
    {
        double R = af / Math.Sqrt(3);
        var pts = Enumerable.Range(0, 6).Select(i => new Vec3(x + R * Math.Cos(i * Math.PI / 3), y + R * Math.Sin(i * Math.PI / 3), z0)).ToList();
        using var wire = Occ.Polyline(pts, true); using var face = Occ.Profile([wire], true);
        return Occ.Extrude(face, new Vec3(0, 0, z1 - z0));
    }

    static Shape Cut(Shape part, Shape tool) { var r = Occ.Cut(part, tool); part.Dispose(); tool.Dispose(); return r; }

    /// <summary>140 × 90 × 10 plate: a 1 mm chamfer round the top, four Ø6.6 corner holes, three staggered rows of slots, "STEEL" engraved 0.8 mm, a 4 mm rib along the back.</summary>
    static Shape Plate()
    {
        using var block = BoxAt(new Vec3(0, 0, 0), 140, 90, 10);
        var top = FeatureRecognitionTests.Edges(block, e => Math.Abs(e.Start.Z - 10) < 1e-6 && Math.Abs(e.End.Z - 10) < 1e-6);
        Shape part = Occ.Chamfer(block, top, 1);
        foreach (var (x, y) in new[] { (12.0, 12.0), (128.0, 12.0), (12.0, 60.0), (128.0, 60.0) })
            part = Cut(part, CylinderAt(new Vec3(x, y, -1), 3.3, 12));
        for (int b = 0; b < 3; b++)
            for (int a = 0; a < (b % 2 == 0 ? 4 : 3); a++)
            {
                double x = 35 + 24 * a + (b % 2 == 0 ? 0 : 12), y = 14 + 12 * b;
                using var e1 = CylinderAt(new Vec3(x, y - 3, -1), 2, 12); using var e2 = CylinderAt(new Vec3(x, y + 3, -1), 2, 12);
                using var mid = BoxAt(new Vec3(x - 2, y - 3, -1), 4, 6, 12); using var e12 = Occ.Fuse(e1, e2);
                part = Cut(part, Occ.Fuse(e12, mid));
            }
        Shape? letters = null;
        foreach (var c in TextOutline.Build("STEEL", "Arial", false, 9))
        {
            using var wire = Occ.Polyline(c.Select(p => new Vec3(48 + p.u, 54 + p.v, 9.2)).ToList(), true);
            using var face = Occ.Profile([wire], true);
            var l = Occ.Extrude(face, new Vec3(0, 0, 2));
            if (letters == null) letters = l; else { var f = Occ.Fuse(letters, l); letters.Dispose(); l.Dispose(); letters = f; }
        }
        part = Cut(part, letters!);
        using var rib = BoxAt(new Vec3(25, 78, 10), 90, 4, 15);
        var withRib = Occ.Fuse(part, rib); part.Dispose();
        return withRib;
    }

    static (Shape bolt, Shape nut) BoltAndNut(double x, double y)
    {
        using var shank = CylinderAt(new Vec3(x, y, -12), 3, 22); using var head = Hex(x, y, 10, 10, 14);
        using var hex = Hex(x, y, 10, -7, -2); using var bore = CylinderAt(new Vec3(x, y, -8), 3, 7);
        return (Occ.Fuse(shank, head), Occ.Cut(hex, bore));
    }

    void Save(CadSession s, string name)
    {
        if (Environment.GetEnvironmentVariable("PRISM_GALLERY_OUT") is not { Length: > 0 } folder) return;
        Directory.CreateDirectory(folder);
        s.Save(Path.Combine(folder, name + ".prism"));
    }

    SceneObject Plate(CadSession s) => s.Objects.Where(o => o.Kind == ObjKind.Solid).OrderByDescending(o => o.Shape!.Props().Volume).First();

    void Commit(CadSession s, Command c, string what)
    {
        var preview = c.TryBuild();
        Assert.True(preview != null, $"{what}: {c.Error}");
        s.CommitCommand();
        Assert.Null(s.ActiveCommand);
        log.WriteLine($"{what}: done");
    }

    [AvaloniaFact(DisplayName = "GAL-H205 | Mounting plate from STEP: every feature recognised, then bolts resized, slots, chamfer, text, rib and width revised, and the history baked")]
    [Trait("TestId", "GAL-H205")]
    public void MountingPlate()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mounting-plate-{Guid.NewGuid():N}.step");
        var parts = new List<Shape> { Plate() };
        foreach (var (x, y) in new[] { (12.0, 12.0), (128.0, 12.0), (12.0, 60.0), (128.0, 60.0) }) { var (b, n) = BoltAndNut(x, y); parts.Add(b); parts.Add(n); }
        var s = new CadSession();
        try
        {
            Occ.ExportStep(parts, path);
            s.ImportStep(path);
            if (Environment.GetEnvironmentVariable("PRISM_GALLERY_OUT") is { Length: > 0 } folder)
            {
                Directory.CreateDirectory(folder);
                File.Copy(path, Path.Combine(folder, "mounting-plate-source.step"), overwrite: true);
            }
        }
        finally { File.Delete(path); foreach (var p in parts) p.Dispose(); }
        Assert.Equal(9, s.Objects.Count(o => o.Kind == ObjKind.Solid));
        var boltIds = FastenerFinder.Find(s.Objects).Select(f => f.Body.Id).ToHashSet();
        var plateId = Plate(s).Id;
        foreach (var o in s.Objects.Where(o => o.Kind == ObjKind.Solid).ToList())
            s.Update(o.Id, x => x.WithName(x.Id == plateId ? "Mounting plate" : boltIds.Contains(x.Id) ? "Hex bolt" : "Nut").WithColor(0xFFA6A9AE), checkpoint: false);
        var plate = Plate(s);
        Assert.Equal(4, boltIds.Count);
        Assert.Equal(4, s.Objects.Count(o => o.Name == "Hex bolt"));

        // 1. recognise everything
        var lines = FeatureSummary.For(plate, s.Objects).Select(l => l.Text).ToList();
        foreach (var l in lines) log.WriteLine("  " + l);
        Assert.Contains("4 × Ø6.6 through (M6 clearance (medium)) — in rows: 116 mm apart along X, 2 rows 48 mm apart along Y", lines);
        Assert.Contains(lines, l => l.StartsWith("11 × slot") && l.Contains("staggered rows: 24 mm apart along X, 3 rows 12 mm apart along Y, every other row shifted 12 mm"));
        Assert.Contains(lines, l => l.StartsWith("Chamfer") && l.Contains("1 mm × 45°"));
        Assert.Contains(lines, l => l.StartsWith("Text: 5 letters") && l.EndsWith("engraved 0.8 mm"));
        Assert.Contains("Rib 4 mm thick, 15 tall", lines);
        Assert.Contains("Fasteners: 4 × M6 × 22 hex bolt with a nut", lines);
        Save(s, "mounting-plate-imported");

        // 2. the four bolt holes to M8 clearance: the bolts and nuts grow with them
        var holes = FeatureFinder.Features(plate).Where(f => f.Kind == FeatureKind.Hole).ToList();
        var edit = StartCommand(s, plate, () => new FeatureEditCommand());
        edit.ConfigureInputs(() => { foreach (var h in holes) Assert.True(edit.Slots[0].TryAdd(new SelItem(plate.Id, SelKind.Face, h.Face))); SetChoice(edit, "Action", "Resize"); SetNumber(edit, "New diameter (0 = keep)", 9); });
        Commit(s, edit, "holes to M8");
        plate = Plate(s);
        Assert.Equal(4, FastenerFinder.Find(s.Objects).Count(f => f.Size == "M8"));

        // 3. the slots in straight rows
        var slot = FeatureFinder.Features(plate).First(f => f.Kind == FeatureKind.Slot);
        var pattern = StartCommand(s, plate, () => new EditPatternCommand());
        pattern.ConfigureInputs(() => { Assert.True(pattern.Slots[0].TryAdd(new SelItem(plate.Id, SelKind.Face, slot.Face))); SetChoice(pattern, "Every other row", "Not shifted"); });
        Commit(s, pattern, "slots unstaggered");
        plate = Plate(s);
        // read from any slot (the short row leaves a gap beside some of them) it is the same 11-slot grid
        Assert.All(FeatureFinder.Features(plate).Where(f => f.Kind == FeatureKind.Slot), f => Assert.StartsWith("11 slots in rows: 24 mm apart along X", FeatureFinder.PatternText(plate, f)));

        // 4. the chamfer swapped for a 1 mm round, 5. the text raised, 6. the rib thickened
        foreach (var (kind, action, set, what) in new (FeatureKind, string, Action<Command>, string)[]
        {
            (FeatureKind.Chamfer, "Change type", _ => { }, "chamfer to fillet"),
            (FeatureKind.Text, "Change type", _ => { }, "text embossed"),
            (FeatureKind.Rib, "Resize", c => SetNumber(c, "New rib thickness (0 = keep)", 6), "rib to 6 mm"),
        })
        {
            var feats = FeatureFinder.Features(plate).Where(f => f.Kind == kind).ToList();
            var c = StartCommand(s, plate, () => new FeatureEditCommand());
            c.ConfigureInputs(() => { foreach (var f in feats) Assert.True(c.Slots[0].TryAdd(new SelItem(plate.Id, SelKind.Face, f.Face))); SetChoice(c, "Action", action); set(c); });
            Commit(s, c, what);
            plate = Plate(s);
        }
        Assert.DoesNotContain(FeatureFinder.Features(plate), f => f.Kind == FeatureKind.Chamfer);
        Assert.False(FeatureFinder.TextOf(plate, FeatureFinder.Features(plate).Single(f => f.Kind == FeatureKind.Text))!.Engraved);
        AssertClose(6, FeatureFinder.RibOf(plate, FeatureFinder.Features(plate).Single(f => f.Kind == FeatureKind.Rib))!.Thickness, 1e-6, "rib");

        // 7. Edit Distance: the plate 150 mm wide instead of 140, measured from its left face
        int Face(Func<ShapeProps, bool> where) => Enumerable.Range(0, plate.Shape!.Count(SubKind.Face)).Single(i =>
        {
            using var f = plate.Shape.Sub(SubKind.Face, i); var info = Occ.FaceInfoOf(f);
            return info.Type == "Plane" && Math.Abs(info.Normal.Normalized().X) > 0.999 && where(f.Props());
        });
        int right = Face(p => p.Min.X > 139), left = Face(p => p.Max.X < 1);
        s.SelectItems([]);
        s.StartCommand(() => new EditDistanceCommand());
        var dist = Assert.IsType<EditDistanceCommand>(s.ActiveCommand);
        dist.ConfigureInputs(() => { dist.Slots[0].TryAdd(new SelItem(plate.Id, SelKind.Face, right)); dist.Slots[1].TryAdd(new SelItem(plate.Id, SelKind.Face, left)); });
        dist.TryBuild();
        dist.ConfigureInputs(() => SetNumber(dist, "Distance", 150));
        Commit(s, dist, "width to 150 mm");
        plate = Plate(s);
        AssertClose(150, plate.Shape!.Props().Size.X, 1e-6, "plate width");
        AssertValidSolid(plate.Shape);

        // 8. bake: the import and the edits become the start
        int steps = s.HistoryPath().Past.Count - 1;
        Assert.True(s.BakeHistory(s.Current));
        Assert.Single(s.HistoryPath().Past);
        log.WriteLine($"baked {steps} steps");
        foreach (var l in FeatureSummary.For(plate, s.Objects)) log.WriteLine("  after: " + l.Text);
        Save(s, "mounting-plate-revised");
    }
}
