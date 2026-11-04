# ECAD

Mechanical integration only: boards, component envelopes, fit checks and STEP exchange. It has no electrical design tools.

- **Select, Datum:** shared sections
- **References:** Create Sketch, Construction Plane
- **Board:** Rectangular board / Board from outline (dropdown), Mounting Hole
- **Components:** Component Envelope
- **Transform:** shared section
- **Mechanical checks:** Interference
- **Inspect:** shared section
- **Exchange:** Import STEP, Export STEP
- **Appearance:** shared section

## Maintained ECAD checks

[Board and envelope headless tests](../../Prism.Tests/Headless/Ecad/BoardTests.cs) verify board dimensions, elevation, hole toggle, invalid insets, outline closure, mounting-hole placement and misses, material retention, undo/redo, and envelope side, rotation and clearance. Run `./tools/test.ps1 -Layer Headless -Feature ECAD`. [Issue #53](https://github.com/chrizpro/prism/issues/53).

Maintained tests for interference output, internal outline cutouts, document roundtrip and STEP exchange remain open. The [standalone ECAD suite](../../tests/Prism.EcadTests/Program.cs) has earlier roundtrip and STEP checks.
