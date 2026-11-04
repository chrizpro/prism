# Solid transforms

Move translates in XYZ. Rotate uses a world axis and a selectable pivot. Scale is uniform. Mirror reflects across a world plane and defaults to making a copy. Duplicate copies selected objects through the session action.

## Review and verification

This guide describes observed behavior. Additional requirements are proposed and need owner review. [Tracking issue #29](https://github.com/chrizpro/prism/issues/29).

- [Maintained headless coverage](../../../Prism.Tests/Headless/Solid/TransformTests.cs)

The headless cases check exact-body bounds and volume for translation, world-origin rotation, uniform scaling and mirror copy, plus the Duplicate session action.

## Coverage gaps

Mixed selections, custom transform pivots and desktop gizmo interactions still need coverage.
