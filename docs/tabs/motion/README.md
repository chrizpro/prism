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
