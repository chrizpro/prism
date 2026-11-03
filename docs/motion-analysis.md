# Kinematics and motion analysis

The CAE workspace has a **Motion** tab for mechanisms. The same tools cover gears, pistons, robot arms, suspensions, linkages, lifts, presses and more: there is nothing specific to any one of them. A mechanism is parts, joints, couplings between joints, a drive on each joint, and forces.

The Motion tab has the assembly tools it needs, and they are the same parts and joints as the Assemble workspace.

## The workflow

1. **Parts.** Model the bodies in the file, or bring parts in with **Insert Component** or **Parts Library**. Picking a plain body with a joint tool makes it a part where it stands; **Make Part** does it for several at once. The first part is grounded. **Ground** changes which one stays still.
2. **Joints.** Connect the parts: revolute for a shaft or hinge, slider for a slide, cylindrical, ball, planar, rigid. Set limits where the real mechanism has stops. Use **Drive Joints** to check by hand that it moves the way you expect; closed loops follow.
3. **Couple.** Make one joint follow another at a ratio. Whatever moves the first moves the second, and couplings chain (A drives B drives C).
4. **Forces**, if you need them: **Spring / Actuator** and **Force / Torque**, plus a material on each part for its mass.
5. **Motion Study.** Choose how each joint moves and press Run study.

### One Couple tool

| Joints | Works as | You enter |
| --- | --- | --- |
| Two turning | Gears, belt, chain | Two sizes (teeth, pulley or pitch diameter), same or opposite way |
| Turning and sliding | Rack, pulley, winch, screw | Pitch diameter, or lead per turn |
| Two sliding | Lever, cable | Travel per mm |

## Motion Study

A study runs with or without **masses and forces**. Without them it needs no materials and gives the motion alone. With them it also gives motor torque, power, energy and joint reactions, and gravity, springs and loads act. It starts with them on when every part has a material, or when the assembly has springs or loads. A joint set to Free turns them on, because a free joint only moves under forces.

Every joint gets one drive:

| Drive | Meaning |
| --- | --- |
| Hold | Stays at its starting position |
| Constant speed | A steady speed in °/s or mm/s |
| Oscillate | A sine of a given amplitude and frequency about the start |
| Move to a position | A smooth minimum-jerk move: it waits, moves, and stops at rest |
| Follow a table | Rows of time and position, typed or from a file |
| Follow the tool path | The joint moves so a point on the mechanism traces a curve |
| Free | Moves under gravity, loads and friction from a starting position and speed. It can carry a motor with a torque–speed curve: stall torque (or force) and no-load speed, with the push falling linearly between them |

**Follow a table** is for motion no other drive describes. A cam's lift goes in as a table with **Repeat until the end of the study** on, and a recorded road profile can drive a suspension. Prism does not calculate contact, so the table says what the contact would do. The table is smoothed with a cubic spline; to repeat, it must end where it starts. Header rows are skipped.

Springs, actuators and loads are objects in the assembly, placed once and used by every study:

- **Spring / Actuator** acts along the line between a point on one part and a point on another part, or on a plain fixed body (an anchor in space). Stiffness and damping make a spring-damper; an **actuator push** makes a cylinder that pushes (or, negative, pulls) with a constant force, its pressure times its area. The study reports the force each one carries.
- **Force / Torque** acts on a part. The direction is an axis or the picked face's normal, fixed in space or turning with the part. In time it is constant, a pulse (on for a while) or a sine.

All of these appear in the browser, can be selected and deleted like joints, and are saved with the document.

## Follow the tool path

To say what a point should do instead of what each joint does, set the joints that should move it to **Follow the tool path**. The study then asks for:

1. The **tool point**: a face, circle or edge on the part that follows the path.
2. The **path**: a curve, a sketch, or edges in order.
3. The tool speed and acceleration along the path and, with masses and forces on, a payload at the tool.

