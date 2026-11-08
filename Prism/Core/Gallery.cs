using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Prism.Core;

/// <summary>An optional extra file offered beside a model, for example its animation video or exploded view image.</summary>
public sealed record GalleryExtra(string Label, string FileName, string Url, long Bytes, string Sha256);

public sealed record GalleryEntry(string Id, string Name, string Description, string Workflow,
    string Author, string License, string MinimumVersion, string ModelUrl, string PreviewUrl, long Bytes, string Sha256, List<GalleryExtra>? Extras = null)
{
    public bool Equals(GalleryEntry? other) => other != null && Id == other.Id && Name == other.Name && Description == other.Description && Workflow == other.Workflow &&
        Author == other.Author && License == other.License && MinimumVersion == other.MinimumVersion && ModelUrl == other.ModelUrl && PreviewUrl == other.PreviewUrl &&
        Bytes == other.Bytes && Sha256 == other.Sha256 && (Extras ?? []).SequenceEqual(other.Extras ?? []);
    public override int GetHashCode() => HashCode.Combine(Id, Sha256, Bytes, Extras?.Count ?? 0);
    public bool IsCompatible(string version) => Version.TryParse(MinimumVersion, out var required) &&
        Version.TryParse(version.Split('-')[0].Split('+')[0], out var current) && current >= required;
}

/// <summary>How the startup gallery groups models: by the first workflow named in a model's Workflow field.</summary>
public static class GallerySections
{
    /// <summary>Workspace tabs in the order the gallery lists them. Models with another first workflow follow, in catalog order.</summary>
    public static readonly string[] Order = ["Solid", "Surface", "Freeform", "NURBS", "Sheet Metal", "CAM", "CAE", "ECAD"];
    /// <summary>Models shown per section until the section is expanded.</summary>
    public const int Preview = 3;

    public static string Of(string workflow)
    {
        var first = workflow.Split('/')[0].Trim();
        return Order.FirstOrDefault(o => o.Equals(first, StringComparison.OrdinalIgnoreCase)) ?? first;
    }

    public static IReadOnlyList<(string Section, List<GalleryEntry> Items)> Group(IEnumerable<GalleryEntry> entries)
    {
        var groups = new Dictionary<string, List<GalleryEntry>>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries) { var k = Of(e.Workflow); if (!groups.TryGetValue(k, out var l)) groups[k] = l = []; l.Add(e); }
        return groups.OrderBy(g => { int i = Array.FindIndex(Order, o => o.Equals(g.Key, StringComparison.OrdinalIgnoreCase)); return i < 0 ? Order.Length : i; })
            .Select(g => (g.Key, g.Value)).ToList();
    }
}

public sealed record GalleryCatalog(int SchemaVersion, List<GalleryEntry> Items)
{
    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    public static GalleryCatalog Parse(string json)
    {
        var catalog = JsonSerializer.Deserialize<GalleryCatalog>(json, Json) ?? throw new InvalidDataException("Empty gallery catalog.");
        if (catalog.SchemaVersion != 1 || catalog.Items == null || catalog.Items.Count > 200)
            throw new InvalidDataException("Unsupported gallery catalog.");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in catalog.Items)
            if (e == null || string.IsNullOrEmpty(e.Id) || !Regex.IsMatch(e.Id, @"\A[a-z0-9][a-z0-9-]{0,79}\z") || !ids.Add(e.Id) ||
                string.IsNullOrWhiteSpace(e.Name) || string.IsNullOrWhiteSpace(e.Description) || string.IsNullOrWhiteSpace(e.Workflow) ||
                string.IsNullOrWhiteSpace(e.Author) || string.IsNullOrWhiteSpace(e.License) || !Version.TryParse(e.MinimumVersion, out _) ||
                !AllowedUrl(e.ModelUrl) || !AllowedUrl(e.PreviewUrl) || e.Bytes <= 0 || e.Bytes > 256 * 1024 * 1024 ||
                !Regex.IsMatch(e.Sha256 ?? "", @"\A[0-9a-fA-F]{64}\z"))
                throw new InvalidDataException("Invalid gallery entry.");
        foreach (var x in catalog.Items.SelectMany(e => e.Extras ?? []))
            if (x == null || string.IsNullOrWhiteSpace(x.Label) || !Regex.IsMatch(x.FileName ?? "", @"\A[A-Za-z0-9][A-Za-z0-9._-]{0,79}\z") ||
                !AllowedUrl(x.Url) || x.Bytes <= 0 || x.Bytes > 128 * 1024 * 1024 || !Regex.IsMatch(x.Sha256 ?? "", @"\A[0-9a-fA-F]{64}\z"))
                throw new InvalidDataException("Invalid gallery extra file.");
        return catalog;
    }
    public string ToJson() => JsonSerializer.Serialize(this, Json);
    public static bool AllowedUrl(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        uri.Scheme == "https" && uri.UserInfo.Length == 0 && uri.IsDefaultPort &&
        (uri.Host == "raw.githubusercontent.com" || uri.Host == "github.com");
}

