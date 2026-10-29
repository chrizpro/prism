# Unified sketches: drawing, datums and editing

Create Sketch opens one editor for 2D and 3D work. Enable **3D sketch** to reveal spatial controls. Each drawing plane retains its own constraints and dimensions; Finish Sketch saves them as one editable sketch.

## Drawing with the gizmo

The large reference grid stays on the plane selected when entering the sketch, including that plane's origin and orientation. Switching drawing planes, following the cursor, choosing datums, transforming geometry and undo do not move it. The finite gizmo plane preview still shows the active drawing frame. Grid snapping continues to use the active drawing plane; the fixed background grid is a visual reference. Starting another sketch captures a new grid anchor.

Choosing a plane does not change the camera. New and reopened sketches default to orthographic projection while preserving the current viewing direction and target. Only explicit view commands such as N / Look normal align the camera; Create Sketch also offers an opt-in initial alignment checkbox. Gizmo plane targets, plane shortcuts, saved datum selection and the drawing-plane picker are available in gizmo edit mode. Outside that mode, clicks pass through the gizmo planes to the drawing/selection tool, and rotation/scale handles cannot intercept ordinary drawing clicks.

- The gizmo follows the last placed point by default. Clear **Gizmo follows last point** to pin it. Following does not move existing curves or invalidate pending shapes.
- Enter **Gizmo handles (G)**, then click a plane square, or use **Gizmo plane** in the panel. The planes are local to the gizmo orientation. Shift-Z/Y/X choose the world XY/XZ/YZ directions through its origin.
- **N / Look normal** aligns the view to the selected gizmo plane. Press again after alignment to reverse. Right-drag or middle-drag orbits; entering a sketch does not lock the camera. The initial alignment is opt-in in Create Sketch.
- Hover along an axis while drawing for an exact directional preview and an **Along X/Y/Z axis** guide. Placing a point establishes a perpendicular support plane if needed; hovering alone does not create one. Click an arrow to lock direction, or use **Axis / Draw along axis** and **Release drawing axis**. Alt bypasses inference and snapping.
- **G** reveals gizmo handles without discarding an unfinished shape. Press G again to resume the current drawing tool; E switches to Edit Sketch. Drag center/arrows/plane squares to move, rings to rotate, or the scale corner to resize. **W/R/S** choose Move/Rotate/Scale in gizmo mode. Ctrl temporarily enables drag handles; choosing a plane requires G. Escape cancels a drag; Ctrl-Z undoes it.
- The right panel exposes absolute location in millimeters, Euler rotation in degrees (world X/Y/Z order), and uniform scale from 0.25 to 8. Enter or Set applies a group. Location/rotation each offer **Reset 0**, scale **Reset 1**, and Reset gizmo resets all groups.

Use the normal Sketch ribbon for drawing and constraints; the 3D panel does not duplicate drawing tools. Rectangle supports two corners, center and three-point methods. Width, Tab, height, Enter commits exact dimensions without finishing the sketch. Choose a different plane to continue drawing; existing curves remain on their original support. The **Existing drawing planes** list returns to earlier geometry.

## Datum references and saved planes

The ribbon's **Datum** dropdown groups Point, Plane, Axis and Gizmo. **Save this plane as datum** lives only in the right options panel, alongside matching datum actions and an optional name. Generic Delete buttons have been removed from the ribbon; use Delete or the contextual menus. The mechanical Hole icon uses the blue modeling palette. Curvature combs start disabled and can be enabled explicitly.

Select a gizmo plane, then **Save this plane as datum**. This snapshots its origin, orientation and display scale. Save as many planes, axes, points or complete gizmos as needed. Choose a reference under **Datum references** to reuse it. Only one reference is active; others appear as small references. Choose **Live gizmo** to detach from saved references.

**Update selected datum snapshot** explicitly replaces its saved frame; moving or following does not silently overwrite it. Datums survive Finish Sketch, Save, Open and Edit Sketch, including datum-only sketches. Their origins participate in optional external snapping.

## Edit Sketch

In 2D use Select directly, or choose a saved Datum gizmo for group transforms; in 3D choose **Edit Geometry** from the right panel. Press **E** or select a point or curve, Shift-click to add, box-select, or double-click an edge to select its connected shape. The live gizmo centers on the selection. Choose **Spline points** to insert, move or remove control points; the tool explains those gestures.

Rectangle corners are visible point handles. Drag a corner to reshape with constraints while keeping its opposite corner fixed. Box-select a rectangle or double-click an edge to select the connected shape, then drag an already-selected edge to translate the entire selection. A click on a selected edge retains the other selected curves. Each completed drag is one undo step.

**G** enters **Gizmo setup**. Handles and numeric fields now reposition, rotate or resize only the gizmo, even with geometry selected. Geometry stays in place. Click a point, Shift-click several points or curves, or box-select; press **Enter / Lock gizmo to selection**. A single point anchors the gizmo there; multiple targets place it at their selection center. Enter exits setup without finishing the sketch. The chosen orientation is retained.

