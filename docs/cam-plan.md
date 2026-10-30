# CAM plan

Status: milestone 1 is partly built. The CAM ribbon tab is now Subtractive, and Additive has "Export for printing" (folder, 3MF/STL/OBJ/PLY, curve quality, one file per body, open in Bambu Studio/OrcaSlicer/PrusaSlicer/other). Not yet tested against a real slicer. Everything else below is still planned.

## Direction

Prism makes the model manufacturable; dedicated tools do the rest. Focus on **CNC (subtractive)** and **3D printing (additive)**. We do not compete with slicers. Injection molding is a later, separate "Mold" workspace.

## What exists today

- Workspace `make` ("CAM"), tabs: Home, CAM, Additive, Tooling, Analyze, View (`Prism/ViewModels/MainViewModel.Workspaces.cs`).
- CNC operations: profile, pocket, adaptive clear, turn, parallel finish, drill, face mill (`Prism/Commands/CamCommands.cs`).
- Posts: GRBL, Mach3, LinuxCNC; lathe and print posts (`Prism/Core/Cam/Post.cs`).
- Setup: stock box, work zero, tool and feeds (`CamSetup.cs`).
- Playback: `PathTimeline` (time to tool position) and `ToolpathViewCommand`. This is the base for the bit animation.
- Built-in slicer (`Slicer.cs`). To be removed later, once the hand-off is solid. Do not grow it.

## Tabs

- **Subtractive** (CNC): Setup, Operations, Tools, Simulate, Post.
- **Additive** (3D printing): Printability, Orientation, Export.
- Shared: Tooling, Analyze, View.
- Possible third process tab later: Sheet/Cut (laser, waterjet, plasma), reusing sketch and sheet-metal flat patterns.

## Export and send to slicer

Export dialog:
- Formats: 3MF (default), STL binary and ASCII, OBJ, STEP.
- Chosen folder, remembered per project.
- Bodies: selected, visible, or all. One file per body or one combined file.
- Tessellation quality (chord and angle tolerance).
- Units and Z-up orientation, written into the 3MF.

Send to slicer:
- User picks a slicer in Settings; default is "just reveal the file".
- Export to a stable path (for example `<project>/print/part.3mf`) and launch the slicer with that file.
- If the slicer is already open, tell the user to use Reload from disk. There is no API to push into an open slicer, and a second launch may open a new window depending on the slicer and its single-instance setting.
- Optional: a file watcher re-exports when the model changes.
- First target: Bambu Studio (tested manually by the user). PrusaSlicer and OrcaSlicer are expected to behave similarly but are unverified.

## Additive: printability checks

- Overhang shading with a threshold, and bridging.
- Minimum wall and feature size against nozzle diameter.
- Bed fit, orientation advice (overhangs, supports, strength direction).
- Mesh health: watertight, non-manifold edges, inverted normals.
- Later: lattices and gyroid fill (a modelling feature, large).
- Skipped: STEP/NURBS slicing. Slicers tessellate STEP themselves; we only control our own tessellation.

## Resin (later)

A printability profile alongside FDM: trapped volumes and drain holes, cupping, layer islands, tilt orientation, support touch points, minimum feature size.

## Subtractive: design-for-CNC checks

- Tool access and reachability per direction.
- Minimum inside corner radius against the smallest tool.
- Thin walls and floors, deep pockets (depth to diameter).
- Undercuts and number of setups or flips.
- Hole checks: standard sizes, depth to diameter, bottom shape.
- Suggested fixes (for example, fillet these edges).

## Subtractive: workflow

- Stock setup, tool library with feeds and speeds by material, multiple setups (WCS).
- Operation ordering and cycle-time estimates.
- More posts (Fanuc, Haas, Siemens).

## Bit animation and simulation

- Phase A: animate tool and holder along `PathTimeline`, with play/pause, scrub, speed, rapids in a different colour.
- Phase B: material removal (heightmap for 3-axis; `HeightMap.cs` is a start).
- Phase C: collision detection, and overlay of cut stock against the target to show leftover or gouges.

## Order of work

1. Split tabs into Subtractive and Additive; export dialog and Send to slicer (Bambu Studio).
2. Overhang shading and the printability panel.
3. Bit animation, Phase A.
4. CNC design-for-manufacture checks.
5. Material removal simulation, resin checks, lattices.

## Open questions

- Who are the first CNC users: hobby routers (GRBL class) or small production mills? This sets the weight on posts and tool libraries.
- Which slicers should be supported beyond Bambu Studio?
