# CAM status and scope

Updated 2026-10-31.

Prism focuses on CNC machining and preparation for FDM and resin printing. Dedicated slicers handle supports, slicing, exposure, printer profiles and printer delivery. Do not expand the legacy built-in FDM slicer.

## Workspace

The public [Manufacturing walkthrough](../../site/guide/manufacturing.md) includes actual app captures of stock setup, profile preview, playback and FDM/resin preparation settings. Capture provenance and limits are recorded in the [site maintenance guide](../../site/README.md#guide-images). These illustrations are separate from the maintained verification below; the print-preparation images do not show completed checks.

The saved workspace ID remains `make` (CAM). Process tabs are **Subtractive**, **3D FDM** and **3D Resin**, alongside Home, Analyze and View. Mold tooling is not part of this workspace.

Separate print tabs are justified by distinct checks and workflows, rather than different export formats:

- FDM: see the [3D FDM workflow](#3d-fdm).
- Resin: see the [3D Resin workflow](#3d-resin).

## Implemented CNC workflow

1. Create or edit a named setup: rectangular stock, G54 work zero and shared cutting settings. Named setups persist in the project; Select setup changes the active one. All use the global +Z tool axis.
2. Choose or save a tool/material preset in the local tool library. Existing generated paths retain their original settings.
3. Generate profiling (including tabs), pocketing/islands, adaptive clearing, drilling/pecking, facing, layered 3D raster roughing with stock allowance, parallel 3D finishing or turning.
4. Set Operation order. Hide operations to omit them. Program order persists with each path and is respected by posting, setup sheets and stock review.
5. Review source freshness and playback. Setup or source changes invalidate generated paths. Switching saved setups does not invalidate another setup's paths.
6. Run CNC Design Check for sampled top-down access, reach and thin walls; inspect inside corner and hole requirements.
7. Run Stock review on the active setup. A swept flat/ball cutter heightfield estimates material removed/remaining and possible gouges, rapid contacts and holder contacts. Program position can be set before rerunning to inspect intermediate stock.
7a. **Image Carve** (Subtractive tab, Toolpaths) carves a picture into the top of the material: type the picture's path and the width, choose whether dark or light areas are deepest, the bit (ball nose or flat) and the depth. Raster passes follow each pixel's brightness, so the toolpath shows the pattern the bit will cut; the picture keeps its proportions and is placed with its lower-left corner at *X from / Y from*. PNG files are read directly, other formats through the interface toolkit. Owner request 2026-11-08 ([#86](https://github.com/chrizpro/prism/issues/86)); placing the picture through the Appearance tab was considered and left as a later option. Test: CAM-H005.
8. Export a setup sheet and post one setup per program to GRBL, Mach3 or LinuxCNC. Turning and milling are separate programs. LinuxCNC gets G7 diameter mode; Mach3 Turn must be configured for diameter mode. Post's **Save to** takes a program file (missing folders are created) or a folder, which gets a timestamped `program_*.nc`; blank uses Documents\Prism\GCode. A drill or cut that goes below the stock bottom into the spoilboard is not counted as a rapid contact when the tool retracts.

## Print preparation and handoff

Both print workflows accept exact CAD bodies, forms and imported meshes. Run checks explicitly after choosing the model/settings. Results and overlays are invalidated when those inputs change.

### 3D FDM

Owner-approved ribbon layout (2026-11-10, [#109](https://github.com/chrizpro/prism/issues/109)).

1. **Place on bed** previews rotating one CAD solid so the chosen flat face points down, then translating it to Z = 0. Confirm to apply an undoable change to the solid. A face that would put other geometry below the bed cannot be used.
2. **FDM check** uses the build width, depth and height entered here. It screens walls, overhangs and bed contact for the checked body. Filament estimate inputs are under Advanced.
3. **Troubleshoot geometry** groups Repair, Mesh Quality, Overhang & Orientation, Wall Thickness and Check geometry. Mesh splitting stays in the Mesh workspace. Nozzle-path import and playback are outside the FDM ribbon.
4. **Export for printing** reviews the body names, dimensions, grouping, scale and mesh topology before writing. When selection falls back to visible bodies, the review states this explicitly. Geometry findings are advisory. A change to the selection, model or export settings requires a fresh review. 3MF is the default.

[FDM preparation tests](../../Prism.Tests/Headless/Cam/FdmPreparationTests.cs) cover the ribbon, face placement and export review and 3MF structure.

Windows verification on 2026-11-10: 71 maintained CAM cases and 11 standalone print-export checks passed. Desktop smoke test CAM-I001 picked a planar face through the viewport, committed placement, flagged a 40 mm part against a 35 mm build width, then reviewed and saved the 3MF. Bambu Studio CLI imported and re-exported one- and two-body samples with the same millimetre dimensions and object counts. The legacy ribbon suite passed the 3D FDM section and stopped at its Home Curves single-button assumption.

### 3D Resin

Owner-approved ribbon layout (2026-11-10, [#111](https://github.com/chrizpro/prism/issues/111)).

1. **Prepare** contains **Tilt part**, **Hollow & Drain** and **Resin check**. Use **Tilt part** to set the intended build angle. Hollowing is optional. In **Hollow body**, select the whole CAD body for a closed hollow with the specified wall thickness. Select faces when you want to leave those faces open.
2. **Drain / vent hole** makes a real cut in the CAD body. Choose a plain **Blind** hole and a depth that reaches the cavity when only one wall should be pierced. **Through all** can open the opposite wall as well. Use **Section** under **Troubleshoot geometry** to inspect the cavity and opening.
3. **Resin check** screens the selected build dimensions, sealed cavities, trapped volume, potential cups, unsupported layer starts and thin features. Rerun it after changing the model or orientation. Findings depend on sampling resolution and the current +Z build direction.
4. **Export for resin** uses the same body and mesh review as FDM. Support placement, raft clearance, exposure settings and the final layer preview belong to the resin slicer.

[Resin preparation tests](../../Prism.Tests/Headless/Cam/ResinPreparationTests.cs) cover the ribbon and the closed-hollow-to-drained workflow. The [shared export review case](../../Prism.Tests/Headless/Cam/FdmPreparationTests.cs) runs for both print processes.

Windows verification on 2026-11-10: 83 maintained CAM and solid body-edit cases passed, along with 11 standalone print-export checks. In desktop smoke test CAM-I002, whole-body Shell produced a 2 mm wall on a 20 mm box. Resin Check reported one sealed cavity. A 6 mm diameter, 4 mm deep blind hole reduced the sealed-cavity count to zero. Section displayed the cutaway and the reviewed 3MF saved at 20 × 20 × 20 mm. The [desktop capture](evidence/CAM-I002-resin-section.png) shows the ribbon and section. Resin slicer launch, import and printer-specific validation remain manual checks.

#### Resin manual test checklist

- [ ] Open a hollow part you know well. Tilt it and use **Troubleshoot geometry → Section** to sweep through each drain or vent. Confirm each opening reaches its cavity and exits where you intended.
- [ ] Run **Resin check** at the intended print angle. With a shallow or narrow opening, compare the result at a finer sampling size against the section and slicer preview.
- [ ] Export to your usual resin slicer with **Export and open in slicer**. Try once with the slicer closed and once while it is open. Check that the expected bodies, dimensions, orientation and holes arrive. If your slicer requires STL, repeat with STL and millimetre scale.
- [ ] With your real printer profile, add supports and a raft if needed. Check build fit and inspect the layers around the cavities, drain openings and unsupported starts. A physical print, if you choose to run one, is a separate check.

### Shared export options

Export supports 3MF (default, named separate bodies in millimetres), STL, OBJ and PLY; selection/visible bodies; combined or one file per body; and tessellation quality. Exports replace a stable path through a temporary file. Export folder, format, quality, slicer and per-body choice are remembered per project and process in local preferences.

Slicer choices include Bambu Studio, OrcaSlicer, PrusaSlicer, CHITUBOX, Lychee Slicer, PreForm, or a custom executable. Install-path discovery is best effort; the program-path override works for any selection. Bambu Studio CLI import was verified for the 3MF fixtures below. Desktop launch behavior and the other slicers remain to be checked. Re-export and use the external application's reload/import workflow to update an existing model.

## Important limits and remaining work

This is a focused three-axis CAM/preparation workflow, not complete industrial CAM.

- Stock removal is a sampled 2.5D heightfield, not an exact solid simulation. It cannot represent undercuts, fixtures, rotary axes or machine kinematics. The grid can miss fine contacts. Holder size and flute clearance are user-supplied global review dimensions.
- Resin geometry is sampled on a bounded grid (at most about 82 cells per dimension). Thin walls, small drains, fine islands and narrow connections can be missed. A coarse clean report does not establish printability. Rerun finer checks and use final slicer validation.
- Resin layer sampling is geometric screening, not actual exposure-layer slicing or support generation. Printed-material overlap identifies unsupported starts; peel physics and support strength are not calculated.
- Named setups currently vary stock/body/origin, not tool-axis orientation. Rotated workplanes, indexed/multi-axis machining and a full setup manager remain future work.
- 3D roughing uses raster layers, a flat cutter, sampled clearance and vertical entries. It does not include rest machining, constant-engagement 3D roughing, helical entry or scallop/constant-Z finishing.
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
