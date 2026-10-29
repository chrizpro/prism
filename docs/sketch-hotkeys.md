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
| G | Toggle gizmo setup in 3D, or for an active 2D datum gizmo |
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
