# Sheet Metal

- **Select, Datum:** shared sections
- **References:** Create Sketch, Construction Plane
- **Sheet metal:** Flange, Fold, Edge Flange, Flat Pattern
- **Cut-outs:** Extrude, Hole, Fillet / Chamfer (dropdown)
- **Transform, Inspect, Appearance:** shared sections

## Fold

**Status: implemented, 2026-11-08.** The owner proposed the idea of drawing the flat sheet and marking where it bends, then asked on 2026-11-08 to proceed and self-approve provided it was "not too radical". The behavior below was approved under that delegation and can still be revised by the owner. Delivery: [#81](https://github.com/chrizpro/prism/issues/81). Further work is under *Proposed*.

**Purpose.** Make sheet-metal parts by describing the flat blank and where it bends, instead of the folded cross-section Flange needs. That covers parts bent in more than one direction (trays, boxes, brackets), which Flange cannot make. Flange stays the quick tool for one-direction parts such as brackets and channels.

**How it works**
1. Sketch the flat outline with normal lines. Holes, slots and rounded corners drawn inside or on the outline are part of the sheet.
2. Draw each bend as a **construction line** across the sheet in the same sketch. The line is treated as infinite and is the middle of the bend.
3. **Sheet Metal › Fold**: pick the sketch and set Thickness, Bend radius (inside), K-factor (default 0.44, as in Flat Pattern), Bend angle (default 90°) and which side of the sketch plane the metal lies on.
4. Bends are numbered left to right, then bottom to top. **Angle of each bend** takes signed degrees in that order, for example `90, -90, 180`, and **Radius of each bend** takes inside radii in millimetres, for example `3, 80, 3`; an empty entry keeps the default. Positive folds toward the sketch normal, negative the other way; angles go up to 180°. A large radius rolls a curve.
5. **Also make the flat pattern beside the part** (on by default) adds the unfolded sheet with its holes and a bend line per zone. **Save the flat pattern as DXF** writes it for laser or waterjet cutting.

**Behavior**
- A bend line is the middle of a **bend zone** as wide as the bend allowance θ·(r + K·t), with K measured from the inside surface, the formula Flat Pattern uses. The zone becomes an exact cylindrical strip. The flat regions between zones stay rigid.
- The largest flat region stays where it was drawn; the others fold from it one bend at a time.
- Parts bent in several directions work. Hems are 180° bends.
- A zone must be a clean rectangle of metal. Bends may meet outside the outline but not inside it, a bend may not pass through a hole, slot or notch, and every flat region must be joined to the rest by a bend. Corner reliefs are drawn in the sketch (a plus-shaped blank leaves the corners open); Fold does not add them.
- Errors name the cause and make no body: no construction lines, a bend that does not cross the sheet, bends that cross inside the sheet, a bend through a hole or notch or without metal on one side, bends that form a closed loop, regions not joined by a bend, an angle that is not a number or outside 0.1° to 180°, a radius that is not a number from 0 to 1000 mm, more angles or radii than bends, or flanges that fold into each other.
- The flat pattern is the sketch's own outline moved beside the part along the sketch's U direction, so it works on any sketch plane. The sketch stays in the document.

**Acceptance criteria**
1. A cross-shaped blank with four 90° bends folds into one valid solid tray whose bounding box and volume match the closed-form values (FLD-H003).
2. A one-bend blank folds into the same solid, by volume and size, that Flange builds from the matching profile (FLD-H001), and Flat Pattern on it returns the blank's length and width to 0.001 mm (FLD-H002).
3. A hole drawn on a flat region is a hole in the folded part and in the flat pattern, of the same size (FLD-H004, FLD-H009).
4. Signed angles fold bends opposite ways (FLD-H005); each bend can have its own inside radius (FLD-H010).
5. The flat pattern's area, length and bend-line count match the blank, and it lies beside the part (FLD-H008); it can be left out (FLD-H009); the DXF is written (FLD-H011).
6. Invalid input is rejected with a message naming the cause (FLD-H006, FLD-H007, FLD-H010).
7. The bend allowance and the placement of a folded region follow the arithmetic in FLD-U001 to FLD-U003, including that a chain of equal adjacent bends equals one arc.

**Limits**
- Flat Pattern, the tool for existing bodies, still refuses a body with bends in more than one direction ("not all parallel"). A part made with Fold gets its flat pattern from Fold itself.
- Bend lines are straight. A circular roll is one bend with a large radius and angle, but splines, cones, forming features (louvres, ribs), multi-piece sheets and lap or welded seams are not covered.
- There is no live preview handle or per-bend table: per-bend angles and radii are typed as lists.
- Fold checks that the joined result is one valid solid, not every possible collision between folded flanges; use Interference.

## Edge Flange

**Status: implemented, 2026-11-08, under the same delegation as Fold.** Delivery: [#81](https://github.com/chrizpro/prism/issues/81).

**Purpose.** Add a flange to an edge of a sheet-metal body that already exists, the extrude-style companion to Fold.

**How it works.** Pick the **thin end face** of the sheet along the edge. Set **Flange length** (the straight leg after the bend), **Bend angle** (0.1° to 180°), **Bend radius (inside)** and **Fold toward** Up or Down. Up is the upper face when the sheet is level; for a sheet that is not level it is a steady side, and Down is the other.

**Behavior**
- The flange runs the whole length of the picked edge, as a cylindrical bend followed by a straight leg, and is joined to the body. It is the same bend Fold makes, so a plate with an edge flange equals the same part folded from a flat sketch.
- The face must be a flat rectangle much longer than it is thick. Anything else, such as the top or bottom face, is refused with a message saying to pick the thin end of the sheet.
- If the flange would run into the rest of the part or fail to join, no body is made.

**Acceptance criteria**
1. A 90° flange up on a 27 × 40 × 2 mm plate (3 mm inside radius, 17 mm leg) has the volume and bounding box of the bracket Fold makes from the same blank (EFL-H001).
2. Down bends it below the plate with the matching bounds (EFL-H002).
3. A flange added to the free end of a Flange bracket adds exactly the bend sector plus leg volume (EFL-H003).
4. A wrong face is refused with a message naming the problem (EFL-H004).

**Limits.** Straight, square-cut end faces only; the whole edge is flanged, not part of it; there is no separate hem or relief option; collisions are caught only as an invalid or unjoined result.

## Observed limitations (2026-11-08)

Findings from building the [tablet-stand gallery study](../../interface/gallery/README.md#curated-collection), recorded as observed behavior, not as intended requirements. Two have since been fixed:

- **Flat Pattern** rejected a Flange body with a slot or a rounded corner, reporting "the bends are not all parallel", because it counted every partial cylinder as a bend ([#78](https://github.com/chrizpro/prism/issues/78)). **Fixed (2026-11-08):** only a cylinder with a concentric partner (inner and outer surface on one axis across the same width) is a bend; slot ends and rounded corners stay in the flat outline. [SHM-H010](../../../Prism.Tests/Headless/SheetMetal/SheetMetalTests.cs) unfolds a bracket with a round-ended slot. Bodies bent in more than one direction are still refused.
- **Cut-outs from a face sketch:** Extrude Cut could silently remove nothing on a tilted face ([#79](https://github.com/chrizpro/prism/issues/79)). **Fixed (2026-11-08)**, see the [Extrude guide](../extrude/README.md).
- A Flange makes every bend from one profile sketch, and Flat Pattern needs the bends to be parallel.

## Proposed: further sheet-metal work

**Proposed, awaiting owner review. Nothing in this section is approved behavior or implemented.** Tracked in [#81](https://github.com/chrizpro/prism/issues/81). Tool names are working names.

- **Flat Pattern for existing bodies bent in more than one direction**, which would let the Flat Pattern tool unfold what Fold makes. (Fold already makes its own flat pattern.)
- **Curved bends** (below).
- **Per-bend editing:** a table or on-screen handles for each bend's angle, direction and radius instead of typed lists.

### Proposed: curved bends from a sketch

Idea: a bend is a **zone** that maps a strip of the flat sheet to 3D without stretching its neutral surface, so the straight bend is only the simplest zone type. Curves then come in tiers that reuse the same fold:

1. **Roll band (curved profile, straight rulings).** The user marks a band between two straight lines in the flat sketch and draws its side-view profile as an ordinary sketch curve (arc, spline, any planar curve). The band's flat width is a derived reference dimension equal to the neutral length of that profile, so editing the profile moves everything attached to the band's edges. A circular arc is exactly one bend (radius R, angle Θ), which Fold now offers through its per-bend radius. A spline is split into tangent-continuous arcs within a chosen tolerance, each arc one zone, or into N small straight bends when the user prefers facets.
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
- Should the Flat Pattern tool learn to unfold non-parallel bends, given that Fold makes its own flat pattern?
- How should the rest of curved bends be offered: splines as arcs or facets, and a cone roll? Should Flatten gain an analytic cone layout like its cylinder layout?
- Should the choice of Fold's fixed region be changeable (it is the largest region today)?
- Should Edge Flange be able to flange part of an edge, or a curved edge, and should it add a hem and relief option?
- Should thickness, K-factor and minimum bend radius belong to the material? Library and custom materials carry mechanical and thermal data but nothing sheet-specific today.

## Verification

[Sheet metal tests](../../../Prism.Tests/Headless/SheetMetal/SheetMetalTests.cs) check Flange volume and bounds for both metal sides, width direction (forward, reversed, centred), hem, a two-bend channel and rejected profiles; Flat Pattern size and bend allowance for three K-factors, placement and the no-bend error; and Flatten / Refold on the documented radius 25 × 70 mm cylinder for each Create choice. [Fold tests](../../../Prism.Tests/Headless/SheetMetal/FoldTests.cs) (FLD-H001 to FLD-H011) check Fold against Flange, the closed-form tray, holes, signed angles, the Flat Pattern round trip, the flat pattern output, per-bend radii, the DXF writer and the rejections, and [Fold arithmetic tests](../../../Prism.Tests/Unit/SheetMetal/SheetFoldTests.cs) (FLD-U001 to FLD-U003) check the bend allowance and region placement without the kernel. [Edge Flange tests](../../../Prism.Tests/Headless/SheetMetal/EdgeFlangeTests.cs) (EFL-H001 to EFL-H004) check the flange against the closed-form bracket. Run `./tools/test.ps1 -Feature SheetMetal`. [Flatten and Refold](../../flatten-refold.md) also has its standalone suite.

Windows verification on 2026-11-05: the combined candidate passed 86 Unit and 592 Headless tests with rebuilt native libraries. See the [integration verification report](../../engineering/release-readiness-2026-11-05.md) for source revisions, commands and limits.

The maintained Integration check [GAL-I006](../../../Prism.Tests/Integration/DesktopGalleryTests.cs) opens the Fold gallery model and verifies its through-holes and its flat pattern; it needs the configured Windows workstation.

Fold verification on 2026-11-08, Linux cloud container with the v0.1.0-alpha.2 Linux native libraries: FLD-H001 to FLD-H007 and FLD-U001 to FLD-U003 passed, and the full Headless suite passed except six tests that fail independently of this change (Grease Pencil and Eraser have no hover-help topic; four loft tests need a native library newer than alpha.2). The same part folded through the running app by MCP and cadkit gave a valid solid of the closed-form size and volume. Not run: the Windows desktop, the native rebuild.
