# Prism ribbon and workspace guides

These are **product behavior guides** for Prism's tabs, panels and workflows. They explain what a feature currently does, its limitations, and its intended use. They are not automatically approved requirements or evidence that all tests currently pass.

Use [the requirements and testing workflow](../engineering/requirements-and-testing.md) for owner approval, issue links, and verification rules. When adding a new guide, copy [the guide template](_guide-template.md) and adapt it; do not overwrite an established guide to match the template.

## Find a guide

| Area | Guide | Coverage |
| --- | --- | --- |
| Solid modeling | [Solid Extrude (review draft)](solid/extrude.md) | First detailed feature specification; proposed requirements pending owner review |
| Direct modeling | [Direct Modeling](direct-modeling/README.md) | Editing existing exact geometry, gizmos and history |
| Surfaces and styling | [Surface and NURBS](surface-and-nurbs/README.md) | Curves, surfaces, NURBS and continuity |
| Sheet metal, electronics and scripting | [Sheet Metal, ECAD and Script](script/README.md) | Separate workflows grouped in one guide |
| Freeform | [Freeform](freeform/README.md) | Subdivision/cage creation and edits |
| Mesh and scan reconstruction | [Mesh and Reverse Engineer](mesh-and-reverse-engineer/README.md) | Mesh cleanup, region fitting and CAD reconstruction |
| Mechanisms | [Motion](motion/README.md) | Joints, drives, couplings and motion studies |
| Fluid analysis | [Fluids](fluids/README.md) | Pipe and 3D flow workflows |
| Data tools | [Data panel](data/README.md) | Document-embedded tabular data |

This table lists **existing** guides, not a claim that every ribbon tab has a page. Add missing tab guides incrementally when there is a feature change or an owner-approved documentation task.

## Related cross-feature documentation

- [Motion analysis](../motion-analysis.md) — detailed mechanics and study workflows
- [CAE studies](../cae-studies/README.md) — analysis implementation, limitations and historical verification
- [CAM scope](../cam-plan.md) — machining and printing workflow scope
- [Gizmo workflows](../gizmo-workflows/README.md) — shared handles, snapping, screenshots and checks
- [Freeform studies](../freeform-studies/README.md) — example models and historical validation
- [Workflow studies](../workflow-studies/README.md) — CAE/CAM case studies
- [Prism automation / MCP](../MCP.md) — driving the app from agents or scripts

Prefer linking these pages to copying their contents into each tab.

## How to extend a guide

1. **Keep the useful narrative.** Preserve how-to instructions, tool tables, limitations, screenshots and existing verification notes.
2. **Separate facts from proposals.** Existing descriptions are not retroactively owner-approved. Label newly proposed or disputed behavior as **Proposed — awaiting owner review**; record open decisions.
3. **Add focused acceptance criteria** for behavior the owner has reviewed. Use stable requirement IDs only when traceability would be useful; do not create IDs for every button.
4. **Link work, don't duplicate it.** GitHub Issues track actionable gaps and link to the canonical guide or heading. Guides may link back to an issue or an existing test project.
5. **Report verification precisely.** Historical test counts are dated evidence, not a live pass/fail signal. Don't add or regenerate tests until the relevant behavior is approved and testing work is authorized.

### Separate kinds of status

These labels must not be conflated:

| Dimension | Values / meaning |
| --- | --- |
| Requirement review | **Proposed** (not approved), **Approved** (owner accepted behavior), **Needs decision** (ambiguous) |
| Implementation coverage | **Done / Thin / Gap** where the guide already uses these labels; these describe implementation maturity |
| Verification | **Not run**, **Passed**, **Failed**, or **Blocked** with command, environment and date when recorded |

An approved requirement can still be a product gap, and a working feature may still need requirement review.

## Suggested review sequence

Review one existing guide at a time. Keep accepted behavior; flag unclear or changed semantics for owner decision; then file issues for confirmed gaps. Start with a small pilot rather than reformatting all guides. See [Motion's review section](motion/README.md#requirements-review-and-traceability) as an example of a *review queue*, not approved tests.
