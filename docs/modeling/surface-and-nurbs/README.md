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
- **Curves:** one dropdown with every curve tool that works outside a sketch (see [Curves dropdown](#curves-dropdown))
- **Create:** Extrude, Revolve, Sweep (dropdown with Pipe), Loft, Boundary Surface
- **Modify:** Trim, Split Surface, More (Fillet / Chamfer, Sew, Offset, Thicken, Unsew, Untrim, Extend, Reverse Normal, Enclosed Body, Flatten / Refold, Warp)
- **Convert:** Convert to NURBS
- **Combine:** Union, Subtract, Intersect (SFC-H006)
- **Transform, Inspect, Appearance:** shared sections (Transform includes Mirror — it accepts any object, so surfaces mirror directly; SFC-H009)

## NURBS

- **Select, Datum:** shared sections
- **References:** Create Sketch (K), Construction Plane
- **Curves:** same dropdown as Surface
- **Surfaces:** Control Grid, Extrude, Revolve, Sweep (dropdown with Pipe), Loft, Boundary Surface
- **Shape:** Edit Points, Sculpt, Rebuild Surface, Warp (direct buttons)
- **Detail:** Add Detail, Raise Degree
- **Continuity:** Match Surface, Check Seam
- **Finish:** Sew, Unsew, Thicken, Trim, Split Surface, Fillet / Chamfer, Untrim, Extend, Reverse Normal, Offset, Enclosed Body
- **Combine:** Union, Subtract, Intersect — the same BooleanCommand as Solid and Home; it accepts solids and surfaces (SFC-H006)
- **Transform, Inspect, Appearance:** shared sections (Transform includes Mirror — surfaces mirror directly; SFC-H009)

## Curves dropdown

Owner direction 2026-11-13: the ribbons were busy and the curve tools are used less than the surface tools, so the Curves and Edit curves groups became **one Curves dropdown** on Home, Solid, Surface, NURBS and Freeform (CRV-H034, SEL-H020). It is one level deep and holds Curve, Curve from Data, Intersection Curve, Project, Draw on Surface, Isocurves, Contour (the four that read from surfaces are left out on Solid and Freeform), then Blend, Join, Explode, Extract Edges, Trim, Extend, Offset, Rebuild and Points Along Curve.

- **Flat curves are edited in the sketch.** The Sketch tab already has its own Fillet, Chamfer, Offset, Trim and Extend, which work while a sketch is open and keep its constraints. The tools in the dropdown are for curves outside a sketch: 3D curves, curves drawn on a surface, and edges.
- **Fillet / Chamfer** sits with the surface tools on Surface (Modify) and NURBS (Finish), because its main job there is rounding the edges of a surface; it still takes flat curves. On Home, Solid and Freeform it is in the Curves dropdown.
- Layouts customized before this change keep working: the old per-tab menu names resolve to the same tools.

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
| Intersection Curve | Between planes, faces, surfaces or bodies, or two flat curves on different planes. Pick two curves and the tool recognises them (SOL-H108): each projects along its own plane and the shared 3D curve is made. The "Between" choice can force either kind. |

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
- **Not available yet:** a color per face. Appearance is stored per object, so differently colored regions need the Separate surfaces result. A proposal is in [Faces as first-class things](#faces-as-first-class-things-proposed-2026-11-13).
- **Mirror was already on Surface, NURBS, Solid and Freeform** via the shared Transform group (SFC-H009); `TransformCommand` accepts any object kind, so surfaces mirror without conversion. Freeform additionally keeps its cage-level "Mirror and weld" under Symmetry.
- **Freeform has no Split Surface or Split Body equivalent, deliberately**: splitting a Catmull-Clark form's baked shape would sever the cage↔surface link and leave a dumb surface. The path is Convert to NURBS, then split — the same reason Combine stays off Freeform.

## Faces as first-class things (proposed 2026-11-13)

**Owner decisions 2026-11-13:** first delivery is the face-identity foundation plus items 1 to 3 (color per face, Select Same, Hide / Show faces); solids as well as surfaces; hide is a saved edit; a face carries the full finish; settings with no successor face are dropped with a message; analysis loads move onto the same face identity (the owner places loads by splitting out the face the force acts on, so they must survive edits). **Items 4 to 10 remain proposed — awaiting owner review.** Split Surface made faces real (select, Isolate, Delete). This proposes what else a user expects to do with a face. The same rules apply to faces of **solids** and **surfaces**, so the feature is not limited to split NURBS surfaces (recommendation; see open decision 2).

### Foundation: a face keeps its identity
Faces are referred to today by number only (`FaceRef(Obj, Face)` for analysis loads, Isolate and selection), and numbers change when the shape is edited. Face color and other face data would be wrong after a Split, Trim, Thicken or fillet unless faces can be followed through an edit.

- **FACE-P1 (proposed):** data attached to a face (color, hidden state, name) follows that face through edits where the kernel can say which new face came from it. When a face is split, every piece inherits its data. When faces merge, the data of the largest piece wins. When a face has no successor it is dropped, and the status line says how many face settings were lost.
- Native `.prism` save and reopen keep the data. Files without it open unchanged.

### Proposed abilities, in priority order
1. **Color per face (Appearance page).** With faces selected, the Appearance page edits those faces. Each face can carry an override of the object's look: base color first, optionally the whole finish (roughness, metal, pattern). The object's material and engineering data stay per object. **Reset** on the page removes the face override. Undo is one step per gesture. The viewport and the CPU render both show it.
2. **Select Same:** Same color, Same surface type, Adjacent faces, Tangent chain, and Select Linked (all faces of the object). Invert and Isolate already work at the Face level.
3. **Hide / Show faces** as an edit kept in the document (Isolate is only a view). Hidden faces are not drawn, picked, exported or rendered. Show All in the face list restores them. Open decision 3.
4. **Extract / Detach faces** into their own surface object (the reverse of the faces result of Split Surface), and **Merge faces** back into one face where they lie on the same surface. This answers "I split, now I want separate objects" without redoing the split.
5. **Face properties:** area, surface type, normal at the pick and boundary length shown in the status bar for one face, total area for several. Measure accepts a face.
6. **Reverse normal for selected faces** of a surface.
7. **Surface faces in the direct tools:** Offset Face, Move Face and Thicken on selected surface faces (the gap already noted in the [selection review](../../interface/selection/README.md#selection-levels-on-surfaces-review-2026-11-12)).
8. **Right-click menu for a surface face** lists only what applies (Color, Isolate, Delete, Extract, Select Same), replacing Push / pull, Shell and Hole.
9. **Names for faces** (for example "Mounting face") shown in the browser tree and usable by Simulation loads and CAM.
10. **Export:** face colors in glTF, 3MF and STEP color attributes, and in the CPU render. Exports that cannot carry face color (STL) ignore it and say so.

### Decisions (owner approved the recommendations 2026-11-14)
1. **Scope of the first delivery.** Recommend items 1, 2 and 3 plus the identity foundation; the rest as later stories.
2. **Solids too?** Recommend yes: the mechanism is identical and users will try it on a box first.
3. **Hide faces:** a document edit (recommended, matches CAD users' expectation) or only the view-state Isolate we have.
4. **Color only, or the full finish per face?** Recommend the full look (it is already one `SurfaceStyle`), with the Appearance page showing "Face override" when faces are selected.
5. **Identity limits:** accept "settings dropped with a message" when the kernel cannot map a face through an edit, rather than blocking the edit.
6. **Existing analysis loads** use bare face numbers and share the same weakness. Move them onto the same identity in this work, or leave them for a bug story.

### Delivered 2026-11-13 (owner-approved scope: foundation and items 1 to 3)
- **Face identity (FACE-P1).** Face data is stored per face number on the object and carried through every shape edit by geometry: a new face takes the data of the old face it lies on (within 0.4% of the model size, facing the same way). A split gives each piece the data; a face with nothing under it has none. Settings with no successor are dropped and the status line says how many. A move or other change that leaves every face area equal keeps the faces in order. No kernel history is used, so a fillet or offset that merges or reshapes faces carries data by where the face now lies, which can occasionally differ from what a kernel history would say.
- **Color and full finish per face.** With faces selected (and their body not selected as a whole) the Appearance page edits those faces: color, finish preset, metal, gloss, pattern and every layer. The page says "Editing N faces". What you change becomes the face's own value; what you leave alone keeps following the body. A body selected as a whole is still colored as a whole and leaves face settings alone. **Reset** on the page returns the selected faces to the body. Applying a library material to faces gives them its look only; the engineering material stays on the body. Solids and surfaces; Freeform and meshes are not covered.
- **Select Same** (Select group, split button): Same Color (same effective color and finish), Adjacent (shares an edge) and Linked (the whole body), added to the current face selection.
- **Hide faces is a saved edit.** **Hide** with faces selected hides just those faces; they are not drawn, hovered or picked, their edges not shared with a shown face disappear, and a click passes through. **Show All** brings them back. Hidden state is saved with the document and survives edits like any other face data. Isolate keeps working as a view only and combines with hidden faces.
- **Loads and fixtures follow their faces.** A fixture or load on a face is moved to the faces that replace it after an edit (a split gives every piece the load). A study that matched its body before the edit still matches afterwards. Faces with no successor are removed from the fixture or load with a status message. This replaces the earlier behavior where the study went stale after any edit. Not covered: stored face sets and saved CAE cases still use bare face numbers and signatures.
- **Not yet:** items 4 to 10 above; Same surface type and Tangent chain in Select Same (they need the kernel's surface types); face color in glTF, 3MF and STEP exports (the viewport and the CPU path-traced render show it, and hidden faces are left out of the render); duplicating a body does not copy face settings. Tests: [FaceStyleTests](../../../Prism.Tests/Headless/Faces/FaceStyleTests.cs) (`FACE-H001` to `FACE-H009`).

**Verification 2026-11-13 (Windows workstation, Debug build, installed native libraries not rebuilt):** `Feature=Faces` Headless 9 of 9 (`FACE-H001` to `FACE-H009`); full Headless 937 of 941, the four failures being the ones that predate this work (HLP-H001, HLP-H003, SEC-H001, SOL-H089); Unit 203 of 203. **Not verified:** the viewport drawing of per-face color and the path-traced render of it have not been looked at in the running desktop app, and no viewport image is attached to an issue yet; no GitHub issue has been opened for this delivery; the work is not committed.

### Acceptance criteria (draft)
- Selecting two faces of a split surface and choosing a color changes only those two faces on screen and in a render; the rest keep the object color.
- Splitting a colored face again gives both pieces the color. Deleting a neighbouring face does not move the color to another face.
- Save, close and reopen keeps face colors. Undo and Redo restore them.
- Reset on the Appearance page returns the selected faces to the object look.
- Select Same Color picks exactly the faces that share the override.

No test IDs are assigned until the owner approves the scope.

## 0.4 modeling gaps (approved by the owner 2026-11-14)

**Status: approved by the owner on 2026-11-14, with the recommended answer to each open decision below; not yet implemented or tested.** Wrap (IMP-P2) and solid Thicken (DRS-P4) are deferred past 0.4. Test IDs are assigned as each item is built. Owner direction 2026-11-14 asked what Prism still lacks next to Creo and Plasticity, and set the headline for 0.4: **surface blending to G2, not G3**. Findings come from reading the ribbon and command classes; the app was not run, so every "today" line is to be re-checked before code. Rules for all items: prefer a mode or dropdown entry over a new top-level ribbon button; keep each workflow different (a tool is added to another tab only where it fits, and unsure placements are listed under Open decisions); no test IDs are assigned until the owner approves.

### 1. Surface blending and fillets (lead item)
**Today.** Boundary Surface already blends two separate surface edges and gives each edge G0/G1/G2 and a weight. Edge Surface, Blend Surface and Network Surface exist only as hidden commands (`EdgeSurfaceCommand`, `BlendSurfaceCommand`, `NetworkSurfaceCommand`), reachable from scripts. Match Surface works only on natural patch boundaries of compatible, untrimmed, non-rational rectangular patches. Fillet / Chamfer on surfaces needs the surfaces sewn first and has no setback.

- **BLD-P1 Boundary Surface modes.** Boundary Surface gets a **Mode** choice: *Boundary* (today's behavior, the default), *Blend* (bridge two separate edges with per-end continuity and fullness), *Edge* (two to four curves or edges span a surface) and *Network* (a grid of crossing curves). Each mode shows only the picks and options it needs. The hidden commands keep working from scripts and saved files and are marked `[Legacy]`. No new ribbon buttons.
- **BLD-P1 delivered 2026-11-14** ([BND-H006 to BND-H010](../../../Prism.Tests/Headless/Surfacing/BoundaryModeTests.cs), Surfacing slice 129 of 129 passed on Windows; not tried by hand in the desktop app). Finding while building it: Boundary Surface already did Edge, Blend and Network work by guessing from the picks (two to four free curves, two separate surface edges, an outline with curves across and "Fit through points"), so the mode choice mostly makes that explicit. **Mode** defaults to *Automatic*, which behaves exactly as before. The other modes force one reading and refuse picks that do not fit with a message that says what is needed: Edge (two to four free curves), Blend (one open edge on each of two surfaces; Fullness shows), Network (outline plus Guide curves, fitted through points). Steps saved before Mode existed reopen in Automatic. `EdgeSurfaceCommand`, `BlendSurfaceCommand` and `NetworkSurfaceCommand` are marked `[Legacy]` and still work from scripts and saved files.
- **BLD-P2 Match Surface beyond natural boundaries.** Match Surface accepts an edge of a trimmed face or a face of a solid as the reference, and a trimmed source where the kernel can match it; when it cannot, it says which condition failed instead of producing a fold. Continuity stays G0, G1, G2 (**no G3**). The result report keeps the sampled gap, normal angle and curvature difference.
- **BLD-P3 Fillet between separate surfaces.** Fillet / Chamfer accepts one edge from each of two unsewn surfaces that meet or nearly meet, and rounds between them. Today's refusal ("sew first") remains only when the surfaces do not come within the tolerance.
- **BLD-P3 delivered 2026-11-14** ([SRF-H033 to SRF-H035](../../../Prism.Tests/Headless/Surfacing/FilletAcrossSurfacesTests.cs), passed on Windows; not tried by hand). Fillet / Chamfer takes one edge from each of two separate surfaces that touch: the surfaces are joined where they meet (within 0.05 mm), the shared edge is rounded or cut, and they become one surface. Two perpendicular 10 mm sheets rounded at 2 mm measure exactly 80 + 80 + the quarter-cylinder area. Edges that do not touch are refused with a message. Surfaces only; mixing in a solid is refused.
- **BLD-P2 not built (2026-11-14).** Match Surface still matches natural patch boundaries only. Matching to an arbitrary edge of a trimmed face or solid needs a new surface-fitting step in the kernel (the existing one edits the control rows next to a natural boundary), which is a research-sized change rather than an extension. It needs your decision on scope before work starts; see the final report for options.
- **BLD-P4 Setback and variable radius.** Fillet gets per-edge radius (start and end values per picked edge), and a **Setback** distance at corners where three or more filleted edges meet. Conic or full-round blends are out of scope unless the kernel supports them cheaply.
- **BLD-P4 per-edge radius delivered 2026-11-14** ([DRS-H003, DRS-H004](../../../Prism.Tests/Headless/Solid/DressUpDepthTests.cs)). Fillet has an optional **Radius per edge** list (`3, 5-8, 2`: one entry per picked edge in pick order, a range grows along the edge, an edge with no entry uses Radius). It is a typed list rather than one panel row per edge. **Setback is not built**: Open Cascade's fillet builder has no setback control, so a vertex setback would need a different approach and is left for the owner to decide.
- **Acceptance (draft).** Blend between two parallel offset sheets at G2 reports gap ≤ 0.01 mm and curvature difference ≤ 0.01 /mm at both seams. Edge mode on four straight edges of a square gives a flat surface. A fillet between two unsewn perpendicular sheets of radius 5 mm leaves a quarter-cylinder 5 mm in radius. Match Surface at G2 never reports a larger gap than G1 on the same input. Each retired hidden command still opens from `CommandCatalog` by its old key.

### 2. Analysis on the modeling tabs
**Today.** Zebra, Curvature Map, Draft Analysis and Check Seam live on the Analyze tab (Check Seam is on NURBS only).
- **ANA-P1.** Surface and NURBS each get one **Analyze** dropdown (one group slot) holding Zebra, Curvature Map, Draft Analysis, Curvature Graph and Check Seam. The commands are the same ones the Analyze tab runs; nothing is copied. The Analyze tab is unchanged.
- **Acceptance.** Each dropdown entry starts the same command class as the Analyze tab; the Surface and NURBS ribbons gain one group, not five buttons.
- **Delivered 2026-11-14 ([SFC-H018](../../../Prism.Tests/Headless/Surfacing/AnalyzeDropdownTests.cs), passed on Windows).** The dropdown holds Zebra Stripes, Curvature Map, Draft Analysis and Check Seam. Check Seam moved into it from the NURBS Continuity group (which keeps Match Surface). Curvature Graph is not in the dropdown: it is a curve tool, not an analysis command on any ribbon. Not tried by hand in the desktop app.

### 3. Merge Surface
- **MRG-P1.** Sew gets a **Result** choice: *Stitch* (today: shared edges, closed shells become solids) and *Merge into one patch* (adjacent faces on the same underlying surface, or tangent-continuous natural patches, become one face). If the kernel cannot merge a pair, the faces stay stitched and the status line names them. Faces are not merged across a crease.
- **Acceptance.** Two coplanar halves of a split plane merge into one face (face count 2 → 1, area unchanged). Two faces meeting at a 90° crease are never merged. Stitch result is unchanged from today.
- **MRG-P1 delivered 2026-11-14** ([SRF-H032](../../../Prism.Tests/Headless/Surfacing/SewMergeTests.cs), Surfacing slice 130 of 130 passed on Windows; not tried by hand). Sew has a **Result** choice: *Stitch* (default, as before) or *Merge into one patch*, which uses the kernel's same-surface merge after sewing. If the kernel cannot merge, the faces stay stitched and the panel says so.

### 4. Solid dress-up depth
**Today.** Fillet takes a start and end radius per call; Chamfer one distance; Shell one thickness; Draft one neutral plane and one angle; Thicken is surface-only.
- **DRS-P1.** Chamfer gains *Two distances* and *Distance and angle*.
- **DRS-P2.** Shell gains a per-face thickness list (faces default to the overall thickness).
- **DRS-P3.** Draft gains **Parting line**: faces above and below a chosen plane or edge loop lean in opposite directions.
- **DRS-P4.** Thicken accepts a face selection on a solid to add or remove material from those faces (solid Thicken). Open decision: this overlaps Offset Face, which stays gizmo-only; recommend not adding it unless the owner finds a case Offset Face cannot do.
- Fillet radius and setback are BLD-P4, which also applies to solids.
- **Acceptance.** A 10 mm box chamfered 2 mm × 4 mm has those two leg lengths. A shell with one face at 3 mm and the rest at 1 mm measures that. A part drafted at a mid-plane parting line with 2° has the upper and lower walls 2° off vertical in opposite directions.
- **DRS-P1 and DRS-P3 delivered 2026-11-14** ([DRS-H001 to DRS-H006](../../../Prism.Tests/Headless/Solid/DressUpDepthTests.cs), Solid slice 141 of 141 passed on Windows; not tried by hand). Chamfer has **Style**: Equal distance (as before), Two distances and Distance and angle, with *Swap the two sides*. Draft has **Parting line**: with *At the neutral plane*, walls above the plane and walls below it lean opposite ways so both ends narrow away from it (a positive angle narrows the part); a wall that lies on the plane is refused with a message to split it first. Walls are assigned by where their centre lies, so a wall that crosses the plane must be split there first. Shell with a thickness per face (DRS-P2) is **not built**: the kernel's shell call takes one thickness, and a per-face version needs the lower-level offset builder, which has not been tried.

### 5. Imprint and Wrap
**Today.** Draw on Surface and Project lay curves on a surface; Draw on Surface can split the face along the curve; Split Face divides a solid face with a plane, edge or sketch. Nothing marks a face with a curve without cutting it apart.
- **IMP-P1 Imprint.** An **Imprint** mode of Split Face (not a new button): the picked curves become edges on the face, dividing it into faces that stay on the same surface, so each can be recolored, hidden or deleted. It works on solids and surfaces and keeps the body's shape. Curves that do not fully cross the face still imprint (the open part ends in the face), which is the difference from Split.
- **IMP-P2 Wrap (larger, later).** Wrap a flat sketch or curve onto a curved face by distance, as Creo does. Proposed only; not planned for 0.4 until the owner agrees.
- **Acceptance.** A circle imprinted on a flat face adds one face (the disc) and one inner loop on the remaining face; volume and outer shape are unchanged. A line ending in the middle of the face adds an edge but no new face.
- **IMP-P1 delivered 2026-11-14** ([SOL-H150, SOL-H151](../../../Prism.Tests/Headless/Solid/ImprintTests.cs), passed on Windows; not tried by hand). Split Face has a **Mode** choice: *Split* (as before) or *Imprint*. A circle imprinted on a box top adds one disc face and leaves the volume unchanged; a line that stops inside the face leaves the body valid with no new face. The curves are projected onto the face the way Split already does. Imprint on surfaces (not only solids) and Wrap are not built.

### 6. "Keep the originals" consistency
**Today.** Combine already has *Keep tool bodies* (default off, `SolidCommands.cs`). Many curve tools have *Keep the original(s)* (Join, Explode, Trim, Extend, Rebuild, Project …) with differing wording and defaults. A first search found none on **Intersection Curve**, Sew/Unsew, Thicken, Offset/Extend/Trim/Split Surface, Pipe, Shell, Split Body, Loft and Sweep; that is a first search only and each class must be read before deciding.
- **KEP-P1.** Tools that consume curves or bodies to make a new one offer one consistent option, **Keep the originals**, in the same place in the panel with one default per tool family. Recommended defaults: *off* where the result replaces the input (Join, Sew, Boolean, Intersection Curve), *on* where the input is a reference (Project, Offset, Thicken, Loft, Sweep profiles). Intersection Curve is the owner's example: two curves become one, and the user may not want the two sources afterward.
- **KEP-P2.** The audit lists every command that takes selections and records for each: has the option, should have it, or should not (for example Extrude of a sketch). Only the "should have it" tools are changed.
- **Acceptance.** For each changed tool: with the option off the sources are gone and undo restores them; with it on they are untouched; the saved history re-edits with the same setting.

- **KEP-P1/P2 first delivery 2026-11-14** ([CRV-H038 to CRV-H040](../../../Prism.Tests/Headless/Curves/KeepOriginalsTests.cs), Curves and Surfacing slices 182 of 182 passed on Windows; not tried by hand). Audit by reading what each command does with its inputs: **Intersection Curve** (two flat curves) now replaces them unless *Keep the original curves* is on (planes, faces and bodies are references and never removed; steps saved before keep their curves). **Sew** and **Thicken** gained *Keep the originals* / *Keep the original surface*, off by default, which is what they did before. Tools that only add a result (Offset, Project, Pipe, Extrude, Revolve, Isocurves) never consume inputs and need no option. Join, Explode, Trim, Extend, Rebuild, Combine, Boundary Surface and Draw on Surface already had one. **Still open:** Shell and Split Body replace the body in place; a kept copy would sit exactly on top of the result, so they are left alone unless you want it. Loft, Sweep and Unsew show a keep-style option already.

### 7. Sweep modes: variable section and helix
**Today.** Sweep takes a path and an optional second rail that steers rotation only. Helices come from Curve from Data (an equation), which stays.
- **SWP-P1 Variable section.** Sweep gets a **Section** choice: *Constant* (today), *Scaled* (start and end scale along the path), *Follow guide curves* (the section is stretched to reach one or two guide rails), and *Law* (scale and twist given by a numeric table or curve). Result is a surface or a solid as today.
- **SWP-P2 Helix.** An interactive **Helix** path for Sweep (and as a curve source): axis (pick or datum), radius, pitch, turns and handedness with on-canvas handles; taper optional. A **Helical sweep** is Sweep with this path, so threads and springs need no formula.
- **Acceptance.** A circle swept *Scaled* 1 → 0.5 along a 100 mm line has end diameter half the start. A helix of pitch 10 mm and 5 turns is 50 mm long along its axis and winds the chosen way. A section swept along two guide rails touches both rails at every sampled station within 0.05 mm.
- **SWP-P1 partly delivered, SWP-P2 delivered 2026-11-14** (Sweep: [SWP-H010 to SWP-H012](../../../Prism.Tests/Headless/Sweep/SweepScaleLawTests.cs); Helix: [CRV-H041, CRV-H042](../../../Prism.Tests/Headless/Curves/HelixTests.cs); Sweep and Curves slices passed on Windows; not tried by hand). Finding: Sweep already scaled its profile linearly to an *end scale*, so *Scaled* existed. It now also has **Scale at the start** and an optional **Scale along the path** list (`0:1, 0.5:1.6, 1:0.7`: position 0 to 1, then scale, joined smoothly), checked against the analytic frustum volume. **Helix** is a third *Points from* choice of Curve from Data (no new button): radius, optional end radius (taper), pitch, turns, handedness and axis, with arrows in the viewport for radius and pitch; it is a curve, so it is the path of a helical sweep. **Not built:** *Follow guide curves* (stretching the section to reach one or two rails) and a twist law; the second rail still steers rotation only. Both need a different sweep builder in the kernel.

### Decisions (owner approved the recommendations 2026-11-14)
1. BLD-P2: accept "match fails with a named reason" when the kernel cannot match a trimmed edge, instead of blocking the tool?
2. BLD-P4 and DRS: confirm conic and full-round blends are out of scope for 0.4.
3. MRG-P1: Merge as a Sew result (recommended) or a separate Merge Surface button?
4. DRS-P4: drop solid Thicken because Offset Face covers it?
5. IMP-P2 Wrap: defer past 0.4?
6. KEP-P1: confirm the recommended defaults, and that the option is added only to tools the audit marks "should have it".
7. Placement beyond the first home: Analyze dropdown (Surface, NURBS), Merge and Imprint (Surface and NURBS first; Solid for faces of solids), Sweep modes (Solid, Surface, NURBS), Keep the originals (everywhere the command already appears). Not certain: whether Imprint and Merge also belong on **Freeform** (which deliberately omits Split and Combine); recommend no.

## Feature requirement reviews

- [Surface Extrude](../extrude/README.md#surface-and-nurbs-extrude) — draft owner-review specification, linked to [Story #7](https://github.com/chrizpro/prism/issues/7). This is not a claim of approved behavior or passing tests.

## Maintained option coverage

The public [Surfaces and NURBS walkthrough](../../../site/guide/surfacing.md) includes actual app captures for ordered Loft profiles, Thicken, NURBS Control Grid, selected control-point editing and zebra inspection. The separate Surface and NURBS ribbon pages use their own workflow captures. These are documentation examples; [capture provenance and verification](../../../site/README.md#guide-images) are separate from the maintained tests below. Proposed feature requirements remain proposed.

[Surface tool tests](../../../Prism.Tests/Headless/Surfacing/SurfaceToolTests.cs) cover Edge Surface (two and four curves), Blend Surface at each continuity and fullness, Sweep 2 Rails as surface and solid, Rebuild Surface, Network Surface, Planar Surface with a hole, Knit with and without solid and its tolerance, Trim Surface with a solid or a surface on either side, Enclosed Body with listed spaces, Unsew of whole bodies and single faces, both Untrim modes, Extend and Reverse Normal. [Inspect tool tests](../../../Prism.Tests/Headless/Solid/AnalysisToolTests.cs) cover Surface Continuity and Match Surface at G0, G1 and G2.

Fixed on 2026-11-13 (owner report: "I can't split the face of the cube, because I can't select a face"): Split Surface's first pick takes a face as well as a whole surface. At the Face pick level, picking a face splits only that face and keeps every other face of the surface as it was, sewn back into one surface; at the Object level the whole surface is split as before (`SRF-H031`).

Fixed on 2026-11-13 (owner reports): Rebuild Surface draws the grid of points it fits through and redraws it as Points in U and V change (`SRF-H029`). Edit Control Points is always in selection mode: a drag in empty space selects the points inside a rectangle without first choosing a method on the ribbon, and the Rectangle or Lasso chosen there is used when one is armed (`SRF-H030`).

Fixed on 2026-11-05: Rebuild Surface's "Largest deviation (approx.)" used to measure distance to the result's tessellation vertices, so an exactly reproduced flat sheet reported several millimetres and a warning. It now measures the distance from points sampled on the original to the rebuilt surface, and the test asserts 0 mm for the flat sheet.

[Surface and NURBS headless tests](../../../Prism.Tests/Headless/Surfacing/SurfaceOptionTests.cs) launch the tools from both ribbons. They check signed planar Offset and Thicken, single and multiple Isocurves in both directions, and NURBS Control Grid plane, dimensions, grid counts, degree and offset on flat examples.

Run `./tools/test.ps1 -Layer Headless -Feature Surfacing`. [Pipe](../pipe/README.md) has separate exact and cage checks. [Boundary Surface, Ruled and Patch](../boundary-surface/README.md) have separate option checks. Trimming and sewing options, curve editing, NURBS control-point editing and continuity, and curved or invalid inputs remain coverage work. [Surface #48](https://github.com/chrizpro/prism/issues/48) and [NURBS #47](https://github.com/chrizpro/prism/issues/47) track it.


Desktop integration: [SFC-I001 viewport evidence](../../integration-evidence/README.md#sfc-i001--control-grid-and-thicken) verifies MCP Control Grid, Thicken and document reopen.

Windows verification on 2026-11-05: the combined candidate passed 86 Unit and 592 Headless tests with rebuilt native libraries. See the [integration verification report](../../engineering/release-readiness-2026-11-05.md) for source revisions, commands and limits.
The [SFC-I001 test](../../../Prism.Tests/Integration/DesktopModelingTests.cs) also checks the measured distance between a face and a parallel offset surface through MCP.
