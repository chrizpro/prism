# Sketch entry and reference points

Create Sketch starts on XY, XZ, YZ, a construction plane, or a planar face. Offset moves the base plane along its normal. A picked planar face supplies the actual drawing plane. Datum Point activates the reference-point tool while a sketch is open and places a point on its active plane.

## Verification

[Sketch entry and point tests](../../../Prism.Tests/Headless/Solid/SketchAndDatumTests.cs) and [offset sketch entry](../../../Prism.Tests/Headless/Solid/SharedToolTests.cs). [Tracking issue #33](https://github.com/chrizpro/prism/issues/33).

## Coverage gaps

Sketch tool families within the contextual Sketch tab have separate workflows. Desktop snapping and visual feedback remain open. New requirements need owner review.
