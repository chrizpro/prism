using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Ribbon;
using Prism.ViewModels;
using Xunit;

namespace Prism.HeadlessTests.Curves;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/curves/README.md">Curve workflow and compatibility.</seealso>
[Trait("Feature", "Curves")]
[Trait("Layer", "Headless")]
public class CurveRibbonTests
{
    static MainViewModel DefaultRibbon()
    {
        AppSettings.Current.Ribbon = new RibbonLayout();
        AppSettings.Current.LastPicks.Clear();
        return new MainViewModel();
    }

    [AvaloniaTheory(DisplayName = "CRV-H034 | Curve menus keep editing, extraction and secondary tools reachable without nesting")]
    [InlineData("Home")]
    [InlineData("Surface")]
    [InlineData("NURBS")]
    [Trait("TestId", "CRV-H034")]
    public void CurveMenusKeepToolsReachable(string title)
    {
        var vm = DefaultRibbon();
        var tab = vm.Tabs.Single(t => t.Title == title);
        var edit = tab.Groups.Single(g => g.Title == "Edit curves").Items.OfType<RibbonButton>().ToList();
        var join = Assert.Single(edit, b => b.Id == "split:curve-join");
        Assert.Equal(new[] { "Join", "Explode", "Extract Edges" }, join.Menu!.Select(b => b.Label));
        Assert.DoesNotContain(edit, b => b.Label is "Explode" or "Rebuild\nCurve" or "Divide");
        var more = Assert.Single(edit, b => b.Label == "More Curve Tools");
        Assert.False(more.IsSplit);
        Assert.Equal(new[] { "Rebuild\nCurve", "Points Along\nCurve" }, more.Menu!.Select(b => b.Label));
        var curves = tab.Groups.Single(g => g.Title == "Curves").Items.OfType<RibbonButton>().ToList();
        var extract = Assert.Single(curves, b => b.Label == "Extract Curves");
        Assert.Equal(new[] { "Isocurves", "Contour" }, extract.Menu!.Select(b => b.Label));
        Assert.All(tab.Buttons.Where(b => b.HasMenu), b => Assert.All(b.Menu!, child => Assert.False(child.HasMenu)));
        var hits = vm.AllCommandHits().Where(h => h.Tab == title).ToList();
        Assert.Contains(hits, h => h.Label == "Points Along Curve");
        Assert.Contains(hits, h => h.Label == "Rebuild Curve");
        Assert.Contains(hits, h => h.Label == "Explode");
        Assert.DoesNotContain(hits, h => h.Label is "Split Curve" or "Divide" or "Duplicate Edge");
        join.Invoke();
        Assert.IsType<JoinCurvesCommand>(vm.Session.ActiveCommand);
    }

    [AvaloniaFact(DisplayName = "CRV-H035 | Renamed curve commands restore old saved identities, button IDs and shortcuts")]
    [Trait("TestId", "CRV-H035")]
    public void OldCurveIdentitiesStillResolve()
    {
        var vm = DefaultRibbon();
        Assert.IsType<DivideCurveCommand>(CommandCatalog.Find("Prism.Commands.DivideCurveCommand|Divide Curve")!());
        Assert.IsType<DuplicateEdgeCommand>(CommandCatalog.Find("Prism.Commands.DuplicateEdgeCommand|Duplicate Edge")!());
        var split = Assert.IsType<SplitCurveCommand>(CommandCatalog.Find("Prism.Commands.SplitCurveCommand|Split Curve")!());
        Assert.Equal(new[] { "Split at (% , separated)", "Keep the original" }, split.Params.Select(p => p.Label));
        var hits = vm.AllCommandHits().Where(h => h.Tab == "Surface").ToList();
        Assert.Equal("origin-point|Divide", hits.Single(h => h.Label == "Points Along Curve").Button.Id);
        Assert.Equal("curve-through|Duplicate Edge", hits.Single(h => h.Label == "Extract Edges").Button.Id);
        Assert.True(vm.RunCommandShortcut("Surface|Divide"));
        Assert.Equal("Points Along Curve", Assert.IsType<DivideCurveCommand>(vm.Session.ActiveCommand).Title);
        vm.Session.CancelCommand();
        Assert.True(vm.RunCommandShortcut("Surface|Duplicate Edge"));
        Assert.Equal("Extract Edges", Assert.IsType<DuplicateEdgeCommand>(vm.Session.ActiveCommand).Title);
    }
    [AvaloniaFact(DisplayName = "CRV-H036 | Customized layouts retain their former Curves and Edit curves menus")]
    [Trait("TestId", "CRV-H036")]
    public void OldCustomMenusRemainAvailable()
    {
        AppSettings.Current.Ribbon = new RibbonLayout
        {
            Edited = { ["Home"] = [new GroupCfg { Title = "My curves", Items = ["menu:Home:Curves"] }] },
        };
        try
        {
            var vm = new MainViewModel();
            var menu = Assert.Single(vm.Tabs.Single(t => t.Title == "Home").Groups.Single(g => g.Title == "My curves").Items.OfType<RibbonButton>());
            Assert.Contains(menu.Menu!, b => b.Label == "Curve");
            Assert.Contains(menu.Menu!, b => b.Label == "Explode");
            Assert.Contains(menu.Menu!, b => b.Label == "Points Along\nCurve");
            Assert.All(menu.Menu!, b => Assert.False(b.HasMenu));
        }
        finally { AppSettings.Current.Ribbon = new RibbonLayout(); }
    }

}
