using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core;
using Prism.Kernel;
using Prism.Meshing;

namespace Prism.Commands;

public sealed class ConvertMeshCommand : MeshCommand
{
    enum OutputKind { Solid, Surface, Freeform, Nurbs }

    readonly ChoiceParam _output, _source, _solidFit, _surfaceFit, _cage;
    readonly NumberParam _tolerance, _sewing, _offset;
    readonly IntParam _minimumRegion, _resolution, _relax;
    readonly BoolParam _extended, _keepOpen;
    TriMesh? _sourceMesh;
    MeshSelection? _sourceRegion;
    int _sourceRevision;
    Conversion? _preview;

    sealed record Conversion(CommandOutput? Output, IReadOnlyList<ResultLine> Rows,
        List<(Vec3 A, Vec3 B)> OpenEdges, List<Vec3> ScanOutliers, List<Vec3> ResultOutliers, bool OpenFallback, bool Invalid);

    public ConvertMeshCommand()
    {
        _output = Choice("Output type", 0, "Solid", "Surface", "Freeform", "NURBS");
        _source = Choice("Source", 0, "Entire mesh", "Selected region");
        _solidFit = Choice("Primitive", 0, "Automatic", "Cylinder", "Sphere", "Cone");
        _surfaceFit = Choice("Face type", 0, "Automatic", "Plane", "Cylinder", "Sphere", "Cone");
        _tolerance = Num("Fit tolerance", 0.3, 0.001, 50, 0.05, "mm");
        _minimumRegion = Add(new IntParam { Label = "Smallest region (triangles)", Value = 8, Min = 2, Max = 100000, IsAdvanced = true });
        _extended = Add(new BoolParam { Label = "Fit additional surface types", Value = true, IsAdvanced = true });
        _sewing = Add(new NumberParam { Label = "Sewing tolerance", Value = 0.2, Min = 0.0001, Max = 10, Step = 0.05, IsAdvanced = true });
        _keepOpen = Check("Keep open result as surface", false);
        _cage = Choice("Start from", 1, "Box", "Ball (quadsphere)", "Cylinder");
        _resolution = Add(new IntParam { Label = "Cage resolution (quads per side)", Value = 8, Min = 2, Max = 30 });
        _relax = Add(new IntParam { Label = "Smooth the cage (passes)", Value = 6, Min = 0, Max = 60 });
        _offset = Num("Float above the scan", 0, -20, 20, 0.2, "mm");
        _output.Section = _solidFit.Section = _surfaceFit.Section = "Output";
        Src.Section = _source.Section = "Source";
        _tolerance.Section = _keepOpen.Section = PreviewButton().Section = "Fit preview";
    }

    protected override IReadOnlyList<string> WorkflowSectionOrder => ["Output", "Source", "Settings", "Fit preview", "Advanced"];

    OutputKind Output => (OutputKind)_output.Index;
    bool UsesRegion => Output == OutputKind.Nurbs || Output != OutputKind.Freeform && _source.Index == 1;
    public override string Title => "Convert Mesh";
    public override string Icon => "mesh-fit";
    public override string OkText => "Convert";
    public override string Hint => "Choose what to create, then preview the fit. Use Edit Region to fit a selected area.";
    public override bool CanEdit => !UsesRegion;

    protected override void UpdateVisibility()
    {
        _source.IsVisible = Output is OutputKind.Solid or OutputKind.Surface;
        Src.IsVisible = !UsesRegion;
        _solidFit.IsVisible = UsesRegion && Output == OutputKind.Solid;
        _surfaceFit.IsVisible = UsesRegion && Output == OutputKind.Surface;
        _minimumRegion.IsVisible = _extended.IsVisible = !UsesRegion && (Output is OutputKind.Solid or OutputKind.Surface);
        _sewing.IsVisible = _keepOpen.IsVisible = Output == OutputKind.Solid && !UsesRegion;
        _cage.IsVisible = _resolution.IsVisible = _relax.IsVisible = _offset.IsVisible = Output == OutputKind.Freeform;
    }

