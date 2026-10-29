# Surfacing and compact ribbon: first implementation

Implemented and exercised October 27, 2026.

## Modeling tools

**Match Surface**, under Surface and NURBS, changes one standalone patch to meet a fixed reference patch or face. Choose each natural boundary (U min/max or V min/max), reverse the along-seam direction when needed, then choose position (G0), tangency (G1) or curvature (G2). Tangent scale controls the departure from the reference. G0 changes one control row, G1 two, and G2 three; the source's opposite boundary is retained. The reference remains unchanged. Preview, accept, undo and redo use the normal command lifecycle and preserve object identity and appearance.

The first implementation supports untrimmed rectangular, non-rational, non-periodic B-spline patches with compatible along-boundary control counts, degree, normalized knots and multiplicities. Cross-boundary degree and control counts may differ if adequate. Trimmed/weighted/periodic or incompatible patches get an explanation rather than an approximate success. Extract and untrim a face when necessary. Matching enforces boundary derivative constraints; it does not certify that the resulting entire patch is free of folds or self-intersections.

**Check Seam / Surface Continuity** measures corresponding natural boundaries: maximum and RMS sampled position gap, maximum sampled normal angle, and maximum sampled cross-boundary normal-curvature difference. These are numerical diagnostics, not a global G2 certificate. The saved `G2-matched-patches.prism` and STEP example contain two matched curved patches. The live MCP check reported zero sampled gap, 1.20742e-6 degrees normal-angle difference and 3.46945e-18 /mm curvature difference.

**Check Cage**, under Freeform, reports invalid/degenerate faces, non-finite and isolated points, open borders, non-manifold edges and bow-tie vertices, winding conflicts, connected components and extraordinary interior points. Problem edges are highlighted red. Open manifold cages are allowed for surfaces; extraordinary points are informational. Conversion to NURBS now rejects cages with failed integrity checks before entering the conversion algorithm. The checker does not test geometric self-intersection. `form_info` exposes the additional diagnostics to MCP.

## Ribbon and manufacturing

- Shared curve, plane and sketch helpers are grouped under References; other related operations use named menus. Direct access remains for Create Sketch, Edit Freeform, NURBS Patch, Match Surface and Check Seam.
- Existing primitive and freeform starting-shape split buttons retain their last-used behavior. Named tool menus retain their group names. Opening a split menu no longer executes its current tool first.
- Ribbon headers can scroll horizontally. Tool rows have visible previous/next arrows when they overflow, plus mouse-wheel scrolling. 1024-pixel screenshots are included; dense NURBS/Freeform ribbons still overflow, and all final tools are reachable through the arrows.
- Manufacture now contains one **CAM** tab instead of separate CNC and 3D Print tabs. CNC includes Profile, Pocket, Drill, Face and Post G-code. Print preparation includes geometry/section/measurement checks, 3MF/STL export for external slicers, and the existing basic FDM slicer. Review retains Layer View.
- Mold preparation currently exposes draft, section and geometry analysis. This is inspection support, not a core/cavity or parting-surface generator. Scan & Print is renamed Scan to separate reverse engineering from manufacturing.

The architectural direction is a shared manufacturing workspace with focused process groups, rather than building a competing general-purpose slicer. CNC posts and basic slicing remain available. More valuable future CAD-side additions include manufacturability checks, mold parting/core/cavity tools, stock/work-coordinate setup and reliable toolpath review.

## Validation

- Native Release kernel and desktop Release builds passed.
- `Prism.SurfaceTests`: 305 checks. G0/G1/G2 over all 16 source/reference side combinations, both along-seam directions, fixed reference/source inputs, compatible refinement and degree elevation, incompatible knot rejection, cage failure fixtures, command preview/commit/undo/redo, native save/reopen and STEP roundtrip.
- `Prism.RibbonTests`: 26 headless UI checks. CAM command availability and stable slicing identity, nested command identities, 1024-pixel layouts, overflow arrow reachability, and split-menu opening without execution. Screenshots: `ribbon-*-1024.png`.
- Previous NURBS regression: 14 checks; freeform regression: 18 checks; Python freeform wrappers: 4 tests. Total: 367 checks/tests.
- Live MCP tested Match Surface and Surface Continuity on the saved patches, Check Cage on the detergent, and the consolidated CAM workspace. All eleven form bodies across the four previous study documents passed the new cage checks; see `existing-cage-validation.json`.

Run from the repository root:

```powershell
dotnet run --project tests/Prism.SurfaceTests
dotnet run --project tests/Prism.RibbonTests
dotnet run --project tests/Prism.NurbsTests
dotnet run --project tests/Prism.FreeformTests
python -m unittest discover -s tests -p test_cadkit_freeform.py
```

## Next surfacing work

This is a first surfacing milestone. Next priorities: automatic boundary compatibility and general trimmed/rational matching; surface blending with explicit continuity; fairer loft behavior; direct editing of faces inside joined bodies while maintaining neighbors; conversion deviation measurements near extraordinary vertices and creases; and progress/cancellation for long native operations. General self-intersection detection and persistent source relationships remain outstanding.

