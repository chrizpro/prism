using Avalonia.Headless.XUnit;
using Prism.Core;
using Prism.Kernel;
using Prism.ViewModels;
using Xunit;

namespace Prism.HeadlessTests.DocumentTabs;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/interface/document-tabs/README.md">Document tabs guide.</seealso>
[Trait("Feature", "DocumentTabs")]
[Trait("Layer", "Headless")]
public class DocumentTabTests
{
    [AvaloniaFact(DisplayName = "TAB-H003 | Close All But This preserves the target and reviews other pinned documents")]
    [Trait("TestId", "TAB-H003")]
    public async Task CloseAllButThisKeepsTargetAndReviewsPinned()
    {
        var vm = new MainViewModel(); var target = vm.ActiveDocument!;
        target.Session.AddObject(Occ.Box(1, 1, 1), "Target", ObjKind.Solid);
        var pinned = vm.NewDocumentTab(); pinned.IsPinned = true;
        pinned.Session.AddObject(Occ.Box(1, 1, 1), "Other", ObjKind.Solid);
        var asked = new List<DocumentTab>();
        Assert.False(await vm.CloseAllTabsAsync(true, tab => { asked.Add(tab); return Task.FromResult(DocumentCloseDecision.Cancel); }, target));
        Assert.Equal(new[] { pinned }, asked); Assert.Contains(target, vm.Documents); Assert.Contains(pinned, vm.Documents);
        Assert.True(await vm.CloseAllTabsAsync(true, _ => Task.FromResult(DocumentCloseDecision.Discard), target));
        Assert.Single(vm.Documents); Assert.Same(target, vm.Documents[0]); Assert.True(target.Session.Dirty);
    }
    [AvaloniaFact(DisplayName = "TAB-H020 | The tab's × asks Save / Don't save / Cancel once for a changed tab, and closes a clean tab without asking")]
    [Trait("TestId", "TAB-H020")]
    public async Task CloseButtonAsksOnceForChangedTabs()
    {
        var vm = new MainViewModel(); var first = vm.ActiveDocument!;
        var changed = vm.NewDocumentTab();
        changed.Session.AddObject(Occ.Box(1, 1, 1), "Part", ObjKind.Solid);
        int asked = 0;
        Assert.False(await vm.CloseDocumentWithPromptAsync(changed, _ => { asked++; return Task.FromResult(DocumentCloseDecision.Cancel); }));
        Assert.Equal(1, asked); Assert.Contains(changed, vm.Documents);          // Cancel keeps the tab, and one click asked once (no "press × again")
        Assert.True(await vm.CloseDocumentWithPromptAsync(changed, _ => { asked++; return Task.FromResult(DocumentCloseDecision.Discard); }));
        Assert.Equal(2, asked); Assert.DoesNotContain(changed, vm.Documents);      // Don't save closes it in that one answer

        Assert.False(vm.Documents.Any(MainViewModel.NeedsCloseDecision));
        var clean = vm.NewDocumentTab();
        Assert.True(await vm.CloseDocumentWithPromptAsync(clean, _ => { asked++; return Task.FromResult(DocumentCloseDecision.Cancel); }));
        Assert.Equal(2, asked); Assert.DoesNotContain(clean, vm.Documents);        // nothing to lose: no question
        Assert.Contains(first, vm.Documents);
    }

    [AvaloniaFact(DisplayName = "TAB-H002 | Close All saves dirty tabs and retains them when Save As is canceled")]
    [Trait("TestId", "TAB-H002")]
    public async Task CloseAllSavesOrStopsWhenSaveAsCanceled()
    {
        var vm = new MainViewModel(); var tab = vm.ActiveDocument!;
        tab.Session.AddObject(Occ.Box(1, 1, 1), "Dirty", ObjKind.Solid);
        vm.PickSaveFile = (_, _, _) => Task.FromResult<string?>(null);
        Assert.False(await vm.CloseAllTabsAsync(true, _ => Task.FromResult(DocumentCloseDecision.Save)));
        Assert.Contains(tab, vm.Documents); Assert.True(tab.Session.Dirty);
        string path = Path.Combine(Path.GetTempPath(), $"prism-tab-{Guid.NewGuid():N}.prism");
        try
        {
            vm.PickSaveFile = (_, _, _) => Task.FromResult<string?>(path);
            Assert.True(await vm.CloseAllTabsAsync(true, _ => Task.FromResult(DocumentCloseDecision.Save)));
            Assert.True(File.Exists(path)); Assert.DoesNotContain(tab, vm.Documents);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
    [AvaloniaFact(DisplayName = "TAB-H001 | Close All skips pinned tabs and stops at Cancel")]
    [Trait("TestId", "TAB-H001")]
    public async Task CloseAllSkipsPinnedAndHonorsCancel()
    {
        var vm = new MainViewModel();
        var pinned = vm.ActiveDocument!; pinned.IsPinned = true;
        pinned.Session.AddObject(Occ.Box(1, 1, 1), "Pinned", ObjKind.Solid);
        var dirty = vm.NewDocumentTab(); dirty.Session.AddObject(Occ.Box(1, 1, 1), "Dirty", ObjKind.Solid);
        var asked = new List<DocumentTab>();
        Assert.False(await vm.CloseAllTabsAsync(false, t => { asked.Add(t); return Task.FromResult(DocumentCloseDecision.Cancel); }));
        Assert.Equal(new[] { dirty }, asked); Assert.Contains(pinned, vm.Documents); Assert.Contains(dirty, vm.Documents);
        Assert.True(await vm.CloseAllTabsAsync(false, _ => Task.FromResult(DocumentCloseDecision.Discard)));
        Assert.Single(vm.Documents); Assert.Same(pinned, vm.Documents[0]);
        asked.Clear();
        Assert.False(await vm.CloseAllTabsAsync(true, t => { asked.Add(t); return Task.FromResult(DocumentCloseDecision.Cancel); }));
        Assert.Equal(new[] { pinned }, asked); Assert.Same(pinned, vm.Documents[0]);
        Assert.True(await vm.CloseAllTabsAsync(true, _ => Task.FromResult(DocumentCloseDecision.Discard)));
        Assert.DoesNotContain(pinned, vm.Documents); Assert.Single(vm.Documents);
    }
}
