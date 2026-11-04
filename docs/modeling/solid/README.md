# Solid ribbon and shared controls

The Solid tab groups primitives, features, modification, Direct Edit, Combine, Transform and Pattern. It also exposes shared Sketch, Datum, Curves, Select, Inspect and Appearance controls. Pipe is available in Surface, NURBS and Freeform. Solid omits Pipe, Sweep and Loft.

## Feature guides

- [Primitives](../primitives/README.md)
- [Solid Extrude](../extrude/README.md#solid-extrude) and [Solid Revolve](../revolve/README.md#solid-revolve)
- [Rib, Emboss and body modifications](../solid-details/README.md)
- [Direct Modeling](../direct-modeling/README.md)
- [Combine](../combine/README.md), [Transform](../transform/README.md) and [Patterns](../patterns/README.md)

## Shared tools

- [Sketch entry and reference points](../sketches/README.md)
- [Construction planes and sketch datums](../construction-planes/README.md)
- [Curve creation](../curves/README.md)
- [Selection](../../interface/selection/README.md)
- [Inspect and Appearance](../../interface/inspection/README.md)

## Review and verification

The tests characterize existing behavior. Open requirements and options remain in the feature guides and [tracking issue #31](https://github.com/chrizpro/prism/issues/31).

- [Ribbon actions](../../../Prism.Tests/Headless/Solid/SolidRibbonTests.cs) cover the Pipe/Sweep/Loft boundary, Direct Edit entry and exit, selection modes, the Datum Gizmo and Materials entry.
- [Shared tools](../../../Prism.Tests/Headless/Solid/SharedToolTests.cs) cover sketch entry, plane offset, curve creation and inspection results.
- [Curve expression unit tests](../../../Prism.Tests/Unit/Solid/CurveExpressionTests.cs) exercise formula evaluation.

Run `./tools/test.ps1 -Feature Solid`. Extrude and Revolve retain their own feature filters. The shared checks leave desktop viewport interactions and the tools inside the contextual Sketch and Direct Modeling tabs for their own workflows. See [the ribbon index](../../tabs/README.md) for other workspaces.

Local verification on 2026-11-03: `./tools/test.ps1` passed 18 Unit and 78 Headless cases on Windows using the installed native binaries. This includes 4 new Unit and 39 new Headless Solid cases.

Shared-tool follow-up on 2026-11-03: all 51 shipped Solid tool entries, including dropdown choices, have representative coverage across the linked suites. The full local run passed 20 Unit and 86 Headless cases, including 2 new selection unit cases and 8 new shared-tool headless cases.
