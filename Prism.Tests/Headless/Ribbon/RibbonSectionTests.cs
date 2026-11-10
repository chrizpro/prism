using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Prism.Controls;
using Prism.Core;
using Prism.Ribbon;
using Prism.ViewModels;
using Xunit;

namespace Prism.HeadlessTests.Ribbon;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/tabs/README.md#ribbon-section-names">Ribbon section names.</seealso>
[Trait("Feature", "Ribbon")]
[Trait("Layer", "Headless")]
public sealed class RibbonSectionTests
{
    [AvaloniaFact(DisplayName = "RIB-H002 | Gizmo has its own axis-colored glyph and retains its saved ribbon ID")]
    [Trait("TestId", "RIB-H002")]
    public void GizmoGlyphMatchesViewportAxes()
    {
        var vm = new MainViewModel();
        var solid = vm.Tabs.Single(tab => tab.Title == "Solid");
        var gizmo = Assert.Single(solid.Buttons, button => button.Label == "Gizmo" && button.Menu == null);
        Assert.Equal("move|Gizmo", gizmo.Id);
        Assert.Equal("gizmo", gizmo.Icon);
        Assert.Equal("move", Assert.Single(solid.Buttons, button => button.Label == "Move" && button.Menu == null).Icon);

        var app = Application.Current!;
        var original = app.RequestedThemeVariant;
        try
        {
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                app.RequestedThemeVariant = theme;
                var colors = Icons.Get(gizmo.Icon)!.SelectMany(shape => new[] { shape.Fill, shape.Stroke?.Brush })
                    .OfType<ISolidColorBrush>().Select(brush => brush.Color.ToUInt32()).ToArray();
                Assert.All(GizmoVisual.AxisColors, axis => Assert.Contains(axis, colors));
            }
        }
        finally { app.RequestedThemeVariant = original; }
    }

    [AvaloniaTheory(DisplayName = "RIB-H001 | Every ribbon group has a name and captions use a readable theme brush")]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("TestId", "RIB-H001")]
    public void CaptionsRenderInBothThemes(bool dark)
    {
        var vm = new MainViewModel();
        Assert.All(vm.Tabs, tab =>
        {
            Assert.NotEmpty(tab.Groups);
            Assert.All(tab.Groups, group => Assert.False(string.IsNullOrWhiteSpace(group.Title), tab.Title));
        });

        var styles = Application.Current!.Styles;
        var fluent = new Avalonia.Themes.Fluent.FluentTheme();
        var prism = new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://Prism/"))
        {
            Source = new Uri("avares://Prism/Styles/Theme.axaml")
        };
        styles.Add(fluent);
        styles.Add(prism);
        try
        {
            AssertCaptions("Subtractive", "Setup", "Toolpaths", "Review", "Export");
            AssertCaptions("3D FDM", "Prepare", "Troubleshoot", "Export");
            AssertCaptions("3D Resin", "Prepare", "Troubleshoot", "Export");
            AssertCaptions("Mold", "Part", "Mold");
            AssertCurvePalette("Solid", dark ? "#79A8F2" : "#2F66C9");
            AssertCurvePalette("Surface", dark ? "#3FCF8E" : "#16824A");
            AssertCurvePalette("NURBS", dark ? "#FF6B6F" : "#D13438");
            AssertCurvePalette("Freeform", dark ? "#B79CFF" : "#6846B4");
        }
        finally
        {
            styles.Remove(prism);
            styles.Remove(fluent);
        }

        void AssertCaptions(string tabTitle, params string[] expected) => WithRibbon(tabTitle, ribbon =>
        {
            var labels = ribbon.GetVisualDescendants().OfType<TextBlock>().Where(label => label.Classes.Contains("glabel")).ToArray();
            Assert.Equal(expected, labels.Select(label => label.Text));
            Assert.All(labels, label =>
            {
                var color = Assert.IsAssignableFrom<ISolidColorBrush>(label.Foreground).Color;
                Assert.True(label.IsVisible && label.Bounds.Height > 0);
                Assert.True(dark ? color.R > 120 : color.R < 120, $"Unexpected {tabTitle} caption color {color}");
            });
        });

        void AssertCurvePalette(string tabTitle, string expected) => WithRibbon(tabTitle, ribbon =>
        {
            var icon = Assert.Single(ribbon.GetVisualDescendants().OfType<IconView>(),
                view => view.IsEffectivelyVisible && view.DataContext is RibbonButton { Id: "split:curve-create" });
            Assert.Equal(tabTitle, icon.RibbonTab);
            var button = (RibbonButton)icon.DataContext!;
            Assert.Equal(new[] { "curve-through|Curve", "curve-equation|Curve from Data" }, button.Menu!.Select(item => item.Id));

            var app = Application.Current!;
            var previous = app.RequestedThemeVariant;
            try
            {
                app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                foreach (var item in button.Menu!)
                {
                    var shape = Icons.Get(item.Icon, icon.RibbonTab)![0];
                    Assert.Equal(Color.Parse(expected), Assert.IsAssignableFrom<ISolidColorBrush>(shape.Stroke!.Brush).Color);
                }
            }
            finally { app.RequestedThemeVariant = previous; }
        });

        void WithRibbon(string tabTitle, Action<RibbonView> verify)
        {
            var tab = vm.Tabs.Single(tab => tab.Title == tabTitle);
            vm.EnsureTabVisible(tab);
            vm.SelectTab(tab);
            var ribbon = new RibbonView { DataContext = vm };
            var window = new Window { Width = 1000, Height = 200, Content = ribbon,
                RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
            window.Show();
            try
            {
                Dispatcher.UIThread.RunJobs();
                verify(ribbon);
            }
            finally { window.Close(); }
        }
    }
}
