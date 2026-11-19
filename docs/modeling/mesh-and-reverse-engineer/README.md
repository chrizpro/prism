# Mesh and scan conversion

The owner approved the combined Mesh tab and the conversion workflow on 2026-11-18. [Delivery issue #145](https://github.com/chrizpro/prism/issues/145) builds on the earlier [Mesh Repair approval #123](https://github.com/chrizpro/prism/issues/123) and [Reverse Engineer workflow approval #124](https://github.com/chrizpro/prism/issues/124).

| Mesh group | Tools |
|---|---|
| Prepare | Import Mesh (with Import Point Cloud and Reconstruct Mesh in its menu), Align |
| Analyze | Mesh Quality (with Clear Colors in its menu), Deviation |
| Repair | Repair, Edit Region, Remesh |
| Convert | Convert Mesh, Section Sketch |

Select, Datum, Transform, Inspect and Appearance remain available as shared sections. Print Check, Wall Thickness and Overhang & Orientation are available in CAM's print preparation. Use File → Export for mesh files. Superseded tools, including standalone Reduce and the triangle-by-triangle Mesh to Solid, use the command legacy attribute and remain available to existing scripts and saved feature recipes.

## Repair and local editing

### Removing something from a mesh

Open **Edit Region**, pick the area with smart patch, brush, rectangle or lasso, and press **Delete and patch**. The tool closes openings created by the deletion and keeps existing holes open.

- **Smooth** adds points at the mesh's own size and follows a curved surface fitted to the mesh around the opening.
- **Flat** only triangulates the opening.

**Smooth selected triangles** reduces local noise while keeping sharp edges, open edges and vertices shared with unselected triangles fixed.

### Controlling Repair

Repair's **Scope** section offers Entire mesh and Selected region. If a current nonempty region exists, opening Repair selects that mesh and defaults to Selected region. Otherwise it defaults to Entire mesh. Choose the scope explicitly before applying.

For a local repair, open Edit Region, select the triangles around an unwanted opening, then open Repair. A hole is eligible only when the triangle beside **every edge of its rim** is selected. Partial rims and openings outside the region stay open. Green outlines preview the holes that will be filled; the report counts filled candidates, protected holes and remaining holes. The hole limit is a count of rim edges, not a diameter in millimeters. Turning Fill holes off leaves hole filling disabled in either scope.

**Cleanup** controls merging duplicate vertices, removing degenerate/duplicate triangles, removing tiny floating shells and correcting normals. Selected-region mode restricts these operations too: vertices shared with unselected faces stay fixed, junk triangles outside the region remain, and a tiny shell is removed only if it is wholly selected. Normal correction uses surrounding unselected faces as anchors; it cannot fix an inconsistent surrounding mesh without changing the scope. Seam defects can require a larger selection or Entire mesh. Entire mesh retains the global cleanup behavior. There is no general automatic repair for self-intersections or non-manifold edges.

Selection is tied to one version of one mesh. A missing, empty, wrong-mesh or stale region blocks selected-region repair and asks you to select again. Applying a topology change requires a new region for the next local repair. Inspect the preview and rerun Mesh Quality / Print Check after applying. Undo restores the prior mesh.

**Remesh** has two modes. **Redistribute triangles** sets a target edge length and can preserve sharp and open edges. **Reduce Faces** simplifies by target triangle count or percentage and can keep open edges. Both can keep the source mesh. Check the triangle count and shape change before applying, especially around small features.

## Convert Mesh

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
2. Use **Section Sketch** through a representative part of the scan. Adjust the plane, offset and fitting tolerance; the report gives the largest sampled section fitting error.
3. Edit the resulting sketch. Add dimensions and appropriate geometric constraints manually. Remove unwanted outlines or segments: a tube cut through its axis has profiles on both sides, so keep only one radial profile before revolving it.
4. Extrude or revolve the sketch using the intended depth, axis and extent. Those features form the editable construction history. Change a driving sketch dimension to adjust the part.
5. Run **Deviation** with the scan as Mesh to check and the rebuilt CAD body as the reference. This samples the scan against the reference; it is not a complete, bidirectional certification of every rebuilt surface. A partial or missing scan cannot establish the intended shape of the missing area.

If automation fits the wrong local shape, use Edit Region and Convert Mesh to choose the intended surface type. For unwanted mesh bumps, use Edit Region → Delete and patch or Smooth selected triangles. Deviation helps review the result; it does not change the geometry.

## Maintained tests

[Combined Mesh workflow tests](../../../Prism.Tests/Headless/Mesh/MeshConsolidationTests.cs) cover open-solid consent, explicit region outputs, deviation in both directions, Freeform, face reduction and local smoothing.

[Mesh tool tests](../../../Prism.Tests/Headless/Mesh/MeshToolTests.cs) cover Repair and Fill Holes, Print Check (printers, custom volume, materials), Reduce, Smooth, Offset, Split (every plane, flip, open cut, keep options), Section Curves, Wall Thickness, Overhang orientation, Deviation, every Align mode, Mesh Boolean, Remesh, Mesh Quality and both conversions between meshes and solids. [Reverse engineering tests](../../../Prism.Tests/Headless/Mesh/ReverseEngineerTests.cs) cover Segment & Fit, Rebuild from Scan (faces, curves and sewn solid), Section Lines & Arcs, Section Sketch and Select Region actions. Run `./tools/test.ps1 -Feature Mesh`.

[Scoped Repair workflow tests](../../../Prism.Tests/Headless/Mesh/MeshRepairScopeTests.cs) cover complete and partial rims, protecting another opening and unselected faces, green preview boundaries, invalid selections, Undo and ribbon organization. [Scoped algorithm tests](../../../Prism.Tests/Unit/Mesh/MeshRegionRepairTests.cs) cover welding anchors, cleanup/colors, whole-shell selection and normal correction. Pointer-driven and real-scan checks remain in the [manual checklist](../../tabs/checklists/mesh-manual-test-checklist.md).

[Reverse Engineer workflow tests](../../../Prism.Tests/Headless/Mesh/ReverseEngineerWorkflowTests.cs) exercise the ribbon routes and section-sketch dimensioning followed by extrude/revolve and Deviation. [Profile fitting tests](../../../Prism.Tests/Unit/Mesh/ReverseEngineerProfileTests.cs) cover sparse straight-sided and dense circular sections. Hands-on checks are in the [Mesh conversion checklist](../../tabs/checklists/reverse-engineer-manual-test-checklist.md).

Fixed on 2026-11-05:

- Section Curves (with "Create curves" off), Section Lines & Arcs (with "Create lines and arcs" off), Segment & Fit ("Nothing - just analyze", and a missing region number) and Rebuild from Scan ("Nothing - analysis…") used to clear their report before returning, so the panel showed no rows. They now keep it; report-only tests cover each mode.
- Solid to Mesh detail levels used to produce the same mesh on small curved bodies: a fixed angular limit outweighed the chord error, and a finer triangulation already stored on the body (such as the display mesh) was reused. Each level now has its own angular limit (Custom leaves the chord error in charge) and meshes a fresh copy of the geometry. The test asserts Coarse < Normal < Fine.

Fixed during the 2026-11-10 Reverse Engineer workflow verification: sparse rectangular section corners could fit a circle even though their straight edges departed from that circle. Arc fitting now checks edge interiors and the closing edge of a complete circle candidate. Section arc previews also sample the fitted curves instead of drawing only their start/mid/end chords, which made a circle preview look rectangular. REV-U001 / REV-H120 cover the rectangular tube section; REV-U002 / REV-H119 retain the circular route and verify the preview follows the circle.

## Tested on

On 2026-11-18, **6 Unit and 83 Headless Mesh tests** passed on Windows Release using the workstation's staged OCCT runtime. The desktop build and ribbon catalog check passed. Desktop scenario **MSH-I002** imported an open 20 × 20 mm mesh, previewed a Solid conversion and confirmed it required explicit consent to keep the open result. It then selected the mesh with a pointer and fitted a 400.0 mm² CAD face. Both checks passed in two runs. See the [preview and checks](evidence/MSH-I002-convert-open-scan.png) and [#145](https://github.com/chrizpro/prism/issues/145) for the tested revision.

The synthetic cylinder from issue #88 still fitted oversize caps during automatic reconstruction. Convert Mesh reported a 1.719 mm maximum sampled scan-to-result distance and 2.076 mm result-to-scan distance. The geometry check flagged the candidate as invalid and the command withheld it from commit. The fitting issue remains open.


Reverse Engineer verification on Windows Release, 2026-11-10: **75 Mesh Headless tests**, **6 Mesh Unit tests** and **45 standalone Reverse Engineer checks** passed. The diameter-driven cylinder extrusion matched 500π mm³, then rebuilt to 720π mm³ after changing the sketch diameter from 10 to 12 mm. A tube's axial section, manually trimmed to one radial outline and dimensioned, revolved to 168π mm³. Both initial solids reported 100% of sampled scan points within 0.05 mm in Deviation. Desktop build: zero errors, nine existing warnings; ribbon catalog freshness check passed.

Native **REV-I001** imported a synthetic Ø10 × 20 mm cylinder mesh, created a circular Section Sketch, added a 10 mm diameter dimension, extruded symmetrically through 20 mm and compared against the scan: valid solid volume 1570.7963 mm³ and 100% sampled points within 0.05 mm. [Section Sketch and ribbon](evidence/REV-I001-section-sketch.png); [rebuilt part and Deviation](evidence/REV-I001-rebuilt-deviation.png). The source revision and commands are recorded in [#124](https://github.com/chrizpro/prism/issues/124). Save/reopen, pointer-driven editing and real-scan robustness remain on the manual checklist.

Windows Release verification on 2026-11-10 passed **72 Mesh Headless tests** and **4 scoped-repair Unit tests**, with no failures or skips. The desktop build succeeded (zero errors, nine existing warnings), and the generated ribbon catalog passed its freshness check. Native scenario **MSH-I001** imported an eight-triangle box with top and bottom openings, selected the four triangles beside the top rim, and verified Repair defaults to Selected region: 8 → 10 triangles and 2 → 1 holes. Entire mesh preview instead produced 12 triangles and zero holes. [Selected-region capture](evidence/MSH-I001-selected-repair.png) and [entire-mesh capture](evidence/MSH-I001-entire-repair.png) show the ribbon, scope controls and green rims. The source revision and commands are recorded in [#123](https://github.com/chrizpro/prism/issues/123). Brush/lasso usability and real-scan checks remain manual.

Generated parts only: a drilled plate, an L bracket and a plate with a boss, remeshed and given up to ±0.2 mm of random noise. Each rebuilt to a valid solid with the right faces, within 0.3% of the volume, and Direct Edit pulled a face as it does on the original. No real scan has been tried, and real scans have holes, missing sides and uneven noise.

Section Sketch does not add constraints yet, so its sketches have no parallel, equal or tangent relations.

The older commands (Fill Holes, Section Curves, Section Lines & Arcs) are off the ribbon but still work from scripts.

Windows verification on 2026-11-05: the combined candidate passed 86 Unit and 592 Headless tests with rebuilt native libraries. See the [integration verification report](../../engineering/release-readiness-2026-11-05.md) for source revisions, commands and limits.
