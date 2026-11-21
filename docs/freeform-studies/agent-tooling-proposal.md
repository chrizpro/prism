# Freeform for agents: diagnosis and proposed tooling

**Status: Approved by the owner on 2026-11-17; P1 to P7 implemented (see Results).** Owner decisions on the open questions: (1) multi-view capture extends `screenshot`; (2) the SVG and station helpers live in the cadkit script layer only, not as app tools; (3) the skill ships in the repository for other agents, and the in-app AI assistant also sees it. Findings marked *Observed* come from running the live MCP/Python API on 2026-11-17; the rest is analysis.

## Diagnosis

An agent modeling a Freeform object is working blind and at the wrong level of abstraction.

### 1. It cannot see (observed)

- `screenshot` returns the whole window, or the whole viewport with `viewport:true`. It shows one camera, every object, the cage wireframe overlay and the grid. An agent needs the silhouette from front, side and top at once, with only the object under test.
- Judging a shape therefore costs a `view` call, `wait_idle`, `screenshot`, repeated per angle. The `view` presets also change the user's camera.
- There is no numeric stand-in for sight. `form_info` returns bounds and counts. There is no cross-section, silhouette or "width at height z" query, so the agent cannot check "the neck is narrower than the shoulders" without reading every vertex.

### 2. Edits report topology health, not what changed (observed)

`form_op` extrude → `{selectFaces, vertexCount, faceCount, topology{...}}`. `set_vertices` → counts and topology only. The agent does not learn:

- the indices of the vertices and faces the op created (it must diff `form_info`, which dumps the entire cage);
- how the bounding box changed, or how far anything moved;
- whether the result got worse (self-intersection, inverted or stretched faces, fold-overs).

`topology.cageValid: true` says the cage is a valid manifold. It says nothing about whether the shape looks right.

### 3. The only construction vocabulary is vertex indices (observed)

The best existing example (`Prism/Scripting/examples/05_freeform_steam_iron.py`) computes a position for every cage vertex of a box. This works for a person who wrote the formula, and fails for an agent describing "a bottle" or "a computer mouse". Blender and Fusion agents succeed because they describe **stations, profiles and silhouettes**, not vertex lists: skin or loft through sections, mirror modifier, curve to mesh, trace a reference outline.

Prism already has `form_loft`, `form_pipe`, `form_sweep`, `form_extrude`, `form_revolve`, and `set_symmetry`. The gap is a way to author the *inputs* (section curves, outlines) compactly and to *check* the outputs.

### 4. No path from a drawing to curves

There is no SVG or outline import. An agent that can write or fetch an SVG path, or that is given a reference image, has no way to turn it into curves. `place_image` exists, but nothing reads an outline off it.

### 5. No agent guidance

The bridge `instructions` string and the `build_freeform_model` prompt describe tools, not a workflow. The prompt tells the agent to "use form_op" but not to start from a loft, mirror half, check silhouettes, and stop editing when the checks pass.

## Proposals

Each item gets acceptance criteria once the direction is approved. Order is by expected value.

### P1. Multi-view capture — **Proposed**

`screenshot` (or a sibling `viewport_capture`) takes `views:["front","right","top","iso"]`, `object` (isolate: show only these objects), `cage:true|false`, `max_width`. It returns one labeled contact-sheet PNG and **restores the camera, visibility and display** afterwards.
*Why:* one call replaces ~12 and does not disturb the user's view.

### P2. Numeric shape probes — **Proposed**

A tool (`solid_profile`, working for Forms, solids, surfaces and meshes) returning:
- `sections`: for N planes along an axis (or at given offsets), the extents, area and centroid of the cross-section;
- `silhouette`: a small ASCII raster per view (front/right/top), about 60 columns wide, with the scale stated.
*Why:* the agent can verify proportions in text, cheaply, and detect pinched or bulging regions.

### P3. Edit replies that describe the change — **Proposed**

`form_op` returns `created:{vertices:[…],faces:[…]}` (index lists or ranges), `bounds` before and after, `moved:{count,maxDistance}`, and `warnings` for degenerate, inverted or badly stretched faces and for self-intersection of the smooth surface. Same for `form_to_nurbs`.
*Why:* removes the diff-the-cage round trip and flags regressions at the step that caused them.

### P4. SVG and outline to curves — **Proposed**

`cad.svg_curves(path_or_text, plane, scale, origin, flip_y, tolerance)` in cadkit, pure Python: parse `path` (M L H V C S Q T A Z), `polygon`, `polyline`, `rect`, `circle`, `ellipse`, `line`; flatten curves to a tolerance; split at corners; emit sketch splines/polylines (closed where the path closes); report bounds and counts. Companion helpers for common section shapes (`cad.section_superellipse(width, height, n)`, `cad.section_from_points`).
*Why:* an agent can draw a side silhouette as SVG, trace a reference, or generate section outlines, and then loft or extrude them.

### P5. Station-based builder — **Proposed**

`cad.form_skin(stations, axis="Z", symmetric=True)` where each station is `{at, width, depth, exponent, offset}` (superellipse section) or a closed point list. It sketches the sections on planes, lofts them into a Freeform and optionally sets mirror symmetry. The agent states proportions and tunes numbers, not vertices.
*Why:* this is the declarative description that makes bottles, handles, fuselages, shoes and mice reachable in a few lines.

### P6. Agent skill and prompt — **Proposed**

A `prism-freeform` skill (Markdown, shipped in the repo and referenced from the MCP `instructions` and `build_freeform_model` prompt) with the workflow: define proportions and reference silhouettes first; build a coarse form from stations; mirror half; verify with P1/P2 after every few edits; tighten with insert_loop and local soft moves; crease deliberately; convert to NURBS only at the end. It lists the common failure modes and the checks that catch them.

