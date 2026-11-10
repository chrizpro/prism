# Mesh manual test checklist

See the [feature guide](../../modeling/mesh-and-reverse-engineer/README.md). Record the Prism build and any result that differs from the expected behavior.

- [ ] Import a mesh with a known scale and an open boundary. Run Mesh Quality or Print Check and confirm the opening is flagged.
- [ ] Repair a simple open mesh and rerun the check. Compare boundary counts and confirm the intended shape is preserved.
- [ ] Split or reduce a copy and inspect both the result and Undo. Check that the original asset remains available.
