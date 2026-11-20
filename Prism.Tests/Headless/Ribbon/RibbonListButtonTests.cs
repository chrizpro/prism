using Avalonia.Headless.XUnit;
using Prism.Ribbon;
using Prism.ViewModels;
using Xunit;

namespace Prism.HeadlessTests.Ribbon;

/// <summary>
/// Every button with a list behaves the same (owner rule 2026-11-20): a click runs the action picked last, or a default (the first that can run)
/// when nothing was picked yet; picking from the list runs that action and makes it the one the next click runs.
/// </summary>
[Trait("Feature", "Ribbon")]
[Trait("Layer", "Headless")]
public sealed class RibbonListButtonTests
{
    static IEnumerable<RibbonButton> All(MainViewModel vm) =>
        vm.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Items).OfType<RibbonButton>();

    [AvaloniaFact(DisplayName = "RIB-H006 | Every list button runs an action when clicked: its last pick, or a default; picking from the list changes what the next click runs")]
    [Trait("TestId", "RIB-H006")]
    public void EveryListButtonRunsAnAction()
    {
        var picks = new Dictionary<string, string>();
        void Hooks() { RibbonButton.RememberPick = (b, v) => picks[b.Id] = v.Label; RibbonButton.RecallPick = b => picks.TryGetValue(b.Id, out var l) ? l : null; }
        try
        {
            var vm = new MainViewModel(); Hooks();   // the view model installs the app's own store; the test keeps picks to itself
            var lists = All(vm).Where(b => b.HasMenu).ToList();
            Assert.NotEmpty(lists);
            Assert.All(lists, b => Assert.True(b.IsSplit && b.Active != null, $"'{b.Label}' must run an action when clicked"));

            // a plain list (Export As) keeps its name and runs what was picked last
            var export = lists.First(b => b.Id == "menu:export-as");
            var step = export.Menu!.Single(v => v.Label.StartsWith("STEP"));
            export.Pick(step);
            Assert.Same(step, export.Active);
            Assert.Equal("Export As", export.Label);

            // the selection-level button: after choosing Edge, a click on it runs Edge again
            var levels = lists.First(b => b.Id == "split:selection-components");
            var edge = levels.Menu!.Single(v => v.Label == "Edge");
            levels.Pick(edge);
            Assert.Same(edge, levels.Active);

            // remembered for the next session
            var again = new MainViewModel(); Hooks();
            Assert.Equal("STEP (.step)", All(again).First(b => b.Id == "menu:export-as").Active!.Label);
        }
        finally { RibbonButton.RememberPick = null; RibbonButton.RecallPick = null; }
    }
}
