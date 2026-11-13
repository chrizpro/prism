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

* **Solids:** Extrude (distance arrows both sides when two-sided, **taper ring**, **to-target offset arrow**), Revolve (angle arc both directions, **symmetric total-angle arc**), Fillet/Chamfer (radius/distance arrow at picked edge, **Fillet end-radius arrow at the edge's end**), Shell (wall-thickness arrow), Move/Rotate/Scale (**the shared gizmo**: arrows / three rings / centre block, see [Command gizmo](#command-gizmo-2026-11-12)), **primitives Box/Cylinder/Sphere/Cone/Torus (dimension arrows on the preview shape; position on the shared gizmo at the shape's middle via `PrimitiveCommand.Gizmo`)**, Sweep (**end-scale arrow at the profile's rim**).
* **Surfaces:** Offset, Thicken (face-normal arrows); Extend (edge-midpoint arrow).
* **Direct edit:** PushPull (face arrow), Draft (angle arc at hinge), DirectEdit (shared gizmo for the move; pull, radius and tilt handles per feature), Hole (placement click/drag + diameter/depth arrows), Emboss (height arrow), **OffsetFace (face-normal arrow), RotateFace (angle arc), MoveFace (shared gizmo + tilt ring), MoveHole (shared gizmo + diameter arrow on the wall), FeatureEdit (move/copy arrow + resize arrows: diameter/slot width+length/fillet radius), EditPattern (spacing arrows / circular angle ring from the detected pattern)**.
* **Warp/Deform:** WarpCommand (bend side arrow, twist ring, taper rim arrow, stretch end arrow, plus start/end %-of-axis arrows) and DeformCommand (same scheme on curves/surfaces).
* **Patterns:** Linear (spacing arrows), Circular (angle ring + ring spacing), Curve (spacing arrow in spacing mode), Fill (spacing arrow), **ComponentPattern (shared gizmo for the spacing or the axis point, angle ring around the resolved axis)**.
* **Assembly/animation:** DriveJoints and Animation drag joint DOFs via `JointHandles`; **MoveComponent and InsertComponent (the shared gizmo: arrows and rings for Move Component, arrows for Insert Component)**.
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

## Handle grip rules (2026-11-12)

* **A value of zero still has a grip.** Sliding handles (`Ref == null`) draw their arrow at the value's position, but when that is shorter than `CadSession.HandleStubPixels` (36 px) the grip sits on a stub along `+Dir` (or `-Dir` for a small negative value), drawn with a tinted shaft. Before this, Move X/Y/Z arrows at 0 collapsed onto the origin dot and only the first one could be grabbed: the owner's "some axis direction seemed unresponsive" on Move Component and Transform → Move. `CadSession.HandleGrip` is the drawn and hit-tested point; `HandleTip` is still the value's true position.
* **Drags are relative.** Pressing on an arrow remembers the offset between its value and the pointer's position along the axis, so dragging the shaft or the stub moves the value by exactly the pointer's travel instead of jumping to the pointer. Turning handles turn by how far the pointer went round as well (see below).
* **Turning handles.** A turning handle without stops is a full ring (`CadSession.TurnTrack`), grabbable anywhere on it, and a drag turns the value by how far the pointer went round, so grabbing the ring a quarter turn from the grip does not jump the value. A turning handle with stops (`ShowRange` and limits, such as a joint) keeps the arc of the stretch it may take, stops at each limit and says "limit" there.
* **Only the hot handle is highlighted and labelled.** Hovering or dragging one handle no longer brightens the others.
* **The nearest grip wins.** When several grips are within reach of the pointer (`GizmoSettings.Tolerance` × 2 px), `CadSession.HandleAt` takes the closest one, not the first in the list.
* Maintained checks in [ViewportHandleTests](../Prism.Tests/Headless/Viewport/ViewportHandleTests.cs): HDL-H021 (labels), HDL-H022 (relative drag), HDL-H024 (zero-value stub), HDL-H029 (ring without stops), HDL-H031 (ring with stops). Dragged live over MCP on Transform → Move (Y and Z arrows at 0 moved 20 and 15 mm, before Transform moved to the shared gizmo) and on the datum Move gizmo (X, Y, Z arrows on a part).

## Command gizmo (2026-11-12)

Owner decision, 2026-11-12: **the gizmo transforms anything that has a position; feature sizes keep one consistent handle style.** Extrude keeps its arrow. The plan and the decision are in [NURBS, Freeform and viewport-interaction plan](engineering/nurbs-freeform-viewport-plan.md#decisions).

* **How a command asks for it.** `Command.Gizmo` returns a `GizmoSpec(Center, Move[3], Turn[3], Scale, …)`: the parameter each arrow, ring and the centre block edits, with `null` hiding a handle. `CadSession.CommandGizmo.cs` draws it with the same `GizmoVisual` as the Move tool, so size, tolerance and snapping come from the gizmo settings. Size handles stay in `Command.Handles`.
* **A gizmo appears only when the command is about moving something.** Move, Rotate, Scale, Move Face, Move Hole, Direct Edit and Move Component open with it. Commands that create or insert something (shapes, Insert Component) keep it off until the user asks for it with the "Move with gizmo" check (`HDL-H032`).
* **Commands on the gizmo now:**

  | Command | Gizmo sets | Size handles kept |
  |---|---|---|
  | Box, Cylinder, Sphere, Cone, Torus | Position X / Y / Z, shown at the middle of the shape **only when "Move with gizmo" is on** (off by default, owner direction 2026-11-13: a gizmo while inserting a shape is in the way) | Width, depth, height, radii, anchored on the shape and clear of the gizmo arrows |
  | Transform → Move | Move X / Y / Z | none |
  | Transform → Rotate | The angle, on three rings. Pressing a ring makes its axis the turn axis and starts again from 0 | none |
  | Transform → Scale | The factor, on the centre block | none |
  | Move Face | Move X / Y / Z at the face | Tilt ring |
  | Move Hole | Move X / Y / Z at the hole | Diameter arrow |
  | Direct Edit | Move X / Y / Z at the middle of a flat face or the feature centre | Pull arrow, tilt ring, rim radius |
  | Move Component | Move X / Y / Z and Turn X / Y / Z | none |
  | Insert Component | Position X / Y / Z, only when "Move with gizmo" is on (off by default) | none |
  | Component Pattern | Linear: spacing X / Y / Z, shown where the first copy lands. Circular: the point the axis passes through | Circular: the angle ring |

* **One control per direction.** In Direct Edit the pull arrow leaves the middle of a flat face along its normal. While it is showing and the normal is a world axis, the gizmo hides its own arrow for that axis, so two arrows never lie on top of each other. Once the face is being moved the pull arrow hides and the gizmo arrow comes back.
* **Who takes the press.** A size handle's own grip beats the gizmo, on press and on hover. Everywhere else the gizmo beats handle shafts and rings. This keeps a radius grip reachable when it sits inside the gizmo, and keeps the gizmo arrows reachable when a size arrow's shaft crosses them.
* **Keys.** W, E and R switch the gizmo between move, turn and scale when the command offers that operation; a command that does not offer it ignores the key. Typed digits, Minus, Backspace, Tab, Enter and Escape go to the engaged gizmo axis when the gizmo was the last control hovered or pressed, or when the command has no size handles. Tab steps to the next axis. Only the engaged axis shows a label.
* **Automation.** The `pointer` tool's reply lists `handles` and `gizmo` (centre, operation and the screen position of each handle), so scripts can aim at them.
* Maintained checks in [ViewportHandleTests](../Prism.Tests/Headless/Viewport/ViewportHandleTests.cs): HDL-H001 to HDL-H003 and HDL-H025 (primitives), HDL-H023 (Transform), HDL-H026 (typing, Tab, W / E / R on Move Component), HDL-H027 (Direct Edit), HDL-H028 (Component Pattern), HDL-H030 (grip against gizmo priority). The older [direct gizmo checks](../tests/Prism.DirectModelTests/GizmoTests.cs) expect the same Direct Edit layout.

## Known caveats / not verified

* The 2026-11-12 command gizmo batch is covered by HDL-H001–H003 and HDL-H023–H031. Seen live over MCP: Box and Cylinder (gizmo at the shape's middle, size arrows on the shape, the radius label on hover) and Direct Edit on a box's top face (gizmo with X and Y arrows at the face middle, pull arrow along the normal) and Transform → Rotate (pressing the X ring switched Axis from Z to X and reset the angle to 0, a 30° drag round the ring gave Angle = 30 and turned the preview). Not seen live, headless only: typing on Move Component, the Component Pattern gizmo, ring dragging and joint stops. No real-mouse pass has been done on any of it.
* Direct Edit on a flat face: the tilt ring's grip and the pull arrow's grip both sit on the face normal and come close together when zoomed far out. The nearest grip wins, so both stay reachable, but the layout could be clearer.
* The older suites could not confirm this batch end to end: `tests/Prism.DirectModelTests` passes its gizmo checks and then stops in its selection checks on the Solid tab's Select section, and `tests/Prism.PanelWorkflowTests` stops on a hotkey catalogue check before it reaches its joint handle drags (now also covered by HDL-H031). Neither stop was compared against the previous commit.
* The 2026-11-12 selection/datum/search work is build-verified and covered by `SelectionTests.cs` (SOL-H081–SOL-H084); drag-select directions, click-pick and datum placement were exercised live over MCP (the `datum` tool closes the earlier "can't arm menu/free tools" gap). Still open by hand: Paint-select feel, the "?" row in the search popup, and a manual click pass to confirm real-mouse placement matches the automation path.
* The 2026-11-11 batch (items 1–9 of the audit) is build-verified and covered by `Prism.Tests/Headless/Viewport/ViewportHandleTests.cs` (HDL-H001–H020, asserting handle presence, anchors, directions and param binding), but **no handle has been dragged by hand in the app** — drag feel, arrow placement in odd geometry, and handle-vs-panel sync need a manual pass.
* Approximation notes: `TrimCurve` from/to arrows project along the chord (exact for straight segments, approximate on curved ones); `MeshOffset`'s arrow points from the mesh centre toward its bounds as a stand-in for "along the normals"; `FeatureEdit` resize arrows appear only when the picked feature is round, a slot or a fillet.
* Text only reads TrueType 'glyf' fonts (Arial, Segoe UI, Times New Roman, Consolas, Courier New, Verdana, Calibri); OpenType/CFF fonts are not supported.
* Emboss decides raised vs engrave by the Style choice and tries to flip a raised shape that points into the body; behaviour on curved faces is untested.
* The previous tracking acquires points, line midpoints and rectangle centers; polygon/arc centers acquire only when they are real points.
