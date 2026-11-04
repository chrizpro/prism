# Scan, mesh and reverse engineering

The **Scan** workspace cleans up meshes and rebuilds scans as CAD. Reference: [Mesh](/tools/mesh), [Reverse Engineer](/tools/reverse-engineer).

The two ribbons do different jobs. **Mesh** fixes a mesh, and the result is still a mesh. **Reverse Engineer** turns a mesh into CAD you can edit. Drawing clean faces over a scan belongs to [Freeform](/guide/freeform): use **Freeform from Scan** and **Create Face**.

## Get and fix a mesh

1. **Import Mesh** (STL, OBJ, PLY, 3MF), **Import Point Cloud** then **Reconstruct Surface**, or **Solid to Mesh**.
2. **Repair** fixes the mesh; its Fill holes setting closes every hole up to a size limit. **Print Check** and **Mesh Quality** report problems.
3. Reshape with **Reduce**, **Remesh**, **Smooth**, **Offset**, **Split** and **Mesh Boolean**.

To remove something, open **Select Region**, pick the area with smart patch, brush, rectangle or lasso, and press **Delete and patch**. **Smooth** patching follows the surrounding curvature; **Flat** only triangulates the opening.

## Three ways to get CAD

| Route | Result | Use it when |
| --- | --- | --- |
| **Section Sketch** | An editable sketch of lines, arcs and circles | You want to rebuild the part with your own features |
| **Rebuild from Scan**, then Direct Edit | A solid with no history | You want a whole part quickly, then change it |
| **Segment & Fit** | Separate exact surfaces, with their sizes | You want to read a size, or need one face or cylinder |

Start with **Align**: drop the scan on the bed, center it, lay a flat face down, or best-fit it to the CAD model it was scanned from.

**Rebuild from Scan** makes an exact solid when every region is a plane, cylinder, cone or sphere. Parts with free-form regions are sewn instead and may stay open. **Mesh to Solid** converts a mesh directly.

## Check

**Deviation** colors the CAD model by its distance from the scan. **Clear Colors** removes the overlay.
