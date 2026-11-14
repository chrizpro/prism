using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using Prism.Core;
using Prism.Core.Export;
using Prism.Meshing;

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
    PrintExport.Review? _review;
    Guid[] _reviewSelection = [];
    SceneObject[] _reviewObjects = [];
    PrintExportOptions? _reviewOptions;

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
        Add(new ActionParam { Label = "Review export", Run = () => Review() });
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

    PrintExportOptions Options() =>
        new(_folderParam.Value, _name.Value, (PrintFormat)_formatParam.Index, _selection.Value,
            _perBody.Value, PrintExport.QualityScale[_quality_.Index]);

    Guid[] CurrentSelection() => S.SelectedObjects.Select(body => body.Id).Order().ToArray();

    void Review()
    {
        _review = null;
        Results.Clear();
        var options = Options();
        try
        {
            var review = PrintExport.Prepare(S, options);
            if (review.UsedVisibleFallback)
                Row("Selection", "No exportable bodies selected. Reviewing visible bodies instead.", 2);
            if (review.Parts.Count == 0) Row("Export", "No exportable bodies found.", 3);
            else
            {
                var grouping = options.PerBody ? "one file per body"
                    : options.Format == PrintFormat.ThreeMf ? "separate objects in one file" : "meshes combined in one file";
                string count = review.Parts.Count == 1 ? "1 body" : $"{review.Parts.Count} bodies";
                Row("Export", $"{count} · {grouping}");
                Row("Scale", options.Format == PrintFormat.ThreeMf ? "Millimetres" : "Geometry in millimetres. Import at millimetre scale.");
            }
            foreach (var part in review.Parts)
            {
                var size = part.Mesh.Size;
                Row(part.Name, $"{size.X:0.###} × {size.Y:0.###} × {size.Z:0.###} mm");
                var report = MeshAnalysis.Report(part.Mesh);
                if (!report.Closed) Row(part.Name, $"Open mesh ({report.BoundaryEdges} boundary edges).", 2);
                if (report.NonManifoldEdges > 0 || report.InconsistentEdges > 0)
                    Row(part.Name, "Mesh has non-manifold edges or inconsistent face directions.", 2);
            }
            foreach (var name in review.Skipped) Row(name, "No mesh triangles. This body will be skipped.", 2);
            _reviewOptions = options;
            _reviewSelection = CurrentSelection();
            _reviewObjects = S.Objects.ToArray();
            _review = review;
        }
        catch (Exception e) when (e is not OutOfMemoryException) { Row("Review failed", e.Message, 3); }
    }

    void Run(bool open)
    {
        try
        {
            var options = Options();
            if (_review == null || _reviewOptions != options ||
                !_reviewSelection.SequenceEqual(CurrentSelection()) || !_reviewObjects.SequenceEqual(S.Objects))
            {
                Review();
                if (_review?.Parts.Count > 0) Row("Next step", "Review these bodies, then choose an export action to save.");
                return;
            }
            var files = PrintExport.WriteFiles(options, _review.Parts);
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
