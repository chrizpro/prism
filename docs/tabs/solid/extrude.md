# Solid Extrude — specification review

> **Requirements status:** Proposed — awaiting owner review (2026-11-03).
>
> **Implementation status:** Source inspected; desktop behavior not validated.
>
> This is a draft living specification, **not** an approved contract or an assertion that every option works. Do not generate or change tests from these proposals until the owner approves their intended behavior.

## Purpose and scope

The Solid Extrude command extends selected sketch profiles, curves or faces along a direction to create or modify exact solid geometry. It is distinct from **Surface Extrude**, which creates a surface result, and **Freeform Extrude**, which operates on subdivision/cage topology. Their requirements should be reviewed separately even if the tools share a label.

User journey (from existing documentation): **Home / Solid → Create → Features → Extrude**. Select a profile, choose its extent, preview the result and specify the result operation.

## Observed in existing code (not yet approved requirements)

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

### Related feature variants

- [Surface Extrude draft](../surface-and-nurbs/extrude.md) ([Story #7](https://github.com/chrizpro/prism/issues/7)): exposes Extrude and uses the same command class with surface output; should have its own approved scope.
- [Freeform Extrude Profile draft](../freeform/extrude.md) ([Story #8](https://github.com/chrizpro/prism/issues/8)): cage-based extrusion uses a different command; no Solid behavior should be assumed for it.
- [Sheet Metal / ECAD / Script](../script/README.md): Sheet Metal includes an Extrude cut-out entry; confirm whether it uses Solid semantics before sharing any requirements.

## Proposed acceptance criteria — **not approved**

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

## Decisions needing owner review

1. Should Solid Extrude strictly require closed planar profiles, and how should selected edge chains behave?
2. For **Through all**, should the result reference only selected target bodies or all intersected visible solids? How should it behave with no eligible body?
3. Should the one-way Cut auto-reversal remain, or should direction stay entirely user-controlled?
4. For multiple closed regions, should default behavior extrude them all or require an explicit region choice?
5. What does **To object** mean for nonplanar targets, and what are the expected offset and tolerance rules?
6. What error experience should users see for zero distance, invalid profile topology and unsupported intersections?
7. Which operation choices and target-selection rules should be contractually supported?

Review these before requesting desktop agents to implement or add tests.

## Verification and traceability

- **Existing implementation:** [`ExtrudeCommand`](../../../Prism/Commands/SolidCommands.cs) and shared `BodyCommand` operation logic. Exact source-to-test coverage still needs an inventory on the local desktop.
- **Existing user documentation:** [modeling guide](../../../site/guide/modeling.md) documents profile selection and the distinction between open surface chains and closed solid profiles.
- **Existing tests:** Not verified for this specific feature. Do not infer that a particular test project covers the requirements above.
- **Tests run for this draft:** None. Native OCCT integration and Avalonia workflow checks require the configured workstation.
- **Feature story:** [Story #6 — Solid Extrude requirements review](https://github.com/chrizpro/prism/issues/6). This tracks owner review; it does not certify implementation or testing.
- **Delivery history:** No approved delivery version recorded by this draft. Future closed stories should link the exact reviewed specification commit or release tag as well as this living page.

## Known documentation limits

The source inspection is not a user acceptance test. This draft may miss existing UI behavior and failure modes. Treat discrepancies as review questions and confirm with a desktop walkthrough before approving requirements.

## Related guidance

- [Tab guide index](../README.md)
- [Requirements, GitHub and testing workflow](../../engineering/requirements-and-testing.md)
