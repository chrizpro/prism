# Sheet Metal

- **Select, Datum:** shared sections
- **References:** Create Sketch, Construction Plane
- **Sheet metal:** Flange, Flat Pattern
- **Cut-outs:** Extrude, Hole, Fillet / Chamfer (dropdown)
- **Transform, Inspect, Appearance:** shared sections

## Observed limitations (2026-11-08)

Findings from building the [Fold gallery study](../../interface/gallery/README.md#curated-collection), recorded as observed behavior, not as intended requirements:

- **Flat Pattern** unfolded a Flange with a hem, round holes (in flat and tilted walls), rectangular windows and chamfered corners. It rejected the same part with a slot or a rounded (filleted) corner, reporting "the bends are not all parallel" ([#78](https://github.com/chrizpro/prism/issues/78)).
- **Cut-outs from a face sketch:** Extrude Cut can point away from the metal and then removes nothing without an error, as seen on a tilted wall. Reversing the direction cut the expected volume ([#79](https://github.com/chrizpro/prism/issues/79)).
- A Flange makes every bend from one profile sketch, and Flat Pattern needs the bends to be parallel.

## Open decisions

**Proposed, awaiting owner review:** whether and which further sheet-metal tools to add (edge flange, flat plate to fold, a separate hem, an unfoldable corner treatment, unfold/refold of one bend), and whether slots and rounded corners should unfold. Tracked in [#81](https://github.com/chrizpro/prism/issues/81). Nothing in this section is approved behavior.

## Verification

[Sheet metal tests](../../../Prism.Tests/Headless/SheetMetal/SheetMetalTests.cs) check Flange volume and bounds for both metal sides, width direction (forward, reversed, centred), hem, a two-bend channel and rejected profiles; Flat Pattern size and bend allowance for three K-factors, placement and the no-bend error; and Flatten / Refold on the documented radius 25 × 70 mm cylinder for each Create choice. Run `./tools/test.ps1 -Feature SheetMetal`. [Flatten and Refold](../../flatten-refold.md) also has its standalone suite.

Windows verification on 2026-11-05: the combined candidate passed 86 Unit and 592 Headless tests with rebuilt native libraries. See the [integration verification report](../../engineering/release-readiness-2026-11-05.md) for source revisions, commands and limits.

The maintained Integration check [GAL-I006](../../../Prism.Tests/Integration/DesktopGalleryTests.cs) opens the Fold gallery model and verifies its through-holes and its flat pattern; it needs the configured Windows workstation.