### P7. Viewport API as a test instrument — **Proposed**

Once P1 and P2 exist, the same calls become assertion helpers for `tests/visual`: a scenario builds a Freeform tool result with a known silhouette, and the check compares ASCII silhouettes or section extents with the known answer instead of eyeballing a PNG. `pointer` and `pick_at` already drive tool handles; the new probes supply the verification half.

## Decided questions (owner, 2026-11-17)

1. P1 extends `screenshot` (`views`, `object`, `display`, `cell_width`, `columns`).
2. P4/P5 are cadkit only. Without `run_script` (the restricted assistant) the skill's "Without scripting" section uses `sketch_*`, `run_command form_loft`, `solid_profile` and `screenshot` views.
3. The skill is `.claude/skills/prism-freeform/SKILL.md` in the repository; the build copies it to `Scripting/skills`, the new `guide` tool serves it to any MCP client, and the in-app assistant's system prompt tells it to call `guide` before curved shapes.

## Verification plan (after approval)

Headless tests under `Prism.Tests/` for P2/P3 (known silhouettes and sections of a box, a revolved Form, a loft); Python unit tests under `tests/` for the SVG parser and station builder (no app needed); a visual script that builds a bottle from stations and checks sections against the specified widths. Which of these ran will be reported exactly.

## Results (2026-11-17)

### What was built

| Item | Where |
| --- | --- |
| P1 multi-view capture | `screenshot` `views`, `object`, `display`, `cell_width`, `columns` ([AutomationApi.cs](../../Prism/Automation/AutomationApi.cs), [ContactSheet.cs](../../Prism/Automation/ContactSheet.cs)) |
| P2 shape probes | `solid_profile` tool, [ShapeProbe.cs](../../Prism/Automation/ShapeProbe.cs); `cad.profile` |
| P3 edit replies | `form_op`: `bounds`, `sizeChange`, `created`, `moved`, `flippedFaces`, `warnings` |
| P4 SVG to curves | `cad.svg_outlines`, `cad.svg_to_sketch`, `cad.parse_path`, `superellipse`, `rounded_rect` ([shapes.py](../../Prism/Scripting/python/cadkit/shapes.py)) |
| P5 station builder | `cad.form_skin`, `cad.form_from_silhouettes` (measures its own result and rescales stations), `cad.stations_from_silhouettes`, `cad.swap_uv` |
| P6 skill and prompts | [`prism-freeform` skill](../../.claude/skills/prism-freeform/SKILL.md), `guide` tool, assistant system prompt, MCP `instructions` and `build_freeform_model` prompt |
| P7 test instrument | `solid_profile` sections and silhouettes can be asserted in visual scripts; the iron example asserts length, width, height and that the handle is a loop |

### Bugs found while using the tools, and fixed

1. A `stackalloc` inside the per-triangle silhouette loop is not released until the method returns, so a large Freeform overflowed the stack and killed the whole app (no crash log). Hoisted; API-H010 covers a 320,000-triangle mesh.
2. Cross-section outline counting grouped triangles by vertex index, but display meshes repeat vertices per face, so a box reported 64 outlines. It now welds by position (API-H006, API-H007).
3. A camera animation still running from the last zoom-to-fit overwrote the first view of a multi-view sheet, so FRONT showed a 3/4 view. Capture now waits for the animation to settle, then for two identical frames in a row.
4. A silhouette of a sheet seen edge-on had zero extent and overflowed an `int` cast (checked arithmetic). Every extent now has a 2% floor.
5. `form_skin` first hid its section sketches, leaving 64 objects after a rebuild loop; it now deletes them.

### The iron

Before: [`03-glide-steam-iron.png`](03-glide-steam-iron.png), built in an earlier session by computing a position for every vertex of a box cage. Two blunt boxes, round tube handle, no taper, no sloped shell.
After: [`07-iron-from-silhouettes.png`](07-iron-from-silhouettes.png) and `07-iron-from-silhouettes.prism`, built by [`07_freeform_iron_from_silhouettes.py`](../../Prism/Scripting/examples/07_freeform_iron_from_silhouettes.py): about 90 lines of outlines and numbers, no hand-placed vertices.

| Measured with `cad.profile` | Brief | Result |
| --- | --- | --- |
| Soleplate length x width x thickness | 280 x 112 x 11 | 274.3 x 111.8 x 10.6 (the 1% end margin and cap rounding account for the length) |
| Shell length x width x height | 280 x ~106 x 70 | 274.3 x 103.3 x 70.0 |
| Handle is a loop | a cut through both legs shows 2 outlines | 2 |
| Worst section error after one correction | under 2 mm | soleplate 1.46 mm (at the thin tip), shell 0.63 mm |

The first build of each part came out about 5% small, as predicted; the one automatic correction fixed it. The iron was built by Claude while the tools were being written and debugged, not by a fresh agent following only the skill; that cold-start test has not been run. A person would still call it a concept study: no steam vents, water-tank window, cord guard or textured grip.

### Not verified

- The in-app assistant reading the guide was not run end to end (the prompt and tool are covered by API-H011 and API-H012 only).
- The restricted (no `run_script`) path was not exercised; its steps are documented in the skill.
- Multi-view `screenshot` is verified by hand on Windows only (it uses window capture); it has no automated test. The view-cube, toolbar, toast and origin axis lines still appear in the pictures.
- The older `tests/Prism.FreeformTests` and `tests/Prism.FreeformWorkflowTests` suites were not rerun; the Headless Freeform slice (38 tests) and Automation slice (12 tests) pass, as do 35 Python tests and the 41-label cadkit slot check.
- `site/data/cadkit.json` (generated by `site/scripts/sync-api.py`) was not regenerated.
