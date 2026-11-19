using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Prism.Core;
using Prism.Meshing;

namespace Prism.Commands;

/// <summary>What the mesh import options remembered between runs, and whether to show them at all.</summary>
public sealed record MeshImportPrefs(int Units = 0, int UpAxis = 0, double Weld = 0, bool Repair = false, int Split = 0, bool Ask = true,
    bool Reduce = false, int ReduceBy = 0, double ReducePercent = 25, int ReduceCount = 500_000)
{
    /// <summary>Where the preferences live. Tests point this somewhere else.</summary>
    public static string File { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Prism", "mesh-import.json");

    public static MeshImportPrefs Load()
    {
        try { return JsonSerializer.Deserialize<MeshImportPrefs>(System.IO.File.ReadAllText(File)) ?? new(); } catch (Exception) { return new(); }
    }

    public void Save()
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(File)!); System.IO.File.WriteAllText(File, JsonSerializer.Serialize(this)); } catch (Exception) { }
    }

    public MeshImportOptions ToOptions() => new()
    {
        Units = (ImportUnits)Math.Clamp(Units, 0, 4), UpAxis = (ImportUpAxis)Math.Clamp(UpAxis, 0, 2), WeldTolerance = Math.Max(0, Weld), AutoRepair = Repair, Split = (ImportSplit)Math.Clamp(Split, 0, 3),
        Reduce = Reduce, ReducePercent = Math.Clamp(ReducePercent, 0.5, 100), ReduceToCount = ReduceBy == 1 ? Math.Max(4, ReduceCount) : 0,
    };
}

/// <summary>
/// The small options step behind Import Mesh. The file is read on a background thread while the panel is already open (progress is shown; Cancel stops the read),
/// then the options are applied on a background thread too. With "Show these options each time" off the panel applies the remembered options and imports by itself.
/// </summary>
public sealed class ImportMeshCommand : Command
{
    readonly string _path;
    readonly bool _auto;
    readonly ChoiceParam _units, _up, _split, _reduceBy;
    readonly NumberParam _weld, _percent;
    readonly IntParam _count;
    readonly BoolParam _repair, _reduce, _ask;

    SynchronizationContext? _ui;
    CancellationTokenSource _readCts = new(), _procCts = new();
    MeshFile? _file; string? _readError; double _frac; string _status = "Reading…";
    MeshImportResult? _result; string? _resultKey, _runningKey; string? _procError;

    public ImportMeshCommand(string path, bool auto = false)
    {
        _path = path; _auto = auto;
        _units = Choice("Units in the file", 0, "Automatic (3MF unit, else mm)", "Millimetres", "Centimetres", "Metres", "Inches");
        _up = Choice("Up axis in the file", 0, "Z is up (keep as is)", "Y is up (rotate to Z-up)", "X is up (rotate to Z-up)");
        _split = Choice("Bodies", 0, "Automatic (3MF objects)", "One body", "Split by file parts (object, group, solid)", "Split by connected pieces");
        _weld = Num("Weld distance (0 = identical corners only)", 0, 0, 100, 0.001);
        _repair = Check("Repair on import (merge, clean, fix normals, fill holes)", false);
        _reduce = Check("Reduce faces", false);
        _reduceBy = Choice("Reduce to", 0, "A percentage of the triangles", "A triangle count");
        _percent = Num("Keep", 25, 0.5, 100, 5, "%");
        _count = Add(new IntParam { Label = "Triangles", Value = 500_000, Min = 4, Max = 50_000_000 });
        _ask = Check("Show these options each time", true);
    }

    public override string Title => "Import Mesh";
    public override string Hint => "Choose how the file is read: its units, which way is up, whether to weld near-identical vertices, repair it and split it into bodies. Untick 'Show these options each time' to import with these settings from now on; cancelling an automatic import brings the options back.";
    public override string Icon => "mesh-import";

    protected override void UpdateVisibility()
    {
        if (_count == null) return;
        _reduceBy.IsVisible = _reduce.Value;
        _percent.IsVisible = _reduce.Value && _reduceBy.Index == 0;
        _count.IsVisible = _reduce.Value && _reduceBy.Index == 1;
    }
    public override string OkText => "Import";
    public override bool SupportsPreview => false;
    public override bool CanEdit => false;

    void Row(string label, string value, int level = 0) => Results.Add(new ResultLine(label, value, level));

