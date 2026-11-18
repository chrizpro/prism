# Freeform tab

The public [Freeform walkthrough](../../../site/guide/freeform.md) includes actual app captures of the control cage, vertex selection with soft influence and the quality markers. [Image provenance and documentation verification](../../../site/README.md#guide-images) record their source and limits. These are workflow illustrations, not additional feature-test executions.

The most common tools have their own buttons. The next most common sit in a dropdown or a small group. **Edit Freeform** holds the rest, so nothing appears in both places.

Living Revolve requirements are maintained in the [Freeform Revolve section](../revolve/README.md#freeform-revolve); this cage command is separate from exact Solid, Surface and NURBS Revolve.

Living [Sweep](../sweep/README.md#freeform-sweep) and [Loft](../loft/README.md#freeform-loft) requirements also distinguish cage creation from exact Solid/Surface/NURBS commands.

## Ribbon

- **Select, Datum, References, Curves:** shared sections
- **Create:** Plane / Box / Sphere / Cylinder / Torus, Create Face, Loft / Sweep / Pipe / Extrude Profile / Revolve
- **Modify:** Edit Freeform, Extrude, Slide, Subdivide, More (Bevel, Delete, Split face, Smooth, Flatten, Fill holes, Thicken, Warp)
- **Edit curves:** Join, Trim, Extend, Rebuild, Blend
- **Loops:** Knife, Insert Edge, Inset, Select Loop, Bridge
- **Lock and align:** Freeze, Align to Curve
- **Topology:** Smooth surface, Weld, Crease, Merge, Unweld edges, Pull, Flip normals
- **Symmetry:** Mirror and weld
- **Pattern:** Linear, Circular, Along Curve, Fill, Table (a freeform is copied as a new freeform)
- **Analyze:** Zebra Stripes, Curvature Map, Draft Analysis
- **Quality:** Check Quality
- **Finish:** Convert to NURBS, Convert fewer faces
- **Transform, Inspect, Appearance:** shared sections

## One tool, several jobs

Each of these reads the selection and does what fits it.

| Tool | What it does |
|---|---|
| Merge | Faces join into one. Edges are removed, joining the faces either side. |
| Weld | Points weld into one. Edges and faces collapse to points. |
| Freeze | Locks the selection. On a frozen selection it unlocks it. With nothing selected it unlocks everything. |
| Bevel | Flat cuts the corner off. Tight adds support loops either side. |
| Smooth | Relaxes points toward their neighbours. With Keep moved points on the scan on, it keeps them on the scan. |
| Crease | Sharpens edges or points. Zero removes it. |

## Edit Freeform panel

The panel keeps what the ribbon has no room for, and only shows what fits the selection mode:

- **Selection:** the mode, position, Grow, Shrink, and Select by (angle, open edges, creased edges) with Select matching
- **Soft selection and Live symmetry**, with Place symmetry when symmetry is on
- **Amounts for ribbon tools:** extrude distance, bevel type and width, slide amount, inset, loop count (while Insert Edge is on)
- **Smoothness and display:** smoothness level, mark poles, show triangles
- **Scan tools**, when the document has a scan

Invert and Select All come from **Invert** in the Select section, and undo is Ctrl+Z.

## Cage edit checks

- **FRM-P0 checked 2026-11-14 (owner request).** The cage edit operations are plain geometry, so [Freeform edit tests](../../../Prism.Tests/Unit/Freeform/FormEditTests.cs) (FRM-U010 to FRM-U033, 22 cases) check them against known answers without the native kernel: every result is a closed manifold, and the volume, face and point counts and Euler number match. They cover Extrude (one face and a 2 x 2 region), Insert Edge, Subdivide, Inset, Bridge, Mirror and weld, Merge, Weld (collapse), Split face, Delete and Fill holes, Flip normals, Bevel (chamfer), Solidify, Flatten, Smooth, a Catmull-Clark step and crease. [Conversion tests](../../../Prism.Tests/Headless/Freeform/FormConvertTests.cs) (FRM-H120 to FRM-H123) check Convert to NURBS: a fully creased 30 x 20 x 10 box comes out as a valid 6000 mm³ solid within 1%, a smooth box is rounded and stays inside its cage, a quad-ball is a valid solid just inside its sphere, and fewer faces gives no more faces than exact with a similar volume.
- **Findings, for owner review (behavior is characterized as it is, not changed):**
  1. **Inset *Amount* is measured along each corner's diagonal**, so 0.5 leaves a ring 0.354 mm wide. Most tools measure the ring's width across.
  2. **Bevel (flat) *Width* is how far each new point moves toward the middle of its faces**, not the length of the cut's legs: 0.5 on a cube's top edges removes about 2 mm³, where 0.5 mm legs would remove 1.
  3. **A single isolated edge cannot be bevelled** (it returns nothing): a flat bevel needs edges that meet end to end or run through the form.
  4. **Crease sharpness 1 holds for one smoothing level.** The default is 1 and the display uses 3 levels, so a default crease rounds off after the first level; a cube creased at 1 and smoothed three times has volume about 6.4 instead of 8, and at 3 it stays exactly 8. The panel's help says larger values hold more levels, but the ribbon button says "Make the selected edges sharp".
  Proposed: measure Inset and Bevel across, allow a single-edge bevel, and make the default crease hold for every display level (or offer *Sharp* as a setting beyond 5). Not applied until you decide.

## Knife and patterns

- **FRM-P1 delivered 2026-11-14 (owner request, then reworked to be like Blender's knife).** **Knife** (Loops group) cuts a freeform along a line you draw across it. Click the form, then click points across it: the cut follows the line you see on screen, so it runs through every face it crosses, whatever their angle. Each stretch between two clicks is a cutting sheet through the eye and both points (or through both points along the view, in a parallel view), so only what lies between the points is cut. Each crossed edge gets a new point, each face the line crosses is split in two along it, a bend in the line becomes a point inside the face it falls in, and a line that passes through existing points splits the face between them.
  - **Snap to points and edge midpoints** (on): a click within 10 pixels of a point or an edge midpoint of the cage lands on it, so a cut can run exactly through them.
  - **Cut through** (off): by default only faces turned toward you are cut, as in Blender; on, the faces turned away are cut too.
  - **Run the cut on to the edges at both ends** (on): the first and last stretch carry on to the edges, so a line starting and ending inside faces still splits them. Off, a face the line ends inside is left alone, and the panel says how many; the cage cannot hold a cut that stops half way.
  - **Constrain the angle (45° steps)** (off): each new point moves around the last one to the nearest 45° direction on screen.
  - **Backspace or undo** takes the last point back; **OK** makes the cut; **Crease the cut** sharpens the new edges. The panel's *Points of the cut* box holds the clicked points (where you clicked, then where the camera was), so a saved knife step reproduces the same cut; it is advanced and can be cleared to start again.
  - **A plane** (Cut with): the earlier way, along a construction plane, a flat face or a world plane at a typed position. A plane that misses the form is refused with a message.
- **Patterns** accept freeforms: Linear, Circular, Along Curve, Fill and Table make new freeforms moved or turned like a body would be. Joining a freeform into, or cutting it from, another body is refused with a message; convert the form to NURBS first.
- **Warp** and the **Analyze** dropdown (Zebra Stripes, Curvature Map, Draft Analysis) are on the Freeform tab; they already accepted forms.
- **Not built:** a Knife that hides faces behind other faces (it cuts every face turned toward you, even one hidden by a nearer part of the form), a Knife limited to selected faces, a cut that stops inside a face, and booleans (by design, since Catmull-Clark does not suit them).
- **Acceptance.** A plane through the middle of a cube cuts its four walls: 4 new points, 4 more faces, volume unchanged, still closed. A plane through two opposite vertical edges splits the top and bottom along their diagonals without adding points. A slanted plane through a quad-ball keeps it closed with every new point on the plane. Three copies of a form 50 mm apart start 0, 50 and 100 mm along. A line drawn across the front of a cube cuts the front face, with a point on each side edge, in a parallel and in a perspective view; with Cut through it cuts all four walls; a line ending inside a face does nothing unless the ends run on; a bent line leaves one point inside the face; a line through two corners splits the face along its diagonal without new points. Tests: [FRM-U030 to FRM-U040](../../../Prism.Tests/Unit/Freeform/FormEditTests.cs) (29 cases with the edit checks) and [FRM-H110 to FRM-H119](../../../Prism.Tests/Headless/Freeform/FormKnifeAndPatternTests.cs). Not tried by hand: the viewport clicking, snapping and the overlay need a look in the app.

- **The cage is shown while Knife is open (2026-11-14, owner report: it could not be used because the cage did not appear).** Before a form is picked every form's cage is drawn, and afterwards the picked form's, with its points, because clicks snap to them. Other Freeform tools that pick on the cage draw it themselves; Knife was the one that did not ([FRM-H124](../../../Prism.Tests/Headless/Freeform/KnifeCageTests.cs)). Not tried by hand.

## Feature requirement reviews

- [Extrude Profile](../extrude/README.md#freeform-extrusion) — draft owner-review specification, linked to [Story #8](https://github.com/chrizpro/prism/issues/8). Distinct from **Modify → Extrude** on a selected cage; that operation needs a separate behavior review.

## Maintained option coverage

[Freeform build tests](../../../Prism.Tests/Headless/Surfacing/FreeformBuildTests.cs) launch Extrude Profile and Revolve from Freeform. They verify cage counts and closure, direction or axis, taper or angle, and smoothness on simple profiles. [Cage unit tests](../../../Prism.Tests/Unit/Freeform/CageTopologyTests.cs) check grid segmentation and cylinder caps without the native kernel.

Run `./tools/test.ps1 -Feature Freeform`. [Pipe](../pipe/README.md) has its own cage checks. Open-profile extrusion, additional primitive controls, cage edits, topology tools, conversion and selection-dependent options remain coverage work. [Issue #46](https://github.com/chrizpro/prism/issues/46) tracks it.

[Freeform primitive tests](../../../Prism.Tests/Headless/Freeform/FormPrimitiveTests.cs) check Plane orientation and counts, Box size, faces and position, Sphere faces per side, Cylinder sides, rings and caps, Torus segments and the clamped tube radius, Check Cage on closed and open cages, and Freeform Quality with and without a reference.

[Bridge unit tests](../../../Prism.Tests/Unit/Freeform/CageBridgeTests.cs) verify open-edge and closed-loop topology and rejection of incompatible selections without modifying the cage. Ribbon selection, face-to-face bridging and additional cage configurations remain open.

Windows verification on 2026-11-05: the combined candidate passed 86 Unit and 592 Headless tests with rebuilt native libraries. See the [integration verification report](../../engineering/release-readiness-2026-11-05.md) for source revisions, commands and limits.
- **Knife fixes (owner report 2026-11-15, screenshots; FRM-H120, FRM-H121; tried live in the desktop app).** (1) *Running the cut on to the edges* carried the sheet across every face it passed beyond the ends of the line, so a line drawn across two cage faces split four. It now continues only inside the faces the drawn line reaches: a diagonal from one cage point to another splits exactly the faces it crosses (a live check on a 60 mm form cube gave 24 → 25 faces for one face's diagonal; the old code gave 26). It is a different setting from *Cut through*, which only adds the faces turned away; the option's hover help now says so. (2) *Picking points was hard:* only a click on the smooth surface counted, but the cage corners stand outside it. A click within 16 px of a cage point, 13 px of an edge midpoint or 7 px of an edge now takes that spot (points beat midpoints beat edges), and a white dot under the pointer shows what a click would take. Turn *Snap* off to click freely on the surface.


## Primitive smoothing, and Fill holes at the selection (owner request and report 2026-11-16; tests only, not tried by hand in the app)

**Primitive options.** Every Freeform primitive (Plane, Box, Sphere, Cylinder, Torus) already had its own segment counts (Box: Faces X, Y, Z; Plane: Faces across and along; Sphere: Faces per side; Cylinder: Sides and Rings; Torus: Segments around and of tube). They now also offer, after Position:
- **Smoothness (0 shows the plain cage), 0 to 4**, default 3 as before. It sets the form's display level at creation (it can still be changed later in Edit Freeform).
- **Sharp edges**, off by default. It sharpens the edges where faces meet at more than 70°: a box's 12 edges (24 short edges with the default 2 faces per side, 12 with one), a cylinder's two cap rims (16 with 8 sides). Gentle bends, like the sides of a round cylinder, and open edges are left smooth; on a plane or sphere it does nothing. So a box can be given more faces to shape with and still stay a crisp box when smoothed.
Checks: [FRM-H130 to FRM-H133](../../../Prism.Tests/Headless/Freeform/FormPrimitiveOptionTests.cs) and [FRM-U052](../../../Prism.Tests/Unit/Freeform/FormHoleTests.cs). Not changed: the segment counts, defaults and existing results. Whether other tools would benefit from the same options was not looked at.


**More segments (owner request 2026-11-16; tests only).** Beyond smoothing, the segment controls now go further, and the limits are higher. New controls come after the older ones, so saved Freeform steps still open as before:
- **Cylinder, Cap rings (0 to 30, default 0).** Each flat end can be divided into rings of faces before its middle face, so an end is no longer one large face with no points to shape. 0 keeps today's single face per end. It is hidden when Caps is off. Dividing the caps does not change the volume.
- **Sphere, Faces X, Y, Z (each 0 to 64, default 0 = the same as Faces per side).** The quad-ball can carry a different number of faces in each direction, for example more around the equator than over the poles. A sphere with counts nx, ny, nz has 2(nx ny + ny nz + nz nx) faces.
- **Higher limits:** Plane 100 faces each way (was 40), Box 64 per direction (20), Sphere faces per side 32 (12), Cylinder 96 sides (48) and 100 rings (30), Torus 96 around (48) and 64 around the tube (32).
Checks: [FRM-U060 to FRM-U062](../../../Prism.Tests/Unit/Freeform/FormSegmentTests.cs) (face counts worked out by hand, closed cage, Euler number 2, volume of the polygon prism) and [FRM-H137](../../../Prism.Tests/Headless/Freeform/FormPrimitiveOptionTests.cs). **Not built:** a separate count for the around and the over of a sphere other than through X, Y and Z, dividing a cone or torus caps, and a different split of a Box face than a straight grid.

**Fill holes (owner report).** It capped **every open border in the whole form**, wherever you had selected, so a hole made far away was filled by selecting something else and pressing it. Now a hole is filled only when its rim touches the selected points, edges or faces. A selection with no hole at it fills nothing and says "No hole touches what you selected (N holes elsewhere in the form were left alone)". With **nothing selected** it fills every open border, as before. Note that an open sheet's own outer edge counts as an open border, so on a plane with nothing selected the outline is capped too (as it always was). Checks: [FRM-U050, FRM-U051](../../../Prism.Tests/Unit/Freeform/FormHoleTests.cs) and [FRM-H134 to FRM-H136](../../../Prism.Tests/Headless/Freeform/FormPrimitiveOptionTests.cs); FRM-H134 and FRM-H135 fail on the old behavior (the far hole is capped).

## Agents and scripts (owner approval 2026-11-17; implemented, verified as listed)

How an AI agent or script should build Freeform shapes, and what it can read back. Full workflow for agents: [the `prism-freeform` skill](../../../.claude/skills/prism-freeform/SKILL.md) (also served by the `guide` tool and read by the in-app assistant). Diagnosis, decisions and the iron comparison: [Freeform for agents](../../freeform-studies/agent-tooling-proposal.md).

- **Describe, then measure.** `cad.form_skin(stations)` builds a Freeform from cross-sections along an axis; `cad.form_from_silhouettes(first, second, axis)` derives the stations from two outlines and rebuilds once, rescaling each station by target / measured size because the smooth surface pulls in from the cage. `cad.svg_outlines` / `cad.svg_to_sketch` turn an SVG into outlines or a sketch. These helpers are Python only (cadkit); app tools underneath are unchanged.
- **Numbers and text for a shape.** Automation tool `solid_profile` (Python `cad.profile`) reports cross-sections along x, y or z (extents, area, centroid, outline count) and ASCII silhouettes for any Freeform, solid, surface or mesh.
- **One picture of several views.** `screenshot` with `views` (front, back, left, right, top, bottom, iso) and `object` returns one labelled sheet at a common scale of only the named objects, restoring the camera, display, grid, origin helpers and visibility.
- **Edit replies describe the change.** `form_op` also returns `bounds`, `sizeChange`, `created` (inclusive index ranges of appended vertices and faces), `removedVertices`/`removedFaces`, `moved` (count, largest distance, vertex), `flippedFaces` (count), `flippedFaceIds` (zero-based indices of faces whose normal changed direction substantially with the same ordered vertices), and `warnings` (possible folds, more stretched faces or n-gons, a cage that lost closure, growth to over three times the size).
- **`guide` tool.** Lists and serves built-in how-to guides (`freeform`).

Tests: [FreeformAgentToolTests](../../../Prism.Tests/Headless/Automation/FreeformAgentToolTests.cs) (API-H003 to API-H012) and [test_cadkit_shapes.py](../../../tests/test_cadkit_shapes.py). Multi-view `screenshot` needs the desktop window and was verified by hand on 2026-11-17 (no automated test).

2026-11-18: API-H005 passed on Windows. Moving the four upper corners of a box through its base reported the four side faces through the automation API.

## Planned major improvements

Owner direction on 2026-11-16 ([#139](https://github.com/chrizpro/prism/issues/139)): substantially improve Freeform modeling, including cage control, surface quality, shaping workflows and feedback for people and agents. This direction is now in the project README roadmap. Specific operations, diagnostics and acceptance criteria remain proposed until reviewed; this documentation change adds no new product behavior.

## Owner requests of 2026-11-16

**Owner decisions 2026-11-16, recorded here:** Delete key — approved and already true (see Delete, corrected). Number keys and soft selection — **no changes needed**. Edge loop slide — approved as *click, hold, move along the edge, release*; **not built yet**. Copy and paste — approved for whatever is selected, including faces, edges and, if it can be made to do something useful, vertices; **not built yet**. The point-reduction ideas stay optional. Everything not named here is still Proposed — awaiting owner review. "Today" lines were read from the code on 2026-11-16 and not tried by hand.

### Slide an inserted edge loop in the viewport — Approved 2026-11-16 as press, hold, move, release; delivered (legacy Freeform workflow suite, not tried by hand)

**Delivered:** with Insert Edge armed, pressing on an edge inserts nothing; while the button is held the loop is previewed and follows the pointer along the edge (the nearest point of the edge to the pointer's ray, so perspective does not skew it); releasing places it, the panel's *Position* takes that value, and the tool stays armed. A press with under about 4 px of movement is a plain click and uses the panel's position as before. **Shift** snaps to 25, 50 and 75 %; **Esc** while holding cancels without inserting. With *Loops to insert* above 1 the loops stay evenly spaced (the position does not apply). Checks: five new lines in `tests/Prism.FreeformWorkflowTests` (press inserts nothing, hold shows the preview, Esc cancels, release places, the loop lands a quarter of the way along). **Not built:** mouse wheel or Up/Down to change the count while holding, and Ctrl to mirror a pair. The original proposal follows.

**Today.** Insert Edge adds a loop where you click an edge, using the *Count* and *Position* typed in the panel ([FormTools.cs](../../../Prism/Tools/FormTools.cs)). There is no way to drag the loop. Separate **Slide** exists, but only for edges that are already selected.

**Proposal.** After the first click on an edge the loop is shown live and follows the pointer *along the ring*; a second click (or releasing the mouse, if the first press dragged) places it. The mouse wheel or Up/Down changes the count while it is live; Shift snaps to 25 %, 50 % and 75 %; Ctrl mirrors a pair of loops about the middle; Esc cancels without changing the cage. The panel's *Position* follows the pointer and still accepts a typed value. The tool stays on for the next click, as now.

**Open decisions.** Click-then-move (Blender) or press-drag-release (Maya)? Recommended click, move, click, because it keeps working with a pen or a trackpad. Should the loop stop at creases? Recommended no.

**Acceptance (draft).** On a 10 mm cube with one loop live, moving the pointer to the middle gives a loop whose points are at 5.0 mm on every ring edge; placing it adds 4 points, 4 faces and 8 edges and keeps the cage closed. Esc leaves 8 points and 6 faces. Typing 0.25 in *Position* puts the loop at 2.5 mm.

### Number keys for Vertex, Edge and Face — no change needed (owner, 2026-11-16)

**Today (finding, not a change).** The keys already exist while a Freeform is being edited: **1** vertices, **2** edges, **3** faces and **4** the whole form ([Hotkeys.cs](../../../Prism/Core/Hotkeys.cs), actions `form.vertex`, `form.edge`, `form.face`, `form.object`). They are limited to Freeform editing, so they do not fire in other workspaces.

**Proposal.** The gap is discoverability, not the keys. Show the key in each mode button's tooltip and in the status-bar selection mode picker. *Context-sensitive* is read as: the same keys do nothing in Freeform until a form is being edited, and **Tab** toggles between Object and the last component mode. **Open decision:** is the Tab toggle wanted, and should the keys stay 1 to 4 when a form is not being edited (where they could switch Object, Face, Edge and Vertex picking for every body)?

### Delete

**Correction, 2026-11-16.** This section first said the Delete key did not delete cage faces. That was wrong: while Edit Freeform is open, the Delete key runs the same *Delete selection* action as the ribbon button ([FormTools.cs](../../../Prism/Tools/FormTools.cs)): for faces it removes them, for edges the faces either side, for points the faces that use them, and refuses frozen faces. **Delivered (owner request, tried by build only):** the ribbon **Modify → More → Delete** is removed, since the key does the same job. With no cage selection and a whole form selected, Delete still removes the whole object, as before. The panel never showed the action.

### Soft selection, extended — no change needed for now (owner, 2026-11-16: "if we have soft selection, then no need to improve it"; the owner may review it further)

**Today.** Soft selection exists in the Edit Freeform panel ([FormCommands.cs](../../../Prism/Commands/FormCommands.cs)): one *radius* (0 = off, millimetres) and one *falloff* (Smooth, Linear, Sharp). Weights come from the straight-line distance in space between points ([FalloffMath](../../../Prism/Commands/FreeformCommands.cs)), so the handle of a mug can be pulled by moving the body next to it.

**Proposal, in priority order.**
1. **Distance along the cage ("connected only").** Weight by the number of edges or the length of the shortest path over the cage instead of straight distance, so unconnected parts nearby are not dragged. Choice: *Space* (today) or *Along the surface*. Recommended default: Along the surface.
2. **Adjust the radius while you work.** Hold a key (proposed **Shift+wheel**) or drag with the soft handle to change the radius live; a shaded ring on the cage shows the influence (white 1, fading to none) before you move anything.
3. **Falloff curves.** The current three plus **Constant**, **Spike**, **Root**, **Sphere** and a **Custom** curve you draw in a small editor. Each is a function of distance / radius, 0 to 1, shown as a thumbnail on the button.
4. **Pin and protect.** Frozen points already do not move; soft selection never moves them, and *Pin* points that keep their position without being frozen from other tools.
5. **Weight painting.** Paint the weights of the soft selection with a brush (radius, strength, smooth, erase), for shapes distance cannot describe. Weights live with the form and survive saving.
6. **Symmetry.** Soft selection respects Live symmetry (already the case for the move itself; the influence ring should mirror).
7. **Proportional types for rotate and scale**, not only move: rotation and scale are weighted by the same falloff about the selection's pivot.

**Open decisions.** Which of 1 to 7 for the first delivery (recommended 1 to 3); whether NURBS *Edit Points* and *Sculpt*, which share the three falloffs, should gain the same options (recommended yes, one shared soft-selection control, as [one tool, several jobs](#one-tool-several-jobs) suggests); where *Custom* curves are saved (in the document, so a model keeps its look).

**Acceptance (draft).** On a 10 x 10 mm plane cage of 11 x 11 points, soft radius 3 mm, Linear falloff, lifting the centre point by 1 mm lifts points at 1.5 mm distance by 0.5 mm and points at 3 mm or more by 0. With *Along the surface*, a second plane cage placed 1 mm above the first and not joined to it does not move. A mirrored move with X symmetry on gives mirrored heights within 1e-6 mm.

### Copy and paste of faces, and in general — Approved 2026-11-16 for faces, edges and (if useful) vertices; not built yet

**Today.** Copy and Paste exist as `Ctrl+C` and `Ctrl+V` for the selection ([Hotkeys.cs](../../../Prism/Core/Hotkeys.cs)); whether they carry the faces of a form was not checked.

**Proposal for Freeform.** With cage faces selected, Ctrl+C copies those faces (with their points, creases and frozen flags) and Ctrl+V pastes them as a **new Freeform body** at the same place, selected and ready to move with the gizmo, or as part of the same cage when *Paste into this form* is chosen (faces that share edges with the cage weld to it). Ctrl+Shift+V pastes at the pointer. The general rule across Prism is in [Transform → Copy and paste](../transform/README.md#copy-and-paste-in-general--proposed-awaiting-owner-review).

**Open decision.** New body or join the cage by default? Recommended new body, because it never changes the form you copied from.

**Acceptance (draft).** Copy the top face of a 10 mm cube cage and paste: a one-face body with 4 points exists at the same place, the cube is unchanged, and Ctrl+Z removes the new body only.

### Fewer points: ideas for keeping the cage light (optional)

The owner's goal is to keep the number of points down while controlling the shape. Suggestions, each independent and none required:

1. **Show the point count** and a target in the Freeform status line, with a warning colour past a chosen number.
2. **Reduce** (the opposite of Subdivide): merge faces in an even pattern to cut points by about a quarter, within a tolerance of the current surface. The ribbon already has *Convert fewer faces* for NURBS conversion; this is the same idea on the cage.
3. **Select edge loops to dissolve**: remove a loop of edges and its points without leaving a hole (Dissolve), so a loop added by mistake or no longer needed goes away cleanly.
4. **Crease instead of loops**: sharpness on an edge keeps a corner tight without extra support loops (Crease exists; the default is the open finding 4 above).
5. **Symmetry and Mirror and weld** first, so only half the points are worked on (both exist).
6. **Surface Check**: list loops that add points but change the surface by less than a tolerance, so they can be dissolved in one click.

**Open decision.** Which, if any, to build; recommended 1 and 3 first as they are cheap and safe.
