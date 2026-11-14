# Resin manual test checklist

Use a part you know and your usual resin slicer. Record the Prism build, slicer version, printer profile, and any result that differs from the expected behavior. See the [3D Resin guide](README.md#3d-resin).

- [ ] Open a hollow part you know well. Tilt it and use **Troubleshoot geometry → Section** to sweep through each drain or vent. Confirm each opening reaches its cavity and exits where you intended.
- [ ] Run **Resin check** at the intended print angle. With a shallow or narrow opening, compare the result at a finer sampling size against the section and slicer preview.
- [ ] Export to your usual resin slicer with **Export and open in slicer**. Try once with the slicer closed and once while it is open. Check that the expected bodies, dimensions, orientation and holes arrive. If your slicer requires STL, repeat with STL and millimetre scale.
- [ ] With your real printer profile, add supports and a raft if needed. Check build fit and inspect the layers around the cavities, drain openings and unsupported starts. A physical print, if you choose to run one, is a separate check.
