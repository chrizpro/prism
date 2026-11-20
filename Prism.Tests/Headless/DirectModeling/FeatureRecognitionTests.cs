using Avalonia.Headless.XUnit;
using Prism.Core;
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
