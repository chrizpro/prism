# NURBS, Freeform and viewport-interaction plan

**Status: Proposed — awaiting owner review (2026-11-12).** Nothing in this page is approved behavior. It answers the owner's request to plan the NURBS and Freeform workflows and the "one standard way to interact in the viewport" question before code is touched. Each proposal names what it changes, what it risks for other workflows, and a suggested order. Decisions go in the **Decisions** section at the end; approved items move into the canonical guides ([Surface and NURBS](../modeling/surface-and-nurbs/README.md), [Freeform](../modeling/freeform/README.md), [viewport interaction](../viewport-interaction-handoff.md)).

## 1. What already holds (fixed 2026-11-12, see the guides)

* Handles whose value is 0 keep a grabbable stub along their axis and drags are relative, so Move X/Y/Z arrows all respond ([viewport interaction](../viewport-interaction-handoff.md#handle-grip-rules-2026-11-12)).
* The Move gizmo moves assembly parts by placement and refuses held parts with a reason ([assembly](../modeling/assembly/README.md)).
* Every joint type can be created **Keep where it is**, so a part no longer jumps inside the other one.
* Running work lives in the one status bar as a JetBrains-style chip ([operation progress](../interface/operation-progress.md)).
* New Study brings its panel forward; clicking a material applies it.

## 2. Viewport interaction: the standard

**Proposal V1 — two manipulators, one look.** Prism keeps exactly two kinds of on-canvas controls, and every tool uses one of them:

| Kind | What it is for | Where it comes from | Examples |
|---|---|---|---|
| **The gizmo** (`GizmoVisual`: three arrows, three plane blocks, three rings, centre grab) | Moving, turning and scaling *things that have a position*: bodies, parts, datums, cage points, NURBS control points, sketch geometry, lights, images | `DatumGizmoTool`, `FormEditTool`, `EditPointsTool`, sketch Transform | Move tool, Edit Freeform, Edit Points, Transform (sketch) |
| **Feature handles** (`HandleSpec`: one arrow per number, a ring for an angle) | *Sizes* of a feature: a distance, radius, thickness, angle, spacing | `Command.Handles` | Extrude distance and taper, Fillet radius, Shell thickness, pattern spacing, joint drive |

The owner asked whether Extrude should be driven by "the gizmo we know and love" instead of its own arrow. Recommendation: **no full gizmo on Extrude**, for three reasons. (1) Extrude has one spatial number per side plus a taper angle; a six-handle gizmo would offer four handles that mean nothing. (2) Fusion, Onshape, Plasticity and Rhino all drive extrude with a single arrow at the profile, so the arrow *is* the standard users expect. (3) The gizmo moves the *whole thing*, which for a feature is a different operation (Move Face / Move Body) and would be confusing to find on the extrude.

What should be unified instead is the **look and feel**, so the arrow on Extrude and the arrow on the gizmo are obviously the same control:

* V1a. Draw feature handles with the gizmo's arrow geometry (shaft + cone from `GizmoVisual.Arrow`), stroke, hit tolerance and snapping settings, instead of the separate cone code in `CadSession.BuildOneHandle`. Same accent colour on hover, same engagement-gated label.
* V1b. A handle that edits a *direction-less* number (radius, thickness) keeps its arrow; a handle that edits a *position* should become a gizmo axis instead (candidates: primitive Position X/Y/Z arrows on Box/Cylinder, Hole placement, Insert Component place arrows). These are today three loose arrows at the body centre, which is a gizmo drawn by hand. Replacing them with the real gizmo (move mode, bound to the preview) removes a second look for the same gesture.
* V1c. Rings for angles (Revolve, Draft, taper, joint drive) use the gizmo's ring geometry and rotation snapping.
* V1d. One rule for labels, already in force for model space: no label at rest, the engaged handle shows its value, typing shows the echo. Sketch labels stay as they are.
* V1e. Chamfer with an angle: the owner wants to "move handles to get the angle right". Today Chamfer is symmetric (one distance; `ck_chamfer` takes one value). Proposal: add **Distance 2** (0 = same) and **Angle** as alternatives, with a second arrow along the other face, which needs a native `ck_chamfer2(d1, d2, reference face)` built on `BRepFilletAPI_MakeChamfer::Add(d1, d2, edge, face)` and a native rebuild. Medium effort; affects only Chamfer.

Risk to other workflows: V1a and V1c are visual, no behavior change; V1b changes the primitives' position handles (covered by HDL-H001–H003, which would be updated); V1e is additive.

## 3. NURBS: make it the fast, low-friction workflow

The NURBS tab is the styling workflow in the spirit of Creo Style, Plasticity and Rhino ([memory and owner decision 2026-11-02](../modeling/surface-and-nurbs/README.md)). Today it is a *list of the right tools*; what makes those products feel fast is that the common loop (draw a curve → surface it → tweak points → check continuity) needs no panel and no mode changes. The proposals below aim at that loop and deliberately do not add per-case tools (owner rule: one generic tool over many specific ones).

**Proposal N1 — Points are always on.** On the NURBS tab, selecting a NURBS curve or single-patch surface shows its control polygon and points immediately (Rhino's "points on", Plasticity's selection of control points). Clicking a point selects it, the gizmo appears on it, dragging moves it with the current soft-influence radius; Shift adds points; Esc clears. **Edit Points** stays as the command for exact values and influence settings, but it is no longer the gate to touch a point. *Changes:* a `NurbsPointsTool` free tool (like `DatumPickTool`) active on the NURBS tab in model view; selection of `SelKind.ControlPoint` items. *Risk:* none to Solid/Surface tabs (tab-scoped); undo is one step per drag like the Move gizmo.

**Proposal N2 — One "Surface" tool that reads the selection.** Instead of choosing between Loft, Boundary, Sweep, Edge Surface, Network: select curves and press **Surface** (hotkey S on the NURBS tab). The command proposes the method from the picks and shows it as a choice the user can override: one closed flat loop → planar; 2–4 open curves meeting at ends → boundary; 2+ open curves not meeting → loft; a profile plus a curve it sits across → sweep; a grid of crossing curves → network. The existing commands keep working from scripts and the ribbon; this is a front door, not a replacement. *Risk:* wrong guesses — mitigated by always showing the chosen method and the alternatives in the panel.

**Proposal N3 — Curve handles in the K sketch.** In the shared "NURBS Curves" sketch, a selected spline shows its control points *and* end-tangent handles (length and direction), draggable in the viewport, as Creo Style and Plasticity do; dragging an end tangent onto another curve's end snaps to G1. This completes the "draw flat, then shape in space" idea in [unified sketches](../unified-sketch.md). *Risk:* sketch constraint solver interplay (tangent handles become tangent constraints only when snapped).

**Proposal N4 — Continuity on the canvas.** Match Surface and Check Seam pick an edge by hovering (highlight the shared edge, show the current continuity G0/G1/G2 and the largest gap as a badge at the edge) and apply with one click; the Zebra toggle lives on the same spot. *Risk:* none to other tabs.

**Proposal N5 — Add Detail and Raise Degree by hover.** With points on (N1), hovering an isocurve offers "add row here" (Rhino InsertKnot by click), so detail goes where the user needs it rather than uniformly. *Risk:* needs the kernel's knot-insertion at a parameter, which `FreeformActions.AddDetail` already wraps uniformly.

**Proposal N6 — Keyboard loop.** K draw, S surface, P points on/off, M match, Z zebra, Tab toggles the panel. All reachable without the ribbon; documented in [sketch hotkeys](../sketch-hotkeys.md).

Order: N1 (largest friction drop, self-contained) → N2 → N3 → N4 → N6 → N5. N1 and N2 together make the tab "special" in the owner's sense: draw, press S, drag points.

## 4. Freeform: close the open items from the usability review

The [Freeform usability review](../freeform-usability-review.md) found the cage tools correct but "select, press a button, then tune numbers". Insert Edge is already interactive; these remain open and are proposed as the next batch:

**Proposal F1 — Interactive extrude and inset.** Extrude and Inset start at zero and show the gizmo-style arrows immediately on the selected faces; the drag sets the distance (Alt free, Ctrl fine, snapping as the gizmo). The panel numbers remain the exact entry. Now that zero-value arrows have a stub, this is mostly wiring. *Risk:* none outside Freeform.

**Proposal F2 — Status lines for silent tools.** Insert Edge, Subdivide, Weld, Delete, Collapse, Split face, Smooth, Flatten, Flip normals, Mirror and weld and Extrude report what they did ("Inserted 1 loop, 12 faces added").

**Proposal F3 — Naming.** "Bevel selected edges" (adds support loops) becomes **Tighten edges**; Chamfer is presented as the bevel. One rename on the ribbon and in the help card.

**Proposal F4 — Points-on parity with NURBS.** A selected Form shows its cage; clicking a cage point, edge or face selects it and the gizmo appears, without pressing Edit Freeform first (Tab still toggles the full editor with its panel). Same mechanism as N1, so the two tabs feel alike.

**Proposal F5 — Tests.** Workflow coverage for interactive extrude, inset, chamfer and slide, which the current suites do not drive.

Order: F1 → F2 → F3 → F4 → F5.

## 5. Decisions needed from the owner

1. V1: keep Extrude on an arrow, unify the look (recommended), or put the full gizmo on it?
2. V1b: replace the primitives' loose position arrows with the real gizmo?
3. V1e: add asymmetric/angle chamfer (needs a native rebuild)?
4. N1–N6: approve the NURBS batch and its order? Any of the hotkeys to change?
5. F1–F5: approve the Freeform batch and its order?
6. Joint **Keep where it is** on every joint type and **click-to-apply materials** were implemented today as fixes for reported confusion; confirm or ask for changes.

## Decisions

* **2026-11-12, owner:** approved V1. Extrude keeps its arrow. The pattern is: **the gizmo for transforming anything that has a position** (part, object, face, vertex, edge, control point…), and **one consistent specialized handle style for feature sizes** (revolve, extrude, chamfer, loft, sweep…), with per-tool extra handles where a feature needs them. V1a–V1d are authorized; commands that drew three loose position arrows move to the real gizmo (V1b, widened to Move Face, Move Hole, Move Component, Transform and Insert Component). V1e (chamfer by angle, native change) is still open.
