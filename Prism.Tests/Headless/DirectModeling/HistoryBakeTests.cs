using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.DirectModeling;

/// <summary>Bake history (a step becomes the new start) and Remove history from a body (it keeps its shape and becomes plain).</summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/direct-modeling/README.md">Direct Modeling guide, section 5 "Bake or remove history".</seealso>
[Trait("Feature", "DirectModeling")]
[Trait("Layer", "Headless")]
public sealed class HistoryBakeTests
{
    /// <summary>Two blocks, each a sketch and an extrude: a 20 x 10 x 5 block at the origin and a 10 x 10 x 8 block at x = 30.</summary>
    static (CadSession s, Guid a, Guid b) TwoBlocks()
    {
        var s = new CadSession();
        var sk1 = CreateRectangleSketch(s, Plane3.XY, 20, 10);
        var e1 = StartCommand(s, sk1, () => new ExtrudeCommand()); SetNumber(e1, "Distance", 5); s.CommitCommand();
        var a = s.Objects.Single(o => o.Kind == ObjKind.Solid).Id;
        var sk2 = CreateRectangleSketch(s, Plane3.XY, 10, 10, 30, 0);
        var e2 = StartCommand(s, sk2, () => new ExtrudeCommand()); SetNumber(e2, "Distance", 8); s.CommitCommand();
        var b = s.Objects.Single(o => o.Kind == ObjKind.Solid && o.Id != a).Id;
        return (s, a, b);
    }

    static double Volume(CadSession s, Guid id) => s.Find(id)!.Shape!.Props().Volume;

    [AvaloniaFact(DisplayName = "DIR-H115 | Bake history: the steps up to a step become the start, the shapes stay, Undo stops there, later steps stay editable and the file gets smaller")]
    [Trait("TestId", "DIR-H115")]
    public void BakeHistory()
    {
        var (s, a, b) = TwoBlocks();
        var past = s.HistoryPath().Past;
        Assert.Equal(5, past.Count);   // start, sketch, extrude, sketch, extrude
        var before = s.Objects;
        var dir = Path.Combine(Path.GetTempPath(), $"bake-{Guid.NewGuid():N}"); Directory.CreateDirectory(dir);
        try
        {
            s.Save(Path.Combine(dir, "before.prism"));
            Assert.Equal(2, s.StepsBakedAt(past[2]));   // the first sketch and its extrude
            Assert.True(s.BakeHistory(past[2]));
            var now = s.HistoryPath().Past;
            Assert.Equal(3, now.Count);
            Assert.StartsWith("Baked history (2 steps)", now[0].Label);
            Assert.Equal(before.Select(o => o.Id), s.Objects.Select(o => o.Id));
            AssertClose(1000, Volume(s, a), 1e-6, "first block"); AssertClose(800, Volume(s, b), 1e-6, "second block");
            s.Save(Path.Combine(dir, "after.prism"));
            Assert.True(new FileInfo(Path.Combine(dir, "after.prism")).Length < new FileInfo(Path.Combine(dir, "before.prism")).Length, "baked file is smaller");

            s.Undo(); s.Undo(); s.Undo();
            Assert.Same(s.Root, s.Current);
            Assert.Single(s.Objects, o => o.Kind == ObjKind.Solid);   // the start holds the first block
            s.Redo(); s.Redo();

            // the step after the bake is still a feature you can edit
            Assert.True(s.EditFeature(s.HistoryPath().Past[^1]));
            SetNumber(s.ActiveCommand!, "Distance", 12);
            s.CommitCommand();
            var second = s.Objects.Single(o => o.Kind == ObjKind.Solid && o.Id != a);
            AssertClose(1200, second.Shape!.Props().Volume, 1e-6, "edited second block");
        }
        finally { Directory.Delete(dir, true); }
    }

    [AvaloniaFact(DisplayName = "DIR-H116 | Remove history from a body: it keeps its shape, and editing its earlier feature or another body's no longer changes it")]
    [Trait("TestId", "DIR-H116")]
    public void RemoveHistoryFromBody()
    {
        var (s, a, b) = TwoBlocks();
        var plain = Assert.Single(s.RemoveHistoryFrom([a]));
        Assert.Null(s.Find(a));
        AssertClose(1000, Volume(s, plain), 1e-6, "plain body keeps its shape");
        Assert.Null(s.FeatureNodeFor(plain));
        Assert.StartsWith("Remove history", s.Current.Label);

        // edit the extrude that made the first block: the plain body stays as it was
        var firstExtrude = s.HistoryPath().Past[2];
        Assert.True(s.EditFeature(firstExtrude));
        SetNumber(s.ActiveCommand!, "Distance", 9);
        s.CommitCommand();
        Assert.NotNull(s.Find(plain));
        AssertClose(1000, Volume(s, plain), 1e-6, "plain body after editing its old extrude");
        Assert.Equal(2, s.Objects.Count(o => o.Kind == ObjKind.Solid));

        // edit the other block's extrude: it changes, the plain body does not
        var secondExtrude = s.HistoryPath().Past.First(n => n.Recipe != null && n.Diff().Added.Any(o => o.Kind == ObjKind.Solid && o.Shape!.Props().Min.X > 29));
        Assert.True(s.EditFeature(secondExtrude));
        SetNumber(s.ActiveCommand!, "Distance", 4);
        s.CommitCommand();
        AssertClose(1000, Volume(s, plain), 1e-6, "plain body after editing the other block");
        Assert.Contains(s.Objects, o => o.Kind == ObjKind.Solid && Math.Abs(o.Shape!.Props().Volume - 400) < 1e-6);
    }
}
