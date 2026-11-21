using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Ribbon;
using Prism.ViewModels;
using Xunit;

namespace Prism.HeadlessTests.Surfacing;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/surface-and-nurbs/README.md#2-analysis-on-the-modeling-tabs">0.4 modeling gaps, item 2.</seealso>
[Trait("Feature", "Surfacing")]
[Trait("Layer", "Headless")]
public class AnalyzeDropdownTests
{
    [AvaloniaTheory(DisplayName = "SFC-H018 | Surface and NURBS offer one Analyze dropdown that runs the same commands as the Analyze tab")]
    [InlineData("Surface")]
    [InlineData("NURBS")]
    [Trait("TestId", "SFC-H018")]
    public void AnalyzeDropdownHoldsTheSurfaceQualityChecks(string title)
    {
        AppSettings.Current.Ribbon = new RibbonLayout();
        var vm = new MainViewModel();
        var tab = vm.Tabs.Single(t => t.Title == title);
        var group = tab.Groups.Single(g => g.Title == "Analyze");
        var menu = Assert.Single(group.Items.OfType<RibbonButton>());
        Assert.Equal("menu:analyze", menu.Id);
        Assert.Equal(new[] { "Zebra\nStripes", "Curvature\nMap", "Draft\nAnalysis", "Check\nSeam" }, menu.Menu!.Select(b => b.Label));
        Assert.All(menu.Menu!, b => Assert.False(b.HasMenu));
        // Check Seam lives only in the dropdown, not also as its own button
        Assert.Single(vm.AllCommandHits().Where(h => h.Tab == title && h.Label == "Check Seam"));

        menu.Menu!.Single(b => b.Label == "Check\nSeam").Invoke();
        Assert.IsType<SurfaceContinuityCommand>(vm.Session.ActiveCommand);
        vm.Session.CancelCommand();
        menu.Menu!.Single(b => b.Label == "Curvature\nMap").Invoke();
        Assert.IsType<CurvatureCommand>(vm.Session.ActiveCommand);
    }
}
