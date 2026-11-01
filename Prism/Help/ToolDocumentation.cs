using System;
using Prism.Core;

namespace Prism.Help;

public static class ToolDocumentation
{
    public static Uri? LinkFor(string toolId)
    {
        var root = AppSettings.Current.ToolDocumentationBaseUrl;
        if (string.IsNullOrWhiteSpace(root) || !Uri.TryCreate(root.TrimEnd('/') + "/", UriKind.Absolute, out var uri)
            || uri.Scheme is not ("https" or "http")) return null;
        return new Uri(uri, Uri.EscapeDataString(toolId));
    }
}
