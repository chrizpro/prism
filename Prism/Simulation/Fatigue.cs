using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core;

namespace Prism.Simulation;

public enum MeanStressModel { Goodman, Gerber, Soderberg }
public enum SurfaceFinish { Ground, Machined, HotRolled, AsForged }

/// <summary>Constant-amplitude loading and the Marin-style modifiers that turn handbook strength into a part endurance limit.</summary>
/// <param name="LoadRatio">R = minimum load / maximum load of the cycle: −1 fully reversed, 0 pulsating from zero, up to 0.95 for a small ripple on a large preload.</param>
/// <param name="SizeFactor">Size modifier kb (1 for small sections, about 0.85–0.9 for 25–50 mm sections).</param>
/// <param name="ReliabilityFactor">Reliability modifier kc, see <see cref="Fatigue.Reliabilities"/>.</param>
/// <param name="RequiredCycles">Life the design must reach, in load cycles.</param>
public sealed record FatigueSettings(double LoadRatio = -1, MeanStressModel Mean = MeanStressModel.Goodman, SurfaceFinish Finish = SurfaceFinish.Machined,
    double SizeFactor = 0.9, double ReliabilityFactor = 0.897, double RequiredCycles = 1e6);

/// <summary>A Basquin stress-life curve σ = A·N^B for fully reversed stress amplitude, valid from 10³ cycles to the knee.</summary>
/// <param name="Se">Corrected endurance (or 5·10⁸-cycle fatigue) strength, MPa.</param>
/// <param name="HasKnee">True for steels: stress amplitudes at or below Se never fail. Non-ferrous metals keep losing strength, so no run-out limit exists.</param>
public sealed record FatigueCurve(double Sut, double Sy, double Se, double SeUncorrected, double F, double A, double B, double KneeCycles, bool HasKnee, string Basis);

public sealed record FatigueResult(FatigueCurve Curve, FatigueSettings Settings, double[] Life, double[] Damage, double[] Safety,
    double MinLife, int MinLifeNode, double MinSafety, int MinSafetyNode, List<string> Notes);

/// <summary>
/// High-cycle stress-life (S-N) fatigue for a solved static study. The static von Mises stress is taken as the peak of a
/// constant-amplitude load cycle (stress scales linearly with load), corrected for mean stress, and read against an estimated
/// S-N curve. The curve comes from ultimate strength with Shigley/Marin rules, not from test data for the exact alloy and
/// surface, so treat results as screening estimates.
/// </summary>
public static class Fatigue
{
    public const double LifeCap = 1e9;
    public static readonly (string Label, double Factor)[] Reliabilities = [("50%", 1.0), ("90%", 0.897), ("95%", 0.868), ("99%", 0.814), ("99.9%", 0.753)];

    static readonly string[] Supported = ["Steel", "Aluminum", "Titanium and Nickel", "Copper Alloys"];

