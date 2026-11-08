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

**Not in the first version (proposed):** curved or rolled bends, forming features such as louvres and ribs, multi-piece sheets, lap or welded seams, and automatic corner relief.

**Open decisions for the owner**
- One connected sheet per part, or several pieces?
- Mark bends as special lines in the flat sketch, or with a Bend tool that picks ordinary lines? (Proposed: the second.)
- Should Flat Pattern also learn to unfold bodies with non-parallel bends, or only return the source flat for parts made flat-first?
- Should slots and rounded corners unfold on bodies made with Flange ([#78](https://github.com/chrizpro/prism/issues/78))?
- Which of the other candidates in #81 (a separate hem, an unfoldable corner treatment, unfolding one bend) are wanted?
- Should thickness, K-factor and minimum bend radius belong to the material? Library and custom materials carry mechanical and thermal data but nothing sheet-specific today.

## Verification

[Sheet metal tests](../../../Prism.Tests/Headless/SheetMetal/SheetMetalTests.cs) check Flange volume and bounds for both metal sides, width direction (forward, reversed, centred), hem, a two-bend channel and rejected profiles; Flat Pattern size and bend allowance for three K-factors, placement and the no-bend error; and Flatten / Refold on the documented radius 25 × 70 mm cylinder for each Create choice. Run `./tools/test.ps1 -Feature SheetMetal`. [Flatten and Refold](../../flatten-refold.md) also has its standalone suite.

Windows verification on 2026-11-05: the combined candidate passed 86 Unit and 592 Headless tests with rebuilt native libraries. See the [integration verification report](../../engineering/release-readiness-2026-11-05.md) for source revisions, commands and limits.

The maintained Integration check [GAL-I006](../../../Prism.Tests/Integration/DesktopGalleryTests.cs) opens the Fold gallery model and verifies its through-holes and its flat pattern; it needs the configured Windows workstation.
