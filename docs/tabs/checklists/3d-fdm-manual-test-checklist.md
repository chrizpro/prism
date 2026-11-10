# 3D FDM manual test checklist

See the [feature guide](../../cam/README.md#3d-fdm). Record the Prism build and any result that differs from the expected behavior.

- [ ] Place a flat face of a rectangular solid on the bed. Check that the face points down, sits at Z = 0, and the dimensions are unchanged.
- [ ] Enter a build width smaller than the part and run FDM Check. Increase the width and rerun to confirm the fit result updates.
- [ ] Select one of two bodies and review export, then change the selection. Confirm the review updates before a file is written.
- [ ] Open a multi-body 3MF in your slicer. Check the object count, millimetre dimensions, orientation, and first layer. Repeat with STL if that is your usual format.