The speed profile along the path is a trapezoid (accelerate, cruise, brake) with the corners rounded by an acceleration-limited radius, so the tool does not have to stop. If the tool does not start on the path, the first move goes straight to its start. Joint values at every output time come from **inverse kinematics** (damped least squares, started from the previous point so the mechanism stays on one branch). The study reports whether every point was reached and the worst miss, the joint ranges, and with forces on, the torque and power each joint needs. With six or more joints the tool can also keep its direction; with fewer, only its position follows the path and the study says so. Joints with a drive of their own are held still while the path is planned.

## Closed loops: pistons, four-bars, linkages

A piston, a slider-crank, a four-bar or any linkage whose joints form a loop works without any special setup: build it from ordinary joints (the last joint of the loop joins two parts that are already placed), then drive one joint of the loop. The loop removes as many joint values as it has independent constraints; the study picks which, keeping the joints you drive and the free ones as the coordinates and solving the others from them at every moment (damped Newton, started from the previous position, so the linkage stays on one branch). The summary marks those joints "set by the loop". If the linkage cannot reach a position (a crank longer than its rod) the study stops with the time and says it cannot close.

The equations of motion are written in the coordinates that remain, so a piston's motor torque, speed, acceleration and the energy balance are exact for the rigid model: they match the textbook slider-crank formulas to better than 0.01 %. A joint inside a loop cannot be driven if the loop already fixes it; drive one of the loop's free joints. A loop closed in a different position than the model starts in is closed from the start (the dependent joints are solved to meet), so make the parts meet where the joints are.

Drive Joints closes loops the same way: the joint you move stays where you put it and the rest of the loop follows. Positions a linkage cannot reach are refused. Animations still solve joints outwards from the grounded part and do not enforce loops.

## Joint reactions

With masses and forces on, a study reports what each joint holds: the force (N) and moment (N·m) its parent exerts on its child in the directions the joint does not let move, in world axes. A revolute joint's turning direction is not reported (that is the motor's torque); a slider's sliding direction, a ball joint's moments, a planar joint's in-plane motion likewise. The values come from releasing the joint one direction at a time by a tiny extra motion and measuring the work against the inertia, weight and applied forces of every part, with the loops kept closed. So a piston's side load on its cylinder wall, a main bearing's load, a hinge's weight and a welded cantilever's bending moment are all available. They match closed-form results (a hinge holding a rod carries m·g; a rod swinging through the bottom carries m(g + ω²d); a welded beam carries W and W·d; a piston's side load is m·a·tanφ).

The largest are listed in the study's results with the time they peak; arrows at the joints in the viewport show the force at the moment you scrub to; the CSV has force and moment columns for every joint. In a flat linkage the out-of-plane forces cannot be separated (they are statically indeterminate) and are reported as zero.

### From motion to a Static study

**Motion → Review → Load from Motion** turns a joint's force from the last Motion Study into a load: choose the joint, a moment (when the force is largest, or a time you choose), whether it acts on the joint's child or its parent (equal and opposite), and the faces of the part that take it. The assembly is moved to that moment so the part sits as it did, and a new Static study is created on that part with the force on those faces. Add supports where the part is held and run it. The force alone is not balanced: the part needs supports, or the loads of its other joints, to be in equilibrium.

## Reading the results

The panel reports, for each joint, its range, peak speed and peak acceleration, then the fastest parts with their peak speed, acceleration and turning rate. The paths the parts' centres of mass follow are drawn in the viewport, with a dot at the time shown. **Show the motion at** scrubs the whole assembly to any moment; **Play / Stop** plays it. **Look for collisions along the motion** checks parts that are not connected by a joint or coupling against each other at up to 36 moments and lists any that overlap, with the time. A joint asked to go past its limit holds at the limit and the study says so.

With masses and forces on, a driven joint also reports the **torque or force the motor needs** at every moment, its RMS and peak, the peak power and the energy put in. Gravity can be switched off or set along any axis. Every joint that moves (including followers) can have viscous friction; a follower can carry a **load**, and a gear train reflects that load and the driven gear's inertia back to the motor with the ratio squared. Free joints stop at their limits. The results add the kinetic plus potential energy at the start and end and, for each part, the peak acceleration and the inertial force `m·a`. Mass, centre of mass and inertia tensor come from each part's geometry and material density.

