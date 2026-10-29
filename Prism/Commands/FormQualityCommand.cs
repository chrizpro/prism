using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core;
using Prism.Core.Form;
using Prism.Kernel;
using Prism.Meshing;

namespace Prism.Commands;

/// <summary>
/// Surface quality of a Freeform: where the cage has poles (points that do not have four neighbours), triangles and n-gons,
/// how stretched the quads are, how the curvature of the smooth surface varies, and how far the surface is from a reference
/// (a scan, a CAD body or another form), shown as a colour map.
/// </summary>
public sealed class FormQualityCommand : Command
{
    readonly SelectionSlot _src, _ref;
    readonly NumberParam _tol, _range;
    readonly BoolParam _poles, _compare;
    Vec3? _worst; double _worstValue;
    FormQuality? _quality;

    public FormQualityCommand()
    {
        _src = Slot(new SelectionSlot { Label = "Freeform to check", Filter = SelFilter.Form });
        _ref = Slot(new SelectionSlot { Label = "Compare with (scan, CAD body or form)", Filter = SelFilter.Solid | SelFilter.SurfaceObj | SelFilter.MeshObj | SelFilter.Form, Optional = true });
        _poles = Check("Mark poles, triangles and n-gons", true);
        _compare = Check("Color the surface by distance to the reference", true);
        _tol = Num("Tolerance", 0.2, 0.0001, 100, 0.05);
        _range = Num("Color range (±)", 1.0, 0.001, 100, 0.1);
    }

    void Row(string label, string value, int level = 0) => Results.Add(new ResultLine(label, value, level));

    public override string Title => "Freeform Quality";
    public override string Icon => "form-edit";
    public override string OkText => "Done";
    public override string Hint => "Check a freeform's cage and surface. Yellow dots are poles with three edges, red dots have five or more, blue squares are n-gons, green squares are triangles. With a reference, the surface is colored by distance: blue = below it, green = on it, red = above it.";

    protected override void OnBegin()
    {
        if (_src.Items.Count == 0)
        {
            var forms = S.Objects.Where(o => o.Form != null && o.Visible).ToList();
            if (forms.Count == 1) _src.Items.Add(SelItem.Of(forms[0]));
        }
    }

    public override void OnCancel() { foreach (var o in ObjectsOf(_src)) S.ClearField(o.Id); }

    SceneObject? Form => ObjectsOf(_src).FirstOrDefault(o => o.Form != null);

    protected override CommandOutput? Build()
    {
        Results.Clear(); _worst = null; _quality = null;
        var result = new CommandOutput { NoOp = true };
        var obj = Form; if (obj?.Form is not { } cage) return result;
        foreach (var o in ObjectsOf(_src)) S.ClearField(o.Id);

        var q = _quality = FormQuality.Analyze(cage);
        Row("Cage", $"{q.Vertices:#,0} points, {q.Faces:#,0} faces ({q.Quads:#,0} quads, {q.Triangles} triangles, {q.NGons} n-gons)", q.Triangles + q.NGons == 0 ? 1 : 2);
        Row("Poles", $"{q.Poles3} with three edges, {q.Poles5OrMore} with five or more" + (q.BoundaryVertices > 0 ? $" ({q.BoundaryVertices} points on open borders)" : ""), q.ExtraordinaryPoints <= 8 ? 1 : q.ExtraordinaryPoints <= 24 ? 2 : 3);
        Row("Edge lengths", $"{q.ShortestEdge:0.##} to {q.LongestEdge:0.##} mm (mean {q.MeanEdge:0.##})");
        Row("Stretched quads", $"{q.StretchedFaces} faces are more than 3x longer than wide (worst {q.WorstAspect:0.#}x)", q.StretchedFaces == 0 ? 1 : q.StretchedFaces < q.Faces / 10 ? 2 : 3);
        Row("Curvature", $"mean {q.MeanCurvature:0.####} /mm, highest {q.MaxCurvature:0.###} /mm (radius {(q.MaxCurvature > 1e-9 ? 1 / q.MaxCurvature : double.PositiveInfinity):0.##} mm)");
        Row("Curvature spread", $"{q.CurvatureSpread:0.#}x between typical and tight areas", q.CurvatureSpread < 6 ? 1 : q.CurvatureSpread < 20 ? 2 : 3);
        _worst = q.MaxCurvature > 0 ? q.CurvatureHotSpot : null; _worstValue = q.MaxCurvature;

        if (_compare.Value && _ref.Items.Count > 0 && S.Find(_ref.Items[0].Obj) is { } reference && reference.Id != obj.Id)
        {
            var surface = MeshAnalysis.FromMeshData(cage.BuildMesh(3));
            var referenceMesh = S.PrintMesh(reference);
            if (referenceMesh == null) Row("Reference", "has no surface to compare with", 2);
            else
            {
                var dev = MeshAnalysis.Deviation(surface, referenceMesh, _tol.Value);
                var (disp, scalars) = surface.ToScalarMeshData(dev.PerVertex); disp.Scalars = scalars;
                S.SetField(obj, surface, disp, 1, (float)-_range.Value, (float)_range.Value);
                Row("Points within tolerance", $"{dev.WithinTolerance:0.#}%", dev.WithinTolerance > 95 ? 1 : dev.WithinTolerance > 80 ? 2 : 3);
                Row("RMS deviation", $"{dev.Rms:0.###} mm");
                Row("Bias", $"{dev.Mean:0.###} mm");
                Row("Largest gap", $"{dev.Max:+0.###} mm above, {dev.Min:0.###} mm below");
                int wi = 0; float wv = 0; for (int i = 0; i < dev.PerVertex.Length; i++) if (Math.Abs(dev.PerVertex[i]) > Math.Abs(wv)) { wv = dev.PerVertex[i]; wi = i; }
                _worst = surface.P(wi); _worstValue = wv;
            }
        }
        return result;
    }

    public override void BuildOverlay(CadSession s, Overlay o)
    {
        if (Form?.Form is { } cage && _poles.Value && _quality is { } q) FormPoleOverlay.Draw(o, cage, q);
        if (_worst is { } p) { o.AddDot(p, 0xFFFFFFFF, 12); o.AddLabel(p, _compare.Value && _ref.Items.Count > 0 ? $"{_worstValue:+0.###;-0.###} mm" : $"tightest {_worstValue:0.###}/mm", true, 1); }
    }
}

/// <summary>Markers for the places where a cage's flow breaks: poles, triangles and n-gons.</summary>
public static class FormPoleOverlay
{
    public static void Draw(Overlay o, SubdMesh cage, FormQuality q)
    {
        foreach (var (v, valence) in q.Poles) o.AddDot(cage.V[v], valence == 3 ? 0xFFFFD23F : 0xFFFF5A5F, 11);
        foreach (int f in q.NGonFaces) o.AddDot(cage.FaceCenter(f), 0xFF4C94FF, 11);
        foreach (int f in q.TriangleFaces) o.AddDot(cage.FaceCenter(f), 0xFF3DD68C, 11);
    }
}
