# ECAD

Mechanical integration only: boards, component envelopes, fit checks and STEP exchange. It has no electrical design tools.

Owner-approved workflow (2026-11-10). [ECAD tracking issue #53](https://github.com/chrizpro/prism/issues/53).

- **Board** contains Import Board STEP, Create Board (Rectangular Board and Board from Outline), and Mounting Hole. Click a flat face to place the hole along its normal. Whole-body selection and world XY coordinates still work for horizontal boards.
- **Components** contains Component Envelope, Place on Board and a manual Transform menu (Move, Rotate, Duplicate). Place on Board takes the bottom center of an upright exact solid, seats it on the clicked board face, and allows rotation about that face normal. Use an envelope for approximate component size, or an imported solid for exact geometry.
- **Fit check** contains Interference, Check Clearance and Section View. Interference reports unchecked pairs as incomplete. Check Clearance compares the closest gap between two solids to the required distance. Zero gap includes contact and overlap. Section View shows the fit inside the enclosure.
- **Export** contains Export STEP. Review the named bodies before saving. Pick exact bodies to limit the export, or leave the selection empty to use visible exact bodies.
- **Select**, **Datum**, **References**, **Inspect** and **Appearance** provide shared CAD tools.

## Maintained ECAD checks

[Board and envelope headless tests](../../Prism.Tests/Headless/Ecad/BoardTests.cs) verify board dimensions, elevation, hole toggle, invalid insets, outline closure, mounting-hole placement and misses, material retention, undo/redo, and envelope side, rotation and clearance. Run `./tools/test.ps1 -Layer Headless -Feature ECAD`. [Issue #53](https://github.com/chrizpro/prism/issues/53).

[Placement, fit and STEP tests](../../Prism.Tests/Headless/Ecad/BoardFitTests.cs) cover a rotated board, a face-based mounting hole, sufficient and insufficient gaps, overlap, incomplete interference, the ECAD groups, and selected-body STEP roundtrip. Internal outline cutouts and native document roundtrip remain open in the maintained suite. The [standalone ECAD suite](../../tests/Prism.EcadTests/Program.cs) has earlier roundtrip and STEP checks.

On 2026-11-10, Windows desktop scenario ECD-I001 placed a rotated connector envelope using a viewport click and confirmed zero clearance where it touched the board. [Desktop capture](evidence/ECD-I001-board-fit.png).

Use the [ECAD manual checklist](../tabs/checklists/ecad-manual-test-checklist.md) to verify a board from an external ECAD app and the STEP handoff to the receiving app.
