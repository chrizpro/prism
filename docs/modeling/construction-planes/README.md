# Construction planes and sketch datums

Construction Plane creates exact reference frames from offsets, angles, midplanes, normal-to-curve locations, three points, tangent faces, two edges or a planar curve. Supported modes can generate a stack from a count or typed values. Datum opens the gizmo settings. Inside a sketch, Axis saves the selected axis, Plane saves the drawing plane with its optional normal offset, and Save gizmo stores the active frame.

## Verification

[Datum actions and three-point plane](../../../Prism.Tests/Headless/Solid/SketchAndDatumTests.cs), [offset plane](../../../Prism.Tests/Headless/Solid/SharedToolTests.cs) and [Datum Gizmo entry](../../../Prism.Tests/Headless/Solid/SolidRibbonTests.cs). [Tracking issue #34](https://github.com/chrizpro/prism/issues/34).

## Coverage gaps

Angled, tangent and curve-normal planes, alternate datum orientations and desktop reuse need further coverage. See [gizmo workflows](../../gizmo-workflows/README.md). New requirements need owner review.
