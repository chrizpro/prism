# Solid feature details and body edits

Rib fills the web between a straight sketch line and the body it meets (a gusset in a corner, `SOL-H109`), with thickness across the sketch plane; Reverse switches to the other side of the line, and "Fixed height" grows the lines by a set height instead (`SOL-H036`). Emboss joins a raised planar profile or cuts an engraving. Hole drills from a face or body with extent and thread options; the circle drawn for the hole can be dragged in the viewport to set its diameter (`HDL-H033`, owner report 2026-11-13). Fillet and Chamfer dress selected edges. Shell offsets walls and can open selected faces. Draft tilts walls about a neutral plane. Split Body keeps the pieces as separate bodies.

## Review and verification

This guide describes observed behavior. Additional requirements are proposed and need owner review. [Tracking issue #27](https://github.com/chrizpro/prism/issues/27).

- [Maintained headless coverage](../../../Prism.Tests/Headless/Solid/BodyEditTests.cs)

The headless cases cover a raised Emboss, a joined Rib, a plain through-hole, single-edge Fillet and Chamfer, open-face Shell, single-face Draft and an equal-volume Split Body.

## Emboss on a curved face

- **EMB-P1 delivered 2026-11-14 (owner request: Emboss was only an Extrude with another name).** Emboss used to extrude the sketch straight along the sketch plane's normal, so on a curved face the letters stood off a flat plane and only touched the surface along a line. Emboss now has a **Method** (*Automatic*, *Straight (extrude)*, *Follow the surface*) and an optional **Face to emboss onto**. On a curved face (Automatic, or Follow the surface) the shape is seen straight on from the sketch, the part of the face it covers is cut out, and that patch is lifted (Emboss) or sunk (Engrave) by the height along the face's own normals, so the top of the letters follows the surface at the same height all over, whatever the face is: cylinder, cone, sphere, a freeform surface. A shape with a hole, like an O, leaves the hole bare. The sketch can be in front of the face as well as on it. A round body is crossed twice by the shape, at the front and at the back, and only the face the sketch looks at is used; picking the face limits it further. On a flat face Automatic is the straight extrude as before, so nothing changes there.
- **Imprint** (Split Face, Mode) used to report success and change nothing for a sketch or curve on a curved face. It now brings curves onto the face as seen from the curves (a flat sketch along its own direction, other curves to the nearest spot), keeps the hits on the near side of a round face, and splits the face with them; if nothing could be marked it says so. A flat sketch is imprinted as before.
- **Limits:** the walls of a raised shape run along the surface normals, so letters across an edge between two faces leave a small gap at a convex edge; limit it to one face, or sketch each part on its own face. Very small details compared with the height (strokes thinner than the height, say) may fail to thicken, and the panel then says how many patches could not be built. Reverse direction applies to Straight only. Text is a sketch tool: the letters are lines and splines in the sketch, not a separate object, so they cannot yet be edited as a string once placed.
- **Acceptance.** A 12 x 10 mm rectangle 1.5 mm proud on a Ø40 cylinder adds the ring sector `10 x asin(6/20) x ((20 + 1.5)^2 - 20^2)` = 189.7 mm³ on the front only, and engraved 1.5 mm removes 176.0 mm³; a square frame (12 mm with a 6 mm hole) adds the outer sector less the inner; a 10 mm square on a Ø40 sphere is valid with its front 1 mm out; a 2 x 3 mm rectangle on a flat top is still +6 mm³ whichever Method is chosen. Imprint of the rectangle on the cylinder adds one face of area `10 x 20 x 2 asin(6/20)` = 121.9 mm² and leaves the volume alone. Tests: [SOL-H170 to SOL-H175](../../../Prism.Tests/Headless/Solid/EmbossSurfaceTests.cs), [SOL-H176, SOL-H177](../../../Prism.Tests/Headless/Solid/ImprintCurvedTests.cs) and the earlier [SOL-H035](../../../Prism.Tests/Headless/Solid/BodyEditTests.cs). Not tried by hand with real text.

## Design history saved by earlier builds (2026-11-13)

A saved design-history step lists its command's numbers by position, so a command that gains a parameter in the middle of its list would read an older file wrongly. Three changes did exactly that, found in a branch review before the 0.3.0 alpha: **Move with gizmo** on the primitives and Insert Component, **Rib extends** on Rib, and **Scale type** with the X, Y and Z factors on Scale. Each command now upgrades an older step on load: a primitive keeps its Join, Cut or Intersect operation, an older Rib keeps **Fixed height** (its only behavior then), and an older Scale stays a uniform scale with its factor, pivot and copy choice (`SOL-H112` to `SOL-H114`, [saved-history upgrade checks](../../../Prism.Tests/Headless/Solid/SavedHistoryUpgradeTests.cs)). A command that adds a parameter later should append it at the end of its list, or override `UpgradeRecipe` with `InsertValue`. Joints (all six types) had the same problem with **Placement** and are covered by `SOL-H117`. A comparison of every command's parameter order before and after this branch (the committed API snapshot against the current one) found nothing else that replays: Section, Motion study and Draw on Surface never make a history step, and the print and resin exports only gained an empty action button before their Export button.

## Coverage gaps

Whole-body Shell subtracts the inner solid from the outer solid to keep a closed wall. The [resin hollow-and-drain case](../../../Prism.Tests/Headless/Cam/ResinPreparationTests.cs) verifies an inward wall, a blind drain through one wall, and the cavity of an outward shell. [Bug #32](https://github.com/chrizpro/prism/issues/32). Engraving, threads, multi-edge blends, open-face outside shells, multi-face drafts and split misses still need maintained coverage.

## Split Body by a sketch — Approved 2026-11-16, delivered (tests, not tried by hand)

**Owner decisions 2026-11-16:** a closed shape inside the outline makes a plug and a body with a pocket; a line that stops inside the body is refused. **Delivered:** Split Body accepts a sketch or curve. The sketch is swept along its plane's normal through the whole body in both directions and the body is split by that sheet, so one rule covers all three cases. Tests [SOL-H160 to SOL-H163](../../../Prism.Tests/Headless/Solid/SplitBodyBySketchTests.cs): a line across a 100 mm cube gives two 500,000 mm³ halves, a 20 mm square gives a 40,000 mm³ plug and a 960,000 mm³ body, a stopped line is refused with "stops inside the body", and the sketch is deleted by default. **Not built:** the *Extend the line to the body's edges* and *Direction* options, and the same rules for Split Face with a sketch (open decision b). The proposal text below is the original draft.

**Today.** [Split Body](../../../Prism/Commands/DirectCommands.cs) accepts a plane, face, surface or solid as the splitting tool and gives each piece its own body. It does not accept a sketch, while [Split Surface](../surface-and-nurbs/README.md) already accepts a sketch or curve and splits along its projection on the surface.

**Intent.** The owner's rule: the tool must work out what the sketch is meant to do, and a line drawn right through the body means "cut here". Proposed reading, in order:

1. **A line or chain that crosses the whole body as seen along the sketch's normal** (it runs past the body's outline at both ends): the sketch is swept straight through the body, in both directions, and the body is split along that sheet. This is the owner's clear case.
2. **A closed loop that lies inside the body's outline:** the body is split into the prism the loop encloses and the rest, so you get a plug and a body with a matching pocket.
3. **A chain that stops inside the outline, or leaves on one side only:** ambiguous. Proposed: refuse with a message that says so and offers *Extend the line to the body's edges*, which treats it as case 1. The tool never guesses past that.
4. **Several curves in one sketch:** each is read separately and they all cut at once.

**Options (proposed):** *Direction* (both ways through the body by default; one side or a distance if you want less), *Delete the sketch afterward* (see [cutting tools](../surface-and-nurbs/README.md#6-keep-the-originals-consistency)), and *Extend the line to the body's edges*. The sketch plane does not have to touch the body.

**Open decisions.** (a) Is the closed-loop plug (case 2) wanted, or should a loop be refused as "that is a cut, use Extrude Cut"? (b) Should the same rules apply to Split Face with a sketch, which currently projects onto the face? Recommended: yes, so the two tools agree.

**Acceptance (draft).** A line across the middle of a 100 x 100 x 100 mm box, drawn on its top plane and running past both sides, gives two bodies of 500,000 mm³ each whose volumes add to the original and whose cut faces are flat. A 20 mm square loop in the middle gives a 20 x 20 x 100 mm plug (40,000 mm³) and a body with the pocket. A line that stops at the middle is refused with the message above. Not yet tested; no tests are written until you decide.

## Split Body and Split Face: whole sketches, a movable plane, Apply (owner request 2026-11-23, built; tests, not tried by hand)

The owner asked that Split Body and Split Face take a sketch as the cutting tool, have a checkbox for a plane controlled from the viewport, and do several cuts without reopening the panel. [SOL-H201 to SOL-H204](../../../Prism.Tests/Headless/Solid/SplitWithPlaneAndSketchTests.cs).

- **A sketch is a sketch however it is clicked.** Both tools already took a sketch picked as a whole object, but a click in the viewport often gave something else: the filled area of a closed sketch arrived as a face (Split Body then cut with that flat face and failed), and a line of a sketch arrived as one edge (Split Face then used one segment, which may not cross the face). Now any pick on a sketch or curve object means the whole sketch. Edges of bodies are still single edges. Split Body's pick is now labelled "Splitting tool (plane, face, surface, solid or sketch)"; scripts that used the old name need the new one.
- **A sketch of several joined lines now splits a face.** Split Face projected the sketch onto the face, which broke a chain of lines into loose pieces that each stopped part way, so a polyline across the face was refused (even when the sketch was picked whole). The pieces are joined back into runs after projecting (SOL-H202).
- **Cut with a movable plane** (checkbox). The tool pick is hidden and a see-through square plane is shown through the centre of the first body or face picked. The shared gizmo moves and turns it; Plane X/Y/Z (world position) and Plane turn X/Y/Z (degrees) set it exactly. With no turn the plane is horizontal. A plane that misses is refused with a message.
- **Apply** splits and reopens the tool with the same options; the movable plane stays where it was (it is not moved to the next body's centre), so a row of bodies or faces can be cut on one plane.
- Older saved steps keep their values: the new options come after the old ones.

## Split extent across modeling workspaces

**Proposed, awaiting owner review.** The [split extent issue](https://github.com/chrizpro/prism/issues/160) tracks the request.

| Command | Selected face | Through all |
| --- | --- | --- |
| Split Face | Divide the explicitly picked face into regions and keep one solid. | Divide all boundary faces crossed by the tool and keep one solid. |
| Split Body | Divide the explicitly picked face into regions and keep one solid. | Create separate solids wherever the tool fully divides the volume. |
| Split Surface (Surface and NURBS) | Apply the tool to explicitly picked surface faces. | Apply the tool across the entire picked surface object. |

The proposed control is **Extent**. A planar sketch would use its own normal for **Through all**, as Split Body already does. An explicit face pick lets the user orbit the model without changing which face will split. Existing saved features should replay with their previous behavior. Freeform's Split Face edits the subdivision cage. A through-cut that preserves the cage needs its own design.

**Open decisions.** Confirm whether **Through all** in Split Face should keep one solid, and whether **Selected face** should also be available inside Split Body. Surface and NURBS also need a decision on what **Separate surfaces** should return when only selected faces are split. The direction for nonplanar curve tools still needs a rule.

**Verification planned after review.** Compare face counts, solid counts and volume on a block, then compare face counts and area on a multi-face surface. Exercise Apply and check that older saved split steps replay. The current [Split Body sketch tests](../../../Prism.Tests/Headless/Solid/SplitBodyBySketchTests.cs) and [Split Face and Split Body command tests](../../../Prism.Tests/Headless/Solid/SplitWithPlaneAndSketchTests.cs) cover the baseline.

## Fillet "inverse" — Cove approved 2026-11-16, delivered (tests, not tried by hand)

**Delivered, with a correction to the draft below.** Fillet has a **Shape** choice, *Round* or *Cove (concave scoop)*. **The draft's "tangent to both walls" cannot be done:** a concave scoop that is tangent to both faces of a convex edge would have to touch them outside the body. What is built is the quarter-round cove: a cylinder of the chosen *Radius* centred on the straight edge is cut away, so on a square edge the walls meet the scoop at **right angles**. Tests [SOL-H090 to SOL-H094](../../../Prism.Tests/Headless/Solid/CoveFilletTests.cs): radius 2 on one 10 mm edge of a 10 mm cube removes a quarter disc, 10 pi = 31.4 mm³ (hand calculation); two opposite edges remove twice that; a round fillet of the same radius removes (4 - pi) x 10 = 8.6 mm³, less; a curved edge is refused. **Limitations:** straight edges only (curved edges are refused); on a concave (inside) edge the same cylinder cuts a round groove, so use it on outside edges; *End radius* and *Radius per edge* are hidden for a cove; two coves meeting at a corner overlap slightly there; Chamfer is unchanged. The draft below is kept for the record.

**Owner decision 2026-11-16:** "inverse" means concave, the cove (option A below); a Shape choice *Round | Cove*. Not implemented in this change: it needs new kernel geometry and a hand-worked cross-section for the acceptance test. Chamfer is unchanged. The draft below stands as the specification to build from.

**Today.** [Fillet](../../../Prism/Commands/SolidCommands.cs) rounds selected edges with a radius, an optional end radius and per-edge constant or variable radii; there is no option to reverse the way it curves.

**What "inverse" could mean — please choose.** "Inverse" is used for two different things in CAD programs and the request does not say which:

- **A. Cove (inside-out round).** On a convex edge, instead of rounding the corner off (a quarter circle centred inside the body), cut a hollow scoop whose arc is centred outside the body, so the two faces meet the new surface tangent but it is concave. Used for decorative edges and for clearance. On a concave edge the same option would add a convex bulge.
- **B. Invert the side the fillet is made on**, when the edge is shared by faces that could be filleted either way (for example a thin rib). This is a *Reverse direction* checkbox like the one on Extrude.

**Recommendation:** A, as a **Shape: Round | Cove** choice, because B is already what happens automatically from the edge's convexity. **Open decisions:** which of A or B; whether Chamfer gets the same choice (a chamfer has no curvature, so recommended no); how a variable radius behaves with a cove (recommended: same rule per edge).

**Acceptance (draft, for A).** The cove surface is tangent to both faces within 0.01° and is concave. The removed volume equals the cove's cross-section area times the edge length, with the cross-section worked out by hand once A is chosen (a hand calculation, not read from the app). Undo restores the body. Not yet tested; no tests are written until you decide.

## Planned advanced edge blends

Owner direction on 2026-11-16 ([#139](https://github.com/chrizpro/prism/issues/139)): substantially improve fillets and chamfers, including advanced corner transitions such as setback fillets. Current Fillet already supports an end radius and per-edge constant/variable radii. Current Chamfer supports equal distances, two distances and distance-plus-angle with side swapping. No explicit setback-corner option was found in the current commands. Additional blend controls, robustness and failure diagnostics are roadmap directions; detailed behavior remains for review before implementation or new feature tests.
