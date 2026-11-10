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

Enable **Snap gizmo transforms** to set move, angle and scale increments. Hold Alt during a drag to bypass snapping. Scale uses square handles, including the diagonal square for uniform scale. Gizmo preferences are shared across sketch, freeform and control-point editing and are remembered between sessions. Axis colors identify directions; active highlights follow the theme accent.

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

**Transform** is one dropdown with Move, Rotate, Scale, Mirror, and Duplicate. It shows the tool you used last. **Inspect** keeps Measure visible, with Properties and Section Analysis under **More inspection**.

Assembly component positioning and sketch editing retain their own semantics. They are not interchangeable with transforming a standalone body.
