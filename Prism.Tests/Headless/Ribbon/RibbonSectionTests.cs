using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Prism.Ribbon;
using Prism.ViewModels;
using Xunit;

namespace Prism.HeadlessTests.Ribbon;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/tabs/README.md#ribbon-section-names">Ribbon section names.</seealso>
[Trait("Feature", "Ribbon")]
[Trait("Layer", "Headless")]
public sealed class RibbonSectionTests
{
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
            AssertCaptions("3D FDM", "Prepare", "Troubleshoot", "Export");
            AssertCaptions("3D Resin", "Prepare", "Troubleshoot", "Export");
            AssertCaptions("Mold", "Part", "Mold");
        }
        finally
        {
            styles.Remove(prism);
            styles.Remove(fluent);
        }

        void AssertCaptions(string tabTitle, params string[] expected)
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
                var labels = ribbon.GetVisualDescendants().OfType<TextBlock>().Where(label => label.Classes.Contains("glabel")).ToArray();
                Assert.Equal(expected, labels.Select(label => label.Text));
                Assert.All(labels, label =>
                {
                    var color = Assert.IsAssignableFrom<ISolidColorBrush>(label.Foreground).Color;
                    Assert.True(label.IsVisible && label.Bounds.Height > 0);
                    Assert.True(dark ? color.R > 120 : color.R < 120, $"Unexpected {tabTitle} caption color {color}");
                });
            }
            finally { window.Close(); }
        }
    }
}
