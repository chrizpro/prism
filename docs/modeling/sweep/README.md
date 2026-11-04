# Sweep — living specification

This README is the canonical requirements document for Surface, Freeform and NURBS Sweep. Keep reviewed requirements current here; issues track defined work and link to this document rather than duplicating it.

**Review state:** Proposed — awaiting owner review (2026-11-03). Current controls below are source observations. Existing-behavior characterization is authorized; unreviewed behavior must not silently become a product contract.

## Variants

| Variant | Entry point / implementation | Story |
|---|---|---|
| Surface | Surface → Create → Sweep; `SweepCommand(surface: true)` | [#18](https://github.com/chrizpro/prism/issues/18) |
| Freeform | Freeform → Create → Sweep; `FormSweepCommand()` | [#19](https://github.com/chrizpro/prism/issues/19) |
| NURBS | NURBS → Surfaces → Sweep; `SweepCommand(surface: true)` | [#20](https://github.com/chrizpro/prism/issues/20) |

Source: [ribbon](../../../Prism/ViewModels/MainViewModel.cs), [exact commands](../../../Prism/Commands/SolidCommands.cs), [Freeform commands](../../../Prism/Commands/FormBuildCommands.cs). The Surface/NURBS Sweep dropdown also contains Pipe, a separate command outside this specification.

## Exact command controls

| Control | Current implementation observation |
|---|---|
| Profile | Sketch, curve, face or selected edges; closed profile construction for a solid |
| Path | Curve or selected edge chain |
| Selection | Connected/tangent/partial-chain rules; profile/path/rail are each resolved into a shape |
| Profile orientation | Follow the path; Keep parallel (no turning); Keep upright (Z stays up) |
| Scale at the end | 0.01..100; default 1; used without a second rail |
| Second rail | Optional; switches to `Occ.SweepRails` and hides orientation/end-scale controls |
| Result | Surface and NURBS create a surface |

## Compatibility

The Solid ribbon and sketch marking menu omit Sweep. The solid-output implementation is retained for saved feature recipes and automation. [Former Solid story #17](https://github.com/chrizpro/prism/issues/17).

A profile is carried along a path into exact solid geometry. A rectangular 10 × 6 mm profile along a perpendicular 12 mm straight path has an independent expected volume of 720 mm³. For a linear end-scale factor k on that straight path, the benchmark volume is `A × L × (1 + k + k²) / 3`.

## Surface Sweep

Uses the exact controls with surface output and no solid Boolean selector. An open three-edge profile may produce uncapped surface faces; the straight-path benchmark independently checks area and absence of solids.

Open decisions: permitted disconnected/nonplanar profiles, boundaries on closed paths, normal orientation, end-scaling behavior and rail degeneracy. Command-level checks do not verify picking or preview display.

## Freeform Sweep

`FormSweepCommand` resamples a profile and path, transports the profile frame along successive tangents and creates a subdivision cage. Controls are Points on the profile (4..96, default 12), Steps along the path (2..200, default 16), Close the ends (default on) and Smoothness (1..4, default 3). Open paths may be reversed to start nearest the profile; closed paths append/weld the seam and do not add end caps.

Open decisions: zero-length/reversing tangents, curved/closed path seams, self-intersections, winding and manifoldness. Cage counts and closure on a straight example do not certify all geometric validity.

## NURBS Sweep

Uses the same exact surface command as Surface; this entry does not add an automatic NURBS conversion step. Shared command checks are relevant geometry evidence, but do not verify the NURBS tab workflow, control-point editing, degree/knot structure or continuity. Decide whether those should become explicit guarantees before adding requirement-based tests for them.

## Tests and evidence

- [Unit supporting math](../../../Prism.Tests/Unit/Sweep/).
- [Maintained headless command checks](../../../Prism.Tests/Headless/Sweep/).
- [Test runner and metadata](../../../Prism.Tests/README.md).

Use `./tools/test.ps1 -Feature Sweep`. Individual cases and expectations live in facts and Feature/Layer/TestId traits, not a duplicate option-to-test matrix.

Local verification on 2026-11-03: **2 Unit and 6 Headless Sweep cases passed**, including solid-output compatibility, Surface and Freeform cases. Windows/.NET SDK 10.0.102 with installed OCCT DLLs; no native rebuild. Rails, curved/closed paths, Keep upright, Booleans and full history/persistence remain unverified by this pilot. NURBS shares the surface implementation, but its UI workflow remains unverified. Existing standalone suites were preserved and not rerun.

Keep approved changes and open decisions here. Completed stories preserve the reviewed spec commit/release; later changes get new issues. See the [maintenance flow](../../engineering/requirements-and-testing.md#feature-documentation-and-test-maintenance).


Desktop integration: [SWP-I001 viewport evidence](../../integration-evidence/README.md#surface-sweep--swp-i001) covers straight-path Surface Sweep with Follow the path and Keep parallel.
