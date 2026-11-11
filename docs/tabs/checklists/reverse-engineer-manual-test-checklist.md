# Reverse Engineer manual test checklist

See the [feature guide](../../modeling/mesh-and-reverse-engineer/README.md). Record the Prism build and any result that differs from the expected behavior.

- [ ] Import a scan or mesh with a clear planar region. Select that region and fit a surface. Check that the preview follows the selected points.
- [ ] Create a Section Sketch through a known feature and compare its outline and scale with the scan.
- [ ] Run Deviation between the scan and the fitted geometry. Confirm you can identify the largest visible mismatch and clear the display.
- [ ] From a cylinder section, edit the generated circle sketch and add a diameter dimension. Extrude with the intended extent, then edit the dimension and confirm the solid rebuilds. Save/reopen and repeat the edit.
- [ ] Take an axial section of a tube. Remove the profile on one side of the axis, dimension the remaining profile and revolve it. Confirm wall thickness, diameter and height; run Deviation against the original scan.
- [ ] Verify Fit Surface to Region is directly accessible under Fit Surfaces and Mesh to Solid is under Rebuild → Fallback. Check that descriptions distinguish editable sketches, fitted/direct-edit CAD, a freeform cage and triangle-based conversion.
- [ ] Try a real scan with uneven noise, holes and thin features. Check which regions automation misses; correct one region manually and compare before/after with Deviation. Record the source mesh, tolerances and units.
