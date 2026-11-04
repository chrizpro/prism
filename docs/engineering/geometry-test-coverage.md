# Geometry command verification — Extrude, Revolve, Loft and Sweep

> **Status:** Test-design inventory, not owner-approved feature acceptance criteria. **No native execution or screenshots were performed** while preparing this page (2026-11-03).
>
> **Goal:** Thoroughly exercise every *actual* option exposed by each command, verify geometry independently of the preview, and preserve visual evidence from the real viewport. This page is a coverage plan, not a claim of passing tests.

## Test architecture

1. **Headless kernel/command layer:** deterministic fixtures (sketches, wires, faces, bodies, guide rails); drive commands through the same parameter/selection logic as the application. Assert shape validity, bounding box, volume/area when meaningful, shell/solid count, topology, orientation, target-body changes, and failure without document corruption.
2. **Desktop viewport layer:** run Prism on the workstation with OCCT and GPU context; drive command UI, preview, accept, undo/redo and save/reopen. Capture screenshots **before and after commit** with camera pose, object selection, command parameters and test ID recorded.
3. **Visual regression:** store reviewed baseline screenshots separately from current run artifacts. Compare images with tolerances for anti-aliasing and GPU differences; always review differences visually. Screenshots are **evidence**, not a substitute for geometry assertions.
4. **Traceability:** each test case links a feature guide and a real test entry point; do not create a separate GitHub issue per option. Create bugs for independently actionable failures and link the corresponding feature story.
5. **Coverage:** generate an option inventory from actual command constructors and UI bindings; for each supported option record positive, negative, boundary, combined-option and edit/rebuild coverage. Mark not-applicable explicitly. Do not equate all combinatorial permutations with necessary tests; use pairwise coverage for interactions plus risk-based cases.

## Initial feature inventory

| Feature | Existing documentation | Coverage families to inventory |
| --- | --- | --- |
| Solid Extrude | [Solid Extrude](../modeling/extrude/README.md#solid-extrude), [Story #6](https://github.com/chrizpro/prism/issues/6) | Profiles, signed distance/reverse, two sides, symmetric, to-object, through-all both ways, taper, regions, body operations, invalid input |
| Surface Extrude | [Surface tab](../modeling/surface-and-nurbs/README.md) | Open/closed edges, direction and extents, multiple selections, resulting sheet topology |
| Freeform Extrude Profile | [Freeform tab](../modeling/freeform/README.md) | Profile closure, distance, X/Y/Z/profile-normal, end scale, steps, profile point count, cap, smoothness, degenerate normals |
| Freeform Modify Extrude | [Freeform tab](../modeling/freeform/README.md) | Selected face/edge/vertex semantics, distance, topology invariants and history; requires separate source/UI inventory |
| Revolve | Existing tab guides and command source (to inspect) | Angle, axis, profile closure, partial/full revolutions, direction, resulting topology, invalid axis/profile |
| Loft | Existing tab guides and command source (to inspect) | Profile count/order, closed/open profiles, guides, continuity, mismatched sections, invalid lofts |
| Sweep | Existing tab guides and command source (to inspect) | Profile/path selection, path curvature, orientation, rails, twist, self-intersections, open/closed outputs |

The last three rows are **coverage prompts**, not assertions that Prism exposes every named control. The implementation must be inspected before enumerating exact cases.

## Minimum evidence per scenario

| Evidence | Headless | Desktop |
| --- | --- | --- |
| Input fixture + parameter values | Required | Required |
| Geometry validity / quantitative assertions | Required | When available |
| Error / exception and document preservation | Required for negative cases | Required for UI error paths |
| Preview image | Optional offscreen render | Required for visual scenarios |
| Final viewport image | Optional offscreen render | Required for visual scenarios |
| Undo/redo and saved/reopened state | Where testable | Required for representative scenarios |
| Actual result, environment, date and source revision | Required | Required |

## Screenshot conventions

- File names: `<feature>/<case-id>-input.png`, `-preview.png`, `-result.png`, `-after-reopen.png` as applicable.
- Capture a fixed camera pose and viewport size, with grid/selection overlays controlled; include a second isometric angle for ambiguous topology.
- Use one high-contrast appearance theme for baseline stability, plus targeted dark/light theme tests.
- Keep raw images as run artifacts; curate a small set of reviewed examples for docs. Do not commit thousands of generated PNGs to the source tree by default.
- A headless offscreen render can show geometry, but it **does not verify Prism's actual interactive viewport**.

## Desktop agent handoff

1. Verify checkout and installed SDK/OCCT dependencies; discover existing test projects and their frameworks (do not assume xUnit).
2. Inspect command parameter constructors, feature operations and actual ribbon UI for each of the four tool families. Update the inventory with exact options, paths and fixture cases.
3. Before treating proposed behavior as a contract, get owner approval of the open requirements. Meanwhile, tests may characterize **existing observed behavior** if explicitly labeled characterization tests, without silently making it the approved contract.
4. Implement a small representative Extrude headless test plus real viewport capture first; prove both paths work before scaling.
5. Expand to every applicable option, boundaries and selected interactions; then repeat for Revolve, Loft and Sweep.
6. Produce an HTML or Markdown report with clickable screenshot thumbnails, parameters, assertion results, failures and links to related GitHub issues. Never claim a screenshot was captured unless the artifact exists.

## Open engineering decisions

- What desktop test driver should control the real Prism viewport (existing harness, UI automation or a purpose-built in-process capture hook)?
- How should geometry fixtures and tolerance policies be shared across kernel tests?
- Where should visual baseline artifacts live (CI artifact store, Git LFS, or curated docs only)?
- Which proposed feature behaviors have owner approval? See each feature specification before calling a failing characterization test a product bug.
