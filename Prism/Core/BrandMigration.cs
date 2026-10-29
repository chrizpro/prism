using System;
using System.IO;

namespace Prism.Core;

/// <summary>Preserve preferences and local support files from the previous product name.</summary>
internal static class BrandMigration
{
    public static void CopyLegacySettings()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(local)) return;
        string legacy = Path.Combine(local, "OurCAD");
        string current = Path.Combine(local, "Prism");
        string marker = Path.Combine(current, ".legacy-imported");
        if (!Directory.Exists(legacy) || File.Exists(marker)) return;
        try
        {
            Directory.CreateDirectory(current);
            foreach (string source in Directory.EnumerateFiles(legacy, "*", SearchOption.AllDirectories))
            {
                // Discovery files belong to a running instance; do not import them.
                if (Path.GetFileName(source) == "automation.json") continue;
                string target = Path.Combine(current, Path.GetRelativePath(legacy, source));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (!File.Exists(target)) File.Copy(source, target);
            }
            File.WriteAllText(marker, "Imported existing preferences without changing the originals.");
        }
        catch (IOException) { /* Retry on the next launch if a file is in use. */ }
        catch (UnauthorizedAccessException) { /* Preferences must not prevent startup. */ }
    }
}
