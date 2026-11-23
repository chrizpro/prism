# Mesh and scan conversion

The owner approved the combined Mesh tab and the conversion workflow on 2026-11-18. [Delivery issue #145](https://github.com/chrizpro/prism/issues/145) builds on the earlier [Mesh Repair approval #123](https://github.com/chrizpro/prism/issues/123) and [Reverse Engineer workflow approval #124](https://github.com/chrizpro/prism/issues/124).

| Mesh group | Tools |
|---|---|
| Prepare | Import Mesh (with Import Point Cloud and Reconstruct Mesh in its menu), Align |
| Analyze | Mesh Quality (with Clear Colors in its menu), Deviation |
| Repair | Repair, Edit Region, Remesh |
| Convert | Section Sketch, Fit Region |

Select, Datum, Transform, Inspect and Appearance remain available as shared sections. Print Check, Wall Thickness and Overhang & Orientation are available in CAM's print preparation. Use File → Export for mesh files. Superseded tools, including standalone Reduce and the triangle-by-triangle Mesh to Solid, use the command legacy attribute and remain available to existing scripts and saved feature recipes.

The owner approved manual mesh repair and the revised ribbon on 2026-11-18 for [#146](https://github.com/chrizpro/prism/issues/146). Section Sketch is the visible reconstruction command. The automatic Convert Mesh tool remains available to scripts as `mesh_convert` and to saved recipes.

## Industrial mesh examples

Open the source OBJ from the gallery card to repeat either edit. For the four-groove sheave, choose **Remesh → Reduce Faces**, keep 55% and apply. For the ten-vane impeller, **Fill Holes** closes one missing facet. Both finished meshes are closed and their volumes are within 1.5% of the original exact solids. [Gallery index](../../../gallery/README.md#what-is-here) · [GAL-H212](../../../Prism.Tests/Headless/Gallery/IndustrialGalleryStudies.cs).

The turned-knob study reconstructs one closed CAD body from the measured grip diameter, taper, cone apex, dome centre and radius. Its native document keeps the fitted patches hidden for inspection and shows the finished solid beside the scan. [GAL-H202](../../../Prism.Tests/Headless/Mesh/MeshGalleryStudies.cs).

## Mesh Quality

Pick a mesh to see triangle statistics and defect highlights. **Preview result** restores the display after **Clear defect display** and runs the self-intersection check for large meshes when enabled. The heat map also appears in Wireframe. Use **Mesh Quality → Clear Colors** to remove the display from the ribbon. [Viewport fixes #148](https://github.com/chrizpro/prism/issues/148).

## Repair and local editing

**Repair** opens Cleanup. Its menu also offers **Fill Hole** and **Stitch Seam**. Cleanup preserves open rims. Fill Hole previews flat or smooth patches for the openings you select. Stitch Seam picks an edge to keep fixed, then the matching boundary edge to move. It welds the endpoints only within **Maximum gap** in millimeters. Repeat on longer seams. Use Draw Faces to bridge gaps or mismatched edges.

### Drawing a repair

Open **Edit Region → Draw Faces**. Choose **Open rim** to add faces, or **Replace selected region** to draft against a cut boundary while the source faces stay visible. Click three or four corners and press Enter to finish a face. Shift-click a rim or draft edge to start from its endpoints, or Shift-drag the edge into a quad. The visible source triangle under the pointer turns cyan. With **Snap to mesh** on, a nearby vertex takes priority over an edge, then a free surface point. A cyan dot marks a vertex snap and an amber line and dot mark an edge snap. These snaps guide the corner position. Only the repair boundary can anchor the new faces to the existing mesh. The XY, XZ or YZ drawing plane and its offset supply points where the scan has no hit. Turn off **Snap to mesh** to place points on the plane while retaining explicit edge starts.

The draft fills in purple after three corners, including while you place a fourth corner or pull a quad. To replace faces on a closed mesh, select them with **Select → Face** or **Edit Region** before opening Draw Faces. The stitch preview shows source faces to remove, joined edges in green, and the remaining open-edge count. Landing on an edge interior splits the adjoining source triangle to give the patch a shared vertex. Applying replaces the mesh in one undoable edit. Replacements must cover the entire cut seam. Invalid joins, collapsed faces and new overlaps detected by the local check block Apply. Intentional holes elsewhere remain open.

**Approved 2026-11-19** in [#147](https://github.com/chrizpro/prism/issues/147). In the shared **Select → Face** mode, click an individual mesh triangle, Shift-click to add triangles, or select a patch with a box, lasso or paint stroke. Choosing Face on the Mesh ribbon closes Mesh Quality or the current Section Sketch preview. Sections you already applied stay in the model. Finish a repair draft before switching to Face selection. **Delete** opens a gap in the original mesh, keeps the remaining face and vertex colors, and supports Undo. Use Object selection to delete the whole mesh. Edit Region offers broader repair commands such as growing a region or patching a gap.


### Filling several holes

Approved on 2026-11-19 along with the Draw Faces target preview and snaps. [Issue #149](https://github.com/chrizpro/prism/issues/149).

Open **Repair → Fill Hole**. Click inside a visible opening or on its rim to toggle it. Selected openings preview in green. **Clear picks** empties the selection. Choose **Flat** or **Smooth** for the batch and press **Fill** to patch the selected openings on the current mesh in one undoable edit. Unselected openings stay open. If a patch fails validation, the mesh stays as it was so you can revise the selection.

### Removing something from a mesh

Open **Edit Region**, pick the area with smart patch, brush, rectangle or lasso, and press **Delete and patch**. The tool closes openings created by the deletion and keeps existing holes open.

- **Smooth** adds points at the mesh's own size and follows a curved surface fitted to the mesh around the opening.
- **Flat** only triangulates the opening.

**Smooth selected triangles** reduces local noise while keeping sharp edges, open edges and vertices shared with unselected triangles fixed.

### Cleanup scope and saved Repair workflows

Cleanup's **Scope** section offers Entire mesh and Selected region. If a current nonempty region exists, opening Repair selects that mesh and defaults to Selected region. Otherwise it defaults to Entire mesh. Choose the scope explicitly before applying.

For a local fill on the ribbon, use **Repair → Fill Hole** and pick the openings. The original batch Repair command remains callable as `mesh_repair`. In that command’s selected-region mode a hole is eligible only when the triangle beside **every edge of its rim** is selected. Partial rims and openings outside the region stay open. Green outlines preview the holes that will be filled; the report counts filled candidates, protected holes and remaining holes. The hole limit is a count of rim edges, not a diameter in millimeters. Turning Fill holes off leaves hole filling disabled in either scope.

**Cleanup** controls merging duplicate vertices, removing degenerate/duplicate triangles, removing tiny floating shells and correcting normals. Selected-region mode restricts these operations too: vertices shared with unselected faces stay fixed, junk triangles outside the region remain, and a tiny shell is removed only if it is wholly selected. Normal correction uses surrounding unselected faces as anchors; it cannot fix an inconsistent surrounding mesh without changing the scope. Seam defects can require a larger selection or Entire mesh. Entire mesh retains the global cleanup behavior. There is no general automatic repair for self-intersections or non-manifold edges.

Selection is tied to one version of one mesh. A missing, empty, wrong-mesh or stale region blocks selected-region repair and asks you to select again. Applying a topology change requires a new region for the next local repair. Inspect the preview and rerun Mesh Quality / Print Check after applying. Undo restores the prior mesh.

**Remesh** has two modes. **Redistribute triangles** sets a target edge length and can preserve sharp and open edges. **Reduce Faces** simplifies by target triangle count or percentage and can keep open edges. Both can keep the source mesh. Check the triangle count and shape change before applying, especially around small features.

## Legacy Convert Mesh automation

| Output | Source and result |
|---|---|
| Solid | Fit the entire scan as a direct-edit solid, or fit a cylinder, sphere or cone to the selected region. A whole-mesh fit can preview open surfaces if it cannot close. Enable **Keep open result as surface** to accept them. |
| Surface | Fit separate CAD faces across the entire mesh or fit one selected region. Choose a plane, cylinder, sphere or cone to override automatic classification. Planes follow boundary loops where possible. Curved analytic faces use the fitted angular and axial extent. Automatic can fit a smooth patch when an analytic surface does not fit. |
| Freeform | Wrap an editable box, ball or cylinder cage around the entire mesh. Refine complex regions with Freeform's cage editing tools. |
| NURBS | Fit one smooth NURBS patch to a selected region. |

Use **Edit Region** to choose triangles before starting a region fit. The command checks that the selection still belongs to the current mesh. A fitted primitive can extend past a selected region, for example when a cylindrical hole wall supplies the dimensions for a closed cutting body.

**Preview result** reports the output type and open CAD edges. It samples vertices and triangle interiors in both directions against the finished geometry, and reports RMS error, largest sampled error and percentage within the fit tolerance. Orange dots show scan samples outside tolerance, red dots show result samples outside tolerance, and red lines show open edges. For Surface and NURBS, open edges can be the intended boundary. The source mesh remains in the document. Samples are approximate and a scan with missing data leaves the intended shape in those areas uncertain.

Geometry on real scans remains to be benchmarked. [Issue #88](https://github.com/chrizpro/prism/issues/88) records oversized cylinder caps in the legacy automatic fit. Automatic multi-patch NURBS reconstruction and exact trim curves on irregular analytic regions remain open decisions.

### From a scan to parametric features

Convert Mesh creates fitted geometry for direct editing or an editable freeform cage. To create a feature history, rebuild the part from sketches.

To build your own parametric part:

1. Clean the mesh in Mesh if needed, then **Prepare → Align** it to useful planes.
2. Open **Section Sketch**. Its **Trace** choice slices the mesh or projects the selected region's border, the silhouette or sharp creases (see [MSH-P06](#section-sketch-traces-and-fit-quality-msh-p06)). Its movable plane starts at the mesh center. Drag the translation arrows or rotation rings, or type a move and angle in the panel. The orange trace shows the measured cut and the cyan curves show the fit. Both are visible through nearby geometry. Apply creates sketches at the current plane positions. Reposition the plane and Apply again for additional cuts, then choose Done. Each Apply is an undo step. The original world and picked plane options, offsets, and batches of parallel cuts are still available.
3. Edit a generated sketch. The orange trace stays as a reference and is saved with the sketch. Toggle it with **Show measured section**. The panel's **Fit Section Curve** fits a line, arc or spline between two picks, or a circle to a closed outline. Use **Edge** selection to choose a single unconstrained curve before opening the tool to replace it while keeping shared endpoints. Add dimensions and constraints to express design intent. For example, enter 50 mm on a line measured as 50.3 mm. The orange reference retains the scan measurement.
4. Use the Solid or Surface tools that suit the design, such as Extrude, Revolve or Loft. Choose the depth, axis or profiles there. Change a driving sketch dimension later to adjust the part. The choice of a ribbon shortcut to these tools is open for review.
5. Run **Deviation** with the scan as Mesh to check and the rebuilt CAD body as the reference. This samples the scan against the reference; it is not a complete, bidirectional certification of every rebuilt surface. A partial or missing scan cannot establish the intended shape of the missing area.

If automation fits the wrong local shape, use Edit Region and Convert Mesh to choose the intended surface type. For unwanted mesh bumps, use Edit Region → Delete and patch or Smooth selected triangles. Deviation helps review the result; it does not change the geometry.

## Mesh polish (approved and delivered 2026-11-19)

**Owner decisions, 2026-11-19:** Reduce faces offers both a percentage and a triangle count and is off unless turned on. Draw Faces' Fit to surface is on by default. Section Sketch adds tangency automatically and estimates its tolerance from the scan's noise. The owner then asked for the Mesh tab to handle a wide range of scans without deviating much from the existing tools, and for a demonstration of importing a mesh and using Smart patch to create a surface or solid. Fit Region (MSH-P05) delivers that. Draw Faces stays in Edit Region's menu with the rim fixes below. Still open: the triangle count that triggers the reduction suggestion (2 million is used for now).

### Selecting triangles without Edit Region (owner report 2026-11-20)

The owner had to open Edit Region to give Fit Region its triangles and could not get faces selected. Mesh tools read a region of their own, separate from the ordinary selection, so triangles picked in **Face** mode did nothing for them. Now:

- In **Face** mode a click picks one triangle and a **double-click takes the whole smooth patch** around it (the same rule as Edit Region's Smart patch, tolerance 0.2 mm, edge angle 20°); Shift adds, Ctrl takes away.
- Whatever triangles are selected when a Mesh tool starts become its region, so Fit Region, Extract and the other region tools work straight from the ordinary selection.
- While Fit Region is open, Face-mode clicks keep editing the triangles (click, double-click, Shift, Ctrl as above; a click on empty space clears them) and the tool updates. Clearing the selection also clears the highlighted region.
- A region with very few triangles, such as a flat face of a CAD export made of two triangles, is split into smaller triangles on the same surface before fitting, so it fits a plane instead of reporting no match (owner report 2026-11-20).
- The construction plane Fit Region adds for a flat region ("Fitted plane") is a rectangle that covers that region with a small margin, its sides lined up with the face, instead of a square well past it (owner request 2026-11-20).
- Checks: [MSH-H153, MSH-H154](../../../Prism.Tests/Headless/Mesh/MeshFitRegionTests.cs).

**Edit Region stays for now** (owner direction): its brush, rectangle and lasso painting, grow and shrink, invert and the Smart patch settings are not yet available from the ordinary selection. Proposed next, for review: give Face mode on meshes those extras (grow, shrink, invert in the right-click menu; brush as a selection method), then retire Edit Region once nothing is lost. Not reproduced headlessly: the owner's report that faces could not be selected inside Edit Region (clicks there picked a patch in the tests); a screenshot would help.

### Fit Region (MSH-P05)

**Mesh → Convert → Fit Region** fits one shape to the triangles picked with Edit Region (Smart patch, brush, rectangle or lasso).

- **Fit:** Automatic, Plane, Cylinder, Sphere, Cone, a smooth NURBS patch inside the outline, or a rectangular NURBS grid. An explicit shape is always fitted to the whole region. Automatic takes the simplest shape whose error is close to the best and within the tolerance, otherwise a smooth patch.
- **Accuracy:** the fit minimises the true distance from the region's vertices to the shape (Levenberg–Marquardt) and ignores points more than three times the noise away, such as the edge of a neighbouring face. Detection on triangle centres alone read a Ø12 hole as Ø11.963 with 0.11° tilt. The vertex fit reads Ø12 ± 0.01 with under 0.05° tilt.
- **Readout:** diameter, axis point and direction, length along the axis, included angle and apex for a cone, centre for a sphere, point and normal for a plane, tilt from the nearest world axis, RMS and largest error.
- **Create:** a surface trimmed to the region (default: a plane keeps the holes inside its outline, a cylinder or cone wall has no caps), a closed solid (for a cutting or joining body), or reference geometry only. **Add reference geometry** (default on) adds a construction plane for a plane and an orange axis line for a cylinder or cone.
- Smart patch now also recognises cones and long curved faces bounded by sharp edges. When segmentation misses, it floods across smooth edges up to the first sharp one and keeps the flood if one cylinder, cone or sphere fits all of it.

A hole a Smart patch click cannot reach is still easiest to rebuild with Section Sketch: sketch the hole, then extrude or cut it.

### Section Sketch cleanup (MSH-P04)

- **Estimate the fit tolerance from the scan's noise** (default on). Each outline is resampled and its noise measured from how the points scatter, so steady curvature does not count as noise. The tolerance is four times the noise, at least 0.01 mm and at most 0.5% of the outline. Outlines with too few points use the typed tolerance. The panel shows the tolerance, the noise and the number of lines and arcs before Apply. A noisy box section that produced 33 lines and 32 arcs at a fixed tolerance gives 4 lines.
- **Fit Section Curve** in the sketch: pick a start and an end on the orange trace. Curves lying along that span are replaced by one fitted line, arc or spline. A click near an existing curve end (green dot) starts exactly there and shares that point, so the profile stays connected. Curves with curve constraints are kept and counted. Selecting one curve first replaces only that curve, as before.
- **Make tangent to neighbouring curves** (default on): when a new line or arc continues a line or arc within 10°, a tangent constraint is added. Splines are not constrained.
- Lines are total-least-squares fits. Arcs are least-squares circles through the chosen end points. Splines use the fewest through points whose curve stays within the tolerance: they start from local averages and are corrected toward the samples.
- The panel reports the last fit's largest deviation, its tolerance, how many curves it replaced and how many tangencies it added.

### Section Sketch traces and fit quality (MSH-P06)

**Owner report, 2026-11-22:** section sketches were not usable. Dots sat near the curves, and deleting a dot removed part of the outline. The owner also wanted a sketch of the face a part sits on, which a plane can't take cleanly, and asked for a way to project edges onto a plane. **Owner decisions, 2026-11-22:** trace the selected region's outline for the face at an end. Offer the region border, the silhouette and sharp creases as projections.

Cause: the dots were arc centre points. Every fitted arc gets one, and arcs fitted to noise were small, so their centres sat next to the outline. A plane inside the scan's noise (0.02 mm above the bottom of a test scan) also cut thousands of tiny loops. Every element was judged by its single worst point, so a hole became an arc plus short lines and a freeform edge became many short lines and arcs. Nothing was constrained.

Behaviour now:

- **Trace** chooses what Section Sketch draws on its plane: **Section** (slice the mesh, as before), **Border of the selected region**, **Silhouette** (the outline of the mesh seen along the plane's normal, including holes) or **Sharp creases** (edges where the surface folds by more than **Crease angle**, default 40°). Projections land on the chosen plane. Choosing the region border switches the plane to **Fitted to the selected region**, so selecting the face a part sits on (Face mode, double-click) gives its footprint on that face.
- **No noise specks.** Section loops where the plane slides along a face rather than crossing walls are skipped, and the panel says how many and suggests the region border. Projected creases and borders smaller than 0.5 mm (vertical edges seen end on) are dropped. Creases that land on top of another one (top and bottom rims) are kept once, using the copy with the fewest curves.
- **Fit quality.** Elements are accepted on a robust deviation: the 95th-percentile distance, a few stray points allowed up to 2.5 × the tolerance, and any short stretch of neighbouring points drifting away counted in full. Closed outlines that stay within 1.5 × the tolerance of a circle all the way round become circles. Runs of pieces that meet smoothly on the scan (measured on the points, so a mild designed corner still counts as a corner) become one spline when that needs fewer points. A fillet between straight edges is rebuilt as a tangent arc (centre where the offset lines cross). Shallow arcs under 45° that the scan left at a corner are removed and the neighbours meet at a sharp corner. Tangent constraints are added where lines and arcs continue smoothly, if solving them moves nothing by more than three tolerances.
- The silhouette is traced on a grid of up to 2,500 cells across, smoothed, and fitted no tighter than three cells.
- While the plane is being dragged, the preview fits lines and arcs only, then refits fully when the plane is released.
- The Elements row counts lines, arcs, splines and circles.

On a 70 × 42 × 20 mm test plate (straight sides, an R10 corner, a freeform top edge, a Ø12 hole; 0.6 mm triangles, 0.03 mm noise), every trace gives 3 lines, the R10 arc, one spline and the circle, with the arc tangent to both sides. Over 27 sections (3 noise levels × 3 seeds × 3 heights), 11 gave exactly that design. 19 were usable without cleanup: at most 10 curves, no arc under 3 mm radius, within 1.6 tolerances. At 0.05 mm noise the freeform edge can stay as a chain of lines and arcs. Use Fit Section Curve to replace a span with a spline.

Not covered: a face that a smart selection can't reach, and scans with holes along the traced outline (an open loop is fitted as an open curve). For solids, sketch mode's **Project / Include** already projects edges, bodies and intersections. Projecting edges one at a time can put two copies on top of each other.

Checks: [Section trace tests MSH-H156 to MSH-H160](../../../Prism.Tests/Headless/Mesh/SectionTraceTests.cs).

### Import Mesh: reduction and large files (MSH-P01, MSH-P02)

- **Reduce faces** (off by default) keeps a percentage of the triangles or reduces to a triangle count for the whole file, shared between bodies by size. Open rims stay in place. It runs in the background after units, up axis, welding and repair. The result row and message show the count before and after. The setting is remembered with the other options. Scripts pass `reduce_percent` or `reduce_to` to `mesh_import`.
- Above 2,000,000 triangles the panel suggests Reduce faces. When the mesh would need more than about 70% of the free memory, the panel warns before Import. Running out of memory is reported as a message instead of closing the app.
- OBJ files are read line by line, so a file is no longer limited to one .NET string (about 1 GB of text). A 5.1-million-triangle, 196 MB OBJ read in 3 to 4.5 s.
- Reductions of meshes over 1,000,000 triangles to under a third first merge vertices on a grid to about three times the target, then run the careful reduction. The 5.1-million-triangle grid reduced to 10% in 29 s with 0.84 GB, instead of 102 s with 2.5 GB, and without non-manifold edges. Cancel takes effect between the two stages.

### Draw Faces: follow the rim and fit to surface (MSH-P03)

- **Follow the rim between corners** (default on): when two consecutive corners are vertices of the same open rim, the side between them follows the rim the shorter way and stitches every rim edge it passes. **Use the longer way around the rim** switches direction. Faces may therefore have many corners. Three clicks on a rim close the whole opening.
- **Fit to surface** (default on) splits the new faces to the size of the mesh around them and places their inner points on the surface fitted to that mesh, as Fill Hole → Smooth does. Points on the outline do not move. If the fitted patch would fold or overlap, the flat patch is kept and the panel says so.
- The stitch preview draws rim edges that are still open in red.

### Remesh speed

Remeshing a CAD-exported mesh was very slow. It checked whether a vertex lies on an open border by walking every neighbour each time, which was quadratic around the many-neighbour vertices that split slivers leave behind, and each collapse scanned every fixed (sharp) edge. Boundary status is now kept per pass and a collapse looks only at the fixed edges at its own vertex. The 113 Mesh headless tests pass without changes to their expectations. The pillow-block gallery study, whose fake scan remeshes a 5,064-sliver CAD export to 56,800 triangles at 0.8 mm, now runs in about 70 s instead of 19 minutes; almost all of that time is still the remesh, because the slivers first split into about 350,000 triangles. Projecting onto a copy of the input with its long edges split was also tried: it gave no further speed-up on this part and changed results through rounding (REV-H102), so it was dropped.

## Maintained tests

[Fit Region tests](../../../Prism.Tests/Headless/Mesh/MeshFitRegionTests.cs) cover trimmed planes, uncapped cylinders with axis readout, the Create choices, and noisy spheres and cones. [Section cleanup tests](../../../Prism.Tests/Headless/Mesh/SectionCleanupTests.cs) cover span refits, tangency, spline smoothing and noise-based tolerance. [Import reduction tests](../../../Prism.Tests/Headless/Mesh/MeshImportReduceTests.cs) cover Reduce faces, the options panel and the streamed OBJ reader. [Draw Faces rim tests](../../../Prism.Tests/Headless/Mesh/MeshDrawFacesRimTests.cs) cover following a rim and fitting to a cylinder. [Gallery studies](../../../Prism.Tests/Headless/Mesh/MeshGalleryStudies.cs) run the pillow block and turned knob scans end to end.

[Section Sketch and mesh selection tests](../../../Prism.Tests/Headless/Mesh/MeshSectionAndSelectionTests.cs) check repeated Apply and Done, angled sections, refitting and nominal dimensioning through save and reopen, and single-triangle selection, deletion and Undo.

[Manual mesh repair headless tests](../../../Prism.Tests/Headless/Mesh/MeshManualRepairTests.cs) and [local topology tests](../../../Prism.Tests/Unit/Mesh/MeshLocalEditTests.cs) cover drawing, visible face and snap targets, seam welding, batch hole filling, rim isolation and undo.

[Combined Mesh workflow tests](../../../Prism.Tests/Headless/Mesh/MeshConsolidationTests.cs) cover open-solid consent, explicit region outputs, deviation in both directions, Freeform, face reduction and local smoothing.

[Mesh tool tests](../../../Prism.Tests/Headless/Mesh/MeshToolTests.cs) cover Repair and Fill Holes, Print Check (printers, custom volume, materials), Reduce, Smooth, Offset, Split (every plane, flip, open cut, keep options), Section Curves, Wall Thickness, Overhang orientation, Deviation, every Align mode, Mesh Boolean, Remesh, Mesh Quality and both conversions between meshes and solids. [Reverse engineering tests](../../../Prism.Tests/Headless/Mesh/ReverseEngineerTests.cs) cover Segment & Fit, Rebuild from Scan (faces, curves and sewn solid), Section Lines & Arcs, Section Sketch and Select Region actions. Run `./tools/test.ps1 -Feature Mesh`.

[Scoped Repair workflow tests](../../../Prism.Tests/Headless/Mesh/MeshRepairScopeTests.cs) cover complete and partial rims, protecting another opening and unselected faces, green preview boundaries, invalid selections, Undo and ribbon organization. [Scoped algorithm tests](../../../Prism.Tests/Unit/Mesh/MeshRegionRepairTests.cs) cover welding anchors, cleanup/colors, whole-shell selection and normal correction. Pointer-driven and real-scan checks remain in the [manual checklist](../../tabs/checklists/mesh-manual-test-checklist.md).

[Reverse Engineer workflow tests](../../../Prism.Tests/Headless/Mesh/ReverseEngineerWorkflowTests.cs) exercise the ribbon routes and section-sketch dimensioning followed by extrude/revolve and Deviation. [Profile fitting tests](../../../Prism.Tests/Unit/Mesh/ReverseEngineerProfileTests.cs) cover sparse straight-sided and dense circular sections. Hands-on checks are in the [Mesh conversion checklist](../../tabs/checklists/reverse-engineer-manual-test-checklist.md).

Fixed on 2026-11-05:

- Section Curves (with "Create curves" off), Section Lines & Arcs (with "Create lines and arcs" off), Segment & Fit ("Nothing - just analyze", and a missing region number) and Rebuild from Scan ("Nothing - analysis…") used to clear their report before returning, so the panel showed no rows. They now keep it; report-only tests cover each mode.
- Solid to Mesh detail levels used to produce the same mesh on small curved bodies: a fixed angular limit outweighed the chord error, and a finer triangulation already stored on the body (such as the display mesh) was reused. Each level now has its own angular limit (Custom leaves the chord error in charge) and meshes a fresh copy of the geometry. The test asserts Coarse < Normal < Fine.

Fixed during the 2026-11-10 Reverse Engineer workflow verification: sparse rectangular section corners could fit a circle even though their straight edges departed from that circle. Arc fitting now checks edge interiors and the closing edge of a complete circle candidate. Section arc previews also sample the fitted curves instead of drawing only their start/mid/end chords, which made a circle preview look rectangular. REV-U001 / REV-H120 cover the rectangular tube section; REV-U002 / REV-H119 retain the circular route and verify the preview follows the circle.

## Tested on

On 2026-11-19, Windows Release at `bb9d0de` passed the desktop build, ribbon catalog check, 10 Mesh Unit tests and 132 Mesh and Selection Headless tests. Four mesh repair viewport scenarios passed twice in shuffled order. [Tracking issue #149](https://github.com/chrizpro/prism/issues/149).

- **MSH-I008** clicked inside one opening and on another rim, toggled a pick off and back on, and filled two of three openings. One Undo restored all three. [Batch Fill Hole viewport](evidence/MSH-I008-batch-fill.png)
- **MSH-I009** captured the cyan target triangle, amber interior-edge guide, and vertex marker. **MSH-H138** also checked exact snap coordinates and an occluded target. [Draw Faces viewport](evidence/MSH-I009-draw-face-snaps.png)

On 2026-11-19, Windows Release at `3d3ee0e` passed the desktop build, ribbon catalog check, 10 Mesh Unit tests and 127 Mesh and Selection Headless tests. Three viewport scenarios passed twice in shuffled order. [Tracking issue #148](https://github.com/chrizpro/prism/issues/148).

- **MSH-I003** checks the purple draft before and after Enter, four joined edges, and one remaining open rim. [Draw Faces viewport](evidence/MSH-I003-purple-draft.png)
- **MSH-I006** starts with Section Sketch active, uses the Face ribbon control, and checks that deleting one triangle leaves three and Undo restores four. [Face selection viewport](evidence/MSH-I006-face-from-section.png)
- **MSH-I007** checks that Preview result restores the heat map in Wireframe after Clear and that the report still counts four open edges. [Mesh Quality viewport](evidence/MSH-I007-quality-preview.png)

On 2026-11-19, the integration into `v0.4-work` passed the Windows Release desktop build, the ribbon catalog check, 10 Mesh Unit tests, and 124 Mesh and Selection Headless tests. Viewport scenarios MSH-I005 and MSH-I006 passed twice in shuffled order.

On 2026-11-19, Windows Release passed **10 Mesh Unit tests** and **122 Mesh and Selection Headless tests**. Both pointer-driven desktop scenarios passed twice in shuffled order. The desktop build and generated ribbon catalog check passed. See [#147](https://github.com/chrizpro/prism/issues/147) for the source revision and commands.

- **MSH-I005** moved a plane to a 6 mm section, tilted the next plane 15 degrees, and applied twice. Done left exactly two sketches. The sketch panel replaced the measured 50.3 mm span, and its driving dimension solved to 50 mm. [Plane, refit and dimension captures](evidence/MSH-I005-section-sketch.png)
- **MSH-I006** used the shared Face tool to pick one triangle. Delete left three triangles in the original mesh, and Undo restored four. [Selection and deletion captures](evidence/MSH-I006-select-delete.png)

On 2026-11-18, Windows Release passed **10 Mesh Unit and 87 Mesh Headless tests** using the workstation's staged OCCT runtime. The desktop build and ribbon catalog check passed. Three desktop scenarios passed twice in shuffled order, including the legacy conversion workflow.

- **MSH-I003** used the pointer to draw a quad over one of two open rims. The preview reported four joined edges. Applying changed 8 triangles to 10 and 2 open rims to 1. [Draw Faces viewport](evidence/MSH-I003-draw-faces.png)
- **MSH-I004** stitched a 0.2 mm gap within a 0.3 mm limit. The two sheets joined with 6 vertices, 4 triangles, and one open outer rim. The companion Fill Hole action closed one of two openings. [Stitch Seam viewport](evidence/MSH-I004-stitch-seam.png)

The tested revision and commands are recorded in [#146](https://github.com/chrizpro/prism/issues/146). Large, heavily damaged scans still need hands-on usability checks.

On 2026-11-18, **6 Unit and 83 Headless Mesh tests** passed on Windows Release using the workstation's staged OCCT runtime. The desktop build and ribbon catalog check passed. Desktop scenario **MSH-I002** imported an open 20 × 20 mm mesh, previewed a Solid conversion and confirmed it required explicit consent to keep the open result. It then selected the mesh with a pointer and fitted a 400.0 mm² CAD face. Both checks passed in two runs. See the [preview and checks](evidence/MSH-I002-convert-open-scan.png) and [#145](https://github.com/chrizpro/prism/issues/145) for the tested revision.

The synthetic cylinder from issue #88 still fitted oversize caps during automatic reconstruction. Convert Mesh reported a 1.719 mm maximum sampled scan-to-result distance and 2.076 mm result-to-scan distance. The geometry check flagged the candidate as invalid and the command withheld it from commit. The fitting issue remains open.


Reverse Engineer verification on Windows Release, 2026-11-10: **75 Mesh Headless tests**, **6 Mesh Unit tests** and **45 standalone Reverse Engineer checks** passed. The diameter-driven cylinder extrusion matched 500π mm³, then rebuilt to 720π mm³ after changing the sketch diameter from 10 to 12 mm. A tube's axial section, manually trimmed to one radial outline and dimensioned, revolved to 168π mm³. Both initial solids reported 100% of sampled scan points within 0.05 mm in Deviation. Desktop build: zero errors, nine existing warnings; ribbon catalog freshness check passed.

Native **REV-I001** imported a synthetic Ø10 × 20 mm cylinder mesh, created a circular Section Sketch, added a 10 mm diameter dimension, extruded symmetrically through 20 mm and compared against the scan: valid solid volume 1570.7963 mm³ and 100% sampled points within 0.05 mm. [Section Sketch and ribbon](evidence/REV-I001-section-sketch.png); [rebuilt part and Deviation](evidence/REV-I001-rebuilt-deviation.png). The source revision and commands are recorded in [#124](https://github.com/chrizpro/prism/issues/124). Save/reopen, pointer-driven editing and real-scan robustness remain on the manual checklist.

Windows Release verification on 2026-11-10 passed **72 Mesh Headless tests** and **4 scoped-repair Unit tests**, with no failures or skips. The desktop build succeeded (zero errors, nine existing warnings), and the generated ribbon catalog passed its freshness check. Native scenario **MSH-I001** imported an eight-triangle box with top and bottom openings, selected the four triangles beside the top rim, and verified Repair defaults to Selected region: 8 → 10 triangles and 2 → 1 holes. Entire mesh preview instead produced 12 triangles and zero holes. [Selected-region capture](evidence/MSH-I001-selected-repair.png) and [entire-mesh capture](evidence/MSH-I001-entire-repair.png) show the ribbon, scope controls and green rims. The source revision and commands are recorded in [#123](https://github.com/chrizpro/prism/issues/123). Brush/lasso usability and real-scan checks remain manual.

Generated parts only: a drilled plate, an L bracket and a plate with a boss, remeshed and given up to ±0.2 mm of random noise. Each rebuilt to a valid solid with the right faces, within 0.3% of the volume, and Direct Edit pulled a face as it does on the original. No real scan has been tried, and real scans have holes, missing sides and uneven noise.

Section Sketch adds tangent constraints (MSH-P06) but no parallel, perpendicular or equal relations.

The older commands (Fill Holes, Section Curves, Section Lines & Arcs) are off the ribbon but still work from scripts.

Windows verification on 2026-11-05: the combined candidate passed 86 Unit and 592 Headless tests with rebuilt native libraries. See the [integration verification report](../../engineering/release-readiness-2026-11-05.md) for source revisions, commands and limits.