    MeshImportPrefs Current() => new(_units.Index, _up.Index, _weld.Value, _repair.Value, _split.Index, _ask.Value, _reduce.Value, _reduceBy.Index, _percent.Value, _count.Value);

    void Post(Action a) { if (_ui != null) _ui.Post(_ => a(), null); else a(); }

    protected override void OnBegin()
    {
        _ui = SynchronizationContext.Current;
        var p = MeshImportPrefs.Load();
        _units.Index = Math.Clamp(p.Units, 0, 4); _up.Index = Math.Clamp(p.UpAxis, 0, 2); _split.Index = Math.Clamp(p.Split, 0, 3);
        _weld.Value = Math.Max(0, p.Weld); _repair.Value = p.Repair; _ask.Value = _auto ? false : p.Ask;
        _reduce.Value = p.Reduce; _reduceBy.Index = Math.Clamp(p.ReduceBy, 0, 1); _percent.Value = Math.Clamp(p.ReducePercent, 0.5, 100); _count.Value = Math.Max(4, p.ReduceCount);
        UpdateVisibility();
        var ct = _readCts.Token;
        var work = S.Work.Begin("Read mesh", _readCts.Cancel);
        var progress = new Progress<ImportProgress>(pr => { work.Report(pr.Status, pr.Fraction); _frac = pr.Fraction; _status = pr.Status; if (_file == null) Post(OnChanged); });
        Task.Run(() => MeshIO.Read(_path, progress, ct), ct).ContinueWith(t => Post(() =>
        {
            work.Dispose();
            if (ct.IsCancellationRequested) return;
            if (t.IsFaulted) _readError = t.Exception!.GetBaseException() is OutOfMemoryException ? OutOfMemory : t.Exception!.GetBaseException().Message; else if (t.IsCompletedSuccessfully) _file = t.Result;
            OnChanged();
            if (_auto && _file != null && S.ActiveCommand == this) { Start(); }
        }), TaskScheduler.Default);
    }

    /// <summary>Automatic mode: wait for the background processing, then commit.</summary>
    void Start()
    {
        if (S.ActiveCommand != this) return;
        if (_result != null && _resultKey == Key()) { S.CommitCommand(); return; }
        OnChanged();     // starts the processing; its completion commits
    }

    public override void OnEnd() { _readCts.Cancel(); _procCts.Cancel(); }

    public override void OnCancel()
    {
        if (!_auto) return;
        var p = MeshImportPrefs.Load();
        if (!p.Ask) { (p with { Ask = true }).Save(); S.Say("Import cancelled. The import options will be shown next time."); }
    }

    string Key() => $"{_units.Index}|{_up.Index}|{_split.Index}|{_weld.Value:R}|{_repair.Value}|{_reduce.Value}|{_reduceBy.Index}|{_percent.Value:R}|{_count.Value}";

