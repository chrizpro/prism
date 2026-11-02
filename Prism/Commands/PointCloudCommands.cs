using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Prism.Core;
using Prism.Kernel;
using Prism.Meshing;

namespace Prism.Commands;

/// <summary>Holds the most recently imported point cloud so the Reconstruct Surface command can work on it.</summary>
public static class PointCloudStore
{
    public static PointCloud? Current { get; private set; }
    public static string Name { get; private set; } = "Scan";
    public static void Set(PointCloud cloud, string name) { Current = cloud; Name = name; }
}

/// <summary>Entry points for getting a point cloud into the session. A cloud is shown as points while Reconstruct Surface is open and ends up as a mesh object.</summary>
public static class PointCloudCommands
{
    public static readonly string[] ImportPatterns = ["*.xyz", "*.pts", "*.xyzn", "*.csv", "*.txt", "*.ply"];

    /// <summary>True when the file is a point cloud (XYZ/PTS/CSV text, or a PLY without faces).</summary>
    public static bool IsPointCloudFile(string path) =>
        PointCloud.IsPointCloudExtension(path) || (Path.GetExtension(path).Equals(".ply", StringComparison.OrdinalIgnoreCase) && PointCloud.PlyHasNoFaces(path));

    /// <summary>If the file is a point cloud, load it and open Reconstruct Surface. Returns false (doing nothing) for mesh files.</summary>
    public static bool TryBegin(CadSession s, string path)
    {
        if (!IsPointCloudFile(path)) return false;
        Begin(s, PointCloud.Load(path), path);
        return true;
    }

    public static void Begin(CadSession s, PointCloud cloud, string path)
    {
        string name = Path.GetFileNameWithoutExtension(path) is { Length: > 0 } n ? n : "Scan";
        PointCloudStore.Set(cloud, name);
        s.Say($"Loaded {cloud.Count:#,0} points from {Path.GetFileName(path)}" + (cloud.HasNormals ? " with normals" : "") + ". Set up the surface and press Reconstruct.");
        s.StartCommand(() => new ReconstructSurfaceCommand());
    }

    /// <summary>Same as <see cref="TryBegin"/> but reads the file on a worker thread.</summary>
    public static async Task<bool> TryBeginAsync(CadSession s, string path)
    {
        if (!IsPointCloudFile(path)) return false;
        s.Say($"Reading {Path.GetFileName(path)}…");
        var cloud = await Task.Run(() => PointCloud.Load(path));
        Begin(s, cloud, path);
        return true;
    }
}

/// <summary>Point cloud to mesh: clean up, estimate and orient normals, then build a surface. The heavy work runs on a worker thread from the Preview button.</summary>
public sealed class ReconstructSurfaceCommand : Command
{
    readonly PointCloud? _cloud;
    readonly string _name;
    readonly BoolParam _outliers, _useNormals;
    readonly NumberParam _downsample, _smooth, _trim;
    readonly IntParam _neighbours, _depth, _holes;
    readonly ChoiceParam _hint;
    string? _key; TriMesh? _result; string _status = "";
    CancellationTokenSource? _cts; bool _force;

    public ReconstructSurfaceCommand()
    {
        _cloud = PointCloudStore.Current; _name = PointCloudStore.Name;
        double diag = 0;
        if (_cloud != null) { var (a, b) = _cloud.Bounds(); diag = (b - a).Length; }
        double auto = _cloud != null && _cloud.Count > 250_000 ? Math.Round(diag / 700, 3) : 0;
        _outliers = Check("Remove stray points", false);
        _downsample = Num("Downsample (cell size, 0 = off)", auto, 0, 1e6, 0.1);
        _useNormals = Check("Use the normals from the file", _cloud?.HasNormals == true);
        _neighbours = Add(new IntParam { Label = "Normal neighbours", Value = 16, Min = 4, Max = 64 });
        _hint = Choice("Normals face", 0, "Outward from the middle", "Up (scanned from above)", "Down (scanned from below)", "Inward (scanned from inside)");
        _depth = Add(new IntParam { Label = "Detail (grid of 2^n cells)", Value = 7, Min = 4, Max = 9 });
        _smooth = Num("Smoothing", 1, 0.3, 4, 0.1, "×");
        _trim = Num("Trim thin areas", 10, 0, 90, 5, "%");
        _holes = Add(new IntParam { Label = "Close holes up to (edges)", Value = 50, Min = 0, Max = 5000 });
        Add(new ActionParam { Label = "Preview result", Run = StartPreview });
    }

