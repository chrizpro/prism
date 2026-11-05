using System.Net;
using System.Security.Cryptography;
using Prism.Core;
using Xunit;

namespace Prism.UnitTests.Gallery;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/interface/gallery/README.md">Startup gallery specification.</seealso>
[Trait("Feature", "Gallery")]
[Trait("Layer", "Unit")]
public sealed class GalleryStoreTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "prism-gallery-test-" + Guid.NewGuid().ToString("N"));
    static readonly byte[] Model = "a native model fixture"u8.ToArray();
    static GalleryEntry Entry => new("table", "Side table", "Oak and steel", "Solid", "Prism", "MIT", "0.1.0",
        "https://raw.githubusercontent.com/chrizpro/prism/develop/gallery/models/table.prism",
        "https://raw.githubusercontent.com/chrizpro/prism/develop/gallery/previews/table.png", Model.Length, Convert.ToHexString(SHA256.HashData(Model)));
    static string Catalog => new GalleryCatalog(1, [Entry]).ToJson();
    sealed class Transport(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls++; return Task.FromResult(respond(request)); }
    }
    GalleryStore Store(Transport transport) => new(new HttpClient(transport), root);
    static HttpResponseMessage Reply(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    [Fact(DisplayName = "GAL-U001 | Cached models open offline as independent editable copies")]
    [Trait("TestId", "GAL-U001")]
    public async Task VerifiedCacheAndWorkingCopiesAreIndependent()
    {
        bool offline = false;
        using var transport = new Transport(_ => offline ? throw new HttpRequestException("offline") : Reply(Model));
        var store = Store(transport);
        var cache = await store.GetModel(Entry);
        Assert.True(store.IsCached(Entry)); offline = true;
        string first = await store.CreateWorkingCopy(Entry, Path.Combine(root, "work"));
        await File.WriteAllTextAsync(first, "user edits");
        string second = await store.CreateWorkingCopy(Entry, Path.Combine(root, "work"));
        Assert.NotEqual(first, second);
        Assert.Equal(Model, await File.ReadAllBytesAsync(second));
        Assert.Equal(Model, await File.ReadAllBytesAsync(cache));
        Assert.Equal("user edits", await File.ReadAllTextAsync(first));
        Assert.Equal(1, transport.Calls);
    }

    [Fact(DisplayName = "GAL-U002 | Truncated and changed downloads never become cached models")]
    [Trait("TestId", "GAL-U002")]
    public async Task CorruptDownloadCannotReplaceVerifiedCache()
    {
        using var transport = new Transport(_ => Reply(new byte[Model.Length]));
        var store = Store(transport);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.GetModel(Entry));
        Assert.False(store.IsCached(Entry));
        Assert.False(File.Exists(store.ModelPath(Entry)));
        using var truncated = new Transport(_ => Reply([1, 2]));
        await Assert.ThrowsAsync<InvalidDataException>(() => Store(truncated).GetModel(Entry));
        Assert.Empty(Directory.Exists(root) ? Directory.GetFiles(root, "*.tmp") : []);
    }

    [Fact(DisplayName = "GAL-U003 | Invalid catalog refresh preserves the last usable offline catalog")]
    [Trait("TestId", "GAL-U003")]
    public async Task FailedRefreshRetainsLastValidCatalog()
    {
        bool invalid = false;
        using var transport = new Transport(_ => Reply(System.Text.Encoding.UTF8.GetBytes(invalid ? "{\"schemaVersion\":99}" : Catalog)));
        var store = Store(transport);
        await store.Refresh(); invalid = true;
        await Assert.ThrowsAsync<InvalidDataException>(() => store.Refresh());
        Assert.Equal("table", Assert.Single(store.ReadCatalog("invalid fallback").Items).Id);
        await File.WriteAllTextAsync(Path.Combine(root, "catalog.json"), "not json");
        Assert.Equal("table", Assert.Single(store.ReadCatalog(Catalog).Items).Id);
    }

    [Fact(DisplayName = "GAL-U004 | Catalog rejects unsafe paths, unsupported hosts and duplicate identities")]
    [Trait("TestId", "GAL-U004")]
    public void CatalogValidationAndMinimumVersion()
    {
        foreach (var entry in new[] { Entry with { Id = "../table" }, Entry with { ModelUrl = "http://github.com/model" },
            Entry with { PreviewUrl = "https://github.com.attacker.test/image" }, Entry with { Sha256 = "bad" }, Entry with { Bytes = 0 } })
            Assert.Throws<InvalidDataException>(() => GalleryCatalog.Parse(new GalleryCatalog(1, [entry]).ToJson()));
        Assert.Throws<InvalidDataException>(() => GalleryCatalog.Parse(new GalleryCatalog(1, [Entry, Entry]).ToJson()));
        Assert.True(Entry.IsCompatible("0.1.0-alpha.1"));
        Assert.False((Entry with { MinimumVersion = "2.0.0" }).IsCompatible("1.0.0"));
    }

    [Fact(DisplayName = "GAL-U005 | Oversized downloads stop before writing a cache entry")]
    [Trait("TestId", "GAL-U005")]
    public async Task TransferSizeLimitIsEnforced()
    {
        using var transport = new Transport(_ => Reply(new byte[Model.Length + 1]));
        var store = Store(transport);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.GetModel(Entry));
        Assert.False(File.Exists(store.ModelPath(Entry)));
    }

    [Fact(DisplayName = "GAL-U006 | Offline preview cache remains readable; tampered model cache is rejected")]
    [Trait("TestId", "GAL-U006")]
    public async Task OfflinePreviewsAndModelCacheIntegrity()
    {
        bool offline = false;
        using var transport = new Transport(_ => offline ? throw new HttpRequestException("offline") : Reply(Model));
        var store = Store(transport);
        await store.GetModel(Entry);
        Assert.Null(store.ReadPreview(Entry));
        await store.GetPreview(Entry);
        offline = true;
        Assert.Equal(Model, store.ReadPreview(Entry));
        Assert.Equal(Model, await store.GetPreview(Entry));
        Assert.Equal(2, transport.Calls);
        await File.WriteAllBytesAsync(store.ModelPath(Entry), new byte[Model.Length]);
        Assert.False(store.IsCached(Entry));
        await Assert.ThrowsAsync<HttpRequestException>(() => store.CreateWorkingCopy(Entry, Path.Combine(root, "work")));
        Assert.False(Directory.Exists(Path.Combine(root, "work")));
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
