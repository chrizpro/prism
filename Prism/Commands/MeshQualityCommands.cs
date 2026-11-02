using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core;
using Prism.Kernel;
using Prism.Meshing;

namespace Prism.Commands;

/// <summary>Triangle quality report with a heat map and a persistent defect display (open edges, non-manifold edges, self-intersections, slivers).</summary>
public sealed class MeshQualityCommand : MeshCommand
{
    public const uint OpenColor = 0xFFFF3030, NonManifoldColor = 0xFFFF40FF, IntersectColor = 0xFFFF9A00, SliverColor = 0xFFFFE030;
    const int AutoIntersectLimit = 60000;

    readonly ChoiceParam _heat;
    readonly NumberParam _needle, _cap;
    readonly BoolParam _open, _nonManifold, _intersect, _sliver;
    string? _key; QualitySummary? _summary; TriMetrics[]? _metrics; SelfIntersectionResult? _hits;

    public MeshQualityCommand() : base("Mesh")
    {
        _heat = Choice("Heat map", 1, "Off", "Aspect ratio", "Smallest angle", "Largest angle", "Triangle size outliers");
        _needle = Num("Needle: smallest angle below", QualityDefaults.Needle, 1, 30, 1, "°");
        _cap = Num("Cap: largest angle above", QualityDefaults.Cap, 120, 179, 5, "°");
        _open = Check("Show open edges (red)", true);
        _nonManifold = Check("Show non-manifold edges (magenta)", true);
        _intersect = Check("Show self-intersecting triangles (orange)", true);
        _sliver = Check("Show sliver triangles (yellow)", true);
        PreviewButton();
        Add(new ActionParam { Label = "Clear defect display", Run = ClearDisplay });
    }

    public override string Title => "Mesh Quality";
    public override string Hint => "Measures triangle shape (aspect ratio, angles, edge lengths), finds self-intersections, and highlights defects in the viewport. The highlights stay until you press 'Clear defect display' or Clear Analysis.";
    public override string Icon => "mesh-quality";
    public override string OkText => "Done";

    void ClearDisplay()
    {
        S.ClearDefects();
        foreach (var o in Meshes()) S.ClearField(o.Id);
        Say("Defect display cleared");
    }
    void Say(string t) => S.Say(t);

