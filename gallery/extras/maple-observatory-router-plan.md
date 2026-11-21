# Maple observatory: proposed router work breakdown

Owner-selected demo bed: **600 Ã— 400 mm**, 2026-11-16. [Gallery delivery #140](https://github.com/chrizpro/prism/issues/140) Â· [Multi-part CAM proposal #141](https://github.com/chrizpro/prism/issues/141).

This is a planning inventory of the existing 116 display solids, not a cutting program or approved fabrication design. Dimensions are the recorded native solid bounds. Stock thickness, allowance, grain, fixtures and final part orientations must be chosen before toolpaths. The source model contains deep hall masses and applied details, rather than a panel-and-joinery kit.

## Inventory

[All 116 parts and decisions](maple-observatory-part-plan.csv). Candidate thickness and footprints are geometric planning values, not purchased stock specifications. The bed-fit check includes only a provisional 10 mm margin per side; it does not establish clamping or cutter access.

| Family | Solids |
| --- | --- |
| Plinth and structural blanks | 6 |
| Hall masses | 3 |
| Flat trim and stairs | 27 |
| Roof blanks | 3 |
| Dome blank | 1 |
| Thin veneer / inlay | 27 |
| Thin facade detail | 43 |
| Hand-fit hardware | 2 |
| Turned columns | 4 |

## Suggested jobs

- **Plinth:** its 300 Ã— 230 Ã— 8 mm finished envelope fits the bed. Face, edge/profile and any locating features belong in a named setup with chosen blank allowance and holding.
- **Walnut roofs:** the two 85 Ã— 132 Ã— 18 mm hipped roofs can fit beside one another on the same bed. A provisional arrangement with 15 mm outer margins and 15 mm spacing uses 200 Ã— 162 mm before blank allowances. They can share tooling, but each blank still needs secure holding; the underside may need its own registered setup.
- **Maple trim/stairs:** group by chosen stock thickness, material and grain. Machine many outlines on one sheet with tabs or on a carrier. Very thin mouldings may be better combined with their parent part.
- **Facade details:** turn each facade's machining face upward. Group compatible frames/inlays; do not assume the assembled upright orientation is machinable from global +Z.
- **Dome:** a dedicated 3D top setup, then an underside setup only if the manufacturing design calls for a hollow shell or locating seat.
- **Columns:** turning is the natural process. Indexed routing is an alternative requiring an explicit fixture plan.
- **Hall masses:** choose between panelized construction and deep solid blanks. The present display geometry does not supply wall joints, tool reach or flip registration.

## Saved project structure

Keep the design as an assembly. Create manufacturing jobs/setup sheets for physical bed loads, grouped by stock and machining orientation. Multiple small parts may share a bed, but a material change, thickness change or flip normally needs a separately reviewed setup. Give every setup its own stock, work zero, holding notes and ordered operations; export its program when ready.

Prism currently saves multiple named **single-body** setups and posts the active setup. Multi-outline 2D operations exist. Assembly-linked multi-body stock, shared removal review and one combined program for a bed load require the proposed extension in #141. Until then, individual-body programs can be prepared independently; they should not be represented as a validated shared-sheet job.

## Decisions before machining

Choose stock thicknesses/materials and fixture strategy; decide whether the halls are panelized; consolidate sub-mm applied details; choose column process; define registration for flips. Automatic nesting is optional after a correct manually placed multi-part workflow exists. No machine program or collision-free fixture claim accompanies this plan.