    public static FatigueCurve Curve(Material material, FatigueSettings s)
    {
        MaterialRules.Structural(material);
        if (!Supported.Contains(material.Category))
            throw new InvalidOperationException($"{material.Name}: no stress-life fatigue model for {material.Category.ToLowerInvariant()} materials. Supported: steels and cast iron, aluminum, titanium and nickel, and copper alloys.");
        if (material.Evidence.StrengthBasis != "Tensile strength" || !double.IsFinite(material.Ultimate) || material.Ultimate <= 0)
            throw new InvalidOperationException($"{material.Name}: fatigue needs a tensile ultimate strength, but this material records a {material.Evidence.StrengthBasis.ToLowerInvariant()}.");
        if (!(s.SizeFactor > 0 && s.SizeFactor <= 1.2) || !(s.ReliabilityFactor > 0 && s.ReliabilityFactor <= 1) || !(s.RequiredCycles >= 1))
            throw new InvalidOperationException("Use a size factor in (0, 1.2], a reliability factor in (0, 1] and at least 1 required cycle.");
        if (!(s.LoadRatio >= -1 && s.LoadRatio <= 0.95))
            throw new InvalidOperationException("The load ratio R = min/max must be between −1 (fully reversed) and 0.95.");
        if (s.Mean == MeanStressModel.Soderberg && !double.IsFinite(material.Yield))
            throw new InvalidOperationException($"{material.Name} has no yield strength, so the Soderberg mean-stress line is unavailable. Use Goodman or Gerber.");

        double sut = material.Ultimate;
        bool steel = material.Category == "Steel";
        bool knee = steel || material.Category == "Titanium and Nickel";
        double sePrime = material.Category switch
        {
            "Steel" => material.Id == "cast-iron" ? 0.4 * sut : sut <= 1400 ? 0.5 * sut : 700,
            "Aluminum" => 0.3 * sut,
            "Titanium and Nickel" => 0.45 * sut,
            _ => 0.3 * sut,
        };
        string basis = material.Category switch
        {
            "Steel" => material.Id == "cast-iron" ? "Cast iron: Se′ ≈ 0.4·Sut" : "Steel: Se′ ≈ 0.5·Sut (700 MPa above 1400 MPa)",
            "Aluminum" => "Aluminum: strength at 5·10⁸ cycles ≈ 0.3·Sut (no endurance limit)",
            "Titanium and Nickel" => "Titanium/nickel: Se′ ≈ 0.45·Sut",
            _ => "Copper alloys: strength at 5·10⁸ cycles ≈ 0.3·Sut (no endurance limit)",
        };
        var (a, b) = s.Finish switch
        {
            SurfaceFinish.Ground => (1.58, -0.085),
            SurfaceFinish.Machined => (4.51, -0.265),
            SurfaceFinish.HotRolled => (57.7, -0.718),
            _ => (272.0, -0.995),
        };
        double ka = Math.Min(a * Math.Pow(sut, b), 1.0);
        double se = ka * s.SizeFactor * s.ReliabilityFactor * sePrime;
        double f = steel ? Math.Clamp(0.9 - 0.13 * (sut - 490) / 890, 0.77, 0.9) : 0.9;
        double knN = knee ? 1e6 : 5e8;
        double top = f * sut;
        if (se >= top) se = top * 0.999;   // modifiers cannot lift the knee above the 10³-cycle strength
        double slope = Math.Log10(se / top) / Math.Log10(knN / 1e3);
        double coef = top / Math.Pow(1e3, slope);
        return new FatigueCurve(sut, material.Yield, se, sePrime, f, coef, slope, knN, knee, $"{basis}; surface factor {ka:0.###}, size {s.SizeFactor:0.###}, reliability {s.ReliabilityFactor:0.###}");
    }

    static (double amplitude, double mean) Cycle(double peak, double r) => (peak * (1 - r) / 2, peak * (1 + r) / 2);

    /// <summary>Fully reversed stress amplitude that does the same damage as the given cycle (+∞ when the mean stress alone exceeds the chosen limit).</summary>
    public static double EquivalentAmplitude(FatigueCurve c, FatigueSettings s, double peak)
    {
        var (sa, sm) = Cycle(peak, s.LoadRatio);
        double d = s.Mean switch
        {
            MeanStressModel.Goodman => 1 - sm / c.Sut,
            MeanStressModel.Gerber => 1 - sm * sm / (c.Sut * c.Sut),
            _ => 1 - sm / c.Sy,
        };
        return d > 1e-9 ? sa / d : double.PositiveInfinity;
    }

    /// <summary>Cycles to failure for a fully reversed stress amplitude, capped at <see cref="LifeCap"/>.</summary>
    public static double LifeAt(FatigueCurve c, double equivalentAmplitude)
    {
        if (double.IsPositiveInfinity(equivalentAmplitude)) return 1;
        if (equivalentAmplitude <= 1e-12) return LifeCap;
        if (c.HasKnee && equivalentAmplitude <= c.Se) return LifeCap;
        return Math.Clamp(Math.Pow(equivalentAmplitude / c.A, 1 / c.B), 1, LifeCap);
    }

