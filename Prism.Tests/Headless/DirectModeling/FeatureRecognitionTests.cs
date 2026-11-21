using Avalonia.Headless.XUnit;
using Prism.Core;
using Prism.Core.Sketching;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.DirectModeling;

/// <summary>
/// Feature recognition on parts that arrive without history: each part is built in code, written to STEP and read back, then recognised.
/// Covers the 2026-11-19 additions (chamfers, hole ends and standard sizes, plates with rounded outlines).
/// </summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/direct-modeling/README.md">Direct Modeling guide, "Feature recognition, editable patterns and baking history".</seealso>
[Trait("Feature", "DirectModeling")]
[Trait("Layer", "Headless")]
public sealed class FeatureRecognitionTests
{
    /// <summary>Writes the shape to STEP and reads it back into a new document, as an imported part.</summary>
    internal static (CadSession session, SceneObject body) Imported(Shape part)
    {
        var path = Path.Combine(Path.GetTempPath(), $"recognise-{Guid.NewGuid():N}.step");
        try
        {
            Occ.ExportStep([part], path);
            var session = new CadSession();
            session.ImportStep(path);
            return (session, session.Objects.Single(o => o.Kind == ObjKind.Solid));
        }
        finally { File.Delete(path); }
    }

    internal static int[] Edges(Shape s, Func<EdgeInfo, bool> pick) =>
        Enumerable.Range(0, s.Count(SubKind.Edge)).Where(i => { using var e = s.Sub(SubKind.Edge, i); return pick(Occ.EdgeInfoOf(e)); }).ToArray();

    static bool Along(EdgeInfo e, Func<Vec3, bool> at) => at(e.Start) && at(e.End);

