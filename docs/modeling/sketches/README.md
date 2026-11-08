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

## Grease pencil

**Approved scope (owner conversation, 2026-11-08):** freehand brainstorming annotations in both 2D and 3D sketch. The owner revised the initial 2D-only request to allow planar drawing in 3D sketch, and confirmed that drawing requires orthographic view while completed marks remain visible in perspective.

In the contextual Sketch tab, find **Brainstorm** immediately left of **Exit**, choose **Grease Pencil** from its Pencil/Eraser dropdown, then drag with the left mouse button. Use **Use orthographic view** in the tool panel if needed. The default surface is the active sketch plane. Enable **Draw on view-facing plane** to draw on a plane facing the camera through the active plane origin. Each stroke captures its plane at pointer down, so orbiting does not turn a stroke into a spatial curve. Parallel rays cannot place a mark; choose a view-facing plane or look toward the drawing plane.

Stroke width is 1–12 screen pixels (default 3). The owner approved five organization colors on 2026-11-08: **red, blue, green, yellow and purple**. Choose one in the tool panel; yellow is the default for new strokes. Each stroke retains its own color through saving, cloning, undo and perspective display. Earlier uncolored strokes retain their legacy amber appearance. Marks do not snap. Release commits one undoable stroke; Escape discards the unfinished stroke. Choose **Eraser** from the same dropdown to click or drag a 12-pixel-radius brush over whole marks on the active sketch section. A swept drag erases strokes crossed between pointer events; one erase gesture is one undo step. Erasing requires orthographic view and leaves CAD geometry alone. **Remove last mark** removes the newest mark on the active sketch section and is undoable. Switching tools discards an unfinished stroke. A click without movement creates no mark. Switching to perspective during a stroke cancels that stroke.

Marks are stored separately from sketch points, entities and constraints. They survive sketch cloning, definition serialization and export, and a sketch containing only marks is retained on Finish. Their overlay is visible during editing and after Finish, including perspective view. They do not create profiles for extrusion, solver constraints, snapping targets or exported CAD curves.

Implementation: [Grease pencil tool](../../../Prism/Tools/GreasePencilTool.cs), [sketch definition](../../../Prism/Core/Sketching/SketchModel.cs) and [maintained Unit checks](../../../Prism.Tests/Unit/Sketch/GreasePencilTests.cs). The checks cover pointer routing, independent annotations, undo/removal, canceled strokes, orthographic drawing, perspective display, cloning/serialization a frozen view-facing plane in 3D sketch, ribbon ordering and undoable eraser gestures.

Limits: no pressure sensitivity, custom color picker, partial-stroke erasing, curve conversion or individual-mark selection/transforms. Marks are local to their sketch section; geometry-edit tools operate on CAD entities rather than marks. Native Finish/file reopening and desktop viewport appearance require verification on a configured Prism workstation.

Verification (2026-11-08): `/workspace/dotnet/dotnet test Prism.Tests/Unit/Prism.UnitTests.csproj --no-restore --filter FullyQualifiedName~GreasePencilTests --verbosity minimal` compiled the application and passed all eleven maintained Unit cases (11 passed, 0 failed), including all five palette colors, swept erasing with undo, and ribbon ordering. The Headless runner was attempted but aborted before tests because this Linux environment lacks `PRISM_NATIVE_DIR` / `libprism_kernel`; no native viewport evidence is claimed. GitHub issue publication was blocked by automatic approval review, so issue tracking remains pending explicit owner authorization.

Desktop build investigation (2026-11-08): `dotnet build Prism.Desktop/Prism.Desktop.csproj --verbosity minimal` and `dotnet build Prism.Desktop/Prism.Desktop.csproj --no-restore -c Release --verbosity minimal` both succeeded with zero errors. Existing compiler warnings and a transitive `Tmds.DBus.Protocol` advisory remain. The owner-reported Rider failure has not reproduced; its first error and branch are needed to diagnose it.

Documentation merge-gate repair (2026-11-08): GitHub run 42439535027 failed its reference-catalog check because the generated catalog omitted the new ribbon group. Regenerated `site/public/ribbon-catalog.json` from the shipped ribbon. The .NET catalog check, `site` documentation build and `web` website build all passed locally (Astro telemetry disabled in this sandbox). No merge conflict with the current `develop` branch was found.
