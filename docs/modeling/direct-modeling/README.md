# Direct Modeling tab

Edit the geometry you already have, with no history needed. It works on any exact body, including parts imported from STEP, IGES or BREP.

**Opening it:** on the Solid tab, the **Direct Edit** section has one button, **Direct Edit**. It opens this tab the way Create Sketch opens the Sketch tab, and **Finish Editing** (far right) goes back.

**Opening it from other workflows (proposed, awaiting owner review).** Solid and Surface each have a **Direct Edit** section with the same button. (Freeform and NURBS had one until 2026-11-16, when the owner removed it as pointless: their own ribbons already show Edit Freeform, Edit Points and Sculpt.) The tab remembers where you came from and disables the tools that cannot work on that kind of geometry; a disabled tool stays visible so the tab reads the same everywhere.

| Tool | Solid | Surface | Freeform | NURBS |
|---|---|---|---|---|
| Gizmo (click a face: pull handle) | all handles | pull handle only | cage tool opens instead | control-point tool opens instead |
| Edit Feature, Replace / Match / Extend / Trim Face, Split Face, Split Body, Draft, Edit Pattern, Mirror Feature | yes | no | no | no |
| Check and Repair, Fill / Cap, Merge Faces | yes | yes | no | yes |
| Edit Freeform | no | no | yes | no |
| Edit Points, Sculpt | no | no | no | yes |
| Transform, Select, Inspect, Appearance | yes | yes | yes | yes |

Acceptance criteria (unapproved): opening Direct Edit from each tab shows the Direct Modeling tab; the first tool that starts is the gizmo (Solid, Surface), Edit Freeform (Freeform) or Edit Points (NURBS); every tool the table marks "no" is disabled; Finish Editing returns to the tab you came from. Checked by SOL-H053 in [SolidRibbonTests](../../../Prism.Tests/Headless/Solid/SolidRibbonTests.cs).

**Adding things stays on the Solid tab.** Hole, Fillet, Chamfer, Shell, Draft and Split Body are there. This tab is for changing what exists; to remove a hole, fillet or boss, select it and press **Delete**.

The tab reads left to right: **Select, Datum, Edit, Repair, Transform, Inspect, Appearance, Exit**.

**Status key:** *Done* works and is covered by tests. *Thin* works but is only tested on simple parts. *Gap* has a known limitation.

## The gizmo

There is no button for it. It is open whenever the tab is open. Click a face, hole, slot, pocket, boss or fillet (one click picks the whole feature) and controls appear on it:

| Handle | Colour | What it does |
|---|---|---|
| The shared gizmo | red, green, blue | Moves the selection along X, Y or Z. It is the same gizmo as the Move tool and sits at the middle of a flat face or at the centre of a feature. A hole, slot, pocket or boss moves as a whole feature; flat faces slide and the body stretches to follow. On a flat face that looks along X, Y or Z, the pull handle stands in for the gizmo arrow in that direction until the face is moved. |
| Pull | orange | Moves every picked face along its own normal. A positive value moves faces out of the part (a boss grows, a hole gets smaller). |
| Tilt arc | purple | Tilts a single flat face about its own X direction. |
| Rim handle | teal | On a round hole or boss, sets the radius. |

