# Gallery and modeling handoff — 2026-11-16

Continue on **`codex/v0.4-illustrated-docs`**, as explicitly requested by the owner. Preserve unrelated work. Do not merge to `develop` or `main` as part of this handoff. Read [requirements and testing](../requirements-and-testing.md), the repository README and the affected canonical feature guide before code changes.

## Completed work

- `fe4cdc2`: Serein S1 phone assembly and recorded CAM bug reproduction.
- `de9cd76`: owner-requested major Freeform and advanced fillet/chamfer roadmap directions.
- `c4b210e`: seven actual Prism studio renders, catalog entries, observatory assembly and proposed router work breakdown.
- The earlier commits on this branch contain the wooden studies, revised guitar, helicopter revisions, toaster and soap mold/CAM. No further gallery models are requested.

The catalog has 36 entries. Seven newly prepared cards are Maple observatory, Art Deco mantel clock, Maple/walnut guitar body, Horizon H4, Linen T2, Serein S1 and Arc soap mold/CAM. The observatory now has 116 native components and one grounded plinth. Its component volumes, bounds and face counts match the earlier verified design. Two experimental Freeform studies remain outside the gallery, under local `artifacts/freeform-study/` and `artifacts/freeform-mouse/`.

## Bugs worth investigating

1. **[CAM stock-top comparison #138](https://github.com/chrizpro/prism/issues/138)** — confirmed through actual Windows MCP. A rotated mold half has stock top `26.00000000000001` mm; roughing top `26` is rejected while `26.01` succeeds. `Rough3DCommand.Build` compares the top without a tolerance. Preserve rejection of materially lower tops when fixing it, and add a focused maintained regression. [Actual rejected/accepted preview evidence](../../interface/gallery/evidence/modeling-bug-findings.json), [CAM guide](../../cam/README.md#known-roughing-stock-top-comparison-issue).
2. **[Intersect volume anomaly #134](https://github.com/chrizpro/prism/issues/134)** — the earlier host reported an extruded airfoil increasing from 43693.2016 to 47316.4706 mm³ after Intersect. Geometry, integration and measurement reuse remain possible causes; no root cause or fix is established. Reproduce on a freshly built current host/native kernel before drawing conclusions. The final helicopter avoids that construction. [Combine guide](../../modeling/combine/README.md#review-and-verification).

Earlier blank viewport captures were intermittent and were not reproduced during the final phone checks. Current diagnostic evidence records a valid 904 × 592 viewport capture. Poor Freeform study results are not evidence of a confirmed Freeform product bug.

## Proposed work, not implemented requirements

- [#139](https://github.com/chrizpro/prism/issues/139): substantial Freeform and advanced fillet/chamfer improvements. Current fillets already support variable/per-edge radii; chamfers support equal distance, two distances and distance/angle. No explicit setback-corner option was found. Detailed new behavior still requires owner review.
- [#141](https://github.com/chrizpro/prism/issues/141): assembly-linked multi-part router jobs. Current named CAM setups each target one body; shared-stock multi-body review and one program for a bed load need further design. Owner chose a **600 × 400 mm** demo bed. [116-part inventory](../../../gallery/extras/maple-observatory-part-plan.csv), [manufacturing work breakdown](../../../gallery/extras/maple-observatory-router-plan.md). The building still needs fabrication decisions for deep halls, tiny applied details, columns, fixtures and flips. No new observatory toolpaths or G-code were generated.

## Actual verification and environment

- `dotnet test Prism.Tests/Unit/Prism.UnitTests.csproj -c Release --filter Feature=Gallery`: **8 passed**, no failures/skips.
- `dotnet test Prism.Tests/Headless/Prism.HeadlessTests.csproj -c Release --filter Feature=Gallery`: final **11 passed**, no failures/skips.
- `python gallery/update_catalog.py --check`: all 36 catalog entries' model, preview and extra hashes match.
- GAL-H007 initially failed because it assumed the existing injection-mold card was in CAM's first three previews. The maintained test now checks section membership and visibility after Show all. Mold examples retain the previously approved CAM categorization. No gallery product behavior changed.
- Native modeling/render evidence used the existing Windows desktop built from `c9a5773`, with native libraries reused. This is distinct from the current-source managed Gallery test builds. No fresh full desktop integration suite or physical machining was performed.
- Local test logs/TRX: `artifacts/gallery-release/`; committed [render/model evidence](../../interface/gallery/evidence/rendered-gallery-verification.json). [Canonical gallery guide](../../interface/gallery/README.md#november-16-rendered-gallery-batch).

## Publication

Changes are committed locally, not pushed or merged. Catalog URLs target `develop`; new hosted downloads remain unavailable until the catalog and assets are published there. Keep publication separate from bug fixes and the owner's requested working branch.
