# Assembly and animation

The **Assemble** workspace builds assemblies bottom-up and animates them. Reference: [Assemble](/tools/assemble), [Joints](/tools/joints), [Animate](/tools/animate).

## Parts and components

Your workspace folder is your parts library. **Parts Library** lists every part in it, with part numbers, descriptions, revisions and costs from each part's **Part info**. **Insert Component** places one in the assembly.

**Make Part** turns bodies already in the file into parts where they stand, so joints can connect them; picking a plain body with a joint tool does this for you. **Create Part** starts a new part inside the assembly and opens it in the Design workspace; **Edit Part** opens the selected component's part the same way. Press **Back to assembly** to bring the changes in. **Ground** fixes a component in place; the first component is grounded for you.

<GuideImage image="assembly-components" />

## Position

- **Move Component** drags a component freely.
- **Mate** snaps a free component onto another by two faces: touching, flush or a set distance apart. Use **Assemble → Joint → Rigid → As Positioned** to lock two parts together after lining them up. Select the parts in the viewport to keep their current relative position and angle.
- **Component Pattern** repeats a component in a line or around an axis. Each copy is a free component that can be jointed or animated on its own.

## Joints

Choose a joint from **Assemble → Joint** or the dedicated **Joints** tab. For a snapping joint, pick a face or edge on each component. The preview shows which part will move and labels the joint axis or slide directions.

| Joint | Allows |
| --- | --- |
| Rigid | No motion |
| Revolute | Rotation about one axis |
| Slider | Translation along one axis |
| Cylindrical | Rotation and translation along one axis |
| Ball | Rotation about a point |
| Planar | Motion in a plane |

Joint limits clamp the motion. **Couple** makes one joint follow another by a ratio. **Drive Joints** sweeps the motion and **Interference** checks that nothing collides.

### See what each joint allows

<JointGuide />

The joint axis comes from the local joint frames you place on the two components; it need not match a world axis. Grounding fixes a component in the assembly. A rigid joint fixes two components relative to each other, so the pair can still move together if neither is grounded.

### Start with a hinge

1. Make the base and moving arm into separate components with **Make Part**, or insert existing parts.
2. **Ground** the base that should stay still.
3. Choose **Revolute**. Pick a face, edge or point on each component to locate the connection and check the joint axis.
4. Use **Drive Joints** to check the arm's rotation. If it should slide instead, use **Slider**; if it should both slide and turn, use **Cylindrical**.
5. Set joint limits where the mechanism has stops, then check the motion again.

Hover over the pin or hole before clicking to see the axis Prism will use. For a four-bar linkage, add all four hinges, then drive the crank and watch the other joints follow. If the linkage cannot reach the requested position, Drive Joints keeps the last valid pose. When animating the linkage, key the crank and let Prism position the other joints.

<GuideImage image="assembly-revolute-setup" />

<GuideImage image="assembly-drive-joint" />

For two shafts that should turn together, give each a revolute joint and use **Couple** to set the ratio and direction. A cylindrical joint allows turning and sliding independently; use Couple between turning and sliding joints for a screw-like relationship.

For forces, motor effort and joint reactions, continue with a [motion study](/guide/simulation#motion). **Drive Joints** is useful for checking movement before setting up the study. Contact is not calculated during a motion study; interference checking is separate.

## Review and share

**Parts List** counts every part with material, mass and cost, and saves a CSV. **Exploded View** pulls the assembly apart. **AR Export** shares it to a phone.

## Animate

1. **New Animation** opens a five-second draft. Use **Edit / Play** to reopen a saved animation.
2. Scrub to a time, change joint values or the exploded view, and press **Add Key**. Use **Play / Pause** to check the movement.
3. Drag a key to retime it, or click it and enter an exact time and value under **Selected key** in the right panel. Use the timeline zoom buttons and collapse track groups to focus on the keys you need. For a parameter ramp, choose an existing numeric document parameter in the right panel. Click **Save animation** to store the clip.
4. **Render Animation** turns it into pictures or a movie; MP4 output needs ffmpeg.
