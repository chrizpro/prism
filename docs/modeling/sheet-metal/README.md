# Sheet Metal

- **Select, Datum:** shared sections
- **References:** Create Sketch, Construction Plane
- **Sheet metal:** Flange, Flat Pattern
- **Cut-outs:** Extrude, Hole, Fillet / Chamfer (dropdown)
- **Transform, Inspect, Appearance:** shared sections

## Observed limitations (2026-11-08)

Findings from building the [Fold gallery study](../../interface/gallery/README.md#curated-collection), recorded as observed behavior, not as intended requirements:

- **Flat Pattern** unfolded a Flange with a hem, round holes (in flat and tilted walls), rectangular windows and chamfered corners. It rejected the same part with a slot or a rounded (filleted) corner, reporting "the bends are not all parallel" ([#78](https://github.com/chrizpro/prism/issues/78)).
- **Cut-outs from a face sketch:** Extrude Cut can point away from the metal and then removes nothing without an error, as seen on a tilted wall. Reversing the direction cut the expected volume ([#79](https://github.com/chrizpro/prism/issues/79)).
- A Flange makes every bend from one profile sketch, and Flat Pattern needs the bends to be parallel.

## Proposed: flat-first sheet metal

**Proposed, awaiting owner review. Nothing in this section is approved behavior, and none of it is implemented.** It comes from the owner's idea of 2026-11-08, tracked in [#81](https://github.com/chrizpro/prism/issues/81). Tool names are working names.

**Purpose.** Describe a sheet-metal part as the flat shape plus where it bends, then fold it, instead of drawing the folded cross-section as Flange requires. This would cover parts bent in more than one direction (trays, enclosures, chassis), which Flange cannot make and Flat Pattern cannot unfold today. A companion **Edge Flange** would extrude a flange from an edge of an existing sheet-metal body.

**Proposed behavior (first version)**
- A closed flat outline sketch plus a thickness makes a flat sheet body. Holes, slots, corner reliefs and rounded corners are drawn in the flat sketch.
- A Bend step takes straight sketch lines that cross the whole sheet. Each gets an angle (up or down, up to 180° for a hem), an inside radius and a K-factor (default 0.44, as in Flat Pattern). The user picks the region that stays fixed. Regions between bend lines stay flat and each bend becomes a cylindrical strip, with length across a bend set by the same bend allowance Flat Pattern uses.
- Editing a sketch dimension or a bend value regenerates the folded part through the design history.
- Edge Flange takes an edge of a sheet-metal body and adds a flange of a given length, angle and inside radius.
- Invalid input gives an explicit error and no partial body: a bend line that does not cross the sheet, bend lines that cross each other inside the material, a disconnected region, or a radius too large for the geometry. Collisions between folded flanges are found with Interference.

**Acceptance criteria (proposed)**
1. Folding a cross-shaped flat sketch with four 90° bends gives one valid solid tray whose wall heights and inside radii match the inputs.
2. For a part folded from a flat sketch, its flat pattern has the sketch's outline size to within 0.01 mm at the same K-factor (fold, then Flat Pattern, returns the original).
3. Holes and slots drawn on the flat appear with the same size on the folded part and in its flat pattern.
4. A 180° bend with a small gap produces a hem. Changing a dimension or angle regenerates the part.
5. Invalid input is rejected with a message naming the cause.

**Not in the first version (proposed):** forming features such as louvres and ribs, multi-piece sheets, lap or welded seams, automatic corner relief, and genuinely conical or curved-crease bends (see below).

### Proposed: curved bends from a sketch

Idea: a bend is a **zone** that maps a strip of the flat sheet to 3D without stretching its neutral surface, so the straight bend is only the simplest zone type. Curves then come in tiers that reuse the same fold:

1. **Roll band (curved profile, straight rulings).** The user marks a band between two straight lines in the flat sketch and draws its side-view profile as an ordinary sketch curve (arc, spline, any planar curve). The band's flat width is a derived reference dimension equal to the neutral length of that profile, so editing the profile moves everything attached to the band's edges. A circular arc is exactly one bend (radius R, angle Θ). A spline is split into tangent-continuous arcs within a chosen tolerance, each arc one zone, or into N small straight bends when the user prefers facets.
2. **Cone roll (radial rulings).** The flat sketch holds an annular sector. A sector of angle φ becomes a cone of half-angle α where sin α = φ / 2π, so one number fixes the shape and the tool can solve either way (give α, get φ). It is closed form and stretch-free by construction.
3. **Anything else** (general developable surfaces) would need a true isometric flattening solver, and is out of scope here.

A curved crease cannot keep both sides flat, so a curved bend line always means at least one side becomes curved; the tool would say which side. Flanges around holes and other stretched shapes are forming, not bending.

**What a throwaway prototype showed (2026-11-08, not committed; Prism 0.1.0-alpha.2 Linux native libraries with current `develop` managed code):**
- Prism's Flat Pattern follows θ·(r + K·t) per bend with setbacks r·tan(θ/2) on the inside line, from the inside surface. This reproduced a 90° bend at K = 0.33 and 0.44 exactly and the Fold stand's 206.1838 mm blank to four decimals (hem as a 180° bend of radius gap/2).
- Folding Prism's own U-channel blank (120.1894 mm) with a 70-line prototype gave an inner width of 60.000 and inner leg heights of 30.000, as Flange had drawn.
- A four-bend tray folded from a cross gives inside depth 33.000 (r + wall) and inside size 106 × 66 mm, matching closed form, with a corner gap of ρ√2 = 5.487 mm between neighbouring walls, the number a relief rule would use.
- One bend of radius 80 mm and 90° unfolds in Flat Pattern to 247.0460 mm, the same as the formula, so a circular arc is already one ordinary bend.
- A chain of N equal adjacent bends equals one smooth arc to about 1e-13 mm. A spline as N straight facets (radius 0.5 mm) gave blank-length errors of −1.444, −0.363, −0.091 and −0.023 mm for N = 3, 6, 12 and 24.
- **Prism's Flatten of a cone is not a true development.** It returns an area-preserving UV rectangle (138.227 mm, the circumference at the mean radius) with the warning "lengths and area may stretch", while the true layout of that frustum is an annular sector of 147.73°. So the existing Flatten cannot yet certify a cone as manufacturable; the closed-form cone zone above could.

**Open decisions for the owner**
- One connected sheet per part, or several pieces?
- Mark bends as special lines in the flat sketch, or with a Bend tool that picks ordinary lines? (Proposed: the second.)
- Should Flat Pattern also learn to unfold bodies with non-parallel bends, or only return the source flat for parts made flat-first?
- Should slots and rounded corners unfold on bodies made with Flange ([#78](https://github.com/chrizpro/prism/issues/78))?
- Which of the other candidates in #81 (a separate hem, an unfoldable corner treatment, unfolding one bend) are wanted?
- How should curved bends be offered: roll band first with arcs and facets, or all tiers together? Should Flatten gain an analytic cone layout like its cylinder layout?
- Should thickness, K-factor and minimum bend radius belong to the material? Library and custom materials carry mechanical and thermal data but nothing sheet-specific today.

## Verification

[Sheet metal tests](../../../Prism.Tests/Headless/SheetMetal/SheetMetalTests.cs) check Flange volume and bounds for both metal sides, width direction (forward, reversed, centred), hem, a two-bend channel and rejected profiles; Flat Pattern size and bend allowance for three K-factors, placement and the no-bend error; and Flatten / Refold on the documented radius 25 × 70 mm cylinder for each Create choice. Run `./tools/test.ps1 -Feature SheetMetal`. [Flatten and Refold](../../flatten-refold.md) also has its standalone suite.

Windows verification on 2026-11-05: the combined candidate passed 86 Unit and 592 Headless tests with rebuilt native libraries. See the [integration verification report](../../engineering/release-readiness-2026-11-05.md) for source revisions, commands and limits.

The maintained Integration check [GAL-I006](../../../Prism.Tests/Integration/DesktopGalleryTests.cs) opens the Fold gallery model and verifies its through-holes and its flat pattern; it needs the configured Windows workstation.
