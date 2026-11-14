# Sketches, solids, and surfaces

<GuideImage image="solid-extrude-preview" />

## Sketch on a plane

Use **References → Create Sketch**. Choose a plane or face, draw your profile, and add dimensions and constraints. A closed profile can become a solid; open curves are useful as paths, rails, or surface boundaries.

The contextual **Sketch** ribbon appears while you edit. Its references, drawing, modification, constraint, and dimension tools follow a consistent section order. Finish Sketch returns to modeling.

All sketch tools use the same numbered, collapsible option sections. Choose a **Method** when a tool offers variants, follow the geometry or selection instructions, adjust settings, then use the placement or apply action. Numeric fields appear when the corresponding drawing step is ready.

- **Rectangle:** choose 2-point, center, or 3-point; place the anchor; enter the live dimensions; adjust orientation where supported; create the rectangle.
- **Ellipse:** choose its construction method, place the center or first endpoint, set the major axis and minor radius. Lock proportions to draw a circle.
- **Offset:** select curves, finish the selection, enter a distance and choose a side, then apply. Chain selection can collect connected curves.

The same presentation also covers lines, arcs, circles, polygons, slots, splines, points, trimming, extending, sketch fillets and chamfers, mirror, pattern, projection, dimensions, constraints, and geometry or spline editing. Collapsing sections does not discard their values. Live preview refreshes preserve partially typed values; invalid numeric input displays feedback beside the field.

## Adjust the gizmo

Choose **Move**, **Rotate**, or **Scale** in the editor. By default, the gizmo shows handles for that operation. Expand **Gizmo display and snapping** to choose **Combined handles** or **Custom visibility**, adjust screen size and pick tolerance, and show or hide labels and sketch plane controls. Combined handles let you switch operations by picking a different handle.

Enable **Snap gizmo transforms** to set move, angle and scale increments. Hold Alt during a drag to bypass snapping (with the Move tool, Alt also drags a copy; see [Pick levels, select all and groups](#pick-levels-select-all-and-groups)). Tools that place or position something, such as primitives, Move Face, Move Hole and Insert Component, use the same gizmo for their position, rotation and scale numbers; sizes such as a hole's diameter keep their own drag handles. Scale uses square handles, including the diagonal square for uniform scale. Gizmo preferences are shared across sketch, freeform and control-point editing and are remembered between sessions. Axis colors identify directions; active highlights follow the theme accent.

## Build solid features

Home and Solid share a **Create** section. Primitive shapes occupy one menu, and **Features** contains Extrude, Revolve, and other tools for solid features. Sweep and Loft are available on Surface, Freeform, and NURBS. Modify contains rounding, beveling, and hollowing tools. Combine groups Union, Subtract, and Intersect.

Select a body, face, or edge before starting a tool when it needs a target. Confirm the preview rather than assuming that the current selection is the intended input.

The command panel follows numbered, collapsible steps. Revolve uses **Profile → Axis → Extent → Result**; choose a world axis or select an axis line in step two. Extrude starts with the profile, then its extent and result. Each section can be collapsed without losing its inputs.

### Select individual edges or chains

Extrude and Revolve offer **Selection intent** in the profile picker:

- **Whole profile** selects a sketch, curve object, or face.
- **Single edges** lets you choose and remove individual sides.
- **Connected chain** follows connected edges and stops at branch junctions.
- **Tangent chain** follows smooth joins, stopping at corners and junctions.
- **Partial loop** takes a first and last edge. **Other path** switches to the other route around a simple loop.

To extrude three sides of a rectangle, use **Extrude Surface**, choose **Single edges**, and pick three sides. A solid extrusion needs a closed profile; selecting an open chain does not create a solid. Chain selection is also available in other tools whose pickers accept multiple edges. The expanded edge selections are stored with editable features.

### Pick levels, select all and groups

The **Select** group on every modeling ribbon sets what a click takes: **Object**, **Face**, **Edge** or **Vertex** (the corner points of solids, surfaces and curves). Drag a rectangle or lasso to take several; a drag from left to right takes only what lies inside it, and a drag from right to left takes whatever it touches. **Through** includes what is hidden behind other geometry, and **Invert** swaps the selection.

- **Select all:** <kbd>Ctrl</kbd>+<kbd>A</kbd> selects everything at the current pick level. In a tool's pick slot it fills the slot with everything the slot accepts.
- **Same Color, Adjacent, Linked:** with a face selected, add every face of that body with the same color, the faces that touch the selection along an edge, or every face of the body.
- **Isolate faces:** with faces selected, **Isolate** (View) shows only those faces and hides the rest of their bodies and every other body. It is only a way of looking: nothing in the document changes, and **Show All** brings everything back.
- **Group:** select two or more objects and press <kbd>Ctrl</kbd>+<kbd>G</kbd> (or use **Group** on the ribbon, or right-click in the browser). The browser shows them under one parent row whose eye hides or shows them together and whose click selects them all. <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>G</kbd> takes them out; the objects are never deleted.
- **Copy and paste:** <kbd>Ctrl</kbd>+<kbd>C</kbd> remembers the selected objects and <kbd>Ctrl</kbd>+<kbd>V</kbd> pastes independent copies in the same place, selected and ready to move, in this or another open document. To copy while you move, hold <kbd>Alt</kbd> as you press an arrow, plane block or the centre of the **Move** gizmo: the originals stay and the copies move.
- **Four Views:** hover a pane and press <kbd>Space</kbd> to maximize it, and again to return.

See [Keyboard shortcuts](/reference/hotkeys) for every key.

### Navigate the object tree

The browser shows a collapsible document tree with source sketches nested below the body that uses them. Imported or unassociated geometry remains in type folders. Searching a nested object retains its ancestors. The branch lines follow the accent color selected in **Preferences**, including changes made while the app is open.

## Shape surfaces and freeforms

**Surface** works with profile-based surfaces, patch filling, trimming, continuity, and conversion. **NURBS** adds curve and control-point operations. **Freeform** works with subdivision cages, edge loops, creases, and conversion to CAD geometry.

Each ribbon keeps specialized tools while sharing the same Transform and Inspect sections. A converted form may require geometry checks before subsequent exact modeling operations.

## Section View

In **Section View**, choose **Movable plane** to start at the center of the selected bodies or, with no selection, the visible bodies. Drag the travel arrow along the plane normal. Drag the grips on the two rings to tilt the plane around its starting center. The size handle changes the displayed rectangle while the cut continues across the whole model. Numeric controls offer the same adjustments. The world planes and a selected planar face or construction plane are also available. Section View changes the viewport and reports the slice area and outline without modifying geometry.

The owner requested the movable plane on 2026-11-09. [Issue #101](https://github.com/chrizpro/prism/issues/101) · [SEC-H001](https://github.com/chrizpro/prism/blob/develop/Prism.Tests/Headless/Section/SectionPlaneTests.cs).

SEC-H001 passed in the Windows Headless runner. It checked a 600 mm² cut through an off-origin box, pointer-driven travel, the oblique overlay, and the unchanged cut when resizing the displayed plane.

## Position and inspect

**Transform** is one dropdown with Move, Rotate, Scale, Mirror, and Duplicate. It shows the tool you used last. **Scale** takes one factor for an even resize, or **Scale type: Per axis (stretch)** for separate X, Y and Z factors, with an arrow on each face of the selection to drag them. The stretch follows the world axes, so a part turned off-axis is sheared, and curved faces become freeform surfaces. **Inspect** keeps Measure visible, with Properties and Section Analysis under **More inspection**.

Assembly component positioning and sketch editing retain their own semantics. They are not interchangeable with transforming a standalone body.
