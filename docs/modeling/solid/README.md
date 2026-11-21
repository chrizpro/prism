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

## Threads

Modeled 60° threads, cut as real geometry (no cosmetic thread, no manufacturing allowance).

- **THR-P0 Internal threads (existing).** The *Hole* tool makes a plain or *Threaded (modeled)* hole, and *Edit Feature* can retype an existing hole into a threaded one.
- **THR-P1 Thread tool (delivered 2026-11-14, owner request).** *Thread* shares one dropdown with Hole on the Home and Solid tabs (click the icon to run the shown tool, the name to choose the other) and cuts a thread into an existing round face. Pick the outside of a pin, bolt or boss for an **external** thread, or the wall of a hole you already drilled for an **internal** one; which is read from the face. The size comes from the face: a pin's diameter is the major diameter, and a hole's diameter is the tap drill, so a Ø5 hole threads as M6 × 1. The pitch is the ISO coarse pitch for that size (M1.6 to M64), or *Custom*; a diameter with no standard size is refused with a message to choose Custom. Options: thread length (the whole face by default), start offset, start at the other end, left-hand. A thread that does not fit, runs more than 200 turns, or is on a flat face is refused with a message. Internal threads are the same geometry as the Hole tool's.
- **Not built:** thread run-out and chamfers, tapered (pipe) threads, other standards than the 60° metric profile, and cosmetic threads that show on a drawing without geometry. The kernel's boolean is fragile where a helical groove meets a round face's seam; the tool checks each cut against the groove's own volume and retries at other rotations of the groove, and refuses with a message if none is clean.
- **Acceptance.** An M10 × 1.5 thread on a Ø10 × 20 pin removes the analytic groove volume (280 mm³ within 6%) and leaves the crest at Ø10; half the length cuts half; left-hand cuts the same amount. A hole drilled to Ø4.917 and threaded with the Thread tool matches the Hole tool's own threaded M6 × 1 hole within 0.5% in volume. Tests: [SOL-H160 to SOL-H163](../../../Prism.Tests/Headless/Solid/ThreadTests.cs), Solid slice 151 of 151 passed on Windows; not tried by hand.

## Review and verification

The public [Solid modeling walkthrough](../../../site/guide/solid.md) illustrates Extrude, Fillet, a confirmed open Shell, Linear Pattern and an editable drawing with actual app captures. [Image provenance and documentation verification](../../../site/README.md#guide-images) record the capture limits; these illustrations do not replace the feature tests below.

The tests characterize existing behavior. Open requirements and options remain in the feature guides and [tracking issue #31](https://github.com/chrizpro/prism/issues/31).

- [Ribbon actions](../../../Prism.Tests/Headless/Solid/SolidRibbonTests.cs) cover the Pipe/Sweep/Loft boundary, Direct Edit entry and exit, selection modes, the Datum Gizmo and Materials entry.
- [Shared tools](../../../Prism.Tests/Headless/Solid/SharedToolTests.cs) cover sketch entry, plane offset, curve creation and inspection results.
- [Inspect tool tests](../../../Prism.Tests/Headless/Solid/AnalysisToolTests.cs) cover Interference (picked, all, created bodies), Check Geometry, Curvature Map ranges, Draft Analysis directions, Surface Continuity and Match Surface.
- [Curve expression unit tests](../../../Prism.Tests/Unit/Solid/CurveExpressionTests.cs) exercise formula evaluation.

Run `./tools/test.ps1 -Feature Solid`. Extrude and Revolve retain their own feature filters. The shared checks leave desktop viewport interactions and the tools inside the contextual Sketch and Direct Modeling tabs for their own workflows. See [the ribbon index](../../tabs/README.md) for other workspaces.

Local verification on 2026-11-03: `./tools/test.ps1` passed 18 Unit and 78 Headless cases on Windows using the installed native binaries. This includes 4 new Unit and 39 new Headless Solid cases.

Shared-tool follow-up on 2026-11-03: all 51 shipped Solid tool entries, including dropdown choices, have representative coverage across the linked suites. The full local run passed 20 Unit and 86 Headless cases, including 2 new selection unit cases and 8 new shared-tool headless cases.

## Coverage scope

The 51 entries above refer only to the Solid ribbon. The contextual [Sketch](../sketches/README.md#sketch-contextual-tab) and [Direct Modeling](../direct-modeling/README.md#contextual-coverage) tabs add 40 and 35 entries respectively, with shared controls appearing in both. The [Solid option-level issue #41](https://github.com/chrizpro/prism/issues/41) tracks options on the Solid ribbon itself. Sketch and Direct Modeling have separate issues in their guides.

Windows verification on 2026-11-05: the combined candidate passed 86 Unit and 592 Headless tests with rebuilt native libraries. See the [integration verification report](../../engineering/release-readiness-2026-11-05.md) for source revisions, commands and limits.
