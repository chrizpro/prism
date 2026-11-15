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
