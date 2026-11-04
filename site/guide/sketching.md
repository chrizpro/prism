# Sketching

Most parts start as a sketch: a profile on a plane that features such as Extrude and Revolve give depth. See the [Sketch ribbon reference](/tools/sketch) for every sketch tool.

## Start a sketch

1. Choose **References › Create Sketch** on Home, Solid, Surface or another modeling ribbon.
2. Pick a plane (XY, XZ, YZ), a construction plane, or a flat face of a body.
3. The contextual **Sketch** ribbon appears and the view turns to face the plane.

Press <kbd>N</kbd> to look straight at the sketch again after orbiting. Orbiting changes only the view; new geometry stays on the active plane.

## Draw

| Key | Tool |
| --- | --- |
| <kbd>L</kbd> | Line |
| <kbd>R</kbd> | Rectangle (while placing, <kbd>R</kbd> rotates the preview) |
| <kbd>C</kbd> | Circle (ellipse with circle proportions) |
| <kbd>A</kbd> | Arc |
| <kbd>S</kbd> | Spline |
| <kbd>D</kbd> | Dimension |

While drawing, dimensions appear beside the pointer. Type a value, press <kbd>Tab</kbd> for the next field and <kbd>Enter</kbd> to accept. Hold <kbd>Shift</kbd> to lock a direction: horizontal or vertical lines, square rectangles, circular ellipses. Hold <kbd>Alt</kbd> to bypass snapping. <kbd>Esc</kbd> ends the current curve and keeps what you placed.

Each tool shows numbered, collapsible option sections in the Sketch panel. Choose a **Method** where a tool offers variants (for example 2-point, center or 3-point rectangles), follow the drawing prompt, then adjust settings. Collapsing a section keeps its values.

## Constrain and dimension

Select geometry first, then click a constraint: Horizontal, Vertical, Coincident, Parallel, Perpendicular, Tangent, Equal, Midpoint, Concentric, Collinear, Symmetric or Fix. Press <kbd>D</kbd> and pick entities to add a dimension; double-click a dimension to change its value.

The **Sketch status** in the panel shows the remaining degrees of freedom. Blue geometry can still move; white geometry is fully constrained. Drag geometry with **Select** to see what is still free.

Click a constraint symbol to select it and press <kbd>Delete</kbd> to remove it.

## Edit

Trim, Extend, Offset, Fillet, Chamfer, Mirror and Pattern are under **Modify**. **Project / Include** brings edges of existing bodies into the sketch as references. **To construction** turns curves into construction geometry that guides the sketch without becoming part of the profile.

## 3D sketches

Tick **3D sketch** in the Sketch panel to draw curves in space for sweep paths, pipes and loft rails. **Draw flat** keeps new geometry on the active plane; **Draw in space** with **Free 3D editing** lets points leave the plane. Press <kbd>G</kbd> to set up the gizmo and <kbd>E</kbd> to toggle geometry editing.

## Finish

Choose **Finish Sketch**. A closed profile can become a solid; open curves are useful as paths, rails or surface boundaries. To change a sketch later, double-click it in the browser or the design history.