**Save results** writes a CSV of every joint's position, speed and acceleration and every part's position, speed and acceleration against time.

### How it is solved

The joint values are the coordinates. The pose of every part comes from the assembly's own joint solver (the same one Drive Joints uses, including gear couplings), so every joint type works. Part Jacobians and the velocity-dependent accelerations are taken by numerical differentiation of that pose function, which gives the mass matrix `M(q)` and bias term `h(q, q̇)` of `M·q̈ + h = Q` without separate formulas per joint type. With some joints driven and some free, the free accelerations are solved from the partitioned system and the driven joints' required effort is the remainder. Free joints are integrated with fixed 2 ms fourth-order Runge–Kutta steps.

## Verified against theory

`tests/Prism.MotionTests` (no CAD kernel needed; 75 checks) checks the engine against closed-form results: box mass and inertia from a mesh; the period of a physical pendulum (0.73299 s computed and theoretical) and energy conservation over two seconds; the holding torque `m·g·d` of a horizontal rod; the torque `I·A·ω²` to oscillate a rod; the 2:1 gear ratio, direction and power balance; the gear train's reflected inertia `I·(N₁/N₂)²`; free spin-up `α = τ/I_reflected`; rack travel `π·d` per turn; the speed and acceleration of a point on a turning arm; joint limits for driven and free joints; gear chains, loops and saving.

The engine tests also cover a full piston (position, speed and acceleration against the slider-crank formulas, the motor torque m·x″·x′·ω², the bearing and cylinder-wall forces through a massless rod, energy conservation with a flywheel), a four-bar over a full turn (coupler length constant, no branch flips), unreachable linkages, and joint reactions (hinge weight, swinging rod, welded cantilever). They also cover: the minimum-jerk ramp, spline interpolation, a motor curve (exponential spin-up), a spring-mass natural frequency and energy, damping, an actuator's F/m, a force pulse's impulse F·t, a payload's holding torque m·g·r, a torque's α = T/(m r²), inverse kinematics of a two-link arm (on target, unreachable, joint limits), path following and the speed profile, and static torques of an arm holding a payload.

`tests/Prism.MotionStudyTests` (needs the CAD kernel) runs the same on real parts: a rod swung in a study matches the pendulum period (0.7345 s vs 0.733 s theory), gears are coupled with Couple and turn each other in Drive Joints, a 20 mN·m load on a 2:1 reduction needs 10 mN·m at the motor, results save to CSV, and the collision sweep finds a post standing in a gear's way. It also covers a mechanism made from plain bodies in one file, a repeating table like a cam, the tool path in an ordinary study, screw and lever couplings, and dragging a piston by hand. Run it with `LD_LIBRARY_PATH` pointing at the built `prism_kernel`.

## What it does not do

- **Rigid bodies only.** No flexing, vibration or stress. Use the Static and Dynamics tabs for those, with loads read from this study.
- **Ideal joints and gears.** No clearance, backlash, tooth contact or friction beyond the viscous friction you enter, and no efficiency loss in gears.
- **Contact is not computed.** Parts are not tested against each other while a study runs; the collision check is a separate sweep. Reactions are rigid-body values: they do not include flexing, clearance or tooth-contact effects. So a cam needs its lift as a table, and a Geneva wheel, a ratchet or a drop test cannot be simulated.
- **Animations do not close loops** (see above). A free joint inside a loop whose neighbour is also free keeps only one of them as a coordinate; its initial speed is then set by the loop. At a dead point (a piston at top dead centre with no flywheel) the effective inertia is zero and a free crank has no defined acceleration; give the crank a flywheel or drive it.
- Inverse kinematics is position-only unless six joints follow the path; paths are followed at a given speed, not a given time, and there is no collision avoidance or singularity check beyond reporting unreachable points.
- Joint limits are inelastic stops.
- A motor curve applies only to a free joint. A driven joint moves as told, so its torque is reported and not limited.
