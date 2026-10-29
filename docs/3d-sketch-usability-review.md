# 3D sketch usability review

Historical review of the implementation before unification. The new behavior and usage instructions are documented in [Unified sketches](unified-sketch.md). The original probe source in `artifacts/3d-sketch-research` targets the former standalone 3D command; use the new SketchSpaceTests suite to validate the current implementation.

Research date: October 28, 2026. Scope: current Prism source and the running Windows interface. This is a research and teaching deliverable; application behavior has not been changed.

## Assessment

The difficulty has identifiable causes in the product. The existing axis/plane gizmo is a useful foundation, but camera locking, drawing constraints, and numeric input have inconsistent interactions. Training helps, but does not resolve all of these issues.

Three independent concepts need to be visible:

| Concept | What it controls | Example |
| --- | --- | --- |
| Camera orientation | Where you look from | Top, Right, or an oblique view |
| Camera projection | How depth looks | Orthographic or Perspective |
| Drawing constraint | Where new points can land | XY plane through a point, or Z axis through a point |

A rectangle can lie on XY while you orbit freely in either projection. Orthographic projection does not inherently prevent orbiting. Prism's separate camera lock does.

## Use it effectively today

### Restore a useful 3D view

1. In the 3D Sketch panel, uncheck **Lock the view to the plane**.
2. Right-drag or middle-drag in the viewport to orbit. Shift-right-drag pans; the wheel zooms. Starting a right-drag promptly avoids the optional hold marking menu.
3. If you want perspective depth, choose **View > Perspective**. This changes projection independently of the checkbox.
4. Use the main viewport for this workflow. The auxiliary Top/Front/Right viewports intentionally lock their camera orientation.

Unchecking the checkbox enables orbit for a normally unlocked main camera, but does not itself rotate the current face-on view. Conversely, using the cube can rotate the camera even while the checkbox remains checked; the cube and viewport do not consistently enforce the same lock.

### Choose a drawing plane or axis

| Action | Current behavior |
| --- | --- |
| X / Y / Z | Toggle the respective axis lock through the last point |
| Same axis key again | Release that axis lock |
| Gizmo arrow | Toggle that axis lock |
| Gizmo plane square | Select XY, XZ, or YZ and clear axis locking |
| Drawing plane dropdown | Select Ground (XY), Front (XZ), or Side (YZ) |
| Tab | Cycle XY → XZ → YZ; an existing axis lock stays active |
| Hold Shift while placing a point | Attempt movement normal to the current plane through the last point |
| Backspace | Remove an in-progress point, or the last completed piece if no points are pending |
| Enter / right-click | Finish a line/spline; right-click again with no pending points can commit the command |

There is no dedicated Shift-Z → XY shortcut today. The window routes Shift-Z to the same `Char:Z` command as Z. For plane selection, use the dropdown or gizmo square. If Tab seems ineffective, release the existing axis lock first.

**Plane follows last point** preserves the coordinate normal to the selected plane: Z for XY, Y for XZ, X for YZ. It does not always mean world height. When it is checked, the last point of the previous finished piece can also establish the next piece's plane. Uncheck it to expose **Plane height**, which is a signed offset along the plane normal. For XZ the defined normal points toward negative Y, so a positive offset gives negative world Y.

Shift lifting needs an oblique view. Looking straight down the lifting axis removes the screen-space movement needed to choose a distance. Explicit X/Y/Z locking partly handles this by turning the view when the selected axis points at the camera; Shift lifting lacks that recovery.

### Draw a rectangle in 3D Sketch

1. Uncheck view locking and orbit to a clear oblique angle.
2. Choose **Rectangle (2 corners)**, or press R with the viewport focused.
3. Select the plane before the first corner; use its gizmo square to clear a previous axis lock.
4. For a fixed world offset, turn off **Plane follows last point** and set **Plane height**. Otherwise the first corner and follow behavior establish the plane position.
5. Click the first corner, then the diagonally opposite corner. Two clicks complete the rectangle automatically.
6. Avoid Shift, axis changes, or plane changes between those clicks. Nearby points/edges can currently override the drawing-plane candidate before rectangle construction projects the result back onto its plane.
7. Choose **Create curves** when the collection is ready.

