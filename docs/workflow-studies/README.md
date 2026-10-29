# CAE and CAM workflow essentials

Implemented and tested in Prism on 2026-10-27. The screenshots and example files come from the real application through its MCP bridge.

## CAE

- Static setups, supports, load vectors and mesh sizes now save in the model document. Thermal and modal commands have **Save setup**; **Saved setups** reruns them. **Select static study** switches between static setups.
- **Save face selection** names faces from one body. **Recall face selection** restores them as the current selection so another command can consume them.
- Body IDs survive save/reopen. References include a geometry signature. Changes to the exact geometry invalidate saved boundary indices instead of guessing which new face they mean. Create a new setup/reselect faces after geometry edits; automatic topology reassociation is not implemented.
- **Mesh refinement** runs h, 0.7h and 0.49h for a static study. It reports maximum displacement change, node counts and peak von Mises stress, with optional CSV output. Cancellation takes effect after the current native solve. There is a coarse-size guard to avoid excessively large refinement runs.
- Refinement does not issue an automatic accuracy pass. A stable global displacement does not prove local stress convergence; sharp support/load singularities can raise peak stress indefinitely.
- **Export results** writes static nodal coordinates, displacement components, von Mises stress and available yield-based safety factors to CSV, plus setup/material/notes in JSON. Missing yield factors are empty CSV fields. Thermal and modal solver files remain available in their result folder.
- Native result handles are not serialized. Reopen a setup and solve again, or use the exported results. Solver results are invalidated on source changes; asynchronous static runs also check source and setup freshness before applying their result.
- Setup edits remain separate from the geometry undo tree, as the existing static-study controls were. Full study undo/redo and automatic face remapping remain future work.

### Recorded checks

The saved live cantilever is 10 × 10 × 40 mm, 6061, fixed at the bottom with a 100 N axial load. After reopening, it returned 627 nodes / 268 quadratic tetrahedra, 100 N net reaction and about 0.00058 mm maximum displacement. The simple axial estimate is 100 × 40 / (68900 × 100) = 0.00058055 mm.

The same document stores an **Axial conduction** case and a **Base support** face group. Rerunning the saved thermal case after reopening completed with 20–100 °C extrema. `live-saved-thermal.json` records the result folder.

The automated 10 × 10 × 20 mm axial comparison uses three actual meshes. `convergence.csv` contains the measurements. The final displacement change is about 0.068%; the peak stress rises near constrained faces. This demonstrates why the comparison reports both quantities without turning peak stress into a blanket convergence verdict.

## CAM workspace

The workspace formerly named Manufacture is now **CAM**, retaining the saved workspace ID `make` and accepting `cam` as an alias. Its tabs are **CAM**, **Additive**, **Tooling**, plus shared Home/Analyze/View.

The workflow follows setup → operation → review → output:

1. **CAM setup** selects one solid, adds XY/top/bottom stock allowance, chooses stock-corner G54 or model origin, and records cutter diameter, feeds, spindle speed and clearance.
2. **Toolpaths** creates Profile, Pocket, Drill or Face operations using the setup defaults. Operations retain their actual settings and geometry/setup provenance. Changing a setup requires regeneration; editing/deleting a source rejects its old paths during review/playback/post.
3. **Playback** animates a cutter at feed-based speed, with Play/resume, Pause, Seek, Reset and a speed multiplier. The completed path builds up behind it. Stock is shown as an axis-aligned wire box, with the G54 axes labeled.
4. **Setup sheet** reviews freshness and timings and exports HTML + JSON. **Post G-code** defaults to the saved setup's G54 origin, with existing GRBL/Mach3/LinuxCNC posts.

This release uses global +Z as the tool axis and an untranslated-axis work coordinate system (origin translation only). It does not add rotated WCS, multiple simultaneous setups, 3D roughing, turning, adaptive machining, feeds-and-speeds recommendations, stock removal or machine/fixture collision detection. A below-clearance lateral-rapid check is included; it is not a collision simulator. NC examples here have not been run on a physical machine.

### Additive playback

- Export geometry as STL/3MF to a dedicated slicer; no slicing work was added. The earlier basic slicer remains accessible to existing scripts but is no longer promoted in the ribbon.
- **Import nozzle path** reads a restricted Marlin-style Cartesian G-code subset for preview. Supported: G0/G1, modal feeds, G20/G21 units, G90/G91 positioning, G92 position resets, and M82/M83 extrusion modes. G90/G91 clear the extruder-mode override, matching Marlin semantics.
- A stylized nozzle/head travels above the path. Extruding and travel moves use different path styles; Z changes on extruding moves define layers. **Layer view** can inspect layers separately. Playback resets the layer limit to show the progressing path.
- Feed-based time retains each imported move's feed. Acceleration, dwell, warmup, homing and extrusion physics are not simulated. Initial position is assumed [0,0,0]; initial G28 is omitted with a note. Homing after motion, arcs, bed leveling, unsupported machine commands, additional axes and multiple nozzles are rejected rather than approximated silently.
- This is a strict subset, not a general G-code interpreter; some external slicer start/end macros need compatible preview input. Nothing is transmitted to a printer or CNC controller.
- Paths have input limits (32 MB, 500,000 moves, bounded finite coordinates). Invalid feeds, layer offsets, move types and malformed coordinate text are rejected. Zoom extents now includes paths even when a document contains no model body.
- Above 40,000 moves, the viewport uses a bounded sparse overview of actual segments instead of drawing every segment each frame. Playback still follows every move. The playback panel identifies this display limit; sparse gaps do not represent missing program moves.

`external-linear-demo.gcode` is a deliberately small preview test fixture, not machine-qualified print output. `nozzle-preview.prism` contains its imported path. `cam-plate.prism` stores a model/stock setup and facing operation; `plate-output` contains its setup sheet and NC example.

## Persistence and validation

Document version 2 includes persistent object identities and analysis/CAM setups; version 1 files still load. New native B-rep output excludes mutable display triangulations. Signatures also ignore OCCT's transient Free/Modified/Checked bookkeeping flags, retaining geometric and topological data. Tests explicitly remesh and reopen geometry to check that these operations do not falsely stale references.

Checks and logs:

- `Prism.WorkflowTests`: **43 checks**, including document/setup/reference roundtrips, actual three-mesh solves, path timing, G-code modes and rejection cases, saved G54 translation, low-rapid rejection, path-only zoom, panel ordering and bounded large-path display.
- `Prism.RibbonTests`: **39 checks**, including CAM/Additive/Tooling at a 1024 px width and existing ribbon controls.
- `Prism.CaeTests`: **19 checks**, including analytic conduction, transient conduction, modal frequencies and axial static displacement.
- `Prism.MaterialTests`: **100 checks** for engineering data, pinned property sets, legacy documents, unsupported material models and undefined yield factors.
- `Prism.RenderTests`: **23 checks**, including material persistence and rendering regressions.

No runtime dependencies were added. The desktop build retains its pre-existing Tmds.DBus.Protocol NuGet vulnerability warning and existing unrelated compiler warnings.

## Primary references

- [COMSOL mesh refinement guidance](https://www.comsol.com/support/knowledgebase/1261) — compare outputs across mesh refinement and consider the quantities of interest.
- [Marlin linear moves](https://marlinfw.org/docs/gcode/G000-G001.html)
- [Marlin M82 extrusion mode](https://marlinfw.org/docs/gcode/M082.html)
- [Marlin G92 position reset](https://marlinfw.org/docs/gcode/G092.html)
