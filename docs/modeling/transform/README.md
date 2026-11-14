# Solid transforms

Move translates in XYZ. Rotate uses a world axis and a selectable pivot. Scale is uniform by default; **Scale type: Per axis (stretch)** gives X, Y and Z their own factors (owner report 2026-11-13: only a uniform scale existed). In that mode the viewport shows one arrow on each face of the selection and dragging it sets that factor (`SOL-H110`, `SOL-H111`). The stretch follows the world axes, so a part turned off-axis is sheared; a sphere becomes an ellipsoid and other curved faces become freeform surfaces. The kernel call needs the native library built from this change (`ck_scale3`). Mirror reflects across a world plane and defaults to making a copy. Duplicate copies selected objects through the session action.

In the viewport Move, Rotate and Scale are driven by the shared gizmo: arrows for the offsets, three rings for the angle (pressing a ring makes its axis the turn axis and starts again from 0) and the centre block for the factor. Maintained check: HDL-H023 in [ViewportHandleTests](../../../Prism.Tests/Headless/Viewport/ViewportHandleTests.cs); see [viewport interaction](../../viewport-interaction-handoff.md#command-gizmo-2026-11-12).

## Review and verification

This guide describes observed behavior. Additional requirements are proposed and need owner review. [Tracking issue #29](https://github.com/chrizpro/prism/issues/29).

- [Maintained headless coverage](../../../Prism.Tests/Headless/Solid/TransformTests.cs)

The headless cases check exact-body bounds and volume for translation, world-origin rotation, uniform scaling and mirror copy, plus the Duplicate session action.

[Warp tests](../../../Prism.Tests/Headless/Solid/WarpTests.cs) check Stretch with start/end ranges, Taper, Twist, Bend of a line centred on the origin, a stretched solid and Bend of a stored NURBS surface patch (WRP-H008).

## Coverage gaps

Fixed on 2026-11-05: Warp's Bend used to add the shape's centre along the bend axis twice, so a shape not centred on the origin jumped when bent (a line from X = 0 to 100 bent 90° ended at X ≈ 113.7 instead of 63.7). Bend tests now cover a centred line and one starting at the origin.

Fixed on 2026-11-12: Warp could not touch any stored NURBS surface — a Control Grid patch reported "Face has no surface." and lofted or swept surfaces crashed the app outright. The build reads the object's mesh for its bounds before converting to NURBS, and converting an already-NURBS face that carries triangulation produced faces with no surface at all (the converter rebuilds those faces for the cached mesh but reports no surface change). `ck_nurbs_convert` now converts a mesh-free copy. WRP-H008 bends a stored patch 90° into a quarter cylinder; warp on Control Grid, loft and sweep surfaces was re-verified in the running app.


Mixed selections, custom transform pivots and desktop gizmo interactions still need coverage.

Windows verification on 2026-11-05: the combined candidate passed 86 Unit and 592 Headless tests with rebuilt native libraries. See the [integration verification report](../../engineering/release-readiness-2026-11-05.md) for source revisions, commands and limits.
