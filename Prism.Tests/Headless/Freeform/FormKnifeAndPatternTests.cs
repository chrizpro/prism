using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Core.Form;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Freeform;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/freeform/README.md#knife-and-patterns">Freeform Knife and patterns (FRM-P1).</seealso>
[Trait("Feature", "Freeform")]
[Trait("Layer", "Headless")]
public class FormKnifeAndPatternTests
{
    /// <summary>A 30 x 20 x 10 form box with one face per side, standing on the ground, added to the session.</summary>
    static (CadSession session, SceneObject form) Box()
    {
        var session = new CadSession();
        var make = Begin(session, () => new FormBoxCommand());
        SetNumber(make, "Width (X)", 30); SetNumber(make, "Depth (Y)", 20); SetNumber(make, "Height (Z)", 10);
        SetInteger(make, "Faces X", 1); SetInteger(make, "Faces Y", 1); SetInteger(make, "Faces Z", 1);
        session.CommitCommand();
        return (session, session.Objects.Last(o => o.Kind == ObjKind.Form));
    }

    [AvaloniaFact(DisplayName = "FRM-H110 | Knife cuts a form box across its middle: four walls split, four new points, still closed")]
    [Trait("TestId", "FRM-H110")]
    public void KnifeCutsAcross()
    {
        var (session, form) = Box();
        var before = form.Form!;
        var (min, max) = before.Bounds();
        var knife = Begin(session, () => new FormKnifeCommand());
        Pick(knife, "Freeform to cut", form);
        SetChoice(knife, "Cut with", "A plane");
        SetChoice(knife, "Or cut across", "Bottom to top (Z)");
        SetNumber(knife, "Position along that direction", (min.Z + max.Z) / 2);
        var (old, cut) = Assert.Single(BuildOutput(knife).ReplaceForms);
        Assert.Equal(form.Id, old.Id);
        Assert.True(cut.IsClosed());
        Assert.Equal(before.F.Count + 4, cut.F.Count);
        Assert.Equal(before.V.Count + 4, cut.V.Count);
        Assert.Equal("4", Result(knife, "New points"));
        Assert.Equal(before.F.Count, form.Form!.F.Count);      // the original is untouched until the step is accepted
    }

    [AvaloniaFact(DisplayName = "FRM-H111 | Knife with a crease sharpness makes the four cut edges creases")]
    [Trait("TestId", "FRM-H111")]
    public void KnifeWithACrease()
    {
        var (session, form) = Box();
        var (min, max) = form.Form!.Bounds();
        var knife = Begin(session, () => new FormKnifeCommand());
        Pick(knife, "Freeform to cut", form);
        SetNumber(knife, "Position along that direction", (min.Z + max.Z) / 2);
        SetChoice(knife, "Cut with", "A plane");
        SetChoice(knife, "Or cut across", "Bottom to top (Z)");
        SetNumber(knife, "Crease the cut", 2);
        var (_, cut) = Assert.Single(BuildOutput(knife).ReplaceForms);
        Assert.Equal(4, cut.Crease.Count(c => c.Value == 2));
    }

    [AvaloniaFact(DisplayName = "FRM-H112 | Knife refuses a plane that misses the form, with a message")]
    [Trait("TestId", "FRM-H112")]
    public void KnifeMissing()
    {
        var (session, form) = Box();
        var knife = Begin(session, () => new FormKnifeCommand());
        Pick(knife, "Freeform to cut", form);
        SetChoice(knife, "Cut with", "A plane");
        SetChoice(knife, "Or cut across", "Bottom to top (Z)");
        SetNumber(knife, "Position along that direction", 500);
        AssertBuildFails(knife, "does not cut");
    }

    [AvaloniaFact(DisplayName = "FRM-H113 | A linear pattern copies a freeform: three copies, each moved by the spacing, none sharing the original's cage")]
    [Trait("TestId", "FRM-H113")]
    public void PatternOfAForm()
    {
        var (session, form) = Box();
        var original = form.Form!; var (min0, _) = original.Bounds();
        var pattern = Begin(session, () => new LinearPatternCommand());
        Pick(pattern, "Objects to pattern", form);
        SetInteger(pattern, "Count 1", 3);
        SetNumber(pattern, "Spacing 1", 50);
        var added = BuildOutput(pattern).Add;
        Assert.Equal(2, added.Count);                                  // the original plus two copies
        Assert.All(added, a => { Assert.Equal(ObjKind.Form, a.Kind); Assert.NotNull(a.Form); Assert.NotSame(original, a.Form); });
        var starts = added.Select(a => a.Form!.Bounds().min.X - min0.X).Order().ToArray();
        Assert.Equal([50.0, 100.0], starts.Select(x => Math.Round(x, 6)));
        Assert.All(added, a => Assert.Equal(original.F.Count, a.Form!.F.Count));
    }

    [AvaloniaFact(DisplayName = "FRM-H114 | A circular pattern turns the copies of a freeform about the axis")]
    [Trait("TestId", "FRM-H114")]
    public void CircularPatternOfAForm()
    {
        var (session, form) = Box();
        var pattern = Begin(session, () => new CircularPatternCommand());
        Pick(pattern, "Objects to pattern", form);
        var output = BuildOutput(pattern);
        Assert.NotEmpty(output.Add);
        Assert.All(output.Add, a => Assert.True(a.Form != null && a.Form.IsClosed()));
    }

