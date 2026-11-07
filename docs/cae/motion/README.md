# Motion tab

One set of tools for every mechanism. A mechanism is parts, joints, couplings between joints, a drive on each joint, and forces. There is no gear, piston or robot tool, because those are all the same thing here.

## Ribbon

- **Mechanism:** Parts (Make Part, Insert Component, Parts Library), Ground, Joint (Rigid, Revolute, Slider, Cylindrical, Ball, Planar), Couple, Drive Joints
- **Simulate:** Motion Study
- **Springs and loads:** Spring / Actuator, Force / Torque
- **Review:** Load from Motion, Clear Results
- **Appearance:** shared section

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

**No new requirement IDs, GitHub Issues, or tests have been assigned by this pilot.** Review and record specific expectations first. When the owner approves a behavior, add a stable ID only if beneficial and link an existing test or actionable issue (if one exists). See [the tab guide index](../../tabs/README.md) and [the shared workflow](../../engineering/requirements-and-testing.md).

### Related detail

[Motion analysis](../../motion-analysis.md) is the canonical detailed workflow reference. Avoid duplicating its definitions here.
