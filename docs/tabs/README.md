# Prism ribbon and workspace guides

Use this index to find guides by ribbon tab or workspace. The [documentation index](../README.md) also lists guides by domain.

Use [the requirements and testing workflow](../engineering/requirements-and-testing.md) for owner approval, issue links, and verification rules. When adding a new guide, copy [the guide template](../engineering/_guide-template.md) and adapt it; do not overwrite an established guide to match the template.

## Ribbon section names

Owner approved the workspace access review in [#126](https://github.com/chrizpro/prism/issues/126), 2026-11-11. Assemble now includes Solid, Surface, Freeform, NURBS, Direct Modeling and Sheet Metal. Scan adds NURBS; ECAD includes Solid, Direct Modeling and Sheet Metal; CAE and Render include Solid and Direct Modeling; CAM includes Solid, Direct Modeling and Mesh; Mold includes Surface, NURBS and Freeform alongside Solid/Direct Modeling. Drawing remains focused on its dedicated editor. Mesh tabs still appear contextually for documents containing meshes; user-hidden tabs and custom layouts remain respected.

Tooling was redundant: its three commands now live directly in **Mold → Inspect** (Draft analysis, Section analysis, Check geometry), so the separate tab is removed. Render follows **Materials → Lighting → Camera → Render → Output**. See the [interface checklist](checklists/interface-manual-test-checklist.md) and [maintained usability checks](../../Prism.Tests/Headless/Interface/UsabilityWorkflowTests.cs).

Each ribbon tab shows its section captions below the tools in both themes. **3D FDM** and **3D Resin** use **Prepare**, **Troubleshoot**, and **Export**. **Mold** uses **Part**, **Mold** and **Inspect**. The Drawing workspace uses its own toolbar with named menus. Direct Modeling remains contextual: enter Direct Edit from Solid to open it; it is not a permanently visible tab.

Owner request and desktop evidence [#113](https://github.com/chrizpro/prism/issues/113). On 2026-11-10, RIB-H001 checked names for all shipped tabs and rendered the two print tabs and Mold in both themes. RIB-I001 captured those tabs on the Windows desktop in both themes. See the [ribbon section tests](../../Prism.Tests/Headless/Ribbon/RibbonSectionTests.cs).

## Modeling ribbon palette

Solid-first review (2026-11-10, [#118](https://github.com/chrizpro/prism/issues/118)). Solid uses blue for modeled geometry and pattern instances. Smaller accents distinguish a direction arrow, cut line or modified edge. The shared Gizmo control uses the viewport's red, green and blue axes, a center handle and a rotation arc. Move uses its separate icon. RIB-H002 verified the viewport axis colors in both themes and the saved Gizmo ribbon ID. RIB-I002 captured the Solid preview on Windows in [Light](evidence/RIB-I002-solid-light.png) and [Dark](evidence/RIB-I002-solid-dark.png). [Ribbon tests](../../Prism.Tests/Headless/Ribbon/RibbonSectionTests.cs).

Owner approved a ribbon-wide palette in [#118](https://github.com/chrizpro/prism/issues/118). Every tool icon, including shared buttons and dropdown entries, takes its primary color from the active modeling ribbon: blue in Solid, green in Surface, red in NURBS and purple in Freeform. Neutral outlines and secondary marks distinguish the action, and the Gizmo retains the viewport's axis colors. The palette applies to the ribbon display; saved tool IDs and geometry behavior are independent of the palette. RIB-H001 checks the palette on all button icons in both themes. Eight targeted ribbon tests passed on Windows. RIB-I004 captured all four tabs in both themes, including Freeform in [Light](evidence/RIB-I004-freeform-light.png) and [Dark](evidence/RIB-I004-freeform-dark.png).

## Find a guide

| Area | Guide | Coverage |
| --- | --- | --- |
| [Solid modeling](../modeling/solid/README.md) | [Extrude](../modeling/extrude/README.md#solid-extrude) · [Revolve](../modeling/revolve/README.md#solid-revolve) | Living feature behavior and open decisions; proposed requirements pending owner review |
| Direct modeling | [Direct Modeling](../modeling/direct-modeling/README.md) | Editing existing exact geometry, gizmos and history |
| Surfaces and styling | [Surface and NURBS](../modeling/surface-and-nurbs/README.md) · [Extrude living specification](../modeling/extrude/README.md#surface-and-nurbs-extrude) · [Revolve](../modeling/revolve/README.md) · [Sweep](../modeling/sweep/README.md) · [Loft](../modeling/loft/README.md) | Surface/NURBS workflows, shared implementations and distinct review scopes |
| Sheet Metal | [Sheet Metal](../modeling/sheet-metal/README.md) | Flanges, flat patterns and cut-outs |
| ECAD | [ECAD](../ecad/README.md) | Boards, components and mechanical checks |
| Script | [Script](../interface/script/README.md) | Script editor and external editors |
| Drawing | [Drawing workspace](../drawing/README.md) | Editable sheets, views, dimensions, annotations and export; approved 2026-11-05 |
| Freeform | [Freeform](../modeling/freeform/README.md) · [Extrude Profile review](../modeling/extrude/README.md#freeform-extrusion) · [Revolve](../modeling/revolve/README.md#freeform-revolve) · [Sweep](../modeling/sweep/README.md#freeform-sweep) · [Loft](../modeling/loft/README.md#freeform-loft) | Subdivision/cage creation and edits |
| Mesh and scan reconstruction | [Mesh and Reverse Engineer](../modeling/mesh-and-reverse-engineer/README.md) | Mesh cleanup, region fitting and CAD reconstruction |
| Mechanisms | [Motion](../cae/motion/README.md) | Joints, drives, couplings and motion studies |
| Fluid analysis | [Fluids](../cae/fluids/README.md) | Pipe and 3D flow workflows |
| Static structural analysis | [Static analysis living specification](../cae/static-analysis/README.md) | Linear-static setup, native solver, independent benchmarks and limits |
| Data tools | [Data panel](../interface/data/README.md) | Document-embedded tabular data |

This table lists **existing** guides, not a claim that every ribbon tab has a page. Add missing tab guides incrementally when there is a feature change or an owner-approved documentation task.

## Manual test checklists

Each file covers a short hands-on pass for one shipped tab. Drawing has its own toolbar and checklist. Record what you actually tested and link any failures to the feature guide. [Tracking issue #114](https://github.com/chrizpro/prism/issues/114).

- [File](checklists/file-manual-test-checklist.md)
- [Home](checklists/home-manual-test-checklist.md)
- [Solid](checklists/solid-manual-test-checklist.md)
- [Surface](checklists/surface-manual-test-checklist.md)
- [Freeform](checklists/freeform-manual-test-checklist.md)
- [NURBS](checklists/nurbs-manual-test-checklist.md)
- [Direct Modeling](checklists/direct-modeling-manual-test-checklist.md)
- [Sheet Metal](checklists/sheet-metal-manual-test-checklist.md)
- [ECAD](checklists/ecad-manual-test-checklist.md)
- [Script](checklists/script-manual-test-checklist.md)
- [Subtractive](checklists/subtractive-manual-test-checklist.md)
- [3D FDM](checklists/3d-fdm-manual-test-checklist.md)
- [3D Resin](../cam/resin-manual-test-checklist.md)
- [Analyze](checklists/analyze-manual-test-checklist.md)
- [Mesh](checklists/mesh-manual-test-checklist.md)
- [Reverse Engineer](checklists/reverse-engineer-manual-test-checklist.md)
- [Static](checklists/static-manual-test-checklist.md)
- [Thermal](checklists/thermal-manual-test-checklist.md)
- [Dynamics](checklists/dynamics-manual-test-checklist.md)
- [Motion](checklists/motion-manual-test-checklist.md)
- [Fluids](checklists/fluids-manual-test-checklist.md)
- [Assemble](checklists/assemble-manual-test-checklist.md)
- [Joints](checklists/joints-manual-test-checklist.md)
- [Animate](checklists/animate-manual-test-checklist.md)
- [Render](checklists/render-manual-test-checklist.md)
- [Mold inspection (formerly Tooling)](checklists/tooling-manual-test-checklist.md)
- [Mold](checklists/mold-manual-test-checklist.md)
- [Materials](checklists/materials-manual-test-checklist.md)
- [Sketch](checklists/sketch-manual-test-checklist.md)
- [View](checklists/view-manual-test-checklist.md)
- [Drawing](checklists/drawing-manual-test-checklist.md)

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
