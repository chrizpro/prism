# Sketch entry and reference points

Create Sketch starts on XY, XZ, YZ, a construction plane, or a planar face. Offset moves the base plane along its normal. A picked planar face supplies the actual drawing plane. Datum Point activates the reference-point tool while a sketch is open and places a point on its active plane.

## Verification

[Sketch entry and point tests](../../../Prism.Tests/Headless/Solid/SketchAndDatumTests.cs) and [offset sketch entry](../../../Prism.Tests/Headless/Solid/SharedToolTests.cs). [Tracking issue #33](https://github.com/chrizpro/prism/issues/33).

## Coverage gaps

Sketch tool families within the contextual Sketch tab have separate workflows. Desktop snapping and visual feedback remain open. New requirements need owner review.

## Sketch contextual tab

Sketch has 40 shipped ribbon entries. Several tools expose more modes and controls in the sketch panel than the ribbon catalog lists.

| Area | Tool families |
| --- | --- |
| Select and Datum | Select with box or lasso, Gizmo, Point, Axis, Plane, Save gizmo |
| References | Project / Include, To construction, Construction |
| Draw | Line, [Rectangle](rectangle/README.md), Ellipse, Arc, Polygon, Slot, Text, Spline |
| Modify | Trim, Extend, Offset, Fillet, Chamfer, Mirror, Pattern |
| Constrain | Horizontal, Vertical, Coincident, Parallel, Perpendicular, Tangent, Equal, Midpoint, Concentric, Collinear, Symmetric, Fix |
| Dimension and display | Smart, Horizontal and Vertical dimensions, Curvature |
| Exit | Finish Sketch, Cancel |

The [maintained Sketch suite](../../../Prism.Tests/Headless/Sketch/) covers Rectangle, Line, Ellipse, Arc, Polygon and Spline modes. Existing standalone [Sketch 2D](../../../tests/Prism.Sketch2DTests/Program.cs), [Sketch Space](../../../tests/Prism.SketchSpaceTests/Program.cs), [sketch panel](../../../tests/Prism.SketchPanelWorkflowTests/Program.cs) and [solver](../../../tests/Prism.SketchConstraintTests/) checks provide additional evidence. [Contextual coverage issue #38](https://github.com/chrizpro/prism/issues/38).

Remaining option-level work includes drawing modifiers and numeric fields, Slot and Text controls, Project / Include modes, sketch modifications, each constraint's allowed picks and solve/failure cases, dimension modes, and contextual finish/cancel. The existing executable suites cover some of these paths. Record their evidence separately from maintained coverage.
