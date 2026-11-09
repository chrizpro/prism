# Unified sketches: drawing, datums and editing

Create Sketch opens one editor for 2D and 3D work. Enable **3D sketch** to reveal spatial controls. Each drawing plane retains its own constraints and dimensions; Finish Sketch saves them as one editable sketch.

## Two ways to sketch

**Status: Approved by the owner 2026-11-07 for implementation; delivery in progress (see the verification note at the end of this section for what is done).** All open decisions below are answered. Until it is approved, the sections below describe current behavior. If it is approved, the sections that describe Draw flat, the sketch gizmo and Gizmo setup (*Drawing with the gizmo*, the paragraphs on G in *Edit Sketch*, *Persistent editing and placed geometry*, *Locked targets, free 3D editing and saved planes*) are to be rewritten or retired in the same change that delivers it.

**Purpose.** Sketching has grown three overlapping ideas: Draw flat, Draw in space, and a sketch gizmo with its own setup, binding and saved-plane machinery. The proposal keeps the power and drops the overlap: two ways to draw, one way to select, and gizmos, planes and datums handled by the Datum tools like everywhere else. Out of scope: constraints, dimensions, each drawing tool's own options, Project/Include, Offset and Pattern, which stay as described below.

### Choosing how to draw

Two modes, always available, chosen with **radio buttons** in the Sketch right panel (the place Draw flat and Draw in space are chosen today; nothing is added to the ribbon). There is no separate Draw flat. **No plane** is the default.

**No plane (the Plasticity way).** There is no guide plane. A point goes where you click, the way Datum Point does: on the point, curve, edge or face under the pointer, otherwise on the grid plane being looked at. Geometry near the pointer also helps: if a curve or point is close, the point is offered on it (highlighted before the click) so a new curve can connect to it without exact aiming. It works in perspective as well as orthographic views. In an orthographic view square to a world axis the coordinate along the view direction is held fixed, so drawing in the top view keeps Z at the grid plane's value (0 for the default grid) and the front view keeps Y. Snapping to existing geometry helps place the first point of a new curve. Drawing "flat" is simply this mode in an axis-aligned view; it needs no mode of its own, and the view can change between curves (draw in the left view, then in the top view).

**With plane (the Fusion way).** A guide plane follows the click, and its axes and three mini-planes help lock the drawing: resting on a mini-plane and holding **Ctrl** locks drawing to it (unchanged), and hovering along an axis offers an exact directional preview with the **Along X/Y/Z axis** guide (unchanged). Two changes:

- The mini-planes are drawn and hit at **twice their current size**, to find the balance between staying out of the way and being reachable when needed; the factor can be revisited after use. The guide starts where you click, so it is near the pointer rather than only near the origin.
- Finishing a curve ends its plane. After **Enter** finishes a curve, the next click anywhere starts a new curve whose start point and plane come from that click (the "plane you are looking at" rule already in this guide), instead of staying locked to the previous curve's plane. This also makes it possible to start a curve from an existing curve.

Both modes keep the snapping rules in *Drawing in space from other sketches*, *Snapping and constraints* and *Continuing a spline across drawing planes*. Alt still bypasses inference and snapping.

### Selecting in a sketch

With no drawing tool armed the sketch is in selection mode; there is no separate Select tool to find. The pick level decides what a click takes:

- **Object** takes the whole shape: a rectangle is its four sides together, a connected outline is the whole outline. A rectangle is never partly selected in this mode.
- **Edge** takes one curve (one side of a rectangle). **Shift + click** adds or removes further edges.
- **Point** is a pick level of its own, alongside Object and Edge: it takes points, which can be dragged directly.
- Box, lasso and paint selection follow the pick level: Object takes whole shapes, Edge individual curves, Point points. A left-to-right box takes only what is fully inside it, right-to-left takes whatever it touches.

### Leaving tools and the sketch

- **Esc** leaves the current drawing tool (the sketch returns to selection mode). It never leaves the sketch.
- **Enter** finishes a curve in progress, as it does today. With no curve in progress, Enter leaves the sketch (Finish Sketch); the Finish button stays available too. A sketch left by accident reopens with Edit Sketch.
- Where the rest of the application uses Esc and Enter differently is a separate, later audit; this section only fixes the sketch.

### Gizmos, planes and datums

The sketch gizmo's own controls are retired: Gizmo handles (G), Gizmo setup and binding, selection transforms, Free 3D editing, Gizmo follows last point, and the sketch panel's saved planes and datums section. Their jobs move to the Datum tools:

