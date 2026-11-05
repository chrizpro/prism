using Avalonia.Headless.XUnit;
using Prism.Core;
using Prism.Kernel;
using Prism.ViewModels;
using Xunit;
using static Prism.HeadlessTests.Solid.SolidRibbonTests;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Solid;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/appearance-studio/README.md">Inspection and material assignment.</seealso>
[Trait("Feature", "Solid")]
[Trait("Layer", "Headless")]
public class AppearanceTests
{
    [AvaloniaFact(DisplayName = "SOL-H077 | Materials applies a material to the selected body and Undo restores it")]
    [Trait("TestId", "SOL-H077")]
    public void MaterialAssignmentIsUndoableAndScoped()
    {
        var vm = new MainViewModel();
        var session = vm.Session;
        var selected = CreateBoxBody(session, Vec3.Zero, 4, 5, 6);
        var other = CreateBoxBody(session, new Vec3(10, 0, 0), 4, 5, 6);
        session.SelectItems([SelItem.Of(selected)]);
        SolidButtons(vm).Single(button => button.Label == "Materials").Invoke();
        Assert.True(vm.ShowMaterials);

        var material = Materials.All.First();
        vm.ApplyMaterial(material);
        Assert.Equal(material.Id, session.Find(selected.Id)!.Look.MaterialId);
        Assert.Null(session.Find(other.Id)!.Look.MaterialId);
        session.Undo();
        Assert.Null(session.Find(selected.Id)!.Look.MaterialId);
    }
}
