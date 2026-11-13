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
- **Modify:** Trim, Split Surface, Sew, More (Offset, Thicken, Unsew, Untrim, Extend, Reverse Normal, Enclosed Body, Flatten / Refold, Warp)
- **Edit curves:** Fillet / Chamfer, Offset, Blend, Join, Explode, Trim, Extend, Rebuild, Divide
- **Convert:** Convert to NURBS
- **Combine:** Union, Subtract, Intersect (SFC-H006)
- **Transform, Inspect, Appearance:** shared sections (Transform includes Mirror — it accepts any object, so surfaces mirror directly; SFC-H009)

## NURBS

- **Select, Datum:** shared sections
- **References:** Create Sketch (K), Construction Plane
- **Curves, Edit curves:** same as Surface
- **Surfaces:** Control Grid, Extrude, Revolve, Sweep (dropdown with Pipe), Loft, Boundary Surface
- **Shape:** Edit Points, Sculpt, Rebuild Surface, Warp (direct buttons)
- **Detail:** Add Detail, Raise Degree
- **Continuity:** Match Surface, Check Seam
- **Finish:** Sew, Unsew, Thicken, Trim, Split Surface, Untrim, Extend, Reverse Normal, Offset, Enclosed Body
- **Combine:** Union, Subtract, Intersect — the same BooleanCommand as Solid and Home; it accepts solids and surfaces (SFC-H006)
- **Transform, Inspect, Appearance:** shared sections (Transform includes Mirror — surfaces mirror directly; SFC-H009)

## One tool, several jobs

| Tool | What it covers |
|---|---|
| Boundary Surface | A flat loop becomes a flat surface, with loops inside it as holes. Two to four curves span a surface. Other loops fill smoothly. Curves across the inside are followed exactly, or fitted through points. Two separate surface edges are blended. |
| Sweep | One path, or a path plus a second rail that steers how the profile turns. |
| Curve | Through the points, or control points. |
| Curve from Data | An equation, or a table or file of points. |
| Fillet / Chamfer | Rounds or cuts the corners of a flat curve, or the edges where two faces of a surface or body meet. |
| Draw on Surface | A curve laid on the surface, or a split of the face along it. |
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
- **Combine (Union / Subtract / Intersect) is on every modeling tab** — Home, Solid, Surface, NURBS and Sheet Metal (SFC-H006). `BooleanCommand` accepts `SelFilter.Solid | SelFilter.SurfaceObj` for base and tools, so it works on surfaces as well as solids; surface Union and solid×surface Intersect build in the running app, and a solid subtracts from a surface. Subtract refuses a surface as the tool with a message that points to Trim and Split Surface (SFC-H014), and an intersection with nothing in common fails instead of leaving an empty object (SFC-H015). Union of surfaces gathers them into one surface object and splits them where they cross; it does not close them into a solid, which is the job of Sew or Enclosed Body. **Freeform intentionally omits Combine** (SFC-H007): Catmull-Clark cages do not survive exact booleans; Convert to NURBS first, then combine.
- **Additive/subtractive NURBS modeling**: Trim/Untrim is the subtractive path on open surfaces and Combine is the additive path once sheets sew or enclose into bodies.
- **Convert to NURBS is off the NURBS tab** (owner direction 2026-11-12): the tab is already NURBS geometry, so the group carried no work there. It remains on Surface and Freeform, where it converts their outputs.
- **Split Surface is on Surface (Modify) and NURBS (Finish)** (SFC-H008). Trim cuts and keeps one side; Split keeps every piece. The **Result** option decides how (owner decision 2026-11-12):
  - **One surface, several faces** (default, SFC-H012): the surface stays one object and each piece is a face of it, joined to its neighbours along the cut. Faces can be selected, deleted and given static-analysis loads on their own, and a later Thicken or Sew keeps them.
  - **Separate surfaces** (SFC-H008, SFC-H010): each piece becomes its own surface object, the same contract Split Body has for solids. Use this when the pieces need different colors, because Prism stores one color per object.
- **Split Surface also takes sketches and curves as the tool** (SFC-H010), which Split Body does not: a sketch or curve splits the surface along its projection (projected along the sketch plane's normal when it has one, otherwise pulled down the surface normals; a curtain through the curve is the fallback). Picking a whole sketch splits along every curve in it, including curves that cross each other (SFC-H011); picking one sketch curve splits along just that one. A curve must run from edge to edge, or close on itself, to divide the surface.
- **Delete removes a selected surface face** (SFC-H013, owner decision 2026-11-12): with faces of a surface selected and no tool open, the Delete key removes those faces and leaves the gap; the rest of the surface stays. Deleting every face removes the surface. This is the quick subtractive workflow: draw a line where the boundary should go, split, select the face to drop, press Delete.
- **Fixed on 2026-11-12:** a sketch whose curves cross (two lines forming a cross) only split along some of them, because the kernel does not intersect the parts of one tool with each other. Split Surface now cuts with each curve in turn.
- **Fillet / Chamfer also dresses surface edges** (SFC-H016, owner direction 2026-11-12): the Edit curves button now takes edges of a surface or solid body as well as flat curves. An edge where two faces of one surface meet is rounded or cut on the surface itself; several edges can be picked. A border edge is refused with a message, and separate surfaces must be sewn first so they share the edge. No new ribbon button was added.
- **Draw on Surface can split the face along the drawn curve** (SKT-H057 to SKT-H059), so a cut line can be drawn in place without a sketch. Details are in the [sketch guide](../sketches/README.md#drawing-directly-on-a-surface).
- **The cut of a split is a real edge** (SFC-H017, fixed 2026-11-12): on a smooth NURBS surface the new edge was hidden as a patch seam, so it could not be seen or picked. It is now drawn and pickable. See the [selection review](../../interface/selection/README.md#selection-levels-on-surfaces-review-2026-11-12).
- **Isolate with faces selected shows only those faces** ([selection guide](../../interface/selection/README.md#isolate-faces)).
- **Not available:** a color per face. Appearance is stored per object, so differently colored regions need the Separate surfaces result. Per-face color would be a new feature and has not been proposed in detail.
- **Mirror was already on Surface, NURBS, Solid and Freeform** via the shared Transform group (SFC-H009); `TransformCommand` accepts any object kind, so surfaces mirror without conversion. Freeform additionally keeps its cage-level "Mirror and weld" under Symmetry.
- **Freeform has no Split Surface or Split Body equivalent, deliberately**: splitting a Catmull-Clark form's baked shape would sever the cage↔surface link and leave a dumb surface. The path is Convert to NURBS, then split — the same reason Combine stays off Freeform.

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
