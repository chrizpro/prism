using System.Net;
using System.Text.Json;
using Prism.Core;
using Xunit;

namespace Prism.UnitTests.Updates;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/packaging/README.md#checking-for-updates">Desktop update behavior.</seealso>
[Trait("Feature", "Updates")]
[Trait("Layer", "Unit")]
public sealed class ReleaseUpdatesTests
{
    const string Windows = "Prism-windows-x64-Setup.exe";
    const string Mac = "Prism-macos-arm64.dmg";

    [Theory(DisplayName = "UPD-U001 | The update check chooses the newest published installer for the current platform")]
    [InlineData("0.2.0-alpha.2", Windows, "v0.2.0-alpha.10")]
    [InlineData("0.2.0-alpha.1", Mac, "v0.2.0-alpha.2")]
    [InlineData("0.2.0-alpha.10+abcdef", Windows, null)]
    [InlineData("0.2.0", Windows, null)]
    [InlineData("0.1.0", "missing", null)]
    [Trait("TestId", "UPD-U001")]
    public async Task FiltersAndComparesReleases(string installed, string platform, string? expected)
    {
        string json = JsonSerializer.Serialize(new object[]
        {
            Release("v0.2.0-alpha.2", Windows, Mac),
            Release("v0.2.0-alpha.10", Windows),
            Release("v0.2.1", Mac + ".sha256"),
            new { tag_name = "v0.3.0", draft = true, assets = new[] { Asset(Windows) } },
            new { tag_name = "v0.4.0", draft = false, assets = new[] { Asset(Windows, "new") } },
        });
        using var client = new HttpClient(new Feed(json));
        var update = await new ReleaseUpdates(client).FindAsync(installed, platform);
        Assert.Equal(expected, update?.Tag);
        if (expected != null)
            Assert.Equal($"{AppInfo.RepositoryUrl}/releases/download/{expected}/{platform}", update!.DownloadUrl);
    }

    [Fact(DisplayName = "UPD-U002 | A published final version outranks its alpha and server errors are surfaced")]
    [Trait("TestId", "UPD-U002")]
    public async Task FinalVersionAndRequestFailure()
    {
        using var client = new HttpClient(new Feed(JsonSerializer.Serialize(new[] { Release("v0.2.0", Windows) })));
        Assert.Equal("v0.2.0", (await new ReleaseUpdates(client).FindAsync("0.2.0-alpha.10", Windows))?.Tag);
        using var failed = new HttpClient(new Feed("{}", HttpStatusCode.Forbidden));
        await Assert.ThrowsAsync<HttpRequestException>(() => new ReleaseUpdates(failed).FindAsync("0.1.0", Windows));
    }

    static object Release(string tag, params string[] names) =>
        new { tag_name = tag, draft = false, assets = names.Select(name => Asset(name)).ToArray() };

    static object Asset(string asset, string status = "uploaded") => new { name = asset, state = status };

    sealed class Feed(string json, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("api.github.com", request.RequestUri?.Host);
            Assert.Equal("/repos/chrizpro/prism/releases", request.RequestUri?.AbsolutePath);
            Assert.NotEmpty(request.Headers.UserAgent);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json) });
        }
    }
}
