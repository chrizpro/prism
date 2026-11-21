using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Freeform;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/freeform/README.md#knife-and-patterns">Freeform Knife and patterns.</seealso>
[Trait("Feature", "Freeform")]
[Trait("Layer", "Headless")]
public class KnifeCageTests
{
    [AvaloniaFact(DisplayName = "FRM-H124 | The cage is drawn while Knife is open: every form's before one is picked, and with snap points after")]
    [Trait("TestId", "FRM-H124")]
    public void KnifeShowsTheCage()
    {
        var session = new CadSession();
        var make = Begin(session, () => new FormBoxCommand());
        session.CommitCommand();
        var form = session.Objects.Last(o => o.Kind == ObjKind.Form);
        int edges = form.Form!.Edges().Count();

        var knife = Begin(session, () => new FormKnifeCommand());
        var before = new Overlay();
        knife.BuildOverlay(session, before);
        Assert.True(before.Lines.Count >= edges, "the cage edges are on show before a form is picked");

        Pick(knife, "Freeform to cut", form);
        var after = new Overlay();
        knife.BuildOverlay(session, after);
        Assert.True(after.Lines.Count >= edges);
        Assert.True(after.Dots.Count >= form.Form.V.Count, "cage points are shown, since clicks snap to them");
    }
}
