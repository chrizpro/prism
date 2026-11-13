using Avalonia.Headless.XUnit;
using Prism.Core;
using Prism.Kernel;
using Prism.ViewModels;
using Xunit;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Selection;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/interface/selection/README.md#groups">Groups.</seealso>
[Trait("Feature", "Selection")]
[Trait("Layer", "Headless")]
public class GroupTests
{
    static (CadSession session, SceneObject[] parts) FourBoxes()
    {
        var session = new CadSession();
        var parts = Enumerable.Range(0, 4).Select(i => CreateBoxBody(session, new Vec3(i * 20, 0, 0), 10, 10, 10)).ToArray();
        return (session, parts);
    }

    [AvaloniaFact(DisplayName = "SEL-H025 | Group puts the selected objects under one new name, Ungroup takes them out, and one object cannot be grouped")]
    [Trait("TestId", "SEL-H025")]
    public void GroupAndUngroup()
    {
        var (session, parts) = FourBoxes();
        session.SelectObjects([parts[0].Id]);
        Assert.False(session.CanGroup);
        Assert.Null(session.GroupSelection());
        Assert.True(session.StatusIsError);

        session.SelectObjects(parts.Select(p => p.Id));
        Assert.True(session.CanGroup);
        Assert.Equal("Group 1", session.GroupSelection());
        Assert.All(parts, p => Assert.Equal("Group 1", session.Find(p.Id)!.Group));
        Assert.Equal(["Group 1"], session.GroupNames);

        session.Undo();                                           // grouping is one undo step
        Assert.All(parts, p => Assert.Null(session.Find(p.Id)!.Group));
        session.Redo();
        Assert.All(parts, p => Assert.Equal("Group 1", session.Find(p.Id)!.Group));

        session.SelectObjects([parts[1].Id]);
        Assert.True(session.CanUngroup);
        session.Ungroup();                                        // only the selected object leaves
        Assert.Null(session.Find(parts[1].Id)!.Group);
        Assert.Equal("Group 1", session.Find(parts[0].Id)!.Group);

        session.Ungroup(["Group 1"]);                             // the rest of the group dissolves
        Assert.Empty(session.GroupNames);
        Assert.Equal(4, session.Objects.Count);
    }

    [AvaloniaFact(DisplayName = "SEL-H026 | A group survives editing its members and saving the file, and can be renamed")]
    [Trait("TestId", "SEL-H026")]
    public void GroupSurvivesEditsAndSave()
    {
        var (session, parts) = FourBoxes();
        session.SelectObjects(parts.Take(2).Select(p => p.Id));
        session.GroupSelection("Legs");
        session.Update(parts[0].Id, o => o.WithShape(Occ.Translate(o.Shape, new Vec3(0, 5, 0))), checkpoint: false);
        session.Update(parts[0].Id, o => o.WithColor(0xFF00FF00), checkpoint: false);
        Assert.Equal("Legs", session.Find(parts[0].Id)!.Group);

        var path = Path.Combine(Path.GetTempPath(), $"group-{Guid.NewGuid():N}.prism");
        try
        {
            session.Save(path);
            var reopened = new CadSession();
            reopened.Open(path);
            Assert.Equal(["Legs", "Legs", null, null], reopened.Objects.Select(o => o.Group));
        }
        finally { File.Delete(path); }

        session.RenameGroup("Legs", "Feet");
        Assert.Equal(["Feet"], session.GroupNames);
        Assert.Equal(2, session.GroupMembers("Feet").Count());
        session.SelectGroup("Feet");
        Assert.Equal(2, session.Selection.Count);
    }

    [AvaloniaFact(DisplayName = "SEL-H027 | The browser nests a group's members under one parent row that hides and shows them together")]
    [Trait("TestId", "SEL-H027")]
    public void BrowserNestsGroup()
    {
        var vm = new MainViewModel();
        var session = vm.Session;
        var parts = Enumerable.Range(0, 3).Select(i => CreateBoxBody(session, new Vec3(i * 20, 0, 0), 10, 10, 10)).ToArray();
        session.SelectObjects(parts.Take(2).Select(p => p.Id));
        session.GroupSelection();
        vm.RebuildBrowser();

        var folder = Assert.Single(vm.AllBrowserNodes(), n => n.IsGroup);
        Assert.Equal("Group 1", folder.Title);
        Assert.Equal(2, folder.Children.Count);
        Assert.All(folder.Children, c => Assert.True(c.IsObject && parts.Take(2).Any(p => p.Id == c.ObjectId)));
        Assert.DoesNotContain(vm.AllBrowserNodes().Where(n => !n.IsGroup && n.IsFolder), f => f.Children.Any(c => c.ObjectId == parts[0].Id));
        Assert.True(folder.CanRename);

        folder.IsVisible = false;                                 // the eye on the parent row hides every member
        Assert.All(parts.Take(2), p => Assert.False(session.Find(p.Id)!.Visible));
        Assert.True(session.Find(parts[2].Id)!.Visible);
        vm.RebuildBrowser();
        Assert.False(Assert.Single(vm.AllBrowserNodes(), n => n.IsGroup).IsVisible);
        Assert.Contains(Prism.Core.Hotkeys.Actions, h => h.Id == "edit.group" && h.Default == "Ctrl+G");
        Assert.Contains(Prism.Core.Hotkeys.Actions, h => h.Id == "edit.ungroup" && h.Default == "Ctrl+Shift+G");
    }
}
