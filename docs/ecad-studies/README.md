# ECAD essentials: mechanical integration

This first pass adds an ECAD tab to the Design workspace, focused on board/enclosure integration. It uses physical CAD solids, with no new simulator, schematic editor or electrical routing engine.

## Tools

- **Create board > Rectangular board:** width, length, thickness (default 1.6 mm), bottom elevation, and four optional mounting holes. Centered on world XY; green FR4/G10 material assigned. Hole spacing is validated to retain edge material and prevent neighboring holes from overlapping.
- **Create board > Board from outline:** extrude one closed planar sketch/curve/face region into a board, retaining its source outline. Internal cutouts can be included in the region.
- **Mounting Hole:** cut an individual mechanical through-hole at world XY coordinates, along world Z. Intended for boards lying in XY; position/rotate a completed assembly afterward. A non-intersecting cut reports an error.
- **Component Envelope:** approximate package or connector geometry named with a reference designator, XY center, mounting-face elevation, rotation, top/bottom side and optional mechanical clearance allowance. Bottom-side envelopes grow away from the mounting face. These are keepout/package volumes, without pin or footprint connectivity. Placement elevations are explicit values, not associative board references.
- **Place & align:** existing move, rotate and duplicate tools.
- **Check fit:** existing interference, measurement and section tools for mechanical clearances. These do not implement electrical design-rule checks.
- **Exchange:** STEP import/export for board and component geometry. Imports use the existing generic STEP reader; there is no native KiCad/IDF parser or electrical metadata roundtrip.

Layer stackups, copper, nets, traces, vias, schematics, Gerber/drill fabrication files and circuit simulation are outside this first pass. FR4 thickness describes a physical solid, not a defined electrical stackup.

## Examples and verification

`controller-board.prism` is an 80 x 50 x 1.6 mm board with four mounting holes, an IC envelope and two connector envelopes. `controller-board.step` exports its four solids. Live MCP interference checks found no overlaps among the four bodies. `controller-board.png` records the viewport.

`ecad-mechanical-demo.prism` and matching STEP are automated test fixtures, including an additional custom-outline board and a bottom-side keepout. They exercise geometry rather than represent a finished electrical design.

- `dotnet run --project tests/Prism.EcadTests`: 17 checks passed for volume/thickness/material, hole cuts and failure cases, undo/redo, top/bottom placement, rotation, clearance allowance, custom outlines, save/reopen and STEP roundtrip.
- `dotnet run --project tests/Prism.RibbonTests`: 34 checks passed, including ECAD command availability and its 1024-pixel layout. The ECAD ribbon fits at that width.
- Desktop Release build passed. The four new commands are also exposed through MCP: `pcb_board`, `pcb_from_outline`, `pcb_hole`, `component_envelope`.

For enclosure work with a dedicated PCB editor, [KiCad can export the PCB and STEP component models as a 3D assembly](https://docs.kicad.org/master/en/pcbnew/pcbnew.html). That is the intended exchange path for detailed electrical designs.
