# Shared gizmo display and snapping

The sketch, freeform cage and control-point editors use the same arrow, ring and scale-handle renderer. Active tool only is the default: Move shows arrows, Rotate shows rings, and Scale shows square handles. Combined handles allows picking another operation directly. Custom visibility independently controls the three operations, including hiding all handles.

The collapsible **Gizmo display and snapping** panel is available in sketch spatial/datum controls and commands with manipulators. Preferences are saved application-wide and include:

- Move, rotate and scale visibility; sketch plane handles; axis labels; work-plane outline.
- Screen size, line width and pick tolerance. Screen size stays stable while zooming.
- Optional move, angle and scale increments. Hold Alt during a drag to bypass these increments.
- A live preview and reset to defaults.

Axes retain the conventional red/green/blue identifiers. Hovered and dragged handles and sketch plane highlights use the preference accent (blue by default). Light and dark themes have separate contrast colors. Uniform scale uses the diagonal square; axis scale uses axis-end squares in the cage and control-point editors. Sketch scale retains its existing uniform behavior. Parameter pull arrows share the accent, size and picking settings.

Sketch translation snaps in its gizmo frame; cage/control-point translation snaps on world axes. Rotation uses degrees; scale increments are percentages relative to 100%. Sketch transform drags retain their existing single-step undo behavior. Display changes do not rebuild document geometry.

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