The result is a closed curve wire. The command does not automatically create a filled surface or solid, and does not give that rectangle persistent width/height constraints.

Typing a single value in this command controls the distance to the second corner, normally the diagonal for a rectangle. It does not set width. There is no supported `40,20` width/height entry here.

### Prefer regular Sketch for precise planar profiles

For a plate, cutout, rectangular extrusion, or rectangle on a tilted face:

1. Choose **Create Sketch**.
2. Select a planar face or construction plane, or choose a base plane and offset.
3. Choose the standard rectangle tool and click the first corner.
4. Move toward the desired quadrant; type the width, press Tab, type the height, and press Enter. For example: `40`, Tab, `20`, Enter.
5. Finish Sketch and apply the modeling operation.

The standard sketch rectangle already offers width/height entry and geometric constraints. It also offers a three-point rectangle for an in-plane rotation. Its typed size controls placement; use sketch dimensions when the size must remain constrained during later edits. A plane placed anywhere in space is sufficient for a planar rectangle; reserve 3D Sketch for paths and geometry that spans planes.

## Findings tied to the implementation

| Finding | Evidence | Consequence |
| --- | --- | --- |
| View lock defaults on | `Sketch3DCommand.cs:45`, `AlignView()` | Entering a command named 3D Sketch immediately makes it feel like a fixed planar editor |
| Orbit silently becomes pan | `GlViewport.cs:763` | The visible “Right-drag: orbit” hint can contradict actual behavior |
| Camera cube bypasses that lock | `ViewCube.cs:311`, `345–357` | Similar navigation controls behave differently |
| Shift-Z loses modifier identity | `MainWindow.axaml.cs:39–51` | The proposed plane shortcut cannot work without input-routing changes |
| Tab and plane squares disagree | `Sketch3DCommand.cs:234–240`, `390` | Changing planes through the keyboard may leave the cursor constrained to an axis |
| Alt instructions conflict with navigation | Hint at `Sketch3DCommand.cs:54`; input interception at `GlViewport.cs:730` | The advertised Alt-click through the gizmo is intercepted as a navigation gesture in the normal viewport path |
| Shift lift degenerates face-on | `Locate()` normal/ray denominator | The default camera alignment makes the advertised lifting gesture least useful |
| Snap can override plane and typed placement | `Locate()` resolves point/edge snaps before Shift and planar typed input | The point preview can disagree with the intended planar rectangle or typed length |
| Rectangle plane is recomputed during drawing | `PlaneForShape()`, `Make()` | Changing planes between clicks reinterprets the unfinished rectangle |
| Rectangle loses its shape identity | `Make()` returns `Shape3.Line` | Output naming becomes Line, and Move points can deform it without preserving a rectangle |
| Axis locks do not govern Move points | `PointerMove()` passes `snapToObjects: false`; axis branch requires true | A visible axis lock can be ignored during point editing |
| 3D curves lack the normal sketch editing contract | `Build()` creates `ObjKind.Curve`; `Command.CanEdit` defaults false for tools | Users should not expect standard constrained-sketch re-editing from this command |

The live interface was inspected in the user's existing 3D Sketch session. Rectangle was selected, Side (YZ) was active, view locking was checked, and the orbit hint remained visible. The checkbox was toggled off and restored; no curve creation, deletion, command commit, or model-file operation was performed in that session. Moving the pointer changes the transient rectangle preview.

## Interaction principles

Keep work-plane control independent of camera control. Use Tab to cycle coordinate planes, explicit axis locks with color feedback, and separate numeric dimensions for rectangles. Manipulators should distinguish axial movement, planar movement, rotation, and numeric positioning.





