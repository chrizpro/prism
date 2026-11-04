# Boundary Surface, Ruled Surface and Patch

Boundary Surface is on Surface and NURBS. Two free rails can span a ruled sheet. A flat closed loop can make a planar face, with nested loops becoming holes. A bent loop can make a smooth patch. Ruled Surface and Patch remain callable separately for scripts and saved workflows.

## Options and behavior

- **Fill with** offers Automatic, Flat surface and Smooth surface. The standalone Patch calls the last mode Smooth patch. Forced Flat rejects a bent boundary.
- **Inside curves** offers Follow them exactly or Fit through points on them. Fitting exposes samples, degree, density and smoothing passes. The implementation can fall back to fitting when exact following fails.
- **Opening fill** accepts an open edge or a surface with an opening. Join and joint tolerance control sewing the result back onto the source.
- **Supported edges** expose continuity and weight. Two separate surface edges use the blend path with Fullness. The implementation can report a fallback to lower continuity for a fill.

## Verification

[Maintained cases](../../../Prism.Tests/Headless/Surfacing/BoundarySurfaceTests.cs) exercise both ribbon entries, standalone Ruled and Patch commands, nested loops, bent-loop fill choices, guide modes, and joined and separate opening fill. [Issue #50](https://github.com/chrizpro/prism/issues/50).

Run `./tools/test.ps1 -Layer Headless -Feature BoundarySurface`. These geometry checks use the native kernel. [Legacy command checks](../../../tests/Prism.DirectModelTests/ToolMergeTests.cs) also cover a blend example.

## Coverage gaps

Individual fitting controls, continuity and weight changes, blend fullness, opening edge picks, nonplanar joins, and additional rail and loop configurations need separate cases. New requirements remain subject to owner review.

Standalone Patch currently rejects one bent spline that Boundary Surface fills. The maintained cases preserve the reproduction and verify the rejected operation leaves the document unchanged. [Issue #51](https://github.com/chrizpro/prism/issues/51).

Local Windows verification with the installed native kernel passed all 26 Boundary Surface, Ruled and Patch cases.
