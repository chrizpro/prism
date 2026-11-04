# Rectangle

Open Sketch from Solid, select Rectangle, and choose a placement mode from the tool options.

| Mode | Placement | Controls |
| --- | --- | --- |
| 2-Point | Two opposite corners | Typed width and height, Orientation, Rotate preview, Shift to square |
| Center | Center and a corner | Typed full width and height, Orientation, Rotate preview, Shift to square |
| 3-Point | Two base endpoints and the signed perpendicular width | Typed base length and angle, then width. H and V lock the base. Shift locks it to the nearest axis. |

Center adds a construction diagonal and a midpoint constraint. A degenerate rectangle keeps the placed points without creating zero-length sides.

## Verification

[Maintained Rectangle tests](../../../../Prism.Tests/Headless/Sketch/RectangleTests.cs) check corner coordinates, loop closure and solver convergence. [Existing standalone Sketch 2D checks](../../../../tests/Prism.Sketch2DTests/Program.cs) also exercise rotated geometry, Shift placement, deletion and Undo. [Tracking issue #39](https://github.com/chrizpro/prism/issues/39).

## Coverage gaps

The maintained suite also covers Rotate preview in both applicable modes and typed base length, angle and width in 3-Point. Combinations of placement with snapping and construction mode still need separate evidence. These are coverage gaps for existing behavior. New or changed requirements remain subject to owner review.
