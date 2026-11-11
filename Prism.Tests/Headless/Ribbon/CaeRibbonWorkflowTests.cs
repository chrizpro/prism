using Avalonia.Headless.XUnit;
using Prism.Ribbon;
using Prism.ViewModels;
using Xunit;

namespace Prism.HeadlessTests.Ribbon;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/cae/README.md">CAE workflows.</seealso>
[Trait("Feature", "CAE")]
[Trait("Layer", "Headless")]
public class CaeRibbonWorkflowTests
{
    [AvaloniaTheory(DisplayName = "CAE-H001 | CAE ribbons keep setup, solving and results in workflow order")]
    [InlineData("Static", "Select|Setup|Supports|Loads|Solve|Results|Advanced|Reuse & Export")]
    [InlineData("Thermal", "Setup|Reuse|Results")]
    [InlineData("Dynamics", "Setup|Reuse|Results")]
    [Trait("TestId", "CAE-H001")]
    public void WorkflowOrderAndCommandsRemainAvailable(string title, string sections)
    {
        var vm = new MainViewModel();
        var tab = vm.Tabs.Single(t => t.Title == title);
        Assert.Equal(sections.Split('|'), tab.Groups.Select(g => g.Title));
        if (title == "Static")
        {
            Assert.Contains(tab.Groups.Single(g => g.Title == "Setup").Items.OfType<RibbonButton>(), b => b.Label == "Materials");
            Assert.Contains(tab.Groups.Single(g => g.Title == "Supports").Items.OfType<RibbonButton>(), b => b.Label == "Fixed\nSupport" && !b.HasMenu);
            Assert.Contains(tab.Groups.Single(g => g.Title == "Loads").Items.OfType<RibbonButton>(), b => b.Label == "Force" && !b.HasMenu);
            Assert.Contains(tab.Buttons, b => b.Label == "Fatigue");
            Assert.Contains(tab.Buttons, b => b.Label == "Generative\nDesign");
        }
    }
}
