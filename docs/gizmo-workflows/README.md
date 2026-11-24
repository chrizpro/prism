# Shared gizmo display and snapping

The sketch, freeform cage and control-point editors use the same arrow, ring and scale-handle renderer. Active tool only is the default: Move shows arrows, Rotate shows rings, and Scale shows square handles. Combined handles allows picking another operation directly. Custom visibility independently controls the three operations, including hiding all handles.

The collapsible **Gizmo display and snapping** panel is available in sketch spatial/datum controls and commands with manipulators. Preferences are saved application-wide and include:

- Move, rotate and scale visibility; sketch plane handles; axis labels; work-plane outline.
- Screen size, line width and pick tolerance. Screen size stays stable while zooming.
- Optional move, angle and scale increments. Hold Alt during a drag to bypass these increments.
- A live preview and reset to defaults.

Axes retain the conventional red/green/blue identifiers. Hovered and dragged handles and sketch plane highlights use the preference accent (blue by default). Light and dark themes have separate contrast colors. Uniform scale uses the diagonal square; axis scale uses axis-end squares in the cage and control-point editors. Sketch scale retains its existing uniform behavior. Parameter pull arrows share the accent, size and picking settings.

Sketch translation snaps in its gizmo frame; cage/control-point translation snaps on world axes. Rotation uses degrees; scale increments are percentages relative to 100%. Sketch transform drags retain their existing single-step undo behavior. Display changes do not rebuild document geometry.

## Move pivot, scale, and grabbing a body by its faces (owner request and decisions 2026-11-23, built)

**Status: approved 2026-11-23 and built; tried by headless tests, not by hand in the app.** [GZM-H001 to GZM-H011](../../Prism.Tests/Headless/Viewport/MoveGizmoTests.cs). The owner split a body on the Direct Modeling tab and wanted to swing the thin piece about one of its ends. Owner decisions: the key is **D**; the pivot resets each time Move opens; picking enough faces of a body is how you grab the whole body; the gizmo must offer move, rotate and scale.

- **Move pivot (D, or the panel's *Move pivot* button).** While Move holds something, D switches the handles and the panel's location and rotation fields to move and turn only the gizmo; what it holds stays put and stays held. In this mode a click on a corner puts the pivot there. A click on a straight edge puts the pivot on the edge (at its end or middle when the click is near one) and turns the gizmo so its X runs along the edge, so the X ring turns about that edge like a hinge. D again (or *Done placing the pivot*) goes back to moving what it holds, about the new pivot. Without the Move gizmo, D does nothing.
- **A placed pivot is kept.** Grabbing something again after Esc keeps a pivot the user placed (in Move pivot, or by moving the gizmo while it held nothing). A first grab still centres the gizmo on what it grabs, and clicking another body while one is held still swaps to it and centres. The pivot resets each time Move opens.
- **Scale.** While Move holds something the operation list has Move, Rotate and Scale, and the gizmo shows scale squares. The centre square scales evenly, an axis square along that gizmo axis, a plane block in that plane, all about the pivot and along the gizmo's own axes (a turned gizmo stretches along its turned axes). A box shows the result while the handle is held; the scale lands when it is let go, as one undo step. The panel's scale fields do the same with typed factors. Bodies, surfaces, curves, meshes and freeforms stretch per axis. A sketch scales evenly only, so it stays an editable sketch. A datum only has its position scaled. Assembly parts are refused, since parts keep their size. With nothing held there is nothing to scale and the list goes back to Move and Rotate.
- **Every face of a body picked means the body.** Move grabs the whole body when all of its faces are picked, so it can turn and scale. Some of its faces still open Move to slide those faces, as before.
- **Move on the Direct Modeling tab.** The face gizmo there is always open, which used to stop Move with "Finish or cancel the active tool first". Move now sets it aside while the Move gizmo is up (only when it has no unapplied change), and the face gizmo comes back when the Move gizmo closes.

**Regression guard.** GZM-H001 to H004 were written first and passed on the code before the change: first grab centres, a datum lends its frame, a typed turn is about the centre and undoes in one step, Esc releases then hides, and a click on another body swaps and centres. They pass after it. Alt-drag copy (SEL-H029), Move of picked parts (SEL-H068), the sketch gizmo (SKT-H045/H046) and Direct Edit's own move, pull and tilt (DirectEditTests) are in the slices run with this change.

**Not built:** snapping the pivot to corners and edges while dragging it (it snaps on click), and per-axis scaling of sketches.

## Screenshots

These are rendered production Avalonia controls using the same overlay geometry as the viewport gizmos.

| Display | Dark | Light |
| --- | --- | --- |
| Move | [Screenshot](move-dark.png) | [Screenshot](move-light.png) |
| Rotate | [Screenshot](rotate-dark.png) | [Screenshot](rotate-light.png) |
| Scale | [Screenshot](scale-dark.png) | [Screenshot](scale-light.png) |
| Combined | [Screenshot](combined-dark.png) | [Screenshot](combined-light.png) |
| Advanced options | [Screenshot](advanced-options-dark.png) | [Screenshot](advanced-options-light.png) |

## Verification

`dotnet run --project tests/Prism.GizmoTests -c Release` checks default/combined/custom masks, hidden-handle picking, constant screen size, legacy settings, persistence round trips, theme contrast, preference accent, snapping, undo, combined-mode switching in the real sketch/cage/control-point input paths, and panel embedding. It also regenerates the screenshots above.

The older translation-shaft regression in SketchModeTests explicitly uses Combined handles, since inactive translation handles are intentionally hidden in the new default mode.

Verified: GizmoTests (44), SketchModeTests (109), FreeformTests (49), SketchPanelWorkflowTests (341), PanelWorkflowTests (20), ProjectionTests (34), and WorkflowTests (43): 640 checks. Desktop Release builds with zero errors. The pre-existing contradictory setup-state assertions in SketchSpaceTests are documented in [the sketch panel verification notes](../sketch-panel-workflows/README.md); this is not a claim that every repository test passes.
