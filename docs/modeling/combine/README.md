# Solid Boolean operations

Union merges the selected bodies, Subtract cuts the tools out of a base body, and Intersect keeps their shared geometry. Keep tool bodies preserves the tools. The base receives the result.

The Combine group sits on every modeling tab — Home, Solid, Surface, NURBS and Sheet Metal. Base and tool picks accept solids and surfaces: surfaces fuse or intersect, and subtracting open surfaces fails with "Boolean operation failed". Freeform omits Combine on purpose — subdivision (Catmull-Clark) cages do not survive exact booleans; Convert to NURBS first. Verified 2026-11-12 by SFC-H006/SFC-H007 in [SurfaceOptionTests](../../../Prism.Tests/Headless/Surfacing/SurfaceOptionTests.cs).

## Review and verification

This guide describes observed behavior. [Tracking issue #28](https://github.com/chrizpro/prism/issues/28).

- [Maintained headless coverage](../../../Prism.Tests/Headless/Solid/CombineTests.cs)

The headless cases compare three overlapping-cube results with analytic volumes and check base replacement and tool retention or removal.

Volume and mass queries on solids with spline faces use adaptive integration over spline spans. A cylindrical cut on a curved loft exposed an error in the earlier integration. Prism reported 13,563.789 mm³ before the cut and 15,887.178 mm³ afterward. The rebuilt kernel measured 16,254.111 mm³ before the cut, 15,887.051 mm³ remaining and 367.061 mm³ removed. An independent triangulated estimate agreed within 0.03% for both the original and trimmed solids. On 2026-11-09, SOL-H107 reproduced the error on Windows and passed against the rebuilt native kernel. It verifies that the remaining and removed volumes add up to the original within 0.001%, and that the mass and volume queries agree. [Issue #92](https://github.com/chrizpro/prism/issues/92)

The Windows verification run passed all 175 Headless tests selected by `Feature=Solid|Feature=Loft|Feature=Mold|Feature=SheetMetal|Feature=Gallery` against the rebuilt Release kernel.

## Coverage gaps

Disjoint and tangent inputs, multiple tools, empty intersections and regeneration still need coverage.
