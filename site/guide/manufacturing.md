# Manufacturing

The **CAM** workspace prepares parts for CNC machining, FDM printing and resin printing. Reference: [Subtractive](/tools/subtractive), [3D FDM](/tools/3d-fdm), [3D Resin](/tools/3d-resin).

## CNC machining

Start with [your first pocket](#try-your-first-pocket) if you are new to CAM. To pick a different cut, use [the strategy guide](#choose-a-strategy).

<GuideImage image="cam-stock-setup" />

<GuideImage image="diagram-cnc-work-zero" />

1. **CAM setup**: rectangular stock, G54 work zero and shared cutting settings. Named setups are saved with the project; **Select setup** changes the active one.
2. **Tool library**: choose or save a tool and material preset.
3. Generate operations: **Profile** (with tabs), **Pocket** (with islands), **Adaptive** clearing, **Drill** (with pecking), **Face**, **3D Rough**, **3D Finish**, **Constant-Z Finish** or **Turning**. Use Constant-Z Finish for steep walls after roughing. It follows a new horizontal outline at each height with a flat end mill.
4. **Operation order** sets the program order; hidden operations are left out of posting.
5. Review with **Playback** and **Pass view**. Changing the setup or the model marks paths as out of date.
6. **Design check** screens top-down access, reach and thin walls. **Stock review** estimates removed and remaining material and possible gouges and holder contacts.
7. Save a **Setup sheet** from **Review**. Then choose **Export → Post G-code** for GRBL, Mach3 or LinuxCNC. Milling and turning are separate programs.

<GuideImage image="cam-profile-preview" />

<GuideImage image="cam-toolpath-playback" />

This is a focused three-axis workflow. Stock review samples a heightfield; it cannot show undercuts, fixtures or machine kinematics, and Playback previews motion only. Prove out programs on your machine.

### Try your first pocket

A **pocket** is a recess with a floor. The **stock** is the blank you start with. The **model** is the part you want after cutting. In this practice job, you make the model first, then ask CAM to remove the material above the pocket floor. You can follow the entire exercise in Prism without a machine.

<GuideImage image="diagram-cnc-pocket" />

1. **Make the target part.** In Design, make a 60 × 40 × 10 mm box with its base at Z = 0. Sketch a Ø20 mm circle centered on its top face. Finish the sketch, then **Extrude** it 3 mm into the block with **Cut**. In the preview, confirm the bottom of the recess is inside the block. The part should have a floor at model Z = 7 mm.
2. **Define the blank and zero.** Open **CAM → Subtractive → CAM setup**, pick the finished model body, and set **Stock allowance XY**, **Top allowance** and **Bottom allowance** to 0. Choose **Stock lower-left, top** for **Work zero (G54)**. The blank should still be 60 × 40 × 10 mm. Its top is model Z = 10 mm and posted Z = 0. Work zero is where coordinates are measured from. Clearance is how high the cutter lifts for travel.
3. **Make the cut plan.** Open **Toolpaths → Pocket** and pick the circular floor face. Use a 4 mm tool diameter for this software exercise. Set **Top of the material (Z)** to 10, **Total cutting depth** to 3, **Depth per pass** to 1, and **Stepover** to 25% of the tool. Depth per pass controls how far down each pass goes. Stepover controls the sideways spacing between neighboring passes. Choose **Create toolpath**.
4. **Watch and check.** Use **Playback** to watch the cutter clear inside the circle, lift, and repeat at the next depth. The three cutting levels should be model Z = 9, 8 and 7 mm. Use **Stock review → Run checks** with the same target body to see the sampled material removed and flag possible gouges or contacts. The target recess has a volume of about 942 mm³. The sampled stock result can differ, especially at the edge of the circle.
5. **Inspect the output.** Use **Post G-code**, choose a controller, and keep **Saved CAM setup** for **Work zero (G54)**. Set **Save to** to a practice folder and click **Save G-code**. The cutting depths should appear at posted Z = −1, −2 and −3 mm. Prism may omit a Z word on a line if the height stays the same. The exercise values are for previewing and checking the workflow. Actual cutting feeds, tool choice and setup depend on the machine and material.

If the path does not appear, check that you picked the **pocket floor**, rather than the top face of the box. If the paths look right from above but cut too high or low, compare **Top of the material (Z)** with the stock top. If you edit the model or setup, regenerate the toolpath before posting.

<GuideImage image="diagram-cnc-stock-review" />

In the reference run, a 1 mm review grid estimated 948 mm³ removed and flagged zero possible gouges, rapid contacts or holder contacts. Use those figures to compare your software exercise. Stock Review samples the cut and does not check the whole machine.

### Choose a strategy

| What you want to cut | Start with |
| --- | --- |
| Flatten the top of a blank | [Face](/tool/face-subtractive) |
| Remove material inside an outline, leaving a floor | [Pocket](/tool/pocket) |
| Cut around the outside of the part or along a closed outline | [Profile](/tool/profile) |
| Make holes at selected locations | [Drill](/tool/drill) |
| Clear an outlined region with a smaller sideways bite | [Adaptive](/tool/adaptive) |
| Remove most of the stock around a curved shape | [3D Rough](/tool/3d-rough) |
| Smooth a shallow curved surface with back-and-forth passes | [3D Finish](/tool/3d-finish) |
| Shape a spinning round blank on a lathe | [Turning](/tool/turning) |

With **2.5D** cuts like Pocket, most cutting happens at one height at a time. With **3D Finish**, the cutter can rise and fall as it travels along a pass. Both can use a fixed tool axis on a machine that supports coordinated XYZ motion.

<GuideImage image="diagram-cnc-finish" />

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
