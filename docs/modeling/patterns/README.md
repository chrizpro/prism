# Solid patterns

Linear repeats in up to three directions. Circular uses an axis with optional rings. Along Curve places instances by count or spacing along a path. Fill distributes instances inside a planar region with a margin. Table reads offsets or absolute positions from rows and supports a Z rotation.

## Review and verification

This guide describes observed behavior. Additional requirements are proposed and need owner review. [Tracking issue #30](https://github.com/chrizpro/prism/issues/30).

- [Maintained headless coverage](../../../Prism.Tests/Headless/Solid/PatternTests.cs)
- [Pattern math unit tests](../../../Prism.Tests/Unit/Solid/)

The headless cases check instance count, analytic centers and conserved copy volume for all five pattern tools. Unit checks exercise path sampling, reverse alignment and fill holes.

## Coverage gaps

Complex paths, alternate fill layouts, ring and direction controls, skip lists, external data and Join/Cut results still need coverage.