    [AvaloniaFact(DisplayName = "DIR-H102 | A plate with rounded outer edges and a grid of slots: the outline is fillets, never a slot, and the slots stay one pattern")]
    [Trait("TestId", "DIR-H102")]
    public void RoundedPlateOutlineIsNotASlot()
    {
        using var plate = BoxAt(new Vec3(0, 0, 0), 80, 60, 5);
        Shape part = Occ.Translate(plate, Vec3.Zero);
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 2; j++)
            {
                using var a = CylinderAt(new Vec3(20 + 20 * i, 20 + 20 * j - 4, -1), 2.5, 7);
                using var b = CylinderAt(new Vec3(20 + 20 * i, 20 + 20 * j + 4, -1), 2.5, 7);
                using var mid = BoxAt(new Vec3(17.5 + 20 * i, 16 + 20 * j, -1), 5, 8, 7);
                using var ab = Occ.Fuse(a, b); using var slot = Occ.Fuse(ab, mid);
                var cut = Occ.Cut(part, slot); part.Dispose(); part = cut;
            }
        var outer = Edges(part, e => Along(e, p => p.X < 1e-6 || p.X > 80 - 1e-6 || p.Y < 1e-6 || p.Y > 60 - 1e-6));
        using var rounded = Occ.Fillet(part, outer, 1); part.Dispose();
        var (_, body) = Imported(rounded);
        var feats = FeatureFinder.Features(body);
        var slots = feats.Where(f => f.Kind == FeatureKind.Slot).ToList();
        Assert.Equal(6, slots.Count);
        Assert.All(slots, s => Assert.True(s.Faces.Count <= 8, $"a slot claimed {s.Faces.Count} faces"));
        Assert.DoesNotContain(feats, f => f.Kind is FeatureKind.Hole or FeatureKind.Pocket);
        Assert.Contains(feats, f => f.Kind == FeatureKind.Fillet);
        Assert.Equal(5, FeatureFinder.PatternSize(body, slots[0]));
    }

    [AvaloniaFact(DisplayName = "DIR-H103 | Chamfers are recognised with their setbacks: equal, unequal, and on a round boss edge")]
    [Trait("TestId", "DIR-H103")]
    public void ChamfersAndTheirSetbacks()
    {
        using var box = BoxAt(new Vec3(0, 0, 0), 40, 30, 10);
        var topFront = Edges(box, e => Along(e, p => Math.Abs(p.Z - 10) < 1e-6 && Math.Abs(p.Y) < 1e-6));
        var topBack = Edges(box, e => Along(e, p => Math.Abs(p.Z - 10) < 1e-6 && Math.Abs(p.Y - 30) < 1e-6));
        using var one = Occ.Chamfer(box, topFront, 2);
        var back = Edges(one, e => Along(e, p => Math.Abs(p.Z - 10) < 1e-6 && Math.Abs(p.Y - 30) < 1e-6));
        using var both = Occ.Chamfer2(one, back, 1.5, 3, 0);
        var (_, body) = Imported(both);
        var chamfers = FeatureFinder.Features(body).Where(f => f.Kind == FeatureKind.Chamfer).Select(f => FeatureFinder.ChamferOf(body, f)!).ToList();
        Assert.Equal(2, chamfers.Count);
        var equal = Assert.Single(chamfers, c => c.EqualSetbacks);
        AssertClose(2, equal.SetbackA, 0.01, "equal chamfer setback");
        AssertClose(45, equal.AngleDeg, 0.1, "equal chamfer angle");
        AssertClose(40, equal.Length, 0.01, "chamfer length");
        var unequal = Assert.Single(chamfers, c => !c.EqualSetbacks);
        var sizes = new[] { unequal.SetbackA, unequal.SetbackB }.Order().ToArray();
        AssertClose(1.5, sizes[0], 0.01, "short setback"); AssertClose(3, sizes[1], 0.01, "long setback");

        // a boss with a chamfered top edge
        using var plate = BoxAt(new Vec3(-20, -20, 0), 40, 40, 5);
        using var post = CylinderAt(new Vec3(0, 0, 5), 6, 10);
        using var fused = Occ.Fuse(plate, post);
        var rim = Edges(fused, e => e.Type == "Circle" && Math.Abs(e.Start.Z - 15) < 1e-6);
        using var bevelled = Occ.Chamfer(fused, rim, 1);
        var (_, bossBody) = Imported(bevelled);
        Assert.True(FeatureFinder.Features(bossBody).Count(f => f.Kind == FeatureKind.Chamfer) == 1, FeatureFinder.Describe(bossBody));
        var cone = Assert.Single(FeatureFinder.Features(bossBody), f => f.Kind == FeatureKind.Chamfer);
        var c2 = FeatureFinder.ChamferOf(bossBody, cone)!;
        AssertClose(1, c2.SetbackA, 0.01, "boss chamfer down the wall"); AssertClose(1, c2.SetbackB, 0.01, "boss chamfer across the top");
        Assert.Contains(FeatureFinder.Features(bossBody), f => f.Kind == FeatureKind.Boss);
    }

    static Shape Edited(CadSession session, SceneObject body, Func<Feat, bool> which, Action<Prism.Commands.Command> set)
    {
        var feat = FeatureFinder.Features(body).First(which);
        var command = StartCommand(session, body, () => new Prism.Commands.FeatureEditCommand());
        command.ConfigureInputs(() => { Assert.True(command.Slots[0].TryAdd(new SelItem(body.Id, SelKind.Face, feat.Face))); set(command); });
        var output = command.TryBuild();
        Assert.True(output != null, command.Error);
        var shape = Assert.Single(output.Replace).New;
        AssertValidSolid(shape);
        session.CancelCommand();
        return shape;
    }

    [AvaloniaTheory(DisplayName = "DIR-H105 | Edit Feature resizes or deletes a recognised chamfer and swaps chamfers and fillets, by the exact volume")]
    [InlineData("chamfer", "Resize", 3.0)]
    [InlineData("chamfer", "Delete", 0.0)]
    [InlineData("chamfer", "Change type", 2.0)]
    [InlineData("fillet", "Change type", 2.0)]
    [Trait("TestId", "DIR-H105")]
    public void ChamferAndFilletActions(string start, string action, double size)
    {
        // a 40 x 30 x 10 block with one top edge (40 mm long) chamfered or rounded by 2 mm
        using var box = BoxAt(new Vec3(0, 0, 0), 40, 30, 10);
        var edge = Edges(box, e => Along(e, p => Math.Abs(p.Z - 10) < 1e-6 && Math.Abs(p.Y) < 1e-6));
        using var part = start == "chamfer" ? Occ.Chamfer(box, edge, 2) : Occ.Fillet(box, edge, 2);
        var (session, body) = Imported(part);
        var kind = start == "chamfer" ? FeatureKind.Chamfer : FeatureKind.Fillet;
        using var result = Edited(session, body, f => f.Kind == kind, c =>
        {
            SetChoice(c, "Action", action);
            if (action == "Resize") SetNumber(c, "New chamfer distance (0 = keep)", size);
        });
        double block = 40 * 30 * 10, chamferCut = size * size / 2 * 40, roundCut = (1 - Math.PI / 4) * size * size * 40;
        double expected = action == "Delete" ? block
            : action == "Resize" ? block - chamferCut
            : start == "chamfer" ? block - roundCut     // swapped for a fillet of the same setback
            : block - chamferCut;                       // a fillet swapped for a chamfer of the same setback
        AssertClose(expected, result.Props().Volume, 1e-3, $"{start} {action} volume");
        var check = session.AddObject(result, "Edited", ObjKind.Solid);
        var feats = FeatureFinder.Features(check);
        if (action == "Delete") Assert.DoesNotContain(feats, f => f.Kind is FeatureKind.Chamfer or FeatureKind.Fillet);
        else if (action == "Resize") AssertClose(3, FeatureFinder.ChamferOf(check, feats.Single(f => f.Kind == FeatureKind.Chamfer))!.SetbackA, 0.01, "new setback");
        else Assert.Contains(feats, f => f.Kind == (start == "chamfer" ? FeatureKind.Fillet : FeatureKind.Chamfer));
    }

    /// <summary>A pegboard-like plate: 4 rows of Ø6 holes 20 mm apart, 40 mm along each row, odd rows shifted 20 mm and one hole shorter.</summary>
    static Shape StaggeredBoard()
    {
        using var plate = BoxAt(new Vec3(0, 0, 0), 180, 100, 5);
        Shape part = Occ.Translate(plate, Vec3.Zero);
        for (int b = 0; b < 4; b++)
            for (int a = 0; a < (b % 2 == 0 ? 4 : 3); a++)
            {
                using var drill = CylinderAt(new Vec3(30 + 40 * a + (b % 2 == 0 ? 0 : 20), 20 + 20 * b, -1), 3, 7);
                var cut = Occ.Cut(part, drill); part.Dispose(); part = cut;
            }
        return part;
    }

    static double[][] HoleCentres(SceneObject body) =>
        FeatureFinder.Features(body).Where(f => f.Kind == FeatureKind.Hole).Select(f => new[] { Math.Round(f.Round!.Origin.X, 3), Math.Round(f.Round.Origin.Y, 3) })
            .OrderBy(c => c[1]).ThenBy(c => c[0]).ToArray();

    [AvaloniaTheory(DisplayName = "DIR-H109 | A staggered grid reads as rows, and Edit Pattern changes the row spacing or removes the stagger")]
    [InlineData("read")]
    [InlineData("row spacing")]
    [InlineData("unstagger")]
    [Trait("TestId", "DIR-H109")]
    public void StaggeredRows(string what)
    {
        var (session, body) = Imported(StaggeredBoard());
        var first = FeatureFinder.Features(body).Where(f => f.Kind == FeatureKind.Hole).OrderBy(f => f.Centre.Y).ThenBy(f => f.Centre.X).First();
        Assert.Equal("14 holes in staggered rows: 40 mm apart along X, 4 rows 20 mm apart along Y, every other row shifted 20 mm", FeatureFinder.PatternText(body, first));
        if (what == "read") return;
        var command = StartCommand(session, body, () => new Prism.Commands.EditPatternCommand());
        command.ConfigureInputs(() =>
        {
            Assert.True(command.Slots[0].TryAdd(new SelItem(body.Id, SelKind.Face, first.Face)));
            if (what == "row spacing") SetNumber(command, "Spacing 2", 25);
            else SetChoice(command, "Every other row", "Not shifted");
        });
        var output = command.TryBuild();
        Assert.True(output != null, command.Error);
        var shape = Assert.Single(output.Replace).New; AssertValidSolid(shape);
        var edited = session.AddObject(shape, "Edited", ObjKind.Solid);
        var expected = new List<double[]>();
        for (int b = 0; b < 4; b++)
            for (int a = 0; a < (b % 2 == 0 ? 4 : 3); a++)
                expected.Add([30 + 40 * a + (b % 2 == 0 || what == "unstagger" ? 0 : 20), 20 + (what == "row spacing" ? 25 : 20) * b]);
        Assert.Equal(expected.OrderBy(c => c[1]).ThenBy(c => c[0]).Select(c => $"{c[0]},{c[1]}"), HoleCentres(edited).Select(c => $"{c[0]},{c[1]}"));
    }

    /// <summary>The solid of the word "SLIM" (Arial, 10 mm capitals) from z0 to z1, starting at x = 10, y = 10.</summary>
    static Shape Letters(double z0, double z1)
    {
        var outlines = TextOutline.Build("SLIM", "Arial", false, 10);
        Shape? all = null;
        foreach (var c in outlines)
        {
            using var wire = Occ.Polyline(c.Select(p => new Vec3(10 + p.u, 10 + p.v, z0)).ToList(), true);
            using var face = Occ.Profile([wire], true);
            var letter = Occ.Extrude(face, new Vec3(0, 0, z1 - z0));
            if (all == null) all = letter; else { var f = Occ.Fuse(all, letter); all.Dispose(); letter.Dispose(); all = f; }
        }
        return all!;
    }

    [AvaloniaTheory(DisplayName = "DIR-H110 | Engraved text is one feature with its depth and letter count; it can be deleted, deepened, embossed or moved")]
    [InlineData("read")]
    [InlineData("Delete")]
    [InlineData("Resize")]
    [InlineData("Change type")]
    [InlineData("Move")]
    [Trait("TestId", "DIR-H110")]
    public void EngravedText(string action)
    {
        using var plate = BoxAt(new Vec3(0, 0, 0), 80, 30, 5);
        using var cutter = Letters(4.2, 6);
        using var part = Occ.Cut(plate, cutter);
        using var deep = Letters(0, 0.8); double letters = deep.Props().Volume;   // the letters' own volume per 0.8 mm
        var (session, body) = Imported(part);
        var text = Assert.Single(FeatureFinder.Features(body), f => f.Kind == FeatureKind.Text);
        var info = FeatureFinder.TextOf(body, text)!;
        Assert.True(info.Engraved); Assert.Equal(4, info.Letters);
        AssertClose(0.8, info.Depth, 1e-4, "depth");
        Assert.True(info.Height > 9.9 && info.Height < 11, $"letter height {info.Height}");   // 10 mm capitals; round letters such as S overshoot a little
        Assert.True(Math.Abs(info.Along.X) > 0.99, $"reads along X, got {info.Along}");
        if (action == "read") return;
        using var result = Edited(session, body, f => f.Kind == FeatureKind.Text, c =>
        {
            SetChoice(c, "Action", action);
            if (action == "Resize") SetNumber(c, "New text depth or height (0 = keep)", 1.6);
            if (action == "Move") SetNumber(c, "Move X", 5);
        });
        double block = 80 * 30 * 5;
        double expected = action switch { "Delete" => block, "Resize" => block - 2 * letters, "Change type" => block + letters, _ => block - letters };
        AssertClose(expected, result.Props().Volume, 1e-2, $"{action} volume");
        if (action is "Change type" or "Move")
        {
            var check = session.AddObject(result, "Edited", ObjKind.Solid);
            var after = Assert.Single(FeatureFinder.Features(check), f => f.Kind == FeatureKind.Text);
            var ti = FeatureFinder.TextOf(check, after)!;
            Assert.Equal(action == "Move", ti.Engraved);
            if (action == "Move") AssertClose(text.Centre.X + 5, after.Centre.X, 1e-3, "text moved");
            else AssertClose(5, ti.PlaneHeight, 1e-4, "embossed on the top face");
        }
    }

    [AvaloniaTheory(DisplayName = "DIR-H113 | A modeled thread is a threaded hole with its size and pitch; it can be deleted or turned back into a plain bore")]
    [InlineData("read")]
    [InlineData("Delete")]
    [InlineData("Change type")]
    [Trait("TestId", "DIR-H113")]
    public void ThreadedHole(string action)
    {
        // a Ø5 hole threaded M6 × 1 by Edit Feature, then written to STEP and read back
        using var block = BoxAt(new Vec3(0, 0, 0), 30, 30, 12);
        using var drill = CylinderAt(new Vec3(15, 15, -1), 2.5, 14);
        using var plain = Occ.Cut(block, drill);
        var (s0, b0) = Imported(plain);
        using var tapped = Edited(s0, b0, f => f.Kind == FeatureKind.Hole, c => { SetChoice(c, "Action", "Change type"); SetChoice(c, "New hole type", "Threaded (modeled)"); });
        var (session, body) = Imported(tapped);
        var thread = Assert.Single(FeatureFinder.Features(body), f => f.Kind == FeatureKind.Thread);
        var info = FeatureFinder.ThreadOf(body, thread)!;
        Assert.Equal("M6 × 1", info.Size);
        AssertClose(1, info.Pitch, 1e-9, "pitch"); AssertClose(6, info.Major, 0.05, "major diameter"); AssertClose(12, info.Length, 0.01, "thread length");
        Assert.DoesNotContain(FeatureFinder.Features(body), f => f.Kind == FeatureKind.Fillet);
        if (action == "read") return;
        using var result = Edited(session, body, f => f.Kind == FeatureKind.Thread, c => SetChoice(c, "Action", action));
        if (action == "Delete") { AssertClose(30 * 30 * 12, result.Props().Volume, 1e-3, "filled"); return; }
        var check = session.AddObject(result, "Plain", ObjKind.Solid);
        var bore = Assert.Single(FeatureFinder.Features(check), f => f.Kind == FeatureKind.Hole);
        AssertClose(info.Minor / 2, bore.Round!.Radius, 1e-4, "plain bore at the minor diameter");
    }

    [AvaloniaFact(DisplayName = "DIR-H114 | A rib on a plate is recognised with its thickness, and Resize thickens it about its centre plane")]
    [Trait("TestId", "DIR-H114")]
    public void RibThickness()
    {
        using var plate = BoxAt(new Vec3(0, 0, 0), 60, 40, 5);
        using var web = BoxAt(new Vec3(10, 18, 5), 30, 4, 20);
        using var part = Occ.Fuse(plate, web);
        var (session, body) = Imported(part);
        var rib = Assert.Single(FeatureFinder.Features(body), f => f.Kind == FeatureKind.Rib);
        var info = FeatureFinder.RibOf(body, rib)!;
        AssertClose(4, info.Thickness, 1e-6, "rib thickness"); AssertClose(20, info.Height, 1e-6, "rib height");
        using var result = Edited(session, body, f => f.Kind == FeatureKind.Rib, c => { SetChoice(c, "Action", "Resize"); SetNumber(c, "New rib thickness (0 = keep)", 6); });
        AssertClose(part.Props().Volume + 2 * 30 * 20, result.Props().Volume, 1e-3, "thicker rib");
        var check = session.AddObject(result, "Thicker", ObjKind.Solid);
        var after = FeatureFinder.RibOf(check, Assert.Single(FeatureFinder.Features(check), f => f.Kind == FeatureKind.Rib))!;
        AssertClose(6, after.Thickness, 1e-6, "new thickness");
        AssertClose(20, (check.Shape!.Props().Min.Y + check.Shape.Props().Max.Y) / 2, 1e-6, "plate still centred");
    }

    [AvaloniaFact(DisplayName = "DIR-H117 | Each solid lists its recognised features in the Browser, grouped by type and size; a line selects its faces")]
    [Trait("TestId", "DIR-H117")]
    public void FeaturesList()
    {
        var lines = FeatureSummary.For(Imported(StaggeredBoard()).body);
        var holes = Assert.Single(lines);
        Assert.Equal("14 × Ø6 through (M6 nominal) — in staggered rows: 40 mm apart along X, 4 rows 20 mm apart along Y, every other row shifted 20 mm", holes.Text);
        Assert.Equal(14, holes.Faces.Count);

        using var box = BoxAt(new Vec3(0, 0, 0), 50, 30, 10);   // the word runs from x = 10.6 to 41.5
        using var bevel = Occ.Chamfer(box, Edges(box, e => Along(e, p => Math.Abs(p.Z - 10) < 1e-6 && Math.Abs(p.Y) < 1e-6)), 1);
        using var round = Occ.Fillet(bevel, Edges(bevel, e => Along(e, p => Math.Abs(p.Z) < 1e-6)), 2);
        using var engraved = Letters(9.2, 11); using var part = Occ.Cut(round, engraved);
        var vm = new Prism.ViewModels.MainViewModel();
        var path = Path.Combine(Path.GetTempPath(), $"features-{Guid.NewGuid():N}.step");
        try { Occ.ExportStep([part], path); vm.Session.ImportStep(path); } finally { File.Delete(path); }
        vm.RebuildBrowser();
        var folder = Assert.Single(vm.AllBrowserNodes(), n => n.IsFolder && n.Title == "Features");
        var texts = folder.Children.Select(c => c.Title).ToList();
        Assert.Contains("Chamfer 1 mm × 45° (1 face)", texts);
        Assert.Contains(texts, t => t.StartsWith("Fillets R2 ("));
        Assert.Contains(texts, t => t.StartsWith("Text: 4 letters") && t.EndsWith("engraved 0.8 mm"));
        Assert.All(folder.Children, c => Assert.True(c.Kind == Prism.ViewModels.NodeKind.Feature && c.FeatureFaces!.Count > 0));
    }

    [AvaloniaFact(DisplayName = "DIR-H104 | Holes report through or blind, depth, drill point and the matching metric size")]
    [Trait("TestId", "DIR-H104")]
    public void HoleEndsAndStandardSizes()
    {
        using var block = BoxAt(new Vec3(0, 0, 0), 60, 30, 20);
        using var through = CylinderAt(new Vec3(10, 15, -1), 3.3, 22);              // Ø6.6: M6 clearance
        using var blind = CylinderAt(new Vec3(30, 15, 8), 2.5, 13);                 // Ø5, 12 deep from the top: M6 tap drill
        using var drilled = CylinderAt(new Vec3(50, 15, 10), 3.4, 11);              // Ø6.8 with a 118° point below it: M8 tap drill
        double tip = 3.4 / Math.Tan(59 * Math.PI / 180);
        using var cone = Occ.Cone(0, 3.4, tip);
        using var point = Occ.Translate(cone, new Vec3(50, 15, 10 - tip) - new Vec3(cone.Props().Center.X, cone.Props().Center.Y, cone.Props().Min.Z));
        using var c1 = Occ.Cut(block, through); using var c2 = Occ.Cut(c1, blind); using var c3 = Occ.Cut(c2, drilled); using var part = Occ.Cut(c3, point);
        var (_, body) = Imported(part);
        var holes = FeatureFinder.Features(body).Where(f => f.Kind == FeatureKind.Hole).ToDictionary(f => Math.Round(2 * f.Round!.Radius, 2), f => FeatureFinder.HoleOf(body, f)!);
        Assert.Equal(3, holes.Count);
        Assert.True(holes[6.6].Through); Assert.Equal("M6 clearance (medium)", holes[6.6].Standard); AssertClose(20, holes[6.6].Depth, 0.01, "through depth");
        Assert.False(holes[5].Through); Assert.Null(holes[5].PointAngleDeg); AssertClose(12, holes[5].Depth, 0.01, "blind depth"); Assert.Equal("M6 tap drill", holes[5].Standard);
        Assert.False(holes[6.8].Through); AssertClose(118, holes[6.8].PointAngleDeg!.Value, 0.1, "drill point"); AssertClose(10, holes[6.8].Depth, 0.01, "depth to the point");
        Assert.Equal("M8 tap drill", holes[6.8].Standard);
    }
}
