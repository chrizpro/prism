# Assembly

Make Part turns a body into a component where it stands. The first component is grounded when the assembly has no grounded component. Joints position components from the grounded parts using local joint frames. Rigid, Revolute, Slider, Cylindrical, Ball, and Planar joints each define different motion channels. Joint limits clamp configured channels. See [Motion](../../cae/motion/README.md) for motion studies and couplings.

## Assemble workflow

Owner-requested usability changes on 2026-11-11 ([#126](https://github.com/chrizpro/prism/issues/126)): **Finish part & return** completes a valid active modeling command/sketch, inserts or refreshes the part, and selects the Assemble workspace and tab. An empty part stays open for modeling. Assemble includes Solid, Surface, Freeform, NURBS, Direct Modeling and Sheet Metal tabs. Components show Grounded, Jointed, Free, or Grounded · Jointed in the browser. Jointed means participation in a persistent joint, not fully constrained motion. Owner explicitly retained Mate's positioning-only behavior; mating does not falsely mark a part as jointed.

[Usability workflow tests and native evidence](../../interface/operation-progress.md#verification--2026-11-11) verify the return flow, browser states and workspace/ribbon access. Status appears below component names so it remains readable in a narrow browser. Direct Modeling opens contextually through Direct Edit.

Owner approved the ribbon and joint placement changes on 2026-11-10. After **Select**, the Assemble ribbon follows this order.

Owner requested removing the duplicate Joint menu from Assemble in [#125](https://github.com/chrizpro/prism/issues/125). Joint creation lives on the dedicated Joints tab; Assemble retains Ground, Move Component and Mate. Maintained verification: [ComponentTests (ASM-H003)](../../../Prism.Tests/Headless/Assembly/ComponentTests.cs). Windows Release verification on 2026-11-10 passed 31 Assembly Headless tests and the generated ribbon catalog freshness check.

| Section | Tools |
| --- | --- |
| Add Parts | Insert Component, Parts Library, Create Part, Make Part |
| Assemble | Ground, Move Component, Mate |
| Manage Parts | Edit Part, Component Pattern, Refresh |
| Review | Interference, Measure, Properties, Parts List, Exploded View |
| Share | Existing AR and sharing tools |

The separate **Joints** tab shows each joint as a button and provides Couple and Drive Joints. In **Rigid**, **Snap to picks** aligns the picked geometry. Choose **As Positioned** and select two bodies or components to lock their current relative placement, including rotation. The joint uses the normal assembly solver and saves as a standard Rigid joint.

**Placement on every joint type (2026-11-12, proposed — awaiting owner review; implemented after the owner reported a part "disappearing" while making a revolute joint):** Revolute, Slider, Cylindrical, Ball and Planar also offer **Placement**. **Snap to picks** (default) moves the second part so its pick meets the first pick: the two frames are made coincident, so a part picked by a large face or a long pin can land inside the other part or far from where it was. **Keep where it is** takes the joint axis (or slide direction, or pivot point) from the first pick and leaves both parts exactly where they stand; the second slot then accepts any face, edge or the whole body, because it only identifies the part. The preview's Placement line reads "Both parts stay where they are". Drive Joints then turns or slides about that axis from the parts' current pose. Maintained check: ASM-H124 in [AssemblyToolTests](../../../Prism.Tests/Headless/Assembly/AssemblyToolTests.cs).

**The ribbon Move gizmo on parts (2026-11-12):** grabbing a component with the Move tool (the shared datum gizmo in move mode, see [viewport interaction](../../viewport-interaction-handoff.md)) updates its placement together with its shape, so the joint solver and Refresh keep it where it was dragged. Grounded parts and parts held by a joint are refused with a status message naming the part and the way to move it (unground it, or Drive Joints). Maintained check: ASM-H123. Move Component (the command with its own arrows and rings) still exists for exact typed offsets.

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

Owner approved table access, multiple turns and segment timing. In the [Parameters panel](../../studios/README.md#parameters), apply the table edits, select a numeric parameter and choose **Animate parameter** to open its track. Formula parameters follow their numeric inputs, so animate an input such as Height when Width is defined as Height × 2.

Select the starting key of a segment and set **Timing to next key** to Constant speed, Ease in, Ease out, Ease both ends, or Hold. **Clip default** preserves the clip's existing easing setting. Hold keeps the starting value until the next key. Timing is saved on the starting key.

Owner approved **Custom curve** for segment timing. Select the starting key, choose Custom curve, and click **Open Curve Editor**. The compact component has two draggable handles and exact Time and Progress fields, with coordinates from 0 to 1. Click **Apply curve** to store it. The horizontal coordinate controls elapsed time and the vertical coordinate controls progress toward the next key. Scrubbing, playback and rendering use the saved curve.

The component is hosted in its own dock tab, also available from **Panels → Curve Editor**. Other features can provide a curve and an apply action to the same host. Animation is the current integrated use. Kinematic drive profiles are a possible future use. [ASM-U005](../../../Prism.Tests/Unit/Assembly/KinematicsTests.cs) verifies evaluation. [ASM-H010 through ASM-H012](../../../Prism.Tests/Headless/Assembly/AnimationTimelineTests.cs) verify persistence, pointer editing and the panel lifecycle.

Windows verification passed 34 Assembly and ribbon Headless checks and 15 Assembly Unit checks. The tests dragged a handle, applied a curve through the separate panel, saved and reopened its timing, and checked that ending the animation clears the editing target. Rendered playback remains a manual checklist item.


Unrestricted rotation tracks accept multiple turns. For five turns in five seconds, key 0° at zero seconds and 1800° at five seconds, then set the first key to Constant speed. Limited joints continue to respect their configured limits.

Windows verification passed 32 targeted Assembly and ribbon Headless checks and 14 Assembly Unit checks. ASM-H009 covers parameter-table access and formula preservation. ASM-H010 saves and reopens a five-turn shaft clip and its segment timing. ASM-U004 checks the five timing presets and the original clip easing behavior. Desktop playback of these presets remains on the [manual checklist](../../tabs/checklists/animate-manual-test-checklist.md).

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
