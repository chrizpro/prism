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
- **Finish:** Sew, Unsew, Thicken, Trim, Untrim, Extend, Reverse Normal, Offset
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
- Compare Warp with the old Deform on NURBS surfaces. Deform moved control points directly, and Warp's result has not been compared.
- A second rail steers rotation only. It does not stretch the profile to reach the rail.

## Feature requirement reviews

- [Surface Extrude](../extrude/README.md#surface-and-nurbs-extrude) — draft owner-review specification, linked to [Story #7](https://github.com/chrizpro/prism/issues/7). This is not a claim of approved behavior or passing tests.

## Maintained option coverage

[Surface and NURBS headless tests](../../../Prism.Tests/Headless/Surfacing/SurfaceOptionTests.cs) launch the tools from both ribbons. They check signed planar Offset and Thicken, single and multiple Isocurves in both directions, and NURBS Control Grid plane, dimensions, grid counts, degree and offset on flat examples.

Run `./tools/test.ps1 -Layer Headless -Feature Surfacing`. [Pipe](../pipe/README.md) has separate exact and cage checks. [Boundary Surface, Ruled and Patch](../boundary-surface/README.md) have separate option checks. Trimming and sewing options, curve editing, NURBS control-point editing and continuity, and curved or invalid inputs remain coverage work. [Surface #48](https://github.com/chrizpro/prism/issues/48) and [NURBS #47](https://github.com/chrizpro/prism/issues/47) track it.
