# Mesh and Reverse Engineer tabs

The two tabs do different jobs. **Mesh** fixes a mesh, and the result is still a mesh. **Reverse Engineer** turns a mesh into CAD you can edit. Drawing clean faces over a scan belongs to Freeform, because a quad cage is what Freeform edits: use Freeform from Scan, Create Face and the scan tools in Edit Freeform.

## Mesh

Owner approved the Mesh organization and scoped Repair in [#123](https://github.com/chrizpro/prism/issues/123) on 2026-11-10. Repair offers Entire mesh and Selected region; region scope preserves geometry outside the selection and fills only holes whose complete rim is selected. Missing/stale regions must be selected again rather than falling back to global repair. Eligible hole rims are previewed before applying.

- **Select, Datum:** shared sections
- **Import & Convert:** Import Mesh, Solid to Mesh, Import Point Cloud / Reconstruct Surface
- **Check:** Mesh Quality, Print Check
- **Repair:** Repair (with Fill holes as a setting), Select Region
- **Reshape:** Reduce, Remesh, Smooth
- **Modify:** Split, Offset, Mesh Boolean
- **Print analysis:** Wall Thickness, Overhang & Orientation, Clear Colors
- **Transform, Inspect, Appearance:** shared sections

### Removing something from a mesh

Open **Select Region**, pick the area with smart patch, brush, rectangle or lasso, and press **Delete and patch**. Only the openings that leaves are closed; other holes stay as they are.

- **Smooth** adds points at the mesh's own size and follows a curved surface fitted to the mesh around the opening.
- **Flat** only triangulates the opening.

### Controlling Repair

Repair's **Scope** section offers Entire mesh and Selected region. If a current nonempty region exists, opening Repair selects that mesh and defaults to Selected region. Otherwise it defaults to Entire mesh. Choose the scope explicitly before applying.

For a local repair, open Select Region, select the triangles around an unwanted opening, then open Repair. A hole is eligible only when the triangle beside **every edge of its rim** is selected. Partial rims and openings outside the region stay open. Green outlines preview the holes that will be filled; the report counts filled candidates, protected holes and remaining holes. The hole limit is a count of rim edges, not a diameter in millimeters. Turning Fill holes off leaves hole filling disabled in either scope.

**Cleanup** controls merging duplicate vertices, removing degenerate/duplicate triangles, removing tiny floating shells and correcting normals. Selected-region mode restricts these operations too: vertices shared with unselected faces stay fixed, junk triangles outside the region remain, and a tiny shell is removed only if it is wholly selected. Normal correction uses surrounding unselected faces as anchors; it cannot fix an inconsistent surrounding mesh without changing the scope. Seam defects can require a larger selection or Entire mesh. Entire mesh retains the global cleanup behavior. There is no general automatic repair for self-intersections or non-manifold edges.

Selection is tied to one version of one mesh. A missing, empty, wrong-mesh or stale region blocks selected-region repair and asks you to select again. Applying a topology change requires a new region for the next local repair. Inspect the preview and rerun Mesh Quality / Print Check after applying. Undo restores the prior mesh.

Remesh remains separate: it rebuilds the triangle pattern, whereas Repair fixes mesh defects. Remesh can retain sharp/open edges and optionally keep the original for comparison; inspect the reported volume change and small details.

## Reverse Engineer

Owner approved the Reverse Engineer organization, clearer output descriptions and scan-to-parametric workflow verification in [#124](https://github.com/chrizpro/prism/issues/124) on 2026-11-10. Automatic sketch constraints and feature-tree recognition remain future work.

- **Select, Datum:** shared sections
- **Prepare:** Align, Select Region
- **Sketch from Scan:** Section Sketch
- **Fit Surfaces:** Segment & Fit, Fit Surface to Region (separate, directly accessible buttons)
- **Rebuild:** Rebuild from Scan, Freeform from Scan, Fallback dropdown (Mesh to Solid)
- **Check:** Deviation, Clear Colors
- **Transform, Inspect, Appearance:** shared sections

### Three ways to get CAD

| Route | Result | Use it when |
|---|---|---|
| Section Sketch | An editable sketch of lines, arcs and circles | You want to rebuild the part with your own features |
| Rebuild from Scan, then Direct Edit | A solid with no history | You want a whole part quickly, then change it |
| Segment & Fit | Separate exact surfaces, with their sizes | You want to read a size, or need one face or cylinder |

**Section Sketch** slices on XY, XZ or YZ, on a construction plane or flat face, or on a plane fitted to the area picked in Select Region.

**Rebuild from Scan** makes an exact solid when every region is a plane, cylinder, cone or sphere: each fitted surface slices a block around the scan, and the pieces inside the scan are joined. Open **Direct Edit** on the Solid tab to change it. Parts with free-form, torus, extruded or revolved regions are sewn instead, so they may stay open and their edges may not work with Direct Edit.

Rebuild from Scan starts in **Nothing - analysis and deviation estimate** mode. Press Preview result to inspect its regions and coverage. To create CAD geometry, change Create to Fitted faces, Fitted faces + boundary curves, or Faces + curves, then sew into a shell / solid. Segment & Fit similarly starts as analysis only. Inspect the output report before applying; identifying regions is not itself a finished CAD rebuild.

**Mesh to Solid** makes one face per triangle. It is heavy and hard to edit, so it is a last resort.

### From a scan to parametric features

Fitted CAD geometry and a parametric feature tree are different outputs. Rebuild from Scan does not recover the original sketches, dimensions, extrusions or fillets. Fit Surface to Region makes fitted CAD geometry; Freeform from Scan creates an editable cage. The tools report their output type so those routes are explicit.

To build your own parametric part:

1. Clean the mesh in Mesh if needed, then **Prepare → Align** it to useful planes.
2. Use **Section Sketch** through a representative part of the scan. Adjust the plane, offset and fitting tolerance; the report gives the largest sampled section fitting error.
3. Edit the resulting sketch. Add dimensions and appropriate geometric constraints manually. Remove unwanted outlines or segments: a tube cut through its axis has profiles on both sides, so keep only one radial profile before revolving it.
4. Extrude or revolve the sketch using the intended depth, axis and extent. Those features form the editable construction history. Change a driving sketch dimension to adjust the part.
5. Run **Deviation** with the scan as Mesh to check and the rebuilt CAD body as the reference. This samples the scan against the reference; it is not a complete, bidirectional certification of every rebuilt surface. A partial or missing scan cannot establish the intended shape of the missing area.

If automation fits the wrong local shape, use Select Region and Fit Surface to Region to specify a plane, cylinder, sphere, cone or smooth patch yourself. For unwanted mesh bumps, Select Region → Delete and patch and Mesh → Split / Mesh Boolean remain manual correction routes. Deviation helps review the result; it does not change the geometry.

## Maintained tests

[Mesh tool tests](../../../Prism.Tests/Headless/Mesh/MeshToolTests.cs) cover Repair and Fill Holes, Print Check (printers, custom volume, materials), Reduce, Smooth, Offset, Split (every plane, flip, open cut, keep options), Section Curves, Wall Thickness, Overhang orientation, Deviation, every Align mode, Mesh Boolean, Remesh, Mesh Quality and both conversions between meshes and solids. [Reverse engineering tests](../../../Prism.Tests/Headless/Mesh/ReverseEngineerTests.cs) cover Segment & Fit, Rebuild from Scan (faces, curves and sewn solid), Section Lines & Arcs, Section Sketch and Select Region actions. Run `./tools/test.ps1 -Feature Mesh`.

[Scoped Repair workflow tests](../../../Prism.Tests/Headless/Mesh/MeshRepairScopeTests.cs) cover complete and partial rims, protecting another opening and unselected faces, green preview boundaries, invalid selections, Undo and ribbon organization. [Scoped algorithm tests](../../../Prism.Tests/Unit/Mesh/MeshRegionRepairTests.cs) cover welding anchors, cleanup/colors, whole-shell selection and normal correction. Pointer-driven and real-scan checks remain in the [manual checklist](../../tabs/checklists/mesh-manual-test-checklist.md).

[Reverse Engineer workflow tests](../../../Prism.Tests/Headless/Mesh/ReverseEngineerWorkflowTests.cs) exercise the ribbon routes and section-sketch dimensioning followed by extrude/revolve and Deviation. [Profile fitting tests](../../../Prism.Tests/Unit/Mesh/ReverseEngineerProfileTests.cs) cover sparse straight-sided and dense circular sections. Hands-on checks are in the [Reverse Engineer checklist](../../tabs/checklists/reverse-engineer-manual-test-checklist.md).

Fixed on 2026-11-05:

- Section Curves (with "Create curves" off), Section Lines & Arcs (with "Create lines and arcs" off), Segment & Fit ("Nothing - just analyze", and a missing region number) and Rebuild from Scan ("Nothing - analysis…") used to clear their report before returning, so the panel showed no rows. They now keep it; report-only tests cover each mode.
- Solid to Mesh detail levels used to produce the same mesh on small curved bodies: a fixed angular limit outweighed the chord error, and a finer triangulation already stored on the body (such as the display mesh) was reused. Each level now has its own angular limit (Custom leaves the chord error in charge) and meshes a fresh copy of the geometry. The test asserts Coarse < Normal < Fine.

Fixed during the 2026-11-10 Reverse Engineer workflow verification: sparse rectangular section corners could fit a circle even though their straight edges departed from that circle. Arc fitting now checks edge interiors and the closing edge of a complete circle candidate. Section arc previews also sample the fitted curves instead of drawing only their start/mid/end chords, which made a circle preview look rectangular. REV-U001 / REV-H120 cover the rectangular tube section; REV-U002 / REV-H119 retain the circular route and verify the preview follows the circle.

## Tested on

Reverse Engineer verification on Windows Release, 2026-11-10: **75 Mesh Headless tests**, **6 Mesh Unit tests** and **45 standalone Reverse Engineer checks** passed. The diameter-driven cylinder extrusion matched 500π mm³, then rebuilt to 720π mm³ after changing the sketch diameter from 10 to 12 mm. A tube's axial section, manually trimmed to one radial outline and dimensioned, revolved to 168π mm³. Both initial solids reported 100% of sampled scan points within 0.05 mm in Deviation. Desktop build: zero errors, nine existing warnings; ribbon catalog freshness check passed.

Native **REV-I001** imported a synthetic Ø10 × 20 mm cylinder mesh, created a circular Section Sketch, added a 10 mm diameter dimension, extruded symmetrically through 20 mm and compared against the scan: valid solid volume 1570.7963 mm³ and 100% sampled points within 0.05 mm. [Section Sketch and ribbon](evidence/REV-I001-section-sketch.png); [rebuilt part and Deviation](evidence/REV-I001-rebuilt-deviation.png). The source revision and commands are recorded in [#124](https://github.com/chrizpro/prism/issues/124). Save/reopen, pointer-driven editing and real-scan robustness remain on the manual checklist.

Windows Release verification on 2026-11-10 passed **72 Mesh Headless tests** and **4 scoped-repair Unit tests**, with no failures or skips. The desktop build succeeded (zero errors, nine existing warnings), and the generated ribbon catalog passed its freshness check. Native scenario **MSH-I001** imported an eight-triangle box with top and bottom openings, selected the four triangles beside the top rim, and verified Repair defaults to Selected region: 8 → 10 triangles and 2 → 1 holes. Entire mesh preview instead produced 12 triangles and zero holes. [Selected-region capture](evidence/MSH-I001-selected-repair.png) and [entire-mesh capture](evidence/MSH-I001-entire-repair.png) show the ribbon, scope controls and green rims. The source revision and commands are recorded in [#123](https://github.com/chrizpro/prism/issues/123). Brush/lasso usability and real-scan checks remain manual.

Generated parts only: a drilled plate, an L bracket and a plate with a boss, remeshed and given up to ±0.2 mm of random noise. Each rebuilt to a valid solid with the right faces, within 0.3% of the volume, and Direct Edit pulled a face as it does on the original. No real scan has been tried, and real scans have holes, missing sides and uneven noise.

Section Sketch does not add constraints yet, so its sketches have no parallel, equal or tangent relations.

The older commands (Fill Holes, Section Curves, Section Lines & Arcs) are off the ribbon but still work from scripts.

Windows verification on 2026-11-05: the combined candidate passed 86 Unit and 592 Headless tests with rebuilt native libraries. See the [integration verification report](../../engineering/release-readiness-2026-11-05.md) for source revisions, commands and limits.
