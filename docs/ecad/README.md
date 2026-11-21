# ECAD

Mechanical integration only: boards, component envelopes, fit checks and STEP exchange. It has no electrical design tools.

Owner-approved workflow (2026-11-10). [ECAD tracking issue #53](https://github.com/chrizpro/prism/issues/53).

- **Board** contains Import Board STEP, Create Board (Rectangular Board and Board from Outline), and Mounting Hole. Click a flat face to place the hole along its normal. Whole-body selection and world XY coordinates still work for horizontal boards.
- **Components** contains Component Envelope, Place on Board and a manual Transform menu (Move, Rotate, Duplicate). Place on Board takes the bottom center of an upright exact solid, seats it on the clicked board face, and allows rotation about that face normal. Use an envelope for approximate component size, or an imported solid for exact geometry.
- **Fit check** contains Interference, Check Clearance and Section View. Interference reports unchecked pairs as incomplete. Check Clearance compares the closest gap between two solids to the required distance. Zero gap includes contact and overlap. Section View shows the fit inside the enclosure.
- **Export** contains Export STEP. Review the named bodies before saving. Pick exact bodies to limit the export, or leave the selection empty to use visible exact bodies.
- **Select**, **Datum**, **References**, **Inspect** and **Appearance** provide shared CAD tools.

## Component envelope placement (owner-approved 2026-11-17; board pick implemented, the rest open)

Status: the optional **Board face** pick is implemented and covered by ECD-H011 to ECD-H013 in `EnvelopePlacementTests.cs`. Remembering the board so the envelope follows it (the owner chose this) and keeping the placement when the size is edited (ECD-P1) are not built yet. The owner has set the editing question aside for now. Finding so far: right-click Edit feature opens the latest step that changed the envelope (the Move), and editing the envelope's own step rewinds the model, so its fields show the unmoved values until OK replays the move. Today the envelope is a box built from its own Center X/Y, elevation and rotation fields, with no stored placement. Moving it and then editing its size rebuilds it at those field values, so it jumps back toward the origin.

**ECD-P1. Editing keeps the placement.**
- Changing an existing envelope's width, length, height, clearance allowance or reference designator does not change where its mounting face center is, which way it faces, or its rotation, including after it was moved or rotated with the gizmo or Transform.
- Height and clearance grow away from the board (toward +Z of the envelope's own frame, or −Z for a Bottom-side envelope). Width and length grow symmetrically about the mounting face center.
- Proposed storage: the envelope keeps its placement frame with the object, separate from the size fields. Undo and redo restore both.

**ECD-P2. Choosing the board.**
- Component Envelope has an optional **Board face** pick, the same flat-face pick Place on Board and Mounting Hole use. Clicking a board face in the viewport also sets the position.
- With a board face picked, X/Y become the offsets U/V on that face, the mounting elevation and Top/Bottom side come from the face, and rotation is about the face normal. This works for several boards at different elevations or tilts.
- With no board face picked, it behaves as it does now, in world coordinates.
- Picking a board face is a one-time placement. The envelope does not remember the board, and moving the board later does not move it. (Alternative for review: remember the board so it follows.)
- Place on Board remains the way to move an existing envelope or an imported exact solid onto a board face.

**Acceptance criteria**
1. Create an envelope, move it by (20, 10, 0) and rotate it 30°, then edit its width. Its mounting face center and rotation are unchanged and only the width changes.
2. Edit the height of a Bottom-side envelope. It still touches the board face and grows downward.
3. With two boards at different elevations, create an envelope with each board's face picked. Each one sits on its own board with zero gap, with no manual Z.
4. Create an envelope on a tilted board face. It sits flat on that face.
5. Undo after a size edit restores the previous size at the same placement.

## Maintained ECAD checks

[Board and envelope headless tests](../../Prism.Tests/Headless/Ecad/BoardTests.cs) verify board dimensions, elevation, hole toggle, invalid insets, outline closure, mounting-hole placement and misses, material retention, undo/redo, and envelope side, rotation and clearance. Run `./tools/test.ps1 -Layer Headless -Feature ECAD`. [Issue #53](https://github.com/chrizpro/prism/issues/53).

[Placement, fit and STEP tests](../../Prism.Tests/Headless/Ecad/BoardFitTests.cs) cover a rotated board, a face-based mounting hole, sufficient and insufficient gaps, overlap, incomplete interference, the ECAD groups, and selected-body STEP roundtrip. Internal outline cutouts and native document roundtrip remain open in the maintained suite. The [standalone ECAD suite](../../tests/Prism.EcadTests/Program.cs) has earlier roundtrip and STEP checks.

On 2026-11-10, Windows desktop scenario ECD-I001 placed a rotated connector envelope using a viewport click and confirmed zero clearance where it touched the board. [Desktop capture](evidence/ECD-I001-board-fit.png).

Use the [ECAD manual checklist](../tabs/checklists/ecad-manual-test-checklist.md) to verify a board from an external ECAD app and the STEP handoff to the receiving app.