/// <summary>On-demand gallery transport. Models are immutable verified cache entries; editing uses separate copies.</summary>
public sealed class GalleryStore(HttpClient http, string root)
{
    public const string CatalogUrl = "https://raw.githubusercontent.com/chrizpro/prism/develop/gallery/catalog.json";
    public static GalleryStore Local { get; } = new(new HttpClient { Timeout = TimeSpan.FromSeconds(30) },
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Prism", "gallery-cache"));
    string CatalogPath => Path.Combine(root, "catalog.json");
    public GalleryCatalog ReadCatalog(string fallback)
    {
        try { return GalleryCatalog.Parse(File.ReadAllText(CatalogPath)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { return GalleryCatalog.Parse(fallback); }
    }
    public async Task<GalleryCatalog> Refresh(CancellationToken token = default)
    {
        var data = await Download(CatalogUrl, 1024 * 1024, token);
        var catalog = GalleryCatalog.Parse(System.Text.Encoding.UTF8.GetString(data));
        await AtomicWrite(CatalogPath, data, token);
        return catalog;
    }
    public string ModelPath(GalleryEntry entry) => Path.Combine(root, entry.Id + "-" + entry.Sha256.ToLowerInvariant() + ".prism");
    public bool IsCached(GalleryEntry entry)
    {
        try
        {
            string path = ModelPath(entry);
            if (new FileInfo(path).Length != entry.Bytes) return false;
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream)).Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
    public async Task<string> GetModel(GalleryEntry entry, CancellationToken token = default)
    {
        if (await Task.Run(() => IsCached(entry), token)) return ModelPath(entry);
        var data = await Download(entry.ModelUrl, entry.Bytes, token);
        if (data.LongLength != entry.Bytes || !Convert.ToHexString(SHA256.HashData(data)).Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The model download is incomplete or has changed. Refresh the gallery and try again.");
        await AtomicWrite(ModelPath(entry), data, token);
        return ModelPath(entry);
    }
    public async Task<byte[]> GetPreview(GalleryEntry entry, CancellationToken token = default)
    {
        string path = PreviewPath(entry);
        if (File.Exists(path)) return await File.ReadAllBytesAsync(path, token);
        var data = await Download(entry.PreviewUrl, 5 * 1024 * 1024, token);
        await AtomicWrite(path, data, token);
        return data;
    }
    string PreviewPath(GalleryEntry entry) => Path.Combine(root, "previews",
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(entry.PreviewUrl))) + ".png");
    public byte[]? ReadPreview(GalleryEntry entry)
    {
        try { return File.ReadAllBytes(PreviewPath(entry)); }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
    public async Task<string> CreateWorkingCopy(GalleryEntry entry, string folder, CancellationToken token = default)
    {
        string source = await GetModel(entry, token);
        Directory.CreateDirectory(folder);
        for (int i = 1; ; i++)
        {
            string path = Path.Combine(folder, entry.Id + (i == 1 ? "" : "-" + i) + ".prism");
            FileStream target;
            try { target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); }
            catch (IOException) when (File.Exists(path)) { continue; }
            try
            {
                await using (target)
                await using (var input = File.OpenRead(source)) await input.CopyToAsync(target, token);
                return path;
            }
            catch { File.Delete(path); throw; }
        }
    }
    /// <summary>Downloads an extra file (video, exploded view image), verifies its size and hash, and saves it under a new name in <paramref name="folder"/>.</summary>
    public async Task<string> SaveExtra(GalleryExtra extra, string folder, CancellationToken token = default)
    {
        var data = await Download(extra.Url, extra.Bytes, token);
        if (data.LongLength != extra.Bytes || !Convert.ToHexString(SHA256.HashData(data)).Equals(extra.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The download is incomplete or has changed. Refresh the gallery and try again.");
        Directory.CreateDirectory(folder);
        string stem = Path.GetFileNameWithoutExtension(extra.FileName), ext = Path.GetExtension(extra.FileName);
        for (int i = 1; ; i++)
        {
            string path = Path.Combine(folder, stem + (i == 1 ? "" : "-" + i) + ext);
            try { await using var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); await target.WriteAsync(data, token); return path; }
            catch (IOException) when (File.Exists(path)) { }
        }
    }
    async Task<byte[]> Download(string url, long limit, CancellationToken token)
    {
        if (!GalleryCatalog.AllowedUrl(url)) throw new InvalidDataException("Unsupported gallery download URL.");
        // HttpClient's timeout ends at headers with ResponseHeadersRead; bound the body transfer as well.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("Gallery download exceeds its size limit.");
        await using var input = await response.Content.ReadAsStreamAsync(deadline.Token);
        using var output = new MemoryStream();
        byte[] buffer = new byte[65536]; int count;
        while ((count = await input.ReadAsync(buffer, deadline.Token)) > 0)
        {
            if (output.Length + count > limit) throw new InvalidDataException("Gallery download exceeds its size limit.");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }
    static async Task AtomicWrite(string path, byte[] data, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllBytesAsync(temp, data, token); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
