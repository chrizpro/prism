using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Prism.Automation;

namespace Prism.Core.Scripting;

/// <summary>Where scripts and the scripting library live. The product name is only used here, so a rename is one edit.</summary>
public static class ScriptPaths
{
    public const string AppFolder = "Prism";
    public static string ScriptsFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), AppFolder, "Scripts");
    /// <summary>The Python package shipped with the application (put on PYTHONPATH when the editor runs a script).</summary>
    public static string PythonLibrary => Path.Combine(AppContext.BaseDirectory, "Scripting", "python");
    public static string Examples => Path.Combine(AppContext.BaseDirectory, "Scripting", "examples");

    /// <summary>Create the scripts folder, copy the examples in, and keep a copy of the library beside the scripts so VS Code and PyCharm can import it.</summary>
    public static string Prepare()
    {
        var dir = ScriptsFolder; Directory.CreateDirectory(dir);
        try
        {
            if (Directory.Exists(Examples))
                foreach (var f in Directory.GetFiles(Examples, "*.py")) { var to = Path.Combine(dir, Path.GetFileName(f)); if (!File.Exists(to)) File.Copy(f, to); }
            var lib = Path.Combine(PythonLibrary, "cadkit");
            if (Directory.Exists(lib))
            {
                var dest = Path.Combine(dir, "cadkit"); Directory.CreateDirectory(dest);
                foreach (var f in Directory.GetFiles(lib, "*.py")) File.Copy(f, Path.Combine(dest, Path.GetFileName(f)), true);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return dir;
    }
}

/// <summary>The address a running script needs to reach the application.</summary>
public sealed record ScriptEnvironment(int Port, string Token, string LibraryDir);

/// <summary>
/// A language scripts can be written in. Python is the first; a C# language can be added by implementing this interface (in-process through Roslyn,
/// or by launching dotnet) and registering it in <see cref="ScriptLanguages"/>: the editor, console, Stop button and external-editor support all work from this contract.
/// </summary>
public interface IScriptLanguage
{
    string Id { get; }
    string Name { get; }
    string Extension { get; }
    /// <summary>What a new script starts with.</summary>
    string Starter { get; }
    /// <summary>Whether scripts can run now (e.g. an interpreter is installed). <paramref name="detail"/> says which, or what is missing.</summary>
    bool IsAvailable(out string detail);
    /// <summary>Run a script file. Output lines go to <paramref name="output"/> (text, isError). Cancelling stops the script. Returns the exit code.</summary>
    Task<int> RunAsync(string scriptPath, ScriptEnvironment env, Action<string, bool> output, CancellationToken ct);
}

public static class ScriptLanguages
{
    public static IReadOnlyList<IScriptLanguage> All { get; } = [new PythonLanguage()];
    public static IScriptLanguage ForFile(string path) => All.FirstOrDefault(l => string.Equals(l.Extension, Path.GetExtension(path), StringComparison.OrdinalIgnoreCase)) ?? All[0];
}

public sealed class PythonLanguage : IScriptLanguage
{
    public string Id => "python";
    public string Name => "Python";
    public string Extension => ".py";
    public string Starter =>
        "import cadkit as cad\n\n" +
        "# Press F5 to run. Everything inside the block below is one undo step.\n" +
        "with cad.history(\"Plate with a hole\"):\n" +
        "    plate = cad.box(width=60, depth=40, height=8)\n" +
        "    hole = cad.cylinder(radius=6, height=20)\n" +
        "    cad.subtract(plate, hole)\n";

    (string exe, string args, string version)? _found; bool _looked;

    (string exe, string args, string version)? Find()
    {
        if (_looked) return _found;
        _looked = true;
        var candidates = new List<(string exe, string pre)>();
        if (Environment.GetEnvironmentVariable("CADKIT_PYTHON") is { Length: > 0 } custom) candidates.Add((custom, ""));
        candidates.Add(("py", "-3")); candidates.Add(("python", "")); candidates.Add(("python3", ""));
        foreach (var (exe, pre) in candidates)
        {
            try
            {
                var psi = new ProcessStartInfo(exe, (pre + " --version").Trim()) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
                using var p = Process.Start(psi); if (p == null) continue;
                var text = (p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd()).Trim();
                if (!p.WaitForExit(4000)) { try { p.Kill(); } catch { } continue; }
                if (p.ExitCode == 0 && text.StartsWith("Python 3")) { _found = (exe, pre, text); return _found; }
            }
            catch (Exception) { }
        }
        return null;
    }

    public bool IsAvailable(out string detail)
    {
        var f = Find();
        detail = f is { } x ? x.version : "Python 3 was not found. Install it from python.org (tick 'Add to PATH') and reopen this panel.";
        return f != null;
    }

    public async Task<int> RunAsync(string scriptPath, ScriptEnvironment env, Action<string, bool> output, CancellationToken ct)
    {
        var f = Find() ?? throw new InvalidOperationException("Python 3 was not found.");
        var psi = new ProcessStartInfo(f.exe, ((f.args + " -u ").TrimStart() + "\"" + scriptPath + "\"").Trim())
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(scriptPath) ?? Environment.CurrentDirectory,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        psi.Environment["CADKIT_PORT"] = env.Port.ToString(); psi.Environment["CADKIT_TOKEN"] = env.Token;
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        var old = Environment.GetEnvironmentVariable("PYTHONPATH");
        psi.Environment["PYTHONPATH"] = string.IsNullOrEmpty(old) ? env.LibraryDir : env.LibraryDir + Path.PathSeparator + old;
        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        proc.OutputDataReceived += (_, e) => { if (e.Data != null) output(e.Data, false); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) output(e.Data, true); };
        proc.Start(); proc.BeginOutputReadLine(); proc.BeginErrorReadLine();
        using var reg = ct.Register(() => { try { if (!proc.HasExited) proc.Kill(true); } catch { } });
        await proc.WaitForExitAsync(CancellationToken.None);
        return ct.IsCancellationRequested ? -1 : proc.ExitCode;
    }
}

