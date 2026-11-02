# Kinematics and motion analysis

The CAE workspace has a **Motion** tab for mechanisms: a hinge, a slider-crank, a gear train, a rack and pinion. You build the mechanism in the **Assemble** workspace, then switch to **CAE → Motion** to say what drives it and read what happens. Nothing is rebuilt or copied between the two: the Motion tab works on the same parts, joints and gears.

The Motion tab also carries the assembly tools it needs (Parts Library, Insert Component, Ground, Joint menu, Gear Pair, Rack and Pinion, Drive Joints), so a small fix to the mechanism does not need a trip back to Assemble.

## The workflow

1. **Assemble.** Insert the parts. **Ground** the one that stays still (the first component is grounded for you).
2. **Joints.** Connect the parts with joints: revolute for a shaft or hinge, slider for a slide, cylindrical, ball, planar, rigid. Set limits where the real mechanism has stops. Use **Drive Joints** to check by hand that it moves the way you expect.
3. **Couple gears.** Make a revolute joint for each gear shaft, then **Joints → Couple → Gear Pair**. Pick the driving and the driven joint and enter the two tooth counts (or pitch diameters). External gears turn opposite ways; choose *Internal gear or belt* for the same direction. For a rack, make a revolute joint for the pinion and a slider for the rack, then **Rack and Pinion** with the pinion's pitch diameter.
   From here on the driven joint follows the driving one everywhere: Drive Joints, animations and the motion studies. Gear pairs chain (A drives B drives C).
4. **Materials** (for dynamics only). Give each part a material so it has mass. Kinematics needs none.
5. **CAE → Motion.** Run **Kinematics** or **Motion Dynamics**.

## Drives, actuators and loads

Every joint in a study gets one of these motions: **Hold**, **Constant speed**, **Oscillate**, **Move to a position** (a smooth minimum-jerk move: it waits, moves, and stops at rest), or, in Motion Dynamics, **Free** (it moves under gravity, loads and friction from a starting position and starting speed). A free joint can carry a **motor** with a torque–speed curve: stall torque (or force) and no-load speed, the push falling linearly between them.

Springs, actuators and loads are objects in the assembly, so they are placed once and used by every study. They are made from the Motion tab:

- **Spring / Actuator** acts along the line between a point on one part and a point on another part, or on a plain fixed body (an anchor in space). Stiffness and damping make a spring-damper (a suspension, a return spring); an **actuator push** makes a cylinder that pushes (or, negative, pulls) with a constant force, its pressure times its area. The study reports the force each one carries.
- **Force** and **Torque** act on a part. The direction is an axis or the picked face's normal, fixed in space or turning with the part. In time they are constant, a pulse (on for a while) or a sine.
- A **payload** (kg) at a tool point is added in Robot Path.

All of these appear in the browser, can be selected and deleted like joints, and are saved with the document.

## Robot Path

For a robot arm, state what you want the tool to do instead of what each joint does. Build the arm in Assemble (parts, a grounded base, a joint per axis), then **Motion → Robot Path**:

1. Pick the **tool point**: a face, circle or edge on the last link.
2. Pick the **path**: a curve, a sketch, or edges in order.
3. Set the tool speed, acceleration along the path and, if you want torques, a payload and gravity. Tick the joints that move the arm.

The speed profile along the path is a trapezoid (accelerate, cruise, brake) with the corners rounded by an acceleration-limited radius, so the tool does not have to stop. If the tool does not start on the path, the first move goes straight to its start. Joint values at every output time come from **inverse kinematics** (damped least squares, started from the previous point so the arm stays on one branch), and the joint motion is then simulated as a planned (spline) drive. The study reports whether every point was reached and the worst miss, points the arm cannot reach (outside its reach or joint limits), the joint ranges, and, with torques on, the torque and power each joint needs with the payload. With six or more joints the tool can also keep its direction; with fewer, only its position follows the path and the study says so.

## Closed loops: pistons, four-bars, linkages

