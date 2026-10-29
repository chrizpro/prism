namespace Prism.Core;

/// <summary>Property provenance and model limits, independent of cosmetic appearance.</summary>
public sealed record MaterialEvidence(string Revision, string Condition, string Source, string CheckedProperties,
    string StrengthBasis = "Tensile strength", bool IsotropicStructural = true, bool IsotropicThermal = true)
{
    public static MaterialEvidence For(string id) => id switch
    {
        "6061" => new("2026-10-27.1", "6061-T6/T651 typical values. Conductivity at 20 °C; expansion averaged 20–100 °C; specific heat at 100 °C. E rounded from 10 Mpsi to 68.9 GPa. Poisson ratio and service limit remain estimates.",
            "https://online.kaiseraluminum.com/depot/PublicProductInformation/Document/1015/Kaiser_Aluminum_6061_Sheet_Coil_and_Plate.pdf", "Density, E (unit conversion), yield, tensile, conductivity, expansion, specific heat"),
        "304" => new("2026-10-27.1", "EN 1.4301 cold-rolled sheet: 230 MPa proof strength and 540 MPa lower tensile bound (range 540–750). Physical properties at 20 °C; expansion 20–100 °C. Poisson ratio and service limit remain estimates.",
            "https://www.outokumpu.com/en/products/product-ranges/-/media/files/products/core/outokumpu-core-range-datasheet.pdf", "Density, E, proof strength, tensile range, conductivity, expansion, specific heat"),
        "bronze" => new("2026-10-27.1", "C93200 cast bronze. Typical values; casting process affects strength. Physical units converted from CDA tables; Poisson ratio and service limit remain estimates.",
            "https://alloys.copper.org/alloy/C93200 ; https://www.copper.org/publications/pub_list/pdf/7014.pdf", "Density, E, conductivity, expansion, specific heat; typical cast strength"),
        "oak" or "fir" => new("2026-10-27.1", "Along-grain clear-wood estimates near 12% moisture. Strength is bending modulus of rupture, not tensile or yield strength. Directional properties are required for CAE.",
            "https://www.fpl.fs.usda.gov/documnts/fplgtr/fplgtr282/fpl_gtr282.pdf", "Strength terminology and orthotropic model requirement; numeric set remains an estimate", "Bending strength (MOR)", false, false),
        "cfrp" or "gfrp" => new("2026-10-27.1", "Generic laminate estimates. Layup, fiber fraction and direction are unspecified; an orthotropic property set is required for CAE.", "No grade-specific source recorded", "Not independently verified", id=="gfrp"?"Flexural strength estimate":"Nominal tensile estimate", false, false),
        "tpu" or "silicone" => new("2026-10-27.1", "Generic elastomer estimates. Linear isotropic structural analysis is not an appropriate constitutive model.", "No grade-specific source recorded", "Not independently verified", "Nominal tensile estimate", false),
        "alumina" => new("2026-10-27.1", "Generic 96% alumina estimates; strength is flexural and flaw-dependent, not a yield limit.", "No supplier grade recorded", "Not independently verified", "Flexural strength estimate"),
        _ => new("2026-10-27.1", "Generic reference estimates near room temperature; actual grade, temper, section, process and temperature can change properties. Service temperature and qualitative ratings are approximate.", "No grade-specific source recorded", "Not independently verified")
    };
}
