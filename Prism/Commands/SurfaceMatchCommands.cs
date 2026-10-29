using System;
using Prism.Core;
using Prism.Kernel;

namespace Prism.Commands;

/// <summary>Exact boundary derivative matching for compatible non-rational rectangular patches.</summary>
public sealed class MatchSurfaceCommand : Command
{
    readonly SelectionSlot _source, _reference;
    readonly ChoiceParam _side, _refSide, _continuity;
    readonly BoolParam _reverse;
    readonly NumberParam _scale;
    public MatchSurfaceCommand()
    {
        _source = Slot(new SelectionSlot { Label = "Surface to change", Filter = SelFilter.SurfaceObj });
        _reference = Slot(new SelectionSlot { Label = "Reference surface or face", Filter = SelFilter.SurfaceObj | SelFilter.Face });
        _side = Choice("Source boundary", 0, "U min", "U max", "V min", "V max");
        _refSide = Choice("Reference boundary", 1, "U min", "U max", "V min", "V max");
        _continuity = Choice("Continuity", 1, "Position (G0)", "Tangent (G1)", "Curvature (G2)");
        _reverse = Check("Reverse along reference boundary", false);
        _scale = Num("Tangent scale", 1, .01, 100, .1, "×");
        _scale.HelpText = "Controls how quickly the changed patch leaves the seam. The reference stays fixed.";
    }
    public override string Title => "Match Surface";
    public override string OkText => "Match";
    public override string Icon => "nurbs-patch";
    public override string Hint => "Match one natural patch boundary to a fixed reference. Compatible untrimmed, non-rational patches only. G1 changes two control rows; G2 changes three. Check the preview for folds before accepting.";
    protected override CommandOutput? Build()
    {
        Results.Clear();
        if (!Complete) return null;
        var source = S.Find(_source.Items[0].Obj)!;
        var reference = S.ShapeOf(_reference.Items[0])!;
        if (source.Id == _reference.Items[0].Obj) throw new FormatException("Choose a different reference surface.");
        var result = Occ.MatchSurface(source.Shape, _side.Index, reference, _refSide.Index, _continuity.Index, _reverse.Value, _scale.Value);
        var report = Occ.SurfaceContinuity(result, _side.Index, reference, _refSide.Index, _reverse.Value);
        Results.Add(new ResultLine("Sampled gap", $"{report.Gap:G4} mm"));
        Results.Add(new ResultLine("Sampled normal angle", $"{report.NormalAngle:G4}°"));
        Results.Add(new ResultLine("Cross-boundary curvature difference", $"{report.CurvatureDifference:G4} /mm"));
        var output = new CommandOutput { Label = "Match surface", Icon = Icon };
        output.Replace.Add((source, result)); return output;
    }
}

public sealed class SurfaceContinuityCommand : AnalysisCommand
{
    readonly SelectionSlot _source, _reference;
    readonly ChoiceParam _side, _refSide;
    readonly BoolParam _reverse;
    readonly IntParam _samples;
    public SurfaceContinuityCommand()
    {
        _source = Slot(new SelectionSlot { Label = "First surface or face", Filter = SelFilter.SurfaceObj | SelFilter.Face });
        _reference = Slot(new SelectionSlot { Label = "Second surface or face", Filter = SelFilter.SurfaceObj | SelFilter.Face });
        _side = Choice("First boundary", 0, "U min", "U max", "V min", "V max");
        _refSide = Choice("Second boundary", 1, "U min", "U max", "V min", "V max");
        _reverse = Check("Reverse along second boundary", false);
        _samples = Add(new IntParam { Label = "Seam samples", Min = 3, Max = 1001, Value = 51 });
    }
    public override string Title => "Surface Continuity";
    public override string Icon => "curvature";
    public override string Hint => "Compare corresponding natural patch boundaries. Reports sampled gaps, normal angles and cross-boundary normal curvature, not a global G2 certificate or self-intersection test.";
    protected override void Analyze()
    {
        if (!Complete) return;
        var r = Occ.SurfaceContinuity(S.ShapeOf(_source.Items[0])!, _side.Index, S.ShapeOf(_reference.Items[0])!, _refSide.Index, _reverse.Value, _samples.Value);
        Row("Maximum gap", $"{r.Gap:G6} mm"); Row("RMS gap", $"{r.RmsGap:G6} mm");
        Row("Maximum normal angle", $"{r.NormalAngle:G6}°");
        Row("Cross-boundary curvature difference", $"{r.CurvatureDifference:G6} /mm");
        Row("Samples", _samples.Value.ToString());
    }
}
