# Scan, mesh and reverse engineering

The **Scan** workspace uses one [Mesh tab](/tools/mesh) to prepare, inspect, repair and convert scans.

## Prepare and repair

1. Open **Import Mesh** for STL, OBJ, PLY or 3MF. Its menu also opens a point cloud and reconstructs a mesh from it.
2. Use **Align** to orient the scan or best-fit it to a reference.
3. Open **Mesh Quality** to find defects. **Repair → Cleanup** cleans the whole mesh or a selected region. Use **Fill Hole** to pick a single rim, or **Stitch Seam** to weld a pair of boundary edges.
4. Use **Remesh** to redistribute triangles by edge length or reduce the triangle count. Keep the source to compare fine details.

**Select → Face** picks individual mesh triangles. Press **Delete** to remove them and leave an opening. **Edit Region** lets you delete and patch, smooth, or extract selected triangles. **Draw Faces** lets you build a custom patch from an open rim or replace a selected region. Snap the corners to the mesh or draw on a work plane. Green lines preview the shared edges before **Stitch and apply**.

<GuideImage image="scan-mesh-quality" />

## Rebuild from sections

Open **Section Sketch** and drag the cutting plane’s arrows or rings to position it. **Apply section** saves a sketch at the current position. Move the plane and apply again for each cut you need, then choose **Done**.

Edit a generated sketch to compare the fitted curves with the orange measured outline. Use **Fit Section Curve** in the sketch panel to fit a span or a closed circle. Add a driving dimension to set the intended size, such as 50 mm for a measured 50.3 mm edge. The measured outline remains available for comparison.

Use the Solid or Surface tools that fit the part you’re rebuilding. For an axial cut through a tube, keep one radial profile and revolve it.

<GuideImage image="scan-section-sketch" />

**Deviation** colors the scan by distance to a reference body or mesh. Open the Mesh Quality menu to **Clear Colors**. Use File → Export for mesh files and CAM for print checks.
