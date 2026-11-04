# Direct Modeling tab

Edit the geometry you already have, with no history needed. It works on any exact body, including parts imported from STEP, IGES or BREP.

**Opening it:** on the Solid tab, the **Direct Edit** section has one button, **Direct Edit**. It opens this tab the way Create Sketch opens the Sketch tab, and **Finish Editing** (far right) goes back.

**Adding things stays on the Solid tab.** Hole, Fillet, Chamfer, Shell, Draft and Split Body are there. This tab is for changing what exists; to remove a hole, fillet or boss, select it and press **Delete**.

The tab reads left to right: **Select, Datum, Edit, Repair, Transform, Inspect, Appearance, Exit**.

**Status key:** *Done* works and is covered by tests. *Thin* works but is only tested on simple parts. *Gap* has a known limitation.

## The gizmo

There is no button for it. It is open whenever the tab is open. Click a face, hole, slot, pocket, boss or fillet (one click picks the whole feature) and handles appear on it:

| Handle | Colour | What it does |
|---|---|---|
| Move arrows (X, Y, Z) | red, green, blue | Move the selection. A hole, slot, pocket or boss moves as a whole feature; flat faces slide and the body stretches to follow. |
| Pull | orange | Moves every picked face along its own normal. A positive value moves faces out of the part (a boss grows, a hole gets smaller). |
| Tilt arc | purple | Tilts a single flat face about its own X direction. |
| Rim handle | teal | On a round hole or boss, sets the radius. |

Drag a handle, or type a number while it is showing. Only one handle can be used per edit; use another for the next one. The same results are available from the older tools (Offset Face, Move Face, Rotate Face and Edit Feature's move), which the gizmo hands the work to.

## History

Direct edits are plain undo steps by default. Right-click a body (in the viewport or the Browser) and choose **Track history** to record its direct edits as editable steps. Features made with the Solid tab (Extrude, Revolve and the rest) keep recording history as before.

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
- [Standalone DirectModelTests](../../../tests/Prism.DirectModelTests/) contain feature recognition, edit actions, direct gizmo, face operations, repair and pattern checks. [Shared Solid tests](../../../Prism.Tests/Headless/Solid/) cover the entry, exit and shared controls.

### Option groups to verify

- Edit Feature offers Move, Resize, Copy, Delete and Change type. Hole conversions include Plain, Counterbore, Countersink and modeled Threaded, with side, size and thread controls.
- Edit Pattern adjusts spacing and count in two directions, angular step, and individual instance removal. Mirror Feature uses a picked plane or face.
- Check and Repair separates inspection from healing. It exposes tiny size, sewing gap, healing, tiny/sliver removal and closing a sealed surface into a solid.
- Fill / Cap offers Automatic, Flat and Smooth patch with a joint tolerance. The current maintained cases use a planar opening for each mode.

Remaining maintained work includes Edit Feature action and type choices, smart-selection scope and gizmo handles, cylindrical and conical Match Face, pattern modes and Mirror Feature, Repair, nonplanar caps and joint tolerances, and rejection, undo and history cases. Desktop interaction remains deferred.
