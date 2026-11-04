# Solid Boolean operations

Union merges the selected bodies, Subtract cuts the tools out of a base body, and Intersect keeps their shared geometry. Keep tool bodies preserves the tools. The base receives the result.

## Review and verification

This guide describes observed behavior. Additional requirements are proposed and need owner review. [Tracking issue #28](https://github.com/chrizpro/prism/issues/28).

- [Maintained headless coverage](../../../Prism.Tests/Headless/Solid/CombineTests.cs)

The headless cases compare three overlapping-cube results with analytic volumes and check base replacement and tool retention or removal.

## Coverage gaps

Disjoint and tangent inputs, multiple tools, empty intersections and regeneration still need coverage.
