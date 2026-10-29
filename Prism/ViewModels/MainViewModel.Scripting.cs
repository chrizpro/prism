using System;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Prism.Core.Scripting;

namespace Prism.ViewModels;

public sealed partial class MainViewModel
{
    /// <summary>The script open in the editor panel (one at a time; its console shows what the last run printed).</summary>
    public ScriptSession Script { get; } = new();

    [ObservableProperty] public partial bool ShowScriptPanel { get; set; }
    partial void OnShowScriptPanelChanged(bool value)
    {
        if (value) OpenRightPanel("script"); else CloseRightPanel("script");
        OnPropertyChanged(nameof(ShowScriptEditor));
    }

    void EnsureScriptLoaded() { if (string.IsNullOrEmpty(Script.Text) && Script.Path == null) Script.New(); }

    public void ToggleScriptPanel()
    {
        ShowScriptPanel = !ShowScriptPanel;
        if (ShowScriptPanel) { ScriptPaths.Prepare(); EnsureScriptLoaded(); }
    }

    public void NewScript() { ScriptPaths.Prepare(); ShowScriptPanel = true; Script.New(); }

    /// <summary>Pick a script file, show it in the editor and run it.</summary>
    public async void RunScriptFile()
    {
        if (PickOpenFile == null) return;
        ScriptPaths.Prepare();
        var path = await PickOpenFile("Run a script", ScriptLanguages.All.Select(l => "*" + l.Extension).ToArray());
        if (path == null) return;
        ShowScriptPanel = true;
        try { Script.Open(path); } catch (Exception e) { Session.Say("Could not open the script: " + e.Message, true); return; }
        await Script.RunAsync();
    }

    /// <summary>Pick a picture file and open the Import Image panel for it.</summary>
    public async void ImportImage()
    {
        if (PickOpenFile == null) return;
        var path = await PickOpenFile("Import image", ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif"]);
        if (path == null) return;
        Session.StartCommand(new Prism.Commands.PlaceImageCommand(path));
    }

    public void OpenScriptsFolder()
    {
        var dir = ScriptPaths.Prepare();
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dir) { UseShellExecute = true }); }
        catch (Exception e) { Session.Say("Could not open the folder: " + e.Message, true); }
    }

    /// <summary>Open the current script (or the whole scripts folder) in VS Code or PyCharm.</summary>
    public void OpenScriptInEditor(string editor)
    {
        if (Script.Path == null || Script.Dirty)
        {
            // an unsaved script goes to the scratch file so the other editor has something to open
            try { var p = Script.Path ?? Path.Combine(ScriptPaths.Prepare(), "my_script" + Script.Language.Extension); if (!string.IsNullOrEmpty(Script.Text)) { if (Script.Path == null && File.Exists(p)) p = Path.Combine(ScriptPaths.ScriptsFolder, $"script_{DateTime.Now:HHmmss}{Script.Language.Extension}"); Script.Save(p); } }
            catch (Exception e) { Session.Say("Could not save the script first: " + e.Message, true); return; }
        }
        var msg = ExternalEditors.Open(editor, Script.Path);
        if (msg != null) Session.Say(msg, true); else Session.Say("Opened in " + (editor == "vscode" ? "VS Code" : "PyCharm") + ". Run it from there while this app is open: the scripts folder has the cadkit library beside your scripts.");
    }
}
