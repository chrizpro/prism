using System;
using Prism.Core;

namespace Prism.Help;

public static class ToolDocumentation
{
    /// <summary>A name as the website writes it in a link: lower case letters and digits, other runs turned into one hyphen.</summary>
    public static string Slug(string text) => System.Text.RegularExpressions.Regex.Replace(text.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');

    /// <summary>
    /// Where Learn More goes. A configured per-tool guide address still wins (the stable tool id is appended). Otherwise it is the
    /// published tool reference, at the tool's own row on its ribbon's page, for example <c>.../tools/solid#tool-fillet</c>.
    /// </summary>
    public static Uri? LinkFor(string toolId, string tab = "", string label = "")
    {
        var root = AppSettings.Current.ToolDocumentationBaseUrl;
        if (!string.IsNullOrWhiteSpace(root))
        {
            if (!Uri.TryCreate(root.TrimEnd('/') + "/", UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")) return null;
            return new Uri(uri, Uri.EscapeDataString(toolId));
        }
        if (tab.Length == 0 || label.Length == 0) return null;
        return new Uri($"{AppInfo.DocsUrl}tools/{Slug(tab)}#tool-{Slug(label)}");
    }
}
