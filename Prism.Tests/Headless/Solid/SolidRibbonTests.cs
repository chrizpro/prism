using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;
using Prism.Ribbon;
using Prism.ViewModels;
using Xunit;

namespace Prism.HeadlessTests.Solid;

/// <seealso href="https://github.com/chrizpro/prism/blob/main/docs/modeling/solid/README.md">Solid ribbon guide.</seealso>
[Trait("Feature", "Solid")]
[Trait("Layer", "Headless")]
public class SolidRibbonTests
{
    [AvaloniaFact(DisplayName = "SOL-H050 | Solid omits Pipe, Sweep and Loft while surfacing tabs retain them")]
    [Trait("TestId", "SOL-H050")]
    public void SolidOmitsSurfacingCommands()
    {
        var vm = new MainViewModel();
        var solid = Buttons(vm.Tabs.Single(tab => tab.Title == "Solid").Buttons).ToList();
        Assert.DoesNotContain(solid, button => button.Label is "Pipe" or "Sweep" or "Loft");

        foreach (var title in new[] { "Surface", "Freeform", "NURBS" })
        {
            var labels = Buttons(vm.Tabs.Single(tab => tab.Title == title).Buttons).Select(button => button.Label);
            Assert.Contains("Pipe", labels);
            Assert.Contains("Sweep", labels);
            Assert.Contains("Loft", labels);
        }
    }

    [AvaloniaFact(DisplayName = "SOL-H051 | Direct Edit opens its contextual tab and Finish returns to Solid")]
    [Trait("TestId", "SOL-H051")]
    public void DirectEditEntersAndLeavesContext()
    {
        var vm = new MainViewModel();
        var solid = vm.Tabs.Single(tab => tab.Title == "Solid");
        vm.SelectedTab = solid;
        Buttons(solid.Buttons).Single(button => button.Icon == "pushpull").Invoke();
        Assert.True(vm.IsDirectEditing);
        Assert.Equal("Direct Modeling", vm.SelectedTab?.Title);
        Assert.IsType<DirectEditCommand>(vm.Session.ActiveCommand);

        vm.SelectedTab!.Buttons.Single(button => button.Icon == "finish").Invoke();
        Assert.False(vm.IsDirectEditing);
        Assert.Same(solid, vm.SelectedTab);
        Assert.Null(vm.Session.ActiveCommand);
    }

    [AvaloniaFact(DisplayName = "SOL-H052 | Solid selection controls switch pick mode and rectangle selection")]
    [Trait("TestId", "SOL-H052")]
    public void SelectionControlsChangeSessionMode()
    {
        var vm = new MainViewModel();
        var buttons = SolidButtons(vm);
        buttons.Single(button => button.Label == "Face").Invoke();
        Assert.Equal(SelMode.Face, vm.Session.Mode);
        buttons.Single(button => button.Label == "Edge").Invoke();
        Assert.Equal(SelMode.Edge, vm.Session.Mode);
        buttons.Single(button => button.Label == "Object").Invoke();
        Assert.Equal(SelMode.Object, vm.Session.Mode);

        var rectangle = buttons.Single(button => button.Label == "Rectangle");
        rectangle.Invoke();
        Assert.Equal(SelectMethod.Rectangle, vm.Session.Method);
        rectangle.Invoke();
        Assert.Equal(SelectMethod.Click, vm.Session.Method);
        buttons.Single(button => button.Label == "Lasso").Invoke();
        Assert.Equal(SelectMethod.Lasso, vm.Session.Method);

        bool previous = vm.Session.XRay;
        buttons.Single(button => button.Label == "Through").Invoke();
        Assert.Equal(!previous, vm.Session.XRay);

        var first = Support.ModelingScenario.CreateBoxBody(vm.Session, Vec3.Zero, 2, 2, 2);
        var second = Support.ModelingScenario.CreateBoxBody(vm.Session, new Vec3(5, 0, 0), 2, 2, 2);
        vm.Session.SelectItems([SelItem.Of(first)]);
        buttons.Single(button => button.Label == "Invert").Invoke();
        Assert.Equal(second.Id, Assert.Single(vm.Session.Selection).Obj);
    }

    [AvaloniaFact(DisplayName = "SOL-H053 | Materials opens Appearance and returns to Solid on close")]
    [Trait("TestId", "SOL-H053")]
    public void MaterialsOpensAppearance()
    {
        var vm = new MainViewModel();
        var solid = vm.Tabs.Single(tab => tab.Title == "Solid");
        vm.SelectedTab = solid;
        SolidButtons(vm).Single(button => button.Label == "Materials").Invoke();
        Assert.True(vm.ShowMaterials);
        vm.CloseMaterials();
        Assert.False(vm.ShowMaterials);
        Assert.Same(solid, vm.SelectedTab);
    }

    [AvaloniaFact(DisplayName = "SOL-H054 | The Solid Datum Gizmo opens its settings panel")]
    [Trait("TestId", "SOL-H054")]
    public void DatumGizmoOpensPanel()
    {
        var vm = new MainViewModel();
        SolidButtons(vm).Single(button => button.Label == "Gizmo").Invoke();
        Assert.True(vm.ShowGizmoPanel);
    }

    static IReadOnlyList<RibbonButton> SolidButtons(MainViewModel vm) =>
        Buttons(vm.Tabs.Single(tab => tab.Title == "Solid").Buttons)
            .Where(button => !button.HasMenu).Distinct().ToList();

    static IEnumerable<RibbonButton> Buttons(IEnumerable<RibbonButton> buttons)
    {
        foreach (var button in buttons)
        {
            yield return button;
            if (button.Menu is { } menu)
                foreach (var item in Buttons(menu))
                    yield return item;
        }
    }
}
