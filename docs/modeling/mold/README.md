# Mold tab

**Review state:** Owner-requested 2026-11-08 ([#86](https://github.com/chrizpro/prism/issues/86)); implemented as the owner described, details open to review.

The **Mold** tab has one button, **Create Mold**. Pick a **mold block** (a solid, usually a box) and the **part to cast** (a solid, or a closed surface that encloses a volume) sitting inside it. Prism cuts the part's shape out of the block, splits the block into two halves and pulls them apart so the cavity is visible. The part stays where it is between the halves.

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

- **Import from file**: STEP, IGES, BREP or native Prism files, including typed files. Native imports copy visible solids and surfaces into the current mold document. The source document stays separate. [MOLD-H006](../../../Prism.Tests/Headless/Mold/MoldTests.cs) · [Bug #98](https://github.com/chrizpro/prism/issues/98).
- **From open tab**: copies the visible solids and surfaces of another open tab (pick the tab in the panel).

A reference part stays in the session, is shown and works with Create Mold, but is **not saved with the file** and cannot be a drawing source, so a saved mold opens (and imports into CAM) as the casing only. Bodies that were already in the document are not reference parts and are saved as usual. Tests: MOLD-H004, MOLD-H005. Files such as STL that import through the mesh dialog are not marked as reference parts yet.

## Saving for manufacturing

Owner direction on 2026-11-09 added **Prism mold (`.prism_mld`)** to Save As. It writes the solid halves created by Create Mold at their current positions. It excludes the cast part, other bodies, sketches, joints, studies and feature history. Saving this copy keeps the full working document and its unsaved state. Use `.prism` for the full project or `.prism_asm` for a full mold with two or more halves and no drawing sheets. A reopened mold-only file can be edited, then saved as a full project or saved as another geometry-only copy. Older files without mold-body identification can still be opened. Recreate the mold to make its halves identifiable for export.

Use **Import mold** in CAM → **Subtractive** or **3D Resin**. It adds exact solids to the active document in one undo step. The mold-specific import is offered in those two tabs. [MOLD-H007](../../../Prism.Tests/Headless/Mold/MoldTests.cs) · [Issue #100](https://github.com/chrizpro/prism/issues/100).

## Not included yet

Draft, undercut and parting-line analysis, gates, runners, vents, ejector pins, shrinkage allowance and more than two parts. The existing **Tooling** tab keeps draft and section analysis for checking the part. The mold is an illustration of the process, not a validated mold design.

## Tests

[MOLD-H001 and MOLD-H002](../../../Prism.Tests/Headless/Mold/MoldTests.cs): the halves sit where the split is, the volumes add up to the block minus the part, a part outside the block is refused. Help picture and hover card are covered by the ribbon help tests.

Windows verification on 2026-11-09 with the fix applied to `fb99758`. MOLD-H006 passed in the 61-test Headless selection. It checked the file-picker filter and verified the native source's visible 8 mm cube arrived as a separate 512 mm³ reference body.

MOLD-H007 passed in the 89-test Windows Headless selection for Mold, Section, DocumentSafety and CAM. A 20 mm block with a 4 mm cubic casting produced two tooling solids totaling 7,936 mm³. The test checked the full assembly save, mold-only Save As, and import into an existing document.
