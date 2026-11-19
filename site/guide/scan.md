# Scan, mesh and reverse engineering

The **Scan** workspace uses one [Mesh tab](/tools/mesh) to prepare, inspect, repair and convert scans.

## Prepare and repair

1. Open **Import Mesh** for STL, OBJ, PLY or 3MF. Its menu also opens a point cloud and reconstructs a mesh from it.
2. Use **Align** to orient the scan or best-fit it to a reference.
3. Open **Mesh Quality** to find defects. **Repair → Cleanup** cleans the whole mesh or a selected region. Use **Fill Hole** to pick a single rim, or **Stitch Seam** to weld a pair of boundary edges.
4. Use **Remesh** to redistribute triangles by edge length or reduce the triangle count. Keep the source to compare fine details.

**Edit Region** lets you delete and patch, smooth, or extract selected triangles. **Draw Faces** lets you build a custom patch from an open rim or replace a selected region. Snap the corners to the mesh or draw on a work plane. Green lines preview the shared edges before **Stitch and apply**.

<GuideImage image="scan-mesh-quality" />

## Rebuild from sections

For a parametric part, use **Section Sketch** and add dimensions and constraints before extruding or revolving it. Those features create the editable history. For example, an axial section through a tube gives profiles on both sides of the axis. Keep one radial profile for the revolve.

<GuideImage image="scan-section-sketch" />

**Deviation** colors the scan by distance to a reference body or mesh. Open the Mesh Quality menu to **Clear Colors**. Use File → Export for mesh files and CAM for print checks.
