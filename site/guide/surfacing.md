# Surfaces and NURBS

Surfaces describe shape without thickness. Use them for flowing product forms, then sew and thicken them into solids. Reference: [Surface](/tools/surface), [NURBS](/tools/nurbs).

**Surface** is for profile-based surfaces and finishing. **NURBS** is a styling workflow: draw curves, build patches, push control points and match continuity.

<GuideImage src="/images/guides/nurbs-duct-vp.png" alt="A tapered curved duct with a circular opening and mounting flange" caption="A duct example combines a flowing transition with a mounting flange and bolt holes." :width="664" :height="488" />

## Curves first

Good surfaces come from good curves. The Curves section on both ribbons has **Curve** (through points or control points), **Curve from Data** (formulas or tables), **Project**, **Isocurves**, **Contour** and **Duplicate Edge**. Edit them with Fillet / Chamfer, Offset, Blend (G0, G1 or G2 to another curve), Join, Explode, Trim, Extend, Rebuild and Divide.

On NURBS, press <kbd>K</kbd> to resume drawing in the shared NURBS curves sketch.

## Build surfaces

| Tool | Use it for |
| --- | --- |
| Extrude, Revolve | Straight and turned surfaces from a profile; open profiles are fine |
| Sweep | A profile carried along a path: follow the path, keep parallel or keep upright, with end scaling |
| Pipe | A round, optionally tapered pipe along a curve |
| Loft | A surface through ordered sections, with optional guide rails |
| Boundary Surface | A patch from boundary curves; also ruled surfaces and patch filling |
| Control Grid | A NURBS patch you shape directly by its control points |

<GuideImage image="surface-loft-sections" />

### Boundary Surface

**Boundary Surface** is under **Create** on Surface and **Surfaces** on NURBS. It fills or spans a boundary made of curves, sketches and surface edges. Reference: [Boundary Surface](/tool/boundary-surface).

1. Start **Boundary Surface** and pick the curves, sketch curves or surface edges that surround the new surface. Anything you selected first fills the **Boundary** input.
2. Leave **Fill with** on Automatic: a flat closed loop becomes a flat surface, with loops inside it as holes, and anything else becomes a smooth surface. Choose Flat surface or Smooth surface to force one; Flat refuses a bent loop.
3. To shape the inside, add curves in **Guide curves**. **Inside curves** either follows them exactly or fits through points taken along them.
4. For a hole in an existing surface, click one open edge to fill the whole opening, or pick the surface to fill every opening. Keep **Join** on to sew the patch to its neighbours; it closes into a solid when it can.
5. Pick an edge on each of two separate surfaces to blend between them. Set the continuity (G0, G1 or G2) and weight for each boundary edge.
6. Check the preview, then confirm. Cancel leaves the model unchanged.

Two curves can also span a ruled surface. To make an enclosure on Surface, build the surfaces, then use **Enclosed Body**.

## Shape and refine (NURBS)

- **Edit Points** shows control points with the gizmo: arrows move, rings rotate, blocks scale. Shift-click adds points to the selection; a soft influence radius moves neighbours with a falloff.
- **Sculpt** brushes the surface: pull, push, move or smooth.
- **Rebuild Surface** fits one patch through an even grid and reports the deviation. **Add Detail** and **Raise Degree** give more control where you need it.
- **Warp** bends, twists, tapers or stretches along an axis.

<GuideImage image="nurbs-control-grid" />

<GuideImage image="nurbs-edit-control-points" />

## Continuity

**Match Surface** matches compatible patch boundaries with G0 (position), G1 (tangent) or G2 (curvature) constraints. **Check Seam** reports the gap and angle along a shared edge. Use **Zebra Stripes**, **Curvature Map** and **Curvature Graph** to see the quality: smooth, unbroken stripes across a seam mean a smooth join.

<GuideImage image="surface-zebra-inspection" />

## Finish

**Trim** and **Untrim**, **Extend**, **Offset** and **Reverse Normal** tidy individual surfaces. **Sew** joins surfaces along shared edges and can make a solid when they close; **Thicken** gives a surface a wall. **Enclosed Body** builds a solid from surfaces that enclose a volume. **Flatten / Refold** lays out a surface flat and wraps geometry back onto it.

<GuideImage image="surface-thicken-preview" />
