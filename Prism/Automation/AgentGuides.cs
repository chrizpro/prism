using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Prism.Automation;

/// <summary>The how-to guides for agents (the in-app assistant and MCP clients): Markdown skill files shipped beside the scripts.
/// The files live in the repository under .claude/skills so coding agents find them as skills; the build copies them to Scripting/skills.</summary>
public static class AgentGuides
{
    public sealed record Guide(string Topic, string Description, string Text);

    /// <summary>Every guide that can be found, topic = folder name without a leading "prism-".</summary>
    public static IReadOnlyList<Guide> All()
    {
        var found = new List<Guide>();
        foreach (var root in Roots())
        {
            if (!Directory.Exists(root)) continue;
            foreach (var dir in Directory.GetDirectories(root).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                var file = Path.Combine(dir, "SKILL.md");
                if (!File.Exists(file)) continue;
                var name = Path.GetFileName(dir).ToLowerInvariant();
                var topic = name.StartsWith("prism-") ? name["prism-".Length..] : name;
                if (found.Any(g => g.Topic == topic)) continue;
                var (description, body) = Parse(File.ReadAllText(file));
                found.Add(new Guide(topic, description, body));
            }
        }
        return found;
    }

    /// <summary>Splits the YAML front matter from the Markdown body and returns the description line.</summary>
    public static (string description, string body) Parse(string text)
    {
        text = text.Replace("\r\n", "\n");
        if (!text.StartsWith("---\n")) return ("", text);
        int end = text.IndexOf("\n---\n", 4, StringComparison.Ordinal);
        if (end < 0) return ("", text);
        string front = text[4..end], body = text[(end + 5)..].TrimStart('\n');
        var line = front.Split('\n').FirstOrDefault(l => l.StartsWith("description:", StringComparison.OrdinalIgnoreCase));
        return (line == null ? "" : line["description:".Length..].Trim().Trim('"'), body);
    }

    static IEnumerable<string> Roots()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "Scripting", "skills");
        // running from a source checkout: the repository copy
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, ".claude", "skills");
            if (Directory.Exists(candidate)) { yield return candidate; yield break; }
        }
    }
}
