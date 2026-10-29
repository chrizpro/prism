using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Prism.Core;

/// <summary>How an object is shaded: a library material (optional), plus metal/gloss finish.</summary>
public enum SurfacePattern { Automatic, Plain, Wood, Brushed, Cast, CarbonWeave }
public sealed record Appearance(string? MaterialId = null, float Metal = 0f, float Gloss = 0.35f,
    SurfacePattern Pattern = SurfacePattern.Automatic, float PatternScale = 12, float PatternAngle = 0, Material? PhysicalSnapshot = null)
{
    public static readonly Appearance Default = new();
}

/// <summary>
/// An engineering material with typical room-temperature handbook values (MatWeb / ASM / manufacturer datasheets).
/// Each record's evidence identifies checked fields, conditions, estimates and model limitations.
/// NaN means "not applicable" (e.g. brittle materials have no yield point).
/// </summary>
public sealed record Material(
    string Id, string Name, string Category,
    double Density,        // g/cm3
    double Youngs,         // GPa
    [property: JsonNumberHandling(JsonNumberHandling.AllowNamedFloatingPointLiterals)] double Yield, // MPa (NaN = no yield basis)
    double Ultimate,       // MPa (tensile, or flexural for brittle materials)
    double Poisson,
    double Conductivity,   // W/(m*K)
    double Expansion,      // um/(m*K)
    double MaxTemp,        // deg C, approximate continuous service limit
    double SpecificHeat,   // J/(kg*K)
    int Cost,              // 1 (cheap) .. 5 (very expensive), relative to mild steel by weight
    int Corrosion,         // 1 (rusts) .. 5 (immune)
    int Machining,         // 1 (difficult) .. 5 (easy)
    uint Color, float Metal, float Gloss,
    string Uses, string Watch)
{
    public MaterialEvidence Evidence { get; init; } = MaterialEvidence.For(Id);
    /// <summary>Strength-to-weight: ultimate strength per density (kN*m/kg).</summary>
    public double SpecificStrength => Ultimate / Density;
    /// <summary>Stiffness-to-weight: modulus per density (MN*m/kg).</summary>
    public double SpecificStiffness => Youngs / Density;
    /// <summary>Thermal diffusivity in mm2/s (k / (rho * cp)).</summary>
    public double Diffusivity => Conductivity / (Density * 1000 * SpecificHeat) * 1e6;
    [JsonIgnore] public Appearance Look => new(Id, Metal, Gloss, PhysicalSnapshot: this);
    public bool HasYield => double.IsFinite(Yield);

    public override string ToString() => Name;
}

public static class Materials
{
    // helper keeps the table compact; N = brittle / not applicable
    const double N = double.NaN;
    static Material M(string id, string name, string cat, double rho, double e, double ys, double uts, double nu, double k, double cte, double tmax, double cp,
        int cost, int corr, int mach, uint color, float metal, float gloss, string uses, string watch) =>
        new(id, name, cat, rho, e, ys, uts, nu, k, cte, tmax, cp, cost, corr, mach, color, metal > .5f ? 1f : 0f, gloss, uses, watch);

