# Assembly

Make Part turns a body into a component where it stands. The first component is grounded when the assembly has no grounded component. Joints position components from the grounded parts using local joint frames. Rigid, Revolute, Slider, Cylindrical, Ball, and Planar joints each define different motion channels. Joint limits clamp configured channels. When a loop closes, the placement walk reports the closure joint and leaves it unenforced. See [Motion](../../cae/motion/README.md) for motion studies and couplings.

## Verification

The public [Assembly and animation guide](../../../site/guide/assembly.md#see-what-each-joint-allows) illustrates all six existing joint types and gives a first hinge workflow. Its diagrams are explanatory schematics, not application captures or new requirements. The guide links to CAE Motion for force-based studies; grounding and a rigid joint have different roles.

- [Kinematics unit tests](../../../Prism.Tests/Unit/Assembly/KinematicsTests.cs) verify all six motion types, slider limits, forward and reverse placement, and closure residuals.
- [Component headless tests](../../../Prism.Tests/Headless/Assembly/ComponentTests.cs) cover grounding and native document roundtrip, and committing rigid and slider joints with exact box geometry.
- [Assembly tool tests](../../../Prism.Tests/Headless/Assembly/AssemblyToolTests.cs) cover Make Part, Move Component and its refusals, linear and circular Component Pattern, Mate (touching, flush, distance and refusals), Drive Joints, Parts List, Exploded View and Create Part.
- Run `./tools/test.ps1 -Feature Assembly`. [Issue #54](https://github.com/chrizpro/prism/issues/54).

## Coverage gaps

Add maintained cases for Insert Component, linked parts and source refresh, alternate joint picks and direction options, remaining joint types through the UI, coupled joints, closed-loop diagnostics and animation. Desktop drag and interaction checks remain deferred.

Windows verification on 2026-11-05: the combined candidate passed 86 Unit and 592 Headless tests with rebuilt native libraries. See the [integration verification report](../../engineering/release-readiness-2026-11-05.md) for source revisions, commands and limits.
