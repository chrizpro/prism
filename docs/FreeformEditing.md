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

| Shortcut | Action |
| --- | --- |
| E outside an active editor or sketch | Extrude |
| Tab with a selected form | Edit Freeform |
| W / E / R in Edit Freeform | Move / Rotate / Scale |
| Alt+X | Extrude cage selection |
| C | Crease page |
| 1 / 2 / 3 / 4 | Vertex / Edge / Face / Object |
| Ctrl+A | Select all components |
| Alt+L / Alt+R | Edge loop / ring |

These actions are listed under Modeling and Freeform in Preferences and can be
rebound. The preferences keymap layout is unchanged.
