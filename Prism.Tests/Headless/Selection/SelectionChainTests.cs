using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Core.Sketching;
using Prism.Kernel;
using Prism.ViewModels;
using Xunit;

namespace Prism.HeadlessTests.Selection;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/interface/selection/README.md">Selection specification.</seealso>
[Trait("Feature", "Selection")]
[Trait("Layer", "Headless")]
public class SelectionChainTests
{
    static CadSession Session() => new MainViewModel().Session;

    /// <summary>The edge whose midpoint is nearest to the given world point.</summary>
    static int EdgeAt(SceneObject o, Vec3 mid)
    {
        int best = -1; double bd = double.MaxValue;
        for (int i = 0; i < o.Shape.Count(SubKind.Edge); i++)
        {
            var e = Occ.EdgeInfoOf(o.Shape.Sub(SubKind.Edge, i));
            var d = ((e.Start + e.End) * .5 - mid).Length;
            if (d < bd) { bd = d; best = i; }
        }
        Assert.True(bd < 1e-3, $"no edge midpoint near {mid}");
        return best;
    }

    static SelectionSlot EdgeSlot(int rule = 0) => new() { Filter = SelFilter.Edge, Multi = true, SelectionRuleIndex = rule };

    [AvaloniaFact(DisplayName = "SEL-H001 | Connected chain expands over a closed sketch loop and stops at open ends")]
    [Trait("TestId", "SEL-H001")]
    public void ConnectedChainExpands()
    {
        var s = Session();
        var rect = Support.ModelingScenario.CreateRectangleSketch(s, Plane3.XY, 20, 30);
        var bottom = EdgeAt(rect, new Vec3(10, 0, 0));
        Assert.Equal(4, SelectionChains.Expand(s, new SelItem(rect.Id, SelKind.Edge, bottom), tangent: false).Count);

        var open = Support.ModelingScenario.CreateCurve(s, [new Vec3(0, 0, 0), new Vec3(10, 0, 0), new Vec3(20, 0, 0), new Vec3(30, 10, 0)]);
        var mid = EdgeAt(open, new Vec3(15, 0, 0));
        var chain = SelectionChains.Expand(s, new SelItem(open.Id, SelKind.Edge, mid), tangent: false);
        Assert.Equal(3, chain.Count);                                        // an open chain still grows both ways to the free ends
    }

    [AvaloniaFact(DisplayName = "SEL-H002 | A connected chain stops at a branch junction instead of guessing")]
    [Trait("TestId", "SEL-H002")]
    public void ConnectedChainStopsAtJunction()
    {
        var s = Session();
        var box = Support.ModelingScenario.CreateBoxBody(s, Vec3.Zero, 20, 20, 20);
        var edge = EdgeAt(box, new Vec3(10, 0, 0));
        Assert.Single(SelectionChains.Expand(s, new SelItem(box.Id, SelKind.Edge, edge), tangent: false));  // every box vertex meets 3 edges
    }

    [AvaloniaFact(DisplayName = "SEL-H003 | Tangent chain follows smooth joints only; connected chain takes all")]
    [Trait("TestId", "SEL-H003")]
    public void TangentChainFollowsSmoothJoints()
    {
        var s = Session();
        var sketch = new SketchModel();
        int p0 = sketch.AddPoint(0, 0), p1 = sketch.AddPoint(10, 0), p2 = sketch.AddPoint(20, 0.3), p3 = sketch.AddPoint(30, 3);
        sketch.AddLine(p0, p1); sketch.AddLine(p1, p2); sketch.AddLine(p2, p3);   // ~2° joint at p1 (smooth), ~15° joint at p2 (corner)
        s.EnterSketch(Plane3.XY, sketch); s.ExitSketch(keep: true);
        var obj = s.Objects.Last(o => o.Kind == ObjKind.Sketch);
        var seed = new SelItem(obj.Id, SelKind.Edge, EdgeAt(obj, new Vec3(5, 0, 0)));
        Assert.Equal(3, SelectionChains.Expand(s, seed, tangent: false).Count);
        Assert.Equal(2, SelectionChains.Expand(s, seed, tangent: true).Count);   // stops at the 15° corner
    }

