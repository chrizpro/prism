# Surface and NURBS tabs

The two tabs do different jobs. **Surface** builds surfaces from curves, edges and faces. **NURBS** is the styling workflow: draw with Create Sketch (K), then shape with control points. Point editing stays on NURBS.

Living feature specifications: [Surface and NURBS Extrude](../extrude/README.md#surface-and-nurbs-extrude) and [Revolve](../revolve/README.md). They distinguish the workflows and their issues even where the current ribbon calls the same command implementation.

[Sweep](../sweep/README.md) and [Loft](../loft/README.md) follow the same arrangement, with separate Surface/NURBS workflow issues and shared exact-command behavior documented once.

They share one definition of **Curves**, **Edit curves**, Sweep and Boundary Surface, so those tools behave the same on both. Inspect on both adds Zebra, Curvature Map and Curvature Graph.

Solid and Freeform take a smaller Curves section: Curve, Curve from Data and Intersection Curve. Freeform also has Edit curves (Join, Trim, Extend, Rebuild, Blend) and Zebra.

Home has one **Curves** dropdown with every tool from Curves and Edit curves, because you may not have chosen a workflow yet.

## Surface

- **Select, Datum:** shared sections
- **References:** Create Sketch, Construction Plane
- **Curves:** Curve, Curve from Data, Intersection Curve, Project, Isocurves, Contour, Duplicate Edge
- **Create:** Extrude, Revolve, Sweep (dropdown with Pipe), Loft, Boundary Surface
- **Modify:** Trim, Sew, More (Offset, Thicken, Unsew, Untrim, Extend, Reverse Normal, Enclosed Body, Flatten / Refold, Warp)
- **Edit curves:** Fillet / Chamfer, Offset, Blend, Join, Explode, Trim, Extend, Rebuild, Divide
- **Convert:** Convert to NURBS
- **Combine:** Union, Subtract, Intersect (SFC-H006)
- **Transform, Inspect, Appearance:** shared sections

## NURBS

- **Select, Datum:** shared sections
- **References:** Create Sketch (K), Construction Plane
- **Curves, Edit curves:** same as Surface
- **Surfaces:** Control Grid, Extrude, Revolve, Sweep (dropdown with Pipe), Loft, Boundary Surface
- **Shape:** Edit Points, Sculpt, Rebuild Surface, Warp (direct buttons)
- **Detail:** Add Detail, Raise Degree
- **Convert:** Convert to NURBS
- **Continuity:** Match Surface, Check Seam
- **Finish:** Sew, Unsew, Thicken, Trim, Untrim, Extend, Reverse Normal, Offset, Enclosed Body
- **Combine:** Union, Subtract, Intersect — the same BooleanCommand as Solid and Home; it accepts solids and surfaces (SFC-H006)
- **Transform, Inspect, Appearance:** shared sections

## One tool, several jobs

| Tool | What it covers |
|---|---|
| Boundary Surface | A flat loop becomes a flat surface, with loops inside it as holes. Two to four curves span a surface. Other loops fill smoothly. Curves across the inside are followed exactly, or fitted through points. Two separate surface edges are blended. |
| Sweep | One path, or a path plus a second rail that steers how the profile turns. |
| Curve | Through the points, or control points. |
| Curve from Data | An equation, or a table or file of points. |
| Fillet / Chamfer | Rounds or cuts the corners of a flat curve. |
| Trim Curve | Keeps one part, or cuts the curve into pieces. |
| Intersection Curve | Between planes, faces, surfaces or bodies, or two flat curves on different planes. |

The older commands (Sweep 2 Rails, Patch, Planar Surface, Ruled, Network Surface, Blend Surface, Split Curve, Deform, Intersect Surfaces, Curve Intersect) are off the ribbon but still work from scripts.

## Still to check

- Drag every merged tool in the running app; these results come from headless tests.
- A second rail steers rotation only. It does not stretch the profile to reach the rail.

Checked in the running app on 2026-11-12:

- Control Grid creates a valid surface (one face, settable grid/degree/relief) and Edit Points, Sculpt, Rebuild Surface, Add Detail, Raise Degree, Match Surface and Check Seam all open or complete on it. The flat-from-one-angle preview is the shallow default relief, not a failure.
- Warp on NURBS surfaces was broken — a stored Control Grid patch failed with "Face has no surface." and warp on lofted/swept surfaces crashed the desktop app. Root cause and fix are recorded in the [transform guide](../transform/README.md#coverage-gaps); WRP-H008 covers it and warp on Control Grid, loft and sweep surfaces now completes in the running app.
- Compared Warp with the old Deform on a Control Grid patch: Deform moves the surface's control net directly and still works; Warp now reaches the same control-net machinery and produces a bent sheet.
- Trim works on a Control Grid patch (subtractive direction), and Untrim restores it.

Approved and shipped on 2026-11-12:

- **Enclosed Body is on the NURBS tab** under Finish (SFC-H005), matching its Surface > Modify placement.
- **Combine (Union / Subtract / Intersect) is on every modeling tab** — Home, Solid, Surface, NURBS and Sheet Metal (SFC-H006). `BooleanCommand` accepts `SelFilter.Solid | SelFilter.SurfaceObj` for base and tools, so it works on surfaces as well as solids; surface Union and solid×surface Intersect build in the running app, while subtracting open surfaces reports "Boolean operation failed" as expected for open geometry. **Freeform intentionally omits Combine** (SFC-H007): Catmull-Clark cages do not survive exact booleans; Convert to NURBS first, then combine.
- **Additive/subtractive NURBS modeling**: Trim/Untrim is the subtractive path on open surfaces and Combine is the additive path once sheets sew or enclose into bodies.

## Feature requirement reviews

- [Surface Extrude](../extrude/README.md#surface-and-nurbs-extrude) — draft owner-review specification, linked to [Story #7](https://github.com/chrizpro/prism/issues/7). This is not a claim of approved behavior or passing tests.

## Maintained option coverage

The public [Surfaces and NURBS walkthrough](../../../site/guide/surfacing.md) includes actual app captures for ordered Loft profiles, Thicken, NURBS Control Grid, selected control-point editing and zebra inspection. The separate Surface and NURBS ribbon pages use their own workflow captures. These are documentation examples; [capture provenance and verification](../../../site/README.md#guide-images) are separate from the maintained tests below. Proposed feature requirements remain proposed.

[Surface tool tests](../../../Prism.Tests/Headless/Surfacing/SurfaceToolTests.cs) cover Edge Surface (two and four curves), Blend Surface at each continuity and fullness, Sweep 2 Rails as surface and solid, Rebuild Surface, Network Surface, Planar Surface with a hole, Knit with and without solid and its tolerance, Trim Surface with a solid or a surface on either side, Enclosed Body with listed spaces, Unsew of whole bodies and single faces, both Untrim modes, Extend and Reverse Normal. [Inspect tool tests](../../../Prism.Tests/Headless/Solid/AnalysisToolTests.cs) cover Surface Continuity and Match Surface at G0, G1 and G2.

Fixed on 2026-11-05: Rebuild Surface's "Largest deviation (approx.)" used to measure distance to the result's tessellation vertices, so an exactly reproduced flat sheet reported several millimetres and a warning. It now measures the distance from points sampled on the original to the rebuilt surface, and the test asserts 0 mm for the flat sheet.

[Surface and NURBS headless tests](../../../Prism.Tests/Headless/Surfacing/SurfaceOptionTests.cs) launch the tools from both ribbons. They check signed planar Offset and Thicken, single and multiple Isocurves in both directions, and NURBS Control Grid plane, dimensions, grid counts, degree and offset on flat examples.

Run `./tools/test.ps1 -Layer Headless -Feature Surfacing`. [Pipe](../pipe/README.md) has separate exact and cage checks. [Boundary Surface, Ruled and Patch](../boundary-surface/README.md) have separate option checks. Trimming and sewing options, curve editing, NURBS control-point editing and continuity, and curved or invalid inputs remain coverage work. [Surface #48](https://github.com/chrizpro/prism/issues/48) and [NURBS #47](https://github.com/chrizpro/prism/issues/47) track it.


Desktop integration: [SFC-I001 viewport evidence](../../integration-evidence/README.md#sfc-i001--control-grid-and-thicken) verifies MCP Control Grid, Thicken and document reopen.

Windows verification on 2026-11-05: the combined candidate passed 86 Unit and 592 Headless tests with rebuilt native libraries. See the [integration verification report](../../engineering/release-readiness-2026-11-05.md) for source revisions, commands and limits.
The [SFC-I001 test](../../../Prism.Tests/Integration/DesktopModelingTests.cs) also checks the measured distance between a face and a parallel offset surface through MCP.
