# Manufacturing

The **CAM** workspace prepares parts for CNC machining, FDM printing and resin printing. Reference: [Subtractive](/tools/subtractive), [3D FDM](/tools/3d-fdm), [3D Resin](/tools/3d-resin).

## CNC machining

<GuideImage image="cam-stock-setup" />

1. **CAM setup**: rectangular stock, G54 work zero and shared cutting settings. Named setups are saved with the project; **Select setup** changes the active one.
2. **Tool library**: choose or save a tool and material preset.
3. Generate operations: **Profile** (with tabs), **Pocket** (with islands), **Adaptive** clearing, **Drill** (with pecking), **Face**, **3D Rough**, **3D Finish** or **Turning**.
4. **Operation order** sets the program order; hidden operations are left out of posting.
5. Review with **Playback** and **Pass view**. Changing the setup or the model marks paths as out of date.
6. **Design check** screens top-down access, reach and thin walls. **Stock review** estimates removed and remaining material and possible gouges and holder contacts.
7. Export a **Setup sheet** and **Post G-code** for GRBL, Mach3 or LinuxCNC. Milling and turning are separate programs.

<GuideImage image="cam-profile-preview" />

<GuideImage image="cam-toolpath-playback" />

This is a focused three-axis workflow. Stock review samples a heightfield; it cannot show undercuts, fixtures or machine kinematics, and Playback previews motion only. Prove out programs on your machine.

## FDM printing

1. **Place on bed** lets you pick the flat face of a CAD solid that should sit on the build plate.
2. **FDM check** uses the build width, depth and height you enter and flags thin walls, overhangs and limited bed contact. Open **Troubleshoot geometry** for detailed checks and imported mesh repair.
3. **Export for printing** shows the bodies and dimensions before saving. Choose 3MF to preserve named bodies in millimetres, then open it in your slicer.

<GuideImage image="cam-fdm-check" />

## Resin printing

1. **Tilt part** to the angle you plan to print at.
2. If you want a hollow part, use **Hollow & Drain** to hollow the whole CAD body and cut drain or vent holes. Use **Troubleshoot geometry → Section** to confirm the openings reach the cavity.
3. Run **Resin check** for build fit, sealed cavities, possible suction cups and unsupported layer starts. Rerun after changing the angle or geometry.
4. **Export for resin** shows the bodies and dimensions before saving. Open the file in your resin slicer.

<GuideImage image="cam-resin-check" />

Supports, slicing and exposure stay in your resin slicer. Checks sample the part on a grid, so a clean coarse report does not prove printability; rerun finer and check in the slicer.

## Slicers

Exports can open Bambu Studio, OrcaSlicer, PrusaSlicer, CHITUBOX, Lychee Slicer, PreForm or a custom executable. Re-export and use the slicer's reload to update a model.
