# Sketch hotkeys

Canonical catalogue: [SketchShortcuts.cs](../Prism/Core/SketchShortcuts.cs). Update the catalogue whenever shortcuts change.

| Gesture | Action |
| --- | --- |
| L | Line |
| R | Rectangle; while placing a two-point/center rectangle, rotate its live preview |
| C | Ellipse with circle proportions |
| S | Spline |
| A | Arc |
| D | Dimension |
| Shift | Hold axis direction for lines, splines, arc directions, slot centers and polygons; square rectangles; circle ellipses |
| G | (retired in sketches; see [Two ways to sketch](unified-sketch.md#two-ways-to-sketch)) |
| W / R / S with a gizmo target | Move / rotate / scale from the center handle |
| E | Toggle geometry editing in 3D |
| Delete | Delete selection |
| Ctrl+Z | Undo |
| Alt | Bypass snapping |
| Tab | Next numeric field |
| Enter | Accept numeric entry / finish current curve |
| Escape | End current drawing, retaining placed geometry |

## Shift by drawing step

- Line and midpoint line: nearest horizontal/vertical endpoint direction.
- Fit/control spline: next point aligned with the previous point.
- Polygon (both methods): center-to-vertex or center-to-side orientation.
- Slot: direction between the two centers; width stays free.
- Three-point/tangent arc: start-to-end direction; the bulge stays free.
- Center arc: center-to-start and center-to-end directions.
- Two-point/center rectangle: equal sides (square), aligned to the sketch axes. Release Shift for the chosen Orientation; R rotates the preview.
- Three-point rectangle: first-side direction; width stays free.
- Ellipse: circle proportions in every method, preserving its existing behavior.

Release Shift to resume free placement. Single-point tools have no previous-point direction to lock. Shift-add selection in Select/Offset and other selection steps remains unchanged.

K starts drawing immediately. From the NURBS tab it resumes the shared NURBS Curves sketch. Spatial Select enables geometry editing directly. XY/XZ/YZ and Look normal are available on the sketch ribbon. In 3D, Point snaps at the actual depth of visible reference sketches; these are copied positions, not associative constraints.

In NURBS, the shared sketch survives renaming and enables free depth editing. In Loft, choose **End at sketch point** to taper a profile to a visible standalone sketch point. Point endpoints currently require an open loft without guide rails.

The Sketch panel offers **Draw flat** and **Draw in space** above the active tool settings. New NURBS sketches default to Draw flat. In Draw in space, **Free 3D editing** allows points to leave their plane; the choice is saved and hidden while drawing flat. Camera navigation does not change drawing mode. Plane selection remains on the gizmo, and Look At remains in the viewport toolbar.

Draw flat controls new-geometry placement independently of point-editing gizmos. New sketches outside NURBS default to Draw in space. Finish the active curve, then use Escape or Select and click a point to bind its editing gizmo; this also works while Draw flat is selected. Existing NURBS definitions saved in the earlier planar-only mode retain flat placement and regain point gizmos when reopened.

Unrestricted 3D editing defaults on for new sketches and is labeled explicitly above the active tool settings. The Add points to a curve action remains available during drawing and editing; click a spline to insert a point at its spatial depth. Draw flat hides the drawing-plane sheet while retaining point-editing handles.

The panel label is **Free 3D editing (Not limited to the plane)**. Select is the idle state: clearing a tool, pressing Done, or ending a pending curve with Escape, Enter or right-click returns directly to Select and keeps placed geometry.