Outside gizmo setup, Edit Sketch's arrows, rotation rings and scale handle transform the selected target. The panel says **Selection transform** and offers numeric equivalents. W/R/S choose move/rotate/scale when a 3D edit selection exists. Press G whenever you want to adjust only the gizmo again, then G to return with the same target, or Enter to bind a new target. Choosing a saved datum in setup can establish a different pivot while retaining the selection.

A confirmed point anchor follows subsequent direct point edits. Changing the geometry selection establishes a new target. Selecting every defining point of a line or complete outline also targets the complete curve/shape, so it can be moved spatially. Plane display size and target geometry scale are separate: enlarging the planes during setup does not change the geometry's scale or its scale-one identity. Constraints remain in effect and invalid transforms roll back.

Complete independent shapes can move and rotate to arbitrary planes while retaining local constraints. An independent subset becomes another drawing plane in the same sketch. Individual points and geometry linked to unselected curves must satisfy their current planar constraints; conflicting or out-of-plane point transforms are refused and restored. This is not a general spatial point constraint solver.

## Ellipse and circle methods

One **Ellipse** tool offers **Center**, **2-Point**, and **3-Point** methods in both the viewport bar and right panel. Toggle **Lock proportions to a circle**, press **L**, or hold Shift:

- Center: center and radius endpoint.
- 2-Point: opposite diameter endpoints; numeric fields become Diameter and Angle.
- 3-Point: three points on the circumference.

Unlocked methods retain center/axes, bounding-box corners, and major-axis endpoints/minor radius behavior. C opens this same tool with circle proportions locked. Saved circles and legacy automation remain compatible.

## Project / Include

Choose **Project**, **Body**, **Intersect**, or **Pierce points** in the options. Project copies selected curves onto the active plane; Body projects all body edges; Intersect returns exact crossing geometry; Pierce points includes only isolated curve-plane crossings.

For a top-plane circle crossed through its center by a side plane: activate the side plane, choose **Project / Include > Pierce points**, and click the circle. The result is two fixed point references, without a flattened line. Curves on other drawing planes of the same open sketch, external sketch curves, and body edges are supported. Hover previews show intersections before import. **As construction** excludes references from feature profiles.

The crash was caused by fixed point references being interpreted as angular dimensions during overlay rendering. New point imports and legacy fixed references are now handled safely.

References are snapshots without associative source updates. This pass implements projection, body projection, sections and isolated pierce points. Additional tools remain unimplemented, including Include 3D Geometry, Project to Surface, Intersection Curve or Iso Curve.

## Offset and grid snap

Offset starts with source selection. Shift-click adds or removes highlighted curves; Chain selection can collect a connected outline. Press Enter or **Done selecting** when ready. Only then does movement preview the offset. Move to the desired side and click, or type a distance and press Enter. The panel offers Distance, Flip side and Apply. **Change selection** or Shift-click returns to selection. Applying is refused while selection remains active; one undo removes the multi-curve result.

**Grid Snap** is in the floating viewport toolbar alongside Grid and the view controls. It has been removed from the ribbon and controls the same snapping setting as before.

## Snapping and constraints

**Snap to other sketches and curves** is optional and off by default. It includes visible model curves and other drawing planes in this sketch. Hidden objects and the old saved representation of the sketch being edited are excluded. Off-plane candidates are rejected unless **Project off-plane curve snaps** is enabled; projected snaps are explicitly labeled. External snaps place sampled positions rather than associative references. Constraints and dimensions operate within each support plane. A single constrained spline remains planar; spatial paths can use pieces across planes.

## Inspector tabs

**Panels** is at the upper right beside Settings. Its persistent checkbox picker lets you enable multiple inspectors as separate right-side tabs. Sketch and active command controls retain a contextual tab. Switching inspectors preserves the sketch, pending drawing and command. Uncheck a panel to remove its tab. The sketch inspector scrolls as a whole, with active drawing-tool options first and Finish Sketch always accessible.

## Saving and verification

Multi-plane sketches and saved datums use document version 3, preventing older editors from silently discarding them. Ordinary single-plane files without datums remain version 2. This editor reads versions 1, 2 and 3. Non-construction curves export to exact kernel geometry; extrusion of a selected tilted region follows that region's normal.

Run focused regression checks from the workspace root:

```powershell
dotnet run --project tests/Prism.SketchSpaceTests -c Release
dotnet run --project tests/Prism.ProjectionTests -c Release
```

Coverage includes plane transitions, typed dimensions, constraints, persistence, datum selection/pivots, whole-shape transforms, undo/cancel, axis inference, circle methods, staged offsets, external snapping, normal views, window shortcuts and inspector tabs. Native review captures are in artifacts/sketch-interaction-review; the packaged desktop preview is in artifacts/sketch-space-preview.

## Workflow references


## Simplified edit entry

Edit Geometry is available in the right options panel only in 3D. It is no longer a ribbon tool. In 2D use Select for direct point/curve editing. In 3D it always enters gizmo setup first: select targets, then press G to leave setup manually or Enter to center and confirm. Gizmo and datum labels are omitted from the viewport; names and coordinates remain in options. Create Sketch already supports selecting a planar surface face, using its position and orientation. Drawing directly on a curved face is not supported.

## Persistent editing and placed geometry

