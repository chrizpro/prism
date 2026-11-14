# Thermal manual test checklist

See the [feature guide](../../cae-studies/README.md). Record the Prism build and any result that differs from the expected behavior.

- [ ] Set up a simple block with two different fixed temperatures on opposite faces. Confirm the marked faces are the intended ones.
- [ ] Open Materials from Setup, then Thermal Analysis. Verify Setup, Heating & Cooling, Mesh and Solve are easy to find and the heating/cooling controls appear for the selected faces.
- [ ] Run Thermal Analysis and inspect the temperature field. Confirm the hot and cold boundaries match the inputs.
- [ ] Save and recall a setup, change one boundary value, then rerun and compare the result.
