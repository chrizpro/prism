# Drawing workspace

## Purpose and status

Create and edit engineering drawings inside Prism, with deliberate control over what a sheet communicates. This is the canonical guide for the editable Drawing workspace and the existing drawing exporter.

**Approved and implemented (2026-11-05).** The owner approved the concrete workspace proposal and then chose one parts-list row per source body with quantity 1. The scope below is authorized on `develop`; approval is distinct from verification evidence.

## Existing behavior and limitations

- Analyze > Make Drawing generates PDF, SVG or DXF and opens the exported file. It has sheet size, projection, units, scale, body and view selection, one section, title information and annotation-category switches.
- The shared engine supports orthographic/isometric projections, sections, details, dimensions, notes, center marks/lines, hole/boss callouts, symbols and a parts list. Additional inputs are exposed through `drawing_export`; these are not an interactive desktop editor.
- Overall dimensions, hole callouts, centerlines and hole positions default on. Callout placement uses fixed offsets/directions. Hole locating dimensions take at most five distinct coordinates per axis. These heuristics do not establish complete manufacturing information or guarantee an uncluttered sheet.
- The current options include a default general tolerance. The Drawing workspace starts with a blank general tolerance and does not add a default edge-breaking instruction.
- The legacy exporter remains available. The new Drawing workspace stores editable sheets in `.prism` and has dedicated maintained tests linked below. Existing `drawing_test` exports remain historical examples.

## Workspace and appearance

Use a dedicated 2D sheet canvas with pan, cursor-centered zoom, fit sheet and actual-size indication. Keep the Prism window header, workspace navigation and normal keyboard focus conventions. Replace modeling-specific canvas controls with drawing controls while this workspace is active.

```text
Prism header and workspace navigation
Sheet | Views | Dimensions | Annotate | Tables | Export
Sheets and views |          Drawing sheet          | Properties
                |                                 | Selection
                |                                 | settings
Sheet tabs      | Sheet 1 of 2       Scale / Zoom / Status
```

The sheet is the visual focus: white paper, fine black drafting lines, a subtle paper edge, and a quiet cool-gray desk. Initial palette: paper `#FFFFFF`, light desk `#DDE3E8`, dark desk `#282E36`, ink `#202830`, selection `#536FC0`. Surrounding controls follow Prism's light/dark theme; paper remains light for print legibility. Use the existing application typeface for controls and a legible sans-serif face for drawing text. No decorative textures, gradients or automatic animation. Selection color and handles are editor overlays, excluded from export.

Left: sheet/view browser and source references. Center: editable sheet. Right: contextual properties. Bottom: sheet tabs and compact sheet/scale/zoom status. Empty state offers Create sheet and Choose model. At the minimum supported window size, side panels can collapse without hiding the sheet tools.

## Approved essential scope

