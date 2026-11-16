using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Prism.Ribbon;
using Prism.ViewModels;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Surfacing;

/// <summary>Project and Wrap were two near-identical tools; Wrap is now a method of Project Curve (owner request 2026-11-14).</summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/surface-and-nurbs/README.md">Surface and NURBS workflows.</seealso>
[Trait("Feature", "Surfacing")]
[Trait("Layer", "Headless")]
public class ProjectWrapMergeTests
{
    [AvaloniaFact(DisplayName = "SFC-H022 | Project Curve's Wrap method lays a 31.4 mm line half way round a 10 mm cylinder with its length kept")]
    [Trait("TestId", "SFC-H022")]
    public void ProjectWrapKeepsLength()
    {
        var session = new CadSession();
        var drum = session.AddObject(CylinderAt(new(0, 0, 0), 10, 20), "Drum", ObjKind.Solid);
        double half = 5 * Math.PI;
        var line = session.AddObject(Occ.Polyline([new(-half, -12, 10), new(half, -12, 10)], false), "Line", ObjKind.Curve);
        var command = Begin(session, () => new ProjectCurveCommand());
        Pick(command, "Curves to project", line);
        Pick(command, "Surface to project onto", drum);
        SetChoice(command, "Method", "Wrap (lay on by distance)");
        command.OnChanged();
        Assert.True(command.Params.OfType<NumberParam>().Single(p => p.Label == "Rotate").IsVisible);
        Assert.False(command.Params.OfType<ChoiceParam>().Single(p => p.Label == "Direction").IsVisible);
        var wrapped = Assert.Single(BuildOutput(command).Add).Shape!;
        AssertClose(20, wrapped.Props().Size.X, 0.05, "Width round the drum (mm)");
        AssertClose(10, wrapped.Props().Size.Y, 0.05, "Depth of the half turn (mm)");
    }

    [AvaloniaFact(DisplayName = "SFC-H023 | The ribbon offers one Project / Wrap tool, not a separate Wrap, and old Project steps keep their values")]
    [Trait("TestId", "SFC-H023")]
    public void OneToolOnTheRibbon()
    {
        var vm = new MainViewModel();
        static IEnumerable<RibbonButton> Flat(IEnumerable<RibbonButton> buttons) { foreach (var b in buttons) { yield return b; if (b.Menu != null) foreach (var i in Flat(b.Menu.OfType<RibbonButton>())) yield return i; } }
        var labels = Flat(vm.Tabs.Single(t => t.Title == "Surface").Buttons).Select(b => b.Label.Replace("\n", " ")).ToList();
        Assert.Contains("Project / Wrap", labels);
        Assert.DoesNotContain("Wrap", labels);

        var session = new CadSession();
        var source = Begin(session, () => new ProjectCurveCommand());
        SetChoice(source, "Method", "Pull normal to the surface");
        var saved = source.Capture()!;
        var old = saved with { Values = saved.Values.Take(2).ToList() };       // only Method and Direction existed
        var reopened = Begin(session, () => new ProjectCurveCommand());
        reopened.Restore(old);
        Assert.Equal(1, reopened.Params.OfType<ChoiceParam>().Single(p => p.Label == "Method").Index);
    }
}
