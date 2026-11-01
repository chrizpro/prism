using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using Prism.Core;
using Prism.Core.Export;

namespace Prism.Commands;

/// <summary>Write bodies to a chosen folder for a slicer, and optionally open them in one. The user keeps slicing in their own tool.</summary>
public sealed class PrintExportCommand : AnalysisCommand
{
    sealed record Preferences(string Folder="", string SlicerPath="", int Format=0, int Quality=1, int Slicer=0, bool PerBody=false);
    static string SettingsFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Prism","print-export.json");
    static readonly Dictionary<string,Preferences> Settings=ReadSettings();
    static Dictionary<string,Preferences> ReadSettings(){try{return JsonSerializer.Deserialize<Dictionary<string,Preferences>>(File.ReadAllText(SettingsFile))??[];}catch{return [];}}
    readonly bool _resin;
    string SettingsKey=>(S.FilePath??"unsaved")+(_resin?"|resin":"|fdm");

    readonly TextParam _folderParam, _name, _path;
    readonly ChoiceParam _formatParam, _quality_, _slicer_;
    readonly BoolParam _selection, _perBody;

    public PrintExportCommand(bool resin=false)
    {
        _resin=resin;
        _folderParam = Text("Folder", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Prism", "Print"));
        _name = Text("File name", "part");
        _formatParam = Choice("Format", 0, PrintExport.FormatNames);
        _quality_ = Choice("Curve quality", 1, PrintExport.QualityNames);
        _selection = Check("Selection only (all visible if none selected)", true);
        _perBody = Check("One file per body", false);
        _slicer_ = Choice("Open in", 0, PrintExport.SlicerNames);
        _path = Text("Program path (optional override)", "");
        Add(new ActionParam { Label = "Export", Run = () => Run(false) });
        Add(new ActionParam { Label = "Export and open in slicer", Run = () => Run(true) });
    }

    public override string Title => _resin?"Export for resin":"Export for printing";
    public override string Icon => "print3d";
    public override string Hint => "Write the model to a folder in a format your slicer reads. 3MF keeps bodies separate, in millimetres. To update a model already open in the slicer, export again to the same file and use the slicer's Reload from disk.";
    protected override void Analyze() { }

    // The document is only known once the command begins, so the project-relative defaults are set here.
    protected override void OnBegin()
    {
        if(S.FilePath is{} project){_folderParam.Value=Path.Combine(Path.GetDirectoryName(project)!,"print",_resin?"resin":"fdm");_name.Value=Path.GetFileNameWithoutExtension(project);}
        if(Settings.TryGetValue(SettingsKey,out var p))
        {
            _folderParam.Value=p.Folder;_path.Value=p.SlicerPath;_perBody.Value=p.PerBody;
            _formatParam.Index=Math.Clamp(p.Format,0,PrintExport.FormatNames.Length-1);_quality_.Index=Math.Clamp(p.Quality,0,PrintExport.QualityNames.Length-1);_slicer_.Index=Math.Clamp(p.Slicer,0,PrintExport.SlicerNames.Length-1);
        }
    }

    void Run(bool open)
    {
        try
        {
            var files = PrintExport.Export(S, new PrintExportOptions(_folderParam.Value, _name.Value, (PrintFormat)_formatParam.Index, _selection.Value, _perBody.Value, PrintExport.QualityScale[_quality_.Index]));
            Results.Clear();
            foreach (var f in files) Row("Saved", f);
            Settings[SettingsKey]=new(_folderParam.Value,_path.Value,_formatParam.Index,_quality_.Index,_slicer_.Index,_perBody.Value);
            try{Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);File.WriteAllText(SettingsFile,JsonSerializer.Serialize(Settings));}catch(Exception e){Row("Preferences","Files exported; preferences could not be saved: "+e.Message,2);}
            if (!open) { S.Say($"Exported {files.Count} file(s) to {_folderParam.Value}"); return; }
            if (_slicer_.Index == 0) { Row("Slicer", "Choose a slicer to open the file.", 2); return; }
            var exe = PrintExport.FindSlicer(_slicer_.Index, _path.Value);
            if (exe == null) { Row("Slicer", "Not found. Choose Other program and enter its path. The files are saved.", 3); return; }
            var running = PrintExport.IsRunning(exe);
            PrintExport.Launch(exe, files);
            Row("Slicer", running ? "Already open: in the slicer use Reload from disk to update an existing model." : "Opened " + Path.GetFileName(exe));
        }
        catch (Exception e) { S.Say(e.Message, true); }
    }
}
