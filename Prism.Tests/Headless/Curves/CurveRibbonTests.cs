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

    [AvaloniaTheory(DisplayName = "CRV-H034 | One Curves dropdown keeps every curve tool reachable without nesting")]
    [InlineData("Home")]
    [InlineData("Surface")]
    [InlineData("NURBS")]
    [Trait("TestId", "CRV-H034")]
    public void CurveMenusKeepToolsReachable(string title)
    {
        var vm = DefaultRibbon();
        var tab = vm.Tabs.Single(t => t.Title == title);
        // owner direction 2026-11-13: the Curves and Edit curves groups became one dropdown to shorten the ribbons
        Assert.DoesNotContain(tab.Groups, g => g.Title == "Edit curves");
        var curves = Assert.Single(tab.Groups.Single(g => g.Title == "Curves").Items.OfType<RibbonButton>());
        Assert.Equal("menu:curves", curves.Id);
        Assert.False(curves.IsSplit);
        var labels = curves.Menu!.Select(b => b.Label).ToList();
        foreach (var tool in new[] { "Isocurves", "Contour", "Extract Edges", "Rebuild\nCurve", "Points Along\nCurve" })
            Assert.Contains(tool, labels);
        // owner direction 2026-11-14: these are [Legacy], hidden from the GUI but still reachable by saved files and scripts
        foreach (var tool in new[] { "Curve", "Blend\nCurve", "Join", "Explode", "Trim\nCurve", "Extend\nCurve", "Offset\nCurve" })
            Assert.DoesNotContain(tool, labels);
        Assert.All(tab.Buttons.Where(b => b.HasMenu), b => Assert.All(b.Menu!, child => Assert.False(child.HasMenu)));
        var hits = vm.AllCommandHits().Where(h => h.Tab == title).ToList();
        Assert.Contains(hits, h => h.Label == "Points Along Curve");
        Assert.Contains(hits, h => h.Label == "Rebuild Curve");
        Assert.DoesNotContain(hits, h => h.Label is "Explode" or "Join" or "Blend Curve" or "Trim Curve" or "Extend Curve" or "Offset Curve");
        Assert.DoesNotContain(hits, h => h.Label is "Split Curve" or "Divide" or "Duplicate Edge");
    }

    [AvaloniaFact(DisplayName = "CRV-H037 | Legacy curve commands are hidden from the ribbon but still resolve for saved files and scripts")]
    [Trait("TestId", "CRV-H037")]
    public void LegacyCurveCommandsStayReachableOutsideTheGui()
    {
        DefaultRibbon();
        foreach (var type in new[] { typeof(CurveByPointsCommand), typeof(BlendCurveCommand), typeof(JoinCurvesCommand), typeof(ExplodeCurveCommand), typeof(TrimCurveCommand), typeof(ExtendCurveCommand), typeof(OffsetCurveCommand) })
            Assert.True(LegacyAttribute.IsLegacy(type), type.Name);
        Assert.False(LegacyAttribute.IsLegacy(typeof(RebuildCurveCommand)));
        Assert.IsType<JoinCurvesCommand>(CommandCatalog.Find("Prism.Commands.JoinCurvesCommand|Join Curves")!());
        Assert.IsType<TrimCurveCommand>(CommandCatalog.Find("Prism.Commands.TrimCurveCommand|Trim Curve")!());
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
            Assert.Contains(menu.Menu!, b => b.Label == "Isocurves");
            Assert.DoesNotContain(menu.Menu!, b => b.Label == "Explode");
            Assert.Contains(menu.Menu!, b => b.Label == "Points Along\nCurve");
            Assert.All(menu.Menu!, b => Assert.False(b.HasMenu));
        }
        finally { AppSettings.Current.Ribbon = new RibbonLayout(); }
    }

}
