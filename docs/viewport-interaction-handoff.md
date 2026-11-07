# Viewport interaction: handoff for the next agent

Goal set by the owner: **everywhere it is possible, the user should be able to do the job directly in the 3D viewport (click, drag, type on the canvas), not only through the options/properties panel.** The target is a direct, canvas-first feel. The owner calls out sketching first ("knock sketching out of the park"), then feature tools, assembly, animation and the rest.

Stack: C# / Avalonia, OpenCascade kernel in `native/`. Solution `Prism.slnx`; run with `dotnet run --project Prism.Desktop` (stop a running `Prism.Desktop.exe` before rebuilding or the copy step fails). Nothing below is committed; check `git status`.

## What is already done (sketching)

| Feature | Where |
|---|---|
| **Alignment tracking** ("acquire"): rest the pointer ~0.2 s on a point, a line midpoint or a rectangle center and a dashed guide line extends horizontally/vertically; the cursor snaps to it, and clicking adds Horizontal/Vertical constraints when the anchor is a real point. Also lines up with the tool's last click. | `Prism/Core/Sketching/SketchContext.cs` (`Snap`/`SnapCore`, `Hover`, `Acquired`, `TrackAcquired`, `RectangleCenter`, `TrackSegs`, `PointFor`); `Prism/Tools/SketchTools.cs` (`ClickTool` sets `sk.TrackFrom`) |
| **Text tool** that is typed and sized in the viewport: drag a box (height = letter height) or click, type on the canvas, drag the corner dot to resize, drag the frame to move, click away / Ctrl+Enter to place. Placed text is clean geometry (lines + one spline per smooth curve; interior fit points hidden). | `Prism/Tools/SketchTextTool.cs`, `Prism/Core/Sketching/TextOutline.cs` (own TrueType glyf reader, no native code), `Prism/Views/MainWindow.axaml.cs` (`OnKeyDown`/`OnTextInput` route typing to the tool so letters do not fire shortcuts) |
| **Dimension tool**: pick points/line/circle/arc, move to preview, click to place, value box opens in the viewport. Smart mode picks aligned/horizontal/vertical from where the pointer sits; a second line makes an angle. White lines with arrowheads. | `Prism/Tools/SketchEditTools.cs` (`DimensionTool`: `Place`, `Resolve`), `SketchContext.DimArrow`, `BuildDimensions` |
| **Emboss** feature (raised or engraved sketch/text) with a drag arrow for height. | `Prism/Commands/EmbossCommand.cs` |

## Patterns to copy

* **Sketch tools** derive from `SketchTool` / `ClickTool` (`Prism/Tools/SketchTools.cs`): `PointerDown/Move/Up`, `Key`, `BuildOverlay` (draw previews with `Overlay.AddLine/AddPolyline/AddDot/AddLabel/AddTri`), `Panel` for the optional options panel. Snap with `sk.Snap(...)`. Make changes through `sk.Edit(s, "name", () => ...)` so undo and the solver work.
* **Typing into the viewport**: `CadSession.Key("Char:5")`, `"Back"`, `"Enter"` etc. is the key channel; `MainWindow.OnKeyDown` only forwards a few letters/digits. A tool that needs free text must be special-cased the way `SketchTextTool` is (`Editing` flag, `TypeText`).
* **Value entry in the viewport**: `CadSession.RequestInput(prompt, text, callback)` opens the on-canvas box (used by `SketchContext.EditDimension`).
* **Feature commands** (`Prism/Commands/*.cs`, base `Command`/`BodyCommand`): expose `Handles` / `HandleSpec(origin, dir, NumberParam, mul)` to get a draggable arrow that edits a number (see `ExtrudeCommand`, `EmbossCommand`, `Command.cs:193`). Typing a number while a handle shows sets it (`CadSession.TypeIntoHandle`). For richer interaction a command can own an `IViewportTool` (`Command.Tool`, see `FormTools.cs`, `FreeformTools.cs`).
* Ribbon registration is in `Prism/ViewModels/MainViewModel.cs` (`Cmd(...)`, `Tool(...)`); icons in `Prism/Controls/Icons.cs`; scripting hooks in `Prism/Automation/AutomationApi.cs`.

