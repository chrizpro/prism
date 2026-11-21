# Mesh manual test checklist

See the [feature guide](../../modeling/mesh-and-reverse-engineer/README.md). Record the Prism build and any result that differs from the expected behavior.

- [ ] Import a mesh with a known scale and an open boundary. Run Mesh Quality or Print Check and confirm the opening is flagged.
- [ ] Repair a simple open mesh and rerun the check. Compare boundary counts and confirm the intended shape is preserved.
- [ ] In Remesh, try Redistribute triangles and Reduce Faces on a copy. Inspect triangle counts, open edges and Undo. Check Keep the original too.
- [ ] On a mesh with three visible openings, open Repair → Fill Hole. Click inside one and on the rim of another. Toggle one off and back on. Fill both green previews, verify the third stays open, and Undo once to restore both.
- [ ] Choose Edit Region and highlight stray triangles near an intentional opening. Open Cleanup and check Selected region is chosen automatically. Verify Cleanup preserves the opening.
- [ ] In Cleanup with Entire mesh scope, remove duplicate or degenerate triangles. Inspect the opening count and verify intentional openings remain.
- [ ] Include only part of a small floating shell, then all of it. With shell removal enabled, confirm it is removed only when wholly selected. Check nearby unselected faces and Undo.
- [ ] Try an actual scan with uneven noise and missing patches. Compare shape and dimensions before/after Repair and Remesh; inspect reported volume change, thin walls, corners and any remaining defects.

- [ ] Hover a face, vertex and edge in Draw Faces and check the target tint and distinct snap markers. Draw a patch over a torn corner beside an intentional opening. Click a rim midpoint, inspect the split and green shared edges, and verify the other opening remains open. Undo restores the original mesh.
- [ ] Stitch a pair of open seam edges within the set gap. Lower the maximum gap and confirm the preview refuses the join without moving either side.
