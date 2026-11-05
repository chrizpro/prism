# Mesh and Reverse Engineer tabs

The two tabs do different jobs. **Mesh** fixes a mesh, and the result is still a mesh. **Reverse Engineer** turns a mesh into CAD you can edit. Drawing clean faces over a scan belongs to Freeform, because a quad cage is what Freeform edits: use Freeform from Scan, Create Face and the scan tools in Edit Freeform.

## Mesh

- **Select, Datum:** shared sections
- **Get a mesh:** Import Mesh, Solid to Mesh, Import Point Cloud / Reconstruct Surface
- **Region:** Select Region
- **Repair:** Repair (with Fill holes as a setting), Print Check, Mesh Quality
- **Reshape:** Reduce, Remesh, Smooth, More (Offset, Split, Mesh Boolean)
- **Print analysis:** Wall Thickness, Overhang & Orientation
- **Display:** Clear Colors
- **Transform, Inspect, Appearance:** shared sections

### Removing something from a mesh

Open **Select Region**, pick the area with smart patch, brush, rectangle or lasso, and press **Delete and patch**. Only the openings that leaves are closed; other holes stay as they are.

- **Smooth** adds points at the mesh's own size and follows a curved surface fitted to the mesh around the opening.
- **Flat** only triangulates the opening.

Repair's Fill holes closes every hole up to a size limit, which is what you want before printing.

## Reverse Engineer

- **Select, Datum:** shared sections
- **Set up:** Align, Select Region
- **Sketch from scan:** Section Sketch
- **Fit:** Segment & Fit / Fit Surface to Region, Rebuild from Scan
- **Check:** Deviation, Clear Colors
- **Convert:** Freeform from Scan, Mesh to Solid
- **Transform, Inspect, Appearance:** shared sections

### Three ways to get CAD

| Route | Result | Use it when |
|---|---|---|
| Section Sketch | An editable sketch of lines, arcs and circles | You want to rebuild the part with your own features |
| Rebuild from Scan, then Direct Edit | A solid with no history | You want a whole part quickly, then change it |
| Segment & Fit | Separate exact surfaces, with their sizes | You want to read a size, or need one face or cylinder |

**Section Sketch** slices on XY, XZ or YZ, on a construction plane or flat face, or on a plane fitted to the area picked in Select Region.

**Rebuild from Scan** makes an exact solid when every region is a plane, cylinder, cone or sphere: each fitted surface slices a block around the scan, and the pieces inside the scan are joined. Open **Direct Edit** on the Solid tab to change it. Parts with free-form, torus, extruded or revolved regions are sewn instead, so they may stay open and their edges may not work with Direct Edit.

**Mesh to Solid** makes one face per triangle. It is heavy and hard to edit, so it is a last resort.

## Maintained tests

[Mesh tool tests](../../../Prism.Tests/Headless/Mesh/MeshToolTests.cs) cover Repair and Fill Holes, Print Check (printers, custom volume, materials), Reduce, Smooth, Offset, Split (every plane, flip, open cut, keep options), Section Curves, Wall Thickness, Overhang orientation, Deviation, every Align mode, Mesh Boolean, Remesh, Mesh Quality and both conversions between meshes and solids. [Reverse engineering tests](../../../Prism.Tests/Headless/Mesh/ReverseEngineerTests.cs) cover Segment & Fit, Rebuild from Scan (faces, curves and sewn solid), Section Lines & Arcs, Section Sketch and Select Region actions. Run `./tools/test.ps1 -Feature Mesh`.

Fixed on 2026-11-05:

- Section Curves (with "Create curves" off), Section Lines & Arcs (with "Create lines and arcs" off), Segment & Fit ("Nothing - just analyze", and a missing region number) and Rebuild from Scan ("Nothing - analysis…") used to clear their report before returning, so the panel showed no rows. They now keep it; report-only tests cover each mode.
- Solid to Mesh detail levels used to produce the same mesh on small curved bodies: a fixed angular limit outweighed the chord error, and a finer triangulation already stored on the body (such as the display mesh) was reused. Each level now has its own angular limit (Custom leaves the chord error in charge) and meshes a fresh copy of the geometry. The test asserts Coarse < Normal < Fine.

## Tested on

Generated parts only: a drilled plate, an L bracket and a plate with a boss, remeshed and given up to ±0.2 mm of random noise. Each rebuilt to a valid solid with the right faces, within 0.3% of the volume, and Direct Edit pulled a face as it does on the original. No real scan has been tried, and real scans have holes, missing sides and uneven noise.

Section Sketch does not add constraints yet, so its sketches have no parallel, equal or tangent relations.

The older commands (Fill Holes, Section Curves, Section Lines & Arcs) are off the ribbon but still work from scripts.

Windows verification on 2026-11-05: the combined candidate passed 86 Unit and 592 Headless tests with rebuilt native libraries. See the [integration verification report](../../engineering/release-readiness-2026-11-05.md) for source revisions, commands and limits.
