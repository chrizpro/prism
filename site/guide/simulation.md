# Simulation

The **CAE** workspace analyzes the model you designed, without exporting it. Reference: [Static](/tools/static), [Thermal](/tools/thermal), [Dynamics](/tools/dynamics), [Motion](/tools/motion), [Fluids](/tools/fluids).

## A static study

<GuideImage image="simulation-static-loads" />

1. Switch to **CAE** and open **Static**.
2. **New Study** on a solid body, then assign a material with **Materials**. A structural study needs material data.
3. Add supports: **Fixed Support** on faces that are bolted, welded or clamped, **Roller / Symmetry** for faces that slide, or **Fixed Directions** to lock chosen axes.
4. Add loads: **Force** (total or per face, along the normal or a world axis), **Pressure**, **Torque** or **Gravity**.
5. **Run Study**. Prism meshes the body with 10-node tetrahedra, solves and colors the result.
6. Review **von Mises Stress**, **Displacement**, **Factor of Safety** and **Fatigue**. **Probe** reads values at points.

<GuideImage image="simulation-static-stress" />

The element size defaults to automatic. Halve it and check that stress away from the supports barely changes; **Mesh refinement** helps check this. Stress peaks right at supports and point-like loads can keep growing with finer meshes, so judge stress away from them.

**Saved CAE setups** and **Save / Recall face selection** let you rerun a study after changing the design. **Generative Design** removes material that carries the least load and produces a new mesh body.

## Thermal and natural frequencies

**Thermal** solves steady or transient conduction in one homogeneous solid, with prescribed temperatures, heat into faces, and convective or radiative cooling. **Dynamics › Natural Frequencies** finds mode frequencies and shapes with fixed faces. Both use an external CalculiX installation.

<GuideImage image="simulation-thermal-result" />

<GuideImage image="simulation-natural-frequency" />

## Motion

Build any mechanism the same way: parts, joints, couplings between joints, a drive on each joint, and forces.

Not sure which joint to use? The [illustrated joint guide](/guide/assembly#see-what-each-joint-allows) shows the six motion types, and the [hinge example](/guide/assembly#start-with-a-hinge) walks through a first connection. The same parts and joints are used in Assemble and CAE Motion.

1. **Make Part** or **Insert Component**, then **Ground** one part.
2. Connect parts with Rigid, Revolute, Slider, Cylindrical, Ball or Planar joints.
3. **Couple** joints that turn together: gears, belts, racks, screws or levers, by ratio.
4. Add **Spring / Actuator** and **Force / Torque**, then run a **Motion Study**.
5. **Load from Motion** carries the forces at a chosen moment into a static study.

Use **Drive Joints** to check the connections first. A motion study can run without masses and forces to inspect movement alone; adding them lets it report motor effort and joint reactions. The moving parts remain rigid, and contact is not calculated during the study. Use a separate interference check when reviewing possible collisions.

<GuideImage image="simulation-motion-study" />

## Fluids

- **Pipe Network** solves steady flow through round, full pipes and pumps: how flow splits and the pressure at each point. **Pipe Flow** handles a single pipe.
- **Flow Study** is basic 3D flow on a block grid, either around a body in a tunnel (drag and drag coefficient) or through a duct modeled as a solid (pressure drop and flow).

<GuideImage image="simulation-pipe-flow" />

Trust the 3D numbers for slow, smooth flow through ducts and manifolds. For fast air or water, treat the result as a picture: there is no turbulence model.

## Limits

Studies are linear and cover one homogeneous solid. Contact and nonlinear materials are not included. A study result helps you decide; it does not certify a design.
