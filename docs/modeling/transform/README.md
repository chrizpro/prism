# Solid transforms

Move translates in XYZ. Rotate uses a world axis and a selectable pivot. Scale is uniform by default; **Scale type: Per axis (stretch)** gives X, Y and Z their own factors (owner report 2026-11-13: only a uniform scale existed). In that mode the viewport shows one arrow on each face of the selection and dragging it sets that factor (`SOL-H110`, `SOL-H111`). The stretch follows the world axes, so a part turned off-axis is sheared; a sphere becomes an ellipsoid and other curved faces become freeform surfaces. The kernel call needs the native library built from this change (`ck_scale3`). Mirror reflects across a world plane and defaults to making a copy. Duplicate copies selected objects through the session action.

In the viewport Move, Rotate and Scale are driven by the shared gizmo: arrows for the offsets, three rings for the angle (pressing a ring makes its axis the turn axis and starts again from 0) and the centre block for the factor. Maintained check: HDL-H023 in [ViewportHandleTests](../../../Prism.Tests/Headless/Viewport/ViewportHandleTests.cs); see [viewport interaction](../../viewport-interaction-handoff.md#command-gizmo-2026-11-12).

**Moving sketches and datum planes (owner report 2026-11-19: "I sometimes find myself moving the gizmo, and the object didn't move").** Two causes were found. Move, Rotate, Scale and Mirror refused datum planes and reference images, so with one selected nothing could move. And a moved sketch kept its old plane and lost its sketch data, so it became a plain curve and reopened where it was. Now a sketch moves by moving its plane (a uniform Scale also scales its points, radii and dimension lengths) and stays editable, and a datum plane's plane moves with it. A third look-alike remains by design: the Datum section's free **Gizmo** moves only itself. Check: [SOL-H178](../../../Prism.Tests/Headless/Solid/MoveEveryKindTests.cs).

## Review and verification

This guide describes observed behavior. Additional requirements are proposed and need owner review. [Tracking issue #29](https://github.com/chrizpro/prism/issues/29).

- [Maintained headless coverage](../../../Prism.Tests/Headless/Solid/TransformTests.cs)

The headless cases check exact-body bounds and volume for translation, world-origin rotation, uniform scaling and mirror copy, plus the Duplicate session action.

[Warp tests](../../../Prism.Tests/Headless/Solid/WarpTests.cs) check Stretch with start/end ranges, Taper, Twist, Bend of a line centred on the origin, a stretched solid and Bend of a stored NURBS surface patch (WRP-H008).

## Copy and paste in general — Direction approved 2026-11-16; table below still awaiting review; not built yet

**Owner direction 2026-11-16:** copy and paste should mostly work on what is selected, including faces and edges, and possibly something unique with vertices. This could eventually replace the dedicated *Extract Edges* tool, which stays unchanged for now. The table of what each selection pastes as, and the open decisions, still need the owner's review.

Owner request 2026-11-16: allow copy and paste wherever it helps, not only on Freeform faces (see [Freeform copy and paste](../freeform/README.md#copy-and-paste-of-faces-and-in-general)).

**Today** (read from [CadSession.Clipboard.cs](../../../Prism/Core/CadSession.Clipboard.cs) on 2026-11-16, not tried by hand). `Ctrl+C` and `Ctrl+V` copy and paste **whole objects** only. They are shared by every open document, so a part can go to another tab, and Alt-drag with Move leaves a copy behind. They do nothing while a command panel is open or while sketching, and they ignore annotations, reference parts and assembly components. Faces, edges, sketch entities and Freeform cage parts cannot be copied.

**Proposal: copy whatever is selected, at the level it is selected.** One rule: Ctrl+C copies what you have selected, Ctrl+V pastes it as the nearest useful thing, in the same place, selected so the gizmo can move it. Ctrl+Shift+V pastes at the pointer.

| Selected | Paste gives | Notes |
|---|---|---|
| Objects (today) | Copies of the objects | Unchanged. |
| Faces of a solid | A new surface object made of those faces | A solid face is never pasted back as a solid, because it would be open. |
| Faces of a surface or NURBS | A new surface with the faces joined where they touch | |
| Freeform cage faces | A new Freeform body (or join the cage, a panel choice) | Points, creases and frozen flags come with it. |
| Edges or a curve | A new curve | |
| Sketch entities (while sketching) | The same entities in the active sketch, offset slightly so they are visible, or in another sketch on a different plane | Dimensions and constraints among the copied entities come along; those to entities left behind are dropped and the panel says how many. |
| Whole sketch | A copy of the sketch on the same plane | |
| Mesh selection | A new mesh of those triangles | |

**Not proposed:** copying history steps, ribbon layouts or assembly components (assemblies have their own insert and pattern tools).

**Open decisions.** (a) Allow paste while a command panel is open (for example into a selection slot)? Recommended no. (b) Paste at the original place, or at the pointer, by default? Recommended original place, as today, so a pasted copy never lands somewhere surprising. (c) Which rows to build first? Recommended Freeform faces, sketch entities and surface faces. (d) Is a pasted face of a solid a surface (recommended) or should the tool ask?

**Acceptance (draft).** Copy the top face of a 10 mm cube and paste: a one-face surface of area 100 mm² exists at the cube's top, the cube is unchanged, and Ctrl+Z removes only the surface. Copy two connected lines and a circle in a sketch and paste: the sketch holds the originals plus the copies, with the dimensions between the copied entities. Copy in one document and paste in another tab gives the same result. Not yet tested; no tests are written until you decide.

## Coverage gaps

Fixed on 2026-11-05: Warp's Bend used to add the shape's centre along the bend axis twice, so a shape not centred on the origin jumped when bent (a line from X = 0 to 100 bent 90° ended at X ≈ 113.7 instead of 63.7). Bend tests now cover a centred line and one starting at the origin.

Fixed on 2026-11-12: Warp could not touch any stored NURBS surface — a Control Grid patch reported "Face has no surface." and lofted or swept surfaces crashed the app outright. The build reads the object's mesh for its bounds before converting to NURBS, and converting an already-NURBS face that carries triangulation produced faces with no surface at all (the converter rebuilds those faces for the cached mesh but reports no surface change). `ck_nurbs_convert` now converts a mesh-free copy. WRP-H008 bends a stored patch 90° into a quarter cylinder; warp on Control Grid, loft and sweep surfaces was re-verified in the running app.


Mixed selections, custom transform pivots and desktop gizmo interactions still need coverage.

Windows verification on 2026-11-05: the combined candidate passed 86 Unit and 592 Headless tests with rebuilt native libraries. See the [integration verification report](../../engineering/release-readiness-2026-11-05.md) for source revisions, commands and limits.