    public static readonly IReadOnlyList<Material> All =
    [
        // ---------------------------------------------------------------- steels and irons
        M("a36", "Mild Steel (A36)", "Steel", 7.85, 200, 250, 400, 0.26, 51.9, 11.7, 400, 490, 1, 1, 4, 0xFF8A9099, 0.85f, 0.35f,
            "Frames, brackets, base plates, general fabrication; welds easily.", "Rusts without paint or plating. Low strength compared with alloy steels."),
        M("1045", "Carbon Steel (1045)", "Steel", 7.85, 205, 530, 625, 0.29, 49.8, 11.9, 400, 486, 1, 1, 4, 0xFF7D838C, 0.85f, 0.4f,
            "Shafts, gears, pins, axles; can be hardened by heat treatment.", "Rusts. Welding needs preheat; distortion during hardening."),
        M("4140", "Alloy Steel (4140, Q&T)", "Steel", 7.85, 205, 850, 1000, 0.29, 42.6, 12.3, 400, 475, 2, 1, 3, 0xFF6C727B, 0.9f, 0.45f,
            "Heavily loaded shafts, gears, tooling, crankshafts, bolts.", "Rusts. Harder to machine once heat treated; weld with care."),
        M("304", "Stainless Steel (304 / 1.4301, cold rolled)", "Steel", 7.90, 200, 230, 540, 0.29, 15, 16, 870, 500, 3, 4, 3, 0xFFC4C8CC, 1f, 0.6f,
            "Food equipment, kitchenware, enclosures, architectural trim, tanks.", "Poor thermal conductor, work-hardens (hard to machine). Pits in chlorides and seawater."),
        M("316", "Stainless Steel (316)", "Steel", 8.00, 193, 205, 515, 0.27, 16.3, 16.0, 870, 500, 3, 5, 3, 0xFFB9BEC4, 0.9f, 0.6f,
            "Marine hardware, chemical and medical equipment, outdoor fittings.", "More expensive than 304. Same low conductivity and work-hardening."),
        M("17-4", "Stainless Steel (17-4 PH, H900)", "Steel", 7.80, 197, 1170, 1310, 0.27, 18.0, 10.8, 315, 460, 4, 4, 3, 0xFFB0B5BB, 0.9f, 0.55f,
            "Aerospace fittings, valve stems, high-strength corrosion-resistant parts.", "Loses toughness above ~315 C service. Costly."),
        M("cast-iron", "Gray Cast Iron (ASTM 30)", "Steel", 7.20, 110, N, 214, 0.26, 46.0, 10.5, 400, 490, 1, 2, 4, 0xFF5E6268, 0.6f, 0.2f,
            "Engine blocks, machine beds, housings, brake rotors; damps vibration well.", "Brittle: no yield point, weak in tension, cracks under shock. Heavy."),

        // ---------------------------------------------------------------- aluminum
        M("6061", "Aluminum 6061-T6", "Aluminum", 2.70, 68.9, 276, 310, 0.33, 167, 23.6, 150, 896, 2, 3, 5, 0xFFC9CED6, 0.8f, 0.5f,
            "The general-purpose structural aluminum: frames, brackets, enclosures, tooling plate.", "Welding softens the heat-affected zone. Not for >150 C. Lower strength than 7075."),
        M("7075", "Aluminum 7075-T6", "Aluminum", 2.81, 71.7, 503, 572, 0.33, 130, 23.4, 120, 960, 3, 2, 4, 0xFFC0C6CF, 0.8f, 0.5f,
            "Aerospace structure, high-performance parts, molds; strength near mild-steel levels.", "Poor weldability, prone to stress-corrosion cracking, costs more than 6061."),
        M("5052", "Aluminum 5052-H32", "Aluminum", 2.68, 70.3, 193, 228, 0.33, 138, 23.8, 150, 880, 2, 4, 3, 0xFFD0D4DB, 0.8f, 0.5f,
            "Sheet-metal parts, marine and fuel tanks; bends and welds well.", "Low strength, cannot be heat-treated; poor machinability (gummy)."),
        M("cast-al", "Cast Aluminum (A356-T6)", "Aluminum", 2.68, 72.4, 165, 228, 0.33, 151, 21.4, 150, 963, 2, 3, 4, 0xFFB6BCC4, 0.7f, 0.35f,
            "Castings: wheels, housings, pump bodies, complex shapes.", "Casting porosity limits fatigue life; lower ductility than wrought grades."),

        // ---------------------------------------------------------------- other metals
        M("ti64", "Titanium Ti-6Al-4V (Grade 5)", "Titanium and Nickel", 4.43, 113.8, 880, 950, 0.34, 6.7, 8.6, 400, 526, 5, 5, 1, 0xFF9DA3A8, 0.85f, 0.45f,
            "Aerospace, medical implants, racing parts: top strength-to-weight with corrosion immunity.", "Expensive, hard to machine, galls, very low conductivity (heat builds up in cutting)."),
        M("inconel", "Inconel 718", "Titanium and Nickel", 8.19, 200, 1030, 1240, 0.29, 11.4, 13.0, 700, 435, 5, 5, 1, 0xFF9A9FA6, 0.9f, 0.5f,
            "Turbine parts, exhaust, rocket engines, high-temperature fasteners.", "Very expensive and very difficult to machine; work-hardens fast."),
        M("copper", "Copper (C110, annealed)", "Copper Alloys", 8.94, 117, 70, 220, 0.34, 391, 17.0, 200, 385, 3, 4, 3, 0xFFC87941, 0.95f, 0.6f,
            "Electrical conductors, busbars, heat sinks, plumbing.", "Soft and heavy; gummy to machine; tarnishes."),
        M("brass", "Brass (C360, free-cutting)", "Copper Alloys", 8.50, 97, 310, 385, 0.34, 115, 20.5, 200, 380, 3, 4, 5, 0xFFD2A94A, 0.9f, 0.6f,
            "Fittings, valves, gears, decorative hardware; machines superbly.", "Contains lead (C360); dezincification in some waters."),
        M("bronze", "Bearing Bronze (C93200, cast)", "Copper Alloys", 8.91, 100, 124, 241, 0.34, 58.2, 18.0, 260, 377, 3, 4, 4, 0xFFAE7A3C, 1f, 0.5f,
            "Bushings and bearings, worm gears, marine hardware.", "Heavy; moderate strength; leaded grades have RoHS issues."),
        M("az31", "Magnesium AZ31B", "Light Metals", 1.77, 45, 200, 260, 0.35, 96.0, 26.0, 120, 1000, 3, 2, 5, 0xFFB5BBC2, 0.7f, 0.4f,
            "Lightest structural metal: laptop and camera bodies, aerospace, power-tool housings.", "Flammable chips when machining; corrodes; low stiffness and fatigue properties."),
        M("zamak", "Zinc Die-Cast (Zamak 3)", "Light Metals", 6.60, 85, 208, 283, 0.30, 113, 27.4, 100, 420, 2, 3, 5, 0xFFA7ADB4, 0.75f, 0.45f,
            "Small die-cast parts: hardware, gears, toys, connectors.", "Creeps under load; heavy; maximum service about 100 C."),

        // ---------------------------------------------------------------- plastics
        M("abs", "ABS", "Plastic", 1.04, 2.3, 40, 40, 0.35, 0.17, 90, 80, 1400, 1, 4, 4, 0xFFE3DDD0, 0f, 0.55f,
            "Enclosures, 3D printing, automotive trim, toys (LEGO).", "UV-sensitive, softens near 100 C, poor solvent resistance."),
        M("pla", "PLA", "Plastic", 1.24, 3.5, 60, 65, 0.36, 0.13, 68, 50, 1800, 1, 3, 4, 0xFFE9E4D6, 0f, 0.45f,
            "3D-printing prototypes, packaging; stiff and easy to print.", "Brittle, creeps and warps above ~55 C, absorbs water slowly."),
        M("petg", "PETG", "Plastic", 1.27, 2.1, 50, 53, 0.38, 0.20, 60, 70, 1200, 1, 4, 3, 0xFFCFE6EC, 0f, 0.75f,
            "Bottles, food containers, 3D prints needing toughness and clarity.", "Scratches easily; stringy to print and machine."),
        M("nylon", "Nylon 6/6 (PA66, dry)", "Plastic", 1.14, 2.9, 80, 82, 0.39, 0.25, 80, 100, 1670, 2, 4, 3, 0xFFEDE6D4, 0f, 0.4f,
            "Gears, bearings, bushings, fasteners; tough with good wear resistance.", "Absorbs water (swells and loses stiffness); stiffer when glass-filled."),
        M("pc", "Polycarbonate (PC)", "Plastic", 1.20, 2.4, 62, 65, 0.37, 0.21, 68, 115, 1200, 2, 3, 4, 0xFFD9E7EE, 0f, 0.85f,
            "Safety glazing, helmets, housings, light covers; extremely impact-tough.", "Scratches and crazes with solvents; UV-degrades unless stabilized."),
        M("pom", "Acetal (POM, Delrin)", "Plastic", 1.41, 3.1, 70, 70, 0.35, 0.31, 110, 90, 1470, 2, 4, 5, 0xFFF1F1EC, 0f, 0.55f,
            "Precision gears, bearings, snap-fits; low friction, stable dimensions, machines beautifully.", "Hard to glue; degrades in strong acids and UV."),
        M("peek", "PEEK", "Plastic", 1.32, 3.6, 100, 100, 0.38, 0.25, 47, 250, 1340, 5, 5, 4, 0xFFC9B79A, 0f, 0.45f,
            "Aerospace, medical, semiconductor parts: high strength to 250 C and chemical resistance.", "Very expensive; needs high processing temperatures."),
        M("hdpe", "HDPE", "Plastic", 0.95, 1.0, 26, 30, 0.42, 0.45, 150, 80, 2250, 1, 5, 3, 0xFFF3F1EA, 0f, 0.35f,
            "Tanks, pipes, cutting boards, chemical containers; excellent chemical resistance.", "Low stiffness, large thermal expansion, creeps; hard to bond."),
        M("pp", "Polypropylene (PP)", "Plastic", 0.90, 1.5, 33, 36, 0.42, 0.22, 100, 100, 1900, 1, 5, 3, 0xFFEFEDE4, 0f, 0.35f,
            "Living hinges, containers, automotive parts; cheap and chemical resistant.", "Warps, UV-sensitive, hard to bond or paint."),
        M("pmma", "Acrylic (PMMA)", "Plastic", 1.18, 3.0, 70, 72, 0.37, 0.19, 70, 80, 1470, 2, 4, 4, 0xFFDCEBF2, 0f, 0.95f,
            "Windows, lenses, light guides, displays; glass-like clarity and UV stable.", "Brittle and notch-sensitive; cracks under solvents."),
        M("ptfe", "PTFE (Teflon)", "Plastic", 2.20, 0.5, 12, 25, 0.46, 0.25, 120, 260, 1000, 3, 5, 3, 0xFFF4F4F0, 0f, 0.3f,
            "Seals, non-stick coatings, bearings, chemical linings; lowest friction.", "Soft, creeps badly, expands a lot, nearly impossible to bond."),
        M("tpu", "TPU (95A, flexible)", "Elastomer", 1.20, 0.026, 25, 40, 0.48, 0.20, 150, 80, 1800, 2, 4, 2, 0xFF3C6EA8, 0f, 0.5f,
            "Flexible 3D prints, seals, gaskets, phone cases, wheels.", "Hard to print and machine; stiffness is very low compared with rigid plastics."),
        M("silicone", "Silicone Rubber", "Elastomer", 1.15, 0.005, N, 8, 0.48, 0.20, 250, 200, 1250, 2, 5, 1, 0xFFC25B4A, 0f, 0.6f,
            "Gaskets, seals, molds, medical tubing; stable from -50 to 200 C.", "Low tear strength; sticky and difficult to bond without primers."),

        // ---------------------------------------------------------------- composites, wood, ceramics
        M("cfrp", "Carbon Fiber / Epoxy (quasi-isotropic)", "Composite", 1.55, 70, N, 600, 0.30, 5.0, 2.0, 120, 1000, 5, 5, 2, 0xFF22252A, 0.2f, 0.85f,
            "Aerospace, racing, drones, bike frames; extreme specific stiffness and strength.", "Anisotropic (values depend on layup), brittle failure, galvanic corrosion with aluminum, expensive."),
        M("gfrp", "Glass Fiber / Epoxy (G10 / FR4)", "Composite", 1.85, 24, N, 310, 0.28, 0.3, 12.0, 130, 1100, 3, 5, 3, 0xFF8CA86B, 0f, 0.6f,
            "Circuit boards, insulators, boat hulls, structural plate; electrically insulating.", "Abrasive to tools, dust is hazardous, anisotropic."),
        M("oak", "Red Oak (along grain)", "Wood and Ceramic", 0.70, 12.5, N, 99, 0.35, 0.17, 5.0, 100, 2380, 2, 2, 4, 0xFFB98B5A, 0f, 0.3f,
            "Furniture, flooring, tooling jigs, structural timber.", "Properties vary with grain direction and moisture; swells and shrinks; burns."),
        M("fir", "Douglas Fir (along grain)", "Wood and Ceramic", 0.53, 13.4, N, 85, 0.29, 0.14, 4.0, 100, 2300, 1, 2, 5, 0xFFD2A877, 0f, 0.25f,
            "Framing, beams, plywood, fixtures; strong for its weight and cheap.", "Weak across the grain; moisture movement; burns."),
        M("glass", "Glass (soda-lime)", "Wood and Ceramic", 2.50, 70, N, 50, 0.22, 1.0, 9.0, 460, 840, 1, 5, 1, 0xFFBFE0E8, 0f, 1.0f,
            "Windows, bottles, lenses, covers; hard, transparent and chemically inert.", "Brittle: shatters from tensile stress, thermal shock and edge flaws."),
        M("alumina", "Alumina Ceramic (96%)", "Wood and Ceramic", 3.75, 300, N, 300, 0.22, 25.0, 8.1, 1500, 880, 3, 5, 1, 0xFFEFEBE0, 0f, 0.7f,
            "Wear parts, insulators, substrates, nozzles; very hard and heat resistant.", "Brittle; must be ground with diamond tools; sensitive to impact and thermal shock."),
    ];

    public static IEnumerable<string> Categories => All.Select(m => m.Category).Distinct();
    static readonly Dictionary<string, Material> ById = All.ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);
    public static Material? Find(string? id) => id != null && ById.TryGetValue(id, out var m) ? m : null;
    public static Material Default => ById["a36"];

    // library extremes, used to scale the comparison bars in the panel
    public static double Max(Func<Material, double> f) => All.Select(f).Where(v => !double.IsNaN(v)).Max();
}
