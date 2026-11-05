# Solid transforms

Move translates in XYZ. Rotate uses a world axis and a selectable pivot. Scale is uniform. Mirror reflects across a world plane and defaults to making a copy. Duplicate copies selected objects through the session action.

## Review and verification

This guide describes observed behavior. Additional requirements are proposed and need owner review. [Tracking issue #29](https://github.com/chrizpro/prism/issues/29).

- [Maintained headless coverage](../../../Prism.Tests/Headless/Solid/TransformTests.cs)

The headless cases check exact-body bounds and volume for translation, world-origin rotation, uniform scaling and mirror copy, plus the Duplicate session action.

[Warp tests](../../../Prism.Tests/Headless/Solid/WarpTests.cs) check Stretch with start/end ranges, Taper, Twist, Bend of a line centred on the origin and a stretched solid.

## Coverage gaps

Fixed on 2026-11-05: Warp's Bend used to add the shape's centre along the bend axis twice, so a shape not centred on the origin jumped when bent (a line from X = 0 to 100 bent 90° ended at X ≈ 113.7 instead of 63.7). Bend tests now cover a centred line and one starting at the origin.


Mixed selections, custom transform pivots and desktop gizmo interactions still need coverage.
