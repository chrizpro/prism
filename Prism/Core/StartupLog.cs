using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Prism.Core;

/// <summary>
/// How long each start-up stage took, measured from the moment the process started (v0.5 splash work, 2026-11-23). The desktop writes it to
/// %LOCALAPPDATA%\Prism\startup.log on every start, so slow stages can be found on the machine that is slow.
/// </summary>
public static class StartupLog
{
    static readonly List<(string stage, double ms)> _marks = [];
    static double _last;

    /// <summary>Milliseconds since the process started.</summary>
    public static double Elapsed
    {
        get
        {
            try { return (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds; }
            catch (Exception) { return Environment.TickCount64; }
        }
    }

    /// <summary>Record that a stage has just finished.</summary>
    public static void Mark(string stage)
    {
        double now = Elapsed;
        _marks.Add((stage, now - _last));
        _last = now;
    }

    public static IReadOnlyList<(string stage, double ms)> Marks => _marks;

    public static string Report() =>
        $"Prism {AppInfo.DisplayVersion} start-up, {DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}" +
        string.Join(Environment.NewLine, _marks.Select(m => $"{m.ms,8:0} ms  {m.stage}")) + Environment.NewLine +
        $"{_marks.Sum(m => m.ms),8:0} ms  total{Environment.NewLine}" +
        (_marks.FindIndex(m => m.stage.StartsWith("Main window shown")) is int shown and >= 0
            ? $"{_marks.Take(shown + 1).Sum(m => m.ms),8:0} ms  until the main window was on screen{Environment.NewLine}" : "");

    public static void Save()
    {
        try
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Prism");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "startup.log"), Report());
        }
        catch (Exception) { /* a log that cannot be written must never stop the app */ }
    }
}
