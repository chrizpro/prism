# Construction planes and sketch datums

Construction Plane creates exact reference frames from offsets, angles, midplanes, normal-to-curve locations, three points, tangent faces, two edges or a planar curve. Supported modes can generate a stack from a count or typed values. Datum opens the gizmo settings. Inside a sketch, Axis saves the selected axis, Plane saves the drawing plane with its optional normal offset, and Save gizmo stores the active frame.

## Verification

[Datum actions and three-point plane](../../../Prism.Tests/Headless/Solid/SketchAndDatumTests.cs), [offset plane](../../../Prism.Tests/Headless/Solid/SharedToolTests.cs) and [Datum Gizmo entry](../../../Prism.Tests/Headless/Solid/SolidRibbonTests.cs). [Tracking issue #34](https://github.com/chrizpro/prism/issues/34).

## Coverage gaps

Angled, tangent and curve-normal planes, alternate datum orientations and desktop reuse need further coverage. See [gizmo workflows](../../gizmo-workflows/README.md). New requirements need owner review.

## Standalone datums

The Datum Gizmo and Construction Plane already work in model space. The Datum menu's Point, Axis, Plane and Save gizmo actions currently require an active sketch. A saved sketch axis belongs to the sketch and the document has no standalone axis object.

**Proposed — awaiting owner review.** Make a standalone Axis available in model space, including placement in empty space. Review how the user specifies the origin and direction, whether the axis persists in the document, and which tools can pick it. Define the corresponding model-space behavior for Point, Plane and Save gizmo. [Issue #42](https://github.com/chrizpro/prism/issues/42).

### Placement and Browser editing

**Proposed — awaiting owner review.** Preserve the Point workflow where each viewport click places another point while the tool is active. Make each placed point selectable in the Browser and expose its editable properties after placement. Sketch Point currently creates sketch geometry, while the Browser's saved datum nodes come from a separate collection.

Extend placement to model space. Axis and Plane should have explicit placement tools that can work in empty space. Review which clicks define the origin and direction or orientation, when a placement is committed, and whether the tool stays active for another placement. Save gizmo currently captures the active frame in one action. Review its standalone behavior separately from viewport placement.

After review, verify repeated placement and cancellation, Browser selection and editing, persistence, and the distinction between model-level and sketch-owned datums.
