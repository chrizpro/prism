# Assembly

Make Part turns a body into a component where it stands. The first component is grounded when the assembly has no grounded component. Joints position components from the grounded parts using local joint frames. Rigid, Revolute, Slider, Cylindrical, Ball, and Planar joints each define different motion channels. Joint limits clamp configured channels. See [Motion](../../cae/motion/README.md) for motion studies and couplings.

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

The joint preview numbers the two picked parts and labels each as moving or staying in place when you create the joint. Moving joints label their axes or slide directions. If the picks cannot form a valid connection, the command shows the solver message before you can commit.

### Hinges and closed linkages

Owner approved easier axis picking and closed-loop motion. In **Revolute** and **Cylindrical**, hover over a pin, hole, or circular edge to preview the axis before selecting it. **Create Part** opens its own modeling tab with visible assembly components dimmed for visual reference.

For a four-bar linkage, ground the base, create the four hinge joints, and use **Drive Joints** to move the crank. Prism adjusts the dependent joint values so the pins stay connected. If a joint or requested move cannot close the linkage within its limits, Prism explains the problem. Drive Joints keeps the last valid pose and resets the rejected value. For animation, key the crank and let the other joints follow. Keying more joints than the linkage can move independently can overconstrain it. Near a toggle position or when changing branches, use smaller steps.

### Animation timeline

Owner approved precise timeline and panel editing. **Animate → New Animation** opens a five-second draft immediately. **Save animation** adds it to the document. **Edit / Play** opens the selected or first saved clip. The animation timeline takes the place of Design History while editing so the viewport and right panel stay usable.

- Scrub the ruler or type the playhead time in **Clip**. Change a value and use **Add Key** to record it. **Play / Pause** previews the result.
- Click a track group heading to collapse or expand it. The zoom buttons enlarge or shrink the time ruler. Scroll horizontally to reach later times when zoomed in.
- Select a key to open **Selected key** in the right panel. Enter time to the millisecond and the value in the channel's units, then click **Apply key**. The starting key's value is editable and its time stays at zero.
- Drag a key to retime it. The key stays selected and an occupied time is rejected. Dragging snaps to 0.05 s. Hold Alt for finer movement.
- In **Document parameter**, choose an existing numeric document parameter to see its value and ramp controls. Set a start value, end value, start time and duration to create its ramp. The panel uses the parameter's model units.

[Animation timeline tests](../../../Prism.Tests/Headless/Assembly/AnimationTimelineTests.cs) exercise pointer-driven key moves and track collapse, occupied times, exact edits and saving, opening a new draft, and switching parameter settings. The 30 targeted Assembly and ribbon Headless checks passed on Windows on 2026-11-10.

Desktop scenario ASM-I003 opened a new clip on the gallery orrery, keyed a joint at 2.125 s and 5 s, and scrubbed back to verify 65° at 2.125 s. [Desktop capture](evidence/ASM-I003-animation-timeline.png).

## Verification

In the model browser, collapse the **Joints** folder to shorten the tree. Its eye hides or shows every joint's viewport frame, label and drive handle, including joints omitted by the browser search. If any row in the folder is visible, the next click hides all. Use an individual joint's eye to control it separately. Joint and coupling definitions, limits and motion remain active. [ASM-H122](../../../Prism.Tests/Headless/Assembly/AssemblyToolTests.cs) exercises the gallery orrery. [Bug #99](https://github.com/chrizpro/prism/issues/99).

The public [Assembly and animation guide](../../../site/guide/assembly.md#see-what-each-joint-allows) illustrates all six existing joint types and gives a first hinge workflow. Its diagrams are explanatory schematics, not application captures or new requirements. The guide links to CAE Motion for force-based studies; grounding and a rigid joint have different roles.

- [Kinematics unit tests](../../../Prism.Tests/Unit/Assembly/KinematicsTests.cs) verify all six motion types, slider limits, forward and reverse placement, and closure residuals.
- [Component headless tests](../../../Prism.Tests/Headless/Assembly/ComponentTests.cs) cover grounding and native document roundtrip, rigid and slider geometry, ribbon entry points, As Positioned placement, moving-part and axis previews, and a four-bar driven through a full turn and sampled from an animation.
- [Assembly tool tests](../../../Prism.Tests/Headless/Assembly/AssemblyToolTests.cs) cover Make Part, Move Component and its refusals, linear and circular Component Pattern, Mate (touching, flush, distance and refusals), Drive Joints, Parts List, Exploded View and Create Part.
- Run `./tools/test.ps1 -Feature Assembly`. [Issue #54](https://github.com/chrizpro/prism/issues/54).

## Coverage gaps

Add maintained cases for Insert Component, linked parts and source refresh, alternate joint picks and direction options, remaining joint types through the UI, coupled joints and desktop closed-loop dragging and animation rendering.

Windows verification on 2026-11-05: the combined candidate passed 86 Unit and 592 Headless tests with rebuilt native libraries. See the [integration verification report](../../engineering/release-readiness-2026-11-05.md) for source revisions, commands and limits.

Windows verification on 2026-11-09 with the fix applied to `fb99758`. ASM-H122 passed in the 61-test Headless selection. It reopened the gallery orrery, filtered the browser, and verified that each parent toggle caused one rebuild, changed visibility for all 26 joint and coupling rows, and hid or restored drive handles while preserving the 18 joint definitions.

Windows verification on 2026-11-10 passed 26 Assembly and ribbon Headless checks and 8 Assembly Unit checks. Desktop scenario ASM-I001 picked two solids in the viewport, confirmed both were labeled as staying in place, and created the rigid joint. [Desktop capture](evidence/ASM-I001-as-positioned.png).

The closed-loop follow-up passed 25 Assembly Headless checks, 9 Assembly Unit checks and all 75 checks in the [standalone Motion suite](../../../tests/Prism.MotionTests/Program.cs). ASM-H005 drives a four-bar through a full turn, samples an animation and verifies a rejected move preserves the last pose. ASM-H006 checks the hinge-axis overlay on an exact cylindrical face. Manual hover, dragging and rendered playback are covered in the [Joints checklist](../../tabs/checklists/joints-manual-test-checklist.md) and [Animate checklist](../../tabs/checklists/animate-manual-test-checklist.md).
