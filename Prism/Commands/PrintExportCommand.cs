using System;
using System.IO;
using System.Linq;
using Prism.Core;
using Prism.Core.Export;

namespace Prism.Commands;

/// <summary>Write bodies to a chosen folder for a slicer, and optionally open them in one. The user keeps slicing in their own tool.</summary>
public sealed class PrintExportCommand : AnalysisCommand
{
    // Remembered for the session so a repeat export lands in the same file the slicer already has open.
    static string _folder = "", _slicerPath = "";
    static int _format, _quality = 1, _slicer;

    readonly TextParam _folderParam, _name, _path;
    readonly ChoiceParam _formatParam, _quality_, _slicer_;
    readonly BoolParam _selection, _perBody;

    public PrintExportCommand()
    {
        _folderParam = Text("Folder", _folder.Length > 0 ? _folder : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Prism", "Print"));
        _name = Text("File name", "part");
        _formatParam = Choice("Format", _format, PrintExport.FormatNames);
        _quality_ = Choice("Curve quality", _quality, PrintExport.QualityNames);
        _selection = Check("Selection only (all visible if none selected)", true);
        _perBody = Check("One file per body", false);
        _slicer_ = Choice("Open in", _slicer, PrintExport.SlicerNames);
        _path = Text("Program path (other)", _slicerPath);
        Add(new ActionParam { Label = "Export", Run = () => Run(false) });
        Add(new ActionParam { Label = "Export and open in slicer", Run = () => Run(true) });
    }

    public override string Title => "Export for printing";
    public override string Icon => "print3d";
    public override string Hint => "Write the model to a folder in a format your slicer reads. 3MF keeps bodies separate, in millimetres. To update a model already open in the slicer, export again to the same file and use the slicer's Reload from disk.";
    protected override void Analyze() { }

    // The document is only known once the command begins, so the project-relative defaults are set here.
    protected override void OnBegin()
    {
        if (S.FilePath is not { } project) return;
        if (_folder.Length == 0) _folderParam.Value = Path.Combine(Path.GetDirectoryName(project)!, "print");
        _name.Value = Path.GetFileNameWithoutExtension(project);
    }

    void Run(bool open)
    {
        try
        {
            _folder = _folderParam.Value; _format = _formatParam.Index; _quality = _quality_.Index; _slicer = _slicer_.Index; _slicerPath = _path.Value;
            var files = PrintExport.Export(S, new PrintExportOptions(_folder, _name.Value, (PrintFormat)_format, _selection.Value, _perBody.Value, PrintExport.QualityScale[_quality]));
            Results.Clear();
            foreach (var f in files) Row("Saved", f);
            if (!open) { S.Say($"Exported {files.Count} file(s) to {_folder}"); return; }
            if (_slicer == 0) { Row("Slicer", "Choose a slicer to open the file.", 2); return; }
            var exe = PrintExport.FindSlicer(_slicer, _path.Value);
            if (exe == null) { Row("Slicer", "Not found. Choose Other program and enter its path. The files are saved.", 3); return; }
            var running = PrintExport.IsRunning(exe);
            PrintExport.Launch(exe, files);
            Row("Slicer", running ? "Already open: in the slicer use Reload from disk to update an existing model." : "Opened " + Path.GetFileName(exe));
        }
        catch (Exception e) { S.Say(e.Message, true); }
    }
}
