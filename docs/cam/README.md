# CAM status and scope

Updated 2026-10-31.

Prism focuses on CNC machining and preparation for FDM and resin printing. Dedicated slicers handle supports, slicing, exposure, printer profiles and printer delivery. Do not expand the legacy built-in FDM slicer.

## Workspace

The public [Manufacturing walkthrough](../../site/guide/manufacturing.md) includes actual app captures of stock setup, profile preview, playback and FDM/resin preparation settings. Capture provenance and limits are recorded in the [site maintenance guide](../../site/README.md#guide-images). These illustrations are separate from the maintained verification below; the print-preparation images do not show completed checks.

The saved workspace ID remains `make` (CAM). Process tabs are **Subtractive**, **3D FDM** and **3D Resin**, alongside Home, Analyze and View. Mold tooling is not part of this workspace.

**Import mold** in **Subtractive** and **3D Resin** adds the exact tooling solids from a `.prism_mld` copy to the current document. This gives the CNC and resin tools editable bodies. [Mold-only file rules and verification](../modeling/mold/README.md#saving-for-manufacturing).

Separate print tabs are justified by distinct checks and workflows, rather than different export formats:

- FDM: nozzle/perimeter wall targets, overhangs, bed contact, build fit, orientation guidance and filament estimates. Imported linear nozzle G-code can be played back.
- Resin: closed cavities, sampled trapped volume, potential unvented cups during build progression, unsupported layer starts, minimum features, peak layer area and resin estimates. Hollowing and drain/vent holes reuse exact CAD tools. Resin slicing/supports/exposure stay external.

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

Export supports 3MF (default, named separate bodies in millimetres), STL, OBJ and PLY; selection/visible bodies; combined or one file per body; and tessellation quality. Exports replace a stable path through a temporary file. Export folder, format, quality, slicer and per-body choice are remembered per project and process in local preferences.

Slicer choices include Bambu Studio, OrcaSlicer, PrusaSlicer, CHITUBOX, Lychee Slicer, PreForm, or a custom executable. Install-path discovery is best effort; the program-path override works for any selection. Real application launch/import compatibility is not yet verified. Re-export and use the external application's reload/import workflow to update an existing model.

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

Also run `Prism.PrintExportTests`, `Prism.RibbonTests`, `Prism.ApiCoverageTests` and `Prism.WorkflowTests` after CAM changes. Real slicer and CNC machine testing remain outstanding.

Resin design references: [Formlabs cups](https://formlabs.com/support/Preventing-suction-cups-in-PreForm/), [Formlabs orientation](https://formlabs.com/support/Model-Orientation/), [Prusa hollowing](https://help.prusa3d.com/article/hollowing_117285?product=sl1s-speed). Controller reference: [LinuxCNC G7/G8](https://www.linuxcnc.org/docs/2.8/html/gcode/g-code.html), [Mach3 Turn manual](https://www.machsupport.com/ftp/Docs/Mach3Turn_1.84.pdf).

## Maintained CAM checks

[Linear G-code unit tests](../../Prism.Tests/Unit/Cam/LinearGcodeTests.cs) verify units, modal coordinates, G92 offsets, extrusion, layer starts, feed and rejection of unsupported input. [CAM workflow headless tests](../../Prism.Tests/Headless/Cam/CamWorkflowTests.cs) check committed stock bounds and both G54 choices, plus successful nozzle import and history preservation when an arc is rejected. Run `./tools/test.ps1 -Feature CAM`. [Issue #52](https://github.com/chrizpro/prism/issues/52).

[CNC operation tests](../../Prism.Tests/Headless/Cam/CncOperationTests.cs) check the cut path of Profile on each side, its passes, climb and conventional direction and tabs; Pocket rings and islands; Adaptive Clearing levels and wall finish; Drill depth, pecks and tool warning; Face in both directions; 3D Finish with both cutters; Turning stages and stock; and tool-library presets. [CAM program tests](../../Prism.Tests/Headless/Cam/CamProgramTests.cs) cover 3D Rough, Tool Library, Select CAM Setup, Operation Order, Playback, Review and its setup sheet, Post for each controller, Stock Review, CNC Design Check, FDM and Resin Preparation.

Fixed on 2026-11-05:

- CNC Design Check counted the vertical walls of a plain block as "top-down inaccessible", because its upward probe started on the wall's own plane. The probe now starts just outside the face, and faces pointing down count as hidden directly. Tests cover a pocketed block (underside only) and an overhanging shelf.
- Turning set "Top of the material" while building, which started a nested preview and listed the result rows twice. It is now set without notifying.

The 3D-print Slice tool, Export for printing and slicer hand-off remain without maintained cases; the standalone suites cover them.

Windows verification on 2026-11-05: the combined candidate passed 86 Unit and 592 Headless tests with rebuilt native libraries. See the [integration verification report](../engineering/release-readiness-2026-11-05.md) for source revisions, commands and limits.