    [AvaloniaFact(DisplayName = "FRM-H115 | Patterns refuse to join a freeform to another body, with a message")]
    [Trait("TestId", "FRM-H115")]
    public void PatternJoinRefusedForForms()
    {
        var (session, form) = Box();
        var solid = CreateBoxBody(session, new(100, 0, 0), 10, 10, 10);
        var pattern = Begin(session, () => new LinearPatternCommand());
        Pick(pattern, "Objects to pattern", form);
        SetChoice(pattern, "Result", "Join into a body");
        Pick(pattern, "Target body", solid);
        Assert.Null(pattern.TryBuild());
        Assert.Contains(pattern.Results, r => r.Label == "Freeforms");
    }

    // ---- the knife drawn across the form: looking straight down at a 30 x 20 x 10 box, a line along X at y = 0
    static (CadSession session, SceneObject form, FormKnifeCommand knife) TopView(bool snap = false)
    {
        var (session, form) = Box();
        session.Camera.Width = 800; session.Camera.Height = 600; session.Camera.SetView(0, 90);
        session.Camera.Fit(new Vec3(-15, -10, 0), new Vec3(15, 10, 10));
        var knife = Begin(session, () => new FormKnifeCommand());
        Pick(knife, "Freeform to cut", form);
        knife.Params.OfType<BoolParam>().Single(p => p.Label.StartsWith("Snap")).Value = snap;
        return (session, form, knife);
    }

    static void Click(FormKnifeCommand knife, SceneObject form, Vec3 at) =>
        Assert.True(knife.PickPoint(new PickResult(SelItem.Of(form), at, Vec3.UnitZ, 0), false));

    [AvaloniaFact(DisplayName = "FRM-H116 | Knife: two clicks draw a line across the top; it splits the top face and puts a point on each side edge")]
    [Trait("TestId", "FRM-H116")]
    public void KnifeLineByClicks()
    {
        var (_, form, knife) = TopView();
        Click(knife, form, new Vec3(-20, 0, 10)); Click(knife, form, new Vec3(20, 0, 10));
        var before = form.Form!;
        var (_, cut) = Assert.Single(BuildOutput(knife).ReplaceForms);
        Assert.True(cut.IsClosed());
        Assert.Equal(before.F.Count + 1, cut.F.Count);
        Assert.Equal(before.V.Count + 2, cut.V.Count);
        Assert.Equal("2", Result(knife, "New points"));
        Assert.All(cut.V.Skip(before.V.Count), v => { Assert.Equal(0, v.Y, 6); Assert.Equal(10, v.Z, 6); Assert.Equal(15, Math.Abs(v.X), 6); });
    }

    [AvaloniaFact(DisplayName = "FRM-H117 | Knife with Cut through also cuts the faces turned away: two walls, top and bottom")]
    [Trait("TestId", "FRM-H117")]
    public void KnifeCutThrough()
    {
        var (_, form, knife) = TopView();
        knife.Params.OfType<BoolParam>().Single(p => p.Label.StartsWith("Cut through")).Value = true;
        Click(knife, form, new Vec3(-20, 0, 10)); Click(knife, form, new Vec3(20, 0, 10));
        var before = form.Form!;
        var (_, cut) = Assert.Single(BuildOutput(knife).ReplaceForms);
        Assert.True(cut.IsClosed());
        Assert.Equal(before.F.Count + 4, cut.F.Count);
        Assert.Equal(before.V.Count + 4, cut.V.Count);
    }

    [AvaloniaFact(DisplayName = "FRM-H118 | Knife clicks snap to a nearby point of the cage, and Undo takes the last click back")]
    [Trait("TestId", "FRM-H118")]
    public void KnifeSnapAndUndo()
    {
        var (_, form, knife) = TopView(snap: true);
        Click(knife, form, new Vec3(14.9, 9.9, 10));
        var path = knife.Params.OfType<TextParam>().Single(p => p.Label.StartsWith("Points of the cut"));
        Assert.StartsWith("15 10 10 |", path.Value);                       // pulled onto the corner of the cage
        Click(knife, form, new Vec3(-15, -10, 10));
        Assert.Equal(2, path.Value.Split('\n').Length);
        Assert.True(knife.TryUndo());
        Assert.Single(path.Value.Split('\n'));
        Assert.True(knife.TryUndo());
        Assert.False(knife.TryUndo());
    }

    [AvaloniaFact(DisplayName = "FRM-H119 | Knife with one click has nothing to cut yet; a line that misses the form is refused with a message")]
    [Trait("TestId", "FRM-H119")]
    public void KnifeNeedsALineThatCrosses()
    {
        var (_, form, knife) = TopView();
        Click(knife, form, new Vec3(-20, 0, 10));
        Assert.Null(knife.TryBuild());
        var miss = TopView();
        Click(miss.knife, miss.form, new Vec3(-40, 30, 10)); Click(miss.knife, miss.form, new Vec3(40, 30, 10));
        AssertBuildFails(miss.knife, "does not cross");
    }
}
