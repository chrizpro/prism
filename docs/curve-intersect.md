# Curve Intersect

Combine two planar profiles into a 3D curve. Each profile defines one view of the result: the tool intersects the surfaces obtained by projecting each profile along its support plane's normal. The input curves do not need to touch in space.

1. Draw two profiles on different, nonparallel planes and finish their sketches. Planar standalone NURBS curves also work.
2. Open **Curve Intersect** under **Curves & planes** on **Surface**, **NURBS**, or **Freeform**. It is also available through command search.
3. Pick the first and second curve/sketch in the viewport or their corresponding right-panel selection fields. Individual planar edges can also be selected.
4. Inspect the live 3D curve. Dashed projection guides identify how the result corresponds to each input; **Show projection guides** hides them.
5. Choose **Create curve**. Both sources stay in the document. Use the new curve as a sweep path, loft guide, NURBS curve or freeform reference. Creation is one undo step.

The result follows the drawn extents of both sources. Parallel supports, nonplanar inputs and profiles with no shared projected range report an error. A straight curve needs its parent sketch's support plane because a line alone does not define a projection direction. For a sketch spanning several planes, select individual planar edges rather than the whole sketch.

Connected intersection edges are joined into wires. Closed or branching profiles can produce more than one connected curve; **Separate disconnected curves** creates a separate object for each wire. The result is kernel B-rep curve geometry, rather than a rendered polyline. It is a derived curve: changes to the sources require re-editing/rebuilding the feature; it is not a live sketch constraint.

The existing **Intersection Curve** tool remains available for crossing surfaces, faces, planes and bodies. **Curve Intersect** handles the two planar profile workflow.

Example document: [two-plane-intersection.prism](../artifacts/curve-intersect-review/two-plane-intersection.prism). Native viewport capture: [curve-intersect.png](../artifacts/curve-intersect-review/curve-intersect.png).

Geometry, preview, disconnected branches, error cases, save/open and undo/redo checks:

```powershell
dotnet run --project tests/Prism.CurveIntersectTests -c Release
```