The 3D option accordions start collapsed. Edit Geometry is a persistent toggle in the right panel; its active styling remains until turned off or another drawing tool is selected. Gizmo Handles toggles setup independently and leaves Edit Geometry active when turned off. Selecting a point in setup immediately places the gizmo there; Shift-selection computes the mean of the selected defining points. Geometry stays unchanged until setup is exited. Enter is optional for choosing the center.

Placed geometry is retained when changing tools, ending with Escape, changing a drawing method, or finishing the sketch. Pending splines with at least two points are committed; incomplete shapes retain their placed points as editable point entities. Retained results can be undone. Explicitly cancelling an entire sketch still discards that sketch.

Leaving Gizmo Handles manually binds the selected targets just as confirmation does. Bound gizmo handles take priority over overlapping point handles. Center drags of point targets stay in their sketch plane, avoiding rejected off-plane moves in oblique views; axis handles retain their explicit direction and constraints remain enforced.

Translation arrows accept drags along their shafts as well as their tips. Translation arrows always move geometry regardless of the last Move/Rotate/Scale dropdown choice; rotation rings rotate and the scale corner scales. This prevents missed shaft clicks from falling through into box selection and clearing the target.

## 2D sketch audit

In 2D, Edit Geometry is hidden: Select directly picks/drags curves and points. Grid Snap is a toggle in the floating viewport toolbar, alongside Grid and the view controls. Two-point and center rectangles expose an Orientation value (degrees) in their drawing options; negative angles and zero are accepted. Three-point rectangles continue to set orientation from their first side. Hold Shift while placing a line endpoint to constrain it to the nearest horizontal or vertical direction; release Shift for a free direction. The shared catalogue is [sketch-hotkeys.md](sketch-hotkeys.md), backed by Core/SketchShortcuts.cs.

Deleting a rectangle removes orphan corners and its internal center diagonal, including when construction is hidden. Live center constraints in other rectangles are preserved. Retained standalone points can be deleted explicitly; the origin remains a fixed reference. Degenerate rectangles retain their input points without creating zero-length edges. Axis and gizmo datum references stay in 2D, can be selected through the Datum references list, and axes can be picked along their visible shafts.

The focused 2D audit can be run with `dotnet run --project tests/Prism.Sketch2DTests -c Release`. It covers viewport controls, rotated rectangles, Shift locking, shared endpoint snapping, shape/control-point deletion, Datum reference selection, degenerate rectangles, ellipse/arc/slot/polygon geometry and staged offsets.

## Consistent 2D tool workflow

Drawing methods, numeric inputs and stage instructions live in the right Sketch panel. The upper-left tool controls have been removed; the floating viewport toolbar retains view controls and Grid Snap. Width/height, length/angle, polygon sides, fillet radius, pattern count and dimension values are available beside the drawing. Enter a field and use Place point or Apply as appropriate.

Two-point and center rectangles can be oriented numerically at any angle. After placing their first point, move to preview a size, press R (or Rotate preview), move to set the orientation, then click to place. Three-point rectangles use their first side to establish the orientation. Hold Shift for the existing square/axis lock behavior.

In 2D, Datum planes can have a normal offset. Finish the sketch to select a saved plane as a Create Sketch support, or a saved axis as the custom axis for Revolve. Select a Datum gizmo while editing a 2D sketch to enter planar target setup. Pick points or curves, Shift-click to add, then press G to bind the selection. Arrows translate, the normal ring rotates, and the scale corner scales the selection. W/R/S change the center handle operation. These transforms keep the sketch in 2D and respect its constraints.

Pattern accepts source selection after activation. Click the curves to include, choose Done selecting (or Enter), then place the copies with the live preview. Change selection returns to the selection stage.

For fillet/chamfer, select the first line and hover the second for a preview. Press on the second line and drag to size the result, then release to apply, or use the right-panel size and Apply. Smart Dimension lets you drag a line outward to preview the extension lines; release, set its driving value in the right panel and Apply. Its label placement survives save/open.

Run the interaction checks with `dotnet run --project tests/Prism.SketchConsistencyTests -c Release`.

## Locked targets, free 3D editing and saved planes

With Gizmo Handles off, the gizmo stays locked to the points it was bound to: clicking other points, curves or empty space does nothing. Press G to pick a new target. Inside Gizmo Handles, clicking a point on another drawing plane switches to that plane and binds the gizmo there; Shift-click only adds targets on the active plane.

**Free 3D editing (not limited to the plane)** in the right panel lets the gizmo lift points off their drawing plane. Each point keeps an offset along its plane's normal (`SkPoint.Z`); constraints and dimensions still act on the in-plane position. Lines, splines, arcs and circles display and export with the offset. Sketches that use it save as document version 3. With the option off, out-of-plane moves are refused as before.

Saved planes, axes, points and gizmos are listed under **Active sketch** in the model tree while editing and under the sketch afterwards. Selecting a row in the sketch being edited makes it the active reference; right-click a saved plane on a finished sketch for **Create sketch on this plane**. The right panel's **Saved planes & datums** section is open by default and always enabled, with **Save gizmo plane as datum**.
