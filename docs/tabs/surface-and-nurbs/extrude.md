# Surface Extrude — specification review

> **Requirements:** Proposed — awaiting owner review (2026-11-03).  
> **Verification:** Code and existing guides inspected; not run in the desktop application.

## Purpose and current workflow

Create a surface by extending a selected profile or open edge chain. The [Surface and NURBS tab guide](README.md) lists Extrude in both workflows. [The modeling guide](../../../site/guide/modeling.md) explains **Extrude Surface → Single edges** to select an open chain (for example three sides of a rectangle).

This is **not** the same contract as [Solid Extrude](../solid/extrude.md) or [Freeform profile extrusion](../freeform/extrude.md).

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

- **Feature story:** Pending link.
- **Implementation:** [`Prism/Commands/SolidCommands.cs`](../../../Prism/Commands/SolidCommands.cs), `ExtrudeCommand(surface: true)`.
- **Tests:** Relevant existing test coverage not established. No tests authored or run.
- **Delivery:** Future completed stories must link the pinned approved spec commit/tag; future changes get new issues.

See [the shared policy](../../engineering/requirements-and-testing.md). This page is a review draft, not a verified contract.
