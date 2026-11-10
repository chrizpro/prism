# Solid feature details and body edits

Rib thickens open sketch lines and joins the result to a target. Emboss joins a raised planar profile or cuts an engraving. Hole drills from a face or body with extent and thread options. Fillet and Chamfer dress selected edges. Shell offsets walls and can open selected faces. Draft tilts walls about a neutral plane. Split Body keeps the pieces as separate bodies.

## Review and verification

This guide describes observed behavior. Additional requirements are proposed and need owner review. [Tracking issue #27](https://github.com/chrizpro/prism/issues/27).

- [Maintained headless coverage](../../../Prism.Tests/Headless/Solid/BodyEditTests.cs)

The headless cases cover a raised Emboss, a joined Rib, a plain through-hole, single-edge Fillet and Chamfer, open-face Shell, single-face Draft and an equal-volume Split Body.

## Coverage gaps

Whole-body Shell subtracts the inner solid from the outer solid to keep a closed wall. The [resin hollow-and-drain case](../../../Prism.Tests/Headless/Cam/ResinPreparationTests.cs) verifies an inward wall, a blind drain through one wall, and the cavity of an outward shell. [Bug #32](https://github.com/chrizpro/prism/issues/32). Engraving, threads, multi-edge blends, open-face outside shells, multi-face drafts and split misses still need maintained coverage.
