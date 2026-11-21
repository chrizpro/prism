# Boundary Surface, Ruled Surface and Patch

Boundary Surface is on Surface and NURBS. Two free rails can span a ruled sheet. A flat closed loop can make a planar face, with nested loops becoming holes. A bent loop can make a smooth patch. Ruled Surface and Patch remain callable separately for scripts and saved workflows.

## Options and behavior

- **Fill with** offers Automatic, Flat surface and Smooth surface. The standalone Patch calls the last mode Smooth patch. Forced Flat rejects a bent boundary.
- **Inside curves** offers Follow them exactly or Fit through points on them. Fitting exposes samples, degree, density and smoothing passes. The implementation can fall back to fitting when exact following fails.
- **Opening fill** accepts an open edge or a surface with an opening. Join and joint tolerance control sewing the result back onto the source.
- **Mode names each intent's picks (owner request 2026-11-16, [BND-H030, BND-H031](../../../Prism.Tests/Headless/Surfacing/BoundaryModeButtonTests.cs); tests only, the panel look is not tried by hand).** The Mode choice (commit `db9d42d`) is a compact **dropdown** like Fill with (a first version made it five large buttons taking three rows of the panel; the owner asked for the dropdown back to save room): **Automatic** (the default, guesses from what you pick), **Fill boundary**, **Edge: span two to four curves**, **Blend: bridge two surface edges** and **Network: outline and curves across**. Each intent names its own picks in the panel (for example Network asks for "Outline curves (a closed loop)" and "Curves across the surface") and the inside-curves pick is hidden in Edge and Blend, where it does not apply. Scripts and saved steps still find the picks by their old names ("Boundary", "Guide curves"). **Limits:** the other options (join, continuity, fit settings) still show or hide as before rather than being regrouped per mode; Patch and Ruled Surface stay separate commands.
- **A flat opening is capped flat in Automatic (owner report 2026-11-16, fixed; tests [BND-H020, BND-H021](../../../Prism.Tests/Headless/Surfacing/BoundaryFlatOpeningTests.cs), tried in the app before the fix only).** An open tube made by Extrude Surface and patched with Boundary Surface used to get balloon-shaped caps (a 60 x 40 mm opening grew to 33,000 mm² and 256 mm tall) because every surface edge defaults to tangent continuity (G1) and a plate fill cannot be tangent to walls that meet the opening at 90°. Knitting those caps gave a solid, but Shell failed with "Weights values too small". Now, when every edge of an opening lies in one plane, **Automatic** fills it with a flat face, as its name says, and the continuity settings apply only when **Smooth surface** is chosen. Result: the knitted tube is a valid solid that shells (60 x 40 x 20 mm tube, 2 mm walls: 15,744 mm³ left). **Not yet tried by hand in the running app after the fix.**
- **Supported edges** expose continuity and weight. Two separate surface edges use the blend path with Fullness. The implementation can report a fallback to lower continuity for a fill.

## Verification

[Maintained cases](../../../Prism.Tests/Headless/Surfacing/BoundarySurfaceTests.cs) exercise both ribbon entries, standalone Ruled and Patch commands, nested loops, bent-loop fill choices, guide modes, and joined and separate opening fill. [Issue #50](https://github.com/chrizpro/prism/issues/50).

Run `./tools/test.ps1 -Layer Headless -Feature BoundarySurface`. These geometry checks use the native kernel. [Legacy command checks](../../../tests/Prism.DirectModelTests/ToolMergeTests.cs) also cover a blend example.

## Coverage gaps

Individual fitting controls, continuity and weight changes, blend fullness, opening edge picks, nonplanar joins, and additional rail and loop configurations need separate cases. New requirements remain subject to owner review.

Standalone Patch currently rejects one bent spline that Boundary Surface fills. The maintained cases preserve the reproduction and verify the rejected operation leaves the document unchanged. [Issue #51](https://github.com/chrizpro/prism/issues/51).

Local Windows verification with the installed native kernel passed all 26 Boundary Surface, Ruled and Patch cases.


## 0.4 illustrated documentation

**Delivered on v0.4-work:** Mode offers Automatic, Fill boundary, Edge (two to four curves), Blend (two surface edges) and Network (outline plus interior curves). Explicit modes validate inputs instead of silently falling back. Fill with remains a separate control. [Maintained mode checks](../../../Prism.Tests/Headless/Surfacing/BoundaryModeTests.cs).

The [0.4 visual guide](../../../site/guide/modeling-04.md) explains the delivered subset on `v0.4-work` with labeled concept diagrams. It separates development features from released installers and names unfinished options. Diagrams explain geometry; they are not viewport verification. See the [approved scope and delivery record](../surface-and-nurbs/README.md#0-4-modeling-gaps-approved-by-the-owner-2026-11-14) for remaining decisions.
