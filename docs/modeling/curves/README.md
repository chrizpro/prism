# Shared curve creation

Curve uses picked points as interpolated points or control points. Curve from Data accepts formulas, typed rows, files or a Data sheet, with smooth, control-point and straight-segment outputs. Intersection Curve finds the section between shapes, including a box cut by a plane, and offers a projected flat-curve mode.

## Verification

[Curve command checks](../../../Prism.Tests/Headless/Solid/SharedToolTests.cs) and [expression unit checks](../../../Prism.Tests/Unit/Solid/CurveExpressionTests.cs). [Tracking issue #36](https://github.com/chrizpro/prism/issues/36).

[Curve editing tests](../../../Prism.Tests/Headless/Curves/CurveToolTests.cs) characterize every option of Fillet / Chamfer Curve, Offset Curve, Blend Curve, Join and Explode, Trim, Split and Extend, Divide, Rebuild, Project Curve, Intersect Surfaces, Duplicate Edge and the Curvature Graph, plus Contour in the [surface tool tests](../../../Prism.Tests/Headless/Surfacing/SurfaceToolTests.cs). Run `./tools/test.ps1 -Feature Curves`.

## Coverage gaps

Closed and control-point curves, import variants, projected intersections and viewport picking need more coverage. New requirements need owner review.

Windows verification on 2026-11-05: the combined candidate passed 86 Unit and 592 Headless tests with rebuilt native libraries. See the [integration verification report](../../engineering/release-readiness-2026-11-05.md) for source revisions, commands and limits.
