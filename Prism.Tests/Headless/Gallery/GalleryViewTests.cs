using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Prism.Core;
using Prism.ViewModels;
using Prism.Views;
using Xunit;
using System.Net;
using System.Text;
using Prism.Kernel;

namespace Prism.HeadlessTests.Gallery;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/interface/gallery/README.md">Startup gallery specification.</seealso>
[Trait("Feature", "Gallery")]
[Trait("Layer", "Headless")]
public class GalleryViewTests
{
    [AvaloniaFact(DisplayName = "GAL-H001 | Startup view loads gallery bindings alongside workspace and file actions")]
    [Trait("TestId", "GAL-H001")]
    public async Task HomeViewLoadsAndWorkflowSearchFiltersCards()
    {
        var vm = new MainViewModel();
        var view = new HomeView { DataContext = vm };
        Assert.NotNull(view.Content);
        var entry = new GalleryEntry("pendant", "Pendant shade", "Revolved shade", "Surface", "Prism", "MIT", "0.1.0",
            "https://github.com/a/model", "https://github.com/a/image", 1, new string('a', 64));
        string root = Path.Combine(Path.GetTempPath(), "prism-gallery-view-" + Guid.NewGuid());
        using var http = new HttpClient(new CatalogTransport(new GalleryCatalog(1, [entry, entry with { Id = "chair", Name = "Lounge chair", Workflow = "Freeform" }]).ToJson()));
        vm.GallerySource = new GalleryStore(http, root);
        try
        {
            await vm.LoadGallery();
            Assert.Equal(2, vm.GalleryCards.Count);
            vm.GallerySearch = "surface";
            var card = Assert.Single(vm.GalleryCards);
            Assert.Equal("Pendant shade", card.Name);
            Assert.Equal("Download & open", card.ActionLabel);
            Assert.True(card.CanOpen);
            vm.GallerySearch = "no match";
            Assert.Empty(vm.GalleryCards);
            Assert.True(vm.GalleryEmpty);
            vm.GallerySearch = "";
            Assert.Equal(2, vm.GalleryCards.Count);
            Assert.True(vm.GalleryAvailable);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    sealed class CatalogTransport(string catalog) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("catalog.json")
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(catalog, Encoding.UTF8) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    [AvaloniaFact(DisplayName = "GAL-H002 | Failed model download leaves the current work and startup screen intact")]
    [Trait("TestId", "GAL-H002")]
    public async Task FailedDownloadDoesNotReplaceUserDocument()
    {
        var vm = new MainViewModel();
        var original = vm.Session;
        var userPart = original.AddObject(Occ.Box(10, 20, 30), "Unsaved user part", ObjKind.Solid);
        vm.HomeOpen = true;
        string root = Path.Combine(Path.GetTempPath(), "prism-gallery-failure-" + Guid.NewGuid());
        using var http = new HttpClient(new CatalogTransport("{}"));
        vm.GallerySource = new GalleryStore(http, root);
        var card = new GalleryCard(new GalleryEntry("missing", "Missing example", "Unavailable", "Solid", "Prism", "MIT", "0.1.0",
            "https://github.com/a/model", "https://github.com/a/image", 1, new string('a', 64)));
        try
        {
            await vm.OpenGalleryCard(card);
            Assert.Same(original, vm.Session);
            Assert.Same(userPart, Assert.Single(original.Objects));
            Assert.True(vm.HomeOpen);
            Assert.False(vm.GalleryBusy);
            Assert.Contains("Could not open Missing example", vm.GalleryStatus);
            Assert.False(Directory.Exists(Path.Combine(root, "work")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
