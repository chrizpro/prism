# Extrude

This guide collects the Extrude workflows by variant. Each section retains its own review status and verification scope.

- [Solid](#solid-extrude)
- [Surface and NURBS](#surface-and-nurbs-extrude)
- [Freeform](#freeform-extrusion)

## Solid Extrude

> **Requirements status:** Proposed — awaiting owner review (2026-11-03).
>
> **Implementation status:** Representative exact command behavior has been verified headlessly; this is not complete UI or option coverage.
>
> This is a draft living specification, **not** an approved contract or an assertion that every option works. The owner authorized characterization of existing observable behavior on 2026-11-03. New or disputed requirements still require review before tests encode them as settled behavior.

### Purpose and scope

The Solid Extrude command extends selected sketch profiles, curves or faces along a direction to create or modify exact solid geometry. It is distinct from **Surface Extrude**, which creates a surface result, and **Freeform Extrude**, which operates on subdivision/cage topology. Their requirements should be reviewed separately even if the tools share a label.

User journey (from existing documentation): **Home / Solid → Create → Features → Extrude**. Select a profile, choose its extent, preview the result and specify the result operation.

### Observed in existing code (not yet approved requirements)

Source: [`Prism/Commands/SolidCommands.cs`](../../../Prism/Commands/SolidCommands.cs), `ExtrudeCommand`; complementary [modeling guide](../../../site/guide/modeling.md). These are implementation observations, not guarantees of correct runtime behavior.

| Area | Options / current implementation | Question for review |
| --- | --- | --- |
| Profile | Sketch object, curve object, face or selected edge(s); multi-selection with selection intent | Which inputs are valid for a *solid*? How should invalid/open profiles report failure? |
| Extent | Distance, Two sides, Symmetric, To object, Through all, Through all both ways | Exact directional and stop behavior, tolerances |
| Direction | Profile normal, with reverse-direction toggle; negative distance supported | Should negative distance and reverse toggle both be supported? |
| Two sides / symmetric | Two explicit distances; symmetric splits total length across both sides | Should negative primary distance be permitted with Two sides? |
| To object | Face, plane or body target; near/far selection, offset | What should happen for curved targets or targets behind profile? |
| Taper | Taper angle on linear **solid** extents | Supported range and failure cases |
| Regions | Supports multiple closed regions with an optional region index | How should selecting one vs all regions behave, especially with multiple sketch planes? |
| Result operation | Solid result behavior delegated to common `BodyCommand` operation handling | Confirm actual operation choices, target selection and multi-body rules |
| Special case | Plain one-way Cut may reverse automatically toward a body | Should automatic reversal be expected, or should direction always be explicit? |
| Zero length | Kernel path rejects near-zero length; command may yield no preview | Required user-facing validation and message |

#### To object — characterized existing behavior

Select a separate reference body, face or plane in **Extend to (face, plane or body)**. With a 10 × 6 mm profile at Z=0 and a box occupying Z=20..24, **Near side** gives a 20 mm extrusion and **Far side** gives 24 mm. **Offset from the target** is added to that signed forward distance: −2 mm from the near side leaves a 2 mm gap. These are current implementation observations, not approval of all target semantics.

The maintained body-target checks use New body, so the reference remains a separate unchanged solid after commit. A missing selected target produces no output. A target behind the active direction reports an error; Reverse direction can make that target reachable.

For a body target, the current code projects its bounding-box limits onto the extrusion direction. For a planar face or construction plane, it calculates a plane intersection from the profile center. These paths are not a general per-point trim to arbitrary curved geometry. Curved/nonintersecting targets, plane/face selection, offsets and tolerances still need their own review and additional coverage. See the feature-level test links below for the executable checks rather than a duplicated option-to-test matrix.

#### Related feature variants

- [Surface Extrude draft](#surface-and-nurbs-extrude) ([Story #7](https://github.com/chrizpro/prism/issues/7)): exposes Extrude and uses the same command class with surface output; should have its own approved scope.
- [Freeform Extrude Profile draft](#freeform-extrusion) ([Story #8](https://github.com/chrizpro/prism/issues/8)): cage-based extrusion uses a different command; no Solid behavior should be assumed for it.
- [Sheet Metal / ECAD / Script](../../interface/script/README.md): Sheet Metal includes an Extrude cut-out entry; confirm whether it uses Solid semantics before sharing any requirements.

### Proposed acceptance criteria — **not approved**

These describe possible expected **observable behavior** for owner review, not assertions that Prism currently meets them.

| Review area | Proposed observable behavior | Decision |
| --- | --- | --- |
| Valid input | A valid closed planar profile can produce a valid solid | Awaiting owner |
| Direction | A user can control extrusion direction predictably, including the reverse toggle | Awaiting owner |
| Extent modes | Distance, Two sides and Symmetric use clearly defined signed-distance semantics | Awaiting owner |
| Target extents | To object gives predictable results when a selected target intersects the extrusion direction | Awaiting owner |
| Boolean result | Solid operation mode (new body, add, cut, intersect if offered) is explicit and uses defined target selection | Awaiting owner |
| Editing | An editable feature can be adjusted without silently changing unrelated solids | Awaiting owner |
| Failure handling | Invalid, zero-length and nonintersecting cases fail without corrupting the document and show actionable feedback | Awaiting owner |
| Undo/redo | Accepted extrusions and edits participate in the expected history/undo workflow | Awaiting owner |

No requirement IDs have been assigned. Add stable IDs selectively **after** each behavior is defined and approved.

### Decisions needing owner review

1. Should Solid Extrude strictly require closed planar profiles, and how should selected edge chains behave?
2. For **Through all**, should the result reference only selected target bodies or all intersected visible solids? How should it behave with no eligible body?
3. Should the one-way Cut auto-reversal remain, or should direction stay entirely user-controlled?
4. For multiple closed regions, should default behavior extrude them all or require an explicit region choice?
5. What does **To object** mean for nonplanar targets, and what are the expected offset and tolerance rules?
6. What error experience should users see for zero distance, invalid profile topology and unsupported intersections?
7. Which operation choices and target-selection rules should be contractually supported?

Review these before requesting desktop agents to implement or add tests.

### Verification and traceability

- **Existing implementation:** [`ExtrudeCommand`](../../../Prism/Commands/SolidCommands.cs) and shared `BodyCommand` operation logic.
- **Existing user documentation:** [modeling guide](../../../site/guide/modeling.md) documents profile selection and the distinction between open surface chains and closed solid profiles.
- **Maintained tests:** [Extrude headless tests](../../../Prism.Tests/Headless/Extrude/) and [Extrude unit math tests](../../../Prism.Tests/Unit/Extrude/). Individual options and expectations are described by the facts and traits in the code. Passing characterization does not approve the draft acceptance criteria above.
- **Recorded verification:** Representative command checks have run on the configured Windows workstation using the installed OCCT DLLs; see the [test guide](../../../Prism.Tests/README.md). Integration remains deferred, and untested paths are not implied to pass.
- **Feature story:** [Story #6 — Solid Extrude requirements review](https://github.com/chrizpro/prism/issues/6). This tracks owner review; it does not certify implementation or testing.
- **Delivery history:** No approved delivery version recorded by this draft. Future closed stories should link the exact reviewed specification commit or release tag as well as this living page.

### Known documentation limits

The source inspection is not a user acceptance test. This draft may miss existing UI behavior and failure modes. Treat discrepancies as review questions and confirm with a desktop walkthrough before approving requirements.

### Related guidance

- [Tab guide index](../../tabs/README.md)
- [Requirements, GitHub and testing workflow](../../engineering/requirements-and-testing.md)

## Surface and NURBS Extrude

> **Requirements:** Proposed — awaiting owner review (2026-11-03).
>
> **Verification:** Shared Surface command cases have run headlessly; the NURBS tab workflow has not been verified. Integration remains deferred.

### Purpose and current workflow

Create a surface by extending a selected profile or open edge chain. The [Surface and NURBS tab guide](../surface-and-nurbs/README.md) lists Extrude in both workflows. [The modeling guide](../../../site/guide/modeling.md) explains **Extrude Surface → Single edges** to select an open chain (for example three sides of a rectangle).

This is the canonical living specification for the two exact surface entry points below. Their common controls and unresolved requirements are described once here and evolve with owner decisions.

This is **not** the same contract as [Solid Extrude](#solid-extrude) or [Freeform profile extrusion](#freeform-extrusion).

### Surface Extrude

Surface → Create → Extrude calls `ExtrudeCommand(surface: true)` and creates an exact Surface object. Track this workflow in [Surface Extrude #7](https://github.com/chrizpro/prism/issues/7).

### NURBS Extrude

NURBS → Surfaces → Extrude calls the same `ExtrudeCommand(surface: true)`. Track this workflow separately in [NURBS Extrude #16](https://github.com/chrizpro/prism/issues/16), while sharing the control/behavior description below. The ribbon wiring is in [MainViewModel.cs](../../../Prism/ViewModels/MainViewModel.cs).

This command path does not add a separate conversion step. Shared Surface command tests do not verify the NURBS ribbon, selection workflow, resulting control-point editing or a particular NURBS representation. Whether this entry should remain an alias, and what conversion/parameterization/control-point guarantees it should provide, remain owner decisions.

### Observed implementation — not yet approved

[`ExtrudeCommand`](../../../Prism/Commands/SolidCommands.cs) accepts a `surface` constructor flag. The Surface path uses the same profile and extent picker as Solid but outputs an object of kind `Surface`, rather than using the solid result-operation pathway.

| Area | Source-observed behavior | Questions for approval |
| --- | --- | --- |
| Profile | Sketches, curves, faces, edges; multi-selection/edge intent | Which open, closed, disconnected and branching selections are permitted? |
| Extent | Distance, Two sides, Symmetric, To object, Through all, Through all both ways | Which modes make sense for surface creation? |
| Direction | Profile normal or reversed; signed distance | What is the expected normal for an open or nonplanar chain? |
| Refinement | Surface command hides the solid taper control | Should surface draft/taper be unsupported? |
| Output | Produces an exact Surface object | Expected behavior for closed profiles and multiple independent regions? |

### Surface and NURBS acceptance criteria for owner review

| Review topic | Suggested user-visible behavior | State |
| --- | --- | --- |
| Profile selection | Selected open edge chains can produce a sheet surface when geometry is valid | Proposed |
| Closed profiles | Closed profiles follow an explicit, predictable surface result rule | Proposed |
| Direction/extent | Supported extents produce surfaces along the selected direction and magnitude | Proposed |
| Target selection | To-object and through-all have defined behavior for surfaces without target solids | Proposed |
| Invalid input | Disconnected, degenerate or unsupported selections produce useful feedback without document corruption | Proposed |
| History | Committed surface extrusions participate in the intended editing/undo workflow | Proposed |

No requirement IDs have been allocated or approved.

### Surface and NURBS decisions needed

1. Should surface extrusion support nonplanar and branching edge selections?
2. Do we support the same six extent modes as Solid? If so, what does **Through all** mean with no solids?
3. Should closed profiles generate a surface shell, one surface per edge, or another form?
4. Are multiple regions/chains extruded as one feature or separate surfaces?
5. What tolerances and validation messages do we want?

### Surface and NURBS traceability and history

- **Feature story:** [Story #7 — Surface Extrude review](https://github.com/chrizpro/prism/issues/7).
- **NURBS workflow story:** [Story #16 — NURBS Extrude review](https://github.com/chrizpro/prism/issues/16).
- **Implementation:** [`Prism/Commands/SolidCommands.cs`](../../../Prism/Commands/SolidCommands.cs), `ExtrudeCommand(surface: true)`.
- **Tests:** [SolidExtrudeTests.cs](../../../Prism.Tests/Headless/Extrude/SolidExtrudeTests.cs) contains the named Surface case; [ExtrudeEvidence.cs](../../../tests/Prism.DirectModelTests/ExtrudeEvidence.cs) contains additional legacy Surface extent checks. Individual cases and expectations remain in those files, not a duplicate matrix here. No NURBS-tab-specific maintained tests were identified.
- **Supporting Unit math:** [ExtrudeDirectionMathTests.cs](../../../Prism.Tests/Unit/Extrude/ExtrudeDirectionMathTests.cs) checks shared plane/vector operations; it does not independently verify Surface or NURBS workflow behavior.
- **Recorded verification:** Surface cases passed in earlier local runs on 2026-11-03; see the [test guide](../../../Prism.Tests/README.md) and [historical evidence](../../engineering/extrude-testing.md). This documentation update did not execute new tests or verify the NURBS workflow.
- **Delivery:** Future completed stories must link the pinned approved spec commit/tag; future changes get new issues.

See [the shared policy](../../engineering/requirements-and-testing.md). This page is a review draft, not a verified contract.

## Freeform extrusion

> **Requirements:** Proposed — awaiting owner review (2026-11-03).
>
> **Verification:** Source inspected and legacy Profile characterization recorded; no dedicated maintained Profile tests or full desktop workflow verification. See the evidence links below.

### Two different Freeform extrusion workflows

The Freeform ribbon lists **Create → Extrude Profile** and **Modify → Extrude**; they should not be treated as the same feature.

- **Extrude Profile:** Constructs a new editable subdivision cage from a selected sketch, curve, or edge.
- **Modify → Extrude:** Edits an existing cage selection; the Freeform guide notes an extrusion amount in the Edit Freeform panel. The precise face/edge/vertex semantics need desktop inspection.

This page pilots **Extrude Profile** requirements. The Modify command remains in scope for documentation discovery, not an assumed clone of the profile command.

### Observed Extrude Profile implementation

[`FormExtrudeCommand`](../../../Prism/Commands/FormShapeCommands.cs) offers these source-observed controls:

| Area | Current code | Review question |
| --- | --- | --- |
| Profile | Curve, sketch or edge | Which open/closed profiles work and how do they handle corners? |
| Distance | Numeric signed distance; initial 30 | Should a zero-distance form be allowed? |
| Direction | Profile normal or world X/Y/Z | Should negative world axes also have explicit UI options? |
| Far end | Scale factor from 0.01 to 20 (initial 1) | Should end scale be allowed to collapse to zero? |
| Topology | 1–60 steps along way, 4–96 profile points | Should these ranges and resampling be user-visible guarantees? |
| Ends | Close the ends checkbox (closed profile) | What happens when open profiles request caps? |
| Display | Smoothness level 1–4 | How does this relate to actual control cage topology? |
| Result | New Freeform (`ObjKind.Form`) cage with point/face counts | What editing/history promises should be made? |

The implementation resamples profile points, constructs rows along the extrusion direction and creates a new form cage. It is distinct from OCCT solid/surface extrusion.

### Surface and NURBS acceptance criteria for owner review

| Review topic | Proposed expected behavior | State |
| --- | --- | --- |
| Profile to form | A valid profile can create an editable freeform cage | Proposed |
| Open profiles | Supported open profiles create a predictable open cage topology | Proposed |
| Closed profiles | Supported closed profiles can create a capped cage when requested | Proposed |
| Scaling | End scale smoothly interpolates between the starting and ending sections | Proposed |
| Direction | Direction options give predictable cage orientation | Proposed |
| Validity | Degenerate profiles and invalid topology fail with useful messages, preserving the document | Proposed |
| Modification | The separate Edit Freeform **Modify → Extrude** tool has documented selection and topology rules | Needs separate review |

No requirements have been approved or given stable IDs.

### Surface and NURBS decisions needed

1. Should **Extrude Profile** and **Modify → Extrude** receive separate stories? They are distinct implementations.
2. How should zero length, negative distance, degenerate normals and scaling toward zero work?
3. For open profiles, are end caps never supported, or do we need a separate definition?
4. What topological invariants should the tool enforce (manifoldness, winding, boundary edges)?
5. Should the smoothing setting only affect the display or the created cage's subdivision properties?

### Surface and NURBS traceability and history

- **Feature story:** [Story #8 — Freeform Extrude Profile review](https://github.com/chrizpro/prism/issues/8).
- **Implementation:** [`Prism/Commands/FormShapeCommands.cs`](../../../Prism/Commands/FormShapeCommands.cs), `FormExtrudeCommand`; existing [Freeform guide](../freeform/README.md) describes Modify → Extrude separately.
- **Tests:** [Existing executable characterization](../../../tests/Prism.DirectModelTests/ExtrudeEvidence.cs) includes Freeform Profile cases; [historical local evidence](../../engineering/extrude-testing.md) records the earlier combined run. No dedicated maintained Freeform Profile Unit/Headless classes exist yet; [the test guide](../../../Prism.Tests/README.md) identifies this gap. Passing legacy cases does not approve the proposed requirements above.
- **Delivery:** When approved and delivered, closed stories link their pinned specification version; future changes get new stories or bugs.

[Shared requirements policy](../../engineering/requirements-and-testing.md)
