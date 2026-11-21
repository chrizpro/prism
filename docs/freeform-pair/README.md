# Freeform saddle and cantilever chair

Open [the saddle](../../gallery/models/oxblood-saddle.prism) or [the chair](../../gallery/models/petrol-ribbon-chair.prism) in Prism. Both retain editable subdivision cages. The [builder](../../gallery/build_freeform_pair.py) drives the desktop through the stdio MCP bridge. The saddle has a shaped cushion and two Freeform pipe rails. The chair uses one closed quad cage bent along the side silhouette, with extra control rows at the foot and seat transitions.

## Findings from the build

- The first saddle cage passed topology diagnostics even though the upper and lower control rows crossed at the outer nose. Checking the thickness at each control column caught this. The revised cage limits the upper edge drop relative to cushion thickness.
- On the chair, the edit report said 98 faces pointed the opposite way and concluded that the surface folded over itself. The check compares each old face normal to its new normal. This measures rotation from the original box. It does not establish self-intersection. The automation reply now identifies the affected faces and flags a possible fold for inspection.
- Station skins accept sections ordered along a single axis. The chair reverses direction along its length. A closed box cage with rows placed along a centerline and thickness measured along the local normal produced the continuous ribbon. There is room for a path-based ribbon helper if this workflow recurs.
- An initial render session stopped returning status. A fresh isolated session completed both previews at 16 samples without denoising. The source of the status loss was not established.

The [saddle measurements](Saddle-inspection.json) and [chair measurements](Chair-inspection.json) are from the reopened models. Profile bounds describe the smoothed principal body. Cage topology alone does not check geometric collisions. These are shape studies.
