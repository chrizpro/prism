# Integration tests — planned

This folder reserves a place in `Prism.slnx` for desktop/viewport workflow tests. There is no integration test project or passing placeholder test yet. Add the project when the first real driver-backed tests are implemented.

The owner requested Unit and Headless examples first, with Integration following later. Existing automated screenshot work is in [PR #11](https://github.com/chrizpro/prism/pull/11). It opens generated models and captures the real viewport; it does not yet exercise the complete selection/preview/commit workflow.

## First scenarios to implement

| Reserved test ID | User workflow | Evidence and assertions |
|---|---|---|
| EXT-I001 | Create a 10 × 6 mm XY rectangle; select it; start Extrude; enter 12 mm; inspect preview; accept; undo; redo | Command state and numeric volume/bounds, fixed-camera screenshots before/preview/after/undo/redo, visible direction handle |
| REV-I001 | Create an XZ rectangle at radius 2..5 mm and height 0..8 mm; select it; choose world Z; enter 90°; preview; accept; reverse | Numeric volume of 42π mm³, positive/negative Y quadrant bounds, actual viewport screenshots and selected-axis highlight |

Use the existing authenticated Prism automation bridge exposed by MCP. Each test must own its desktop instance and document, settle the camera before capture, and save images/state under ignored `artifacts/tests/integration/`. Geometry assertions and image review establish different things. A screenshot file existing is not enough to call a workflow verified.

Return to the [test guide](../README.md) for currently runnable tests.
