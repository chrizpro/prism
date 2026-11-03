# Freeform extrusion — specification review

> **Requirements:** Proposed — awaiting owner review (2026-11-03).  
> **Verification:** Source and [Freeform tab guide](README.md) inspected; no desktop checks run.

## Two different Freeform extrusion workflows

The Freeform ribbon lists **Create → Extrude Profile** and **Modify → Extrude**; they should not be treated as the same feature.

- **Extrude Profile:** Constructs a new editable subdivision cage from a selected sketch, curve, or edge.
- **Modify → Extrude:** Edits an existing cage selection; the Freeform guide notes an extrusion amount in the Edit Freeform panel. The precise face/edge/vertex semantics need desktop inspection.

This page pilots **Extrude Profile** requirements. The Modify command remains in scope for documentation discovery, not an assumed clone of the profile command.

## Observed Extrude Profile implementation

[`FormExtrudeCommand`](../../../Prism/Commands/FormShapeCommands.cs) offers these source-observed controls:

| Area | Current code | Review question |
| --- | --- | --- |
| Profile | Curve, sketch or edge | Which open/closed profiles work and how do they handle corners? |
| Distance | Numeric signed distance; initial 30 | Should a zero-distance form be allowed? |
| Direction | Profile normal or world X/Y/Z | Should negative world axes also have explicit UI options? |
| Far end | Scale factor from 0.01 to 20 (initial 1) | Should end scale be allowed to collapse to zero? |
| Topology | 1–60 steps along way, 4–96 profile points | Should these ranges and resampling be user-visible guarantees? |
| Ends | Close the ends checkbox (closed profile) | What happens when open profiles request caps? |
| Display | Smoothness level 1–4 | How does this relate to actual control cage topology? |
| Result | New Freeform (`ObjKind.Form`) cage with point/face counts | What editing/history promises should be made? |

The implementation resamples profile points, constructs rows along the extrusion direction and creates a new form cage. It is distinct from OCCT solid/surface extrusion.

## Proposed acceptance criteria — owner review required

| Review topic | Proposed expected behavior | State |
| --- | --- | --- |
| Profile to form | A valid profile can create an editable freeform cage | Proposed |
| Open profiles | Supported open profiles create a predictable open cage topology | Proposed |
| Closed profiles | Supported closed profiles can create a capped cage when requested | Proposed |
| Scaling | End scale smoothly interpolates between the starting and ending sections | Proposed |
| Direction | Direction options give predictable cage orientation | Proposed |
| Validity | Degenerate profiles and invalid topology fail with useful messages, preserving the document | Proposed |
| Modification | The separate Edit Freeform **Modify → Extrude** tool has documented selection and topology rules | Needs separate review |

No requirements have been approved or given stable IDs.

## Decisions needed

1. Should **Extrude Profile** and **Modify → Extrude** receive separate stories? They are distinct implementations.
2. How should zero length, negative distance, degenerate normals and scaling toward zero work?
3. For open profiles, are end caps never supported, or do we need a separate definition?
4. What topological invariants should the tool enforce (manifoldness, winding, boundary edges)?
5. Should the smoothing setting only affect the display or the created cage's subdivision properties?

## Traceability and history

- **Feature story:** Pending link.
- **Implementation:** [`Prism/Commands/FormShapeCommands.cs`](../../../Prism/Commands/FormShapeCommands.cs), `FormExtrudeCommand`; existing [Freeform guide](README.md) describes Modify → Extrude separately.
- **Tests:** Coverage for these specific behaviors not verified. No tests added or run.
- **Delivery:** When approved and delivered, closed stories link their pinned specification version; future changes get new stories or bugs.

[Shared requirements policy](../../engineering/requirements-and-testing.md)
