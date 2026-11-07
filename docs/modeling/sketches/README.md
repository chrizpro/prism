# Sketch entry and reference points

Create Sketch starts on XY, XZ, YZ, a construction plane, or a planar face. Offset moves the base plane along its normal. A picked planar face supplies the actual drawing plane. Datum Point activates the reference-point tool while a sketch is open and places a point on its active plane.

While a command is asking for a plane, origin planes and hidden construction planes appear for picking, and their browser eyes show checked to match. Turning an eye off suppresses that plane until the command ends; turning off a plane that was already switched on hides it for good, like any object eye. A new pick reveals suppressed planes again. Settings → Interaction → **Reference plane size** scales how big all of these planes draw (25–200 %); pick bounds follow the drawn size. [SOL-H078–H080](../../../Prism.Tests/Headless/Solid/SketchAndDatumTests.cs) cover the reveal, browser sync, suppression lifetime and scaled pick bounds.

## Verification

[Sketch entry and point tests](../../../Prism.Tests/Headless/Solid/SketchAndDatumTests.cs) and [offset sketch entry](../../../Prism.Tests/Headless/Solid/SharedToolTests.cs). [Tracking issue #33](https://github.com/chrizpro/prism/issues/33).

## Coverage gaps

Sketch tool families within the contextual Sketch tab have separate workflows. Desktop snapping and visual feedback remain open. New requirements need owner review.

## Sketch contextual tab

Sketch has 49 shipped ribbon entries. Several tools expose more modes and controls in the sketch panel than the ribbon catalog lists.

| Area | Tool families |
| --- | --- |
| Select and Datum | The same Select group as every other tab (Object, Face ▾, Paint ▾, Invert, Through): Object arms the sketch Select tool, the drag methods set its rectangle, lasso or paint selection, and the body-only controls are unavailable in a sketch ([SKT-H015](../../../Prism.Tests/Headless/Sketch/SketchEditTests.cs)); the sketch tab has no Datum group |
| References | Project / Include, To construction, Construction |
| Draw | Line, [Rectangle](rectangle/README.md), Ellipse, Arc, Polygon, Slot, Text, Spline |
| Modify | Edit, Trim, Extend, Offset, Fillet, Chamfer, Mirror, Pattern |
| Constrain | Horizontal, Vertical, Coincident, Parallel, Perpendicular, Tangent, Equal, Midpoint, Concentric, Collinear, Symmetric, Fix |
| Dimension and display | Smart, Horizontal and Vertical dimensions, Curvature |
| Exit | Finish Sketch, Cancel |

The [maintained Sketch suite](../../../Prism.Tests/Headless/Sketch/) covers Rectangle, Line, Ellipse, Arc, Polygon and Spline modes. Existing standalone [Sketch 2D](../../../tests/Prism.Sketch2DTests/Program.cs), [Sketch Space](../../../tests/Prism.SketchSpaceTests/Program.cs), [sketch panel](../../../tests/Prism.SketchPanelWorkflowTests/Program.cs) and [solver](../../../tests/Prism.SketchConstraintTests/) checks provide additional evidence. [Contextual coverage issue #38](https://github.com/chrizpro/prism/issues/38).

Remaining option-level work includes drawing modifiers and numeric fields, Slot and Text controls, Project / Include modes, sketch modifications, each constraint's allowed picks and solve/failure cases, dimension modes, and contextual finish/cancel. The existing executable suites cover some of these paths. Record their evidence separately from maintained coverage.

## Editing, constraints and dimensions

Offset copies the selected geometry at a specified distance. Horizontal and Vertical constraints align a line or two selected points. Dimension offers Smart, Horizontal and Vertical modes. Smart uses the selection and label position to choose the measurement.

[Maintained edit tests](../../../Prism.Tests/Headless/Sketch/SketchEditTests.cs) exercise a single-line offset, both line axis constraints, and all three dimension modes on an oblique line. [Issue #43](https://github.com/chrizpro/prism/issues/43) tracks remaining modification modes, pick combinations, and invalid inputs.


Desktop integration: [SKT-I001 viewport evidence](../../integration-evidence/README.md#sketch-constraints--skt-i001) covers Horizontal and Vertical glyphs and a driving line dimension.
