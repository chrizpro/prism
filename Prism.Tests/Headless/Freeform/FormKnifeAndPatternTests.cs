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
}
