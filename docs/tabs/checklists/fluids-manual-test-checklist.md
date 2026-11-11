# Fluids manual test checklist

See the [feature guide](../../cae/fluids/README.md). Record the Prism build and any result that differs from the expected behavior.

- [ ] Run Pipe Flow on a straight pipe with known length, diameter, and flow input. Increase the length while keeping the other inputs fixed and compare pressure loss.
- [ ] Build a simple Pipe Network with a branch and inspect the flow balance at the junction.
- [ ] For a 3D Flow Study, use a simple duct and verify inlet and outlet face assignments and units before comparing the result with a reference case.
- [ ] Check Setup, Solve and Results in Flow Study. In Through mode, verify the selected solid represents the fluid volume inside the duct.
