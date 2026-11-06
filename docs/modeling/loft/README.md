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
