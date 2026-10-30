using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Prism.Core;

/// <summary>
/// What a part is, beyond its shape: the numbers and notes a shop, a drawing and a parts list need. It is saved inside the part's file,
/// so it travels with the part everywhere (email, cloud, another machine), and the parts library can search it.
/// </summary>
public sealed record PartInfo(string PartNumber = "", string Description = "", string Revision = "", string Material = "", string Supplier = "",
    double? Cost = null, List<string>? Tags = null, Dictionary<string, string>? Custom = null)
{
    [JsonIgnore] public bool IsEmpty => PartNumber.Length == 0 && Description.Length == 0 && Revision.Length == 0 && Material.Length == 0 && Supplier.Length == 0
        && Cost == null && (Tags == null || Tags.Count == 0) && (Custom == null || Custom.Count == 0);

    [JsonIgnore] public string TagText => string.Join(", ", Tags ?? []);
    [JsonIgnore] public string CustomText => string.Join("\n", (Custom ?? []).Select(kv => $"{kv.Key}: {kv.Value}"));

    public static List<string> ParseTags(string text) =>
        [.. text.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>"name: value" on each line.</summary>
    public static Dictionary<string, string> ParseCustom(string text)
    {
        var res = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int cut = line.IndexOf(':');
            if (cut > 0) res[line[..cut].Trim()] = line[(cut + 1)..].Trim();
        }
        return res;
    }

    /// <summary>True if every word of the filter appears somewhere in the part's information.</summary>
    public bool Matches(string filter)
    {
        string hay = string.Join(" ", PartNumber, Description, Revision, Material, Supplier, TagText, CustomText);
        return filter.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(w => hay.Contains(w, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A few readable lines for tooltips.</summary>
    public string Summary()
    {
        var lines = new List<string>();
        if (PartNumber.Length > 0) lines.Add("Part number: " + PartNumber + (Revision.Length > 0 ? "  rev " + Revision : ""));
        if (Description.Length > 0) lines.Add(Description);
        if (Material.Length > 0) lines.Add("Material: " + Material);
        if (Supplier.Length > 0) lines.Add("Supplier: " + Supplier);
        if (Cost != null) lines.Add($"Cost: {Cost:0.##}");
        if (Tags is { Count: > 0 }) lines.Add("Tags: " + TagText);
        return string.Join("\n", lines);
    }

    // ------------------------------------------------------------ reading from a file without opening the part
    static readonly Dictionary<string, (DateTime when, long size, PartInfo? info)> Cache = [];

    /// <summary>The part information stored in a Prism file, or null. Only that block is read; the geometry is skipped, and the answer is remembered until the file changes.</summary>
    public static PartInfo? ReadFile(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists) return null;
            lock (Cache)
                if (Cache.TryGetValue(path, out var hit) && hit.when == fi.LastWriteTimeUtc && hit.size == fi.Length) return hit.info;
            PartInfo? info = null;
            var bytes = File.ReadAllBytes(path);
            var reader = new Utf8JsonReader(bytes);
            if (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
                while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
                {
                    bool wanted = reader.ValueTextEquals("Info");
                    reader.Read();
                    if (wanted && reader.TokenType == JsonTokenType.StartObject) { info = JsonSerializer.Deserialize<PartInfo>(ref reader); break; }
                    reader.Skip();
                }
            lock (Cache) Cache[path] = (fi.LastWriteTimeUtc, fi.Length, info);
            return info;
        }
        catch (Exception) { return null; }
    }
}