## Recommended changes, in order

### 1. Make navigation predictable

Default 3D Sketch to a freely orbiting camera and preserve the current view. Keep an explicit **Look at drawing plane** action for one-time alignment. Offer **Lock camera to plane** separately for users who want it. Keep Orthographic/Perspective independent.

Display the active camera state and gesture in the viewport: “Free orbit · Perspective” or “Camera locked to XY · Drag to pan · Unlock”. Make cube navigation and viewport navigation agree. Preserve intentional fixed auxiliary views.

### 2. Make plane selection a single consistent operation

Use one plane-selection method for the dropdown, gizmo, Tab, and shortcuts. It should clear the axis lock, update the visible plane, and preserve camera orientation unless camera locking is explicitly enabled.

Add the proposed shortcuts as explicit plane selections: **Shift-X → YZ**, **Shift-Y → XZ**, **Shift-Z → XY**. Keep X/Y/Z for line-axis locking. Handle these modifier combinations before the current generic character routing. Show both shortcuts and full plane names in tooltips and in a small active-plane indicator.

### 3. Give rectangles a stable plane and proper dimensions

Capture the rectangle's frame at the first corner. Keep that frame through preview, snapping, numeric input, and completion. Offer separate Width and Height fields with Tab switching, using the regular sketch tool's existing interaction pattern. Support two-corner first, then center and three-point variants.

Resolve the planar constraint before accepting snaps: accept a point on the plane, explicitly label a projected snap, or reject an incompatible snap. Do not silently show an off-plane corner and then build somewhere else. Prevent zero-width/height rectangles with an actionable message. Preserve rectangle identity so later edits can maintain perpendicular sides.

### 4. Fix conflicting gestures and editing state

Resolve Alt-click's conflict with orbit navigation. Make explicit axis locks apply to Move points, or visibly disable them there. Give Shift lift the same visibility recovery as explicit axis locking, or provide a numeric normal-offset field. Clear stale numeric/axis state when switching shapes and document whether a change discards unfinished points.

### 5. Explain which workflow to choose

Present a short description near the entry points: **Sketch on a plane** for dimensioned profiles; **3D Sketch** for spatial paths and free curves. Make “Create curves” and closed-wire feedback clear so a finished rectangle is not mistaken for an automatically created surface.

## Validation performed

Eight focused probes passed in an isolated Avalonia headless session using the current application code and native kernel. They confirmed: default camera locking despite perspective projection; unlock restoration; Tab retaining an axis lock; the face-on orthographic Shift-lift singularity; silent projection of an off-plane rectangle corner; loss of rectangle shape identity; reinterpretation after a plane change; and rejection of comma-separated or signed numeric input. These are behavior probes supporting this review, not a full mouse/keyboard integration suite.

Reproduce from the workspace root with `dotnet run --project artifacts/3d-sketch-research/Probe.csproj -c Release`. Source: [probe](../artifacts/3d-sketch-research/Program.cs). Results: [recorded output](../artifacts/3d-sketch-research/results.txt). Compilation succeeded with four existing unrelated warnings. No application source was edited.

## Acceptance scenarios

Before shipping changes, verify these user tasks end to end:

- Enter 3D Sketch from an oblique view and orbit immediately in both projections.
- Press Shift-Z and place an XY rectangle without changing the camera; X/Y/Z still constrain line segments.
- Select an axis, then change plane by Tab, dropdown, or gizmo; all three clear the axis identically.
- Draw a 40 × 20 rectangle at Z=30 with numeric entry and verify all four vertices and right angles.
- Hover an off-plane edge while drawing that rectangle; preview, snap label, dimensions, and result agree.
- Attempt normal movement from a face-on view and receive a usable interaction.
- Drag a point with a visible axis lock and verify its displacement follows that axis.
- Exit or cancel the command and restore the intended camera-lock state without affecting auxiliary viewports.

These recommendations are design judgments derived from the observed implementation and common modeling workflows.
