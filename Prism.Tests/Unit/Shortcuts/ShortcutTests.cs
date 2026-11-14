using Prism.Core;
using Prism.Help;
using Xunit;

namespace Prism.UnitTests.Shortcuts;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/site/reference/hotkeys.md">Keyboard shortcuts.</seealso>
[Trait("Feature", "Shortcuts")]
[Trait("Layer", "Unit")]
public class ShortcutTests
{
    [Fact(DisplayName = "KEY-U001 | Fit (F) is used by Zoom Extents only: no other built-in shortcut in any context has it")]
    [Trait("TestId", "KEY-U001")]
    public void FitKeyIsUnique()
    {
        var fit = Hotkeys.Actions.Single(a => a.Id == "view.extents");
        Assert.Equal("F", fit.Default);
        Assert.Equal(Hotkeys.Global, fit.Scope);
        Assert.DoesNotContain(Hotkeys.Actions, a => a.Id != fit.Id && string.Equals(a.Default, "F", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(DisplayName = "KEY-U002 | The Home key still fits the view, outside the animation timeline where it goes to the start")]
    [Trait("TestId", "KEY-U002")]
    public void HomeStillFits()
    {
        var home = Hotkeys.Actions.Single(a => a.Id == "view.extents.home");
        Assert.Equal(("Home", Hotkeys.Global), (home.Default, home.Scope));
        var start = Hotkeys.Actions.Single(a => a.Id == "animation.start");
        Assert.Equal(("Home", Hotkeys.Command), (start.Default, start.Scope));   // the more specific scope wins while the timeline is open
    }

    [Fact(DisplayName = "KEY-U003 | Learn More links to a tool's row on its ribbon page of the website")]
    [Trait("TestId", "KEY-U003")]
    public void LearnMoreLinksToTheToolRow()
    {
        Assert.Equal("fillet-chamfer", ToolDocumentation.Slug("Fillet / Chamfer"));
        Assert.Equal("direct-modeling", ToolDocumentation.Slug("Direct Modeling"));
        var saved = AppSettings.Current.ToolDocumentationBaseUrl;
        AppSettings.Current.ToolDocumentationBaseUrl = null;
        try
        {
            Assert.Equal(AppInfo.DocsUrl + "tools/solid#tool-fillet", ToolDocumentation.LinkFor("x|Fillet", "Solid", "Fillet")!.AbsoluteUri);
            Assert.Null(ToolDocumentation.LinkFor("x|Fillet"));
        }
        finally { AppSettings.Current.ToolDocumentationBaseUrl = saved; }
    }

    [Fact(DisplayName = "KEY-U004 | Search shows a key only beside the ribbon button that key really runs")]
    [Trait("TestId", "KEY-U004")]
    public void SearchShowsTheRightKeys()
    {
        bool Default(string id) => !Hotkeys.IsChanged(Hotkeys.Find(id)!);
        if (Default("view.extents") && Default("view.extents.home")) Assert.Equal("F / Home", Hotkeys.ShortcutFor("View", "Zoom Extents"));
        if (Default("model.extrude"))
        {
            Assert.Equal("Alt+X", Hotkeys.ShortcutFor("Solid", "Extrude"));
            Assert.Equal("", Hotkeys.ShortcutFor("Surface", "Extrude"));        // the surface extrude: Alt+X starts the solid one
        }
        if (Default("sketch.start"))
        {
            Assert.Equal("K", Hotkeys.ShortcutFor("NURBS", "Create Sketch"));   // K starts the NURBS sketch
            Assert.Equal("", Hotkeys.ShortcutFor("Home", "Create Sketch"));
        }
        Assert.Equal("", Hotkeys.ShortcutFor("Solid", "Fillet"));
    }

    [Fact(DisplayName = "KEY-U005 | Space maximizes the hovered quad view globally while Animation Play/Pause keeps Space when the timeline is open")]
    [Trait("TestId", "KEY-U005")]
    public void SpaceYieldsToAnimationTimeline()
    {
        var max = Hotkeys.Actions.Single(a => a.Id == "view.quad.maximize");
        Assert.Equal(("Space", Hotkeys.Global), (max.Default, max.Scope));
        var play = Hotkeys.Actions.Single(a => a.Id == "animation.play");
        Assert.Equal(("Space", Hotkeys.Command), (play.Default, play.Scope));
        Assert.Equal(["view.quad.maximize"], Hotkeys.Resolve("Space", Hotkeys.ActiveScopes(false, false, "design")).Select(a => a.Id));
        Assert.Equal(["animation.play", "view.quad.maximize"], Hotkeys.Resolve("Space", Hotkeys.ActiveScopes(true, false, "design")).Select(a => a.Id));
    }
}
