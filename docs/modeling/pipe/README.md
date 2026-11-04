# Pipe

Pipe follows a selected path with a round section. Use the start and end radii to make a constant section or taper it.

| Ribbon | Result and controls |
| --- | --- |
| Surface and NURBS | The same exact command offers a capped solid or an open surface. Both radii are editable. |
| Freeform | Builds a subdivision cage. The two radii, sides, steps along the path, smoothness, and end caps are editable. For a closed path the implementation welds the seam instead of adding end caps. |

## Verification

[Maintained Pipe tests](../../../Prism.Tests/Headless/Surfacing/PipeTests.cs) open Pipe from the shipped ribbons. The exact cases compare straight results with analytic cylinder and frustum dimensions, volume, or lateral area. Freeform cases check end radii, sampling, closure and smoothness. Missing-path cases check that building produces no output or history change.

Run `./tools/test.ps1 -Layer Headless -Feature Pipe`. [Issue #49](https://github.com/chrizpro/prism/issues/49) tracks verification.

## Coverage gaps

Curved and closed paths, edge and sketch picking, steep taper and invalid-input combinations need further checks. New requirements remain subject to owner review.

Local Windows verification with the installed native kernel passed all 14 Pipe headless cases.
