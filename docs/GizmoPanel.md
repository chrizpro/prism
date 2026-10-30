# Shared Gizmo panel

Open **Gizmo** from the shared **Datum** ribbon dropdown, or use the
**Gizmo…** launcher in Edit Form, Edit Control Points or the sketch panel.
The panel occupies its own right-side tab and retains the current editor.
Done restores that editor without committing its geometry.
Crease is launched from the ribbon or its shortcut; it is no longer duplicated
as an action in Edit Form.

The panel provides Move/Rotate/Scale, visibility of handle families and individual
X/U, Y/V and Z/N axes, labels, sketch plane handles, screen size, picking tolerance
and snapping increments. Hidden handles cannot intercept pointer events.
Appearance and snapping are shared preferences.

Choose **Place gizmo without moving geometry** to position its pivot with the
arrows or numeric X/Y/Z fields, and choose Rotate to orient its axes. Select
the target cage components, control points or sketch geometry before choosing
**Bind to selection and return**. Enter also binds when this panel is active.
There must be a selection; an empty target is rejected without leaving placement.
After binding, the handles transform the selection in the placed frame.
To change the target, open Gizmo again, choose placement, adjust the frame and
selection, then bind again. Selection changes during placement leave the pivot
where it was positioned.

Point and Axis in the Datum dropdown currently create references in either 2D or
3D sketches and are available while a sketch is active. Standalone model datum
points and axes are not yet available.

Cancel placement, Escape, or leaving the panel without binding restores the
prior pivot. Placement itself does not deform geometry. In Freeform and NURBS,
changing the selection releases the previous binding. These pivot frames belong
to the current editing session; they are not saved as document datums.

Sketch placement uses the sketch's existing reference-frame and constraint
machinery. Binding preserves the independently placed origin and orientation;
the normal sketch editing path continues to enforce constraints.

## Datum gizmo in model view

When no editor or sketch is active, the Gizmo ribbon button or the **G** shortcut shows a free-standing datum gizmo. The ribbon places it at the world origin; the shortcut places it at the pointer when it is over the viewport, otherwise at the origin. Drag an arrow or plane block to move it, or a ring to rotate it; the Gizmo panel offers exact location and rotation. It never changes geometry. **Esc** or **Hide gizmo** removes it, and opening an editor or sketch hides it until you return to model view.