    public override string Title => "Reconstruct Surface";
    public override string Hint => "Turns a point cloud (XYZ, PTS, PLY...) into a triangle mesh. Stray points can be removed and big scans thinned first; normals are estimated and oriented automatically; Detail sets the mesh resolution and Trim removes surface where there is little data.";
    public override string Icon => "mesh-reconstruct";
    public override string OkText => "Reconstruct";
    public override bool CanEdit => false;
    protected override void UpdateVisibility() { _useNormals.IsVisible = _cloud?.HasNormals == true; _neighbours.IsVisible = !(_useNormals.IsVisible && _useNormals.Value); _hint.IsVisible = _neighbours.IsVisible; }

    string KeyNow() => string.Join("|", _outliers.Value, _downsample.Value.ToString("R"), _useNormals.Value, _neighbours.Value, _hint.Index, _depth.Value, _smooth.Value.ToString("R"), _trim.Value.ToString("R"), _holes.Value);

    Func<CancellationToken, Action<string>?, TriMesh> Job()
    {
        var cloud = _cloud!; bool outl = _outliers.Value, useN = _useNormals.Value; double ds = _downsample.Value; int k = _neighbours.Value, hint = _hint.Index;
        var o = new ReconstructOptions { Depth = _depth.Value, Smoothing = _smooth.Value, Trim = _trim.Value / 100, FillHoleEdges = _holes.Value };
        return (ct, progress) =>
        {
            var (min, max) = cloud.Bounds(); var c = (min + max) * 0.5; double far = (max - min).Length * 20 + 1;
            Vec3? view = hint switch { 1 => new Vec3(c.X, c.Y, c.Z + far), 2 => new Vec3(c.X, c.Y, c.Z - far), 3 => c, _ => null };
            return SurfaceReconstruction.FromCloud(cloud, ds, outl, k, useN, view, o, progress, ct);
        };
    }

    void StartPreview()
    {
        if (_cloud == null) return;
        var key = KeyNow();
        if (key == _key && _result != null) return;
        _cts?.Cancel(); var cts = _cts = new CancellationTokenSource();
        var job = Job(); _force = true;
        _status = "Working…"; OnChanged();
        Task.Run(() =>
        {
            try
            {
                var m = job(cts.Token, msg => Dispatcher.UIThread.Post(() => { if (!cts.IsCancellationRequested) { _status = msg + "…"; OnChanged(); } }));
                Dispatcher.UIThread.Post(() => { if (cts.IsCancellationRequested) return; _key = key; _result = m; _status = ""; OnChanged(); });
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { Dispatcher.UIThread.Post(() => { if (!cts.IsCancellationRequested) { _status = "Failed: " + e.Message; OnChanged(); } }); }
        });
    }

    public override void BuildOverlay(CadSession s, Overlay o)
    {
        if (_cloud == null) return;
        int step = Math.Max(1, _cloud.Count / 20000);
        for (int i = 0; i < _cloud.Count; i += step)
        {
            uint col = _cloud.C is { } c ? 0xFF000000u | (uint)c[3 * i] << 16 | (uint)c[3 * i + 1] << 8 | c[3 * i + 2] : 0xFF35B6FF;
            o.AddDot(_cloud.Point(i), col, 3);
        }
    }

    protected override CommandOutput? Build()
    {
        Results.Clear();
        if (_cloud == null) { Results.Add(new ResultLine("No point cloud", "Use Mesh > Import Point Cloud first", 2)); return null; }
        Results.Add(new ResultLine("Points", _cloud.Count.ToString("#,0"), 0));
        Results.Add(new ResultLine("File data", (_cloud.HasNormals ? "normals " : "") + (_cloud.HasColors ? "colours" : _cloud.HasNormals ? "" : "positions only"), 0));
        var key = KeyNow();
        if (_result == null || _key != key)
        {
            if (Committing)
            {
                _cts?.Cancel();
                _result = Job()(CancellationToken.None, null); _key = key;
            }
            else
            {
                if (_status.Length > 0) Results.Add(new ResultLine("Status", _status, _status.StartsWith("Failed") ? 3 : 2));
                else Results.Add(new ResultLine(_result == null ? "Not computed yet" : "Settings changed", "Press 'Preview result', or apply directly", 2));
                return null;
            }
        }
        var rep = MeshAnalysis.Report(_result);
        Results.Add(new ResultLine("Triangles", rep.Triangles.ToString("#,0"), 1));
        Results.Add(new ResultLine("Watertight", rep.Closed ? "Yes" : $"No ({rep.HoleLoops} open boundaries)", rep.Closed ? 1 : 2));
        var o = new CommandOutput { Label = "Reconstruct surface", Icon = Icon };
        o.Add.Add(new NewObject(null, _name, ObjKind.Mesh, 0xFFC8D0DA, null, null, null, _result));
        return o;
    }
}