1. **Drawing documents and sheets.** Store editable drawing data in the Prism document with stable source-body references. Support multiple sheets, add/rename/duplicate/delete sheet, sheet size/orientation, mm/in, first/third-angle projection, scale, editable title block and notes. Reopening restores the layout and annotations. Older documents still open. Drawing edits participate in dirty state, save and undo/redo.
2. **Views.** Place a base view from selected bodies, aligned projected views, isometric views, straight section views and circular detail views. Move/delete views, change scale and hidden-line visibility, edit section/detail definitions and retain parent relationships. Deleting a parent previews dependent removals before committing. View movement carries its annotations; projected alignment is maintained unless the user explicitly releases it.
3. **Dimensions.** Select projected geometry to create horizontal, vertical, aligned, angular, diameter and radius dimensions with model-unit values, precision and optional tolerances. Support baseline/ordinate hole positioning. Drag dimension placement, edit formatting and delete individual dimensions. Values are measured from references, independent of paper zoom and view scale. Text overrides are visibly identified in the editor.
4. **Annotations.** Place/edit/move/delete notes and leaders, hole callouts, center marks/lines, datum identifiers, feature-control frames and surface-finish symbols. Expose the existing symbol capabilities through desktop controls; do not imply automated standards compliance. Add balloons and an editable parts list linked to selected source bodies, with clear quantity and identity rules.
5. **Optional automation.** Start with views and no generated dimensions/callouts. Suggest dimensions for a selected view/category on request, preview them, then accept all or selected suggestions. Accepted items remain individually editable/deletable. Re-running suggestions must not duplicate accepted annotations or reset manual placement. Group repeated features only when their defining properties agree. Do not silently truncate hole locations; offer a hole table or another sheet when dense patterns need it.
6. **Layout feedback.** Provide alignment guides/snapping and report overlapping annotation bounds, text outside the printable sheet and stale references. Warnings select the affected item. Automatic placement should avoid occupied annotation bounds where possible; user placement remains authoritative. Warnings help review and do not certify drawing completeness.
7. **Model updates.** Explicit Update drawing regenerates referenced geometry and preserves placements where references remain valid. Deleted or ambiguous geometry references become visibly unresolved; preserve the last drawing and flag affected annotations rather than silently attaching them elsewhere. This first delivery does not promise robust remapping across arbitrary topology changes.
8. **Output.** Export PDF, SVG and DXF from the same composed sheet used by the editor. Provide all-sheet PDF and selected-sheet SVG/DXF. Preserve physical sheet size, line styles, text, scale and units; exclude selection overlays. Export reports unresolved references and layout warnings for review.

## Acceptance and verification

- A user can create, dimension, annotate, save, close, reopen and export a two-sheet bracket drawing entirely in the desktop app.
- A section and detail can be placed and edited, with their source markers and annotations preserved after save/reopen and undo/redo.
- A dimension remains numerically correct when the view scale or canvas zoom changes. Inch/mm formatting and angular/radius/diameter cases have independent numerical expectations.
- A dense hole pattern with more than five distinct positions is neither silently truncated nor automatically covered with labels. Suggestions can be selectively accepted and then moved/deleted without duplication on the next suggestion pass.
- A source-body edit marks the drawing stale. Updating preserves valid placements; a deleted referenced edge produces an unresolved indication without inventing a replacement.
- Light/dark desktop captures demonstrate readable paper, selection, panels, empty state and a dense annotated sheet at representative window sizes. PDF/SVG/DXF are inspected against the sheet composition.

Maintained coverage: [Unit drawing tests](../../Prism.Tests/Unit/Drawing/DrawingDocumentTests.cs), [Windows/OCCT Headless drawing tests](../../Prism.Tests/Headless/Drawing/DrawingWorkspaceTests.cs), and [desktop MCP drawing scenario](../../Prism.Tests/Integration/DesktopDrawingTests.cs). Classes link back to this guide and use `Feature=Drawing`, layer and stable test IDs. [Verification evidence](evidence/README.md) records dated results, source revision and actual desktop captures.

## Using the workspace

Choose **Drawing** in the workspace bar or workspace menu. Create a sheet, select source bodies under **Choose model**, and use **Place base view**. Set its orientation and scale, choose **Prepare view**, then click the paper to place it. A faint preview follows the pointer. Select a parent view before creating a projected, section or detail view.

Use **Dimensions** to pick points on projected geometry, then click to place the dimension. Angular dimensions use arm point, vertex and second arm point. Radius dimensions require a native circular edge or arc; diameter and center-mark tools require a complete native circle. Isometric views are illustrative and do not accept dimensions. Inspect the measured value, decimal places and optional tolerance in Properties; a text override is explicitly flagged.

Select or drag items on the sheet. View movement carries annotations and aligned projected views. Alignment guides and 1 mm placement snapping assist dragging; Alt bypasses snapping. Escape cancels a pending operation or drag. Wheel zooms around the pointer; middle/right drag pans; F or Fit fits the whole sheet. 100% is nominal physical size at 96 DPI, not a monitor calibration. The browser and Properties can be collapsed.

**Suggest** previews a selected category for one view. Accept selected suggestions or all; dismiss unwanted suggestions. Repeating a category preserves accepted items and manual placements. Hole-position suggestions and the circle-position table retain every detected circular position. These are projected circles, not automatic recognition of threads, countersinks, hole depth or manufacturing intent. Enter the actual specification in a hole callout. Dense layouts may require another sheet.

