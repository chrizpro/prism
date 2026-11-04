# Solid primitives

Box, Cylinder, Sphere, Cone and Torus create exact bodies. Their dimensions and XYZ position are editable. Operation selects New body, Join, Cut or Intersect. The Cone supports a top radius and the Torus constrains its minor radius relative to the major radius.

## Review and verification

This guide describes observed behavior. Additional requirements are proposed and need owner review. [Tracking issue #26](https://github.com/chrizpro/prism/issues/26).

- [Maintained headless coverage](../../../Prism.Tests/Headless/Solid/PrimitiveTests.cs)

The headless cases compare all five default primitives with independent volume formulas and require valid single solids.

## Coverage gaps

Positioning, non-default dimensions, primitive Boolean targets and invalid dimensions still need broader coverage.
