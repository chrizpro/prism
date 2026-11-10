# Subtractive manual test checklist

Use a simple block with a flat-bottom pocket, an island inside the pocket, two holes, and a perimeter. Record the Prism build, setup name, units, tool, post, and any result that differs from the expected behavior. See the [CAM workflow](../../cam/README.md#implemented-cnc-workflow).

## Setup

- [ ] Create a **CAM setup** with stock larger than the model. Use a known stock-top corner for work zero. Confirm the displayed stock and zero match the intended setup.
- [ ] Save a second setup with a different zero. Switch between them, save and reopen the project, and confirm each setup keeps its own stock and origin.
- [ ] Save a tool preset and apply it to a new operation. Confirm diameter, feeds, and speeds match the preset and an existing path keeps its earlier settings.

## Toolpaths

- [ ] **Pocket** the flat-bottom recess in several depth passes. Confirm the island remains untouched and the last pass reaches the chosen floor.
- [ ] Run **Adaptive** on the same pocket. Compare the areas cleared and verify the path stays clear of the island. Use **Pass view** to inspect intermediate depths.
- [ ] Run **Drill** on the two holes with pecking and confirm the two locations and depth. Use **Face** to preview passes across the stock.
- [ ] Run **Profile** around the outside with holding tabs. Confirm the cutter stays outside the part and the final passes leave the expected tabs.
- [ ] On a separate curved sample, compare **3D Rough** with an allowance against **3D Finish** with parallel passes. Look at an area that is steep as well as an area that is shallow.
- [ ] Try **Image Carve** with a simple light-to-dark image. Reverse the depth mapping and verify the carved areas swap. Use a separate turning setup to check the face, rough, and finish stages on a simple half-section.

## Review and post

- [ ] Put the inside operations before the outer Profile using **Operation order**. Hide one operation and check that **Stock review**, the **Setup sheet**, and the posted program omit it.
- [ ] Compare **Playback** with **Stock review**. Confirm playback follows the cutter motion and the stock view shows the expected remaining material. Change the model or active setup settings and confirm affected paths are flagged for regeneration.
- [ ] Use **Post G-code** with your controller choice. Check the saved path, units, zero, clearance moves, and operation order in your controller's simulator or backplot. Keep this first pass in simulation. Prism's stock view does not represent fixtures or machine motion limits.

[Maintained CNC operation tests](../../../Prism.Tests/Headless/Cam/CncOperationTests.cs) · [CAM program tests](../../../Prism.Tests/Headless/Cam/CamProgramTests.cs)
