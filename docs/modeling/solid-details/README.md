# Solid feature details and body edits

Rib fills the web between a straight sketch line and the body it meets (a gusset in a corner, `SOL-H109`), with thickness across the sketch plane; Reverse switches to the other side of the line, and "Fixed height" grows the lines by a set height instead (`SOL-H036`). Emboss joins a raised planar profile or cuts an engraving. Hole drills from a face or body with extent and thread options; the circle drawn for the hole can be dragged in the viewport to set its diameter (`HDL-H033`, owner report 2026-11-13). Fillet and Chamfer dress selected edges. Shell offsets walls and can open selected faces. Draft tilts walls about a neutral plane. Split Body keeps the pieces as separate bodies.

## Review and verification

This guide describes observed behavior. Additional requirements are proposed and need owner review. [Tracking issue #27](https://github.com/chrizpro/prism/issues/27).

- [Maintained headless coverage](../../../Prism.Tests/Headless/Solid/BodyEditTests.cs)

The headless cases cover a raised Emboss, a joined Rib, a plain through-hole, single-edge Fillet and Chamfer, open-face Shell, single-face Draft and an equal-volume Split Body.

## Design history saved by earlier builds (2026-11-13)

A saved design-history step lists its command's numbers by position, so a command that gains a parameter in the middle of its list would read an older file wrongly. Three changes did exactly that, found in a branch review before the 0.3.0 alpha: **Move with gizmo** on the primitives and Insert Component, **Rib extends** on Rib, and **Scale type** with the X, Y and Z factors on Scale. Each command now upgrades an older step on load: a primitive keeps its Join, Cut or Intersect operation, an older Rib keeps **Fixed height** (its only behavior then), and an older Scale stays a uniform scale with its factor, pivot and copy choice (`SOL-H112` to `SOL-H114`, [saved-history upgrade checks](../../../Prism.Tests/Headless/Solid/SavedHistoryUpgradeTests.cs)). A command that adds a parameter later should append it at the end of its list, or override `UpgradeRecipe` with `InsertValue`. Other commands changed in this branch the same way (Joint placement, Motion study mode, Mesh Repair scope, Draw on Surface result, Section) have not been checked against older files.

## Coverage gaps

Whole-body Shell subtracts the inner solid from the outer solid to keep a closed wall. The [resin hollow-and-drain case](../../../Prism.Tests/Headless/Cam/ResinPreparationTests.cs) verifies an inward wall, a blind drain through one wall, and the cavity of an outward shell. [Bug #32](https://github.com/chrizpro/prism/issues/32). Engraving, threads, multi-edge blends, open-face outside shells, multi-face drafts and split misses still need maintained coverage.
