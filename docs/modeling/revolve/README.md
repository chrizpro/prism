# Revolve — living specification

This is the canonical requirements document for **Solid, Surface, Freeform and NURBS Revolve**. Update this README when requirements evolve; issues link to its variant sections and track a defined piece of review or delivery work. Do not maintain a second copy of the requirements in issue descriptions.

**Requirements status:** Proposed — awaiting owner review (2026-11-03). The controls below are observed in the current source, not automatically approved guarantees. Existing-behavior characterization is authorized; new or disputed behavior still needs an owner decision.

**Verification status:** Representative exact Solid/Surface command cases have run headlessly. Freeform Revolve and the NURBS tab workflow have not been verified by the maintained suite. Integration and viewport testing remain deferred.

## Variants and implementation

| Variant | Entry point | Implementation | Review story |
|---|---|---|---|
| [Solid](#solid-revolve) | Home / Solid → Create → Features → Revolve | `RevolveCommand()` | [#12](https://github.com/chrizpro/prism/issues/12) |
| [Surface](#surface-revolve) | Surface → Create → Revolve | `RevolveCommand(surface: true)` | [#13](https://github.com/chrizpro/prism/issues/13) |
| [Freeform](#freeform-revolve) | Freeform → Create → Revolve | `FormRevolveCommand()` | [#14](https://github.com/chrizpro/prism/issues/14) |
| [NURBS](#nurbs-revolve) | NURBS → Surfaces → Revolve | `RevolveCommand(surface: true)` | [#15](https://github.com/chrizpro/prism/issues/15) |

Source: [ribbon definitions](../../../Prism/ViewModels/MainViewModel.cs), [exact commands](../../../Prism/Commands/SolidCommands.cs), and [Freeform commands](../../../Prism/Commands/FormShapeCommands.cs). Surface and NURBS are separate workflows using the same exact command; Freeform creates a cage through a different implementation.

## Exact Revolve controls

These observations apply to Solid, Surface and the NURBS entry point. Their shared description lives here so the three workflows do not acquire contradictory copies.

| Control | Source-observed behavior | Review boundary |
|---|---|---|
| Profile | Sketch, curve, face or selected edges; supports multiple selections and profile-selection intent | Define permitted closure, planarity, disconnected regions and self-intersections for each output |
| Axis | World X, World Y, World Z, or Pick a line; default World Z | Confirm permitted axis selections and handling of profiles touching/crossing the axis |
| Picked axis | Origin axis, datum, or a selected edge sampled to obtain endpoints and a direction | Decide whether non-straight edges should be rejected rather than interpreted by endpoints |
| Angle | Signed primary angle, −360° to 360°; default 360° | Confirm zero-angle diagnostics and negative-angle semantics |
| Full 360° | Builds a full revolution independently of the primary angle; the current branch does not apply the reverse toggle | Decide the intended reverse-toggle behavior in full mode |
| Two sides | Primary angle plus a nonnegative second angle; starts behind the profile by the second angle | Define negative primary angles and combined spans beyond a full turn |
| Symmetric | Splits the primary total angle across both sides of the profile | Confirm signed-angle behavior and tolerances |
| Reverse direction | Changes the angular sign for Angle, Two sides and Symmetric | Confirm interactions with signed angles and handles |

The panel follows Profile → Axis → Extent → Result where applicable. Source checks for near-zero totals in Two sides and Symmetric return no output; the plain Angle branch delegates to the kernel. Do not promise identical error messages for these paths without verification.

## Solid Revolve

**Purpose:** Rotate a closed profile around an axis to create or modify exact solid geometry.

The command resolves profiles as faces and uses the shared exact controls above. Result operations are New body, Join, Cut and Intersect through `BodyCommand`; target selection and Boolean behavior belong to that shared implementation. The current maintained tests explicitly use New body, rather than certifying all Boolean modes.

**Characterized example:** An XZ rectangle spanning radius 2..5 mm and height 0..8 mm, revolved around world Z, produces an annular solid. Full volume is `π × (5² − 2²) × 8 = 168π mm³`; partial/reversed/symmetric cases check numerical volume and bounding limits independently of the preview.

**Proposed expectations for review:** Valid closed inputs should produce valid, dimensionally correct solids; extent and axis choices should be predictable; invalid input should preserve the document; committed features should support the intended editing/history/persistence workflow. These broad expectations need explicit owner decisions for edge cases before becoming an approved contract.

**Open decisions:** Axis-crossing/self-intersecting profiles, multi-region handling, unsupported axes, Boolean target rules, zero/full-turn tolerances and the exact failure experience. Additional feature editing/regeneration and persistence coverage remains outstanding.

Story: [Solid Revolve #12](https://github.com/chrizpro/prism/issues/12).

## Surface Revolve

**Purpose:** Rotate a profile into exact surface geometry using the Surface tab.

The command resolves the profile without requiring face construction and adds an `ObjKind.Surface` result. It uses the exact controls above and does not offer the Solid result-operation selector. The implementation permits curve/edge input, but the complete supported open/closed selection contract still needs review.

**Characterized example:** Fully revolving the same closed rectangular wire yields a valid four-face surface shell with no solid. Its analytic area is `2π × (5 + 2) × 8 + 2π × (25 − 4) = 154π mm²`. That is representative shared-command evidence, not a guarantee for all profile types or angles.

**Proposed expectations for review:** Supported open/closed profiles should create a predictable sheet/shell result; changing extent or axis should preserve the intended surface boundaries; invalid selections should fail without corrupting the document.

**Open decisions:** Open profile boundaries, partial-turn seams, multiple chains, consistent normal orientation, cap/shell expectations and surface feature editing/history semantics.

Story: [Surface Revolve #13](https://github.com/chrizpro/prism/issues/13).

## Freeform Revolve

**Purpose:** Spin a profile into a new editable subdivision cage, rather than exact OCCT geometry.

| Control | Source-observed behavior |
|---|---|
| Profile | One selected curve, sketch or edge; reads and resamples a profile chain |
| Axis through the origin | X, Y or Z; default Z; no picked arbitrary-axis control |
| Angle (degrees) | 1°..360°; default 360° |
| Segments around | 3..64; default 8 |
| Points on the profile | 4..96; default 12 |
| Close the open ends (360° only) | Default on; fills holes for an open profile only in a full revolution |
| Smoothness (levels) | 1..4; default 3 |

The implementation creates rows of resampled points, builds a cage grid, welds the seam on a full turn, and attempts an outward orientation. A partial revolution retains its angular boundaries. The result is `ObjKind.Form`; the command reports cage point/face counts. None of these source observations certifies manifoldness or absence of self-intersections for all inputs.

**Proposed expectations for review:** Supported profiles should create editable cages with predictable seams/boundaries; sampling and smoothing controls should have documented effects; unsupported/degenerate inputs should preserve the document and provide feedback.

**Open decisions:** Profiles on or across the axis, seam welding tolerance, cap behavior, winding/manifoldness, self-intersections, topology limits and the distinction between display smoothness and cage topology. No dedicated maintained Freeform Revolve tests were identified in this review.

Story: [Freeform Revolve #14](https://github.com/chrizpro/prism/issues/14).

## NURBS Revolve

**Purpose:** Expose profile revolution in the NURBS styling workflow.

The NURBS ribbon calls the same `RevolveCommand(surface: true)` as Surface. Its current controls and exact surface output are described once in [Exact Revolve controls](#exact-revolve-controls) and [Surface Revolve](#surface-revolve). This ribbon command path does not add a separate conversion step.

The distinct issue tracks NURBS workflow requirements and verification. Shared surface-command tests provide implementation evidence, but they do not verify the NURBS ribbon entry, selection, subsequent control-point editing, or any particular NURBS representation.

**Open decisions:** Should NURBS Revolve remain an alias of Surface Revolve? What automatic conversion, knot/degree, parameterization, continuity and point-editability guarantees are intended? These are questions for review, not promises implied by the tab name.

Story: [NURBS Revolve #15](https://github.com/chrizpro/prism/issues/15).

## Tests and evidence

- Unit: [RevolveAxisMathTests.cs](../../../Prism.Tests/Unit/Revolve/RevolveAxisMathTests.cs), supporting managed axis/vector math.
- Headless: [SolidRevolveTests.cs](../../../Prism.Tests/Headless/Revolve/SolidRevolveTests.cs), including named Solid and Surface cases.
- [Maintained test guide](../../../Prism.Tests/README.md) explains the runner and metadata.

Use `./tools/test.ps1 -Feature Revolve` to run the maintained feature tests. Individual coverage lives in `Fact`/`AvaloniaFact` names and `Feature`, `Layer` and `TestId` traits; this README and the issues do not duplicate an option-to-test matrix.

Historical local verification on 2026-11-03: 4 Unit and 8 Headless Revolve cases passed using the installed workstation native binaries. [Verified test snapshot](https://github.com/chrizpro/prism/tree/0c4d08c82e6e82e9f913b154f27daa07963e488c/Prism.Tests). Freeform and NURBS workflow-specific coverage remain unverified. No new tests, native builds or viewport executions were performed for this documentation change.

Integration is deferred. When introduced, attach representative actual viewport images to the relevant issue with test ID and source revision. Images complement geometric assertions; they do not approve requirements or prove numerical correctness alone.

## Evolving this specification

Record owner-approved behavior here as requirements change; keep pending decisions visibly pending. Link the corresponding feature issue, enhancement or bug without copying its chronology into the requirements. When a review/delivery story closes, preserve the exact reviewed spec commit or release link in that issue. Future changes receive new issues and update this living document.

See the [shared workflow](../../engineering/requirements-and-testing.md) and [ribbon guide index](../../tabs/README.md).
