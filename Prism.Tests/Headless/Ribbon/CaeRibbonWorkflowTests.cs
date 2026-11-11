using Avalonia.Headless.XUnit;
using Prism.Ribbon;
using Prism.ViewModels;
using Prism.Commands;
using Prism.Core;
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
    [InlineData("Fluids", "Pipe Calculations|3D Flow")]
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

    [AvaloniaTheory(DisplayName = "CAE-H002 | Analysis panels expose setup and solve controls without running a solver")]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("TestId", "CAE-H002")]
    public void AnalysisPanelGroupsKeepTheirInputs(bool thermal)
    {
        var session = new CadSession();
        session.StartCommand(() => new CaeSolveCommand(thermal));
        var command = Assert.IsType<CaeSolveCommand>(session.ActiveCommand);
        Assert.Contains(command.WorkflowSections, s => s.Title == "Setup" && s.IsVisible);
        Assert.Contains(command.WorkflowSections, s => s.Title == "Solve" && s.IsVisible);
        Assert.Contains(command.WorkflowSections, s => s.Title == "Mesh" && s.IsVisible);
        if (thermal)
        {
            Assert.All(command.Slots.Skip(1), slot => Assert.Equal("Heating & Cooling", slot.Section));
            command.Params.OfType<ChoiceParam>().Single(p => p.Label == "Thermal analysis").Index = 1;
            Assert.Contains(command.Params, p => p.Label == "Duration" && p.IsVisible);
        }
        else
        {
            Assert.Contains(command.Params, p => p.Label == "Mode to display" && p.Section == "Results" && p.IsVisible);
            Assert.Contains(command.Params, p => p.Label == "Show mode" && p.Section == "Results" && p.IsVisible);
        }
    }

    [AvaloniaFact(DisplayName = "CAE-H003 | Pipe calculations remain live and 3D flow groups its controls")]
    [Trait("TestId", "CAE-H003")]
    public void FlowPanelGroupsPreserveCalculations()
    {
        var session = new CadSession();
        session.StartCommand(() => new PipeFlowCommand());
        var pipe = Assert.IsType<PipeFlowCommand>(session.ActiveCommand);
        Assert.All(pipe.Params, p => Assert.Equal("Setup", p.Section));
        Assert.Contains(pipe.Results, r => r.Label == "Pressure drop");
        session.CancelCommand();
        session.StartCommand(() => new FlowStudyCommand());
        var flow = Assert.IsType<FlowStudyCommand>(session.ActiveCommand);
        Assert.Contains(flow.Params, p => p.Label == "Run flow" && p.Section == "Solve");
        Assert.Contains(flow.Params, p => p.Label == "Show" && p.Section == "Results");
    }
}
