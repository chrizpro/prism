using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Viewport;

/// <summary>
/// Every hole added in one Hole command keeps its own size arrows, and the slow helical cuts (Thread, threaded holes) do not rebuild live
/// (owner reports 2026-11-15: extra holes lost their handles; Thread froze the window).
/// </summary>
[Trait("Feature", "Viewport handles")]
[Trait("Layer", "Headless")]
public class HoleAndThreadHandleTests
{
    [AvaloniaFact(DisplayName = "HDL-H043 | Each extra hole added in the Hole tool has its own diameter arrow, and pulling any of them sizes them all")]
    [Trait("TestId", "HDL-H043")]
    public void EveryHoleKeepsItsArrow()
    {
        var session = new CadSession();
        var body = CreateBoxBody(session, Vec3.Zero, 60, 40, 10);
        var hole = Begin(session, () => new HoleCommand());
        hole.ConfigureInputs(() => SetSelection(hole, hole.Slots[0].Label, body));
        Assert.Single(hole.Handles);
        var more = hole.Params.OfType<TextParam>().Single(p => p.Label.StartsWith("More holes"));
        hole.ConfigureInputs(() => more.Value = "10, 5\n-10, -5");
        var arrows = hole.Handles;
        Assert.Equal(3, arrows.Count);
        Assert.Equal(3, arrows.Select(a => (Math.Round(a.Origin.X, 3), Math.Round(a.Origin.Y, 3))).Distinct().Count());
        Assert.All(arrows, a => Assert.Same(arrows[0].Param, a.Param));
    }

    [AvaloniaFact(DisplayName = "HDL-H045 | A part moved in Direct Edit keeps its place when an earlier feature of it is edited")]
    [Trait("TestId", "HDL-H045")]
    public void DirectEditMoveSurvivesEditingTheFeature()
    {
        var session = new CadSession();
        Begin(session, () => new BoxCommand()); session.CommitCommand();
        var box = session.Objects.Single();
        session.DirectEditing = true;
        var move = StartCommand(session, box, () => new TransformCommand(TransformCommand.Kind.Move));
        SetNumber(move, "Δ X", 50); SetNumber(move, "Δ Y", 20);
        session.CommitCommand();
        session.DirectEditing = false;
        var moved = session.Objects.Single().Shape.Props().Center;
        Assert.InRange(moved.X, 49, 51);

        var node = session.HistoryPath().Past.First(h => h.Recipe != null);
        Assert.True(session.EditFeature(node));
        SetNumber(session.ActiveCommand!, "Width (X)", 80);
        session.CommitCommand();
        var after = session.Objects.Single().Shape.Props();
        AssertPoint(new Vec3(50, 20, moved.Z), after.Center, 1e-6, "The box is still where it was moved after its width changed (mm)");
        Assert.InRange(after.Size.X, 79.9, 80.1);
    }

    [AvaloniaFact(DisplayName = "HDL-H044 | Thread and threaded holes start with the live preview off, so choosing them does not freeze the window")]
    [Trait("TestId", "HDL-H044")]
    public void SlowCutsStartWithoutLivePreview()
    {
        var session = new CadSession();
        var thread = Begin(session, () => new ThreadCommand());
        Assert.False(thread.PreviewEnabled);
        session.CancelCommand();

        var body = CreateBoxBody(session, Vec3.Zero, 40, 40, 10);
        var hole = Begin(session, () => new HoleCommand());
        Assert.True(hole.PreviewEnabled);                       // a plain hole is quick, so it keeps its live preview
        hole.ConfigureInputs(() => SetSelection(hole, hole.Slots[0].Label, body));
        SetChoice(hole, "Hole type", "Threaded (modeled)");
        Assert.False(hole.PreviewEnabled);
    }
}
