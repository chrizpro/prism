# Direct Modeling tab

Edit the geometry you already have, with no history needed. It works on any exact body, including parts imported from STEP, IGES or BREP. The tab is laid out left to right in the order you tend to use it:

**Select, Datum, Add, Edit faces, Edit features, Repair, Transform, Inspect, Appearance**

How to use it in practice is in the [user guide](../../direct-modeling.md). This page is the overview: what each section is for, what every tool does, and where each one could improve.

**Status key:** *Done* works and is covered by tests. *Thin* works but is only tested on simple parts. *Gap* has a known limitation.

## Across the whole tab

- **Click a face, get the whole feature.** In the direct tools one click on a hole, slot, pocket, boss, counterbore, countersink or fillet selects the whole feature. The panel's **Select by** choice also offers single face, tangent chain, same size and pattern. Hovering previews what a click would select.
- **Delete key and right-click** are feature-aware: with faces selected they remove or edit the feature, never the whole body.
- **Speed:** edits that touch many features apply their changes in one step.

Improvements that apply to every section:
- Nothing on this tab has been used in the running app yet; everything is verified by headless tests. A walkthrough in the real app is the first thing to do.
- Several tools share an icon (see "Icons" at the end), so the ribbon is harder to scan than it should be.
- Only Push / Pull, Move, Copy and the Edit Feature move have a drag arrow in the viewport. The other edit tools are typed values only.

---

## Select

**Intent:** choose what the next tool works on. Bodies, faces and edges; the group is shared by every modeling tab.

| Tool | What it does | Status / where to improve |
|---|---|---|
| Object | Pick whole bodies, surfaces and curves. | Done. |
| Face | Pick individual faces. | Done. In this group a click always picks a single face. The smarter "whole feature / pattern" choice only exists inside tool panels. A feature mode here would make selecting before choosing a tool as easy as inside a tool. |
| Edge | Pick individual edges. | Done. Edge chain rules (connected, tangent, partial loop) live in the tool panels. |
| Vertex | Pick cage vertices (Freeform bodies only). | Not relevant to exact bodies on this tab. |

## Datum

**Intent:** reference geometry: points, axes, planes and the gizmo that positions things.

| Tool | What it does | Status / where to improve |
|---|---|---|
| Gizmo | Position and bind the gizmo; adjust its handles and snapping. | Done. |
| Point | Place a reference point on the active drawing plane. | Works in sketches only. |
| Axis, Plane, Save gizmo | Save a gizmo axis, plane or the gizmo itself as a datum. | Only active while sketching. On this tab the group is mostly the gizmo; direct modeling would use datums made from faces, hole axes and pattern centres (for example "plane through this face", "axis of this hole"). |

## Add

**Intent:** create new geometry on an existing body. Everything here makes something new; nothing here changes what is already there.

| Tool | What it does | Status / where to improve |
|---|---|---|
| Hole | New plain or modeled threaded hole on any exact body, through-all or blind, from a face or a point. Metric sizes available. | Done. Thread sizes are metric only. |
| Fillet | Round selected edges. | Done, an older tool. The panel does not use "Select by", so picking all edges of the same kind is manual. |
| Chamfer | Bevel selected edges. | Same as Fillet. |
| Shell | Hollow out a body, optionally opening faces. | Done. |
| Split Body | Cut a body into separate pieces with a plane, face or surface. | Done. |

## Edit faces

**Intent:** change existing faces and let the body follow. These tools work on faces, not on recognised features.

| Tool | What it does | Status / where to improve |
|---|---|---|
| Push / Pull | Drag flat faces, whole holes or bosses out or in. Positive pulls out, negative pushes in. | Done. A slot or pocket is refused with a message pointing to Delete Face or Move Face. Edit Feature could take over those. |
| Move Face | Slide and tilt flat faces; the body stretches to follow. | Done. Flat faces only, typed values only (no drag arrow). |
| Offset Face | Move every picked face along its own normal by one distance; neighbours extend or trim. | Thin: tested on blocks and holes. |
| Rotate Face | Turn flat faces about an edge, the face's own direction or a world axis. | Thin. Flat faces only. |
| Replace Face | Make faces lie on another plane or face. | Done. A target square-on to the face is refused. |
| Extend / Trim Face | Grow or shrink a face in its own plane until it meets a target plane or face. | Thin: tested on a block wall. |
| Match Face | Give faces the plane, cylinder radius or cone angle of a source face. | Thin. Sphere and torus sources are not supported. |
| Draft | Lean walls away from a pull direction by an angle around a neutral plane. | Done. |
| Split Face | Divide a face along a plane, edge or sketch; the shape stays the same. | Thin. |
| Merge Faces | Join the picked neighbouring faces that lie on the same surface; other faces are left alone. | Done. Needs the rebuilt native kernel (see Gaps). |
| Delete Face | Remove faces and let the neighbours close the gap. Takes whole features: holes, bosses, pockets, fillets. | Done. |

