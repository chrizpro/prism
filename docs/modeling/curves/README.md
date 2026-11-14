# Curve creation and editing

Curve uses picked points as interpolated points or control points. Curve from Data accepts formulas, typed rows, files or a Data sheet, with smooth, control-point and straight-segment outputs. Intersection Curve finds the section between shapes, including a box cut by a plane, and offers a projected flat-curve mode.

## Curve tool cleanup

Approved by the owner on 2026-11-08; implemented on `cleanup/curve-tools`, branched from `develop`. [Delivery issue #72](https://github.com/chrizpro/prism/issues/72).

Since 2026-11-13 (owner direction) Home, Solid, Surface, NURBS and Freeform each show one **Curves** dropdown that holds every curve creation and editing tool; the separate Edit curves group is gone. See the [Curves dropdown](../surface-and-nurbs/README.md#curves-dropdown).

> **Proposed, awaiting owner review (2026-11-14):** Curve, Blend Curve, Join, Explode, Trim Curve, Extend Curve and Offset Curve are retired from the Curves dropdown, search and ribbon customization. Their commands are marked `[Legacy]` in code, so they are hidden from the GUI but still load from saved files and run from scripts; search for `[Legacy` to list them when deciding whether to delete them. Sketch's own Trim, Extend and Offset tools are unchanged. The descriptions of those tools below describe the retired behavior.

- **Curve dropdown:** Curve and Curve from Data. Interactive Curve still offers through-points and control-points modes.
- **Join dropdown:** Join, Explode and Extract Edges (formerly Duplicate Edge). Join assembles touching segments without bridging gaps. Explode separates existing segments. Extract Edges copies selected body/surface edges into independent curves, joining touching edges and keeping the source body. Join and extraction share the same joining operation.
- **Trim Curve:** Keep one part using a percentage range, or choose **Cut into pieces** and enter split percentages. There is no separate Split ribbon entry. Trim and Extend currently require a single segment; use Join > Explode for a multi-segment curve first.
- **Fillet / Chamfer:** one command with a Corners choice, as before. Since 2026-11-12 it also accepts edges where two faces of a surface or body meet and rounds or cuts them on the body ([Surface and NURBS guide](../surface-and-nurbs/README.md), SFC-H016).
- **More Curve Tools dropdown:** Rebuild Curve and **Points Along Curve** (formerly Divide). Rebuild resamples a curve into a smooth spline and reports approximate deviation; it can change the shape. Points Along Curve creates equally spaced points and reports length/spacing without cutting or changing the source curve. The Segments setting produces segment-count plus one points on an open curve.
- **Extract Curves dropdown:** Isocurves and Contour, on Home, Surface and NURBS. These derive surface U/V lines and repeated planar sections respectively.
- **Intersection Curve and Blend Curve remain distinct:** intersection derives a path from crossing shapes or two projected planar curves; blend creates a new bridge between free endpoints with G0/G1/G2 continuity. Project remains a separate surface operation. Curvature Graph stays under Inspect on Surface and NURBS.

Menus stay one level deep so every leaf remains available to command search and custom shortcuts. More Curve Tools and Extract Curves have fixed names; Curve and Join use the app's existing split-button behavior, showing the last chosen action. Click their label or right-click to choose another action.

Curve tools share the primary icon color of the visible modeling ribbon. See the [modeling ribbon palette](../../tabs/README.md#modeling-ribbon-palette).

### Existing documents and customization

Old Split history and the `curve_split` scripting name use a compatibility adapter that delegates cutting to Trim, retaining the old parameter schema. Old Divide Curve and Duplicate Edge saved identities resolve to the renamed commands. Existing scripting names (`curve_divide`, `curve_duplicate_edge`) remain valid. Old configurable button IDs and shortcut targets are retained, as are former Curves/Edit curves dropdown IDs in customized layouts. Reset a tab in Settings to adopt the shipped grouping.

## Curve from a table

- **Checked and improved 2026-11-14 (owner report).** *Curve from Data* in its table mode read typed text or a file, and a sheet of the Data panel only through the panel's *Make curve* button or by typing `@` and the sheet's name. It now lists the document's sheets in **Use a table from the Data panel**, with **Open the Data panel** and **New table for this curve** (which adds a sheet, points the curve at it and opens the panel to type or paste into). A curve step saved before the choice existed reopens with its values in place. Tests: [CRV-H050 to CRV-H052](../../../Prism.Tests/Headless/Curves/CurveFromTableTests.cs).

## Verification

[Curve command checks](../../../Prism.Tests/Headless/Solid/SharedToolTests.cs) and [expression unit checks](../../../Prism.Tests/Unit/Solid/CurveExpressionTests.cs). [Tracking issue #36](https://github.com/chrizpro/prism/issues/36).

[Curve editing tests](../../../Prism.Tests/Headless/Curves/CurveToolTests.cs) characterize every option of Fillet / Chamfer Curve, Offset Curve, Blend Curve, Join and Explode, Trim and its legacy Split adapter, Extend, Points Along Curve, Rebuild, Project Curve, Intersect Surfaces, Extract Edges and the Curvature Graph, plus Contour in the [surface tool tests](../../../Prism.Tests/Headless/Surfacing/SurfaceToolTests.cs). Run `./tools/test.ps1 -Feature Curves`.

[Curve ribbon checks](../../../Prism.Tests/Headless/Curves/CurveRibbonTests.cs) cover menu reachability, saved command aliases, legacy shortcuts/button IDs and customized menus.

### Cleanup verification — 2026-11-08

- Release build of `Prism.Tests/Headless/Prism.HeadlessTests.csproj` succeeded with .NET SDK 10.0.403 on Linux (existing unrelated compiler/analyzer warnings).
- Seven UI-only checks passed by directly calling the maintained test bodies from a temporary Avalonia Headless console harness: CRV-H034 for Home/Surface/NURBS, CRV-H035, CRV-H036, HLP-H001 and HLP-H003. The harness configured `AppBuilder.Configure<Application>().UseHeadless(...).SetupWithoutStarting()` and an isolated `PRISM_SETTINGS_FILE`, then called the test methods. This verifies menu reachability, old identities/shortcuts/custom menus and help resolution; it is not a normal xUnit suite run.
- The generated ribbon catalog passed `dotnet run --project tools/Prism.DocsCatalog -c Release -- . --check` and `node site/scripts/check-catalog.mjs`. `npm ci --ignore-scripts && npm run build` in `site/` also passed.
- The normal `dotnet test` Headless invocation aborted at suite startup because `PRISM_NATIVE_DIR`/`libprism_kernel.so` is unavailable. Native curve geometry, Split-adapter output and desktop viewport verification were not run here. Run `./tools/test.ps1 -Layer Headless -Feature Curves` on the configured native workstation before merging.

## Coverage gaps

Closed and control-point curves, import variants, projected intersections and viewport picking need more coverage. New requirements need owner review.

Windows verification on 2026-11-05: the combined candidate passed 86 Unit and 592 Headless tests with rebuilt native libraries. See the [integration verification report](../../engineering/release-readiness-2026-11-05.md) for source revisions, commands and limits.


## 0.4 illustrated documentation

**Delivered on v0.4-work:** Curve from Data adds Points from → Helix with radius, end radius (0 keeps the start radius), pitch, turns, handedness, X/Y/Z axis and position. It creates a curve for a later Sweep, not a thread feature. [Helix checks](../../../Prism.Tests/Headless/Curves/HelixTests.cs). Intersection Curve defaults to replacing two flat source curves unless Keep the original curves is on; reference planes, faces and bodies remain. Old saved steps retain curves. [Retention checks](../../../Prism.Tests/Headless/Curves/KeepOriginalsTests.cs).

The [0.4 visual guide](../../../site/guide/modeling-04.md) explains the delivered subset on `v0.4-work` with labeled concept diagrams. It separates development features from released installers and names unfinished options. Diagrams explain geometry; they are not viewport verification. See the [approved scope and delivery record](../surface-and-nurbs/README.md#0-4-modeling-gaps-approved-by-the-owner-2026-11-14) for remaining decisions.