    protected override CommandOutput? Build()
    {
        var o = NoOp();
        var obj = Meshes().FirstOrDefault();
        if (obj == null) { Row("Pick a mesh", "or press Cancel and import one", 2); return o; }
        var m = obj.Tri!;
        string key = Key(obj.Id, m.TriCount, m.VertexCount, _needle.Value, _cap.Value);
        if (key != _key || !ReferenceEquals(_lastMesh, m))
        {
            _summary = MeshQuality.Summarize(m, _needle.Value, _cap.Value); _metrics = MeshQuality.All(m, _needle.Value, _cap.Value); _key = key; _lastMesh = m; _hits = null;
        }
        var s = _summary!; var mt = _metrics!;

        // self-intersections: automatic for moderate meshes, on request (Preview / apply) for big ones
        bool wantHits = _intersect.Value;
        if (wantHits && _hits == null)
        {
            if (m.TriCount <= AutoIntersectLimit || Committing) _hits = MeshQuality.SelfIntersections(m);
            else { var h = Cached(Key(key, "x"), () => MeshQuality.SelfIntersections(m)); if (h != null) _hits = h; }
        }
        int nHit = _hits?.Triangles.Length ?? 0;

        Row("Triangles", Count(s.Triangles));
        Row("Good triangles", $"{Count(s.Good)}  ({Pct(s.Good, s.Triangles)})", s.Good == s.Triangles ? 1 : s.Good * 10 >= s.Triangles * 9 ? 2 : 3);
        Row("Slivers", $"{Count(s.Slivers)}  ({Count(s.Needles)} needle, {Count(s.Caps)} cap, {Count(s.Degenerate)} degenerate)", s.Slivers == 0 ? 1 : 2);
        Row("Aspect ratio", $"mean {s.AspectMean:0.##}, worst {(s.Degenerate > 0 ? "infinite" : s.AspectMax.ToString("0.##"))}  (1 = equilateral)");
        Row("Smallest angle", $"min {s.MinAngleMin:0.#}°, mean {s.MinAngleMean:0.#}°");
        Row("Largest angle", $"max {s.MaxAngleMax:0.#}°");
        Row("Edge length", $"{N(s.EdgeMin)} / {N(s.EdgeMean)} / {N(s.EdgeMax, "mm")}  (min / mean / max)");
        Row("Size outliers", $"{Count(s.SmallAreaOutliers)} tiny, {Count(s.LargeAreaOutliers)} huge  (median {N(s.AreaMedian, "mm²")})", s.SmallAreaOutliers + s.LargeAreaOutliers == 0 ? 1 : 2);
        Row("Open edges", Count(s.OpenEdges), s.OpenEdges == 0 ? 1 : 3);
        Row("Non-manifold edges", Count(s.NonManifoldEdges), s.NonManifoldEdges == 0 ? 1 : 3);
        if (wantHits) Row("Self-intersecting triangles", _hits == null ? "not computed yet (press Preview result)" : Count(nHit), _hits == null ? 2 : nHit == 0 ? 1 : 3);

        Histogram("Aspect ratio", s.AspectHistogram);
        Histogram("Smallest angle", s.MinAngleHistogram);
        Histogram("Largest angle", s.MaxAngleHistogram);

        // heat map (kept on the object until cleared)
        if (_heat.Index == 0) S.ClearField(obj.Id);
        else
        {
            var metric = (QualityMetric)(_heat.Index - 1);
            var vert = MeshQuality.ToVertexMax(m, MeshQuality.Severity(mt, metric));
            var (disp, sc) = m.ToScalarMeshData(vert); disp.Scalars = sc;
            S.SetField(obj, m, disp, 1, 0f, 1f);
            Row("Heat map", "blue = good, red = worst: " + new[] { "", "high aspect ratio", "small angle", "large angle", "size far from the median" }[_heat.Index]);
        }

        // persistent viewport highlights
        var defects = MeshQuality.Defects(m, mt, _hits?.Triangles ?? [], _open.Value, _nonManifold.Value, _sliver.Value);
        bool any = defects.OpenEdges.Count + defects.NonManifoldEdges.Count + defects.SelfIntersecting.Count + defects.Slivers.Count > 0;
        S.SetDefects(any ? ov => DrawDefects(ov, defects) : null);
        Row("Legend", "red = open edge, magenta = non-manifold, orange = self-intersection, yellow = sliver");
        return o;
    }

    TriMesh? _lastMesh;
    static string Pct(int a, int n) => n == 0 ? "-" : (100.0 * a / n).ToString("0.#") + "%";

    void Histogram(string title, IReadOnlyList<HistBucket> buckets)
    {
        int total = buckets.Sum(b => b.Count), max = Math.Max(1, buckets.Max(b => b.Count));
        foreach (var b in buckets)
            Row($"  {title} {b.Label}", $"{new string('█', (int)Math.Round(10.0 * b.Count / max))}{new string('░', 10 - (int)Math.Round(10.0 * b.Count / max))} {Count(b.Count)}  ({Pct(b.Count, total)})");
    }

    /// <summary>Draws a defect set into an overlay (used by the persistent display).</summary>
    public static void DrawDefects(Overlay o, DefectSet d)
    {
        foreach (var (a, b) in d.OpenEdges) o.AddLine(a, b, OpenColor, 4.5f);
        foreach (var (a, b) in d.NonManifoldEdges) o.AddLine(a, b, NonManifoldColor, 4.5f);
        foreach (var (a, b, c) in d.Slivers) { o.AddTri(a, b, c, SliverColor); o.AddPolyline([a, b, c], SliverColor, 2, true); }
        foreach (var (a, b, c) in d.SelfIntersecting) { o.AddTri(a, b, c, IntersectColor); o.AddPolyline([a, b, c], IntersectColor, 3, true); }
    }
}

static class QualityDefaults { public const double Needle = MeshQuality.DefaultNeedleAngle, Cap = MeshQuality.DefaultCapAngle; }
