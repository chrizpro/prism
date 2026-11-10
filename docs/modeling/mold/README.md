# Mold tab

**Review state:** Owner-requested 2026-11-08 ([#86](https://github.com/chrizpro/prism/issues/86)); implemented as the owner described, details open to review.

The **Mold** tab has two named sections. **Part** contains **Import from file** and **From open tab**. **Mold** contains **Create Mold**. To create a mold, pick a **mold block** (a solid, usually a box) and the **part to cast** (a solid, or a closed surface that encloses a volume) sitting inside it. Prism cuts the part's shape out of the block, splits the block into two halves and pulls them apart so the cavity is visible. The part stays where it is between the halves.

## Where the block splits

The owner wanted the split to be user-defined:

- **Parting direction**: Z, X or Y (top and bottom, left and right, or front and back halves).
- **Split at**: a percentage of the block's size along that direction (50% by default).
- **Parting plane or face** (optional): pick a plane, a planar face or a surface and it replaces the percentage. The plane must cut all the way through the block.
- **Explode distance**: the gap between the two halves (40 mm by default). Each half moves half of it.

The part must lie completely inside the block, otherwise the tool says so and changes nothing. The first half replaces the block (it keeps the block's name) and the second becomes a new body named *… (upper half)*.

## Options for making a real mold

- **Shrinkage allowance** (% , 0 by default): the cavity is the part scaled up about its centre, so the moulded part comes out the right size after it shrinks.
- **Cavity offset** (mm, 0 by default; negative shrinks): grows the cavity all round, for clearance or a tool offset.
- **Block margin**: pick no block and Prism makes one around the part with this margin on every side; the halves are then both new bodies (*Mold (lower half)*, *Mold (upper half)*).
- **Part opacity while exploded** (55% by default): once the mold exists the part changes colour (orange) and becomes see-through so it reads as the thing being cast.

## Importing the part

The **Part** group has two buttons, both bringing the part in as a **reference part**:

- **Import from file**: STEP, IGES or BREP from your hard drive.
- **From open tab**: copies the visible solids and surfaces of another open tab (pick the tab in the panel).

A reference part stays in the session, is shown and works with Create Mold, but is **not saved with the file** and cannot be a drawing source, so a saved mold opens (and imports into CAM) as the casing only. Bodies that were already in the document are not reference parts and are saved as usual. Tests: MOLD-H004, MOLD-H005. Files such as STL that import through the mesh dialog are not marked as reference parts yet.

## Not included yet

Draft, undercut and parting-line analysis, gates, runners, vents, ejector pins, shrinkage allowance and more than two parts. The existing **Tooling** tab keeps draft and section analysis for checking the part. The mold is an illustration of the process, not a validated mold design.

## Tests

[MOLD-H001 and MOLD-H002](../../../Prism.Tests/Headless/Mold/MoldTests.cs): the halves sit where the split is, the volumes add up to the block minus the part, a part outside the block is refused. Help picture and hover card are covered by the ribbon help tests.
