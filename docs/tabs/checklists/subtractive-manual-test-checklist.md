# Subtractive manual test checklist

Use a part with a flat-bottom pocket, an island, holes, and an outside profile. Record the Prism build, controller and version, units, and setup name. See the [CAM workflow](../../cam/README.md#implemented-cnc-workflow).

## Your controller and machine

- [ ] Follow **Setup → Toolpaths → Review → Export** on your part. Confirm the setup matches the intended stock, work zero, tools, and operation order. Check the stock view for the material you expect to leave behind.
- [ ] Choose your controller under **Export → Post G-code**. Open the saved program in your controller simulator or backplot. Confirm it loads, and compare units, work zero, cutting order, pocket floor, island clearance, hole locations, and the outside profile with the Prism setup.
- [ ] In the simulator, check the moves between cuts and confirm the program fits your machine's travel. Check clearance against your actual clamp and fixture locations. Prism's stock view does not model the fixture or machine travel.
- [ ] If you proceed to a physical prove-out, follow your shop's established procedure with a qualified operator. Record the result separately from the simulation. Measure the finished features against the model.

## Constant-Z Finish

- [ ] Use the same test tray for **3D Finish** and **Constant-Z Finish**. In **Pass view**, confirm each Constant-Z loop stays at one height and changes shape as the wall tapers. Compare the wall and floor coverage with the parallel passes.
- [ ] Backplot the posted Constant-Z program with your controller and setup. Verify that the cutter retracts between contours, that the entries are clear in the roughed stock, and that the cutter and holder clear the real fixtures. Check any skipped loops before deciding whether a different tool or setup is needed.

## Already verified in Prism

The maintained CAM cases cover setup and zero, tool presets, profile and holding tabs, pockets and islands, Adaptive, drilling, facing, 3D paths, turning, image carving, operation order, sampled stock, and posting. CAM-H004 checks the four ribbon sections, launches **Post G-code** through **Export**, and writes to both a chosen folder and an explicit file path. This verifies Prism's output path and generated program content. Controller interpretation and physical cutting remain hands-on checks.

[CAM workflow tests](../../../Prism.Tests/Headless/Cam/CamWorkflowTests.cs) · [CNC operation tests](../../../Prism.Tests/Headless/Cam/CncOperationTests.cs) · [CAM program tests](../../../Prism.Tests/Headless/Cam/CamProgramTests.cs)
