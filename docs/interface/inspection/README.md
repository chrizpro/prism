# Inspect and Appearance

Measure reports geometry dimensions and distances. Properties reports volume, area, mass and related quantities. Section Analysis clips the view and reports the area and outline of the cut. Materials opens Appearance and assigns a material to the selected supported objects.

## Verification

[Inspection checks](../../../Prism.Tests/Headless/Solid/SharedToolTests.cs), [Appearance entry](../../../Prism.Tests/Headless/Solid/SolidRibbonTests.cs) and [material assignment and undo](../../../Prism.Tests/Headless/Solid/AppearanceTests.cs). [Tracking issue #37](https://github.com/chrizpro/prism/issues/37).

## Coverage gaps

Desktop clipping, multi-body measurement and displayed material finishes need more coverage. New requirements need owner review.

## Appearance and Materials proposal

**Proposed — awaiting owner review.** Give visual Appearance and engineering Materials distinct workflows. [Issue #45](https://github.com/chrizpro/prism/issues/45).

- **Materials** assigns engineering properties such as density, stiffness and thermal conductivity. Each material supplies a representative default look.
- **Appearance** controls the visual finish on a body with or without an assigned material. Provide a color wheel and keep exact hex entry. Decorative finishes such as upholstery can give a couch the desired fabric look. Changing its appearance preserves engineering properties and analysis inputs.
- **Default and override** uses the material's default look until a visual override is applied. A separate reset restores the default. Review whether changing the material should preserve an existing override and which decorative finish controls should ship first.

The current Appearance panel accepts swatches and hex input. After review, verify color selection, material property preservation, Undo, and save/reopen in the maintained suites linked above.
