# ECAD manual test checklist

See the [feature guide](../../ecad/README.md). Record the Prism build and any result that differs from the expected behavior.

- [ ] Import a populated board STEP from your electronics tool. Confirm scale, board orientation, mounting holes and the expected component solids.
- [ ] Place a connector or envelope on the board by clicking the mounting location and setting the angle. Check that it lines up with the enclosure opening. Try the same workflow with a tilted board.
- [ ] Use **Check Clearance** between the connector and lid. Enter the minimum gap you need and compare it with a known dimension. Use **Section View** to inspect the inside.
- [ ] Review and export just the board and desired components. Reopen the STEP file in the receiving app and verify the body count, scale and fit.