    /// <summary>Largest fully reversed amplitude the material carries for the given number of cycles.</summary>
    public static double Allowable(FatigueCurve c, double cycles) => c.HasKnee && cycles >= c.KneeCycles ? c.Se : c.A * Math.Pow(Math.Max(cycles, 1), c.B);

    /// <summary>Factor by which the load could grow before the cycle just reaches the allowable amplitude at the required life.</summary>
    public static double SafetyFactor(FatigueCurve c, FatigueSettings s, double peak)
    {
        if (peak < 1e-9) return 1000;
        var (sa, sm) = Cycle(peak, s.LoadRatio);
        double sf = Allowable(c, s.RequiredCycles), n;
        if (s.Mean == MeanStressModel.Gerber && sm > 1e-12)
        {
            double q = 2 * sm * sf / (c.Sut * sa);
            n = 0.5 * (c.Sut / sm) * (c.Sut / sm) * (sa / sf) * (-1 + Math.Sqrt(1 + q * q));
        }
        else
        {
            double limit = s.Mean == MeanStressModel.Soderberg ? c.Sy : c.Sut;
            n = 1 / (sa / sf + sm / limit);
        }
        return Math.Clamp(n, 0, 1000);
    }

    /// <summary>Fatigue life, damage and safety factor at every node of a solved static study.</summary>
    public static FatigueResult Analyze(StudyResults r, Material material, FatigueSettings s)
    {
        var c = Curve(material, s);
        int n = r.Mesh.NodeCount;
        if (r.Solution.VonMises.Length < n) throw new InvalidOperationException("The study has no stress result. Run it first.");
        var life = new double[n]; var damage = new double[n]; var safety = new double[n];
        double minLife = double.MaxValue, minSafety = double.MaxValue; int iLife = 0, iSafety = 0, yielded = 0, lowCycle = 0;
        for (int i = 0; i < n; i++)
        {
            double peak = r.Solution.VonMises[i];
            life[i] = LifeAt(c, EquivalentAmplitude(c, s, peak));
            damage[i] = s.RequiredCycles / life[i];
            safety[i] = SafetyFactor(c, s, peak);
            if (life[i] < minLife) { minLife = life[i]; iLife = i; }
            if (safety[i] < minSafety) { minSafety = safety[i]; iSafety = i; }
            if (double.IsFinite(c.Sy) && peak > c.Sy) yielded++;
            if (life[i] < 1e3) lowCycle++;
        }
        var notes = new List<string>
        {
            $"S-N curve: {c.Basis}. Corrected strength {c.Se:0.#} MPa" + (c.HasKnee ? " at the 10⁶-cycle knee; stress amplitudes below it never fail." : " at 5·10⁸ cycles; life keeps shortening as stress rises, with no run-out limit."),
            "The static solution is read as the peak of the cycle, scaled linearly with load. Surface cracks start where stress is highest, so look at surface nodes.",
            "Notch effects come from the mesh itself (no extra fatigue notch factor). Stress peaks at supports and point loads are mesh-dependent; confirm with a refined mesh.",
            "Estimated from ultimate strength, not test data. Corrosion, temperature, residual stress, welds and variable-amplitude loading are not included.",
        };
        if (yielded > 0) notes.Add($"{yielded:N0} nodes exceed the yield strength under peak load: the linear stress is not realistic there and stress-life results are unreliable. Use strain-life methods or reduce the load.");
        if (lowCycle > 0) notes.Add($"{lowCycle:N0} nodes calculate to fewer than 1,000 cycles, outside the high-cycle range this curve covers.");
        return new FatigueResult(c, s, life, damage, safety, minLife, iLife, minSafety, iSafety, notes);
    }
}
