using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Prism.Core;

namespace Prism.ViewModels;

/// <summary>A file shown on the home screen.</summary>
public sealed record HomeFile(string Path, string Name, string Folder, string When, string Kind);

/// <summary>
/// The workspace is the folder your parts live in: Save, Open and new-part names all start there, and the home screen lists what is in it.
/// (Not to be confused with the Mode, which only decides which ribbon tabs show.)
/// </summary>
public sealed partial class MainViewModel
{
    public Func<string, System.Threading.Tasks.Task<string?>>? PickFolder { get; set; }

    [ObservableProperty] public partial bool HomeOpen { get; set; }
    [ObservableProperty] public partial string WorkspaceFolder { get; set; } = "";
    [ObservableProperty] public partial string WorkspaceLabel { get; set; } = "No workspace";
    [ObservableProperty] public partial bool HasWorkspace { get; set; }
    [ObservableProperty] public partial bool HasWorkspaceFiles { get; set; }
    [ObservableProperty] public partial bool HasRecent { get; set; }
    [ObservableProperty] public partial bool ShowHomeOnStart { get; set; } = true;

    public ObservableCollection<HomeFile> WorkspaceFiles { get; } = [];
    public ObservableCollection<HomeFile> RecentList { get; } = [];

    public static string DefaultWorkspacePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Prism");

    static readonly string[] OpenPatterns = ["*.prism", "*.ourcad", "*.step", "*.stp", "*.iges", "*.igs", "*.brep", "*.dxf", "*.stl", "*.obj", "*.ply", "*.3mf"];

    void InitHome()
    {
        var s = AppSettings.Current;
        ShowHomeOnStart = s.ShowHomeOnStart;
        // tests and scripts drive a bare window: they set PRISM_NO_HOME so the home screen does not cover it
        HomeOpen = s.ShowHomeOnStart && Environment.GetEnvironmentVariable("PRISM_NO_HOME") != "1";
        RefreshHome();
    }

    partial void OnShowHomeOnStartChanged(bool value) { AppSettings.Current.ShowHomeOnStart = value; AppSettings.Save(false); }

    public void OpenHome() { RefreshHome(); HomeOpen = true; }
    public void CloseHome() => HomeOpen = false;

    static string When(DateTime t)
    {
        var d = DateTime.Now.Date - t.Date;
        return d.Days == 0 ? "Today " + t.ToString("t") : d.Days == 1 ? "Yesterday" : d.Days < 7 ? t.ToString("dddd") : t.ToString(t.Year == DateTime.Now.Year ? "MMM d" : "MMM d, yyyy");
    }

    static string KindOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".prism" or ".ourcad" => "Part", ".step" or ".stp" => "STEP", ".stl" or ".obj" or ".ply" or ".3mf" => "Mesh", _ => "File",
    };

    static HomeFile? Describe(string path)
    {
        try { var fi = new FileInfo(path); return fi.Exists ? new HomeFile(path, Path.GetFileNameWithoutExtension(path), Path.GetDirectoryName(path) ?? "", When(fi.LastWriteTime), KindOf(path)) : null; }
        catch (Exception) { return null; }
    }

    public void RefreshHome()
    {
        var s = AppSettings.Current;
        string? ws = s.WorkspacePath is { Length: > 0 } p && Directory.Exists(p) ? p : null;
        WorkspaceFolder = ws ?? ""; HasWorkspace = ws != null;
        WorkspaceLabel = ws != null ? "📁 " + (Path.GetFileName(ws.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) is { Length: > 0 } n ? n : ws) : "📁 Choose a workspace";
        WorkspaceFiles.Clear();
        if (ws != null)
        {
            try
            {
                foreach (var f in new DirectoryInfo(ws).EnumerateFiles("*", SearchOption.AllDirectories).Where(f => f.Extension.Equals(".prism", StringComparison.OrdinalIgnoreCase) || f.Extension.Equals(".ourcad", StringComparison.OrdinalIgnoreCase)).OrderByDescending(f => f.LastWriteTime).Take(40))
                    if (Describe(f.FullName) is { } h) WorkspaceFiles.Add(h);
            }
            catch (Exception) { /* unreadable folder: show it empty */ }
        }
        HasWorkspaceFiles = WorkspaceFiles.Count > 0;
        RecentList.Clear();
        foreach (var r in s.RecentFiles.ToList())
            if (Describe(r) is { } h) RecentList.Add(h);
        HasRecent = RecentList.Count > 0;
    }

    public void SetWorkspaceFolder(string path)
    {
        try { Directory.CreateDirectory(path); } catch (Exception e) { Session.Say("Could not use that folder: " + e.Message, true); return; }
        AppSettings.Current.WorkspacePath = path; AppSettings.Save(false);
        RefreshHome();
        Session.Say("Workspace: " + path, false);
    }

    public void UseDefaultWorkspace() => SetWorkspaceFolder(DefaultWorkspacePath);

    public async void ChooseWorkspaceFolder()
    {
        if (PickFolder == null) return;
        var p = await PickFolder("Choose your workspace folder");
        if (p != null) SetWorkspaceFolder(p);
    }

    public void ShowWorkspaceInExplorer()
    {
        if (!HasWorkspace) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{WorkspaceFolder}\"") { UseShellExecute = true }); } catch (Exception) { }
    }

    void AddRecent(string path)
    {
        var r = AppSettings.Current.RecentFiles;
        r.RemoveAll(x => x.Equals(path, StringComparison.OrdinalIgnoreCase)); r.Insert(0, path);
        if (r.Count > 12) r.RemoveRange(12, r.Count - 12);
        AppSettings.Save(false);
        RefreshHome();
    }

    /// <summary>A name for a new part that does not clash with anything already in the workspace.</summary>
    string NextPartName()
    {
        if (!HasWorkspace) return "Untitled.prism";
        for (int i = 1; i < 10000; i++) { var n = $"Part {i}.prism"; if (!File.Exists(Path.Combine(WorkspaceFolder, n))) return n; }
        return "Untitled.prism";
    }

    /// <summary>A blank part (the untouched tab is reused) and straight into the modeling window.</summary>
    public void NewPart()
    {
        if (ActiveDocument is not { IsPristine: true }) NewDocumentTab();
        CloseHome();
    }

    public void OpenPath(string path)
    {
        if (!File.Exists(path)) { Session.Say("That file is no longer there: " + path, true); RefreshHome(); return; }
        Guard(() =>
        {
            var doc = OpenTarget();
            if ((path.EndsWith(".prism", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".ourcad", StringComparison.OrdinalIgnoreCase))) doc.Open(path);
            else doc.ImportFile(path);
        });
        AddRecent(path);
        CloseHome();
        ShowOpeningTab();
    }
}
