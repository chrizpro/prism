# Surfaces and NURBS

Surfaces describe shape without thickness. Use them for flowing product forms, then sew and thicken them into solids. Reference: [Surface](/tools/surface), [NURBS](/tools/nurbs).

**Surface** is for profile-based surfaces and finishing. **NURBS** is a styling workflow: draw curves, build patches, push control points and match continuity.

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

## Shape and refine (NURBS)

- **Edit Points** shows control points with the gizmo: arrows move, rings rotate, blocks scale. Shift-click adds points to the selection; a soft influence radius moves neighbours with a falloff.
- **Sculpt** brushes the surface: pull, push, move or smooth.
- **Rebuild Surface** fits one patch through an even grid and reports the deviation. **Add Detail** and **Raise Degree** give more control where you need it.
- **Warp** bends, twists, tapers or stretches along an axis.

## Continuity

**Match Surface** matches compatible patch boundaries with G0 (position), G1 (tangent) or G2 (curvature) constraints. **Check Seam** reports the gap and angle along a shared edge. Use **Zebra Stripes**, **Curvature Map** and **Curvature Graph** to see the quality: smooth, unbroken stripes across a seam mean a smooth join.

## Finish

**Trim** and **Untrim**, **Extend**, **Offset** and **Reverse Normal** tidy individual surfaces. **Sew** joins surfaces along shared edges and can make a solid when they close; **Thicken** gives a surface a wall. **Enclosed Body** builds a solid from surfaces that enclose a volume. **Flatten / Refold** lays out a surface flat and wraps geometry back onto it.