A piston, a slider-crank, a four-bar or any linkage whose joints form a loop works in the Motion tab without any special setup: build it from ordinary joints in Assemble (the last joint of the loop joins two parts that are already placed), then drive one joint of the loop. The loop removes as many joint values as it has independent constraints; the study picks which, keeping the joints you drive and the free ones as the coordinates and solving the others from them at every moment (damped Newton, started from the previous position, so the linkage stays on one branch). The summary marks those joints "set by the loop". If the linkage cannot reach a position (a crank longer than its rod) the study stops with the time and says it cannot close.

The equations of motion are written in the coordinates that remain, so a piston's motor torque, speed, acceleration and the energy balance are exact for the rigid model: they match the textbook slider-crank formulas to better than 0.01 %. A joint inside a loop cannot be driven if the loop already fixes it; drive one of the loop's free joints. A loop closed in a different position than the model starts in is closed from the start (the dependent joints are solved to meet), so make the parts meet where the joints are.

Drive Joints and animations in the Assemble workspace still solve joints outwards from the grounded part and do not enforce loops; the Motion tab does.

## Joint reactions

Motion Dynamics reports what each joint holds: the force (N) and moment (N·m) its parent exerts on its child in the directions the joint does not let move, in world axes. A revolute joint's turning direction is not reported (that is the motor's torque); a slider's sliding direction, a ball joint's moments, a planar joint's in-plane motion likewise. The values come from releasing the joint one direction at a time by a tiny extra motion and measuring the work against the inertia, weight and applied forces of every part, with the loops kept closed. So a piston's side load on its cylinder wall, a main bearing's load, a hinge's weight and a welded cantilever's bending moment are all available. They match closed-form results (a hinge holding a rod carries m·g; a rod swinging through the bottom carries m(g + ω²d); a welded beam carries W and W·d; a piston's side load is m·a·tanφ).

The largest are listed in the study's results with the time they peak; arrows at the joints in the viewport show the force at the moment you scrub to; the CSV has force and moment columns for every joint. In a flat linkage the out-of-plane forces cannot be separated (they are statically indeterminate) and are reported as zero.

### From motion to a Static study

**Motion → Review → Load from Motion** turns a joint's force from the last Motion Dynamics study into a load: choose the joint, a moment (when the force is largest, or a time you choose), whether it acts on the joint's child or its parent (equal and opposite), and the faces of the part that take it. The assembly is moved to that moment so the part sits as it did, and a new Static study is created on that part with the force on those faces. Add supports where the part is held and run it. The force alone is not balanced: the part needs supports, or the loads of its other joints, to be in equilibrium.

## Kinematics

A prescribed motion. For every joint that can move (those driven through a gear are shown as followers):

| Motion | Meaning |
| --- | --- |
| Hold | the joint stays at its starting position |
| Constant speed | a steady speed in °/s or mm/s |
| Oscillate | a sine of a given amplitude and frequency about the starting position |

Set the duration and output interval, then **Run study**. The panel reports, for each joint, its range, peak speed and peak acceleration, then the fastest parts with their peak speed, acceleration and turning rate. The paths the parts' centres of mass follow are drawn in the viewport, with a dot at the time shown. **Show the motion at** scrubs the whole assembly to any moment; **Play / Stop** plays it. **Look for collisions along the motion** checks parts that are not connected by a joint or gear against each other at up to 36 moments and lists any that overlap, with the time. A joint asked to go past its limit holds at the limit and the study says so.

**Save results** writes a CSV of every joint's position, speed and acceleration and every part's position, speed and acceleration against time.

## Motion dynamics

The same set-up plus masses and forces. Each joint is either:

- **driven** (hold, constant speed or oscillate), in which case the study returns the **torque or force the motor needs** at every moment, its RMS and peak, the peak power and the energy put in; or
- **free**, in which case it moves under gravity, a constant torque or force, and friction from a starting position and starting speed. A free link swings, a free flywheel spins up.

