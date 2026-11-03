using System;
using System.Linq;
using Prism.Core;
using Prism.Simulation;

namespace Prism.Commands;

/// <summary>High-cycle stress-life fatigue check on the active, solved static study.</summary>
public sealed class FatigueCommand : AnalysisCommand
{
    readonly ChoiceParam _show, _mean, _finish, _reliability;
    readonly NumberParam _ratio, _size, _cycles;

    public FatigueCommand()
    {
        _show = Choice("Result on model", 0, "Fatigue safety factor", "Fatigue life", "Damage at required life");
        _ratio = Num("Load ratio R (min / max)", -1, -1, 0.95, 0.1, "");
        _cycles = Num("Required life", 1e6, 1, 1e9, 1e5, "cycles");
        _mean = Choice("Mean-stress correction", 0, "Goodman", "Gerber", "Soderberg");
        _finish = Choice("Surface finish", 1, "Ground", "Machined", "Hot rolled", "As forged");
        _size = Num("Size factor", 0.9, 0.1, 1.2, 0.05, "");
        _reliability = Choice("Reliability", 1, Fatigue.Reliabilities.Select(r => r.Label).ToArray());
        Add(new ActionParam { Label = "Show on model", Run = Show });
    }

    public override string Title => "Fatigue";
    public override string Icon => "cae-fatigue";
    public override string Hint => "High-cycle stress-life fatigue for the active, solved static study. The static stress is the peak of a repeating load cycle (R = min / max; −1 is fully reversed). " +
        "Life and safety factor come from an S-N curve estimated from the material's ultimate strength, corrected for mean stress, surface, size and reliability. A screening estimate, not a certification: use test data for critical parts.";

    protected override void OnBegin() { S.StudyChanged += Refresh; }
    public override void OnEnd() { S.StudyChanged -= Refresh; }
    void Refresh() { if (ReferenceEquals(S.ActiveCommand, this)) OnChanged(); }

    FatigueSettings Settings() => new(_ratio.Value, (MeanStressModel)_mean.Index, (SurfaceFinish)_finish.Index, _size.Value, Fatigue.Reliabilities[_reliability.Index].Factor, _cycles.Value);

    (StudyDef study, StudyResults results, SceneObject body, Material material) Source()
    {
        var st = S.ActiveStudy ?? throw new InvalidOperationException("Create and run a static study first.");
        var r = st.Results ?? throw new InvalidOperationException("Run the static study first: fatigue reads its stress result.");
        if (st.OtherBodies.Count > 0) throw new InvalidOperationException("Fatigue works on a one-body study for now: each material has its own curve.");
        var body = S.Find(st.BodyId) ?? throw new InvalidOperationException("The study's body no longer exists.");
        StudySetup.Validate(body, st.ShapeSignature, []);
        var material = body.Material ?? throw new InvalidOperationException("Assign a material before running fatigue.");
        if (material.Name != r.MaterialName) throw new InvalidOperationException($"The study was solved with {r.MaterialName}, but the body is now {material.Name}. Run the static study again.");
        return (st, r, body, material);
    }

    protected override void Analyze()
    {
        try
        {
            var (_, r, _, material) = Source();
            var s = Settings();
            var f = Fatigue.Analyze(r, material, s);
            var c = f.Curve;
            Row("Material", $"{material.Name}: ultimate {c.Sut:0.#} MPa" + (double.IsFinite(c.Sy) ? $", yield {c.Sy:0.#} MPa" : ", no yield point"));
            Row(c.HasKnee ? "Endurance limit" : "Strength at 5·10⁸ cycles", $"{c.Se:0.#} MPa (uncorrected {c.SeUncorrected:0.#} MPa)");
            Row("Load cycle", s.LoadRatio <= -1 ? "Fully reversed (mean stress 0)" : $"R = {s.LoadRatio:0.###}: peak stress is {(1 - s.LoadRatio) / 2 * 100:0.#}% alternating, {(1 + s.LoadRatio) / 2 * 100:0.#}% mean");
            Row("Peak stress", $"{r.MaxVonMises:0.##} MPa at node {r.MaxVonMisesNode + 1:N0}");
            string life = f.MinLife >= Fatigue.LifeCap ? $"Run-out: beyond {Fatigue.LifeCap:0.##e0} cycles" : $"{f.MinLife:0.###e0} cycles";
            bool meets = f.MinLife >= s.RequiredCycles && f.MinSafety >= 1;
            Row("Shortest life", $"{life} at node {f.MinLifeNode + 1:N0}", meets ? 0 : 3);
            Row("Fatigue safety factor", $"{f.MinSafety:0.##} at {s.RequiredCycles:0.##e0} cycles (node {f.MinSafetyNode + 1:N0})", f.MinSafety >= 1 ? 0 : 3);
            Row("Verdict", meets ? $"Meets the {s.RequiredCycles:0.##e0}-cycle requirement." : $"Does not reach {s.RequiredCycles:0.##e0} cycles.", meets ? 0 : 3);
            foreach (var n in f.Notes) Row("Note", n, 1);
        }
        catch (InvalidOperationException e) { Row("Setup", e.Message, 3); }
    }

    void Show()
    {
        try
        {
            var (_, r, body, material) = Source();
            var f = Fatigue.Analyze(r, material, Settings());
            double[] field; string title, unit; double lo, hi;
            switch (_show.Index)
            {
                case 0:
                    field = f.Safety; title = "Fatigue safety factor"; unit = "";
                    lo = 5; hi = Math.Min(f.MinSafety, 4.99);
                    break;
                case 1:
                    field = f.Life.Select(x => Math.Log10(x)).ToArray(); title = "Fatigue life"; unit = "log10 cycles";
                    lo = Math.Log10(Fatigue.LifeCap); hi = Math.Min(Math.Log10(f.MinLife), lo - 0.01);
                    break;
                default:
                    field = f.Damage.Select(d => Math.Log10(Math.Max(d, 1e-12))).ToArray(); title = "Damage at required life"; unit = "log10 (1 = failure)";
                    lo = -3; hi = Math.Max(f.Damage.Max() > 0 ? Math.Log10(f.Damage.Max()) : -3, -2.99);
                    break;
            }
            double size = body.Mesh.Max.DistanceTo(body.Mesh.Min);
            S.ShowResult(ResultView.Build(r, body.Id, ResultKind.Scalar, 0, size, field, title, unit, lo, hi));
            S.Say($"Fatigue: shortest life {(f.MinLife >= Fatigue.LifeCap ? "beyond 1e9" : f.MinLife.ToString("0.###e0"))} cycles, safety factor {f.MinSafety:0.##}.");
        }
        catch (InvalidOperationException e) { S.Say(e.Message, true); }
    }
}
