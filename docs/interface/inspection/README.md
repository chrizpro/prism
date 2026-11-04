# Inspect and Appearance

Measure reports geometry dimensions and distances. Properties reports volume, area, mass and related quantities. Section Analysis clips the view and reports the area and outline of the cut. Materials opens Appearance and assigns a material to the selected supported objects.

## Verification

[Inspection checks](../../../Prism.Tests/Headless/Solid/SharedToolTests.cs), [Appearance entry](../../../Prism.Tests/Headless/Solid/SolidRibbonTests.cs) and [material assignment and undo](../../../Prism.Tests/Headless/Solid/AppearanceTests.cs). [Tracking issue #37](https://github.com/chrizpro/prism/issues/37).

## Coverage gaps

Desktop clipping, multi-body measurement and displayed material finishes need more coverage. New requirements need owner review.
