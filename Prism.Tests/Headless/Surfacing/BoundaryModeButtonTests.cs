using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Prism.Ribbon;
using Prism.ViewModels;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Surfacing;

/// <summary>Boundary Surface shows its five intents as buttons, and each names its own picks. Owner request 2026-11-16.</summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/modeling/boundary-surface/README.md">Boundary Surface, Patch and Ruled Surface.</seealso>
[Trait("Feature", "BoundarySurface")]
[Trait("Layer", "Headless")]
public class BoundaryModeButtonTests
{
    static BoundarySurfaceCommand Start(MainViewModel vm)
    {
        var button = vm.Tabs.Single(t => t.Title == "Surface").Buttons
            .SelectMany(b => (IEnumerable<RibbonButton>?)b.Menu ?? [b]).Distinct()
            .Single(b => b.Label.Replace('\n', ' ') == "Boundary Surface");
        button.Invoke();
        return Assert.IsType<BoundarySurfaceCommand>(vm.Session.ActiveCommand);
    }

    [AvaloniaFact(DisplayName = "BND-H030 | Mode is a compact dropdown with the five intents, Automatic first and chosen")]
    [Trait("TestId", "BND-H030")]
    public void ModeIsADropdown()
    {
        var command = Start(new MainViewModel());
        var mode = command.Params.OfType<ChoiceParam>().Single(p => p.Label == "Mode");
        Assert.False(mode.IsIllustrated, "the large button grid took three rows of the panel; Mode is a dropdown like Fill with");
        Assert.Equal(5, mode.Options.Count);
        Assert.Equal("Automatic", mode.Selected);
        Assert.False(command.Params.OfType<ChoiceParam>().Single(p => p.Label == "Fill with").IsIllustrated);      // the same kind of control, so the panel is consistent
    }

    [AvaloniaFact(DisplayName = "BND-H031 | Each mode names its own picks and hides the inside-curves pick where it does not apply; scripts still find the slots by label")]
    [Trait("TestId", "BND-H031")]
    public void ModesRenameThePicks()
    {
        var command = Start(new MainViewModel());
        var boundary = command.Slots.Single(s => s.Label == "Boundary");
        var guides = command.Slots.Single(s => s.Label == "Guide curves");
        var mode = command.Params.OfType<ChoiceParam>().Single(p => p.Label == "Mode");

        SetChoice(command, "Mode", "Network: outline and curves across");
        Assert.Equal("Outline curves (a closed loop)", boundary.DisplayLabel);
        Assert.Equal("Curves across the surface", guides.DisplayLabel);
        Assert.True(guides.IsVisible);

        SetChoice(command, "Mode", "Edge: span two to four curves");
        Assert.Equal("Two to four curves that meet end to end", boundary.DisplayLabel);
        Assert.False(guides.IsVisible);

        SetChoice(command, "Mode", "Blend: bridge two surface edges");
        Assert.Equal("One open edge on each of two surfaces", boundary.DisplayLabel);
        Assert.False(guides.IsVisible);

        SetChoice(command, "Mode", "Automatic");
        Assert.Equal("Boundary", boundary.DisplayLabel);
        Assert.Equal("Guide curves", guides.DisplayLabel);
        Assert.True(guides.IsVisible);
        Assert.Equal("Boundary", boundary.Label);                         // the lookup name for scripts and saved steps never changes
        Assert.Equal(0, mode.Index);
    }
}
