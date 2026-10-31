# Viewport interaction: handoff for the next agent

Goal set by the owner: **everywhere it is possible, the user should be able to do the job directly in the 3D viewport (click, drag, type on the canvas), not only through the options/properties panel.** The model is Fusion 360's feel. The owner calls out sketching first ("knock sketching out of the park"), then feature tools, assembly, animation and the rest.

Stack: C# / Avalonia, OpenCascade kernel in `native/`. Solution `Prism.slnx`; run with `dotnet run --project Prism.Desktop` (stop a running `Prism.Desktop.exe` before rebuilding or the copy step fails). Nothing below is committed; check `git status`.

## What is already done (sketching)

| Feature | Where |
|---|---|
| **Alignment tracking** (Fusion-style "acquire"): rest the pointer ~0.2 s on a point, a line midpoint or a rectangle center and a dashed guide line extends horizontally/vertically; the cursor snaps to it, and clicking adds Horizontal/Vertical constraints when the anchor is a real point. Also lines up with the tool's last click. | `Prism/Core/Sketching/SketchContext.cs` (`Snap`/`SnapCore`, `Hover`, `Acquired`, `TrackAcquired`, `RectangleCenter`, `TrackSegs`, `PointFor`); `Prism/Tools/SketchTools.cs` (`ClickTool` sets `sk.TrackFrom`) |
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

A rough count of command files that expose any `Handles`/`HandleSpec`/viewport tool (so most things are panel-only):

* **No viewport handles or tools at all** (panel only): `HoleCommand`, `PatternCommands` (6 commands), `BoundarySurfaceCommand`, `SurfaceCommands2`, `SurfaceMatchCommands`, `PlaneCommands`, `FormCommands`/`FormBuildCommands`/`FormShapeCommands` (partly covered by `FormEditTool`), `NurbsToolCommands` (23 commands), `MeshCommands` (15), `MeshFitCommands`, `CamCommands` (11) / `CamWorkflowCommands`, `CaeCommands`/`CaeWorkflowCommands`, `EcadCommands`, `FlattenCommand`/`RefoldCommand`, `WarpCommand`, `RenderCommand`, `PrintExportCommand`, `DrawingCommand`, `ArCommand`, `GenerativeCommand`, `EquationCurveCommand`, `CheckCageCommand`.
* **Only one handle where several are natural**: `SolidCommands.cs` (Extrude has distance arrows; Fillet/Chamfer radius, Shell thickness, Revolve angle, Rib, Loft/Sweep guides, Boolean offsets have little or none), `SurfaceCommands`, `SurfaceToolCommands`, `SheetMetalCommands`, `AssemblyCommands` (joints), `AnimationCommands`, `DirectCommands`.

### Suggested priority and the interaction to build

1. **Fillet / Chamfer / Shell / Draft / Offset / Thicken**: a drag arrow at the selected edge/face for radius/thickness/distance (like Extrude), live preview, type-to-set.
2. **Hole**: click a face to place, drag to set diameter and depth; show the hole preview in place.
3. **Patterns** (linear, circular, along path): drag handles for spacing/count/angle; click the axis in the viewport.
4. **Revolve, Sweep, Loft**: pick profile/axis/rails by clicking in the viewport (already partly selection slots), add an angle arc handle for Revolve, drag guides for Loft/Sweep.
5. **Move / Rotate / Scale / Mirror and Planes**: gizmo-style direct manipulation, and drag-to-offset for construction planes (`PlaneCommands`).
6. **Assembly joints**: click two faces/points, drag the joint limits and motion in the viewport (`AssemblyCommands.cs`).
7. **Animation**: drag objects to set keyframes on the timeline, scrub in the viewport.
8. **Sketch gaps still worth doing**: apply the acquire/tracking feature to more tools' own inference (arc, circle radius, offset, mirror), add extension-line tracking (continue a line's direction), tangent/perpendicular hints at arc/line ends, a click-drag "rotate" for text, and dimension handling for angle arrowheads (angle dimensions have no arrowheads yet).
9. **CAM/CAE/mesh/NURBS**: add click-to-pick on the model and drag handles for the main numeric parameter (toolpath stock offsets, load/constraint arrows, mesh brush radius, NURBS patch controls).

### Rules of thumb for each tool

* Every number in the panel that has a spatial meaning should also be draggable or typeable on the canvas; the panel stays as the exact-entry fallback and mirrors what the canvas shows.
* Show a live preview and a small label with the value near the pointer; Enter/click commits, Esc cancels, right-click backs out.
* Click-to-pick geometry in the viewport instead of requiring a slot to be activated in the panel where possible (hover highlight, then click).
* Reuse snapping and tracking: `SketchContext.Snap` for sketch work.

## Known caveats / not verified

* None of the new interactions above have been exercised by hand end to end; they build and the font parser was run against seven system fonts, but the viewport behaviour (tracking feel, text editing, dimension placement) should be tried in the app.
* Text only reads TrueType 'glyf' fonts (Arial, Segoe UI, Times New Roman, Consolas, Courier New, Verdana, Calibri); OpenType/CFF fonts are not supported.
* Emboss decides raised vs engrave by the Style choice and tries to flip a raised shape that points into the body; behaviour on curved faces is untested.
* The previous tracking acquires points, line midpoints and rectangle centers; polygon/arc centers acquire only when they are real points.
