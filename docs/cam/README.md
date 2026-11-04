# CAM status and scope

Updated 2026-10-31.

Prism focuses on CNC machining and preparation for FDM and resin printing. Dedicated slicers handle supports, slicing, exposure, printer profiles and printer delivery. Do not expand the legacy built-in FDM slicer.

## Workspace

The saved workspace ID remains `make` (CAM). Process tabs are **Subtractive**, **3D FDM** and **3D Resin**, alongside Home, Analyze and View. Mold tooling is not part of this workspace.

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
8. Export a setup sheet and post one setup per program to GRBL, Mach3 or LinuxCNC. Turning and milling are separate programs. LinuxCNC gets G7 diameter mode; Mach3 Turn must be configured for diameter mode.

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
