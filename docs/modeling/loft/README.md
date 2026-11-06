# Loft — living specification

This is the canonical requirements document for Surface, Freeform and NURBS Loft. Update this README as requirements evolve; issue descriptions link here and preserve their delivered scope.

**Review state:** Proposed — awaiting owner review (2026-11-03). Controls below describe source-observed implementation, not blanket owner approval. Characterization of existing behavior is authorized.

## Variants

| Variant | Entry point / implementation | Story |
|---|---|---|
| Surface | Surface → Create → Loft; `LoftCommand(surface: true)` | [#22](https://github.com/chrizpro/prism/issues/22) |
| Freeform | Freeform → Create → Loft; `FormLoftCommand()` | [#23](https://github.com/chrizpro/prism/issues/23) |
| NURBS | NURBS → Surfaces → Loft; `LoftCommand(surface: true)` | [#24](https://github.com/chrizpro/prism/issues/24) |

Source: [ribbon](../../../Prism/ViewModels/MainViewModel.cs), [exact Loft and GuidedLoft](../../../Prism/Commands/SolidCommands.cs), [Freeform Loft](../../../Prism/Commands/FormBuildCommands.cs).

## Exact command controls

| Control | Current implementation observation |
|---|---|
| Sections (in order) | Curves, selected edges or faces; connected picked edges on one object form a profile group; group pick order is retained |
| Guide rails | Optional one or two; source reports that extra groups beyond the first two are ignored |
| End at sketch point | Standalone point from a visible sketch; one profile may loft to that vertex; incompatible with rails or Closed loft |
| Ruled transitions | Straight (ruled) transitions toggle; default off |
| Closed loft | Connects the last profile back to the first when there are more than two sections |
| Rail accuracy | 4..40 intermediate sections; default 12; shown with rails |
| Result | Surface and NURBS create a surface |

GuidedLoft samples intermediate sections and transforms them along the selected rail(s). It is not a documented universal G0/G1/G2 continuity contract. Existing continuity expectations require review rather than assumption from a successful build.

## Compatibility

The Solid ribbon and sketch marking menu omit Loft. The solid-output implementation is retained for saved feature recipes and automation. [Former Solid story #21](https://github.com/chrizpro/prism/issues/21).

Creates exact solid geometry between supported closed sections. Equal 10 × 6 mm rectangles 12 mm apart give an independent 720 mm³ prism benchmark. Similar rectangles with half-scale final dimensions have frustum volume `A × H × (1 + k + k²) / 3` for the ruled benchmark.

## Surface Loft

Uses the same controls with surface output; open profiles can create an uncapped sheet. A straight loft between matching open three-edge sections provides a simple analytic area benchmark.

Open decisions: mixed open/closed sections, profile grouping, normal orientation, partial boundaries and surface feature history. Numerical sheet checks do not establish the selection/preview workflow.

### Sketch rails obscured by the preview

2026-11-05 owner report: in Front view, with two profiles selected and Guide rails picking active, the visible third sketch's rails cannot be seen against the loft preview. Source inspection at `f6fd49e` identified the display path: `GlViewport.DrawScene` draws sketch/curve edges before opaque command-preview faces; selected section/rail overlays are drawn afterward. CPU picking uses document objects rather than preview faces, so the preview itself does not block edge picking. The correction was verified with an isolated planar fixture; the owner's unsaved model was not modified.

Workaround: turn **Preview** off while selecting rails, then enable it again to inspect the loft.

**Approved and implemented (2026-11-05):** the owner approved this specific display correction with “Proceed with the fix.” While the exact Surface/NURBS Loft command is collecting profiles or guide rails, visible sketch/curve candidates are drawn over the preview in their object colors, with hover highlighting and the existing blue section/orange rail highlights. Explicit visibility and temporary hidden-object state are respected. Other commands and committed geometry keep their existing display behavior. This approval does not settle the other proposed Loft requirements.

Verification on the configured Windows workstation: `dotnet test Prism.Tests/Headless/Prism.HeadlessTests.csproj -c Release --filter Feature=Loft` passed **7 cases**, including LFT-H007 for front-view sketch rail overlay, picking/removal and hidden-sketch exclusion. `dotnet test Prism.Tests/Integration/Prism.IntegrationTests.csproj -c Release --filter TestId=LFT-I002` captures the actual preview with an unselected sketch rail. A separate computer-use check exercised Guide rails → Pick geometry, selected the rail with a mouse click and removed it with another click. [LFT-I002 viewport evidence and scope](../../integration-evidence/README.md#sketch-rail-visibility--lft-i002). Desktop/MCP were rebuilt; installed native binaries were reused. NURBS shares the command, but its ribbon workflow was not separately exercised.

### Curved rails and slow preview diagnosis

2026-11-05 follow-up report: rail visibility is corrected, but the guided surface pulls away from the left rail and preview computation appears to hang. An isolated reproduction from the owner's three sketches (two elliptical profiles at Z=0/120 and two interpolating spline rails in XZ) built in 1.555 s and tessellated in 16.315 s to 159,491 triangles. The surface was topologically valid, but 13 samples per rail measured gaps up to 0.276 mm on the right and 1.777 mm on the left. These are observed samples, not certified maximum errors. The live command/selection was retained; a diagnostic save adopted a local artifact path.

**Approved (2026-11-05):** the owner replied “Proceed with the geometry fix” to fitting intermediate sections against both rails in each section's plane, preserving end profiles and stabilizing closed-section alignment, with maintained regression and desktop checks. The implementation uses the first rail's sampled position and the second rail's intersection with the same section plane, instead of unrestricted nearest points on whole rails. Rail sampling is oriented against the first profile, including reversed profile order. Closed slices use a continuous seam location and common periodic knot spacing to avoid combining many slightly different knot sequences. General nonparallel, looping/multiple-crossing rails, continuity guarantees and global progress/cancellation remain unresolved. A section plane with no unique second-rail crossing is rejected rather than fitted against an unrelated height.

For opposite-side rails on closed profiles, the fitter uses the section diameter in the rail-span direction. This avoids unstable nearest points inside an oval jumping between its front/back sides. End sections retain their exact curves while their periodic seams are aligned; intermediate closed curves use uniform periodic interpolation. The new native entry point requires rebuilding the kernel together with the managed application.

Verification on 2026-11-06: **14 Loft Headless cases passed** on Windows with a rebuilt native kernel. LFT-H008 checks the reported ellipse/spline fixture with both profile orders and both rail orders: end-profile samples have zero measured gap; the maximum of 49 samples per rail is 0.024618 mm at default accuracy 12. This is a sampled approximation, not a universal exact-coincidence tolerance. Display meshes contain 6,770–9,692 triangles and took 1.674–1.948 s; builds took 0.646–0.748 s in that run. LFT-H009 checks one/two straight rails and reversed rail direction against an analytic square-frustum volume. Timings are dated measurements, not cross-platform guarantees; tests bound triangle count and geometric error rather than wall-clock time. Desktop evidence: [LFT-I003](../../integration-evidence/README.md#curved-ellipse-loft--lft-i003).

The final desktop run on 2026-11-06 passed **4 Loft Integration cases**, including LFT-I003 preview/commit/save and the existing straight-section/visibility scenarios. The native kernel, Desktop and MCP were rebuilt. [Actual viewport evidence and native hash](../../integration-evidence/README.md#curved-ellipse-loft--lft-i003).

## Freeform Loft

Creates a subdivision cage through two or more curves/sketches/edges in order. Controls: Points per section (4..96, default 16), Close the ends for closed sections (default on) and Smoothness (1..4, default 3). It resamples and aligns successive sections; mixing open and closed profiles is rejected in the implementation.

Open decisions: alignment and starting-point rules, resampling corners, cap winding, manifoldness, self-intersections and the relationship between smoothness and cage topology. A simple capped cage test is representative evidence, not complete validity certification.

## NURBS Loft

The NURBS ribbon invokes the same exact Surface Loft command without an additional conversion step. Shared surface-command checks do not certify the NURBS ribbon, subsequent point editing, knot/degree structure, or continuity. Define any such promises through owner review.

## Tests and evidence

- [Unit supporting math](../../../Prism.Tests/Unit/Loft/).
- [Maintained headless command checks](../../../Prism.Tests/Headless/Loft/).
- Existing standalone [Loft checks](../../../tests/Prism.LoftTests/Program.cs) and [selection checks](../../../tests/Prism.LoftPickTests/Program.cs) remain intact; they are not assumed replaced by this pilot.
- [Runner and metadata](../../../Prism.Tests/README.md).

Use `./tools/test.ps1 -Feature Loft`. Detailed options and cases remain in facts and traits; no issue-to-option matrix is maintained.

Local verification on 2026-11-03: **2 Unit and 6 Headless Loft cases passed**, including solid-output compatibility, Surface and Freeform cases. Windows/.NET SDK 10.0.102 with installed OCCT DLLs; no native rebuild. Additional rails, endpoints, regeneration, Booleans, closed lofts and error paths remain coverage work. NURBS entry-point behavior is not separately verified. Existing standalone suites were preserved and not rerun.

Update this living document when approved requirements change. Closed stories pin their reviewed spec commit/release; future changes get separate issues. See the [maintenance flow](../../engineering/requirements-and-testing.md#feature-documentation-and-test-maintenance).


Desktop integration: [LFT-I001 viewport evidence](../../integration-evidence/README.md#surface-loft--lft-i001) covers straight-section Surface Loft with Smooth and Ruled transitions.
