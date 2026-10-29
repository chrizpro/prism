# Native NURBS modeling exercise

Completed October 27, 2026 through the real Prism MCP bridge. These are native B-spline surface/curve studies, not SubD conversions. Open the .prism documents to inspect the models; matching PNG and STEP files are alongside them.

## Studies

| Document | Construction exercised | Final visible bodies |
|---|---|---|
| N01-nurbs-mouse.prism | 8-by-6 bicubic control net, pole editing and undo, offset thickening, wheel and button cuts, flat NURBS base | Three valid solids |
| N02-lofted-bottle.prism | Eight closed interpolated section curves, smooth surface loft, planar end caps, sewing at 0.001 mm, solid creation | Body and cap, both valid |
| N03-nurbs-duct.prism | Six rounded-rectangle-to-circle sections, offset curved loft, 1.8 mm wall, annular flange extrusion and four mounting holes | Wall and flange, both valid |

The mouse retains its hidden editable source surface. The bottle and duct retain hidden section curves. The bottle is a closed solid form study, not a hollow production bottle. The duct wall and flange are separate solids. These studies test construction and integrity, not certified surface continuity or production readiness.

## Bugs found and fixed

1. Command preselection populated both the required loft sections and optional guide rails. A section curve used accidentally as a rail caused an expensive native distance calculation and a UI hang. Preselection now seeds one compatible visible input, preferring required slots. The captured stack is in loft-hang-stack.txt.
2. MCP configuration caused repeated previews while selection lists and parameters were incomplete. Command.ConfigureInputs now defers intermediate rebuilds; run_command and command_set build after configuration.
3. Bounding properties depended on the display triangulation. A flat 2.8 mm NURBS base could report roughly 6.98 mm thickness after meshing. Native bounds now use geometric surfaces rather than display triangles.
4. OCCT reported a mouse fillet as completed despite producing an invalid body. Fillet and chamfer now reject invalid topology. The final mouse uses the valid unfilleted housing. invalid-fillet-fixture.prism preserves the earlier failing result for diagnosis; it is deliberately invalid, not a finished model.

## Validation

- Release native kernel and desktop builds passed.
- Prism.NurbsTests: 14 checks, including required-only preselection, batched preview configuration, geometric bounds independent of tessellation, actual thickening dimensions, and rejection of the reproduced invalid fillet without changing its input.
- Prism.FreeformTests: 18 checks passed.
- Python cadkit freeform tests: 4 checks passed.
- Reopened all three saved documents and checked each visible solid for validity and positive volume.
- Exported and reimported all three STEP files. All imported shapes passed validity; total volumes matched at the API's reported precision.
- Machine-readable properties and roundtrip results: validation-results.json. Incremental MCP requests/results after recovery: mcp-session.jsonl. Earlier mouse construction predates that log.

Run from the repository root:

```powershell
dotnet run --project tests/Prism.NurbsTests
dotnet run --project tests/Prism.FreeformTests
python -m unittest discover -s tests -p test_cadkit_freeform.py
```

## Remaining gaps and recommended direction

Use geometric rigor and fluid direct interaction as the design benchmarks. Direct modeling can produce rigorous CAD; the important requirements are tolerances, valid topology, inspectable continuity, stable references, and predictable edits.

Prioritize explicit G0/G1/G2 surface matching and boundary constraints, with measured gap/tangency/curvature diagnostics. Then improve face-level control-net editing inside joined geometry, exposed weights/knot controls, and preservation of editable source relationships. The present exercise demonstrates basic surface construction, not parity in those areas.

Slow native operations still run synchronously without useful progress or cancellation. Document tabs also overflow without clearly keeping the active study visible. Reopening a saved study restores its geometry, but not its modeling undo history. Those workflow limitations matter independently of adding more surface commands.

