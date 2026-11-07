using Avalonia.Headless.XUnit;
using Prism.Ribbon;
using Prism.ViewModels;
using Xunit;

namespace Prism.HeadlessTests.Selection;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/interface/selection/README.md">Selection specification.</seealso>
[Trait("Feature", "Selection")]
[Trait("Layer", "Headless")]
public class SelectionRibbonTests
{
    /// <summary>The group's parent buttons plus each split's variants, in order.</summary>
    static string[] FlatLabels(RibbonGroup group) => group.Items.OfType<RibbonButton>()
        .SelectMany(b => b.Menu == null ? [b] : b.Menu.Prepend(b))
        .Select(b => b.Label.Replace("\n", " ")).ToArray();

    [AvaloniaFact(DisplayName = "SEL-H009 | Every modeling tab's Select group offers the same pick levels, drag methods, Invert and Through")]
    [Trait("TestId", "SEL-H009")]
    public void ModelingTabsShareTheSelectGroup()
    {
        var vm = new MainViewModel();
        var reference = vm.Tabs.Single(t => t.Title == "Solid").Groups.Single(g => g.Title == "Select");
        foreach (var title in new[] { "Home", "Direct Modeling", "Surface", "NURBS", "Sheet Metal", "Analyze", "Static", "Assemble", "Joints" })
        {
            var group = vm.Tabs.Single(t => t.Title == title).Groups.Single(g => g.Title == "Select");
            Assert.Equal(FlatLabels(reference), FlatLabels(group));
        }
        var labels = FlatLabels(reference);
        foreach (var expected in new[] { "Object", "Face", "Edge", "Vertex", "Rectangle", "Lasso", "Paint", "Invert", "Through" })
            Assert.Contains(expected, labels);
    }

    [AvaloniaFact(DisplayName = "SEL-H010 | The Sketch tab's Select group is the same as the modeling tabs'; Edit sits in Modify")]
    [Trait("TestId", "SEL-H010")]
    public void SketchSelectGroupContents()
    {
        var vm = new MainViewModel();
        var sketch = vm.Tabs.Single(t => t.Title == "Sketch");
        var reference = vm.Tabs.Single(t => t.Title == "Solid").Groups.Single(g => g.Title == "Select");
        Assert.Equal(FlatLabels(reference), FlatLabels(sketch.Groups.Single(g => g.Title == "Select")));
        Assert.DoesNotContain(sketch.Groups.Single(g => g.Title == "Modify").Items.OfType<RibbonButton>(), b => b.Label == "Edit");   // Select edits geometry directly
    }
}
