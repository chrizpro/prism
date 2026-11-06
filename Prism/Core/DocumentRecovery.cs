using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Prism.Core;

public static class NativeDocumentFile
{
    public static void WriteAtomic(string path, string contents)
    {
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = Path.Combine(Path.GetDirectoryName(path)!, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                byte[] data = Encoding.UTF8.GetBytes(contents);
                stream.Write(data); stream.Flush(true);
            }
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

public sealed record RecoveryEntry(string Path, string Name, DateTime SavedAt)
{
    public override string ToString() => Name + " — " + SavedAt.ToString("g");
}

public sealed class RecoveryStore(string folder)
{
    public string Folder { get; } = Path.GetFullPath(folder);
    public static RecoveryStore Local { get; } = new(Environment.GetEnvironmentVariable("PRISM_RECOVERY_DIR") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Prism", "recovery"));
    readonly Dictionary<Guid, string> paths = [];
    public string PathFor(CadSession session, string title)
    {
        if (paths.TryGetValue(session.DocId, out var existing)) return existing;
        string safe = new(title.Select(c => c < 32 || "<>:\"/\\|?*".Contains(c) ? '_' : c).Take(60).ToArray());
        if (string.IsNullOrWhiteSpace(safe)) safe = "Untitled";
        return paths[session.DocId] = Path.Combine(Folder, safe + "--" + session.DocId.ToString("N") + ".prism");
    }
    public IReadOnlyList<RecoveryEntry> List()
    {
        if (!Directory.Exists(Folder)) return [];
        return new DirectoryInfo(Folder).EnumerateFiles("*.prism").OrderByDescending(f => f.LastWriteTimeUtc)
            .Select(f => new RecoveryEntry(f.FullName, Path.GetFileNameWithoutExtension(f.Name).Split("--")[0], f.LastWriteTime)).ToArray();
    }
}

/// <summary>Scheduling only; the owner runs Tick on the UI thread where native sessions can be serialized.</summary>
public sealed class RecoverySchedule
{
    bool enabled;
    int minutes;
    DateTimeOffset due;
    public bool ShouldRun(DateTimeOffset now, bool requested, int intervalMinutes)
    {
        int interval = Math.Clamp(intervalMinutes, 1, 120);
        if (!requested) { enabled = false; return false; }
        if (!enabled || minutes != interval)
        {
            enabled = true; minutes = interval; due = now.AddMinutes(interval); return false;
        }
        if (now < due) return false;
        due = now.AddMinutes(interval);
        return true;
    }
}

public enum DocumentCloseDecision { Cancel, Save, Discard }
