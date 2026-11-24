# Flatten / Refold

The Surface ribbon has one **Flatten / Refold** entry. Flatten creates a layout;
**Keep flat and exit** leaves it in the document for sketches, cuts, measurements
and DXF export. Select the saved layout and reopen the same tool to switch
automatically to **Refold / wrap**. Each operation is an undoable feature.

Single faces retain their source surface and reference layout across save/open.
Cylinders retain the existing analytic, length-preserving return path. General
faces use a UV layout whose dimensions follow the middle U and V isocurves;
this can stretch lengths and areas and is not a manufacturing development of
a doubly curved surface. Choose **Surface pattern with face boundaries** under **Layout method** to flatten
the actual shell using the conformal mesh solver. The output follows the sampled
rim and holes, includes shared face boundaries as separate guide curves when
joining faces, and reports stretch. DXF exports the outer and hole outlines.
A curved spatial edge may still develop straight. The existing **UV layout for
refolding** option retains the editable return map for a single supporting face.
Surface patterns from the mesh solver have no return map. Cylinders with a
rectangular analytic development continue to retain their return map.

Refold / wrap accepts curves, sketches, sheets and solids. A saved layout supplies
the target automatically; an optional target face overrides it. A plain planar
reference face can also be mapped to a target surface without flattening first.
U, V and normal scales and offsets, Mirror U, Swap UV, Flip normal and Keep input
geometry control the result. Thickness follows the target normal. General mapping
fits B-spline geometry, refines it until sampled error meets the mapping tolerance,
preserves the B-rep topology and rejects invalid results. It does not certify a
global error bound or rule out every possible self intersection.

Reference curves can be mapped to target curves with the same geometry inputs.
Each reference is a single line, arc or spline edge. Along-curve placement uses
arc length, and cross sections follow rotation-minimizing frames through spatial
bends and inflections. U scale/offset control along-curve placement, V controls
the transverse offset, and Normal controls the other cross-section direction.
Swap UV exchanges the two cross-section directions for curve wrapping.

**Rebuild last fillet after wrapping** uses a final Fillet feature recorded in
Prism's history: it wraps the unfilleted body, then applies the specified radius
(or variable end radius) again. It requires that final feature and its inputs;
imported fillets and bodies edited after filleting need manual refilleting.

Periodic seams and singularities may require splitting the target or input first.
Geometry that extends outside the reference domain is rejected. Complex mappings
that cannot meet the requested tolerance are also rejected rather than committed.

Python scripts use the same operations:

```python
import cadkit as cad

layout = cad.flatten({"object": "Cylinder 1", "face": 1}, create="Flat surface only")
# Draw or cut on the layout, then:
curved = cad.refold(layout)
# Or wrap a separate object onto another target:
wrapped = cad.wrap("Logo 1", reference=layout, target="Surface 1",
                   u_scale=0.8, normal_offset=1, keep_input_geometry=True)
```

The `flatten` and `refold` automation command names remain available for existing
scripts. Saved recipes from the earlier separate tools are migrated when restored.

Plasticity comparison: this covers its documented Unwrap plus surface/curve
Deform workflow, geometry categories and mapping controls. Full equivalence is
not established: automatic reblending of arbitrary imported fillets remains a
gap, and the kernels may accept different edge cases. Reference:
[Unwrap Face](https://plasticity-document-revisions.vercel.app/solid/unwrap-face)
and [Deform](https://plasticity-document-revisions.vercel.app/solid/deform-solid-and-sheet.en).

The [Aurelia leaf gallery model](../gallery/models/aurelia-leaf.prism) retains a NURBS forming surface, its flat reference, a pierced blank and the refolded sheet. The result has 14 interior wires and spans 23.20 mm in Z. Toggle the forming surface in the browser to inspect how the leaf follows it.

The [Meridian helmet](../gallery/models/meridian-helmet.prism) uses Flatten, a 4 mm solid Extrude and Refold on a shared master surface. The finished shell has four trimmed panels. The rib is one valid solid with a cross-section at each panel boundary. Five sampled normal heights agreed within 0.04 mm. Independent multi-face return mapping remains outside this example.

The [revised Meridian helmet](../gallery/models/meridian-helmet.prism) shows the curved
outline and internal seams calculated from the four-face shell. The copper rib is a
4 mm extrusion refolded through the shared master surface's UV layout. The geometric
pattern reported average stretch of 14.9% and a worst case of 74.3%. [FLT-H201](../Prism.Tests/Headless/Surfacing/SurfacePatternTests.cs)
checks the connected outline and seams. [Issue #163](https://github.com/chrizpro/prism/issues/163).

## Regression coverage

The [headless Refold tests](../Prism.Tests/Headless/Surfacing/RefoldStressTests.cs) exercise a cylinder tilted 41 degrees with 0.05 mm clearance at its seam and between holes. They check individual opening positions, the surviving web and the analytic surface area. The curve-wrap case compares a selected solid edge with the same edge extracted as a standalone guide on a spatial spline. Both passed on Windows on November 23, 2026 after correcting reference-edge selection ([issue #162](https://github.com/chrizpro/prism/issues/162)).
