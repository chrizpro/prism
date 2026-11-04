# Assembly and animation

The **Assemble** workspace builds assemblies bottom-up and animates them. Reference: [Assemble](/tools/assemble), [Joints](/tools/joints), [Animate](/tools/animate).

## Parts and components

Your workspace folder is your parts library. **Parts Library** lists every part in it, with part numbers, descriptions, revisions and costs from each part's **Part info**. **Insert Component** places one in the assembly.

**Make Part** turns bodies already in the file into parts where they stand, so joints can connect them; picking a plain body with a joint tool does this for you. **Create Part** starts a new part inside the assembly and opens it in the Design workspace; **Edit Part** opens the selected component's part the same way. Press **Back to assembly** to bring the changes in. **Ground** fixes a component in place; the first component is grounded for you.

## Position

- **Move Component** drags a component freely.
- **Mate** snaps a free component onto another by two faces: touching, flush or a set distance apart.
- **Component Pattern** repeats a component in a line or around an axis. Each copy is a free component that can be jointed or animated on its own.

## Joints

Joints position components from the grounded parts. Pick a face, edge or point on each of two components and choose the type:

| Joint | Allows |
| --- | --- |
| Rigid | No motion |
| Revolute | Rotation about one axis |
| Slider | Translation along one axis |
| Cylindrical | Rotation and translation along one axis |
| Ball | Rotation about a point |
| Planar | Motion in a plane |

Joint limits clamp the motion. **Couple** makes one joint follow another by a ratio. **Drive Joints** sweeps the motion and **Interference** checks that nothing collides.

## Review and share

**Parts List** counts every part with material, mass and cost, and saves a CSV. **Exploded View** pulls the assembly apart. **AR Export** shares it to a phone.

## Animate

1. **New Animation** creates an empty five-second animation.
2. **Edit / Play** opens the timeline. Scrub to a time, change joint values or the exploded view, and press **Key the changed values**.
3. Play to see it move, and **Save animation** to store the keys with the document.
4. **Render Animation** turns it into pictures or a movie; MP4 output needs ffmpeg.
