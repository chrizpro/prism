# Freeform modeling usability review

Scope: how the Freeform (subdivision cage) tools are triggered and whether that matches what users of other subdivision and CAD modelers expect. Method: source review, the existing `Prism.FreeformWorkflowTests` (31 checks, all pass), and a headless probe that drove each tool through the real ribbon, panel, pointer and hotkey paths on a 40 mm box with 3x3x3 segments. This is a research deliverable; no behavior was changed (except one documentation fix, see below).

## Update after review

Insert edge loop is now a sticky click-to-insert tool with a hover preview (see `FreeformEditing.md`). Recommendations 1, 4 (for Insert) and 7 are addressed; the rest remain open.

## Summary

Selection is intuitive. Operations are where the workflow diverges from common expectations: most tools are "select, then press a button, then tune numbers in the panel", while comparable modelers make the main topology tools interactive (hover preview, click to place, drag to adjust).

**On the edge loop question:** common subdivision and CAD modelers do not make the user pick two edges to insert a loop. They offer an insert-loop tool: hover an edge, see a preview line across its ring, click once to place it, and optionally drag to slide it. A single edge defines the loop. Prism already needs only one edge, but it does not place the loop on click.

## How loops are triggered today

| Step | Prism behavior | Verdict |
| --- | --- | --- |
| Select an edge | One click picks it (9 px threshold, hover highlight). | Matches convention |
| Insert | Press ribbon **Insert Edge** or panel **Insert edge loop**. | Extra step; no hover preview |
| Position | Panel numbers: "Loops to insert" (1-24), "Loop position along the edge" (0-1, default 0.5). | Not interactive; use Slide afterwards |
| Result | New vertices selected, mode switches to Vertex. Success is silent. | Needs a status message |
| Select an existing loop | Double-click an edge, or Alt+L; Alt+R for a ring. | Matches convention |

Findings:
- Insert only processes the first selected edge (all selected faces in Face mode). After double-clicking a 12-edge loop, Insert adds one ring, not twelve. Users selecting a loop would not expect that.
- With nothing or a vertex selected it errors with "Select an edge (or a face) to insert a loop through." That message is clear, but nothing in the panel hint or the prompt text ever mentions Insert.

## Per-tool review

| Tool | Trigger | Expected convention | Verdict |
| --- | --- | --- | --- |
| Select modes | 1 / 2 / 3 / 4; ribbon Select group; panel Select choice (kept in sync) | Number keys or mode buttons | Intuitive |
| Click, Shift+click, box, Ctrl+lasso | Pointer | Standard | Intuitive |
| Loop / ring select | Double-click, Alt+L, Alt+R, ribbon Loops group | Double-click or Alt-click on an edge | Intuitive |
| Move / rotate / scale | Gizmo, W / E / R, G places the gizmo | Standard | Intuitive |
| Edit Freeform | Tab, ribbon | Tab toggles edit mode | Intuitive |
| Insert edge loop | Select, then button | Tool with hover preview, click to place | Works; needs interactive tool |
| Extrude | Alt+X or ribbon; offsets by panel "Extrude distance" (default 8 mm), then faces stay selected | Starts at zero and follows the drag | Works; not interactive. Faces and open edges only (clear errors otherwise) |
| Inset | Panel only, visible in Face mode | Hotkey or ribbon button with drag | Hard to find |
| Bevel selected edges | Panel only | Bevel widens an edge into a strip | **Misleading name**: it adds support loops either side of the edge to tighten it (confirmed: 12 ring edges give 24 support loops). The strip-producing tool is **Chamfer** |
| Chamfer | Panel only; needs inner edges | Bevel | Works (12 loop edges give 12 new faces). A single isolated edge may fail with "needs inner edges that run through the form", so select a loop first |
| Slide | Panel only | Slide along the loop | Works; discoverable only in the panel |
| Bridge | Ribbon Loops group; two open edge chains | Two picks | Correct place for a two-pick workflow; clear error otherwise |
| Merge edges / faces, Weld, Unweld, Collapse, Split face, Delete | Ribbon (some) or panel | Standard | Work. Unweld errors clearly when the edge is not interior |
| Crease | C, live preview, Enter / Esc | Standard | Intuitive |
| Subdivide, Smooth, Flatten, Pull, Flip normals | Ribbon / panel | Standard | Work |
| Freeze / Unfreeze, Thicken, Fill holes, Mirror and weld | Ribbon / panel | Standard | Work; Fill holes errors correctly on a closed cage |

All ribbon buttons resolved to the right panel action and had correct enabled states.

## Silent successes

These succeed without any status message, so the user must infer the result from the viewport: Insert Edge, Subdivide, Weld, Delete, Collapse, Split face, Smooth, Flatten, Flip normals, Mirror and weld, and Extrude. Several of them also leave the status bar showing the previous hint.

## Discoverability

About 50 controls sit in the panel. Inset, Bevel, Chamfer, Slide, Delete, Merge faces, Collapse, Split face, Smooth, Flatten, Fill holes and Thicken have no ribbon button or shortcut. The panel hint ("Click to select; drag the gizmo...") does not explain the select-then-act model.

## Recommendations (priority order)

1. **Interactive insert-loop mode.** Hover shows a preview line across the ring under the cursor; click places it; drag slides it; the wheel changes the count. Keep the current button as a numeric fallback.
2. **Insert on a selected loop** should insert one ring per selected edge ring (de-duplicated), not only the first.
3. **Interactive extrude.** Start at zero distance and drag the arrows, keeping the numeric field for exact values.
4. **Status messages** for every silent success, for example "Inserted 1 loop (12 faces added)".
5. **Naming.** Rename "Bevel selected edges" to "Support loops" or "Tighten edges" and present Chamfer as the bevel.
6. **Ribbon exposure** for Inset, Chamfer, Slide, Delete and Merge faces, plus hotkeys for the most common ones.
7. **Prompt text** should state "select an edge, then Insert Edge".
8. **Tests.** Add workflow coverage for insert loop, inset, chamfer and slide, which the current suites do not exercise.

## Documentation fix made

`FreeformEditing.md` listed "E outside an active editor or sketch" as Extrude. The code and tests use Alt+X; E switches the gizmo to rotate. The row is corrected.

## Update: ribbon tiers

Inset, Chamfer and Slide now have their own ribbon buttons beside Extrude and Insert Edge. Less frequent editing tools (Delete, Merge faces, Collapse, Split face, Bevel, Smooth, Flatten, Fill holes, Thicken) sit in a **More** dropdown in Modify. Niche selection helpers such as Grow stay in the Edit Freeform panel only. The ribbon Select group and the panel's Select choice share one setting and are covered by workflow tests.

## Update: loops group

Insert Edge and Inset moved to the Loops group; Select Loop is one tool: choose Loop or Ring under **Edge selection** in the panel (Alt+L and Alt+R still pick directly); Chamfer moved to **More**. Subdivide now splits only the selected faces when any are selected. The ribbon selector now shows the active mode, so it matches the panel. Decreasing resolution is not offered: it cannot be done reliably without losing edits, so undo is the way back.

## Update: subdivide and smoothing

**Subdivide** opens its own panel step with **Divisions across** and **Divisions down** (1-16) and a live preview; it applies to the selected faces, or every face when none are selected. Non-quad faces get one centre split. **Smooth surface** (Components group, or the panel action) toggles between the smoothed surface and the plain cage faces; **Smoothness** now allows 0 for the cage view.