## The audit: where viewport interaction is missing

Refreshed 2026-11-10 by a complete pass over all ~200 sealed command classes in `Prism/Commands/` (every class inspected, not just files that grep for `Handle`); updated 2026-11-11 after the owner-approved implementation of the top nine priority groups. The gizmo standard is `HandleSpec(origin, dir, NumberParam, mul)` — an arrow along a direction whose tip edits the number live; with `Ref`+`Radius` it becomes a turning arc/ring for an angle. `SurfaceHandle`/`EdgeHandle` build arrows from picked geometry. Handles are conditional: most return `[]` until their required selection resolves.

### Already interactive (primary spatial values are draggable)

* **Solids:** Extrude (distance arrows both sides when two-sided, **taper ring**, **to-target offset arrow**), Revolve (angle arc both directions, **symmetric total-angle arc**), Fillet/Chamfer (radius/distance arrow at picked edge, **Fillet end-radius arrow at the edge's end**), Shell (wall-thickness arrow), Move/Rotate/Scale (axis arrows / angle ring / factor arrow), **primitives Box/Cylinder/Sphere/Cone/Torus (dimension arrows on the preview shape + X/Y/Z position arrows via `PrimitiveCommand`)**, Sweep (**end-scale arrow at the profile's rim**).
* **Surfaces:** Offset, Thicken (face-normal arrows); Extend (edge-midpoint arrow).
* **Direct edit:** PushPull (face arrow), Draft (angle arc at hinge), DirectEdit (offset/radius/delta/tilt handles per feature), Hole (placement click/drag + diameter/depth arrows), Emboss (height arrow), **OffsetFace (face-normal arrow), RotateFace (angle arc), MoveFace (axis arrows + tilt ring), MoveHole (axis arrows + diameter arrow on the wall), FeatureEdit (move/copy arrow + resize arrows: diameter/slot width+length/fillet radius), EditPattern (spacing arrows / circular angle ring from the detected pattern)**.
* **Warp/Deform:** WarpCommand (bend side arrow, twist ring, taper rim arrow, stretch end arrow, plus start/end %-of-axis arrows) and DeformCommand (same scheme on curves/surfaces).
* **Patterns:** Linear (spacing arrows), Circular (angle ring + ring spacing), Curve (spacing arrow in spacing mode), Fill (spacing arrow), **ComponentPattern (linear arrows / angular ring around the resolved axis)**.
* **Assembly/animation:** DriveJoints and Animation drag joint DOFs via `JointHandles`; **MoveComponent (3 axis arrows + 3 turn rings about the component's own centre)**.
* **Curves:** CurveFillet/Chamfer (size arrow), OffsetCurve (distance arrow), ExtendCurve (arrow on each free end), **TrimCurve (keep-from/to % arrows along the chord)**, Pipe/FormPipe (radius arrow at each end).
* **Freeform:** FormExtrude (distance + end-scale arrows), FormRevolve (angle ring), plus all cage+sculpt tools.
* **Mesh/section:** MeshOffset (outward arrow), MeshCut (offset arrow along the cut normal), MeshSection (first-position + spacing arrows), Section analysis (offset arrow; custom-plane mode anchors on the picked plane).
* **Images/lights/scan:** PlaceImage/EditImage (full `ImageGizmoTool`), Lights (`LightTool`), Probe, MeshRegion brush, Measure (read-only), ConstructionPlane offset/angle (method-dependent), CalibrateImage (two-point pick), **ContourFlange (width + thickness + bend-radius arrows)**.

### Partial — some spatial numbers still panel-only

* `CurvePatternCommand`: start/end offsets, twist. `FillPatternCommand`: row spacing, margin, layout rotation.
* `ConstructionPlaneCommand`: plane size, three-points coordinates.
* `FeatureEditCommand`: bore/sink/thread dims stay typed (retype is a numeric spec, not a spatial drag).

### Good candidates — spatial numbers, no manipulator (ordered by expected value)

1. **`RibCommand`** — thickness + height arrows.
2. **Minor/optional:** `MateCommand` offset arrow, `ExplodeCommand` 0–1 arrow, `EquationCurveCommand` move arrows, `InsertComponentCommand` place arrows, build-volume dims on `FdmCheck`/`ResinCheck`/`MeshCheck`, ECAD board dims.

### Not applicable (~100 commands, no spatial gizmo meaning)

Tolerances and process values (sew/repair/check/K-factor/mesh quality thresholds), text-driven inputs (table pattern, curve splitting), integer counts, CAE setup (loads/fixtures are vectors-at-location rather than sized geometry — arrows are possible but are a design choice), CAM feeds/speeds/strategies, drawing-space scales, render/print settings, mirror (needs no number), Boolean (slots only), Loft (no NumberParams at all — it is pick-driven). `JointCommand` min/max limits are configuration, not drag targets.

### Open question for the owner

`MoveBodyCommand` is the only command with an explicit `Handles => []` opt-out (`DirectMoveCommands.cs:38`) — likely deliberate because the move axis is constrained by the kept face, but worth confirming rather than assuming it is a bug.

### Rules of thumb for each tool

* Every number in the panel that has a spatial meaning should also be draggable or typeable on the canvas; the panel stays as the exact-entry fallback and mirrors what the canvas shows.
* Show a live preview and a small label with the value near the pointer; Enter/click commits, Esc cancels, right-click backs out.
* Click-to-pick geometry in the viewport instead of requiring a slot to be activated in the panel where possible (hover highlight, then click).
* Reuse snapping and tracking: `SketchContext.Snap` for sketch work.

## Selection, datum gizmo and command search (2026-11-12, proposed pending owner review)

* **Every left-drag selects without arming a mode** (`CadSession.Selection.cs` `TryBeginClickDrag`): a left press always opens a candidate rectangle, like Fusion — it works on an empty scene and over origin planes, which previously swallowed the drag (the reported bug). A release without moving falls back to the normal click, so picks are unchanged and now land on release rather than press. Direction semantics were already correct: left-to-right requires full containment, right-to-left takes whatever it touches (`RegionSelect.Region.Crossing`).
* **Paint select** (`SelectMethod.Paint`, ribbon **Paint** in the Select-method split): the freehand stroke itself is the selector — whatever it crosses is picked (faces, edges, bodies at the current pick level; Through applies). Implemented as `Region.Stroke` with screen-space segment×segment and segment×triangle tests in `RegionSelect.Collect`; the trail is thinned to ≤128 segments for speed.
* **The datum gizmo is global and verified**: `G` (`datum.gizmo`) or the **Datum** ribbon dropdown places `DatumGizmoTool` in plain model view — no sketch or command needed. Its arrows, plane blocks, centre grab and turn rings are draggable, exact values are typeable in the Gizmo panel, Esc hides it. This is the "prepare points/frames for whatever reason" path: the placed frame is a ready reference for tools that take an orientation.
* **Datum Point/Axis/Plane are global too** (`DatumPickTool`, `CadSession.Datums.cs`): each click drops a free-standing datum until Esc/Enter/right-click — on picked faces (normal), edges/curves (direction), datums (copied) or the work plane in empty space. Clicks land under the cursor: origin-axis picks (which report the world origin for selection) no longer teleport placements to (0,0,0). Model datums ride on one vertex-shaped `Datums` carrier object so they draw, pick (points included — `SelFilter.Datum`), list, undo and save exactly like sketch datums; deleting a selected datum removes just that entry. Placement uses `FreeTool`, a mode-less slot behind commands and sketches in `ActiveTool` — no command commit semantics, placements checkpoint one undo step each. Covered by SOL-H086–H092; manual feel pass still open.
* **The ribbon Move tool is the datum gizmo in move mode** (`DatumGizmoTool.MoveMode`, `MoveWithGizmo` in `MainViewModel.Gizmo.cs`): with a selection it binds immediately; otherwise a viewport click binds the picked object or datum (`SelKind.Datum` items are now pickable in object mode). Arrows, plane blocks, rings and typed panel values apply one rigid `Xform` delta to every bound target (`CadSession.TransformTarget`: `Occ.Transform` for breps, `TriMesh.Transformed` for meshes, cage points for forms, frame rewrite for datums, plane+frame for construction planes). One undo step per drag; Esc releases targets, then hides. `TransformCommand` remains in command search. Sketch-kind objects are not whole-object movable (open decision).
* **Command search reaches documentation**: every result row has a **?** button (and F1 while the popup is open) that opens the tool's help card — purpose, numbered steps, tip, and the **Learn More** link which resolves through `ToolDocumentation.LinkFor(toolId)` → `AppSettings.ToolDocumentationBaseUrl` + the tool's id.
* **Every ribbon button resolves a hover topic**: `ForCommand` → tab-keyed `ForButton` → icon topic (`Find`), with `ToolHelpModel.cs` (`FindModel`, `FindUi`) covering modeling, sketch, datum, joint, mesh, form and utility tools. Feature tools get generated before/after previews; action/mode tools declare `NoPreview` (text-only card — a picture would not explain them). SOL-H089 asserts zero uncovered leaf buttons. **Missing full docs**: `ToolDocumentationBaseUrl` is unset — until it is configured, Learn More shows "Online tool guides are coming soon."
* **Model-space labels are engagement-gated** (`CadSession.BuildOneHandle`): command handles draw as bare arrows/rings/dots — the one consistent gizmo look — with no labels at rest. A label appears only for the handle the user is engaging: hover or drag shows the live value, typing shows the entry echo (`{typed}_ mm — Enter`). `_handleIdx` doubles as the hover-tracking index (`PointerMove` picks the first `HandleHit`). Sketch-mode labels/dimensions are unaffected — typed dimensions stay there. Identifier labels that are not values stay (origin plane/axis letters, construction-plane names, loft section numbers, pattern/hole instance indices — they name things rather than read a value). Commands that need a fitting control keep their own handles (e.g. chamfer's per-edge distance arrows) — the generic transform gizmo is not forced onto them.
* **Datum placement verified live (MCP)**: the `datum` automation tool (`arm|finish|gizmo|bind|list`) arms `DatumPickTool`/`DatumGizmoTool` without a sketch; `pointer` events place datums at exact world coordinates — (30,20,0), (50,0.2,0) on the X axis, (−40,−30,0), (10,60,0) all landed exactly, including the formerly broken on-axis click.

## Known caveats / not verified

* The 2026-11-12 selection/datum/search work is build-verified and covered by `SelectionTests.cs` (SOL-H081–SOL-H084); drag-select directions, click-pick and datum placement were exercised live over MCP (the `datum` tool closes the earlier "can't arm menu/free tools" gap). Still open by hand: Paint-select feel, the "?" row in the search popup, and a manual click pass to confirm real-mouse placement matches the automation path.
* The 2026-11-11 batch (items 1–9 of the audit) is build-verified and covered by `Prism.Tests/Headless/Viewport/ViewportHandleTests.cs` (HDL-H001–H020, asserting handle presence, anchors, directions and param binding), but **no handle has been dragged by hand in the app** — drag feel, arrow placement in odd geometry, and handle-vs-panel sync need a manual pass.
* Approximation notes: `TrimCurve` from/to arrows project along the chord (exact for straight segments, approximate on curved ones); `MeshOffset`'s arrow points from the mesh centre toward its bounds as a stand-in for "along the normals"; `FeatureEdit` resize arrows appear only when the picked feature is round, a slot or a fillet.
* Text only reads TrueType 'glyf' fonts (Arial, Segoe UI, Times New Roman, Consolas, Courier New, Verdana, Calibri); OpenType/CFF fonts are not supported.
* Emboss decides raised vs engrave by the Style choice and tries to flip a raised shape that points into the body; behaviour on curved faces is untested.
* The previous tracking acquires points, line midpoints and rectangle centers; polygon/arc centers acquire only when they are real points.
