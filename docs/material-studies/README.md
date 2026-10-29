# Materials and appearance improvements

Implemented and exercised in the actual Prism application through its MCP bridge, 2026-10-27. These are application screenshots and renderer output, not generated mockups.

## Appearance

- Procedural wood with growth bands, long grain and pores; brushed striations, cast finish and carbon weave. Automatic finish follows the material; highly polished metals use Plain. Plain and explicit overrides are available.
- Pattern size is in model millimeters. Angle rotates the pattern about world Z. Coordinates are anchored to each body's bounding-box minimum, so translations preserve the finish. Rotations and shape edits can require adjusting the grain angle/scale. This is not UV mapping or a fully body-local texture transform.
- No texture bitmaps, additional tessellation or per-vertex texture buffers. GPU work is shader arithmetic and a few uniforms per draw. Performance has not been profiled across GPUs.
- Viewport studio reflections distinguish metal from plastic. Bare metal library defaults use metallic=1; nonmetals use 0. Cosmetic overrides never change engineering data.
- CPU rendering now uses a continuous metallic/roughness GGX reflection model, Fresnel response, energy-weighted diffuse lighting and multiple-importance sampling for direct and reflected studio lights. Materials no longer switch abruptly between plastic and metal at 0.5 metallic.
- CPU renders use the same procedural pattern definitions at full detail. Viewport fine grain is filtered using screen-space derivatives. Rendered metals reflect scene objects and studio lighting; the viewport uses an inexpensive studio approximation.
- This remains a small CPU renderer: no denoiser, transmission/refraction, photographic texture library, measured anisotropic metal BRDF, clear coat or subsurface model. Glass is currently an opaque finish. Brushed striations do not implement anisotropic reflection.

The material browser keeps appearance controls ahead of collapsible library engineering data. A separate expander shows the properties actually assigned to the selected body. The source/estimate status is visible for each library choice.

## Engineering data

All 37 entries are reference data, not certified design allowables. A subset has been checked against the primary sources below; the remainder are explicitly marked as estimates. Temperature-dependent curves and orthotropic tensors are not implemented.

| Grade | Checked/corrected | Basis |
|---|---|---|
| C93200 cast bronze | Density **8.91 g/cm³**, E **100 GPa**, typical yield/UTS **124/241 MPa**, conductivity **58.2 W/(m·K)**, expansion **18 µm/(m·K)**, heat capacity **377 J/(kg·K)** | CDA physical table, converted from US units; CDA typical casting strength table. Strength depends on casting process. |
| 304 / EN 1.4301 cold-rolled sheet | Density **7.90 g/cm³**, E **200 GPa**, proof strength **230 MPa**, lower tensile bound **540 MPa** of a 540–750 range, k **15 W/(m·K)**, expansion **16 µm/(m·K)**, cp **500 J/(kg·K)** | Outokumpu Core, tables 5 and 7. These cold-rolled values must not be silently applied as plate-condition allowables. |
| 6061-T6/T651 | Density **2.70**, yield/UTS **276/310 MPa**, E **68.9 GPa** rounded from **10 Mpsi**, k **167 W/(m·K)** at 20 °C, expansion **23.6** averaged 20–100 °C, cp **896 J/(kg·K)** at **100 °C** | Kaiser datasheet. Its printed 68.3 GPa parenthetical is inconsistent with 10 Mpsi; the conversion is documented. |
| Oak / Douglas fir | Existing numeric estimates retained; strength relabeled **bending modulus of rupture**, not tensile or yield | USDA terminology and orthotropic model requirements checked. Numeric species/moisture sets have not been independently verified. |

Poisson ratios and service-temperature limits for the three checked metal entries remain estimates. Qualitative cost, corrosion and machining ratings remain approximate. Service limits are not code-based design limits.

Sources:

- [Copper Development Association C93200](https://alloys.copper.org/alloy/C93200)
- [CDA cast copper alloys, typical casting strengths](https://www.copper.org/publications/pub_list/pdf/7014.pdf)
- [Outokumpu Core datasheet](https://www.outokumpu.com/en/products/product-ranges/-/media/files/products/core/outokumpu-core-range-datasheet.pdf)
- [Kaiser 6061 sheet, coil and plate](https://online.kaiseraluminum.com/depot/PublicProductInformation/Document/1015/Kaiser_Aluminum_6061_Sheet_Coil_and_Plate.pdf)
- [USDA Wood Handbook](https://www.fpl.fs.usda.gov/documnts/fplgtr/fplgtr282/fpl_gtr282.pdf)
- [PBRT microfacet reflection reference](https://pbr-book.org/4ed/Reflection_Models/Roughness_Using_Microfacet_Theory)

## Persistence and solver behavior

- New assignments carry a physical-property snapshot, including condition, provenance and revision. Saving an older document also captures its resolved material values. Future library edits cannot silently change that saved property set. Legacy files that contain only material IDs still load using the library and compatible appearance defaults.
- Color, metallic, gloss and pattern are independent of the snapshot. Mass and solvers use the saved physical data. Removing a material clears both the ID and its snapshot.
- Structural studies now require an assigned material instead of silently falling back to A36.
- Wood and generic fiber laminates require directional models, and elastomers require a suitable nonlinear structural model; the current isotropic structural solver rejects these unsupported sets. Isotropic thermal runs also reject wood/laminates until directional conductivity is supported.
- Materials without yield data can still support appropriate linear elastic calculations, but no yield-based factor of safety is invented from tensile/flexural strength. The UI/API report it as unavailable, and safety-factor contours are rejected. This does not add a brittle-failure criterion.
- CalculiX summaries include the actual engineering property set for reproducibility.

## Validation and artifacts

- `tests/Prism.MaterialTests`: **100 checks** covering every library entry's complete physical-data roundtrip (including undefined yield), custom pinned data, legacy files, finish coordinates/orientation, unsupported solver models and a real brittle-material static solution without a false safety factor.
- `tests/Prism.RenderTests`: **23 checks**, including assigned colors, finish persistence, exact white backgrounds without ground, premultiplied transparent silhouettes, completion, cancellation and PNG export. Numerical white-furnace integrations bound reflected energy at normal/grazing views for metal and dielectric surfaces; continuity across metallic=0.5 and finite polished-metal grazing behavior are checked.
- `tests/Prism.CaeTests`: **19 checks** including analytic conduction, transient conduction, modal frequencies and existing static displacement.
- `material-gallery.prism`: oak, stainless steel, copper, ABS, carbon and aluminum, on slabs and spheres. Sphere gloss intentionally varies to show rough and polished finishes.
- `viewport.png`: actual viewport. `gallery-studio.png`: actual HD studio render (1280 × 720, 512 samples, 5 bounces).
- Final HD gallery completed in **146.4 seconds** on this machine. This is a single scene/run, not a general performance benchmark.
- Live MCP checks additionally exercised invalid finish inputs, clear/reassign, and the actual ribbon's Brushed/Polished buttons. Both finish buttons preserved the assigned engineering property set. Evidence is in `live-validation.json`, `live-material-probe.json` and `live-finish-presets.json`.
- Initial comparison images are retained as iteration evidence; `gallery-studio-initial.png` uses the superseded, overly broad wood bands.

Builds retain existing unrelated compiler warnings and the existing desktop NuGet vulnerability warning for Tmds.DBus.Protocol 0.21.2. No dependencies were added for this work.
