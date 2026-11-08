# Sheet Metal

- **Select, Datum:** shared sections
- **References:** Create Sketch, Construction Plane
- **Sheet metal:** Flange, Fold, Flat Pattern
- **Cut-outs:** Extrude, Hole, Fillet / Chamfer (dropdown)
- **Transform, Inspect, Appearance:** shared sections

## Fold

**Status: implemented (first version, straight bends), 2026-11-08.** The owner proposed the idea of drawing the flat sheet and marking where it bends, then asked on 2026-11-08 to proceed and self-approve provided it was "not too radical". The first version below was approved under that delegation and can still be revised by the owner. Delivery: [#81](https://github.com/chrizpro/prism/issues/81). Further work is under *Proposed*.

**Purpose.** Make sheet-metal parts by describing the flat blank and where it bends, instead of the folded cross-section Flange needs. That covers parts bent in more than one direction (trays, boxes, brackets), which Flange cannot make. Flange stays the quick tool for one-direction parts such as brackets and channels.

**How it works**
1. Sketch the flat outline with normal lines. Holes, slots and rounded corners drawn inside or on the outline are part of the sheet.
2. Draw each bend as a **construction line** across the sheet in the same sketch. The line is treated as infinite and is the middle of the bend.
3. **Sheet Metal › Fold**: pick the sketch and set Thickness, Bend radius (inside), K-factor (default 0.44, as in Flat Pattern), Bend angle (default 90°) and which side of the sketch plane the metal lies on.
4. Bends are numbered left to right, then bottom to top. **Angle of each bend** takes signed degrees in that order, for example `90, -90, 180`; an empty entry keeps the default. Positive folds toward the sketch normal, negative the other way; up to 180°.

**Behavior**
- A bend line is the middle of a **bend zone** as wide as the bend allowance θ·(r + K·t), with K measured from the inside surface, the formula Flat Pattern uses. The zone becomes an exact cylindrical strip. The flat regions between zones stay rigid.
- The largest flat region stays where it was drawn; the others fold from it one bend at a time.
- Parts bent in several directions work. Hems are 180° bends.
- A zone must be a clean rectangle of metal. Bends may meet outside the outline but not inside it, a bend may not pass through a hole, slot or notch, and every flat region must be joined to the rest by a bend. Corner reliefs are drawn in the sketch (a plus-shaped blank leaves the corners open); Fold does not add them.
- Errors name the cause and make no body: no construction lines, a bend that does not cross the sheet, bends that cross inside the sheet, a bend through a hole or notch or without metal on one side, bends that form a closed loop, regions not joined by a bend, an angle that is not a number or outside 0.1° to 180°, more angles than bends, or flanges that fold into each other.
- The sketch stays in the document and is the part's flat pattern.

**Acceptance criteria**
1. A cross-shaped blank with four 90° bends folds into one valid solid tray whose bounding box and volume match the closed-form values (verified by FLD-H003).
2. A one-bend blank folds into the same solid, by volume and size, that Flange builds from the matching profile (FLD-H001), and Flat Pattern on it returns the blank's length and width to 0.001 mm (FLD-H002).
3. A hole drawn on a flat region is a hole in the folded part, of the same size (FLD-H004).
4. Signed angles fold bends opposite ways (FLD-H005).
5. Invalid input is rejected with a message naming the cause (FLD-H006, FLD-H007).
6. The bend allowance and the placement of a folded region follow the arithmetic in FLD-U001 to FLD-U003, including that a chain of equal adjacent bends equals one arc.

**Limits of the first version**
- Flat Pattern on a part with bends in more than one direction still refuses it ("not all parallel"). The sketch is that part's flat pattern; saving it as DXF is separate work.
- Bend lines are straight. Curved bends, forming features (louvres, ribs), multi-piece sheets and lap or welded seams are not covered.
- There is no live preview handle or per-bend table yet: per-bend angles are typed as a list.
- Fold checks a bend zone and the joined solid, not every possible collision between folded flanges; use Interference.

## Observed limitations (2026-11-08)

Findings from building the [tablet-stand gallery study](../../interface/gallery/README.md#curated-collection), recorded as observed behavior, not as intended requirements:

- **Flat Pattern** unfolded a Flange with a hem, round holes (in flat and tilted walls), rectangular windows and chamfered corners. It rejected the same part with a slot or a rounded (filleted) corner, reporting "the bends are not all parallel" ([#78](https://github.com/chrizpro/prism/issues/78)). Slots and rounded corners drawn on a flat sketch for Fold are not affected.
- **Cut-outs from a face sketch:** Extrude Cut can point away from the metal and then removes nothing without an error, as seen on a tilted wall. Reversing the direction cut the expected volume ([#79](https://github.com/chrizpro/prism/issues/79)).
- A Flange makes every bend from one profile sketch, and Flat Pattern needs the bends to be parallel.

## Proposed: further sheet-metal work

**Proposed, awaiting owner review. Nothing in this section is approved behavior or implemented.** Tracked in [#81](https://github.com/chrizpro/prism/issues/81). Tool names are working names.

- **Edge Flange:** take an edge of a sheet-metal body and extrude a flange from it with a length, angle and inside radius.
- **Flat Pattern for folded parts:** unfold bodies with bends in more than one direction, or return the source flat for parts made with Fold; and let slots and rounded corners unfold on Flange bodies (#78).
- **Curved bends** (below).
- **Per-bend editing:** a table or on-screen handles for each bend's angle, direction and radius instead of a typed list.

### Proposed: curved bends from a sketch

Idea: a bend is a **zone** that maps a strip of the flat sheet to 3D without stretching its neutral surface, so the straight bend is only the simplest zone type. Curves then come in tiers that reuse the same fold:

1. **Roll band (curved profile, straight rulings).** The user marks a band between two straight lines in the flat sketch and draws its side-view profile as an ordinary sketch curve (arc, spline, any planar curve). The band's flat width is a derived reference dimension equal to the neutral length of that profile, so editing the profile moves everything attached to the band's edges. A circular arc is exactly one bend (radius R, angle Θ). A spline is split into tangent-continuous arcs within a chosen tolerance, each arc one zone, or into N small straight bends when the user prefers facets.
2. **Cone roll (radial rulings).** The flat sketch holds an annular sector. A sector of angle φ becomes a cone of half-angle α where sin α = φ / 2π, so one number fixes the shape and the tool can solve either way (give α, get φ). It is closed form and stretch-free by construction.
3. **Anything else** (general developable surfaces) would need a true isometric flattening solver, and is out of scope here.

A curved crease cannot keep both sides flat, so a curved bend line always means at least one side becomes curved; the tool would say which side. Flanges around holes and other stretched shapes are forming, not bending.

**What a throwaway prototype showed before Fold was built (2026-11-08; Prism 0.1.0-alpha.2 Linux native libraries with current `develop` managed code):**
- Prism's Flat Pattern follows θ·(r + K·t) per bend with setbacks r·tan(θ/2) on the inside line, from the inside surface. This reproduced a 90° bend at K = 0.33 and 0.44 exactly and the Fold stand's 206.1838 mm blank to four decimals (hem as a 180° bend of radius gap/2).
- Folding Prism's own U-channel blank (120.1894 mm) with a 70-line prototype gave an inner width of 60.000 and inner leg heights of 30.000, as Flange had drawn.
- A four-bend tray folded from a cross gives inside depth 33.000 (r + wall) and inside size 106 × 66 mm, matching closed form, with a corner gap of ρ√2 = 5.487 mm between neighbouring walls, the number a relief rule would use.
- One bend of radius 80 mm and 90° unfolds in Flat Pattern to 247.0460 mm, the same as the formula, so a circular arc is already one ordinary bend.
- A chain of N equal adjacent bends equals one smooth arc to about 1e-13 mm. A spline as N straight facets (radius 0.5 mm) gave blank-length errors of −1.444, −0.363, −0.091 and −0.023 mm for N = 3, 6, 12 and 24.
- **Prism's Flatten of a cone is not a true development.** It returns an area-preserving UV rectangle (138.227 mm, the circumference at the mean radius) with the warning "lengths and area may stretch", while the true layout of that frustum is an annular sector of 147.73°. So the existing Flatten cannot yet certify a cone as manufacturable; the closed-form cone zone above could.

**Open decisions for the owner**
- Should Fold keep bends as construction lines, or move to a Bend tool that picks ordinary lines and gives each its own settings? (The first version used construction lines because they need no new selection mode.)
- Should Flat Pattern learn to unfold non-parallel bends, or only return the source flat for parts made with Fold?
- Should slots and rounded corners unfold on bodies made with Flange (#78)?
- Which of the other candidates in #81 (a separate hem, an unfoldable corner treatment, unfolding one bend) are wanted?
- How should curved bends be offered: roll band first with arcs and facets, or all tiers together? Should Flatten gain an analytic cone layout like its cylinder layout?
- Should the choice of the fixed region be changeable (it is the largest region today)?
- Should thickness, K-factor and minimum bend radius belong to the material? Library and custom materials carry mechanical and thermal data but nothing sheet-specific today.

## Verification

[Sheet metal tests](../../../Prism.Tests/Headless/SheetMetal/SheetMetalTests.cs) check Flange volume and bounds for both metal sides, width direction (forward, reversed, centred), hem, a two-bend channel and rejected profiles; Flat Pattern size and bend allowance for three K-factors, placement and the no-bend error; and Flatten / Refold on the documented radius 25 × 70 mm cylinder for each Create choice. [Fold tests](../../../Prism.Tests/Headless/SheetMetal/FoldTests.cs) (FLD-H001 to FLD-H007) check Fold against Flange, the closed-form tray, holes, signed angles, the Flat Pattern round trip and the rejections, and [Fold arithmetic tests](../../../Prism.Tests/Unit/SheetMetal/SheetFoldTests.cs) (FLD-U001 to FLD-U003) check the bend allowance and region placement without the kernel. Run `./tools/test.ps1 -Feature SheetMetal`. [Flatten and Refold](../../flatten-refold.md) also has its standalone suite.

Windows verification on 2026-11-05: the combined candidate passed 86 Unit and 592 Headless tests with rebuilt native libraries. See the [integration verification report](../../engineering/release-readiness-2026-11-05.md) for source revisions, commands and limits.

The maintained Integration check [GAL-I006](../../../Prism.Tests/Integration/DesktopGalleryTests.cs) opens the Fold gallery model and verifies its through-holes and its flat pattern; it needs the configured Windows workstation.

Fold verification on 2026-11-08, Linux cloud container with the v0.1.0-alpha.2 Linux native libraries: FLD-H001 to FLD-H007 and FLD-U001 to FLD-U003 passed, and the full Headless suite passed except six tests that fail independently of this change (Grease Pencil and Eraser have no hover-help topic; four loft tests need a native library newer than alpha.2). The same part folded through the running app by MCP and cadkit gave a valid solid of the closed-form size and volume. Not run: the Windows desktop, the native rebuild.