## Edit features

**Intent:** change whole recognised features (holes, slots, pockets, bosses, fillets, patterns) and whole bodies.

| Tool | What it does | Status / where to improve |
|---|---|---|
| Edit Feature | Pick a feature, then **Move**, **Copy** (N copies at a spacing), **Resize** (hole or boss diameter, slot width and length, fillet radius), **Change type** (plain, counterbore, countersink, modeled thread) or **Delete**. The panel only shows what applies. | Done and the core of the tab. The ribbon tooltip still describes only move, copy, resize and delete and should mention Change type. Resize of non-round pockets and bosses is not supported (the message points to Move Face). Only Move and Copy have a drag arrow. |
| Edit Pattern | Change the spacing or count of a recognised line, grid or circle, or drop one instance. | Thin: tested on lines, grids and rings. A same-size hole at the exact centre of a hexagonal ring is read as part of the grid. |
| Mirror Feature | Copy features across a plane or flat face. | Thin: tested on a boss and a pocket. |
| Remove Fillets | Remove picked fillet chains, or every round up to a radius. | Done. |
| Edit Freeform | Shape a Freeform body by its cage (extrude, inset, delete, move). | Belongs to the Freeform tab; it sits here for convenience. |
| Move Body | Move or turn a body while keeping a relationship to another body: a face flush, two holes coaxial, or a round face tangent to a flat face or to another cylinder (pin against pin, or inside a hole). | Thin. Spheres and cones are not supported for tangency, and the motion allowed after the snap is the simple one (slide in the plane, or along the axis) with no orbiting. |

## Repair

**Intent:** make imported or damaged geometry sound before you edit it.

| Tool | What it does | Status / where to improve |
|---|---|---|
| Check and Repair | Report open edges, tiny or sliver faces, duplicates and bad edges on an imported body, then heal, sew or clean it. After an import the status line says if a body looks invalid and points here. | Thin: tested on parts with synthetic defects (a sliver face, a gap, duplicates). It has not been run on real-world broken STEP files; that is where it will need tuning. |
| Fill / Cap | Close an open loop of edges with a flat cap or a smooth patch so the body closes. | Thin. Control over how smoothly a patch meets its neighbours is not exposed. |

## Transform

**Intent:** move whole objects. Shared by every modeling tab.

| Tool | What it does | Status / where to improve |
|---|---|---|
| Move, Rotate, Scale, Mirror | Translate, rotate, resize uniformly, or reflect the selected objects. | Done. Whole-object only; the face-aware versions are in Edit faces and Move Body. |
| Duplicate | Copy the selected objects. | Done. |

## Inspect

**Intent:** read things off the model without changing it. Shared by every modeling tab.

| Tool | What it does | Status / where to improve |
|---|---|---|
| Measure | Sizes, distances and angles between faces, edges, bodies or points. | Done. Could pick up the same whole-feature selection (measure a hole's diameter with one click). |
| Properties | Volume, mass, centre of mass and moments of inertia. | Done. |
| Section Analysis | Cut the model with a plane to see inside. | Done. |

## Appearance

**Intent:** how bodies look and what they are made of. Shared by every modeling tab.

| Tool | What it does | Status / where to improve |
|---|---|---|
| Materials | Open the material library: colours, finishes and engineering data to apply to bodies. | Done. Nothing specific to direct modeling. |

---

## Known gaps

- **Browser build:** its WebAssembly kernel has not been rebuilt, so Merge Faces will not work there until it is (`native/build-wasm.ps1`).
- **Native kernel:** other machines need `native/build.ps1` to pick up the new picked-only merge.
- **Live app:** the hover preview, drag arrow, right-click menu, "Select by" control and ribbon layout have only been verified through headless tests.
- **Real-world files:** Check and Repair and the feature recogniser have been tested on parts built in code, not on large messy imported models.

## Icons

Several tools share an icon, which makes the ribbon hard to scan: Push / Pull, Offset Face and Extend / Trim Face; Move Face and Rotate Face; Replace Face, Merge Faces and Match Face; Delete Face and Remove Fillets; Split Body and Split Face. Each needs its own.

## Suggested next steps

1. Walk the tab in the running app and fix whatever looks cramped or unclear.
2. Give every tool its own icon.
3. Add a feature mode and "Select pattern" to the Select group.
4. Add a drag arrow to Offset Face, Resize and Edit Pattern.
5. Add datums made from faces, hole axes and pattern centres.
6. Test Check and Repair and the recogniser on real imported parts.
7. Rebuild the browser kernel.
