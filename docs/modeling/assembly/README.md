# Assembly

Make Part turns a body into a component where it stands. The first component is grounded when the assembly has no grounded component. Joints position components from the grounded parts using local joint frames. Rigid, Revolute, Slider, Cylindrical, Ball, and Planar joints each define different motion channels. Joint limits clamp configured channels. When a loop closes, the placement walk reports the closure joint and leaves it unenforced. See [Motion](../../cae/motion/README.md) for motion studies and couplings.

## Assemble workflow

Owner approved the ribbon and joint placement changes on 2026-11-10. After **Select**, the Assemble ribbon follows this order.

| Section | Tools |
| --- | --- |
| Add Parts | Insert Component, Parts Library, Create Part, Make Part |
| Assemble | Ground, Move Component, Mate, Joint (Rigid, Revolute, Slider, Cylindrical, Ball, Planar) |
| Manage Parts | Edit Part, Component Pattern, Refresh |
| Review | Interference, Measure, Properties, Parts List, Exploded View |
| Share | Existing AR and sharing tools |

The separate **Joints** tab shows each joint as a button and provides Couple and Drive Joints. In **Rigid**, **Snap to picks** aligns the picked geometry. Choose **As Positioned** and select two bodies or components to lock their current relative placement, including rotation. The joint uses the normal assembly solver and saves as a standard Rigid joint.

The joint preview numbers the two picked parts and labels each as moving or staying in place when you create the joint. Moving joints label their axes or slide directions. If the solver leaves a joint unenforced, its message appears in the preview.

## Verification

In the model browser, collapse the **Joints** folder to shorten the tree. Its eye hides or shows every joint's viewport frame, label and drive handle, including joints omitted by the browser search. If any row in the folder is visible, the next click hides all. Use an individual joint's eye to control it separately. Joint and coupling definitions, limits and motion remain active. [ASM-H122](../../../Prism.Tests/Headless/Assembly/AssemblyToolTests.cs) exercises the gallery orrery. [Bug #99](https://github.com/chrizpro/prism/issues/99).

The public [Assembly and animation guide](../../../site/guide/assembly.md#see-what-each-joint-allows) illustrates all six existing joint types and gives a first hinge workflow. Its diagrams are explanatory schematics, not application captures or new requirements. The guide links to CAE Motion for force-based studies; grounding and a rigid joint have different roles.

- [Kinematics unit tests](../../../Prism.Tests/Unit/Assembly/KinematicsTests.cs) verify all six motion types, slider limits, forward and reverse placement, and closure residuals.
- [Component headless tests](../../../Prism.Tests/Headless/Assembly/ComponentTests.cs) cover grounding and native document roundtrip, rigid and slider geometry, ribbon entry points, As Positioned placement and the preview of the actual moving part and slide axis.
- [Assembly tool tests](../../../Prism.Tests/Headless/Assembly/AssemblyToolTests.cs) cover Make Part, Move Component and its refusals, linear and circular Component Pattern, Mate (touching, flush, distance and refusals), Drive Joints, Parts List, Exploded View and Create Part.
- Run `./tools/test.ps1 -Feature Assembly`. [Issue #54](https://github.com/chrizpro/prism/issues/54).

## Coverage gaps

Add maintained cases for Insert Component, linked parts and source refresh, alternate joint picks and direction options, remaining joint types through the UI, coupled joints, closed-loop diagnostics and animation. Desktop drag and interaction checks remain deferred.

Windows verification on 2026-11-05: the combined candidate passed 86 Unit and 592 Headless tests with rebuilt native libraries. See the [integration verification report](../../engineering/release-readiness-2026-11-05.md) for source revisions, commands and limits.

Windows verification on 2026-11-09 with the fix applied to `fb99758`. ASM-H122 passed in the 61-test Headless selection. It reopened the gallery orrery, filtered the browser, and verified that each parent toggle caused one rebuild, changed visibility for all 26 joint and coupling rows, and hid or restored drive handles while preserving the 18 joint definitions.

Windows verification on 2026-11-10 passed 26 Assembly and ribbon Headless checks and 8 Assembly Unit checks. Desktop scenario ASM-I001 picked two solids in the viewport, confirmed both were labeled as staying in place, and created the rigid joint. [Desktop capture](evidence/ASM-I001-as-positioned.png).
