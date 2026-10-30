# Flatten / Refold

The Surface ribbon has one **Flatten / Refold** entry. Flatten creates a layout;
**Keep flat and exit** leaves it in the document for sketches, cuts, measurements
and DXF export. Select the saved layout and reopen the same tool to switch
automatically to **Refold / wrap**. Each operation is an undoable feature.

Single faces retain their source surface and reference layout across save/open.
Cylinders retain the existing analytic, length-preserving return path. General
faces use a UV layout whose dimensions follow the middle U and V isocurves;
this can stretch lengths and areas and is not a manufacturing development of
a doubly curved surface. Multi-face mesh flattening continues to report strain,
but does not yet retain a general return map.

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
