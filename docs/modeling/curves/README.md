# Shared curve creation

Curve uses picked points as interpolated points or control points. Curve from Data accepts formulas, typed rows, files or a Data sheet, with smooth, control-point and straight-segment outputs. Intersection Curve finds the section between shapes, including a box cut by a plane, and offers a projected flat-curve mode.

## Verification

[Curve command checks](../../../Prism.Tests/Headless/Solid/SharedToolTests.cs) and [expression unit checks](../../../Prism.Tests/Unit/Solid/CurveExpressionTests.cs). [Tracking issue #36](https://github.com/chrizpro/prism/issues/36).

## Coverage gaps

Closed and control-point curves, import variants, projected intersections and viewport picking need more coverage. New requirements need owner review.
