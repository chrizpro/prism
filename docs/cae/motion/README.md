# Motion tab

One set of tools for every mechanism. A mechanism is parts, joints, couplings between joints, a drive on each joint, and forces. There is no gear, piston or robot tool, because those are all the same thing here.

## Ribbon

- **Setup:** Motion Study, Materials
- **Loads:** Spring / Actuator, Force / Torque
- **Results:** Clear Results, Load from Motion

Owner approved the Motion workflow organization in [#122](https://github.com/chrizpro/prism/issues/122). Build the mechanism in Assemble and Joints, then open Motion Study. The panel groups Setup, Drives, Loads, Solve and Results. Choose a joint to show its drive inputs, while a compact result summary lists all drives. Switching the selection preserves every joint's settings.

Select Movement only or Include forces. Choosing a Free drive switches to Include forces because gravity and loads determine its movement. Run study works in the background with progress and Stop study. Changing the setup or model while it runs prevents publication of stale results.

Result Time is entered in seconds and shows the nearest stored output sample. Move to Position supports the shared Curve Editor through Move timing → Custom curve. Apply the curve and rerun the study. [Maintained workflow tests](../../../Prism.Tests/Headless/Assembly/MotionWorkflowTests.cs) cover hidden drive inputs, forces, cancellation, stale results, exact time and custom motion profiles. [Ribbon checks](../../../Prism.Tests/Headless/Ribbon/CaeRibbonWorkflowTests.cs) cover the tab layout.

### Verification — 2026-11-10

Windows Release verification passed 43 Headless checks (Motion, CAE and Assembly), 15 Assembly Unit checks and 75 standalone Motion engine checks. The desktop build succeeded and the generated ribbon catalog passed its freshness check. MOT-I001 opened the gallery orrery in the native app and solved 11 samples over one second: the driver reached 30° at 30°/s and coupled followers moved at their ratios. [Setup capture](evidence/MOT-I001-motion-workflow.png) and [solved capture](evidence/MOT-I001-motion-results.png) show the native interface. The source revision and verification commands are recorded in [#122](https://github.com/chrizpro/prism/issues/122).

The [manual checklist](../../tabs/checklists/motion-manual-test-checklist.md) covers pointer-driven curve editing, playback and representative mechanism checks. Motion Study inputs remain session state; saving the assembly persists its joint/load objects, not a reusable study configuration. Contact and flexible-body behavior remain outside the study.

## What it covers

| Mechanism | How it is built |
|---|---|
| Gear train or gearbox | Revolute joints, Couple by tooth count |
| Engine piston | A closed loop of joints; drive the crank |
| Robot arm | One joint per axis, Follow the tool path |
| Car suspension | Linkage plus a spring; a table for the road |
| Excavator arm | Linkages moved by actuators |
| Wiper linkage | A four-bar loop |
| Scissor lift | A closed linkage and an actuator |
| Door with a gas strut | A hinge and an actuator or spring |
| Lead-screw stage | Couple by lead per turn |
| Pendulum or swing arm | A free joint under gravity |
| Punch press | A slider-crank with a flywheel and a force pulse |
| Belt or chain drive | Couple, same way, by pulley size |
| Cam and follower | Follow a table, repeating |

Prism does not calculate contact, so a cam's lift has to be given as a table. A Geneva wheel, a ratchet and a drop test cannot be simulated, and parts do not flex.

## Older names

Kinematics, Motion Dynamics and Robot Path are now one Motion Study. Gear Pair and Rack and Pinion are now Couple. Force and Torque are now Force / Torque. The script names `cae_kinematics`, `cae_motion`, `cae_robot`, `gear_pair` and `rack_pinion` still open these tools, but `gear_pair` and `rack_pinion` now use the labels of Couple.

Details are in [motion-analysis.md](../../motion-analysis.md).

For a shorter introduction, see the public [simulation guide](../../../site/guide/simulation.md#motion) and [illustrated joint guide](../../../site/guide/assembly.md#see-what-each-joint-allows). These explain the existing shared assembly/motion workflow; diagrams are schematics, not simulation evidence. The proposed review status below remains unchanged.

## Requirements review and traceability

**Review status: Proposed — awaiting owner review.** The current descriptions above document the existing Motion workflow; they have **not** been converted into owner-approved acceptance criteria. This section is a lightweight pilot for how to review a tab guide before asking agents to write tests.

| Review topic | Current description to confirm | Decision / evidence |
| --- | --- | --- |
| Joint behavior | Rigid, revolute, slider, cylindrical, ball and planar joints are offered | Owner approval needed for detailed motion constraints and edge cases |
| Drives and coupling | Drive Joints and Couple can control assemblies | Owner approval needed for limit handling, sign conventions and invalid mechanisms |
| Simulation scope | Contact and flexible-body simulation are out of scope in the existing guide | Confirm intended limitations with owner before making them test requirements |

The workflow changes above are approved and tracked in #122. The broader simulation acceptance criteria in this pilot still require review. See the [tab guide index](../../tabs/README.md) and [shared workflow](../../engineering/requirements-and-testing.md).

### Related detail

[Motion analysis](../../motion-analysis.md) is the canonical detailed workflow reference. Avoid duplicating its definitions here.
