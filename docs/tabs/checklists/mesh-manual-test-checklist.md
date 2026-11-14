# Mesh manual test checklist

See the [feature guide](../../modeling/mesh-and-reverse-engineer/README.md). Record the Prism build and any result that differs from the expected behavior.

- [ ] Import a mesh with a known scale and an open boundary. Run Mesh Quality or Print Check and confirm the opening is flagged.
- [ ] Repair a simple open mesh and rerun the check. Compare boundary counts and confirm the intended shape is preserved.
- [ ] Split or reduce a copy and inspect both the result and Undo. Check that the original asset remains available.
- [ ] On a mesh with two openings, use Select Region (brush or lasso) around the complete rim of only one opening. Open Repair and confirm Selected region is chosen automatically. Confirm only that rim is green, apply, and verify the other opening stays open.
- [ ] Select only part of a rim. Confirm it has no green fill preview and remains open. Grow the selection until the entire rim is included and repeat.
- [ ] In Entire mesh scope, turn Fill holes off and run cleanup. Verify intentional openings remain. Turn it on and inspect every green rim before applying.
- [ ] Include only part of a small floating shell, then all of it. With shell removal enabled, confirm it is removed only when wholly selected. Check nearby unselected faces and Undo.
- [ ] Try an actual scan with uneven noise and missing patches. Compare shape and dimensions before/after Repair and Remesh; inspect reported volume change, thin walls, corners and any remaining defects.
