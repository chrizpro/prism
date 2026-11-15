# Solid Boolean operations

Union merges the selected bodies, Subtract cuts the tools out of a base body, and Intersect keeps their shared geometry. Keep tool bodies preserves the tools. The base receives the result.

The Combine group sits on every modeling tab — Home, Solid, Surface, NURBS and Sheet Metal. Base and tool picks accept solids and surfaces: surfaces fuse or intersect, and a surface can be cut by a solid. Subtract with a surface as the tool is described in the next section; a lone flat sheet, which encloses no space, is still refused (SFC-H014). Intersect and Subtract fail with a message when nothing would be left (SFC-H015). Until 2026-11-12, subtracting a surface from a surface reported success, changed nothing and deleted the tool, and intersecting bodies that do not meet left an empty object. Freeform omits Combine on purpose — subdivision (Catmull-Clark) cages do not survive exact booleans; Convert to NURBS first. Verified 2026-11-12 by SFC-H006/SFC-H007 in [SurfaceOptionTests](../../../Prism.Tests/Headless/Surfacing/SurfaceOptionTests.cs).

## Cutting a surface with a surface (owner request 2026-11-14)

An open surface does not need to be closed by hand to cut with. Two extruded rectangles, say, are open tubes (walls only); using one to cut the other just works.

- The tool is closed in memory (a flat cap over each open end, or the surface as it is when it already encloses space) only to tell what is inside it. Whatever part of the target lies inside is removed.
- **Fill the cut with the tool's walls** (shown when a surface is a tool; **on by default**): the tool's walls that lie inside the target are kept and joined on, so the result is closed along the cut, a notched tube. Off leaves the cut open, the walls simply missing.
- A solid cut by a surface that encloses space works the same way. A surface that cannot enclose space (one flat sheet, or ends that cannot be capped) is refused with a message and both objects are left alone.
- **Acceptance.** Target tube B (20 x 20 mm, 10 mm tall, wall area 800 mm²) overlapping tool tube A on one corner by 10 x 10 mm: with fill off the result's walls are 800 - 2 x 100 = **600 mm²**; with fill on the tool's two walls inside B add 2 x 100 mm², giving **800 mm²** and no open edge along the cut. Tests: [SFC-H019 to SFC-H021](../../../Prism.Tests/Headless/Surfacing/CombineSurfaceTests.cs) and SFC-H014. Old steps saved without the option reopen with fill on.
- Not done: Union and Intersect of open surfaces still use the plain operations; the fill option applies to Subtract.

## Review and verification

This guide describes observed behavior. [Tracking issue #28](https://github.com/chrizpro/prism/issues/28).

- [Maintained headless coverage](../../../Prism.Tests/Headless/Solid/CombineTests.cs)

The headless cases compare three overlapping-cube results with analytic volumes and check base replacement and tool retention or removal.

Volume and mass queries on solids with spline faces use adaptive integration over spline spans. A cylindrical cut on a curved loft exposed an error in the earlier integration. Prism reported 13,563.789 mm³ before the cut and 15,887.178 mm³ afterward. The rebuilt kernel measured 16,254.111 mm³ before the cut, 15,887.051 mm³ remaining and 367.061 mm³ removed. An independent triangulated estimate agreed within 0.03% for both the original and trimmed solids. On 2026-11-09, SOL-H107 reproduced the error on Windows and passed against the rebuilt native kernel. It verifies that the remaining and removed volumes add up to the original within 0.001%, and that the mass and volume queries agree. [Issue #92](https://github.com/chrizpro/prism/issues/92)

The Windows verification run passed all 175 Headless tests selected by `Feature=Solid|Feature=Loft|Feature=Mold|Feature=SheetMetal|Feature=Gallery` against the rebuilt Release kernel.

## Coverage gaps

Disjoint and tangent inputs, multiple tools, empty intersections and regeneration still need coverage.


**Approved 2026-11-14:** a consistent *Keep the originals* option across tools that consume curves or bodies (Combine already has *Keep tool bodies*). See [0.4 modeling gaps, item 6](../surface-and-nurbs/README.md#0-4-modeling-gaps-approved-by-the-owner-2026-11-14).
