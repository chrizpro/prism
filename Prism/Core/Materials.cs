using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Prism.Core;

/// <summary>How an object is shaded: a library material (optional), plus metal/gloss finish.</summary>
public enum SurfacePattern { Automatic, Plain, Wood, Brushed, Cast, CarbonWeave }
public sealed record Appearance(string? MaterialId = null, float Metal = 0f, float Gloss = 0.35f,
    SurfacePattern Pattern = SurfacePattern.Automatic, float PatternScale = 12, float PatternAngle = 0, Material? PhysicalSnapshot = null, SurfaceStyle? Surface = null)
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
    public SavedAppearance? DefaultAppearance { get; init; }
    [JsonIgnore] public bool IsCustom => Id.StartsWith("custom:", StringComparison.Ordinal);
    [JsonIgnore] public string DisplayName => IsCustom ? Name + " · Custom" : Name;
    /// <summary>Strength-to-weight: ultimate strength per density (kN*m/kg).</summary>
    public double SpecificStrength => Ultimate / Density;
    /// <summary>Stiffness-to-weight: modulus per density (MN*m/kg).</summary>
    public double SpecificStiffness => Youngs / Density;
    /// <summary>Thermal diffusivity in mm2/s (k / (rho * cp)).</summary>
    public double Diffusivity => Conductivity / (Density * 1000 * SpecificHeat) * 1e6;
    [JsonIgnore] public Appearance Look
    {
        get
        {
            var look = new Appearance(Id, Metal, Gloss, PhysicalSnapshot: this, Surface: Id == "glass" ? new SurfaceStyle { Transmission = 1, Ior = 1.52f, AbsorptionColor = Color, AbsorptionDistance = 100 } : null);
            if (DefaultAppearance is not { } recipe) return look;
            var applied = recipe.ApplyTo(look);
            return applied with { Surface = applied.Surface! with { Override = false } };
        }
    }
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
        // ---------------------------------------------------------------- carbon and alloy steels
        M("a36", "Mild Steel (A36)", "Steel", 7.85, 200, 250, 400, 0.26, 51.9, 11.7, 400, 490, 1, 1, 4, 0xFF8A9099, 0.85f, 0.35f,
            "Frames, brackets, base plates, general fabrication; welds easily.", "Rusts without paint or plating. Low strength compared with alloy steels."),
        M("1018", "Low-Carbon Steel (1018, cold drawn)", "Steel", 7.87, 205, 370, 440, 0.29, 51.9, 11.5, 425, 486, 1, 1, 5, 0xFF8D939C, 0.85f, 0.4f,
            "Shafts, pins, studs, weldments and general machining; the default cheap bright bar.", "Rusts. Will not harden except by case carburizing."),
        M("a572", "HSLA Structural Steel (A572 Gr 50)", "Steel", 7.85, 200, 345, 450, 0.26, 45.0, 11.7, 400, 490, 1, 2, 4, 0xFF868B94, 0.85f, 0.35f,
            "Structural shapes, bridges, welded frames; stronger than A36 for the same weight.", "Rusts. Thick sections need weld procedure care."),
        M("1045", "Carbon Steel (1045)", "Steel", 7.85, 205, 530, 625, 0.29, 49.8, 11.9, 400, 486, 1, 1, 4, 0xFF7D838C, 0.85f, 0.4f,
            "Shafts, gears, pins, axles; can be hardened by heat treatment.", "Rusts. Welding needs preheat; distortion during hardening."),
        M("1144", "Resulfurized Steel (1144, Stressproof)", "Steel", 7.85, 205, 690, 760, 0.29, 45.0, 11.6, 400, 475, 2, 1, 5, 0xFF80868F, 0.85f, 0.45f,
            "Shafts, arbors and pins that must stay straight; strong with no heat treatment.", "Rusts. Sulfur hurts ductility and weldability; not for shock loading."),
        M("4140", "Alloy Steel (4140, Q&T)", "Steel", 7.85, 205, 850, 1000, 0.29, 42.6, 12.3, 400, 475, 2, 1, 3, 0xFF6C727B, 0.9f, 0.45f,
            "Heavily loaded shafts, gears, tooling, crankshafts, bolts.", "Rusts. Harder to machine once heat treated; weld with care."),
        M("8620", "Carburizing Steel (8620)", "Steel", 7.85, 205, 385, 535, 0.29, 44.5, 12.2, 425, 475, 2, 1, 4, 0xFF767C85, 0.85f, 0.4f,
            "Gears, cams, splines and wear parts case-hardened for a hard shell over a tough core.", "Rusts. Needs carburizing to earn its keep; weld with preheat."),
        M("5160", "Spring Steel (5160, Q&T)", "Steel", 7.85, 205, 1500, 1650, 0.29, 46.6, 11.8, 315, 475, 2, 1, 3, 0xFF646B74, 0.9f, 0.45f,
            "Leaf springs, high-stress flat parts, large hand tools.", "Rusts. Avoid the ~260-370 C temper-embrittlement range when heat treating."),
        M("52100", "Bearing Steel (52100, through-hardened)", "Steel", 7.81, 210, 1600, 2000, 0.30, 46.0, 11.9, 150, 460, 3, 1, 2, 0xFF5F666F, 0.9f, 0.5f,
            "Ball and roller bearings, races, wear sleeves and rollers.", "Rusts. Nearly unmachinable when hardened; grind it. Brittle at full hardness."),
        M("cast-steel", "Cast Steel (ASTM A27, 65-35)", "Steel", 7.83, 200, 240, 450, 0.28, 45.0, 11.7, 400, 490, 2, 1, 4, 0xFF7A8089, 0.7f, 0.3f,
            "Cast brackets, housings and structural parts too complex to fabricate.", "Rusts. Casting porosity and residual stress; weld repair is common."),

        // ---------------------------------------------------------------- stainless steels
        M("303", "Stainless Steel (303, free-machining)", "Stainless Steel", 7.90, 193, 240, 620, 0.29, 16.2, 17.2, 870, 500, 3, 3, 5, 0xFFC8CCD1, 1f, 0.6f,
            "Machined fittings, fasteners and shafts that need stainless without fighting the cut.", "Sulfur lowers corrosion resistance and weldability versus 304."),
        M("304", "Stainless Steel (304 / 1.4301, cold rolled)", "Stainless Steel", 7.90, 200, 230, 540, 0.29, 15, 16, 870, 500, 3, 4, 3, 0xFFC4C8CC, 1f, 0.6f,
            "Food equipment, kitchenware, enclosures, architectural trim, tanks.", "Poor thermal conductor, work-hardens (hard to machine). Pits in chlorides and seawater."),
        M("316", "Stainless Steel (316)", "Stainless Steel", 8.00, 193, 205, 515, 0.27, 16.3, 16.0, 870, 500, 3, 5, 3, 0xFFB9BEC4, 0.9f, 0.6f,
            "Marine hardware, chemical and medical equipment, outdoor fittings.", "More expensive than 304. Same low conductivity and work-hardening."),
        M("410", "Stainless Steel (410, martensitic)", "Stainless Steel", 7.70, 200, 275, 485, 0.28, 24.9, 9.9, 650, 460, 3, 3, 3, 0xFFAFB5BC, 0.9f, 0.55f,
            "Cutlery, valves, fasteners and wear parts; hardenable like carbon steel.", "Only moderate corrosion resistance; welds need preheat and tempering."),
        M("416", "Stainless Steel (416, free-machining)", "Stainless Steel", 7.70, 200, 275, 517, 0.28, 24.9, 9.9, 650, 460, 3, 3, 5, 0xFFB4B9C0, 0.9f, 0.55f,
            "Screws, gears and machined stainless parts at production rates.", "Corrosion resistance a notch below 410; avoid welding."),
        M("430", "Stainless Steel (430, ferritic)", "Stainless Steel", 7.75, 200, 310, 450, 0.28, 26.1, 10.4, 815, 460, 2, 3, 4, 0xFFBCC1C8, 0.9f, 0.6f,
            "Appliance and automotive trim, sinks, interior panels; magnetic and cheaper than 304.", "Not hardenable. Moderate corrosion resistance; avoid marine exposure."),
        M("17-4", "Stainless Steel (17-4 PH, H900)", "Stainless Steel", 7.80, 197, 1170, 1310, 0.27, 18.0, 10.8, 315, 460, 4, 4, 3, 0xFFB0B5BB, 0.9f, 0.55f,
            "Aerospace fittings, valve stems, high-strength corrosion-resistant parts.", "Loses toughness above ~315 C service. Costly."),
        M("15-5", "Stainless Steel (15-5 PH, H1025)", "Stainless Steel", 7.80, 196, 1000, 1070, 0.27, 17.8, 10.8, 315, 460, 4, 4, 3, 0xFFADB2B9, 0.9f, 0.55f,
            "Aerospace structural parts and shafts; better toughness than 17-4 at similar strength.", "Service limit ~315 C. Costly."),
        M("2205", "Duplex Stainless (2205)", "Stainless Steel", 7.80, 190, 450, 655, 0.30, 19.0, 13.7, 300, 500, 4, 5, 2, 0xFFC0C5CC, 0.9f, 0.55f,
            "Chemical tanks, marine and offshore hardware, pressure vessels needing chloride resistance.", "Embrittles above ~300 C service; hard to machine; costly."),

        // ---------------------------------------------------------------- tool steels
        M("a2", "Tool Steel A2 (air-hardening)", "Tool Steel", 7.86, 203, 1500, 1800, 0.29, 25.0, 10.7, 200, 460, 3, 2, 3, 0xFF575E68, 0.9f, 0.5f,
            "Punches, dies, shear blades and forming tools; stable size change on hardening.", "Rusts. Moderate wear resistance; grind after hardening."),
        M("d2", "Tool Steel D2 (high-carbon, high-chrome)", "Tool Steel", 7.70, 210, 1500, 1750, 0.29, 20.0, 10.4, 450, 460, 3, 2, 2, 0xFF4E555F, 0.9f, 0.5f,
            "Blanking and forming dies for long production runs; extreme abrasion resistance.", "Rusts slowly (12% Cr is only semi-stainless). Hard carbides fight grinding and EDM."),
        M("h13", "Tool Steel H13 (hot-work)", "Tool Steel", 7.80, 210, 1380, 1590, 0.29, 24.3, 10.4, 540, 460, 4, 2, 3, 0xFF5A616B, 0.9f, 0.5f,
            "Die-casting dies, extrusion and forging tooling that stays hard at temperature.", "Needs careful preheat and multiple tempers; watch for heat checking."),
        M("m2", "High-Speed Steel M2", "Tool Steel", 8.16, 210, 2000, 2200, 0.30, 19.0, 11.5, 540, 420, 4, 1, 2, 0xFF494F59, 0.9f, 0.55f,
            "Drills, end mills, taps, broaches; keeps a cutting edge hot (red hardness).", "Rusts. Very brittle; grind or EDM only; needs triple tempering."),
        M("o1", "Tool Steel O1 (oil-hardening)", "Tool Steel", 7.85, 210, 1500, 1900, 0.29, 30.0, 11.5, 200, 460, 2, 1, 4, 0xFF596069, 0.9f, 0.45f,
            "Gages, jigs, knives and general tooling; easy to machine and harden in a small shop.", "Rusts. Distorts more than air-hardening grades; soft skin after hardening."),
        M("s7", "Tool Steel S7 (shock-resistant)", "Tool Steel", 7.80, 207, 1450, 2000, 0.29, 28.0, 12.0, 315, 460, 3, 1, 3, 0xFF545B64, 0.9f, 0.45f,
            "Chisels, punches, shear blades and high-impact tools; toughness over wear.", "Rusts. Lower abrasion resistance than D2."),

        // ---------------------------------------------------------------- cast irons
        M("cast-iron", "Gray Cast Iron (ASTM 30)", "Cast Iron", 7.20, 110, N, 214, 0.26, 46.0, 10.5, 400, 490, 1, 2, 4, 0xFF5E6268, 0.6f, 0.2f,
            "Engine blocks, machine beds, housings, brake rotors; damps vibration well.", "Brittle: no yield point, weak in tension, cracks under shock. Heavy."),
        M("ductile-iron", "Ductile Iron (ASTM A536, 65-45-12)", "Cast Iron", 7.10, 168, 310, 448, 0.29, 36.0, 10.8, 450, 500, 1, 2, 4, 0xFF62666C, 0.6f, 0.25f,
            "Crankshafts, hubs, pump housings, pipe fittings; near-steel strength that still casts well.", "Rusts. Heavier than steel per part; casting quality drives fatigue life."),
        M("malleable-iron", "Malleable Iron (ASTM A47, 32510)", "Cast Iron", 7.30, 172, 224, 345, 0.28, 45.0, 11.0, 400, 490, 1, 2, 4, 0xFF686C72, 0.6f, 0.25f,
            "Small brackets, levers and hardware needing a tough thin-section casting.", "Rusts. Limited to thin sections; niche versus ductile iron today."),

        // ---------------------------------------------------------------- aluminum
        M("1100", "Aluminum 1100-H14", "Aluminum", 2.71, 68.9, 115, 125, 0.33, 222, 23.6, 150, 904, 1, 5, 3, 0xFFD3D8DF, 0.8f, 0.45f,
            "Deep-drawn parts, chemical equipment, reflectors and spun shapes; nearly pure aluminum.", "Very soft and gummy to machine. No meaningful strength."),
        M("2024", "Aluminum 2024-T351", "Aluminum", 2.78, 73.1, 325, 470, 0.33, 121, 23.2, 120, 875, 3, 2, 4, 0xFFBFC5CE, 0.8f, 0.5f,
            "Aircraft skins and structures, fatigue-critical machined parts.", "Poor corrosion resistance bare (often clad); practically unweldable."),
        M("3003", "Aluminum 3003-H14", "Aluminum", 2.73, 68.9, 145, 150, 0.33, 193, 23.2, 150, 893, 1, 4, 4, 0xFFCED4DC, 0.8f, 0.45f,
            "Sheet-metal general purpose: tanks, ductwork, cookware, formed panels.", "Low strength and not heat-treatable."),
        M("5052", "Aluminum 5052-H32", "Aluminum", 2.68, 70.3, 195, 228, 0.33, 138, 23.8, 150, 880, 2, 4, 4, 0xFFC9CFD8, 0.8f, 0.5f,
            "Sheet-metal enclosures, marine panels, fuel tanks; best common alloy for bending and welding.", "Not heat-treatable; moderate strength only."),
        M("5083", "Aluminum 5083-H116", "Aluminum", 2.66, 70.3, 215, 305, 0.33, 117, 23.8, 120, 900, 2, 5, 4, 0xFFC4CBD4, 0.8f, 0.5f,
            "Marine hulls, cryogenic tanks, pressure vessels and welded structures; strongest common non-heat-treatable alloy.", "Sensitizes in warm service for some tempers; costly vs 5052."),
        M("6061", "Aluminum 6061-T6", "Aluminum", 2.70, 68.9, 276, 310, 0.33, 167, 23.6, 150, 896, 2, 3, 5, 0xFFC9CED6, 0.8f, 0.5f,
            "The general-purpose structural aluminum: frames, brackets, enclosures, tooling plate.", "Welding softens the heat-affected zone. Not for >150 C. Lower strength than 7075."),
        M("6063", "Aluminum 6063-T6", "Aluminum", 2.70, 68.3, 214, 241, 0.33, 200, 23.4, 150, 900, 2, 4, 5, 0xFFD0D5DD, 0.8f, 0.5f,
            "Extrusions, frames, architectural trim and heat sinks; the extrusion alloy.", "Weaker than 6061; check wall sections in loaded parts."),
        M("7075", "Aluminum 7075-T6", "Aluminum", 2.81, 71.7, 503, 572, 0.33, 130, 23.4, 120, 960, 3, 2, 4, 0xFFC0C6CF, 0.8f, 0.5f,
            "Aerospace structure, high-performance parts, molds; strength near mild-steel levels.", "Poor weldability, prone to stress-corrosion cracking, costs more than 6061."),
        M("mic6", "Cast Aluminum Tooling Plate (MIC-6)", "Aluminum", 2.70, 71.0, 105, 166, 0.33, 140, 23.6, 150, 900, 2, 3, 5, 0xFFCBD1DA, 0.75f, 0.4f,
            "Jig plates, fixtures and flat reference bases; stress-relieved cast plate that stays flat.", "Low strength and ductility; not for stressed structure."),
        M("cast-al", "Cast Aluminum (A356-T6)", "Aluminum", 2.68, 72.4, 165, 228, 0.33, 151, 21.4, 150, 963, 2, 3, 4, 0xFFB6BCC4, 0.7f, 0.35f,
            "Castings: wheels, housings, pump bodies, complex shapes.", "Casting porosity limits fatigue life; lower ductility than wrought grades."),

        // ---------------------------------------------------------------- titanium
        M("ti-cp2", "Titanium Grade 2 (commercially pure)", "Titanium", 4.51, 103, 275, 345, 0.32, 16.4, 8.6, 425, 523, 4, 5, 3, 0xFFA8AEB5, 0.85f, 0.45f,
            "Chemical process vessels, marine parts and cold-formed components; the workable pure grade.", "Galls badly. Low modulus; springback in forming."),
        M("ti64", "Titanium Ti-6Al-4V (Grade 5)", "Titanium", 4.43, 113.8, 880, 950, 0.34, 6.7, 8.6, 400, 526, 5, 5, 1, 0xFF9DA3A8, 0.85f, 0.45f,
            "Aerospace, medical implants, racing parts: top strength-to-weight with corrosion immunity.", "Expensive, hard to machine, galls, very low conductivity (heat builds up in cutting)."),
        M("ti64eli", "Titanium Ti-6Al-4V ELI (Grade 23)", "Titanium", 4.43, 114, 795, 860, 0.31, 6.7, 8.6, 350, 526, 5, 5, 1, 0xFFA2A8AF, 0.85f, 0.45f,
            "Implants, cryogenic and fracture-critical aerospace parts; extra purity for toughness.", "Cost and machining of Grade 5, with slightly lower strength."),
        M("ti-3al", "Titanium Ti-3Al-2.5V (Grade 9)", "Titanium", 4.48, 103, 483, 621, 0.32, 7.3, 9.5, 315, 540, 5, 5, 2, 0xFFA5ABB2, 0.85f, 0.45f,
            "Aircraft hydraulic tubing and bicycle frames; half the strength of Grade 5 but cold-workable.", "Costly. Galls; use sharp tooling."),

        // ---------------------------------------------------------------- nickel alloys
        M("inconel", "Inconel 718", "Nickel", 8.19, 200, 1030, 1240, 0.29, 11.4, 13.0, 700, 435, 5, 5, 1, 0xFF9A9FA6, 0.9f, 0.5f,
            "Turbine parts, exhaust, rocket engines, high-temperature fasteners.", "Very expensive and very difficult to machine; work-hardens fast."),
        M("inconel625", "Inconel 625", "Nickel", 8.44, 207.5, 460, 930, 0.29, 9.8, 12.8, 870, 410, 5, 5, 1, 0xFFA0A6AD, 0.9f, 0.5f,
            "Marine, chemical and aerospace parts needing corrosion resistance that survives heat.", "Work-hardens in the cut; very expensive."),
        M("monel", "Monel 400", "Nickel", 8.83, 179, 240, 550, 0.32, 21.8, 13.9, 480, 427, 5, 5, 3, 0xFFA4AAA0, 0.9f, 0.5f,
            "Seawater valves and pumps, chemical equipment, hydrofluoric-acid service.", "Costly. Work-hardens; heavy."),
        M("hastelloy", "Hastelloy C-276", "Nickel", 8.89, 205, 355, 790, 0.30, 10.0, 11.2, 1000, 427, 5, 5, 1, 0xFF9DA39B, 0.9f, 0.5f,
            "Aggressive chemical service, scrubbers, sour-gas equipment; resists nearly everything.", "Very expensive; work-hardens severely."),

        // ---------------------------------------------------------------- copper alloys
        M("copper", "Copper (C110, annealed)", "Copper Alloys", 8.94, 117, 70, 220, 0.34, 391, 17.0, 200, 385, 3, 4, 3, 0xFFC87941, 0.95f, 0.6f,
            "Electrical conductors, busbars, heat sinks, plumbing.", "Soft and heavy; gummy to machine; tarnishes."),
        M("brass", "Brass (C360, free-cutting)", "Copper Alloys", 8.50, 97, 310, 385, 0.34, 115, 20.5, 200, 380, 3, 4, 5, 0xFFD2A94A, 0.9f, 0.6f,
            "Fittings, valves, gears, decorative hardware; machines superbly.", "Contains lead (C360); dezincification in some waters."),
        M("cartridge-brass", "Cartridge Brass (C26000, 70/30)", "Copper Alloys", 8.53, 110, 95, 330, 0.35, 120, 19.9, 200, 375, 3, 3, 3, 0xFFD6B25E, 0.9f, 0.6f,
            "Deep-drawn cups and cases, radiator cores, fasteners; the best cold-working brass.", "Season-cracks in ammonia environments; dezincifies in some waters."),
        M("bronze", "Bearing Bronze (C93200, cast)", "Copper Alloys", 8.91, 100, 124, 241, 0.34, 58.2, 18.0, 260, 377, 3, 4, 4, 0xFFAE7A3C, 1f, 0.5f,
            "Bushings and bearings, worm gears, marine hardware.", "Heavy; moderate strength; leaded grades have RoHS issues."),
        M("becu", "Beryllium Copper (C17200)", "Copper Alloys", 8.25, 128, 965, 1140, 0.30, 105, 17.0, 250, 420, 5, 4, 3, 0xFFC98A54, 0.9f, 0.55f,
            "Springs, electrical contacts, bellows, non-sparking tools; the strongest copper alloy.", "Beryllium dust is toxic - machine wet and handle chips carefully. Costly."),
        M("albronze", "Aluminum Bronze (C95400)", "Copper Alloys", 7.45, 110, 230, 585, 0.33, 59.0, 16.2, 315, 440, 4, 5, 3, 0xFFBF8A4E, 0.9f, 0.5f,
            "Gears, bearings, marine hardware and non-sparking tools; strong and seawater-proof.", "Costly. Welding needs procedure control."),
        M("phos-bronze", "Phosphor Bronze (C51000)", "Copper Alloys", 8.86, 110, 455, 524, 0.34, 69.0, 17.8, 200, 380, 4, 4, 3, 0xFFBC8B54, 0.9f, 0.55f,
            "Springs, electrical contacts, fasteners and marine hardware; excellent fatigue life.", "Costly. Strength relaxes at elevated temperature."),

        // ---------------------------------------------------------------- magnesium, zinc and other metals
        M("az31", "Magnesium AZ31B", "Light Metals", 1.77, 45, 200, 260, 0.35, 96.0, 26.0, 120, 1000, 3, 2, 5, 0xFFB5BBC2, 0.7f, 0.4f,
            "Lightest structural metal: laptop and camera bodies, aerospace, power-tool housings.", "Flammable chips when machining; corrodes; low stiffness and fatigue properties."),
        M("az91", "Magnesium AZ91D (die-cast)", "Light Metals", 1.81, 45, 160, 230, 0.35, 51.0, 26.0, 120, 1050, 3, 2, 5, 0xFFABB1B9, 0.7f, 0.4f,
            "Die-cast housings: laptops, power tools, automotive brackets; the dominant cast magnesium.", "Corrodes without coating. Flammable chips and melt."),
        M("zamak", "Zinc Die-Cast (Zamak 3)", "Light Metals", 6.60, 85, 208, 283, 0.30, 113, 27.4, 100, 420, 2, 3, 5, 0xFFA7ADB4, 0.75f, 0.45f,
            "Small die-cast parts: hardware, gears, toys, connectors.", "Creeps under load; heavy; maximum service about 100 C."),
        M("zamak5", "Zinc Die-Cast (Zamak 5)", "Light Metals", 6.60, 85, 269, 328, 0.30, 108, 27.0, 100, 419, 2, 2, 5, 0xFF9FA5AC, 0.75f, 0.45f,
            "Die-cast hardware needing more strength than Zamak 3; slightly less ductile.", "Creeps under load; aging growth; about 100 C maximum."),
        M("lead", "Lead (chemical, pure)", "Other Metals", 11.34, 16, 11, 17, 0.44, 35.0, 28.9, 150, 129, 2, 4, 5, 0xFF5C6167, 0.7f, 0.3f,
            "Radiation shielding, ballast weights, sound damping, battery plates.", "Toxic - control chips and dust. Creeps under its own weight."),
        M("tin", "Tin (pure)", "Other Metals", 7.31, 50, 11, 16, 0.36, 66.8, 22.0, 120, 228, 4, 4, 4, 0xFFC7CDD4, 0.85f, 0.6f,
            "Plating, solder alloying, bearing surfaces, food-safe coatings.", "Very soft. Tin pest crumbling below 13 C."),
        M("solder", "Solder (63/37 Sn-Pb, eutectic)", "Other Metals", 8.40, 30, 35, 52, 0.40, 50.0, 21.0, 120, 176, 3, 3, 5, 0xFFAEB4BA, 0.8f, 0.5f,
            "Electronics joints; mass and volume estimates for assembled boards.", "Creeps at room temperature. Lead content (RoHS-restricted)."),
        M("tungsten", "Tungsten (pure)", "Other Metals", 19.30, 400, 550, 980, 0.29, 173, 4.5, 500, 134, 5, 4, 1, 0xFF4E545C, 0.9f, 0.6f,
            "Balance weights, electrodes, X-ray shielding, high-temperature fixtures.", "Very brittle and very hard to machine; costly."),
        M("tungcarb", "Tungsten Carbide (WC-Co, cemented)", "Other Metals", 15.00, 550, N, 2000, 0.26, 88.0, 5.5, 500, 210, 5, 3, 1, 0xFF3C4147, 0.9f, 0.6f,
            "Cutting inserts, dies, seals and wear parts; the hardest common tooling material.", "Brittle - strength figure is transverse rupture, not tensile. Grind or EDM only; costly."),
        M("silver", "Silver (fine, annealed)", "Other Metals", 10.49, 83, 55, 170, 0.37, 420, 19.0, 200, 235, 5, 4, 4, 0xFFE0E4E8, 1f, 0.85f,
            "Electrical contacts, jewelry, plating and heat spreaders; best conductor of any metal.", "Very costly. Soft and tarnishes."),
        M("gold", "Gold (24k, annealed)", "Other Metals", 19.30, 79, 40, 130, 0.42, 315, 14.2, 250, 129, 5, 5, 4, 0xFFE6B93D, 1f, 0.85f,
            "Connector plating, jewelry, electronics bond pads; never corrodes.", "Extremely costly and very soft."),

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
        M("pvc", "PVC (rigid, uPVC)", "Plastic", 1.38, 3.0, 48, 55, 0.38, 0.17, 50, 65, 900, 1, 5, 4, 0xFFE8E4DC, 0f, 0.5f,
            "Pipe, fittings, chemical tanks and extruded profiles; cheap and corrosion-proof.", "Softens ~65 C; UV degrades outdoors; fumes if burned."),
        M("ldpe", "LDPE", "Plastic", 0.92, 0.25, 10, 13, 0.45, 0.33, 200, 80, 2300, 1, 5, 3, 0xFFF5F3EC, 0f, 0.3f,
            "Film, squeeze bottles, liners and packaging; flexible and nearly unbreakable.", "Very soft, creeps, huge thermal expansion."),
        M("hips", "Polystyrene (HIPS)", "Plastic", 1.05, 2.1, 24, 27, 0.35, 0.17, 90, 75, 1300, 1, 4, 4, 0xFFEFEDE8, 0f, 0.4f,
            "Thermoformed trays, disposable housings, model parts; cheap and easy to form.", "Brittle under impact vs ABS; weak UV and solvent resistance; softens ~80 C."),
        M("pa6", "Nylon 6 (PA6, dry)", "Plastic", 1.13, 2.6, 75, 82, 0.39, 0.25, 85, 110, 1700, 2, 4, 3, 0xFFEFE8DA, 0f, 0.4f,
            "Gears, rollers, cable ties and wear parts; slightly tougher and cheaper than PA66.", "Absorbs even more water than PA66; dimensions and stiffness move."),
        M("pa6g", "Nylon 6 + 30% Glass (PA6-GF30)", "Plastic", 1.36, 8.0, N, 165, 0.34, 0.34, 30, 150, 1400, 3, 4, 3, 0xFFDBD5C8, 0f, 0.4f,
            "Structural housings, brackets and automotive parts; near-metal stiffness in plastic.", "Still absorbs water. Abrasive on tooling; strength is fiber-direction dependent."),
        M("pa12", "Nylon 12 (PA12, MJF/SLS)", "Plastic", 1.01, 1.7, 45, 50, 0.40, 0.25, 110, 110, 1700, 3, 4, 4, 0xFFEAE4D8, 0f, 0.4f,
            "Powder-bed printed parts, fuel lines and flexible components; lower moisture uptake than PA6.", "Printed properties vary with orientation and refresh rate."),
        M("pet", "PET (bottle grade)", "Plastic", 1.38, 2.9, 55, 65, 0.37, 0.24, 70, 115, 1300, 1, 4, 4, 0xFFE4EFF2, 0f, 0.7f,
            "Bottles, film, food containers and strapping; clear and stiff for the price.", "Hydrolyzes when processed wet; softens ~70 C so hot-fill needs special grades."),
        M("pbt", "PBT (unfilled)", "Plastic", 1.31, 2.5, 55, 57, 0.38, 0.25, 85, 130, 1400, 2, 4, 4, 0xFFEDEAE2, 0f, 0.45f,
            "Electrical connectors, automotive and appliance parts; stable and fast-molding.", "Hydrolyzes in hot/wet service; notch sensitive unfilled."),
        M("pcabs", "PC-ABS Blend", "Plastic", 1.12, 2.5, 55, 58, 0.37, 0.22, 70, 105, 1400, 2, 4, 4, 0xFFE8E6E0, 0f, 0.5f,
            "Housings, helmets and automotive trim; ABS processability with PC toughness and heat.", "Needs UV stabilizer outdoors; stress-cracks in some solvents."),
        M("asa", "ASA", "Plastic", 1.06, 2.3, 44, 48, 0.36, 0.17, 95, 90, 1300, 2, 4, 4, 0xFFEDE9DE, 0f, 0.45f,
            "Outdoor enclosures, automotive exterior and marine parts; the UV-stable ABS alternative.", "Same solvent weakness and print warp as ABS."),
        M("psu", "Polysulfone (PSU)", "Plastic", 1.24, 2.5, 70, 75, 0.37, 0.26, 56, 150, 1250, 4, 5, 4, 0xFFE8DCC8, 0f, 0.6f,
            "Sterilizable medical and food-service parts, membranes; amber and steam-stable.", "Stress-cracks in ketones/esters; costly."),
        M("pps", "PPS (40% glass-filled)", "Plastic", 1.64, 10.0, N, 140, 0.35, 0.30, 30, 220, 950, 4, 5, 3, 0xFF3E3A33, 0f, 0.4f,
            "Under-hood parts, pump housings, chemical and high-temperature service; metal-like stiffness.", "Brittle unfilled; abrasive; costly."),
        M("pei", "PEI (Ultem 1000)", "Plastic", 1.27, 3.1, 105, 110, 0.36, 0.22, 56, 170, 1150, 5, 5, 4, 0xFFB98D4E, 0f, 0.5f,
            "Aerospace interiors, sterilizable medical devices and high-temp electronics.", "Very costly; stress-cracks in some solvents."),
        M("uhmw", "UHMW-PE", "Plastic", 0.93, 0.9, 22, 40, 0.46, 0.41, 200, 80, 1900, 2, 5, 4, 0xFFFAF8F2, 0f, 0.3f,
            "Wear strips, chain guides, chute liners and marine fenders; nearly frictionless and unbreakable.", "Creeps badly; low stiffness and heat resistance; hard to bond."),
        M("phenolic", "Phenolic (Bakelite, filled)", "Plastic", 1.40, 7.0, N, 60, 0.30, 0.35, 30, 140, 1500, 2, 4, 3, 0xFF4A3F36, 0f, 0.4f,
            "Electrical insulation, knobs, terminal boards and jigs; the original hard thermoset.", "Brittle; cannot be remelted; abrasive dust when machined."),
        M("tpu", "TPU (95A, flexible)", "Elastomer", 1.20, 0.026, 25, 40, 0.48, 0.20, 150, 80, 1800, 2, 4, 2, 0xFF3C6EA8, 0f, 0.5f,
            "Flexible 3D prints, seals, gaskets, phone cases, wheels.", "Hard to print and machine; stiffness is very low compared with rigid plastics."),
        M("silicone", "Silicone Rubber", "Elastomer", 1.15, 0.005, N, 8, 0.48, 0.20, 250, 200, 1250, 2, 5, 1, 0xFFC25B4A, 0f, 0.6f,
            "Gaskets, seals, molds, medical tubing; stable from -50 to 200 C.", "Low tear strength; sticky and difficult to bond without primers."),
        M("nr", "Natural Rubber (60A)", "Elastomer", 0.96, 0.005, N, 20, 0.49, 0.15, 220, 70, 1900, 1, 3, 2, 0xFF2E2A26, 0f, 0.5f,
            "Vibration mounts, tires and elastic elements; the best tear and resilience per cost.", "Ozone, UV and oils destroy it; low heat limit."),
        M("nbr", "Nitrile Rubber (NBR, 70A)", "Elastomer", 1.00, 0.007, N, 14, 0.49, 0.25, 230, 100, 1900, 1, 3, 2, 0xFF2B2B2E, 0f, 0.5f,
            "Oil and fuel seals, O-rings, gaskets and hoses; the standard hydrocarbon elastomer.", "Ozone and UV crack it; swells in ketones and brake fluid."),
        M("cr", "Neoprene (CR, 60A)", "Elastomer", 1.45, 0.007, N, 15, 0.49, 0.19, 200, 100, 2000, 2, 4, 2, 0xFF32302C, 0f, 0.5f,
            "Weather seals, gaskets, wetsuits and marine parts; decent oil and weather balance.", "Densified grades sink; moderate oil resistance only."),
        M("epdm", "EPDM Rubber (70A)", "Elastomer", 1.15, 0.008, N, 14, 0.49, 0.25, 240, 120, 1900, 1, 4, 2, 0xFF35322E, 0f, 0.5f,
            "Weatherstripping, roofing, steam and brake-fluid seals; best ozone/UV resistance.", "Useless with oils and fuels - it swells."),
        M("fkm", "Fluoroelastomer (FKM, Viton, 75A)", "Elastomer", 1.85, 0.010, N, 12, 0.49, 0.20, 160, 200, 1600, 5, 5, 2, 0xFF3A3228, 0f, 0.5f,
            "Fuel system seals, chemical O-rings and high-temperature gaskets.", "Very costly for rubber; attacked by steam and amines."),

        // ---------------------------------------------------------------- composites
        M("cfrp", "Carbon Fiber / Epoxy (quasi-isotropic)", "Composite", 1.55, 70, N, 600, 0.30, 5.0, 2.0, 120, 1000, 5, 5, 2, 0xFF22252A, 0.2f, 0.85f,
            "Aerospace, racing, drones, bike frames; extreme specific stiffness and strength.", "Anisotropic (values depend on layup), brittle failure, galvanic corrosion with aluminum, expensive."),
        M("cfrp-ud", "Carbon Fiber / Epoxy (unidirectional)", "Composite", 1.60, 135, N, 1500, 0.32, 0.8, 1.0, 150, 1000, 5, 5, 2, 0xFF1E2126, 0.2f, 0.85f,
            "Spar caps, shafts and load-aligned structures; maximum strength in the fiber direction.", "Nearly all strength is along the fibers - across them it is resin-weak."),
        M("gfrp", "Glass Fiber / Epoxy (G10 / FR4)", "Composite", 1.85, 24, N, 310, 0.28, 0.3, 12.0, 130, 1100, 3, 5, 3, 0xFF8CA86B, 0f, 0.6f,
            "Circuit boards, insulators, boat hulls, structural plate; electrically insulating.", "Abrasive to tools, dust is hazardous, anisotropic."),
        M("aramid", "Aramid / Epoxy (Kevlar-49)", "Composite", 1.40, 60, N, 1200, 0.34, 0.15, -2.0, 150, 1420, 5, 4, 1, 0xFF9E8F4E, 0f, 0.5f,
            "Ballistic panels, pressure vessels and sports gear; extreme toughness per weight.", "Very weak in compression; UV degrades it; fuzzes instead of machining."),

        // ---------------------------------------------------------------- wood
        M("oak", "Red Oak (along grain)", "Wood", 0.70, 12.5, N, 99, 0.35, 0.17, 5.0, 100, 2380, 2, 2, 4, 0xFFB98B5A, 0f, 0.3f,
            "Furniture, flooring, tooling jigs, structural timber.", "Properties vary with grain direction and moisture; swells and shrinks; burns."),
        M("fir", "Douglas Fir (along grain)", "Wood", 0.53, 13.4, N, 85, 0.29, 0.14, 4.0, 100, 2300, 1, 2, 5, 0xFFD2A877, 0f, 0.25f,
            "Framing, beams, plywood, fixtures; strong for its weight and cheap.", "Weak across the grain; moisture movement; burns."),
        M("maple", "Hard Maple (along grain)", "Wood", 0.68, 12.6, N, 109, 0.40, 0.17, 5.0, 100, 1600, 3, 2, 4, 0xFFD8C49A, 0f, 0.3f,
            "Flooring, workbenches, butcher block, instrument necks; hard and fine-grained.", "Moves with humidity; burns in routing; not durable outdoors."),
        M("pine", "White Pine (along grain)", "Wood", 0.42, 8.8, N, 66, 0.40, 0.12, 5.0, 100, 1700, 1, 2, 5, 0xFFE4CF9E, 0f, 0.25f,
            "Framing, trim, patterns and shop furniture; light, cheap and forgiving to work.", "Soft - dents easily; weak; low rot resistance."),
        M("walnut", "Black Walnut (along grain)", "Wood", 0.64, 11.6, N, 100, 0.40, 0.17, 5.0, 100, 1600, 4, 3, 4, 0xFF5D432F, 0f, 0.35f,
            "Fine furniture, gunstocks, veneer and decorative parts.", "Costly; moves with humidity; dust is an irritant."),
        M("cherry", "Cherry (along grain)", "Wood", 0.56, 10.0, N, 85, 0.40, 0.16, 5.0, 100, 1600, 4, 3, 4, 0xFF9A5F43, 0f, 0.35f,
            "Furniture, cabinetry and millwork; works beautifully and darkens with age.", "Blotchy without finish control; medium durability."),
        M("teak", "Teak (along grain)", "Wood", 0.65, 11.0, N, 97, 0.40, 0.17, 5.0, 100, 1600, 5, 5, 3, 0xFF8A6B43, 0f, 0.35f,
            "Boat decks, outdoor furniture and marine trim; oily and rot-proof.", "Very costly; natural oils interfere with gluing; silica dulls tools."),
        M("bamboo", "Bamboo (laminated)", "Wood", 0.80, 14.0, N, 100, 0.35, 0.20, 4.0, 100, 1600, 3, 2, 4, 0xFFC9B070, 0f, 0.35f,
            "Flooring, panels and sustainable structures; grass-derived with hardwood stiffness.", "Moisture and mold sensitive; quality varies with the adhesive."),
        M("plywood", "Birch Plywood (panel)", "Wood", 0.68, 10.5, N, 55, 0.35, 0.15, 6.0, 100, 1700, 2, 2, 4, 0xFFD6BD8E, 0f, 0.3f,
            "Jigs, crates, formwork and furniture; stable sheet stock with real strength.", "Edges split; non-marine glue delaminates when wet."),
        M("mdf", "MDF (medium-density fiberboard)", "Wood", 0.75, 4.0, N, 40, 0.30, 0.14, 8.0, 90, 1700, 1, 1, 3, 0xFFBFA87E, 0f, 0.3f,
            "Cabinets, fixtures, templates and paint-grade parts; flat, cheap and uniform.", "Swells permanently in water; weak edges; formaldehyde dust when cut."),

        // ---------------------------------------------------------------- ceramics and stone
        M("glass", "Glass (soda-lime)", "Ceramic", 2.50, 70, N, 50, 0.22, 1.0, 9.0, 460, 840, 1, 5, 1, 0xFFBFE0E8, 0f, 1.0f,
            "Windows, bottles, lenses, covers; hard, transparent and chemically inert.", "Brittle: shatters from tensile stress, thermal shock and edge flaws."),
        M("borosil", "Borosilicate Glass", "Ceramic", 2.23, 63, N, 65, 0.20, 1.1, 3.3, 500, 830, 3, 5, 1, 0xFFCFE8EC, 0f, 1.0f,
            "Labware, sight glasses, ovenware and chemical process parts; survives thermal shock.", "Brittle; costly versus soda-lime."),
        M("fused-silica", "Fused Silica (quartz)", "Ceramic", 2.20, 72, N, 55, 0.17, 1.4, 0.5, 1000, 740, 5, 5, 1, 0xFFE8F0F2, 0f, 1.0f,
            "Optics, semiconductor parts and high-temperature windows; almost zero expansion.", "Very costly; brittle; diamond tooling only."),
        M("alumina", "Alumina Ceramic (96%)", "Ceramic", 3.75, 300, N, 300, 0.22, 25.0, 8.1, 1500, 880, 3, 5, 1, 0xFFEFEBE0, 0f, 0.7f,
            "Wear parts, insulators, substrates, nozzles; very hard and heat resistant.", "Brittle; must be ground with diamond tools; sensitive to impact and thermal shock."),
        M("zirconia", "Zirconia (Y-TZP)", "Ceramic", 6.05, 210, N, 900, 0.30, 2.0, 10.5, 900, 400, 5, 5, 1, 0xFFF2EFE8, 0f, 0.75f,
            "Knife edges, bearings, dental crowns and wear parts; the tough ceramic.", "Very costly; strength is flexural; ages in steam."),
        M("sic", "Silicon Carbide", "Ceramic", 3.20, 410, N, 450, 0.16, 120, 4.0, 1350, 670, 5, 5, 1, 0xFF4A4E55, 0f, 0.6f,
            "Seals, abrasives, armor and semiconductor parts; extreme stiffness and heat capacity.", "Very brittle; diamond machining only; costly."),
        M("porcelain", "Porcelain", "Ceramic", 2.40, 70, N, 60, 0.20, 1.5, 6.0, 1000, 900, 2, 5, 1, 0xFFF5F3EC, 0f, 0.8f,
            "Insulators, sanitaryware and tiles; hard, white and weatherproof.", "Brittle; glaze crazes; chips at edges."),
        M("concrete", "Concrete (structural, ~C30)", "Ceramic", 2.40, 30, N, 3.5, 0.18, 1.7, 10.0, 300, 880, 1, 4, 1, 0xFF9A9A95, 0f, 0.15f,
            "Foundations, machine bases, dams and structural elements; cheap bulk strength in compression.", "Tensile strength is ~10% of compressive - needs rebar. Cracks, creeps and spalls in fire."),
        M("granite", "Granite", "Ceramic", 2.65, 60, N, 15, 0.25, 2.8, 7.0, 300, 880, 3, 5, 1, 0xFF8E8A84, 0f, 0.5f,
            "Surface plates, machine bases and countertops; stiff, stable and wear-proof.", "Brittle; quartz dust is a silicosis hazard when cut."),
        M("marble", "Marble", "Ceramic", 2.70, 55, N, 12, 0.25, 2.5, 8.0, 300, 880, 3, 3, 2, 0xFFE8E4DC, 0f, 0.6f,
            "Sculpture, cladding and countertops; softer and more workable than granite.", "Acids etch it (calcite); veins crack; less durable than granite."),

        // ---------------------------------------------------------------- fabric and soft goods
        M("leather", "Leather (vegetable-tanned)", "Fabric and Soft", 0.95, 0.10, N, 25, 0.40, 0.15, 60, 80, 1500, 3, 2, 5, 0xFF8A5A33, 0f, 0.45f,
            "Upholstery, straps, bellows and drive belts; tough natural sheet.", "Not for CAE - viscoelastic and highly variable. Moisture and stretch change it."),
        M("canvas", "Cotton Canvas (duck)", "Fabric and Soft", 0.60, 0.30, N, 40, 0.30, 0.07, 50, 120, 1300, 1, 1, 5, 0xFFC7BBA3, 0f, 0.4f,
            "Covers, bags, tents and workwear; cheap heavy woven sheet.", "Rots when wet; shrinks; not for CAE."),
        M("pu-foam", "Polyurethane Foam (medium density)", "Fabric and Soft", 0.05, 0.010, N, 0.2, 0.30, 0.03, 100, 90, 1400, 1, 2, 5, 0xFFF5ECD2, 0f, 0.25f,
            "Seats, cushions, packaging and acoustic panels; bulk at almost no mass.", "Burns readily; crumbles at edges; not for CAE."),
        M("cardboard", "Corrugated Cardboard", "Fabric and Soft", 0.20, 0.50, N, 8, 0.30, 0.07, 30, 80, 1300, 1, 1, 5, 0xFFC8A06B, 0f, 0.3f,
            "Packaging, mockups and prototype form studies.", "Collapses when wet; no structural use; not for CAE."),
    ];

    public static IEnumerable<string> Categories => All.Select(m => m.Category).Distinct();
    static readonly Dictionary<string, Material> ById = All.ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);
    public static Material? Find(string? id) => id != null && ById.TryGetValue(id, out var m) ? m : null;
    public static Material Default => ById["a36"];

    // library extremes, used to scale the comparison bars in the panel
    public static double Max(Func<Material, double> f) => All.Select(f).Where(v => !double.IsNaN(v)).Max();
}