    protected override CommandOutput? Build()
    {
        Results.Clear();
        _preview = null;
        var region = UsesRegion ? S.RegionSel : null;
        var obj = UsesRegion ? region == null ? null : S.Find(region.ObjectId) : Meshes().FirstOrDefault();
        var mesh = obj?.Tri;
        if (UsesRegion && (region == null || region.IsEmpty || !ReferenceEquals(region.Mesh, mesh)))
        {
            Row("Select a region first", "Use Edit Region to select triangles on the scan.", 2);
            return null;
        }
        if (mesh == null || obj == null) return null;

        if (!ReferenceEquals(_sourceMesh, mesh) || !ReferenceEquals(_sourceRegion, region))
        {
            _sourceMesh = mesh;
            _sourceRegion = region;
            _sourceRevision++;
        }
        var key = Key(obj.Id, _sourceRevision, region?.Version ?? 0, _output.Index, _solidFit.Index, _surfaceFit.Index,
            _tolerance.Value, _minimumRegion.Value, _extended.Value, _sewing.Value, _cage.Index,
            _resolution.Value, _relax.Value, _offset.Value);
        var conversion = Cached(key, () => Fit(obj, mesh, region));
        if (conversion == null) return null;
        _preview = conversion;
        foreach (var row in conversion.Rows) Results.Add(row);
        if (conversion.Invalid)
        {
            Row("Invalid solid", "Try Surface or fit smaller regions.", 3);
            if (Committing) return null;
        }
        if (conversion.OpenFallback)
        {
            Row("Solid could not close", "Enable Keep open result as surface to accept the preview.", 2);
            if (Committing && !_keepOpen.Value) return null;
        }
        return conversion.Output;
    }

    Conversion Fit(SceneObject obj, TriMesh mesh, MeshSelection? region)
    {
        MeshCommand fitter;
        CommandOutput? output;
        if (region != null)
        {
            var local = new MeshFitSurfaceCommand();
            int type = Output switch
            {
                OutputKind.Nurbs => 5,
                OutputKind.Solid => _solidFit.Index switch { 1 => 2, 2 => 3, 3 => 4, _ => 0 },
                _ => _surfaceFit.Index,
            };
            fitter = local;
            output = local.BuildConversion(obj, region, type, _tolerance.Value,
                surfaceOnly: Output == OutputKind.Surface, primitiveOnly: Output == OutputKind.Solid);
        }
        else if (Output == OutputKind.Freeform)
        {
            var form = new FormFromScanCommand();
            fitter = form;
            output = form.BuildConversion(obj, _cage.Index, _resolution.Value, _relax.Value, _offset.Value);
        }
        else
        {
            var rebuild = new MeshRebuildCommand();
            fitter = rebuild;
            output = rebuild.BuildConversion(obj, Output == OutputKind.Solid, _tolerance.Value,
                _sewing.Value, _minimumRegion.Value, _extended.Value);
        }

        var rows = fitter.Results.Where(r => r.Label != "Output").ToList();
        var openEdges = new List<(Vec3, Vec3)>();
        var scanOutliers = new List<Vec3>();
        var resultOutliers = new List<Vec3>();
        if (output == null || output.Add.Count == 0)
        {
            rows.Insert(0, new("Output", "No geometry could be fitted.", 2));
            return new(null, rows, openEdges, scanOutliers, resultOutliers, false, false);
        }

        output.Label = Title;
        output.Icon = Icon;
        var reference = region?.Extract() ?? mesh;
        var parts = new List<TriMesh>();
        int freeEdges = 0;
        bool valid = true;
        foreach (var part in output.Add)
        {
            if (part.Form is { } form)
                parts.Add(MeshAnalysis.FromMeshData(form.BuildMesh()));
            else if (part.Shape is { } shape)
            {
                var geometry = shape.Tessellate(Math.Max(reference.Size.Length * 0.0002, 0.0001));
                parts.Add(MeshAnalysis.FromMeshData(geometry));
                var check = Occ.Check(shape);
                valid &= check.Valid && (part.Kind != ObjKind.Solid || check is { Solids: > 0, FreeEdges: 0 });
                freeEdges += check.FreeEdges;
                if (check.FreeEdges > 0) AddEdges(geometry, Occ.FreeEdges(shape), openEdges);
            }
        }
        var result = TriMesh.Merge(parts);
        if (result.TriCount > 0)
        {
            Compare("Scan to result", reference, result, rows, scanOutliers);
            Compare("Result to scan", result, reference, rows, resultOutliers);
        }

        bool openFallback = Output == OutputKind.Solid && output.Add.All(p => p.Kind == ObjKind.Surface);
        string description = Output switch
        {
            OutputKind.Solid when openFallback => "Open CAD surface",
            OutputKind.Solid => "Direct-edit solid",
            OutputKind.Freeform => "Editable freeform cage",
            OutputKind.Nurbs => "NURBS patch",
            _ => "Fitted CAD surface",
        };
        rows.Insert(0, new("Output", description, openFallback ? 2 : 1));
        if (Output != OutputKind.Freeform)
            rows.Insert(1, new("Open edges", Count(freeEdges), freeEdges > 0 && Output == OutputKind.Solid ? 2 : 0));
        return new(output, rows, openEdges, scanOutliers, resultOutliers, openFallback, Output == OutputKind.Solid && !openFallback && !valid);
    }