Drag a handle, or type a number while it is showing; the number goes to the control the pointer was last over. Only one handle can be used per edit; use another for the next one. Maintained checks: HDL-H027 in [ViewportHandleTests](../../../Prism.Tests/Headless/Viewport/ViewportHandleTests.cs) and the older [direct gizmo checks](../../../tests/Prism.DirectModelTests/GizmoTests.cs). The same results are available from the older tools (Offset Face, Move Face, Rotate Face and Edit Feature's move), which the gizmo hands the work to.

## History

Direct edits are plain undo steps by default. Right-click a body (in the viewport or the Browser) and choose **Track history** to record its direct edits as editable steps. Features made with the Solid tab (Extrude, Revolve and the rest) keep recording history as before. Moving, turning or scaling whole bodies is the exception: it is always recorded, so editing an earlier feature of a part (for example a component's size) does not send the parts back to where they were first made (HDL-H045). Face-level edits on a body that is not tracking history are still dropped when an earlier feature is edited, and the status bar says which steps stayed on the old branch.

## Sections

| Section | Tools | Notes |
|---|---|---|
| Select | Object, Face / Edge / Vertex, Rectangle / Lasso (dropdown), Invert, Through | Shared by every modeling tab. See "Selecting" below. |
| Datum | Gizmo (with Point, Axis, Plane, Save gizmo in its dropdown) | Shared. This is the datum gizmo, not the edit gizmo. |
| Edit | Edit Feature, Replace Face (dropdown: Replace, Match, Extend / Trim), Split (dropdown: Split Face, Split Body), Draft, Pattern (dropdown: Edit Pattern, Mirror Feature) | Edit Feature also does resize, copy and Change type (plain, counterbore, countersink, modeled thread). |
| Repair | Check and Repair, Fill / Cap, Merge Faces | Check and Repair has only been tried on parts built in code. |
| Transform, Inspect, Appearance | as on Solid | Shared. |
| Exit | Finish Editing | |

## Improvements still to do

- Replace / Match / Extend and the two Splits share a dropdown each, but are still separate commands underneath; one command with a mode would be cleaner.
- Rebuild the browser (WebAssembly) kernel so Merge Faces works there.
- Walk the whole tab in the running app. See the contextual coverage section for the scope of headless verification.
- Test Check and Repair and the feature recogniser on large messy imported parts.

## Selecting

Selection is the heart of direct modeling, so the Select section is the same on every modeling tab (Solid, Surface, Freeform, NURBS, Direct Modeling and the rest).

- **Rectangle / Lasso** is a dropdown. Pick one, then drag in the view. A rectangle dragged left to right takes only what is completely inside it; right to left (or a lasso) takes whatever it touches. A tiny drag is an ordinary click. Shift adds, Ctrl takes away. Click the button again to go back to plain clicking.
- **What it picks** follows the mode: faces, edges or whole bodies, including curves, sketches and Freeform bodies in Object mode. Inside a tool that is asking for faces or edges, it adds them to that pick. In Edit Points it picks control points, and in Edit Freeform the Lasso choice applies to its empty-space drag.
- **Through** (X-ray) lets the rectangle and lasso also take faces, edges and bodies hidden behind others, so you can select both sides of a part in one drag. Without it only what you can see is picked. With it on, **Alt-click** steps through what is under the pointer, front to back.
- **Invert** selects what is not selected. Faces and edges invert within the bodies you picked from, so pick the few you want to leave out, press Invert and get the rest. With nothing selected it selects everything of the current kind.

## Contextual coverage

The shipped Direct Modeling tab has 35 entries including dropdown choices and shared controls. [Issue #40](https://github.com/chrizpro/prism/issues/40) tracks its option-level verification.

- [Maintained Direct Modeling tests](../../../Prism.Tests/Headless/DirectModeling/) exercise Replace Face, planar Match Face, Extend and Trim to a target, splitting and merging a planar face, and all three Fill / Cap modes on a planar opening.
- [Direct edit tests](../../../Prism.Tests/Headless/DirectModeling/DirectEditTests.cs) cover Push / Pull on flat faces, hole walls and surface faces; Move Hole / Boss moves and diameters; Delete Face on solids and surfaces; Offset Face on flat faces and holes; Rotate Face about world axes and a picked edge; Move Face moves and every tilt axis; Remove Fillets by pick and by radius; Move Body flush (touching and aligned) and coaxial; Mirror Feature; and Check & Repair reporting, healing and closing.
- [Standalone DirectModelTests](../../../tests/Prism.DirectModelTests/) contain feature recognition, edit actions, direct gizmo, face operations, repair and pattern checks. [Shared Solid tests](../../../Prism.Tests/Headless/Solid/) cover the entry, exit and shared controls.

### Option groups to verify

- Edit Feature offers Move, Resize, Copy, Delete and Change type. Hole conversions include Plain, Counterbore, Countersink and modeled Threaded, with side, size and thread controls.
- Edit Pattern adjusts spacing and count in two directions, angular step, and individual instance removal. Mirror Feature uses a picked plane or face.
- Check and Repair separates inspection from healing. It exposes tiny size, sewing gap, healing, tiny/sliver removal and closing a sealed surface into a solid.
- Fill / Cap offers Automatic, Flat and Smooth patch with a joint tolerance. The current maintained cases use a planar opening for each mode.

Remaining maintained work includes Edit Feature action and type choices, smart-selection scope and gizmo handles, cylindrical and conical Match Face, pattern modes and Mirror Feature, Repair, nonplanar caps and joint tolerances, and rejection, undo and history cases. Desktop interaction remains deferred.

### Feature and pattern actions

[Maintained feature action tests](../../../Prism.Tests/Headless/DirectModeling/FeatureActionTests.cs) exercise Delete, Resize, Copy and Move on a drilled hole, plus linear hole spacing, count and instance removal. [Issue #44](https://github.com/chrizpro/prism/issues/44) tracks additional feature and pattern options, including conversions, circular layouts and mirror targets.

Windows verification on 2026-11-05: the combined candidate passed 86 Unit and 592 Headless tests with rebuilt native libraries. See the [integration verification report](../../engineering/release-readiness-2026-11-05.md) for source revisions, commands and limits.

## Sculpt Face and Warp on faces — direction approved 2026-11-16; details awaiting owner review

**2026-11-17: Sculpt Face is no longer in the ribbon; Edit Face took its place.** The owner found it poor and asked for it to go unless it could be improved a lot. The command and its tests (DIR-H072 to DIR-H077) are kept, so it can return. The wanted replacement is Fusion's **Edit Face**: turn the picked face into a grid of control points (the grid size adjustable), move the points with the gizmo, and leave the editor to turn it back into a face of the solid with its outline kept. That is a separate proposal, not started; Prism already has a control-point editor for NURBS surfaces, Replace Face, and the face-to-net and net-to-face conversion that Sculpt Face uses.

**Warp button (delivered 2026-11-16, tried by tests only).** The Direct Modeling tab's *Shape* group now has **Warp**, enabled for the Solid and Surface workflows. It is the same Warp as on the Surface, Freeform and NURBS tabs: it bends, twists, tapers or stretches a **whole** body or surface along an axis. It does not yet act on single faces.

**Owner direction.** Sculpt a face of a solid in place, in the spirit of Fusion's face editing, and let Warp take **faces to warp** so a face can be modified "a little". The owner chose the "Option A" shape of this: the face becomes a NURBS surface, its interior is reshaped, and it goes back into the body.

**What it is not (so the expectation is clear).** It is not Fusion's Press Pull or Move Face (those move a face and let its neighbours extend or trim; Push/Pull, Replace Face and Match Face already do that), and it is not the Sculpt workspace (T-splines). It reshapes the *surface* of a face and keeps the face's outline where it is.

**Proposed behavior (first version).**
1. Pick one or more faces of a solid; reshape them with a soft brush (pull, push, smooth, with the radius and falloff controls already used by Freeform and NURBS Sculpt), and, for Warp, with a **Faces to warp** pick that limits Bend, Twist, Taper or Stretch to those faces.
2. **The outline stays fixed.** The outer ring of control points never moves, so the face still meets its neighbours exactly. Neighbouring faces do not follow and are not smooth with the new surface; the join is a sharp edge.
3. **The result must be a valid solid.** The edit is built on a copy; if the body is not valid (or the faces cannot be sewn back), the command refuses with a message and the body is unchanged.
4. Planar and gently curved faces first. Cylinders, cones, spheres and faces with holes or trimmed outlines are later and, until they work, are refused with a message.
5. One undo step per edit; the saved history re-edits with the same settings.

**Open decisions for the owner.** (a) How the brush is driven: a viewport brush like NURBS Sculpt, or first a typed bump (position, radius, height) with handles? Recommended: typed bump with handles first, brush second. (b) How much detail a face gets: automatic from the radius, or an *Add detail* button like Sculpt? (c) Whether Warp's range controls (*starts at*, *ends at*) still apply to a faces-only warp.

**Feasibility evidence, 2026-11-16 (not a delivered feature).** [DIR-H070 and DIR-H071](../../../Prism.Tests/Headless/DirectModeling/SculptFaceFeasibilityTests.cs) take the top face of a 40 x 40 x 20 mm box, convert it to NURBS, refine it twice, raise (+4 mm) or dent (-4 mm) a smooth bump in its interior with the outline ring unchanged, and sew it back with the other five faces: both are valid solids, the raised one is taller than 20 mm and larger than 32,000 mm³, the dented one smaller. **Not yet shown:** curved faces, faces with holes or trimmed outlines, several faces at once, and the interactive tool.

### Delivered 2026-11-16 (owner: "just get to work and let me know the restriction"; checked by tests, not tried by hand in the app)

Owner decisions: turning a face into NURBS is acceptable; keep Warp on the Direct Modeling ribbon with an option to modify faces individually. The open decisions above were settled by my recommendations: a **typed bump** (position, radius, height) first, detail as a plain number (default 3 refinements, 1 to 5), and Warp's *starts at / ends at* range still applying, measured over the whole body.

- **Sculpt Face** (Direct Modeling > Shape, solid workflow; `sculpt_face` for scripts): pick a flat, four-sided face; *Height* (negative pushes in), *Bump radius* (set to 40 % of the face's smaller side when you pick), *Falloff* (Smooth, Linear, Sharp), *Move the bump across* and *up* (mm), *Detail*. Tests [DIR-H070 to DIR-H076](../../../Prism.Tests/Headless/DirectModeling/SculptFaceTests.cs): a +4 mm bump on a 40 x 40 x 20 mm box top is a valid solid between 32,001 and 33,600 mm³ with the same width and depth; a -3 mm dent removes volume; offsets move the bump; a box turned 30 degrees, a face tilted 20 degrees and a box with every edge filleted (3 mm) each gain the same bump volume (632 mm³, 630 for the filleted one).
- **Warp, Faces to warp** (optional second pick on Warp): bends, twists, tapers or stretches only the picked faces; the other faces are left exactly as they were. With no faces picked Warp is unchanged (WRP-H012). Tests [WRP-H010 to WRP-H012](../../../Prism.Tests/Headless/Solid/WarpFacesTests.cs).

**Limitations (all refused with a message, body unchanged):**
1. **Flat faces only.** Cylinders, cones, spheres and free-form faces are refused ("works on flat faces for now").
2. **Four sides, no holes.** A face with a hole, a rounded corner, or more or fewer than four edges is refused. Faces whose corners are rounded off by a fillet qualify when the flat part still has four straight edges.
3. **The outline is fixed.** The outer ring of points never moves, so the neighbouring faces do not follow and the join is a **sharp edge**, not smooth; no tangent blend into the neighbours.
4. **The face becomes NURBS.** It is no longer a plane after the edit, so tools that need a flat face (Replace Face, Extend/Trim Face) refuse it afterwards. Undo restores it.
5. **Valid solid or nothing.** The result is sewn at 0.01 mm; if it does not close into a valid solid the edit is refused. A very large height or a very tight radius can trigger that.
6. **One face at a time for Sculpt Face.** Warp takes several faces, but each is warped on its own with its own outline.
7. **Handles, no brush.** Sculpt Face has four viewport handles (owner request 2026-11-16, [DIR-H077](../../../Prism.Tests/Headless/DirectModeling/SculptFaceTests.cs); the dragging itself is not tried by hand): an arrow out of the face for **height**, an arrow across it for **radius**, and two arrows from the face's centre that **slide the bump** across and up the face. They edit the same typed values. There is no free brush yet.
8. **Warp's handles in the viewport** are shown for whole-body warps only; a faces-only warp has typed values and no handles yet.
9. **Surfaces** (not solids) are accepted by Warp's face option but not by Sculpt Face, and are less tested.
10. Not yet checked: faces much larger than the bump radius (the bump only touches the middle), very thin faces, bodies with hundreds of faces (speed), and edits repeated many times on the same face (each edit refines it again).

## Slots with a rounded rim (2026-11-17)

Feature recognition now looks for holes, bosses, slots and pockets before it groups smooth curved faces into fillet chains. A slot whose edge is rounded used to be taken as a fillet chain, so its flat walls were left as separate faces. Now the walls, the round ends and the rim rounds that run on from them are one **Slot**, and a click on any of them picks all. Test: DIR-H090. Checked on a 626-face plate with 51 slotted openings (about 51 slots found, the plate faces left as faces); whether Pull or Move on such a slot works on every part is not yet checked.

**Large parts.** On that plate each Direct Edit preview took 7 to 14 seconds. A live preview that takes more than 3 seconds is now switched off for the rest of that command, with a message; Apply still makes the change, and ticking Preview brings it back. The edit itself is not faster.


## Edit Face (owner-approved 2026-11-17; implemented, desktop check pending)

Status: built and covered by DIR-H091 to DIR-H097 in `EditFaceTests.cs`; not yet tried by hand in the running app. Differences from the proposal below: **flat faces only** in this first version (a curved four-sided face is refused with a message), and there is **no Add detail** yet, so the grid size is set with Grid cells across and up (changing it rebuilds the grid and resets the points). Edit feature brings the moved points back. Moving a point through the far side of the solid is refused. Falloff with the influence radius is the shared control-point behavior and is not separately tested. It replaces Sculpt Face, which left the ribbon on 2026-11-17. The model is Fusion's **Edit Face**: the picked face becomes a grid of control points, you move the points, and leaving the editor turns the result back into a face of the solid. The aim is that it is easy to use: a grid you can see on the face, points you can grab, and a result that is a smooth surface.

**Using it**
1. **Direct Modeling → Shape → Edit Face** (where Sculpt Face was). Click one face of a solid.
2. The face shows a grid of control points drawn over it. The default is 3 × 3 cells, which is 4 × 4 points. The outer ring of points is locked and drawn greyed, because the face's outline stays where it is; the inner points can be moved.
3. Click a point to select it (Shift adds; a box or lasso picks several). Drag the shared gizmo to move, rotate or scale the selected points. Typing exact X, Y and Z for a single point works as it does in Edit Control Points.
4. **Move points** chooses **Along the face normal** (the default, for bumps and dents) or **Any direction**.
5. **Influence radius** and **Falloff** make nearby points follow the one you move. At radius 0 only the selected points move.
6. **Grid size** sets the cells across and up (2 to 8 each). Changing it before any point has moved rebuilds the grid. After points have moved, **Add detail** adds points without changing the shape.
7. **Reset points** puts every point back.
8. The solid updates live while you edit. A preview that takes more than 3 seconds is switched off with a message, and **Apply** still works.
9. **Apply** replaces the face with the smooth surface through the grid and keeps the outline. The neighbouring faces meet it at the original edges, which stay sharp: the new surface is not made tangent to them. One undo step. Edit feature reopens it with the points where they were.
10. If the result would not be a valid solid, nothing changes and the message says so.

**First version covers:** one flat or curved face with four sides and no holes. Other faces are refused with a message that names the reason (more than four sides, holes in the face). Not in this version: faces with more than four sides or holes, tangent continuity with the neighbours, mirrored (symmetric) editing, and several faces at once.

**Acceptance criteria**
1. A box top shows a 4 × 4 grid: 12 locked points and 4 movable ones.
2. Moving one inner point 2 mm along the normal gives a valid solid that is larger by the expected volume, with the face's edges unchanged and the moved point on the new surface.
3. A 5 × 5 cell grid shows 6 × 6 points, 16 of them movable.
4. With influence radius greater than 0 and a smooth falloff, points near the moved one follow it by less the farther they are. At radius 0 they stay.
5. A locked point cannot be selected or moved.
6. Reset points restores the original volume and bounds.
7. A reshape that makes an invalid solid changes nothing and shows a message.
8. Undo and redo step over the whole edit. Edit feature restores the moved points.
9. A five-sided face and a face with a hole are refused, each with its own message.
10. Moving along the normal keeps every moved point on its original line along the normal.

**Reuse:** Sculpt Face's face-to-net and net-to-face conversion, and the control-point selection, gizmo and falloff of Edit Control Points. When this lands, Sculpt Face and its tests (DIR-H072 to DIR-H077) are retired.

## Large imported parts: speed (owner report 2026-11-19)

The owner reported that an imported pegboard (`Skadis_Raster (1).step`: 220 × 220 × 5.1 mm, about 60 slots with rounded rims, 626 faces) was almost impossible to work with. Measured in the running app: moving the whole body 10 mm took 26 s, Move Face on the top 30 s, Delete Face on one slot 11 s, and a single click to select about 4.4 s. A trace showed that measuring the body (volume, area and exact box) took about 4 s on this part and was repeated for the selection panel, the Move preview, the display mesh and the automation reply, never kept.

A shape now keeps its measurements, and a moved copy inherits them (same volume and area, box moved). Measured headlessly on the same file: Move takes 0.1 s plus 1.7 s to rebuild the display mesh; Move Face on the top takes about 10 s, 4.3 s of which is the one measurement of the new shape. Checks: [DIR-H098, DIR-H099](../../../Prism.Tests/Headless/DirectModeling/ImportedPartSpeedTests.cs); DIR-H100 times any STEP file given in `PRISM_SPEED_STEP`. Not yet tried by hand in the app.

Still open: the measurement itself integrates area to a relative precision of 1e-9, which is what makes it slow on hundreds of curved faces; the moved body's display mesh is rebuilt rather than moved; and Move Face on the top split the board from 626 to 834 faces.
