# Scan, mesh and reverse engineering

The **Scan** workspace cleans up meshes and rebuilds scans as CAD. Reference: [Mesh](/tools/mesh), [Reverse Engineer](/tools/reverse-engineer).

The two ribbons do different jobs. **Mesh** fixes a mesh, and the result is still a mesh. **Reverse Engineer** turns a mesh into CAD you can edit. Drawing clean faces over a scan belongs to [Freeform](/guide/freeform): use **Freeform from Scan** and **Create Face**.

## Get and fix a mesh

1. **Import Mesh** (STL, OBJ, PLY, 3MF), **Import Point Cloud** then **Reconstruct Surface**, or **Solid to Mesh**.
2. Use **Mesh Quality** and **Print Check** in **Check** to find problems, then open **Repair**. Choose Entire mesh or Selected region. For a local repair, first use Select Region to select the triangles around the entire rim of the unwanted opening. Other openings remain open. Green rims preview holes that will close; Fill holes can be turned off. The hole-size limit counts rim edges.
3. Use **Reshape** for Reduce, Remesh and Smooth. **Modify** exposes Split, Offset and Mesh Boolean. Remesh makes the triangle pattern more even; keep the original for comparison and inspect the reported volume change.

To remove something, open **Select Region**, pick the area with smart patch, brush, rectangle or lasso, and press **Delete and patch**. **Smooth** patching follows the surrounding curvature; **Flat** only triangulates the opening.

<GuideImage image="scan-mesh-quality" />

## Three ways to get CAD

| Route | Result | Use it when |
| --- | --- | --- |
| **Section Sketch** | An editable sketch of lines, arcs and circles | You want to rebuild the part with your own features |
| **Rebuild from Scan**, then Direct Edit | A solid with no history | You want a whole part quickly, then change it |
| **Segment & Fit** | Separate exact surfaces, with their sizes | You want to read a size, or need one face or cylinder |

Start with **Align**: drop the scan on the bed, center it, lay a flat face down, or best-fit it to the CAD model it was scanned from.

**Rebuild from Scan** makes an exact solid when every region is a plane, cylinder, cone or sphere. Parts with free-form regions are sewn instead and may stay open. **Mesh to Solid** converts a mesh directly.

The ribbon follows **Prepare → Sketch from Scan → Fit Surfaces → Rebuild → Check**. Fit Surface to Region is directly accessible for local corrections; Mesh to Solid is under **Rebuild → Fallback**.

For a **parametric part**, create a Section Sketch, edit it to add dimensions and constraints, then extrude or revolve. You can trim unwanted sketch outlines first: an axial tube section has an outline on either side of the axis, and you normally revolve only one. Change the sketch dimensions to adjust your rebuilt part. Rebuild from Scan and surface fitting do not recover the original feature tree; Freeform from Scan creates an editable cage.

<GuideImage image="scan-section-sketch" />

## Check

**Deviation** colors the scan by its distance from the reference CAD model or mesh. **Clear Colors** removes the overlay.
Select the scan as the mesh to check and the rebuilt CAD body as the reference. The check samples the scan against the reference; it cannot establish the intended shape where the scan is missing.