    void Compare(string label, TriMesh source, TriMesh target, List<ResultLine> rows, List<Vec3> outliers)
    {
        // Triangle interior samples reveal errors on large faces even when their vertices happen to fit.
        var points = new List<float>();
        void Sample(Vec3 p) { points.Add((float)p.X); points.Add((float)p.Y); points.Add((float)p.Z); }
        int vertexStep = Math.Max(1, (source.VertexCount + 19999) / 20000);
        int triangleStep = Math.Max(1, (source.TriCount + 19999) / 20000);
        for (int i = 0; i < source.VertexCount; i += vertexStep) Sample(source.P(i));
        for (int i = 0; i < source.TriCount; i += triangleStep)
            Sample((source.P(source.T[3 * i]) + source.P(source.T[3 * i + 1]) + source.P(source.T[3 * i + 2])) / 3);
        var samples = new TriMesh([.. points], []);
        var deviation = MeshAnalysis.Deviation(samples, target, _tolerance.Value);
        rows.Add(new(label, $"RMS {N(deviation.Rms, "mm")}, max {N(deviation.AbsMax, "mm")}, {deviation.WithinTolerance:0.#}% within tolerance",
            deviation.WithinTolerance >= 99 ? 1 : 2));
        var outside = Enumerable.Range(0, deviation.Count).Where(i => Math.Abs(deviation.PerVertex[i]) > _tolerance.Value).ToArray();
        int step = Math.Max(1, (outside.Length + 499) / 500);
        for (int i = 0; i < outside.Length; i += step) outliers.Add(samples.P(outside[i]));
    }

    static void AddEdges(MeshData mesh, int[] edges, List<(Vec3, Vec3)> lines)
    {
        foreach (int edge in edges)
        {
            if (edge + 1 >= mesh.EdgeSegStart.Length) continue;
            for (int s = mesh.EdgeSegStart[edge]; s < mesh.EdgeSegStart[edge + 1]; s++)
            {
                var p = mesh.EdgePoints;
                int i = 6 * s;
                lines.Add((new(p[i], p[i + 1], p[i + 2]), new(p[i + 3], p[i + 4], p[i + 5])));
            }
        }
    }

    public override void BuildOverlay(CadSession session, Overlay overlay)
    {
        if (_preview is not { } preview || !PreviewEnabled) return;
        foreach (var (a, b) in preview.OpenEdges) overlay.AddLine(a, b, 0xFFFF4D4D, 4.5f);
        foreach (var p in preview.ScanOutliers) overlay.AddDot(p, 0xFFFFBD55, 4);
        foreach (var p in preview.ResultOutliers) overlay.AddDot(p, 0xFFFF4D4D, 4);
    }
}
