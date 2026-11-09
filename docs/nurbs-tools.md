# NURBS tools (curves and surfaces)

All of these are on the **NURBS** tab and can be driven through the MCP with `run_command` (name in the last column). The scripted checks that exercise them are in `tests/mcp/` (`nurbs_curves.ps1`, `nurbs_surfaces.ps1`, `nurbs_extras.ps1`; they need the app running, and `oc.ps1` is the tiny HTTP helper they use).

## Edit curves
| Tool | What it does | MCP name |
|---|---|---|
| Fillet Curve / Chamfer Curve | Round or cut every corner of a flat curve or sketch (each loop separately). Open curves keep their ends. | `curve_fillet`, `curve_chamfer` |
| Offset Curve | Parallel copy of a flat curve, rounded or sharp corners | `curve_offset` |
| Blend Curve | Joins the nearest ends of two curves with position (G0), tangent (G1) or curvature (G2) continuity, with a bulge at each end | `curve_blend` |
| Join / Explode | Join curves that touch end to end; break a curve or sketch into segments | `curve_join`, `curve_explode` |
| Trim / Split / Extend Curve | Keep part of a curve, cut it at percentages, or lengthen it along its tangent | `curve_trim`, `curve_split`, `curve_extend` |
| Rebuild Curve | Redraw through N evenly spaced points; reports the largest deviation | `curve_rebuild` |
| Divide | Points at equal distances along a curve; reports its length | `curve_divide` |

## Curves from surfaces
| Tool | What it does | MCP name |
|---|---|---|
| Project | Onto a surface along a direction (hits every surface in the way) or pulled to the nearest point | `curve_project` |
| Draw on Surface | Click points on any surface; the curve follows the surface, over edges onto neighbouring faces ([guide](modeling/sketches/README.md#drawing-directly-on-a-surface)) | `curve_on_surface` |
| Isocurves | Constant U or V curves, one at a position or several spread evenly | `curve_isocurve` |
| Intersect | Curves where two surfaces or bodies cross | `curve_intersect_surfaces` |
| Contour | Evenly spaced section curves through a body | `curve_contour` |
| Duplicate Edge | Copy picked edges as curves | `curve_duplicate_edge` |
| Curvature Graph | Comb of hairs along a curve; reports tightest and typical radius (use `commit:false` through the MCP to read the rows) | `curve_curvature` |

## Surfaces
| Tool | What it does | MCP name |
|---|---|---|
| Edge Surface | Surface across 2 (ruled), 3 or 4 boundary curves, in any order or direction | `edge_surface` |
| Blend Surface | Surface between an edge of one surface and an edge of another with G0 / G1 / G2 continuity (G2 is exact for flat faces) | `blend_surface` |
| Pipe | Round (optionally tapered) pipe along a curve, solid or surface | `pipe` |
| Sweep 2 Rails | A profile carried along two rails | `sweep_2_rails` |
| Network Surface | A surface inside a closed outline that also passes close to curves running across it (the outline is exact, the inner curves are followed by points sampled along them) | `network_surface` |
| Rebuild Surface | One fitted patch through an even U x V grid; reports the deviation | `rebuild_surface` |
| Variable Fillet | Fillet has an "End radius" box: the radius changes along each edge | `fillet` |

## Shape tools
| Tool | What it does | MCP name |
|---|---|---|
| Deform | Bend, Twist, Taper or Stretch single-patch surfaces and curves along an axis, over a chosen region (adds detail first so the result stays smooth). Bend gives an exact arc for strips. | `deform` |
| Edit Points gumball | Edit Control Points now has the Freeform gizmo: arrows move, rings rotate (E), blocks scale (R), W for move; soft influence radius still applies. Shift-click always adds a point to the selection, even under the gizmo. | `edit_points` |

## Not done yet (ask first)
Flow along a surface, cage edit, loft end tangency, rail revolve, and a fillet between two separate (unsewn) surfaces (sew them first). Network Surface asks for the outline and the inner curves separately; it does not yet work out the outline from a grid of crossing curves.