    [AvaloniaFact(DisplayName = "SEL-H004 | Partial loop takes the shorter path, Other path takes the longer way round")]
    [Trait("TestId", "SEL-H004")]
    public void PartialLoopPicksPath()
    {
        var s = Session();
        var rect = Support.ModelingScenario.CreateRectangleSketch(s, Plane3.XY, 20, 30);
        var bottom = new SelItem(rect.Id, SelKind.Edge, EdgeAt(rect, new Vec3(10, 0, 0)));
        var right = new SelItem(rect.Id, SelKind.Edge, EdgeAt(rect, new Vec3(20, 15, 0)));
        var slot = EdgeSlot(3);                                                // "Partial loop"
        SelectionChains.Pick(s, slot, bottom);                                 // first pick = start edge
        Assert.Single(slot.Items);
        SelectionChains.Pick(s, slot, right);                                  // second pick = end edge
        Assert.Equal(2, slot.Items.Count);                                     // bottom + right is the short way
        slot.OtherPath = true; SelectionChains.UpdatePartial(s, slot);
        Assert.Equal(4, slot.Items.Count);                                     // the other way round is bottom+left+top+right
        slot.OtherPath = false; SelectionChains.UpdatePartial(s, slot);
        Assert.Equal(2, slot.Items.Count);
    }

