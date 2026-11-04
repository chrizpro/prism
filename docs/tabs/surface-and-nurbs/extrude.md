# Surface and NURBS Extrude — living specification

> **Requirements:** Proposed — awaiting owner review (2026-11-03).  
> **Verification:** Shared Surface command cases have run headlessly; the NURBS tab workflow has not been verified. Integration remains deferred.

## Purpose and current workflow

Create a surface by extending a selected profile or open edge chain. The [Surface and NURBS tab guide](README.md) lists Extrude in both workflows. [The modeling guide](../../../site/guide/modeling.md) explains **Extrude Surface → Single edges** to select an open chain (for example three sides of a rectangle).

This is the canonical living specification for the two exact surface entry points below. Their common controls and unresolved requirements are described once here and evolve with owner decisions.

This is **not** the same contract as [Solid Extrude](../solid/extrude.md) or [Freeform profile extrusion](../freeform/extrude.md).

## Surface Extrude

Surface → Create → Extrude calls `ExtrudeCommand(surface: true)` and creates an exact Surface object. Track this workflow in [Surface Extrude #7](https://github.com/chrizpro/prism/issues/7).

## NURBS Extrude

NURBS → Surfaces → Extrude calls the same `ExtrudeCommand(surface: true)`. Track this workflow separately in [NURBS Extrude #16](https://github.com/chrizpro/prism/issues/16), while sharing the control/behavior description below. The ribbon wiring is in [MainViewModel.cs](../../../Prism/ViewModels/MainViewModel.cs).

This command path does not add a separate conversion step. Shared Surface command tests do not verify the NURBS ribbon, selection workflow, resulting control-point editing or a particular NURBS representation. Whether this entry should remain an alias, and what conversion/parameterization/control-point guarantees it should provide, remain owner decisions.

## Observed implementation — not yet approved

[`ExtrudeCommand`](../../../Prism/Commands/SolidCommands.cs) accepts a `surface` constructor flag. The Surface path uses the same profile and extent picker as Solid but outputs an object of kind `Surface`, rather than using the solid result-operation pathway.

| Area | Source-observed behavior | Questions for approval |
| --- | --- | --- |
| Profile | Sketches, curves, faces, edges; multi-selection/edge intent | Which open, closed, disconnected and branching selections are permitted? |
| Extent | Distance, Two sides, Symmetric, To object, Through all, Through all both ways | Which modes make sense for surface creation? |
| Direction | Profile normal or reversed; signed distance | What is the expected normal for an open or nonplanar chain? |
| Refinement | Surface command hides the solid taper control | Should surface draft/taper be unsupported? |
| Output | Produces an exact Surface object | Expected behavior for closed profiles and multiple independent regions? |

## Proposed acceptance criteria — owner review required

| Review topic | Suggested user-visible behavior | State |
| --- | --- | --- |
| Profile selection | Selected open edge chains can produce a sheet surface when geometry is valid | Proposed |
| Closed profiles | Closed profiles follow an explicit, predictable surface result rule | Proposed |
| Direction/extent | Supported extents produce surfaces along the selected direction and magnitude | Proposed |
| Target selection | To-object and through-all have defined behavior for surfaces without target solids | Proposed |
| Invalid input | Disconnected, degenerate or unsupported selections produce useful feedback without document corruption | Proposed |
| History | Committed surface extrusions participate in the intended editing/undo workflow | Proposed |

No requirement IDs have been allocated or approved.

## Decisions needed

1. Should surface extrusion support nonplanar and branching edge selections?
2. Do we support the same six extent modes as Solid? If so, what does **Through all** mean with no solids?
3. Should closed profiles generate a surface shell, one surface per edge, or another form?
4. Are multiple regions/chains extruded as one feature or separate surfaces?
5. What tolerances and validation messages do we want?

## Traceability and history

- **Feature story:** [Story #7 — Surface Extrude review](https://github.com/chrizpro/prism/issues/7).
- **NURBS workflow story:** [Story #16 — NURBS Extrude review](https://github.com/chrizpro/prism/issues/16).
- **Implementation:** [`Prism/Commands/SolidCommands.cs`](../../../Prism/Commands/SolidCommands.cs), `ExtrudeCommand(surface: true)`.
- **Tests:** [SolidExtrudeTests.cs](../../../Prism.Tests/Headless/Extrude/SolidExtrudeTests.cs) contains the named Surface case; [ExtrudeEvidence.cs](../../../tests/Prism.DirectModelTests/ExtrudeEvidence.cs) contains additional legacy Surface extent checks. Individual cases and expectations remain in those files, not a duplicate matrix here. No NURBS-tab-specific maintained tests were identified.
- **Supporting Unit math:** [ExtrudeDirectionMathTests.cs](../../../Prism.Tests/Unit/Extrude/ExtrudeDirectionMathTests.cs) checks shared plane/vector operations; it does not independently verify Surface or NURBS workflow behavior.
- **Recorded verification:** Surface cases passed in earlier local runs on 2026-11-03; see the [test guide](../../../Prism.Tests/README.md) and [historical evidence](../../engineering/extrude-testing.md). This documentation update did not execute new tests or verify the NURBS workflow.
- **Delivery:** Future completed stories must link the pinned approved spec commit/tag; future changes get new issues.

See [the shared policy](../../engineering/requirements-and-testing.md). This page is a review draft, not a verified contract.