Sheet properties set size/orientation, default scale for new views, mm/in, projection, title information, notes and an explicitly chosen general tolerance. Sheet tabs switch pages; Sheet offers duplication and deletion. Parent/sheet deletion explains dependent removals and requires a second activation; ordinary edits support Undo/Redo. Parts lists retain one row per body (quantity 1), with editable names and materials.

Use **Update drawing** after changing the source model. Geometry generation works on a deep copy of the source. Annotation references are conservatively matched by projected curve signatures; unmatched references remain flagged, retaining their last values until recreated. Review warnings for overlaps, off-sheet content, overrides and unresolved references. They help review and do not establish manufacturing completeness. Export all-sheet PDF or current-sheet PDF/SVG/DXF through **Export**. Editor selection and suggestion overlays are excluded.


## Scope boundaries and open decisions

The initial delivery follows the essential scope above, using a Fusion-style drawing workflow as a reference, without claiming full Fusion parity. Custom reusable templates, revision clouds/tables, drawing sketches, welding symbols, broken/breakout/auxiliary views and automated standards validation are follow-up candidates. Full parity requires a separately reviewed capability inventory.

Drawing data is embedded in `.prism`, with explicit model updates and unresolved-reference reporting as described above. Standalone linked drawing files and automatic model-update policy remain future decisions. Owner decision (2026-11-05): one parts-list row per selected source body, quantity 1; bodies with equal names remain separate. Grouping assembly instances is deferred. The existing exporter groups by name and retains its legacy behavior.

## Implementation and references

- [Workspace proposal and delivery issue #58](https://github.com/chrizpro/prism/issues/58).
- [Editable drawing model](../../Prism/Core/Drawing/DrawingDocument.cs), [sheet editor](../../Prism/Views/DrawingWorkspace.axaml), [session/history integration](../../Prism/Core/CadSession.Drawing.cs).
- [Drawing command](../../Prism/Commands/DrawingCommand.cs), [projection/composition engine](../../Prism/Core/Drawing/DrawingSheet.cs), [output writers](../../Prism/Core/Drawing/DrawingOutput.cs).
- [Workspace definitions](../../Prism/ViewModels/MainViewModel.Workspaces.cs), [automation guide](../MCP.md#drawings), [test maintenance workflow](../engineering/requirements-and-testing.md).
- Autodesk reference: [Drawing workspace tools](https://help.autodesk.com/cloudhelp/ENU/Fusion-Drawing/files/DWG-REF-DRAWING-TAB.htm), [drawing views](https://help.autodesk.com/cloudhelp/ENU/Fusion-Drawing/files/GUID-7AA7A8E9-D423-40A0-8139-7B09AE073DC7.htm), [document settings](https://help.autodesk.com/cloudhelp/ENU/Fusion-Drawing/files/DWG-REF-DOCUMENT-SETTINGS.htm). Consulted 2026-11-05 for workflow comparison, not a complete parity audit.

## Known limits

Reference matching is conservative and does not promise automatic remapping through arbitrary topology changes. Curve picks use projected polylines; circular dimensions require a matched analytic kernel circle and use its model radius. Section-created circular intersections without a corresponding source circular edge may not offer radius/diameter tools. A circle position table is editable text generated on request; regenerate it after model changes. General notes and long table/title text may need shortening or more sheet space. The PDF writer uses Helvetica/WinAnsi; unsupported free-text characters are replaced with `?`. Symbol frames are vector geometry. Templates, advanced view types and standards certification remain outside this delivery. The desktop integration scenario exercises the document workflow through MCP; the recorded pointer check separately covers picking, dragging and Undo, and is not exhaustive UI automation.

Combined-source verification on 2026-11-05 passed 86 Unit and 592 Headless tests with rebuilt native libraries. See the [release integration report](../engineering/release-readiness-2026-11-05.md) for desktop results and source revisions.