- A gizmo to move or rotate things: place or select a **Gizmo** datum and take it over (see *Standalone datums* in the [construction-planes guide](modeling/construction-planes/README.md#standalone-datums)).
- A plane to draw on or reference: place a **Plane** datum.
- Looking straight at a plane: select it in the Browser and choose **Look At** (N).

The guide gizmo used by **With plane** is not the retired editing gizmo; it only guides drawing and is never used to create planes. It starts where you click.

### Adding points to an existing curve

A way to insert a point into a curve that already exists is required, whichever mode is active. An **Insert point** action adds a point where you click on a curve: a line becomes two lines, an arc two arcs, a circle two arcs, and a spline gains a through-point. The existing Spline points tool already inserts, moves and removes spline control points and stays as it is.

### Saved files

Existing sketches keep opening, including their drawing planes, saved datums and off-plane point offsets (`SkPoint.Z`). Retiring a control must not discard data a file already holds. Document versions follow the rule in *Saving and verification*.

### Owner decisions (2026-11-07)

- Modes are switched with radio buttons in the Sketch right panel; Draw flat was never a ribbon item and goes away with No plane.
- The mini-planes are twice their current size, to be tuned later.
- In No plane a click goes where it points, like Datum Point, with help from nearby geometry.
- In With plane the guide starts where you click.
- Point is its own pick level. Insert point splits arcs and circles as well as lines and splines.
- The guide gizmo only guides; planes come from the Datum tools.

### Decisions on the remaining questions (2026-11-07)

- **Esc and Enter:** Enter finishes a curve in progress and otherwise leaves the sketch; Esc leaves the tool and never the sketch.
- **Remembering the mode:** per document (it is saved with the sketch).
- **"Nearby":** the same screen tolerance as picking an edge today.
- **Splitting a circle:** one click makes two arcs that meet at the clicked point and its opposite.

### Acceptance criteria

1. A new sketch is a plain 2D sketch on its plane (the long-standing behavior, unchanged). A **3D sketch** checkbox in the Sketch panel turns on the two modes, which appear as radio buttons and open in No plane. A NURBS sketch opens as a 3D sketch in No plane. There is no Draw flat choice.
   *Owner direction 2026-11-07.* In a 2D sketch the drawing plane is fixed, so axis rotation is limited to it.
   - In No plane a new shape always lies on the principal plane that faces the view as it is now, through the nearby start point if there is one; there is no hidden plane left over from earlier clicks.
2. In No plane, a click on an existing point, curve, edge or face places the point there; otherwise it lands on the grid plane being looked at. A curve or point near the pointer is offered and highlighted before the click, and clicking connects to it.
3. In No plane in the top view, every placed point has the same Z; in the front view, the same Y. After the view changes to another axis view, the next curve uses that view's fixed coordinate.
4. In No plane in a perspective view, drawing works and the first point of a curve can snap to existing geometry.
5. In With plane the guide starts where the first click was made; the mini-planes are twice their previous size; resting on one and holding Ctrl locks drawing to it.
6. In With plane, after Enter finishes a curve, the next click anywhere starts a new curve whose plane comes from that click; a curve can be started from an existing curve's point.
7. With no drawing tool armed, a click selects; at Object level it selects the whole shape, at Edge level one curve, at Point level one point; Shift + click adds more.
8. Box, lasso and paint selection in a sketch honor the pick level, including selecting a whole rectangle at Object level, a single side at Edge level and only the corners at Point level.
9. Esc ends a drawing tool and leaves the sketch open. Enter finishes a curve in progress; with none in progress it finishes the sketch, and the sketch can be reopened with Edit Sketch.
10. The sketch has no Gizmo handles, Gizmo setup or Free 3D editing controls; a Gizmo or Plane datum placed from the Datum tools can do the jobs they covered.
11. A file saved with drawing planes, saved datums and off-plane points before this change opens with the same geometry.
12. A point can be inserted into an existing line, arc, circle and spline.

### Implementation and verification links

Code areas: [`SketchSpace`](../Prism/Core/Sketching/SketchSpace.cs), [`SketchContext`](../Prism/Core/Sketching/SketchContext.cs), [`SketchEditTools`](../Prism/Tools/SketchEditTools.cs) (Select tool, pick levels), [`SketchInsertPointTool`](../Prism/Tools/SketchInsertPointTool.cs), [`SketchPanel`](../Prism/Views/SketchPanel.axaml). Tests: [`SketchRegionSelectTests`](../Prism.Tests/Headless/Sketch/SketchRegionSelectTests.cs) (`SKT-H031`–`SKT-H036`, pick levels and region selection), [`SketchKeysTests`](../Prism.Tests/Headless/Sketch/SketchKeysTests.cs) (`SKT-H029`–`SKT-H030`, Esc and Enter), [`InsertPointTests`](../Prism.Tests/Headless/Sketch/InsertPointTests.cs) (`SKT-H037`–`SKT-H041`), [`SpaceSplineTests`](../Prism.Tests/Headless/Sketch/SpaceSplineTests.cs) (`SKT-H028` With plane, `SKT-H042` No plane).

**Delivery status (2026-11-07).** Verified by headless tests only; none of it has been tried by hand in the running application.

| Criterion | State |
| --- | --- |
| 1. 2D by default, 3D opens in No plane, radio buttons, no Draw flat | Done: new sketches are plain 2D again; the **3D sketch** checkbox reveals the **No plane** / **With plane** radios and starts in No plane. NURBS sketches start as 3D in No plane. In No plane every new shape lies on the principal plane facing the view (SKT-H044). |
| 2–4. No plane placement | Done for Point, Line and Spline: a new shape that begins on a point or curve of another sketch, another drawing plane or a body begins at its real position, and carries on in the plane through it. The nearby offer uses the existing snap tolerances. Other tools (rectangle, circle...) still project such snaps onto the view plane. |
| 5. With plane: guide starts at the click, squares ×2, Ctrl lock | The squares are twice their size (`PlaneSquareScale`). Ctrl lock is unchanged. The guide starts where the first click was made only when that click is on existing geometry (see 6); otherwise it stays where it was. |
| 6. A new curve takes its plane from the click | Done for Point, Line and Spline starting on geometry off the current plane, in any view. |
| 7–8. Pick levels, box/lasso/paint | Done: **Object**, **Edge** and **Point** in the Select group. Point is also exclusive of curves at its own level. |
| 9. Esc / Enter | Already behaved this way; now covered by tests. |
| 10. Gizmo controls retired | Partly: the G hotkey, "Gizmo follows last point", "Free 3D editing" and the sketch panel's saved planes and datums section are removed from the interface. The sketch Datum ribbon group and **Save gizmo** are removed too (owner decision 2026-11-07: datums are made outside a sketch); the engine that stores datums in saved sketches stays so old files open unchanged. Moving, rotating and scaling a selection now goes through the Sketch tab's **Transform** button (Modify group): the gizmo sits on the selected points or curves with no bind step, and the Gizmo panel takes typed location, rotation and per-axis scale ([SKT-H045](../Prism.Tests/Headless/Sketch/SketchTransformTests.cs)). With the scale operation (S) the gizmo shows a block at each axis tip: dragging one scales the selection along that axis only, and the center square scales evenly ([SKT-H046](../Prism.Tests/Headless/Sketch/SketchTransformTests.cs)). Bind/Unbind is not part of this update (owner decision 2026-11-07). |
| 11. Old files open | Unchanged data model; every existing sketch test passes. |
| 12. Insert point | Done: lines, arcs, circles and splines (ribbon **Insert Point**, and a button in the Sketch panel). Ellipses are not split. |

**Still open.** (a) *Decided:* datums are placed outside a sketch; inside one the Datum buttons only say so. (b) The guide gizmo in With plane still starts at the origin of the sketch's first plane until a click on geometry moves it. (c) The older sections of this guide still describe the retired gizmo workflow and need rewriting.

## Drawing with the gizmo

The large reference grid keeps the plane selected when entering the sketch as its anchor — origin and orientation included — while the view is perspective. An orthographic view snapped squarely to a world axis instead shows the grid of the plane being looked at (front view → the front plane), whether drawing flat or in space, sketching or not; the entry anchor returns when the view leaves the snapped axes. Switching drawing planes, following the cursor, choosing datums, transforming geometry and undo do not move the anchor. The finite gizmo plane preview still shows the active drawing frame. Grid snapping continues to use the active drawing plane; the fixed background grid is a visual reference. Starting another sketch captures a new grid anchor.

Choosing a plane does not change the camera. New and reopened sketches default to orthographic projection while preserving the current viewing direction and target. Only explicit view commands such as N / Look normal align the camera; Create Sketch also offers an opt-in initial alignment checkbox. Gizmo plane targets, plane shortcuts, saved datum selection and the drawing-plane picker are available in gizmo edit mode. Outside that mode, clicks pass through the gizmo planes to the drawing/selection tool, and rotation/scale handles cannot intercept ordinary drawing clicks.

- The gizmo follows the last placed point by default. Clear **Gizmo follows last point** to pin it. Following does not move existing curves or invalidate pending shapes.
- Enter **Gizmo handles (G)**, then click a plane square, or use **Gizmo plane** in the panel. The planes are local to the gizmo orientation. Shift-Z/Y/X choose the world XY/XZ/YZ directions through its origin.
- **N / Look normal** aligns the view to the selected gizmo plane. Press again after alignment to reverse. Right-drag or middle-drag orbits; entering a sketch does not lock the camera. The initial alignment is opt-in in Create Sketch.
- Hover along an axis while drawing for an exact directional preview and an **Along X/Y/Z axis** guide. Placing a point establishes a perpendicular support plane if needed; hovering alone does not create one. Click an arrow to lock direction, or use **Axis / Draw along axis** and **Release drawing axis**. Alt bypasses inference and snapping.
- **G** reveals gizmo handles without discarding an unfinished shape. Press G again to resume the current drawing tool; E switches to Edit Sketch. Drag center/arrows/plane squares to move, rings to rotate, or the scale corner to resize. **W/R/S** choose Move/Rotate/Scale in gizmo mode. Ctrl temporarily enables drag handles; choosing a plane requires G. The exception is Ctrl that is holding a drawing-plane lock (rest on a plane square, then hold Ctrl): clicks on the gizmo then draw, so the next curve can start right at the gizmo, and a right or middle drag orbits the view instead of panning without releasing the lock (Shift still pans). [SKT-H023–H024](../Prism.Tests/Headless/Sketch/PlaneLockTests.cs). Escape cancels a drag; Ctrl-Z undoes it.
- The right panel exposes absolute location in millimeters, Euler rotation in degrees (world X/Y/Z order), and uniform scale from 0.25 to 8. Enter or Set applies a group. Location/rotation each offer **Reset 0**, scale **Reset 1**, and Reset gizmo resets all groups.

Use the normal Sketch ribbon for drawing and constraints; the 3D panel does not duplicate drawing tools. Rectangle supports two corners, center and three-point methods. Width, Tab, height, Enter commits exact dimensions without finishing the sketch. Choose a different plane to continue drawing; existing curves remain on their original support. The **Existing drawing planes** list returns to earlier geometry.

## Datum references and saved planes

The ribbon's **Datum** dropdown groups Point, Plane, Axis and Gizmo. **Save this plane as datum** lives only in the right options panel, alongside matching datum actions and an optional name. Generic Delete buttons have been removed from the ribbon; use Delete or the contextual menus. The mechanical Hole icon uses the blue modeling palette. Curvature combs start disabled and can be enabled explicitly.

Select a gizmo plane, then **Save this plane as datum**. This snapshots its origin, orientation and display scale. Save as many planes, axes, points or complete gizmos as needed. Choose a reference under **Datum references** to reuse it. Only one reference is active; others appear as small references. Choose **Live gizmo** to detach from saved references.

**Update selected datum snapshot** explicitly replaces its saved frame; moving or following does not silently overwrite it. Datums survive Finish Sketch, Save, Open and Edit Sketch, including datum-only sketches. Their origins participate in optional external snapping.

## Edit Sketch

In 2D use Select directly, or choose a saved Datum gizmo for group transforms; in 3D choose **Edit** in the Sketch tab's Select group. Press **E** or select a point or curve, Shift-click to add, box-, lasso- or paint-select, or double-click an edge to select its connected shape. The live gizmo centers on the selection. Choose **Spline points** to insert, move or remove control points; the tool explains those gestures.

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

**Snap to other sketches and curves** is optional and on by default. It includes visible model curves, other sketches and other drawing planes in this sketch. Hidden objects and the old saved representation of the sketch being edited are excluded. Drawing in space, Point, Line and Spline keep an off-plane snap at its real depth (see *Drawing in space from other sketches* below). Otherwise off-plane candidates are rejected unless **Project off-plane curve snaps** is enabled; projected snaps are explicitly labeled. External snaps place sampled positions rather than associative references. Constraints and dimensions operate within each support plane. A spline continued onto another drawing plane stays one curve whose later points sit off its home plane (see *Continuing a spline across drawing planes*); its constraints still act within the home plane.

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

Maintained headless checks: [grid-plane rules](../Prism.Tests/Headless/Viewport/GridPlaneTests.cs) (`GRID-H001`–`GRID-H004`) cover the orthographic snapped-view grid, the perspective entry-plane anchor and the off-axis flat-sketch case; [sketch selection and edit](../Prism.Tests/Headless/Sketch/SketchEditTests.cs) (`SKT-H017`/`SKT-H018`) cover clicking a curve on an inactive drawing plane to switch to it and pick or bind it.

## Workflow references


## Simplified edit entry

**Edit** is a ribbon tool in the Sketch tab's Select group, alongside Select and its Box / Lasso / Paint method split; the former right-panel Select/Edit controls are retired. In 2D use Select for direct point/curve editing. In 3D, G enters gizmo setup: select targets, then press G to leave setup manually or Enter to center and confirm. Gizmo and datum labels are omitted from the viewport; names and coordinates remain in options. Create Sketch already supports selecting a planar surface face, using its position and orientation. Drawing directly on a curved face is not supported.

## Persistent editing and placed geometry

The 3D option accordions start collapsed. Edit stays the active sketch tool until another ribbon tool is picked. Gizmo Handles toggles setup independently and leaves Edit active when turned off. Selecting a point in setup immediately places the gizmo there; Shift-selection computes the mean of the selected defining points. Geometry stays unchanged until setup is exited. Enter is optional for choosing the center.

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

With Gizmo Handles off, the gizmo stays locked to the points it was bound to: clicking empty space does nothing. Clicking a curve drawn on another drawing plane switches to that plane and binds what is under the pointer in the same click — Select and Edit Sketch both treat inactive-plane curves as clickable (they also highlight on hover), so everything visible can be picked and edited without first changing the drawing plane. Shift-click still only adds targets on the active plane. Inside Gizmo Handles the same rule applies to points: clicking a point on another drawing plane switches to it and binds the gizmo there.

**Free 3D editing (not limited to the plane)** in the right panel lets the gizmo lift points off their drawing plane. Each point keeps an offset along its plane's normal (`SkPoint.Z`); constraints and dimensions still act on the in-plane position. Lines, splines, arcs and circles display and export with the offset. Sketches that use it save as document version 3. With the option off, out-of-plane moves are refused as before.

Saved planes, axes, points and gizmos are listed under **Active sketch** in the model tree while editing and under the sketch afterwards. Selecting a row in the sketch being edited makes it the active reference; right-click a saved plane on a finished sketch for **Create sketch on this plane**. The right panel's **Saved planes & datums** section is open by default and always enabled, with **Save gizmo plane as datum**.

## Continuing a spline across drawing planes

Approved by the owner on 6 November 2026. While Spline has placed points in Draw in space, switching to another gizmo plane (resting on a plane square, holding Ctrl to lock it, or any other plane switch) does not finish the curve. The next clicks land on the new plane and continue the **same spline**. It belongs to the plane it was started on; points placed on later planes are stored as offsets along that plane's normal (`SkPoint.Z`), as Free 3D editing does. The last point is shared, not doubled, and the preview bends through every plane. Enter, double-click or right-click finishes it as one curve and one undo step. Constraints still act within the home plane, so later off-plane points are plain points. Line keeps its existing behaviour: a new line starts at the shared point on the new plane. [SKT-H019–H020](../Prism.Tests/Headless/Sketch/SpaceSplineTests.cs).

## Drawing in space from other sketches

Approved by the owner on 6 November 2026. In Draw in space, Line, Spline and Point snap to visible points and curves of other sketches, of other drawing planes in the same sketch, and of bodies, even when they lie off the active drawing plane. The placed point keeps its real 3D position, stored as an offset along the drawing plane's normal (`SkPoint.Z`); the drawing plane itself stays where it is, and the gizmo moves to the new point as it does after any placement, so the next plane chosen from its squares passes through it. Example: a spline started on a circle from a separate 2D sketch begins exactly on that circle. Drawing flat keeps projecting such snaps onto the view plane. Sketch points are found where they show on screen, at their real position, before anything else: this reaches points that sit off their own plane (the later points of a spline continued across planes, or points lifted by Free 3D editing) and works even when the drawing plane is edge-on. A point of the active plane is reused, so the new curve shares it; a new shape started on a point in a view that changes the drawing plane gets that plane through the point. [SKT-H021–H022, SKT-H027](../Prism.Tests/Headless/Sketch/SpaceSplineTests.cs), [SKT-H026](../Prism.Tests/Headless/Sketch/PlaneLockTests.cs).

## Starting a new shape on the plane you are looking at

Owner direction, 6 November 2026. Create Sketch starts a 2D sketch after choosing its plane; only the NURBS K flow starts in 3D. Drawing in space, finishing a shape (Enter), cancelling it (Esc) or changing tools is the cue that the next shape may go on a different plane: its first click lands on the gizmo plane that faces the view when the view is snapped to an axis (within about 8°) or the current drawing plane has turned close to edge-on (within about 15°). A casually tilted view keeps the current plane, and a shape in progress, a Ctrl plane lock or a hovered plane square always decide the plane instead. Draw flat already follows snapped views the same way. [SKT-H025](../Prism.Tests/Headless/Sketch/PlaneLockTests.cs).
