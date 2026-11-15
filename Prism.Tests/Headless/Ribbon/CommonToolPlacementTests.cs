using Avalonia.Headless.XUnit;
using Prism.Ribbon;
using Prism.ViewModels;
using Xunit;

namespace Prism.HeadlessTests.Ribbon;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/tabs/README.md#tools-shared-between-modeling-tabs">Tools shared between modeling tabs.</seealso>
[Trait("Feature", "Ribbon")]
[Trait("Layer", "Headless")]
public sealed class CommonToolPlacementTests
{
    static IEnumerable<RibbonButton> Flatten(IEnumerable<RibbonButton> buttons)
    {
        foreach (var button in buttons)
        {
            yield return button;
            if (button.Menu != null) foreach (var inner in Flatten(button.Menu.OfType<RibbonButton>())) yield return inner;
        }
    }

    static HashSet<string> Labels(MainViewModel vm, string tab) =>
        Flatten(vm.Tabs.Single(t => t.Title == tab).Buttons).Select(b => b.Label.Replace("\n", " ")).ToHashSet();

    [AvaloniaTheory(DisplayName = "RIB-H003 | Tools that work on solids, surfaces and curves alike are on every modeling tab that makes them")]
    [InlineData("Project / Wrap", "Home,Solid,Surface,NURBS")]
    [InlineData("Match Surface", "Surface,NURBS")]
    [InlineData("Linear", "Home,Solid,Surface,NURBS,Freeform")]
    [InlineData("Circular", "Home,Solid,Surface,NURBS,Freeform")]
    [InlineData("Along Curve", "Home,Solid,Surface,NURBS,Freeform")]
    [InlineData("Analyze", "Solid,Surface,NURBS,Freeform")]
    [InlineData("Warp", "Surface,NURBS,Freeform")]
    [InlineData("Knife", "Freeform")]
    [InlineData("Curvature Map", "Solid,Surface,NURBS,Freeform")]
    [InlineData("Draft Analysis", "Solid,Surface,NURBS,Freeform")]
    [InlineData("Thread", "Home,Solid")]
    [InlineData("Hole", "Home,Solid")]
    [InlineData("Thicken", "Surface,NURBS,Freeform")]
    [InlineData("Intersection Curve", "Home,Solid,Surface,NURBS,Freeform")]
    [Trait("TestId", "RIB-H003")]
    public void ToolIsOnTheseTabs(string tool, string tabs)
    {
        var vm = new MainViewModel();
        foreach (var tab in tabs.Split(',')) Assert.True(Labels(vm, tab).Contains(tool), $"{tool} should be on the {tab} tab");
    }

    [AvaloniaFact(DisplayName = "RIB-H004 | Check Seam is offered only where surfaces meet: not in the Solid tab's Analyze dropdown")]
    [Trait("TestId", "RIB-H004")]
    public void CheckSeamStaysWithSurfaces()
    {
        var vm = new MainViewModel();
        Assert.DoesNotContain("Check Seam", Labels(vm, "Solid"));
        Assert.Contains("Check Seam", Labels(vm, "Surface"));
        Assert.Contains("Check Seam", Labels(vm, "NURBS"));
    }

    [AvaloniaTheory(DisplayName = "RIB-H005 | Hole and Thread share one dropdown on Home and Solid, with Hole showing first")]
    [InlineData("Home")]
    [InlineData("Solid")]
    [Trait("TestId", "RIB-H005")]
    public void HoleAndThreadShareADropdown(string tab)
    {
        var vm = new MainViewModel();
        var split = Assert.Single(Flatten(vm.Tabs.Single(t => t.Title == tab).Buttons), b => b.Id == "split:hole-thread");
        Assert.Equal(["Hole", "Thread"], split.Menu!.OfType<RibbonButton>().Select(b => b.Label).ToArray());
        var top = vm.Tabs.Single(t => t.Title == tab).Groups.SelectMany(g => g.Items.OfType<RibbonButton>()).ToList();
        Assert.DoesNotContain(top, b => b.Label == "Thread");      // it lives in the dropdown, not beside it
        Assert.Equal("Hole", Assert.Single(top, b => b.Id == "split:hole-thread").Label);
    }
}