Gravity can be switched off or set along any axis. Every joint (including gear followers) can have viscous friction; a follower can carry a **load** (a torque or force on the driven shaft), and a gear train reflects that load and the driven gear's inertia back to the motor with the ratio squared. Free joints stop at their limits.

The results add the kinetic plus potential energy at the start and end and, for each part, the peak acceleration and the inertial force `m·a`. Mass, centre of mass and inertia tensor come from each part's geometry and material density.

### How it is solved

The joint values are the coordinates. The pose of every part comes from the assembly's own joint solver (the same one Drive Joints uses, including gear couplings), so every joint type works. Part Jacobians and the velocity-dependent accelerations are taken by numerical differentiation of that pose function, which gives the mass matrix `M(q)` and bias term `h(q, q̇)` of `M·q̈ + h = Q` without separate formulas per joint type. With some joints driven and some free, the free accelerations are solved from the partitioned system and the driven joints' required effort is the remainder. Free joints are integrated with fixed 2 ms fourth-order Runge–Kutta steps.

## Verified against theory

`tests/Prism.MotionTests` (no CAD kernel needed; 75 checks) checks the engine against closed-form results: box mass and inertia from a mesh; the period of a physical pendulum (0.73299 s computed and theoretical) and energy conservation over two seconds; the holding torque `m·g·d` of a horizontal rod; the torque `I·A·ω²` to oscillate a rod; the 2:1 gear ratio, direction and power balance; the gear train's reflected inertia `I·(N₁/N₂)²`; free spin-up `α = τ/I_reflected`; rack travel `π·d` per turn; the speed and acceleration of a point on a turning arm; joint limits for driven and free joints; gear chains, loops and saving.

The engine tests also cover a full piston (position, speed and acceleration against the slider-crank formulas, the motor torque m·x″·x′·ω², the bearing and cylinder-wall forces through a massless rod, energy conservation with a flywheel), a four-bar over a full turn (coupler length constant, no branch flips), unreachable linkages, and joint reactions (hinge weight, swinging rod, welded cantilever). They also cover: the minimum-jerk ramp, spline interpolation, a motor curve (exponential spin-up), a spring-mass natural frequency and energy, damping, an actuator's F/m, a force pulse's impulse F·t, a payload's holding torque m·g·r, a torque's α = T/(m r²), inverse kinematics of a two-link arm (on target, unreachable, joint limits), path following and the speed profile, and static torques of an arm holding a payload.

`tests/Prism.MotionStudyTests` (needs the CAD kernel) runs the same on real parts: a rod made in the assembly and swung from the CAE tab matches the pendulum period (0.7345 s vs 0.733 s theory), gears are coupled with the Gear Pair command and turn each other in Drive Joints, a 20 mN·m load on a 2:1 reduction needs 10 mN·m at the motor, results save to CSV, and the collision sweep finds a post standing in a gear's way. Run it with `LD_LIBRARY_PATH` pointing at the built `prism_kernel`.

## What it does not do

- **Rigid bodies only.** No flexing, vibration or stress. Use the Static and Dynamics tabs for those, with loads read from this study.
- **Ideal joints and gears.** No clearance, backlash, tooth contact or friction beyond the viscous friction you enter, and no efficiency loss in gears.
- **Contact is not computed.** Parts are not tested against each other while the dynamics runs; the collision check is a separate, kinematic sweep. Reactions are rigid-body values: they do not include flexing, clearance or tooth-contact effects.
- **Loops are solved in the Motion tab only** (see above); the Assemble workspace's Drive Joints does not yet close them. A free joint inside a loop whose neighbour is also free keeps only one of them as a coordinate; its initial speed is then set by the loop. At a dead point (a piston at top dead centre with no flywheel) the effective inertia is zero and a free crank has no defined acceleration; give the crank a flywheel or drive it.
- Inverse kinematics is position-only unless the arm has six usable joints; paths are followed at a given speed, not a given time, and there is no collision avoidance or singularity check beyond reporting unreachable points.
- Joint limits are inelastic stops.
- A motor is a prescribed motion; there is no motor curve, so torque limits are not applied.
