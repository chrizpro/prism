# Prism ribbon and workspace guides

Use this index to find guides by ribbon tab or workspace. The [documentation index](../README.md) also lists guides by domain.

Use [the requirements and testing workflow](../engineering/requirements-and-testing.md) for owner approval, issue links, and verification rules. When adding a new guide, copy [the guide template](../engineering/_guide-template.md) and adapt it; do not overwrite an established guide to match the template.

## Find a guide

| Area | Guide | Coverage |
| --- | --- | --- |
| [Solid modeling](../modeling/solid/README.md) | [Extrude](../modeling/extrude/README.md#solid-extrude) · [Revolve](../modeling/revolve/README.md#solid-revolve) | Living feature behavior and open decisions; proposed requirements pending owner review |
| Direct modeling | [Direct Modeling](../modeling/direct-modeling/README.md) | Editing existing exact geometry, gizmos and history |
| Surfaces and styling | [Surface and NURBS](../modeling/surface-and-nurbs/README.md) · [Extrude living specification](../modeling/extrude/README.md#surface-and-nurbs-extrude) · [Revolve](../modeling/revolve/README.md) · [Sweep](../modeling/sweep/README.md) · [Loft](../modeling/loft/README.md) | Surface/NURBS workflows, shared implementations and distinct review scopes |
| Sheet Metal | [Sheet Metal](../modeling/sheet-metal/README.md) | Flanges, flat patterns and cut-outs |
| ECAD | [ECAD](../ecad/README.md) | Boards, components and mechanical checks |
| Script | [Script](../interface/script/README.md) | Script editor and external editors |
| Freeform | [Freeform](../modeling/freeform/README.md) · [Extrude Profile review](../modeling/extrude/README.md#freeform-extrusion) · [Revolve](../modeling/revolve/README.md#freeform-revolve) · [Sweep](../modeling/sweep/README.md#freeform-sweep) · [Loft](../modeling/loft/README.md#freeform-loft) | Subdivision/cage creation and edits |
| Mesh and scan reconstruction | [Mesh and Reverse Engineer](../modeling/mesh-and-reverse-engineer/README.md) | Mesh cleanup, region fitting and CAD reconstruction |
| Mechanisms | [Motion](../cae/motion/README.md) | Joints, drives, couplings and motion studies |
| Fluid analysis | [Fluids](../cae/fluids/README.md) | Pipe and 3D flow workflows |
| Static structural analysis | [Static analysis living specification](../cae/static-analysis/README.md) | Linear-static setup, native solver, independent benchmarks and limits |
| Data tools | [Data panel](../interface/data/README.md) | Document-embedded tabular data |

This table lists **existing** guides, not a claim that every ribbon tab has a page. Add missing tab guides incrementally when there is a feature change or an owner-approved documentation task.

## Related cross-feature documentation

- [Motion analysis](../motion-analysis.md) — detailed mechanics and study workflows
- [CAE studies](../cae-studies/README.md) — analysis implementation, limitations and historical verification
- [CAM scope](../cam/README.md) — machining and printing workflow scope
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

Review one existing guide at a time. Keep accepted behavior; flag unclear or changed semantics for owner decision; then file issues for confirmed gaps. Start with a small pilot rather than reformatting all guides. See [Motion's review section](../cae/motion/README.md#requirements-review-and-traceability) as an example of a *review queue*, not approved tests.
