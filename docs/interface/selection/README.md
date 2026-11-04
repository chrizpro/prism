# Selection controls and region picking

Object, Face and Edge choose the pick level. Vertex requires a selected Freeform cage or active cage editing. Rectangle dragged left to right requires containment, and the reverse drag accepts crossing geometry. Lasso uses the drawn polygon. Through includes occluded objects in region selection. Invert complements the current selection at its pick level.

## Verification

[Selection actions](../../../Prism.Tests/Headless/Solid/SolidRibbonTests.cs), [region and vertex checks](../../../Prism.Tests/Headless/Solid/SelectionTests.cs) and [region math](../../../Prism.Tests/Unit/Solid/SelectionRegionTests.cs). [Tracking issue #35](https://github.com/chrizpro/prism/issues/35).

## Coverage gaps

Desktop drag gestures, complex occlusion and face/edge visibility tolerances remain open. New requirements need owner review.
