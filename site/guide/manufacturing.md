# Manufacturing

The **CAM** workspace prepares parts for CNC machining, FDM printing and resin printing. Reference: [Subtractive](/tools/subtractive), [3D FDM](/tools/3d-fdm), [3D Resin](/tools/3d-resin).

## CNC machining

1. **CAM setup**: rectangular stock, G54 work zero and shared cutting settings. Named setups are saved with the project; **Select setup** changes the active one.
2. **Tool library**: choose or save a tool and material preset.
3. Generate operations: **Profile** (with tabs), **Pocket** (with islands), **Adaptive** clearing, **Drill** (with pecking), **Face**, **3D Rough**, **3D Finish** or **Turning**.
4. **Operation order** sets the program order; hidden operations are left out of posting.
5. Review with **Playback** and **Pass view**. Changing the setup or the model marks paths as out of date.
6. **Design check** screens top-down access, reach and thin walls. **Stock review** estimates removed and remaining material and possible gouges and holder contacts.
7. Export a **Setup sheet** and **Post G-code** for GRBL, Mach3 or LinuxCNC. Milling and turning are separate programs.

This is a focused three-axis workflow. Stock review samples a heightfield; it cannot show undercuts, fixtures or machine kinematics, and Playback previews motion only. Prove out programs on your machine.

## FDM printing

1. **FDM check** screens bed fit, nozzle walls, overhangs and orientation, with filament estimates.
2. **Orient part**, **Split mesh** and **Repair** prepare the geometry. **Wall Thickness** and **Overhang & Orientation** color problem areas.
3. **Export for printing** writes 3MF (named bodies in millimetres), STL, OBJ or PLY and can open your slicer.

**Import nozzle path** plays back linear G-code from your slicer layer by layer.

## Resin printing

1. **Resin check** finds closed cavities, trapped volume, unvented cups, unsupported layer starts, minimum features and peak layer area.
2. **Hollow body** with a wall thickness, then add a **Drain / vent hole** and recheck.
3. **Tilt part**, inspect with **Section**, then **Export for resin**.

Supports, slicing and exposure stay in your resin slicer. Checks sample the part on a grid, so a clean coarse report does not prove printability; rerun finer and check in the slicer.

## Slicers

Exports can open Bambu Studio, OrcaSlicer, PrusaSlicer, CHITUBOX, Lychee Slicer, PreForm or a custom executable. Re-export and use the slicer's reload to update a model.