    protected override CommandOutput? Build()
    {
        Results.Clear();
        if (_readError != null) { Row("Problem", _readError, 3); Error = _readError; return null; }
        if (_file == null) { Row("Reading", $"{_status}  {_frac:P0}"); Error = "Still reading the file."; return null; }
        var f = _file;
        Row("File", $"{Path.GetFileName(_path)}  ({f.Format}, {f.Mesh.TriCount:#,0} triangles, {f.PartCount} part{(f.PartCount == 1 ? "" : "s")})");
        double scale = MeshImporter.ScaleFor((ImportUnits)_units.Index, f.UnitToMm, out string label);
        Row("Units", f.Unit != null && _units.Index == 0 ? $"{label} (declared by the file)" : label);
        if (f.Mesh.HasColors) Row("Colours", f.Mesh.VertexColors != null ? "Per-vertex colours found" : "Per-face colours found");
        if (MeshImporter.SizeHint(MeshImporter.RawExtent(f), scale, label) is { } hint) Row("Size check", hint, 2);
        if (!_reduce.Value && f.Mesh.TriCount > MeshImportOptions.SuggestReductionAbove)
            Row("Large mesh", $"{f.Mesh.TriCount:#,0} triangles. Turn on Reduce faces to make it lighter before it enters the document.", 2);
        if (MemoryWarning(_reduce.Value ? MeshImporter.ReduceTarget(f.Mesh.TriCount, f.Mesh.TriCount, Options()) : f.Mesh.TriCount) is { } memory) Row("Memory", memory, 2);

        string key = Key();
        if (_result == null || _resultKey != key)
        {
            if (Committing)
            {
                try { _result = MeshImporter.Process(f, Options(), null, default); _resultKey = key; }
                catch (OutOfMemoryException) { GC.Collect(); Row("Problem", OutOfMemory, 3); Error = OutOfMemory; return null; }
                catch (Exception e) { Row("Problem", e.Message, 3); Error = e.Message; return null; }
            }
            else
            {
                if (_procError != null && _runningKey == key) { Row("Problem", _procError, 3); Error = _procError; return null; }
                if (_runningKey != key) StartProcessing(key);
                Row("Processing", "Applying the options…");
                return new CommandOutput { NoOp = true };
            }
        }
        var r = _result!;
        Row("Result", $"{r.TriCount:#,0} triangles in {r.Parts.Count} {(r.Parts.Count == 1 ? "body" : "bodies")}" +
            (r.TriCount < r.TriCountBeforeReduce ? $" (reduced from {r.TriCountBeforeReduce:#,0})" : ""), r.Parts.Count > 0 ? 1 : 3);
        if (r.Parts.Count > 0) Row("Size", $"{r.SizeMm.X:#,0.##} × {r.SizeMm.Y:#,0.##} × {r.SizeMm.Z:#,0.##} mm");
        foreach (var m in r.Messages.Where(m => m != r.SizeWarning)) Row("Note", m, 2);
        if (r.Parts.Count == 0) { Error = "That file contains no triangles."; return null; }

        var o = new CommandOutput { Label = "Import mesh", Icon = "mesh-import" };
        bool small = r.TriCount <= 200_000;
        if (small || Committing)
            foreach (var part in r.Parts)
                o.Add.Add(new NewObject(null, r.Parts.Count == 1 ? r.Name : part.Name, ObjKind.Mesh, part.Color ?? 0xFFC8D0DA, Tri: part.Mesh));
        else Row("Preview", "Not shown for large meshes", 0);
        var prefs = Current();
        string msg = $"Imported {r.TriCount:#,0} triangles" + (r.TriCount < r.TriCountBeforeReduce ? $" (reduced from {r.TriCountBeforeReduce:#,0})" : "") + (r.Parts.Count > 1 ? $" in {r.Parts.Count} bodies" : "") + $" from {Path.GetFileName(_path)}" + (r.SizeWarning != null ? ". " + r.SizeWarning : "");
        o.After = s => { prefs.Save(); s.ZoomExtents(); s.Say(msg); };
        return o;
    }

    MeshImportOptions Options() => Current().ToOptions();

    const string OutOfMemory = "There is not enough memory to import this mesh. Turn on Reduce faces, close other documents, or import fewer bodies.";

    /// <summary>A warning when the mesh would need more than about 70% of the memory that is free now; null otherwise.</summary>
    internal static string? MemoryWarning(long triangles)
    {
        var info = GC.GetGCMemoryInfo();
        long free = info.TotalAvailableMemoryBytes - info.MemoryLoadBytes;
        long need = MeshImportOptions.EstimatedBytes(triangles);
        if (free <= 0 || need < free * 0.7) return null;
        return $"This mesh needs about {need / 1e9:0.#} GB and about {Math.Max(free, 0) / 1e9:0.#} GB is free. Turn on Reduce faces, or close other programs first.";
    }

    void StartProcessing(string key)
    {
        _procCts.Cancel(); _procCts = new CancellationTokenSource();
        var ct = _procCts.Token; var file = _file!; var opts = Options();
        _runningKey = key; _procError = null;
        var work = S.Work.Begin("Prepare mesh", _procCts.Cancel);
        Task.Run(() => MeshImporter.Process(file, opts, null, ct), ct).ContinueWith(t => Post(() =>
        {
            work.Dispose();
            if (ct.IsCancellationRequested || _runningKey != key) return;
            if (t.IsFaulted) _procError = t.Exception!.GetBaseException() is OutOfMemoryException ? OutOfMemory : t.Exception!.GetBaseException().Message;
            else if (t.IsCompletedSuccessfully) { _result = t.Result; _resultKey = key; }
            if (S.ActiveCommand != this) return;
            if (_auto && _result != null && _resultKey == key && _result.Parts.Count > 0) { S.CommitCommand(); return; }
            OnChanged();
        }), TaskScheduler.Default);
    }
}