/// <summary>One script being edited and run: its text, file, console output and the line a failure points at.</summary>
public sealed class ScriptSession
{
    public IScriptLanguage Language { get; set; } = ScriptLanguages.All[0];
    public string? Path { get; private set; }
    public string Text { get; set; } = "";
    public bool Dirty { get; set; }
    public bool Running { get; private set; }
    public int ErrorLine { get; private set; }
    public string ErrorText { get; private set; } = "";

    public event Action<string, bool>? Output;
    public event Action? StateChanged;
    /// <summary>Raised when the text was replaced from outside the editor box (New, Open).</summary>
    public event Action? Loaded;
    CancellationTokenSource? _cts;

    public string DisplayName => Path == null ? "Untitled" + Language.Extension : System.IO.Path.GetFileName(Path);

    public void New() { Path = null; Text = Language.Starter; Dirty = false; ErrorLine = 0; Loaded?.Invoke(); StateChanged?.Invoke(); }

    public void Open(string path)
    {
        Language = ScriptLanguages.ForFile(path); Path = path; Text = File.ReadAllText(path); Dirty = false; ErrorLine = 0;
        Loaded?.Invoke(); StateChanged?.Invoke();
    }

    public void Save(string? path = null)
    {
        path ??= Path ?? throw new InvalidOperationException("Choose a file name first.");
        File.WriteAllText(path, Text); Path = path; Dirty = false; StateChanged?.Invoke();
    }

    static readonly Regex FileLine = new(@"File ""(?<f>[^""]+)"", line (?<n>\d+)", RegexOptions.Compiled);

    public async Task<int> RunAsync()
    {
        if (Running) return -1;
        if (!Language.IsAvailable(out var why)) { Output?.Invoke(why, true); return -1; }
        var server = AutomationServer.Current;
        if (server == null) { Output?.Invoke("The application's automation service is not running (PRISM_AUTOMATION=0?).", true); return -1; }
        ScriptPaths.Prepare();
        var path = Path ?? System.IO.Path.Combine(ScriptPaths.ScriptsFolder, "_scratch" + Language.Extension);
        File.WriteAllText(path, Text);
        if (Path != null) Dirty = false;
        ErrorLine = 0; ErrorText = ""; Running = true; _cts = new CancellationTokenSource(); StateChanged?.Invoke();
        string lastError = ""; int errLine = 0;
        void Out(string line, bool err)
        {
            if (err)
            {
                lastError = line.Length > 0 ? line : lastError;
                var m = FileLine.Matches(line);
                foreach (Match x in m) if (string.Equals(System.IO.Path.GetFullPath(x.Groups["f"].Value), System.IO.Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)) errLine = int.Parse(x.Groups["n"].Value);
            }
            Output?.Invoke(line, err);
        }
        Output?.Invoke($"--- running {System.IO.Path.GetFileName(path)} ---", false);
        int code;
        try { code = await Language.RunAsync(path, new ScriptEnvironment(server.Port, server.Token, ScriptPaths.PythonLibrary), Out, _cts.Token); }
        catch (Exception e) { Output?.Invoke(e.Message, true); code = -2; }
        Running = false; ErrorLine = code > 0 ? errLine : 0; ErrorText = code > 0 ? lastError : "";
        Output?.Invoke(code == 0 ? "--- finished ---" : code == -1 ? "--- stopped ---" : $"--- failed (exit code {code}) ---", code > 0);
        StateChanged?.Invoke();
        return code;
    }

    public void Stop() => _cts?.Cancel();
}

/// <summary>Open a script in the user's own editor.</summary>
public static class ExternalEditors
{
    static string? Which(string name)
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            foreach (var ext in new[] { ".cmd", ".exe", ".bat", "" })
                try { var p = System.IO.Path.Combine(dir.Trim('"'), name + ext); if (File.Exists(p)) return p; } catch { }
        return null;
    }

    static string? PyCharmPath()
    {
        var onPath = Which("pycharm64") ?? Which("pycharm"); if (onPath != null) return onPath;
        string[] roots = [Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + @"\JetBrains", Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"\Programs"];
        foreach (var r in roots)
            try { if (Directory.Exists(r)) foreach (var d in Directory.GetDirectories(r, "PyCharm*")) { var exe = System.IO.Path.Combine(d, "bin", "pycharm64.exe"); if (File.Exists(exe)) return exe; } } catch { }
        return null;
    }

    /// <summary>Returns a message when the editor could not be started (so the user knows what to install), otherwise null.</summary>
    public static string? Open(string editor, string? file)
    {
        var folder = ScriptPaths.Prepare();
        file ??= folder;
        string? exe = editor == "vscode" ? Which("code") ?? LocalVsCode() : PyCharmPath();
        if (exe == null) return editor == "vscode" ? "Visual Studio Code was not found. Install it from code.visualstudio.com; the scripts folder is " + folder : "PyCharm was not found. Open the scripts folder in it yourself: " + folder;
        try
        {
            var psi = new ProcessStartInfo(exe) { UseShellExecute = true };
            if (editor == "vscode") { psi.ArgumentList.Add(folder); if (file != folder) psi.ArgumentList.Add(file); }
            else { psi.ArgumentList.Add(folder); if (file != folder) psi.ArgumentList.Add(file); }
            Process.Start(psi); return null;
        }
        catch (Exception e) { return "Could not start the editor: " + e.Message; }
    }

    static string? LocalVsCode()
    {
        var p = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Microsoft VS Code", "Code.exe");
        return File.Exists(p) ? p : null;
    }
}
