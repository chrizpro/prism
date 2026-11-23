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

## Lowering a cylindrical boss

Select the cylindrical side and drag the gizmo along the cylinder axis. On a simple boss with one flat cap and a perpendicular planar shoulder, this changes the height and preserves the cap angle, including after **Tilt face**. The drag stops with an explanation if the low edge would cross the shoulder. Verified against a 5 mm downward Z drag on a boss with a 23° cap. [Owner report #154](https://github.com/chrizpro/prism/issues/154) · [DIR-H200](../../../Prism.Tests/Headless/DirectModeling/DirectEditTests.cs).

Windows desktop verification, 2026-11-20: [selected wall before dragging](evidence/DIR-H200-before.png) and [after lowering the cap](evidence/DIR-H200-lowered.png). The pointer API measured −4.999998 mm along Z and the volume decreased by 320π mm³ within 1 mm³.

The [pin-fin cold plate](../../../gallery/models/pin-fin-cold-plate.prism) demonstrates this edit in the other direction. Start with the [STEP blank](../../../gallery/extras/pin-fin-source.step), raise its pilot boss 20 mm, then use Edit Feature to copy it across six columns and four rows. The original sits on the left in the comparison document. [Gallery verification](../../../Prism.Tests/Headless/Gallery/IndustrialGalleryStudies.cs).

The [keyed shaft coupling](../../../gallery/models/keyed-shaft-coupling.prism) starts from a [five-hole STEP blank](../../../gallery/extras/shaft-coupling-blank.step). Move Face lowers the hub by 24 mm, Edit Feature enlarges the pilots from 6 to 18 mm, and Edit Pattern distributes ten openings at 36° spacing. The original and revised flanges open side by side.

## The gizmo

There is no button for it. It is open whenever the tab is open. Click a face, hole, slot, pocket, boss or fillet (one click picks the whole feature) and controls appear on it:

| Handle | Colour | What it does |
|---|---|---|
| The shared gizmo | red, green, blue | Moves the selection along X, Y or Z. It is the same gizmo as the Move tool and sits at the middle of a flat face or at the centre of a feature. A hole, slot, pocket or boss moves as a whole feature; flat faces slide and the body stretches to follow. On a flat face that looks along X, Y or Z, the pull handle stands in for the gizmo arrow in that direction until the face is moved. |
| Pull | orange | Moves every picked face along its own normal. A positive value moves faces out of the part (a boss grows, a hole gets smaller). |
| Tilt arc | purple | Tilts a single flat face about its own X direction. |
| Rim handle | teal | On a round hole or boss, sets the radius. |

**Apply** commits the current edit as one undo step and keeps Direct Edit ready for the next pick. **Done** commits a pending edit and returns to the previous tab. After Apply, Done can also leave immediately. An invalid edit stays open for correction. [Owner request #156](https://github.com/chrizpro/prism/issues/156) · [DIR-H201](../../../Prism.Tests/Headless/DirectModeling/DirectEditTests.cs) and [Windows command panel after Apply](evidence/DIR-H201-apply-done.png).

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

- **Replace Face onto a tilted plane, and Merge Faces on nearly flat faces (owner report 2026-11-23, fixed; [DIR-H202 to DIR-H204](../../../Prism.Tests/Headless/DirectModeling/ReplaceFaceTiltedTests.cs), tried in the app before the fix only).** A split piece was turned about its corner and its end set onto the box wall with Replace Face. The added material was the end face swept straight out along its own normal, so a 2.5 mm facet stuck out about 1 mm beside the long side. Now, when every neighbour of the face is flat, the added or removed material is bounded by the neighbours' own planes, so they stretch or trim to the new plane with no extra face (a face with a curved neighbour keeps the old sweep). The same code moves faces for Direct Edit and for Move on solid faces. Merge Faces then refused the two side faces, which were 0.0001 mm apart: the kernel's face merge now treats faces within 0.001 mm and 0.00001 rad as one surface (native kernel change; rebuild `native/` with `build.ps1`). The browser kernel needs the same rebuild.
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

**Windows desktop verification, 2026-11-18.** On `v0.4-work` at `b6c43909`, clicking an inner point on the top of a 30 × 30 × 10 mm box and setting it 2 mm higher produced a valid solid with volume 9,112.5 mm³ (up from 9,000 mm³). Two maintained ribbon checks detected missing hover help for Edit Face. [Issue #142](https://github.com/chrizpro/prism/issues/142).

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

Two changes. A shape now keeps its measurements, and a moved copy inherits them (same volume and area, box moved). And the exact box, which took 3.6 s of the 4 s, now searches only the faces that can widen it: the vertices give a box inside the exact one, and a face whose quick box (without its tolerance padding) fits inside that box is skipped. On the pegboard it takes 0.14 s and gives the same box to the last digit; volume and area are computed exactly as before. Measured headlessly on the same file: import 6.9 s to 3.1 s; Move 0.1 s plus 1.7 s to rebuild the display mesh; Move Face on the top about 6 s in all (the edit itself is about 4.7 s), of which measuring the new shape is 0.5 s instead of 4.3 s. Checks: [DIR-H098, DIR-H099, DIR-H101](../../../Prism.Tests/Headless/DirectModeling/ImportedPartSpeedTests.cs) (kept and inherited numbers match a fresh measurement; exact boxes for a sphere, a cylinder and a plate with rounded slot rims); DIR-H100 times any STEP file given in `PRISM_SPEED_STEP`. Not yet tried by hand in the app.

Still open: the moved body's display mesh is rebuilt rather than moved, and Move Face on the top split the board from 626 to 834 faces.

## Feature recognition, editable patterns and baking history (owner-approved 2026-11-19; in progress)

**Owner request, 2026-11-19:** direct modeling should recognise everything it can on an imported part (holes, text, fillets, chamfers, bolts in holes, patterns) so it can be edited, taking ideas from NX and Solid Edge; and the user should be able to bake or remove history when a part gets too heavy. **Approved 2026-11-19:** the owner approved this plan and added smooth, KeyCreator-style editing (from a KeyCreator direct-modeling video), in particular editing by a dimension (section 6). Live rules (section 4) still need their own proposal. Each section records its status as it is built.

**Where Prism is today** (measured on the owner's 626-face pegboard STEP and in the code): holes, bosses, slots, pockets, fillets, counterbores and countersinks are recognised, one click picks the whole feature, and Edit Feature moves, resizes, copies, deletes or converts them (fillets can be resized). Line, grid and circular patterns are found: all 50 pegboard slots in 0.6 s. Gaps: no chamfers, threads, text, fasteners or ribs; the pegboard's rounded outer edge is mis-read as one 24-face "slot"; a staggered grid is reported as two diagonal steps instead of rows; nothing lists what was found; and history can only be switched off per body before editing, never baked afterwards.

**Ideas taken from NX and Solid Edge:** Solid Edge's *Recognize Holes* and *Recognize Patterns* turn imported geometry into hole and pattern features listed in PathFinder. NX synchronous modeling labels chamfers, resizes blends and chamfers, and edits patterns of faces. Both keep *live rules* (stay coplanar, concentric, tangent, symmetric) while faces move. NX *Remove Parameters* and Solid Edge's move to synchronous drop history from a body.

### 1. Recognise more (phase 1)

| Feature | How it is recognised | What you can do with it |
|---|---|---|
| Chamfer | a narrow flat (or conical, on a round edge) strip joining two faces at convex edges, with equal or unequal setbacks | Resize the distance or distances, swap to a fillet of matching size, delete it (the edges go sharp) |
| Fillet (existing) | as now, with its radius in the readout | as now; also swap to a chamfer |
| Hole, refined | through or blind, depth, drill-point angle, counterbore and countersink sizes; the size matched to the nearest metric clearance or tap-drill size ("Ø6.6, M6 clearance, through") | as now; Change type keeps the matched size |
| Threaded hole or stud | helical faces inside a hole or on a boss | Read the diameter and pitch ("M6 × 1"), delete the thread to leave a plain bore, change the size |
| Bolt, screw, pin or nut in a hole | a separate body on a hole's axis that passes through it: a shank that fits the hole, with a head or nut wider than the hole | Moving, resizing or deleting the hole carries the fastener with it; select all fasteners of one size |
| Text, engraved or embossed | many small faces whose walls all rise the same depth from one flat or gently curved face, with letter-like outlines (many separate loops close together, rich in splines and arcs) | Pick the whole word with one click; move it, change its depth, flip engraved and embossed, delete it, or replace it with new text in the same place (the original characters cannot be read back from the geometry) |
| Rib or wall | two parallel flat faces closer together than about a fifth of their size, joined to the body along one side | Change the thickness about its centre plane |
| Outer round or chamfer around a plate | a ring of rounds or chamfers along the outside of a body | Recognised as a fillet or chamfer chain, never as a slot (fixes the pegboard) |

**Status 2026-11-19, built and covered:** chamfers (flat, equal or unequal, with both setbacks and the angle; and a cone on a boss's edge), hole ends (through or blind, depth, drill-point angle) and the nearest metric size (tap drill and clearance sizes first, then nominal), and the rounded-outline fix (a ring of faces is a hole only when its walls face its axis). Two recogniser bugs were fixed on the way: inside and outside corners were judged at the average of an edge's points, which on a circular edge is its centre, and a drill point's cone was taken for a countersink. Checks: [DIR-H102 to DIR-H104](../../../Prism.Tests/Headless/DirectModeling/FeatureRecognitionTests.cs). Edit Feature resizes a chamfer (one distance, or two), deletes it, and swaps chamfers and fillets under Change type (the new size keeps the old setback unless typed); right-click offers Swap for a fillet or chamfer and no longer offers Move or Copy on rounds and bevels ([DIR-H105](../../../Prism.Tests/Headless/DirectModeling/FeatureRecognitionTests.cs)). **Text** is recognised as one feature per word: engraved or embossed, depth or height, letter count, letter height and reading direction. Edit Feature moves or copies it, deletes it, sets a new depth or height (Resize), and flips it between engraved and embossed (Change type, mirrored through the face it sits on so it still reads the right way) ([DIR-H110](../../../Prism.Tests/Headless/DirectModeling/FeatureRecognitionTests.cs)). Two more recogniser fixes: inside and outside corners are now voted along the whole shared edge (the inward corners of letters such as M fooled a single-point judgement), and a reversed flat face no longer reports its normal pointing into the body. **Fasteners** are recognised across bodies: a bolt, socket screw, round-head screw or pin whose shank runs along a hole (plain, counterbored or countersunk) in another body, with its metric size and length under the head, and any nut on it ("M6 × 25 hex bolt with a nut"). Edit Feature's **Bolts and nuts in the hole follow it** (on by default) moves them with the hole, removes them with it, and on Resize grows them to the matching metric size about where they bear on the plate, which keeps them seated and keeps their round faces exact (so a bolt one size up is also proportionally longer) ([DIR-H111, DIR-H112](../../../Prism.Tests/Headless/DirectModeling/FastenerTests.cs)). **Threaded holes** (modeled internal threads) are recognised with their major and minor diameters, pitch, length and metric size ("M6 × 1"); Edit Feature moves or deletes the whole threaded void, and Change type removes the thread, leaving a plain bore at the minor diameter ([DIR-H113](../../../Prism.Tests/Headless/DirectModeling/FeatureRecognitionTests.cs)). **Ribs** (a thin wall standing on the body) report thickness and height, and Resize changes the thickness about the rib's centre plane ([DIR-H114](../../../Prism.Tests/Headless/DirectModeling/FeatureRecognitionTests.cs)). Not yet built in this section: replacing text with new words, and external threads on studs.

Acceptance criteria (approved 2026-11-19), each on a part built in code, exported to STEP and imported again so no history survives: every listed feature is recognised with its sizes read within 0.01 mm (0.1° for angles); one click picks the whole feature; each action in the table changes exactly that feature and leaves a valid solid with the expected volume change; the pegboard's outer edge is a fillet chain and its 50 slots stay one pattern; recognising the 626-face pegboard stays under 1 s.

### 2. Patterns you can edit (phase 1)

- Rectangular, staggered (alternate rows shifted, as on the pegboard), circular and along-a-line patterns are described the way a person would: "50 slots in rows 40 mm apart along X; rows 20 mm apart, every other row shifted 20 mm".
- Edit Pattern changes the count and spacing in each direction, adds or removes single members, and turns the stagger on or off.
- Mirrored pairs: two equal features placed symmetrically about a plane are recognised as a pair, and editing one edits the other unless the user chooses only the one picked.

**Status 2026-11-19, built and covered:** grids are read as rows (a row direction along a world axis first, then the most features per row, then the shortest step), with the stagger found when every other row is shifted. The owner's pegboard reads "50 slots in staggered rows: 40 mm apart along X, 10 rows 20 mm apart along Y, every other row shifted 20 mm". Edit Pattern on a grid now sets Spacing along the rows, Spacing 2 between rows, the count per row and the number of rows, and **Every other row** (as it is, shifted half a step, not shifted); rows keep their own start and length, so shorter shifted rows stay shorter. Checks: [DIR-H109](../../../Prism.Tests/Headless/DirectModeling/FeatureRecognitionTests.cs); DIR-H100 prints the reading for any STEP file. Not yet: mirrored pairs.

Acceptance criteria (approved 2026-11-19): the pegboard reports staggered rows with the steps above; changing its row spacing to 25 mm moves every slot accordingly and keeps them one pattern; on code-built parts, rectangular, circular and mirrored hole sets report their counts and steps and edit as described.

### 3. Recognised features list (phase 2)

A **Features** group under each solid in the Browser lists what was found, grouped by type and size, for example "Holes: 8 × Ø6.6 through (M6 clearance), 4 × M5 tapped", "Slots: 50 × 5 × 15 mm, staggered pattern", "Fillets: R1 (64 edges)", "Chamfers: 1 mm × 45° (4 edges)", "Text: engraved 0.5 mm", "Fasteners: 4 × M6 × 20 hex bolt". Clicking a line selects those features; right-click offers the same actions as Edit Feature. The list is recomputed after each edit and shown only for solids.

**Status 2026-11-19, built and covered:** each solid (not assembly components, and not bodies over 4,000 faces) has a collapsed **Features** folder in the Browser listing what was recognised, grouped by type and size, with patterns described in words and fasteners in its holes; clicking a line selects those faces. Right-click actions on a line are not built yet: select it, then use Edit Feature or right-click in the view. Check: [DIR-H117](../../../Prism.Tests/Headless/DirectModeling/FeatureRecognitionTests.cs).

### 4. Live rules while moving faces (phase 3, separate proposal)

Optional rules, as in NX and Solid Edge, applied when a face moves with the gizmo or Move Face: faces that were coplanar stay together, concentric holes stay concentric, tangent faces stay tangent, symmetric features stay symmetric. Each rule can be switched off in the panel. This is the hardest part; it would get its own proposal with worked examples before work starts.

### 5. Bake or remove history

- **Bake history** (right-click a step in the Design history panel, or the panel menu for the whole document): everything up to that step becomes one starting point. Steps before it can no longer be edited or undone; steps after it are kept and stay editable. A confirmation says how many steps will be merged and that this cannot be undone except by reopening the saved file.
- **Remove history from body** (right-click a solid): the body keeps its shape and becomes a plain body, like an imported part. Its earlier feature steps stop being editable; other bodies keep their history. Direct edits keep working on it.
- The history panel shows how many steps and stored snapshots the document carries, so the user can see when baking would help.

**Status 2026-11-19, built and covered:** right-click a step in the Design history panel, **Bake history up to here…**, asks first (it says how many steps merge) and then makes that step the new start; the steps after it keep their recipes and stay editable, Undo stops at the start, and the saved file holds only the later steps. Right-click a solid, **Remove history (keep the shape)**, adds one ordinary (undoable) step that swaps the body for an identical plain copy under a new id, so the steps that made it no longer change it. Checks: [DIR-H115, DIR-H116](../../../Prism.Tests/Headless/DirectModeling/HistoryBakeTests.cs). Not yet: the step and snapshot count in the history panel, and baking from the panel's own menu for the whole document (Bake on the last step does the same). A reopened file calls its start "Start" again, and anything that pointed at the old body by id (a joint, a drawing view) has to be picked again after Remove history.

Acceptance criteria (approved 2026-11-19): after Bake history on a document, the history holds one starting step plus the steps after the baked one, every body keeps its shape (equal volume and box), saving produces a smaller file, and Undo stops at the baked step. After Remove history from body, editing an earlier feature of another body still replays correctly and does not change the plain body.

### 6. Edit Distance (KeyCreator-style editing by dimension)

**Direct Modeling → Edit → Edit Distance.** Pick what to move (a face, or any face of a hole, slot, pocket or boss, which picks the whole feature) and what to measure from (a flat face, a hole's axis or a datum). The panel shows the current distance; type a new one and the picked side moves by the difference along the measuring direction, the body stretching to follow as Move Face and Edit Feature's Move already do. Parallel flat faces measure square to the faces; a hole, slot or boss measures from its axis (centre to face, or centre to centre). **Symmetric** moves both sides by half the change each. If the picked feature is part of a pattern, **Also edit** offers the whole pattern, as the other face tools do.

**Status 2026-11-19, built and covered:** Edit Distance is in the Edit section after Edit Feature, with a hover card. The distance fills in with the current value when both picks are made; the viewport shows the dimension (grey: now, blue: after) and an arrow that drags the value. Checks: [DIR-H106 to DIR-H108](../../../Prism.Tests/Headless/DirectModeling/EditDistanceTests.cs). Not yet: Also edit for patterns, and two features moved symmetrically on the same body (refused with a message). Not tried by hand in the app.

Acceptance criteria (approved with the plan): on a plate with a hole 20 mm from one edge, setting the distance to 35 mm moves the hole 15 mm and keeps its diameter; between two parallel faces 40 mm apart, setting 30 mm moves the picked face 10 mm closer and the volume drops accordingly; Symmetric moves both faces 5 mm; a distance between non-parallel flat faces is refused with a message; the result is one undo step.

### Demo plan

A mounting-plate study built in code, exported to STEP and imported again so no history remains: a plate with outer chamfers and rounds, a circular pattern of counterbored holes with bolts in them, a staggered slot pattern, an engraved part number and a rib. The demo recognises everything and lists it, then resizes the bolt circle (the bolts follow), changes the slot pattern's count, swaps a chamfer for a fillet, replaces the engraved text, thickens the rib and bakes the history. It becomes a Direct Modeling gallery study with screenshots.

**Demo status 2026-11-19, built and covered:** [GAL-H205](../../../Prism.Tests/Headless/DirectModeling/RecognitionDemoStudy.cs) makes a 140 × 90 mm mounting plate in code (1 mm chamfer round the top, four Ø6.6 holes with M6 × 22 hex bolts and nuts, 11 slots in three staggered rows, "STEEL" engraved, a 4 mm rib), writes it and the bolts to STEP and reads them back with no history. Everything is recognised, then: the holes go to M8 clearance and the bolts and nuts grow to M8 with them, the slot rows are straightened, the chamfer becomes a 1 mm round, the text is raised, the rib goes to 6 mm, Edit Distance makes the plate 150 mm wide, and the seven steps are baked. With PRISM_GALLERY_OUT set it saves the document as imported and as revised. Two older pattern bugs showed up here and were fixed: a feature next to a gap in its grid (the short row) took its steps from the wrong neighbours, and three nearly collinear features made a huge "circle" that every feature seemed to lie on.

### STEP mounting plate gallery candidate

The gallery candidate ships the [source STEP](../../../gallery/extras/mounting-plate-source.step), the [fresh import](../../../gallery/extras/mounting-plate-imported.prism) and the [revised native model](../../../gallery/models/mounting-plate-revised.prism). In the fresh import, the STEP geometry carries no feature recipes. Edit Feature resizes the four clearance holes to Ø9 mm and carries their fasteners from M6 to M8. Edit Pattern aligns the 11 slots into straight rows. Change type swaps the edge chamfer for an R1 round and raises the engraving. Resize takes the rib from 4 to 6 mm. Edit Distance sets the plate width to 150 mm. The revised model captures the result after baking the seven steps.

Windows verification on 2026-11-20 used desktop and native source revision `be8a459`. The targeted Headless run passed 239 cases across Direct Modeling, Mesh, the selection and ribbon changes, and the ruled-loft regression. [GAL-H205](../../../Prism.Tests/Headless/DirectModeling/RecognitionDemoStudy.cs) exercised the full STEP round-trip and generated the three gallery files. The desktop visual run passed 50 checks in 12 scenarios, including movement, tilt, draft, push/pull, hole changes and removing top-edge fillets. The visual suite still records the separate rounded-outline fillet-recognition limitation in [#87](https://github.com/chrizpro/prism/issues/87).

The [split preview](../../../gallery/previews/mounting-plate-revised.png) uses the same Prism camera and neutral gray finish for both saves. To regenerate the native files, set `PRISM_GALLERY_OUT` and run the Headless test filtered by `TestId=GAL-H205`. Run [the preview script](../../../gallery/render_direct_mounting_plate.py) against a dedicated Prism desktop session with that output folder. The candidate is ready for gallery review. [Gallery workflow #61](https://github.com/chrizpro/prism/issues/61).

### Reduction gearbox housing

The [source STEP](../../../gallery/extras/reduction-gearbox-housing.step) opens as one 244 × 168 × 90 mm solid with 144 faces. It has a stepped through bore, an auxiliary shaft seat, a circular pattern of eight counterbores, four countersinks, four slotted feet, four gussets, a stepped side port, a drilled service pad and two underside reliefs. Use the [fresh import](../../../gallery/extras/reduction-gearbox-imported.prism) to try individual edits, then compare it with the [revised native model](../../../gallery/models/reduction-gearbox-revised.prism). The revised sequence grows the base by 16 mm, raises the main flange by 3 mm and the service pad by 4 mm, mirrors the dowel hole and counterbores the original dowel hole. The [same-camera comparison](../../../gallery/previews/reduction-gearbox-revised.png) uses one neutral gray finish.

The Windows desktop smoke test started each case with a fresh STEP import. Eighteen of 19 checks matched their measured outcome, including flat-face moves, rotation and draft, hole conversion and mirroring, Split Face followed by Merge Faces, and Split Body with volume conservation. The two-hole pad spacing moved from 30 to 34 mm. **Edit Pattern** reported an 8-to-6 change for the counterbore circle, but the solid retained all eight and had zero volume change. **Check & Repair** reported a valid closed solid and flagged two non-manifold edges in its mesh-based inspection. [Recorded outcomes and STEP hash](evidence/reduction-gearbox.json) · [Direct Modeling verification issue #40](https://github.com/chrizpro/prism/issues/40) · [Feature and pattern issue #44](https://github.com/chrizpro/prism/issues/44).

Rebuild with [BuildGearcaseStep.cs](../../../gallery/BuildGearcaseStep.cs) via `dotnet run --file gallery/BuildGearcaseStep.cs -- <output.step>` with Prism's native libraries on the search path. Run [test_gearcase_direct.py](../../../gallery/test_gearcase_direct.py) with `--step`, `--out` and the private desktop `--discovery` path. Add `--preview` to create the two native models and comparison in the output folder.

### Open-brace spindle carrier

The owner approved a square spindle carrier that turns into an open-brace design with a visibly wider base. The [source STEP](../../../gallery/extras/spindle-carrier-source.step) opens as a single solid with a 134 mm footprint, a Ø58 mm through-bore, eight counterbores and two round mounting holes. [Fresh import](../../../gallery/extras/spindle-carrier-imported.prism).

The [revised carrier](../../../gallery/models/spindle-carrier-revised.prism) has a 202 mm slotted base, tapered shoulders, two rounded triangular through-windows, a Ø74 mm bore with a Ø82 mm seat and six countersinks. The workflow joins widening stock to the imported solid and cuts the new pattern, windows and slots. **Offset Face** enlarges the cylindrical bore from Ø70 to Ø74 mm before the seat is cut. **Move Face** shifts the underside down 3 mm. The circular pattern was replaced with Boolean tooling for this revision after the gearbox test exposed the separate [Edit Pattern limitation](https://github.com/chrizpro/prism/issues/44).

On Windows desktop source `be8a459`, the script checked single-solid validity and positive volume after the Boolean and direct-edit operations. It measured the base width, bore and seat radii, verified the two slots and six countersinks and checked both through-windows with 4 mm square probes. It then reopened both native saves for matching monochrome renders. [Recorded measurements and STEP hash](evidence/spindle-carrier.json) · [Comparison](../../../gallery/previews/spindle-carrier-revised.png) · [Direct Modeling verification #40](https://github.com/chrizpro/prism/issues/40).

To rebuild, run [BuildSpindleCarrierStep.cs](../../../gallery/BuildSpindleCarrierStep.cs) with `dotnet run --file gallery/BuildSpindleCarrierStep.cs -- <output-folder>` and Prism's native libraries on the search path. Then run [revise_spindle_carrier.py](../../../gallery/revise_spindle_carrier.py) with the same `--folder` and a private desktop `--discovery` path.

### Pump-mount bracket rework (2026-11-20)

A stress test of Direct Modeling on a history-free STEP: turn a plain pump-mount bracket into a visibly different part using only edits to the imported geometry. The [source STEP](../../../gallery/extras/pump-bracket-source.step) is one 63-face solid, 220 × 130 × 150 mm: a chamfered base with R12 corners, a square tower, a Ø96 flange boss with a Ø60 bore and six Ø6.6 holes on a 78 mm circle, two 10 mm triangular gussets, slotted and counterbored feet, two dowels and a cable pocket. Compare the [fresh import](../../../gallery/extras/pump-bracket-imported.prism) with the [revised model](../../../gallery/models/pump-bracket-revised.prism) (history-free, saved from the revised STEP) in the [same-camera comparison](../../../gallery/previews/pump-bracket-revised.png).

The revision is 280 × 130 × 190 mm. The base is wider, with R20 corners and an R1 rim, and the feet moved out with it. The tower is 40 mm taller, tapered 3° and rounded R35 at the top. The flange is raised 25 mm, grown to Ø130 with a 12 mm deeper spigot and chamfered, with a Ø80 bore and eight Ø9 holes on a 106 mm circle. The gussets are 16 mm thick, with R4 roots and an R6 root along the tower. Other changes: longer slots, countersinks in place of counterbores, M8 tapped holes in place of the dowels, two mirrored M10 lifting holes, two Ø14 ports set 28 mm from the ends with Edit Distance, the pocket deleted and "PX-280" engraved on the back. The result is one valid closed solid (187 faces, 1,366,045 mm³, no free edges), and its STEP export reads back identically.

All 37 steps replay from a fresh import and pass their measured checks ([report](evidence/pump-bracket.json)). Some steps needed a workaround. These seven known issues are measured on every run:

- **KNOWN ISSUE:** Move Face and Edit Distance on an end face bounded by corner rounds and a chamfer pull out a tab only as wide as the flat strip (30 mm × 1696 mm² of the 2340 mm² end). The rounds and the chamfer stay behind. Workaround: Remove Fillets, delete the chamfer faces, move the ends, then fillet again.
- **KNOWN ISSUE:** Direct Edit refuses a flat face together with its corner rounds ("whole features or plain faces, not both"), so a rounded end cannot be stretched in one step.
- **KNOWN ISSUE:** A chamfer that runs around a rounded outline is not recognised as a chamfer (Edit Feature: "plain face"), related to [#87](https://github.com/chrizpro/prism/issues/87).
- **KNOWN ISSUE:** Triangular gussets are not recognised as ribs, so Resize cannot change their thickness. Workaround: Move Face on the outer face.
- **KNOWN ISSUE:** Growing a boss (Offset Face, and Edit Feature Resize to a new diameter) fills every hole that passes through it. The extra volume equals the bore inside the boss exactly. Workaround: drill the bore and bolt circle again with Hole.
- **KNOWN ISSUE:** Edit Feature Change type to "Threaded (modeled)" leaves a plain bore at the M8 minor diameter, with no thread. Workaround: the Thread command on that bore.
- **KNOWN ISSUE:** Rotate Face on the base front tilts the face but leaves the R20 corner rounds upright, which leaves a notch at each corner. The step is undone in the replay.

Also seen during the session but not in the replay. Hole with Direction "World Y" on a face whose normal is −Y drills away from the material and reports success while removing nothing; "Automatic / face normal" works. Fillet R2.5 on the gussets' sloped edges failed. Bake history is only in the timeline's right-click menu, and "Remove history" only in the plain context menu, so neither can be run through the automation API.

These worked as intended: Edit Pattern (6 → 8 on a circle, evenly spaced), Move Hole (moves and diameter changes), Edit Feature Move, Resize (slot), Change type (countersink) and Delete (pocket), Push / Pull, Draft, Delete Face healing, Remove Fillets, Mirror Feature (plain and tapped holes), Edit Distance, Thread, Emboss engraving, and Fillet and Chamfer on imported edges. The inward draft refuses 8°, which is correct because the walls would cut through the gussets; 3° clears them.

Rebuild the STEP with [BuildPumpBracketStep.cs](../../../gallery/BuildPumpBracketStep.cs) (`dotnet run --file gallery/BuildPumpBracketStep.cs -- <output.step>`, with Prism's native libraries on the search path). Then run [test_pump_bracket_direct.py](../../../gallery/test_pump_bracket_direct.py) with `--step`, `--out`, `--save` and a private desktop `--discovery` path, and [render_pump_bracket.py](../../../gallery/render_pump_bracket.py) for the comparison. Verified 2026-11-20 on Windows against a private desktop build of this branch: 37 of 37 steps passed and 7 known issues were recorded.

**Order of work:** 1 (recognition), 6 (Edit Distance), 2 (patterns), 5 (history), 3 (the Browser list), then the demo. 4 only after its own proposal.
