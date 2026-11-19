# Scan, mesh and reverse engineering

The **Scan** workspace uses one [Mesh tab](/tools/mesh) to prepare, inspect, repair and convert scans.

## Prepare and repair

1. Open **Import Mesh** for STL, OBJ, PLY or 3MF. Its menu also opens a point cloud and reconstructs a mesh from it.
2. Use **Align** to orient the scan or best-fit it to a reference.
3. Open **Mesh Quality** to find defects, then **Repair** to clean the entire mesh or a selected region. To fill a particular hole, use **Edit Region** to select the triangles around its entire rim. Green outlines show the holes that will close.
4. Use **Remesh** to redistribute triangles by edge length or reduce the triangle count. Keep the source to compare fine details.

**Edit Region** also lets you delete and patch, smooth or extract selected triangles. Smooth patching follows surrounding curvature. Flat patching triangulates the opening.

<GuideImage image="scan-mesh-quality" />

## Convert

| Convert Mesh output | Use it for |
| --- | --- |
| Solid | A direct-edit body fitted to the mesh, or a cylinder, sphere or cone fitted to one region |
| Surface | CAD faces fitted across the mesh or to one selected region |
| Freeform | An editable cage you can refine in [Freeform](/guide/freeform) |
| NURBS | A smooth patch fitted to a selected region |

Select triangles with **Edit Region** before converting a region. In **Convert Mesh**, preview the result and compare the sampled deviation in both directions. Red lines show open edges. A solid fit that leaves open surfaces has a **Keep open result as surface** option.

For a parametric part, use **Section Sketch** and add dimensions and constraints before extruding or revolving it. Those features create the editable history. For example, an axial section through a tube gives profiles on both sides of the axis. Keep one radial profile for the revolve.

<GuideImage image="scan-section-sketch" />

**Deviation** colors the scan by distance to a reference body or mesh. Open the Mesh Quality menu to **Clear Colors**. Use File → Export for mesh files and CAM for print checks.
