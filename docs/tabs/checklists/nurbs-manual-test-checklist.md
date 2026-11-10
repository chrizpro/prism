# NURBS manual test checklist

See the [feature guide](../../modeling/surface-and-nurbs/README.md). Record the Prism build and any result that differs from the expected behavior.

- [ ] Create a control grid and move an interior control point. Confirm the patch changes and the control net remains editable.
- [ ] Join or match two compatible patch edges. Run Check Seam and compare the reported gap before and after.
- [ ] Use Zebra Stripes or Curvature Map to inspect the seam and confirm Clear Analysis removes the overlay.
