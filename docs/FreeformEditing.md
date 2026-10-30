# Freeform editing

The Freeform Select group has Object and a Components menu containing Face, Edge and Vertex.
During Edit Freeform these controls, the status-bar modes and the panel's
Select choice use the same component selection. Vertex can also open Edit Freeform
on the selected form. Object mode selects the whole cage for transforms.

The cage uses bright lines with a dark outline for contrast against the surface.
Subdivision triangle lines are hidden while editing; **Show surface triangles**
restores them in an edge display mode. Vertex markers appear in Vertex mode.

Double-click an edge to select its loop, including an open boundary. Double-click
a vertex to select a vertex chain through it, or a quad face to select a strip of
faces. The nearest incident edge or face edge chooses the direction; Shift adds
another loop. Loops stop where poles or non-quad topology prevent continuation.

Crease opens a temporary page in the right panel. Sharpness previews live:
0 removes the crease, fractional values give a gradual crease, and larger values
keep it sharp through more subdivision levels. Apply/Enter keeps the change;
Cancel/Escape restores the prior geometry. Both return to the same editor and
selection. Crease edits are local undo steps until the editor is committed.
The temporary-page mechanism is shared by command panels for future operations.

When another modeling tool requests the options area, unfinished work offers
**Finish and switch**, **Discard and switch**, or **Keep editing**. Reselecting
the current tool preserves its state. Untouched empty tools switch immediately.
Changed inputs are protected even when they cannot yet produce a preview.
Switching between temporary pages resolves only the current page and retains
the parent editor. Leaving the editor for a different main tool explicitly
finishes or discards the whole editor session, including its temporary page.
Escape first dismisses a pending switch request; otherwise Apply/Enter and
Cancel/Escape on a temporary page return to its parent.

## Insert edge loop

Insert edge loop is a sticky tool. Press **Insert Edge** in the ribbon (or **Insert edge loop**
in the panel), then click edges: each click adds a loop across the quads that edge belongs to.
Hovering an edge previews the loop in green. The tool stays on for more clicks until you press
**Esc**, press the ribbon button again, or choose anything else (another action, a selection
mode, a gizmo mode, Crease). **Loops to insert** and **Loop position along the edge** in the
panel control what each click adds; undo removes one click at a time. If edges or faces are
already selected when you start the tool, they get their loop immediately.

## Where each tool lives

Freeform tools are grouped by what they act on, so you can tell where to look:

| Kind | Acts on | Where | Examples |
| --- | --- | --- | --- |
| Create | Nothing yet (or curves) | Ribbon **Create** group; each one is its own command with OK / Cancel | Plane, Box, Sphere, Loft, Sweep, Revolve, Create Face |
| Edit | Selected vertices, edges or faces of an existing form | One editor, **Edit Freeform**. The panel lists every action; the ribbon Modify, Loops, Lock, Components and Symmetry groups are shortcuts to the common ones and are enabled only while editing | Extrude, Insert Edge, Inset, Bevel, Chamfer, Slide, Weld, Crease, Bridge |
| Check | The whole form, without changing it | **Check Quality** | Poles, triangles, stretch, curvature |
| Finish | The whole form, leaving Freeform | **Convert to NURBS** | Solid for CAD operations |

The rule of thumb: if you choose components and then change them, it is part of Edit
Freeform; if it makes a new form, it is a Create command.

### Ribbon tiers inside Edit Freeform

Every action is in the Edit Freeform panel. The ribbon only promotes tools by how often they are used:

| Tier | Where | Tools |
| --- | --- | --- |
| Essential | Own ribbon button | Extrude, Slide, Subdivide (selected faces, or all), Insert Edge, Inset, Select loop/ring (one tool), Bridge, Freeze, Weld, Crease, Mirror |
| Useful, less frequent | **More** dropdown in Modify | Chamfer, Delete, Merge faces, Collapse, Split face, Bevel (support loops), Smooth, Flatten, Fill holes, Thicken |
| Niche | Panel only | Grow, Shrink, Invert, Flood-select, Select open or creased edges and similar helpers |

The ribbon **Select** group (Object, Face, Edge, Vertex) and the panel's Select choice are the
same setting: changing either one, or pressing 1-4, updates the other.

| Shortcut | Action |
| --- | --- |
| Alt+X outside an active editor | Extrude |
| M (Modify) with a selected form | Edit Freeform |
| T / R / S in Edit Freeform | Translate / Rotate / Scale |
| E in Edit Freeform | Extrude cage selection |
| C | Crease page |
| 1 / 2 / 3 / 4 | Vertex / Edge / Face / Object |
| Ctrl+A | Select all components |
| Alt+L / Alt+R | Select edge loop / ring (same tool as the ribbon **Select Loop**, whose Loop/Ring choice is **Edge selection** in the panel) |

These actions are listed under Modeling and Freeform in Preferences and can be
rebound. The preferences keymap layout is unchanged.

## Subdivide and smooth surface

Subdivide opens a panel step: set **Divisions across** and **Divisions down**, watch the preview, then apply or cancel. It splits the selected faces, or every face if none are selected. **Smooth surface** switches the display between the smoothed result and the plain cage faces (**Smoothness** 0).

## Moving or scaling more than one axis

The gizmo's white squares (between the arrows in Move and Scale) drag two axes at once: the square near the X and Y arrows moves in XY, and so on. Holding **Ctrl** while dragging an arrow does the same for the plane across that axis (Ctrl + Z arrow moves XY). The large white-ringed center dot in Move drags freely in the screen plane (all three axes), and the white block at the center in Scale scales uniformly. Rotation stays one axis per ring. Alt bypasses snapping as before.

In Object selection mode, clicking anywhere on the form selects the whole object. The status bar offers Object, Face, Edge and Vertex; the ribbon Select group mirrors it and highlights nothing extra in Object mode.

**Live symmetry placement.** The mirror plane (or radial axis) starts on the world axes. Select an edge loop, a face or points and use **Place symmetry through the selection** to pass it through their center, so a model that is away from the origin mirrors about its own middle. **Put symmetry back on the world axes** undoes it. The position is saved with the form (for example x@25,0,0), and the automation symmetry argument accepts the same text.

With live symmetry on, the cage edges that lie on the mirror seam are drawn in a distinct color; no plane is drawn.
