using System;
using System.Diagnostics;
using System.IO;
using Prism.Core;
using Prism.Core.Export;

namespace Prism.Commands;

/// <summary>Get the model into augmented reality: export GLB + USDZ and share a page a phone on the same Wi-Fi can open.</summary>
public sealed class ArCommand : Command
{
    readonly TextParam _name;
    readonly ChoiceParam _size;

    public ArCommand()
    {
        _name = Text("Name", "", "folder and page title");
        _size = Choice("Size in AR", 0, "Real size (1:1)", "Tabletop (about 30 cm)", "Fit to 1 metre");
        Add(new ActionParam { Label = "Export for AR", Run = () => Export(false) });
        Add(new ActionParam { Label = "Export and share to phone", Run = () => Export(true) });
        Add(new ActionParam { Label = "Stop sharing", Run = () => { ArExport.StopShare(); Refresh(); S.Say("Sharing stopped."); } });
        Add(new ActionParam { Label = "Open the folder", Run = OpenFolder });
    }

    public override string Title => "AR";
    public override string Icon => "ar";
    public override string OkText => "Done";
    public override string Hint => "Export the visible model as GLB (Android, web) and USDZ (iPhone / iPad). 'Share to phone' serves it on your Wi-Fi: open the address on the phone, then tap 'View in your space'.";

    string Folder => ArExport.PackageFolder(string.IsNullOrWhiteSpace(_name.Value) ? (S.FilePath != null ? Path.GetFileNameWithoutExtension(S.FilePath) : "model") : _name.Value);
    string TitleText => string.IsNullOrWhiteSpace(_name.Value) ? (S.FilePath != null ? Path.GetFileNameWithoutExtension(S.FilePath) : "Prism model") : _name.Value;
    double Fit => _size.Index switch { 1 => 0.3, 2 => 1.0, _ => 0 };

    void Export(bool share)
    {
        try
        {
            var f = ArExport.ExportPackage(S, Folder, TitleText, Fit);
            if (share) { var url = ArExport.StartShare(f); S.Say($"Open {url} on your phone (same Wi-Fi)."); }
            else S.Say($"Exported to {f}");
            Refresh();
        }
        catch (Exception e) { S.Say(e.Message, true); }
    }

    void OpenFolder()
    {
        try { Process.Start(new ProcessStartInfo { FileName = Folder, UseShellExecute = true }); } catch (Exception e) { S.Say(e.Message, true); }
    }

    void Refresh()
    {
        Results.Clear();
        Results.Add(new ResultLine("Folder", Folder));
        if (ArExport.Sharing) Results.Add(new ResultLine("On your phone, open", ArExport.ShareUrl ?? ""));
    }

    protected override CommandOutput? Build() { Refresh(); return new CommandOutput { NoOp = true }; }
}
