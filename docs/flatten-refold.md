# Flatten a face, draw, then Refold

**Flatten** already supports individual faces of a solid or surface. On **Surface → Modify → Flatten**, click a curved face rather than choosing the whole object in the Browser. The selection field identifies the face, for example **Face 1 of Cylinder 1**. The original body and all its other faces stay intact.

An untrimmed cylindrical face now uses an analytic layout: horizontal distance is circumferential arc length, vertical distance is axial height. A radius 25 mm, height 70 mm cylinder produces a 157.079633 × 70 mm flat pattern. The pattern retains its cylinder frame, parameter bounds, source identity and layout position for returning drawings to the source.

1. Flatten the cylindrical face. Use **Flat surface and outline** or **Flat surface only** if you want a face to sketch on.
2. Start a sketch on the new flat surface and draw within its outline. Finish the sketch.
3. Choose **Surface → Modify → Refold**. Select the original flat pattern and the sketches/curves drawn on it.
4. Inspect the live preview and choose **Refold onto source face**.

Refold creates B-rep curves on the original cylindrical support. It preserves line/spline/conic definitions in surface parameter space and builds corresponding 3D curves to the chosen tolerance. These are curve geometry that can be used in subsequent modeling. Drawing a closed profile does not itself cut a hole in the body.

**Refold the flat face (including cuts)** is enabled by default. It rebuilds a cylindrical face from the actual flat pattern, preserving interior cutout wires rather than copying the uncut source face. No sketch selection is required to return a cut face. You can optionally select drawings to wrap as additional curves. Original bodies, flat patterns and drawings are retained. Creation is one undo step.

Refold currently applies to cylindrical layouts made by Flatten with the original rectangular outer boundary. You can cut interior holes, windows and slots into that flat face and refold them. Edge notches, disconnected pieces and seam-crossing cuts are rejected explicitly. Other surfaces continue to use the existing flattening solver and stretch reports. Irregular cylindrical source trims/holes are not replaced by a bounding rectangle when flattening. Moved/resized layouts and drawings outside the original pattern are rejected rather than silently wrapped in the wrong place.

The cylindrical return map survives recoloring, visibility changes, history and save/open. Documents with return maps use file version 4; older versions remain readable.

## MCP proof

The review was driven through **Prism.Mcp** using JSON-RPC `initialize` and `tools/call`, including `run_command`, `sketch_begin` on the flat face, `sketch_add`, `sketch_finish`, `refold`, and native `screenshot` calls. It used a separate application instance; the user's existing discovery endpoint was preserved. Set `PRISM_AUTOMATION_DISCOVERY` to a separate absolute discovery-file path in both the bridge and application to isolate such a session.

The drawing contains a 40-edge gear outline, a central circle, six smaller circles and two splines: 49 edges total. The original cylinder's volume before and after was 137444.6786 mm³. The refolded curve is valid and preserves all 49 edges.

- [MCP transcript](../artifacts/flatten-refold-review/mcp-session.jsonl)
- [Proof summary](../artifacts/flatten-refold-review/mcp-proof-summary.json)
- [Selected face and flat preview](../artifacts/flatten-refold-review/01-selected-face-flatten.png)
- [Drawing on the flat pattern](../artifacts/flatten-refold-review/02-complex-flat-sketch.png)
- [Refold preview](../artifacts/flatten-refold-review/03-refold-preview.png)
- [Refolded cylinder](../artifacts/flatten-refold-review/04-refolded-cylinder.png)
- [Returned cylindrical face and drawing](../artifacts/flatten-refold-review/05-refolded-face.png)
- [Flat document](../artifacts/flatten-refold-review/flat-pattern-with-sketch.prism)
- [Refolded document](../artifacts/flatten-refold-review/refolded-cylinder.prism)

`dotnet run --project tests/Prism.RefoldTests -c Release` checks exact layout dimensions, face-only behavior, rotated cylinders, geometric return error, complex profiles, save/open, undo/redo and invalid input. Measured radial error is below 0.00005 mm.

## Actual cutout regression

The subsequent MCP test reopened the flat model and subtracted real cutter solids to create 11 openings: a gear-shaped window, six circular holes and four capsule slots. This exposed the old Refold behavior: it silently restored the uncut source face. Refold now maps the inner trim wires onto the cylinder and reconstructs the trimmed surface. Surface-area measurement uses adaptive integration so rational cylindrical trims are measured accurately.

The cut pattern and refolded face both measure **10050.371983 mm²**, compared with **10995.574288 mm²** before cutting. Both have 11 interior cutouts; the returned face is valid. Tests also confirm that the center of the gear window has no face, and that the trim loops survive undo/redo and save/open.

- [MCP cut/refold proof](../artifacts/flatten-cut-refold-review/proof-summary.json)
- [Actual flat cutouts](../artifacts/flatten-cut-refold-review/02-real-cutouts-flat.png)
- [Cut face refolded](../artifacts/flatten-cut-refold-review/04-cutouts-refolded.png)
- [Pattern around the cylinder](../artifacts/flatten-cut-refold-review/05-cutouts-around-cylinder.png)
- [Perforated model](../artifacts/flatten-cut-refold-review/perforated-refolded-cylinder.prism)

Run `dotnet run --project tests/Prism.RefoldCutTests -c Release` for the cut-face regression.
