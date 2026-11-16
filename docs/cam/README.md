# CAM status and scope

Updated 2026-10-31.

Prism focuses on CNC machining and preparation for FDM and resin printing. Dedicated slicers handle supports, slicing, exposure, printer profiles and printer delivery. Do not expand the legacy built-in FDM slicer.

## Workspace

The public [Manufacturing walkthrough](../../site/guide/manufacturing.md) includes actual app captures of stock setup, profile preview, playback and FDM/resin preparation settings. Capture provenance and limits are recorded in the [site maintenance guide](../../site/README.md#guide-images). These illustrations are separate from the maintained verification below; the print-preparation images do not show completed checks.

The saved workspace ID remains `make` (CAM). Process tabs are **Subtractive**, **3D FDM** and **3D Resin**, alongside Home, Analyze and View. Mold tooling is not part of this workspace.

**Import mold** in **Subtractive** and **3D Resin** adds the exact tooling solids from a `.prism_mld` copy to the current document. This gives the CNC and resin tools editable bodies. [Mold-only file rules and verification](../modeling/mold/README.md#saving-for-manufacturing).

Separate print tabs are justified by distinct checks and workflows, rather than different export formats:

- FDM: see the [3D FDM workflow](#3d-fdm).
- Resin: see the [3D Resin workflow](#3d-resin).

## Implemented CNC workflow

The owner approved the **Setup → Toolpaths → Review → Export** ribbon order on 2026-11-10. The Toolpaths menu retains the existing operations. **Post G-code** is under **Export**, after the review tools. Use the [Subtractive manual test checklist](../tabs/checklists/subtractive-manual-test-checklist.md) for controller and machine verification. [CAM tracking issue #52](https://github.com/chrizpro/prism/issues/52).

1. Create or edit a named setup: rectangular stock, G54 work zero and shared cutting settings. Named setups persist in the project; Select setup changes the active one. All use the global +Z tool axis.
2. Choose or save a tool/material preset in the local tool library. Existing generated paths retain their original settings.
3. Generate profiling (including tabs), pocketing/islands, adaptive clearing, drilling/pecking, facing, layered 3D raster roughing with stock allowance, parallel 3D finishing, Constant-Z Finish or turning.
4. Set Operation order. Hide operations to omit them. Program order persists with each path and is respected by posting, setup sheets and stock review.
5. Review source freshness and playback. Setup or source changes invalidate generated paths. Switching saved setups does not invalidate another setup's paths.
6. Run CNC Design Check for sampled top-down access, reach and thin walls; inspect inside corner and hole requirements.
7. Run Stock review on the active setup. A swept flat/ball cutter heightfield estimates material removed/remaining and possible gouges, rapid contacts and holder contacts. Program position can be set before rerunning to inspect intermediate stock.
7a. **Image Carve** (Subtractive tab, Toolpaths) carves a picture into the top of the material: type the picture's path and the width, choose whether dark or light areas are deepest, the bit (ball nose or flat) and the depth. Raster passes follow each pixel's brightness, so the toolpath shows the pattern the bit will cut; the picture keeps its proportions and is placed with its lower-left corner at *X from / Y from*. PNG files are read directly, other formats through the interface toolkit. Owner request 2026-11-08 ([#86](https://github.com/chrizpro/prism/issues/86)); placing the picture through the Appearance tab was considered and left as a later option. Test: CAM-H005.
8. Export a setup sheet and post one setup per program to GRBL, Mach3 or LinuxCNC. Turning and milling are separate programs. LinuxCNC gets G7 diameter mode; Mach3 Turn must be configured for diameter mode. Post's **Save to** takes a program file (missing folders are created) or a folder, which gets a timestamped `program_*.nc`; blank uses Documents\Prism\GCode. A drill or cut that goes below the stock bottom into the spoilboard is not counted as a rapid contact when the tool retracts.

Windows verification on 2026-11-10: 77 targeted CAM and ribbon cases passed. CAM-H004 checks section order, Toolpaths menu membership and posting through the ribbon to a folder and a file. CAM-I003 opened Post G-code from Export in the desktop build and wrote a 33-line GRBL facing program containing G21, G54 and feed moves. [Desktop capture](evidence/CAM-I003-subtractive-export.png). Controller interpretation and machining remain manual checks.

### Playing stock removal

The owner-approved wood demo (#131, 2026-11-15) requires showing the block being cut. **Stock Review → Play removal** starts from the current Program position, or restarts at zero after 100%. It recomputes the swept-cutter stock at 5% increments of feed-based program time; the marker identifies the current tool position. **Pause removal** stops the timer; **Next removal step** advances one interval. Changing Program position still needs Run checks for an individual review. Closing the command stops playback. This is a stepped sampled review, not real-time machine playback, and larger programs may compute slowly. Full-program toolpath lines are suppressed while this command is open so the stock surface remains readable; operations remain visible and included in the simulation.

The Walnut valet gallery demo uses a 1.8 mm grid, 8 mm holder diameter, 40 mm flute/holder clearance length and 0.3 mm comparison tolerance. These review settings describe the demo tool envelope; they are entered in Stock Review and are not saved as part of the CAM setup. Its four paths retain their individual cutter diameters. [Maintained CAM checks](../../Prism.Tests/Headless/Cam/CamProgramTests.cs) cover stepping/restart and uncluttered stock display; [GAL-I014](../../Prism.Tests/Integration/DesktopCncPatternGalleryTests.cs) exercises actual timer playback, saved paths and stock reduction. Windows verification on 2026-11-15 passed GAL-I014 with actual desktop timer playback and stock reduction; the final scoped Headless run passed 111 CAM/appearance/pattern cases (82 CAM cases).

### Sampled 3D finishing clearance

During the owner-approved walnut tray demo ([#131](https://github.com/chrizpro/prism/issues/131)), the nearest height-map lookup could choose a low sample beside a steep wall and underestimate cutter clearance. 3D Finish now takes the highest compensated height in the adjacent grid neighborhood, retaining a conservative sampled band near transitions. Flat stretches retain their height; narrow grooves and steep walls can retain additional material. [CNC-H020](../../Prism.Tests/Headless/Cam/CncOperationTests.cs) checks both flat and ball cutters and their interpolated segments against an off-grid step's analytic envelope. Windows verification on 2026-11-15 passed all 81 Headless CAM cases; GAL-I014 passed on the saved four-operation tray, with zero sampled gouges, rapid contacts and holder contacts at its documented review settings. This remains sampled screening, with the stock-review and machine checks described below.

### Constant-Z Finish

Owner approved 2026-11-10 ([#116](https://github.com/chrizpro/prism/issues/116)). In **Subtractive → Toolpaths**, **Constant-Z Finish** traces a fresh horizontal contour at each depth. Use it after roughing for steep walls. **3D Finish** still runs parallel passes over shallow surfaces.

- Create a saved +Z CAM setup for one exact solid. Use a flat end mill and enter the finish top, finish bottom and depth per pass. The first pass is one step below the finish top, or at the finish bottom when the range is smaller than one step. A final pass reaches the finish bottom, which must be above the model's lowest face.
- The operation offsets the section by the cutter radius plus a 0.05 mm margin for linearization. It screens each loop against the model above that height and skips contours that lack clearance. The result reports skipped contours or levels. Cutting stays at one height per loop, with a vertical entry and a retract to clearance between loops.
- The clearance screen uses the selected model and flat cutter envelope. It does not model the tool holder, fixtures, remaining stock, or machine limits. Use Pass View, Playback and Stock Review, then inspect the posted program in the controller simulator. See the [manual checklist](../tabs/checklists/subtractive-manual-test-checklist.md#constant-z-finish).
- Initial scope is closed contours, a flat cutter and up to 200 height levels. Ball-nose compensation, slope filtering, ramps and custom user-drawn paths are outside this operation.

[Maintained Constant-Z tests](../../Prism.Tests/Headless/Cam/ConstantZFinishTests.cs) cover a tapered cavity, an undercut, flat-height moves, retractions, setup validation and the GRBL post. Windows verification on 2026-11-10 passed 10 Unit and 79 Headless CAM cases. CAM-I004 created four contour levels on a tapered desktop model, committed the operation and saved the GRBL program. [Desktop capture](evidence/CAM-I004-constant-z-finish.png). The controller simulator and physical surface finish remain manual checks.

## Print preparation and handoff

Both print workflows accept exact CAD bodies, forms and imported meshes. Run checks explicitly after choosing the model/settings. Results and overlays are invalidated when those inputs change.

### 3D FDM

Owner-approved ribbon layout (2026-11-10, [#109](https://github.com/chrizpro/prism/issues/109)).

1. **Place on bed** previews rotating one CAD solid so the chosen flat face points down, then translating it to Z = 0. Confirm to apply an undoable change to the solid. A face that would put other geometry below the bed cannot be used.
2. **FDM check** uses the build width, depth and height entered here. It screens walls, overhangs and bed contact for the checked body. Filament estimate inputs are under Advanced.
3. **Troubleshoot geometry** groups Repair, Mesh Quality, Overhang & Orientation, Wall Thickness and Check geometry. Mesh splitting stays in the Mesh workspace. Nozzle-path import and playback are outside the FDM ribbon.
4. **Export for printing** reviews the body names, dimensions, grouping, scale and mesh topology before writing. When selection falls back to visible bodies, the review states this explicitly. Geometry findings are advisory. A change to the selection, model or export settings requires a fresh review. 3MF is the default.

[FDM preparation tests](../../Prism.Tests/Headless/Cam/FdmPreparationTests.cs) cover the ribbon, face placement and export review and 3MF structure. The [FDM manual test checklist](../tabs/checklists/3d-fdm-manual-test-checklist.md) covers the desktop and slicer handoff.

Windows verification on 2026-11-10: 71 maintained CAM cases and 11 standalone print-export checks passed. Desktop smoke test CAM-I001 picked a planar face through the viewport, committed placement, flagged a 40 mm part against a 35 mm build width, then reviewed and saved the 3MF. Bambu Studio CLI imported and re-exported one- and two-body samples with the same millimetre dimensions and object counts. The legacy ribbon suite passed the 3D FDM section and stopped at its Home Curves single-button assumption.

### 3D Resin

Owner-approved ribbon layout (2026-11-10, [#111](https://github.com/chrizpro/prism/issues/111)).

1. **Prepare** contains **Import mold**, **Tilt part**, **Hollow & Drain** and **Resin check**. Use **Tilt part** to set the intended build angle. Hollowing is optional. In **Hollow body**, select the whole CAD body for a closed hollow with the specified wall thickness. Select faces when you want to leave those faces open.
2. **Drain / vent hole** makes a real cut in the CAD body. Choose a plain **Blind** hole and a depth that reaches the cavity when only one wall should be pierced. **Through all** can open the opposite wall as well. Use **Section** under **Troubleshoot geometry** to inspect the cavity and opening.
3. **Resin check** screens the selected build dimensions, sealed cavities, trapped volume, potential cups, unsupported layer starts and thin features. Rerun it after changing the model or orientation. Findings depend on sampling resolution and the current +Z build direction.
4. **Export for resin** uses the same body and mesh review as FDM. Support placement, raft clearance, exposure settings and the final layer preview belong to the resin slicer.

[Resin preparation tests](../../Prism.Tests/Headless/Cam/ResinPreparationTests.cs) cover the ribbon and the closed-hollow-to-drained workflow. The [shared export review case](../../Prism.Tests/Headless/Cam/FdmPreparationTests.cs) runs for both print processes.

Windows verification on 2026-11-10: 83 maintained CAM and solid body-edit cases passed, along with 11 standalone print-export checks. In desktop smoke test CAM-I002, whole-body Shell produced a 2 mm wall on a 20 mm box. Resin Check reported one sealed cavity. A 6 mm diameter, 4 mm deep blind hole reduced the sealed-cavity count to zero. Section displayed the cutaway and the reviewed 3MF saved at 20 × 20 × 20 mm. The [desktop capture](evidence/CAM-I002-resin-section.png) shows the ribbon and section. Resin slicer launch, import and printer-specific validation remain manual checks.

The [Resin manual test checklist](resin-manual-test-checklist.md) covers the remaining hands-on checks.

### Shared export options

Export supports 3MF (default, named separate bodies in millimetres), STL, OBJ and PLY; selection/visible bodies; combined or one file per body; and tessellation quality. Exports replace a stable path through a temporary file. Export folder, format, quality, slicer and per-body choice are remembered per project and process in local preferences.

Slicer choices include Bambu Studio, OrcaSlicer, PrusaSlicer, CHITUBOX, Lychee Slicer, PreForm, or a custom executable. Install-path discovery is best effort; the program-path override works for any selection. Bambu Studio CLI import was verified for the 3MF fixtures below. Desktop launch behavior and the other slicers remain to be checked. Re-export and use the external application's reload/import workflow to update an existing model.

## Important limits and remaining work

This is a focused three-axis CAM/preparation workflow, not complete industrial CAM.

- Stock removal is a sampled 2.5D heightfield, not an exact solid simulation. It cannot represent undercuts, fixtures, rotary axes or machine kinematics. The grid can miss fine contacts. Holder size and flute clearance are user-supplied global review dimensions.
- Resin geometry is sampled on a bounded grid (at most about 82 cells per dimension). Thin walls, small drains, fine islands and narrow connections can be missed. A coarse clean report does not establish printability. Rerun finer checks and use final slicer validation.
- Resin layer sampling is geometric screening, not actual exposure-layer slicing or support generation. Printed-material overlap identifies unsupported starts; peel physics and support strength are not calculated.
- Named setups currently vary stock/body/origin, not tool-axis orientation. Rotated workplanes, indexed/multi-axis machining and a full setup manager remain future work.
- 3D roughing uses raster layers, a flat cutter, sampled clearance and vertical entries. It does not include rest machining, constant-engagement 3D roughing, helical entry or scallop finishing.
- CNC corner and hole guidance is not automatic recognition or certification of every radius, hole and tool-access constraint.
- Posts beyond GRBL/Mach3/LinuxCNC, exact tool assemblies, automatic fixture checks and machine-specific prove-out remain future work. Do not add nominal controller support without checking its dialect.
- Mesh hollowing, lattices, automatic support-contact placement and material-specific resin process profiles are deferred to dedicated tools.

## Validation

`tests/Prism.CamTests` covers analytical resin cavity/cup/vent/island cases, stock removal and contact regressions, direct CAD/mesh checks, named-setup freshness and roundtrip, operation ordering, post validation and generated 3D roughing. It renders process ribbons and print panels in dark/light themes to `docs/cam-workflows`.

Also run `Prism.PrintExportTests`, `Prism.RibbonTests`, `Prism.ApiCoverageTests` and `Prism.WorkflowTests` after CAM changes. CNC machine testing remains outstanding.

Resin design references: [Formlabs cups](https://formlabs.com/support/Preventing-suction-cups-in-PreForm/), [Formlabs orientation](https://formlabs.com/support/Model-Orientation/), [Prusa hollowing](https://help.prusa3d.com/article/hollowing_117285?product=sl1s-speed). Controller reference: [LinuxCNC G7/G8](https://www.linuxcnc.org/docs/2.8/html/gcode/g-code.html), [Mach3 Turn manual](https://www.machsupport.com/ftp/Docs/Mach3Turn_1.84.pdf).

## Maintained CAM checks

[Linear G-code unit tests](../../Prism.Tests/Unit/Cam/LinearGcodeTests.cs) verify units, modal coordinates, G92 offsets, extrusion, layer starts, feed and rejection of unsupported input. [CAM workflow headless tests](../../Prism.Tests/Headless/Cam/CamWorkflowTests.cs) check committed stock bounds and both G54 choices, plus successful nozzle import and history preservation when an arc is rejected. Run `./tools/test.ps1 -Feature CAM`. [Issue #52](https://github.com/chrizpro/prism/issues/52).

[CNC operation tests](../../Prism.Tests/Headless/Cam/CncOperationTests.cs) check the cut path of Profile on each side, its passes, climb and conventional direction and tabs; Pocket rings and islands; Adaptive Clearing levels and wall finish; Drill depth, pecks and tool warning; Face in both directions; 3D Finish with both cutters; Turning stages and stock; and tool-library presets. [CAM program tests](../../Prism.Tests/Headless/Cam/CamProgramTests.cs) cover 3D Rough, Tool Library, Select CAM Setup, Operation Order, Playback, Review and its setup sheet, Post for each controller, Stock Review, CNC Design Check, FDM and Resin Preparation.

Fixed on 2026-11-05:

- CNC Design Check counted the vertical walls of a plain block as "top-down inaccessible", because its upward probe started on the wall's own plane. The probe now starts just outside the face, and faces pointing down count as hidden directly. Tests cover a pocketed block (underside only) and an overhanging shelf.
- Turning set "Top of the material" while building, which started a nested preview and listed the result rows twice. It is now set without notifying.

The standalone suites cover the legacy 3D-print Slice tool and additional print formats. The maintained FDM tests cover review and the 3MF file boundary.

Windows verification on 2026-11-05: the combined candidate passed 86 Unit and 592 Headless tests with rebuilt native libraries. See the [integration verification report](../engineering/release-readiness-2026-11-05.md) for source revisions, commands and limits.

## Arc soap mold CAM demonstration

Owner-authorized 2026-11-16 ([#136](https://github.com/chrizpro/prism/issues/136)): the [Arc soap tooling study](../interface/gallery/README.md#arc-soap-mold-and-cam--november-16-native-study) has two cavity-up +Z setups, each with facing, registration-hole peck drilling, raster roughing and ball-nose finishing. Both saved setup/source reviews accepted their four paths after reopen. Sampled stock review completed with zero reported gouges, rapid contacts or holder contacts at the documented grids; conservative finishing material remains around small details. The gallery guide records exact review settings, native files and actual Prism screenshots. No physical machining, renderer or test suite was run, and no CAM behavior changed.

## Known roughing stock-top comparison issue

Confirmed 2026-11-16 during the Arc soap mold study ([#138](https://github.com/chrizpro/prism/issues/138)): a tooling half rotated 180° had serialized stock top 26.00000000000001 mm. Entering a roughing top of 26 mm was refused by the strict stock-top comparison; 26.01 mm succeeded with all other settings unchanged. The saved gallery paths use the latter workaround. [Actual rejected/accepted preview evidence](../interface/gallery/evidence/modeling-bug-findings.json). This is an unresolved numerical comparison bug; no product fix or regression pass is claimed. Existing [CAM program coverage](../../Prism.Tests/Headless/Cam/CamProgramTests.cs) remains unchanged.

## Multi-part router jobs — proposed extension

Owner requested planning the observatory's parts for a **600 × 400 mm** demo bed on 2026-11-16 ([#140](https://github.com/chrizpro/prism/issues/140)). Current named setups each bind one model body; 3D roughing/finishing and Stock Review use that body. Multiple 2D outlines can be machined, but shared-sheet multi-part stock review and assembly-linked bed layouts are not a complete supported workflow.

Proposed for owner review ([#141](https://github.com/chrizpro/prism/issues/141)): linked part instances on a stock/fixture layout, quantities and labels, one named setup per physical bed load or flip, a common work zero, multi-body source freshness and stock review, operation ordering across parts, and separate posted programs/setup sheets. Manual placement should precede optional automatic nesting. No new product behavior or requirements tests are implemented by this proposal. The [observatory inventory and router work breakdown](../../gallery/extras/maple-observatory-router-plan.md) records fabrication decisions needed before toolpath generation.
