# Solid primitives

Box, Cylinder, Sphere, Cone and Torus create exact bodies. Their dimensions and XYZ position are editable. In the viewport the position is set with the shared move gizmo at the middle of the shape, and each dimension has its own arrow on the shape ([viewport interaction](../../viewport-interaction-handoff.md#command-gizmo-2026-11-12), checks HDL-H001 to HDL-H003, HDL-H025 and HDL-H030). Operation selects New body, Join, Cut or Intersect. The Cone supports a top radius and the Torus constrains its minor radius relative to the major radius.

## Review and verification

This guide describes observed behavior. Additional requirements are proposed and need owner review. [Tracking issue #26](https://github.com/chrizpro/prism/issues/26).

- [Maintained headless coverage](../../../Prism.Tests/Headless/Solid/PrimitiveTests.cs)

The headless cases compare all five default primitives with independent volume formulas and require valid single solids.

## Coverage gaps

Positioning, non-default dimensions, primitive Boolean targets and invalid dimensions still need broader coverage.

The maintained Box cases also check Join, Cut and Intersect against an explicitly selected target, using overlapping boxes with analytic volumes. [Solid options #41](https://github.com/chrizpro/prism/issues/41).