    [AvaloniaFact(DisplayName = "SEL-H005 | Partial loop refuses a branch junction and keeps the start edge for a retry")]
    [Trait("TestId", "SEL-H005")]
    public void PartialLoopRejectsJunction()
    {
        var s = Session();
        var sketch = new SketchModel();                                        // open T: junction at (10,0)
        int a = sketch.AddPoint(0, 0), b = sketch.AddPoint(10, 0), c = sketch.AddPoint(20, 0), d = sketch.AddPoint(10, 10);
        sketch.AddLine(a, b); sketch.AddLine(b, c); sketch.AddLine(c, d); sketch.AddLine(d, b);
        s.EnterSketch(Plane3.XY, sketch); s.ExitSketch(keep: true);
        var obj = s.Objects.Last(o => o.Kind == ObjKind.Sketch);
        var slot = EdgeSlot(3);
        SelectionChains.Pick(s, slot, new SelItem(obj.Id, SelKind.Edge, EdgeAt(obj, new Vec3(5, 0, 0))));
        var ex = Assert.Throws<KernelException>(() => SelectionChains.Pick(s, slot, new SelItem(obj.Id, SelKind.Edge, EdgeAt(obj, new Vec3(15, 0, 0)))));
        Assert.Contains("junction", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(slot.Items);                                             // start kept so only the end pick has to be redone
    }

    [AvaloniaFact(DisplayName = "SEL-H006 | Hover preview shows the chain a click would add and stays quiet for single edges")]
    [Trait("TestId", "SEL-H006")]
    public void HoverPreviewShowsChain()
    {
        var s = Session();
        var rect = Support.ModelingScenario.CreateRectangleSketch(s, Plane3.XY, 20, 30);
        var bottom = new SelItem(rect.Id, SelKind.Edge, EdgeAt(rect, new Vec3(10, 0, 0)));
        Assert.Empty(SelectionChains.Preview(s, EdgeSlot(0), bottom));         // Single edges: the hovered edge itself is the preview
        Assert.Equal(3, SelectionChains.Preview(s, EdgeSlot(1), bottom).Count);// Connected chain lights the rest of the loop
        var partial = EdgeSlot(3);
        SelectionChains.Pick(s, partial, bottom);                              // start picked; hovering the right edge shows the short path
        Assert.Single(SelectionChains.Preview(s, partial, new SelItem(rect.Id, SelKind.Edge, EdgeAt(rect, new Vec3(20, 15, 0)))));
        var other = Support.ModelingScenario.CreateRectangleSketch(s, new Plane3(new Vec3(0, 0, 50), Vec3.UnitX, Vec3.UnitY), 5, 5);
        Assert.Empty(SelectionChains.Preview(s, partial, new SelItem(other.Id, SelKind.Edge, 0)));  // a different object is not a loop end
    }

    [AvaloniaFact(DisplayName = "SEL-H007 | Rule names and edge-rule indices stay consistent for profile and plain edge slots")]
    [Trait("TestId", "SEL-H007")]
    public void RuleIndexMapping()
    {
        var plain = EdgeSlot(0);
        Assert.Equal(["Single edges", "Connected chain", "Tangent chain", "Partial loop"], plain.SelectionRules);
        Assert.Equal(0, plain.EdgeRule); Assert.False(plain.IsPartialLoop);
        plain.SelectionRuleIndex = 3;
        Assert.Equal(3, plain.EdgeRule); Assert.True(plain.IsPartialLoop); Assert.Equal(SelFilter.Edge, plain.PickFilter);
        var profile = new SelectionSlot { Filter = SelFilter.Edge | SelFilter.CurveObj, Multi = true, ProfileSelection = true, SelectionRuleIndex = 0 };
        Assert.Equal(["Whole profile", "Single edges", "Connected chain", "Tangent chain", "Partial loop"], profile.SelectionRules);
        Assert.Equal(-1, profile.EdgeRule);                                    // "Whole profile" resolves a clicked curve to its connected profile
        Assert.True(profile.PickFilter.HasFlag(SelFilter.Edge));               // edges stay pickable so the click knows which curve was meant
        profile.SelectionRuleIndex = 1;
        Assert.Equal(0, profile.EdgeRule); Assert.True(profile.PickFilter.HasFlag(SelFilter.Edge));
    }

    [AvaloniaFact(DisplayName = "SEL-H008 | An out-of-range seed edge fails with a clear message")]
    [Trait("TestId", "SEL-H008")]
    public void BadSeedFailsCleanly()
    {
        var s = Session();
        var box = Support.ModelingScenario.CreateBoxBody(s, Vec3.Zero, 10, 10, 10);
        var ex = Assert.Throws<KernelException>(() => SelectionChains.Expand(s, new SelItem(box.Id, SelKind.Edge, 999), false));
        Assert.Contains("no longer exists", ex.Message);
        var gone = Assert.Throws<KernelException>(() => SelectionChains.Expand(s, new SelItem(Guid.NewGuid(), SelKind.Edge, 0), false));
        Assert.Contains("no longer exists", gone.Message);
    }

    [AvaloniaFact(DisplayName = "SEL-H011 | In Face mode a slot with edge rules offers no body edges but still whole curves; Edge and Object modes keep edges")]
    [Trait("TestId", "SEL-H011")]
    public void FaceModeTurnsOffEdgePicksInRuleSlots()
    {
        var s = Session();
        s.StartCommand(() => new LoftCommand());
        var slot = Assert.IsAssignableFrom<SelectionSlot>(s.ActiveCommand!.ActiveSlot);
        Assert.True(slot.HasSelectionRules);
        foreach (var mode in new[] { SelMode.Object, SelMode.Edge })
        {
            s.Mode = mode;
            Assert.True(s.ActiveFilter.HasFlag(SelFilter.Edge), mode.ToString());
        }
        s.Mode = SelMode.Face;
        Assert.False(s.ActiveFilter.HasFlag(SelFilter.Edge));
        Assert.True(s.ActiveFilter.HasFlag(SelFilter.CurveObj), "a whole curve is an object, so it stays pickable");
        Assert.True(s.ActiveFilter.HasFlag(SelFilter.Face), "faces stay pickable for sections");
        s.CancelCommand();
    }
}
