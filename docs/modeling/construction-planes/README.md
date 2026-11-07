# Construction planes and sketch datums

Construction Plane creates exact reference frames from offsets, angles, midplanes, normal-to-curve locations, three points, tangent faces, two edges or a planar curve. Supported modes can generate a stack from a count or typed values. Datum opens the gizmo settings. Datums are made outside a sketch; the sketch tab has no Datum group.

Hidden planes reappear whenever a command asks for a plane (Create Sketch, intersection curves, and other plane picks), so any of them can be picked or hidden again from the browser, whose eyes track what is actually drawn. An eye turned off during the pick suppresses the plane for that command only; a plane switched off for real stays off. [SOL-H078 and SOL-H079](../../../Prism.Tests/Headless/Solid/SketchAndDatumTests.cs) cover the reveal, the browser sync and the suppression lifetime.

Settings → Interaction → **Reference plane size** scales the drawn size of construction planes and the origin triad (25–200 % of the default model-relative size); pick bounds follow the drawn size, so smaller planes declutter a busy scene without becoming harder to hit.

## Verification

[Datum actions and three-point plane](../../../Prism.Tests/Headless/Solid/SketchAndDatumTests.cs), [offset plane](../../../Prism.Tests/Headless/Solid/SharedToolTests.cs) and [Datum Gizmo entry](../../../Prism.Tests/Headless/Solid/SolidRibbonTests.cs). [Tracking issue #34](https://github.com/chrizpro/prism/issues/34).

## Coverage gaps

Angled, tangent and curve-normal planes, alternate datum orientations and desktop reuse need further coverage. See [gizmo workflows](../../gizmo-workflows/README.md). New requirements need owner review.

## Standalone datums

The Datum split's **Point, Axis and Plane** tools now work in model space (owner-approved 2026-01-30; [issue #42](https://github.com/chrizpro/prism/issues/42)). Picking one arms a persistent viewport tool — each click drops another datum until **Esc**, **Enter** or a **right-click** finishes. Inside a sketch the Datum buttons do nothing (owner decision 2026-11-07); finish the sketch first.

Placement semantics:

- **Point** — on the picked face/edge/plane, or on the work plane where nothing was hit. Origin helpers only inform direction: a click on a drawn origin axis or origin plane lands under the cursor on the work plane (origin-axis picks report the world origin for *selection* purposes, which previously teleported the datum to (0,0,0) — fixed).
- **Axis** — along a picked edge or curve, along a picked face's normal, along a picked origin axis or copied from another datum, else the work-plane normal at the cursor.
- **Plane** — matched to a picked face, construction plane, origin plane or another plane/gizmo datum, else the work plane at the cursor.

Model datums live on a single `Datums` carrier object (a Sketch-kind object with a vertex shape and only `SketchData.Datums` in its model), so they draw, pick as `SelKind.Datum` (points included, via `SelFilter.Datum` — previously only axes and planes matched a filter), list in the Browser, undo one placement at a time and persist in files — the same treatment sketch datums get. Placement is a session-level free tool (`CadSession.FreeTool`) behind command and sketch tools in the `ActiveTool` chain, so it requires no open command and no sketch.

Editing after placement:

- **Select + Move** — datum items are pickable in object-selection mode. The ribbon **Move** tool raises the datum gizmo in *move mode* (`DatumGizmoTool.MoveMode`): a click on an object or datum binds it, then arrows/blocks/rings and the panel's typed origin/rotation values apply the same rigid transform to every bound target (`CadSession.TransformTarget` — shapes via `Occ.Transform`, meshes via `TriMesh.Transformed`, forms via cage points, datums via frame rewrite). Esc releases the payload first, then hides the gizmo. The param-driven `TransformCommand` stays reachable from command search.
- **Delete** removes just the picked datum entries from the carrier (an emptied carrier object disappears); it never deletes siblings.

[SOL-H086–SOL-H088](../../../Prism.Tests/Headless/Solid/SketchAndDatumTests.cs) cover repeated placement, naming, undo, empty-space/work-plane fallbacks, face/edge adoption, Esc and right-click finish, and ribbon enablement; SOL-H071/SOL-H072 cover the sketch-side behavior; SOL-H090–SOL-H092 cover click-at-cursor placement (including clicks on a drawn origin axis), datum picking/`SetDatumFrame` repositioning, and gizmo-target transforms; SOL-H096 covers empty-space clicks in a Front view, which land on the grid being looked at (the front plane) because the ground plane is edge-on there; SOL-H094 covers placement while the move gizmo is showing, which no longer takes the click (a body or gizmo handle under the pointer used to be grabbed instead of getting a point).

### Open decisions

- Browser-side datum editing (rename, visibility per datum rather than per carrier) is not implemented — the carrier object is the Browser row; repositioning happens through Move/gizmo or `SetDatumFrame`.
- Sketch-kind objects (profiles, the carrier itself) are not movable as whole-object targets — their datums move individually.
- Axis direction on a multi-edge curve object uses the curve's overall chord.
- Hovering previews of the placement before commit are not implemented.

### Datum options panel and orientation gizmo

**Status: Proposed — awaiting owner review (drafted 2026-11-07).** The behavior below is implemented on `develop` for review, but it is not approved and no tests encode it yet. It changes the SOL-H094 rule above only in the narrow case described under *Orientation gizmo*.

**Purpose.** The pick decides where a datum goes, but not always how an Axis or Plane faces. The user should be able to choose the orientation before clicking and correct it afterwards, and to reopen that correction later on any existing datum.

**Datum panel.** While a Point, Axis or Plane tool is armed, the right-hand panel shows a **Datum** panel for that tool. It closes, and the previous panel returns, when the tool finishes.

- *Orientation* (Axis and Plane only): **From the pick** (default, the placement rules above) or a fixed world direction — Axis: along world X, Y or Z; Plane: XY, XZ or YZ. A fixed choice keeps the picked position and replaces only the direction.
- *Extra rotation* (Axis and Plane only): X, Y, Z in degrees about the world axes, applied after the orientation choice. Zero changes nothing.
- *Location*: X, Y, Z (mm) of the selected datum, or of the last one placed when nothing is selected. Typing moves only that datum, keeps its orientation and is one undo step. This is the entry point for later editing of an existing point.
- Point has no orientation section.

**Orientation gizmo.** After an Axis or Plane click, the new datum is selected and the datum gizmo appears on it in rotate mode, bound to that datum. Dragging a ring reorients it and an arrow moves it; both are undoable. Selecting an Axis or Plane datum from the Browser or the Select tool shows the same gizmo. The armed tool stays armed: a click that misses the gizmo's handles places another datum, and a click on a handle drags the gizmo. 

**Gizmo is a placement tool too.** In model view, **Gizmo** works like Point, Axis and Plane (owner direction 2026-11-07): choosing it arms a persistent tool, a click drops a Gizmo datum on the pick (aligned to a picked face, else the work plane), and Esc or a right click finishes. Each placement is selected and the move gizmo binds to it, so the handles act on that datum right away; the next placement adds another, so any number can coexist. The Datum panel offers the same orientation and extra-rotation options as Plane.

**Enter places at the pointer.** In all four tools, moving the pointer over the viewport and pressing **Enter** places a datum there exactly as a click would; Esc or a right click finishes. This replaces the earlier rule that Enter finished the tool.

**Taking possession of a datum (Proposed, 2026-11-07).** One workflow for every kind of datum — Point, Axis, Plane and Gizmo. Selecting a single datum in plain model view (from the Browser, the Select tool, or by placing it) makes the Datum panel the context panel, binds the free gizmo to it (rotate handles for an Axis or Plane, move handles for a Point or Gizmo) and lets you type its location. The panel's **Gizmo settings…** button opens the full Gizmo panel on that same datum: location, rotation, snapping and handle options, exactly as before. Deselecting restores the panel that was open. Gizmo datums are no different from the others here, so editing an axis's coordinates and "becoming" a gizmo are the same action.

**Switching tools.** Choosing another tool from the Datum dropdown disarms the previous one and the dropdown shows the active tool, including Gizmo. Starting a tool hides a leftover free gizmo.

Dragging a handle updates the model at most about eight times a second and once more on release, so the Browser and history stay calm; typed location values made in quick succession share one undo step.

**Acceptance criteria (Proposed).**

1. Arming Axis or Plane shows the Datum panel with an orientation list; arming Point shows it without orientation or rotation; finishing the tool restores the previous panel.
2. With Orientation set to a world direction and rotation zero, the placed Axis points along it, or the placed Plane faces it, at the picked position.
3. A non-zero extra rotation turns the placed datum by that angle about the world axes; with rotation zero the datum is unchanged.
4. After an Axis or Plane click the datum is the only selection and the gizmo is bound to it in rotate mode; after a Gizmo or Point click it is bound in move mode.
5. Dragging a gizmo ring changes only that datum's direction; its origin is unchanged. Undo restores it.
6. Typing a location moves only the selected or last-placed datum, preserves its orientation, and one undo reverses it.
7. With the Axis tool armed, choosing Gizmo from the dropdown leaves no armed tool and the dropdown shows Gizmo; choosing Axis again re-arms it. Each Gizmo placement adds one more Gizmo datum; none replaces another.
8. Selecting an existing Point, Axis, Plane or Gizmo datum outside any armed tool shows the Datum panel and the gizmo on it; **Gizmo settings…** opens the Gizmo panel bound to that datum.
10. One Esc ends an armed datum tool together with its bound gizmo and the datum selection; with a datum selected and no tool, one Esc hides the gizmo and clears the selection. (The Move tool keeps its two-step Esc: release the payload, then hide.)
9. With any of the four tools armed, pressing Enter with the pointer over the viewport places one datum at the pointer and keeps the tool armed; Esc ends it.

**Open decisions for the owner.**

- *Gizmo settings:* handle size, snapping and visibility live in the Gizmo settings panel, which the Gizmo tool no longer opens. Where should they be reachable from?
- *Saved planes:* the sketch-only "Saved gizmos and planes" section of the Gizmo panel overlaps with model Plane datums. Keep, move or remove it?
- Should the panel be reopenable on a selected datum without arming a tool, so the orientation and location of an existing datum can be edited from the panel alone?
- Should the orientation list also offer "normal to the work plane" or "view direction"?
- Is the extra rotation about world axes (current) or the datum's own axes?

**Implementation links.** [`DatumPickTool`](../../../Prism/Tools/DatumPickTool.cs), [`CadSession.Datums`](../../../Prism/Core/CadSession.Datums.cs), [`MainViewModel.Datum`](../../../Prism/ViewModels/MainViewModel.Datum.cs), [`DatumOptionsPanel`](../../../Prism/Views/DatumOptionsPanel.axaml). Maintained tests: none yet for this section; it will go in [SketchAndDatumTests](../../../Prism.Tests/Headless/Solid/SketchAndDatumTests.cs) after approval. Verified so far: build, and the 36 existing datum and selection headless tests pass (2026-11-07); the panel and gizmo have not been checked by hand in the running app.
