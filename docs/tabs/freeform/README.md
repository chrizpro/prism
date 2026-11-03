# Freeform tab

The most common tools have their own buttons. The next most common sit in a dropdown or a small group. **Edit Freeform** holds the rest, so nothing appears in both places.

## Ribbon

- **Select, Datum, References, Curves:** shared sections
- **Create:** Plane / Box / Sphere / Cylinder / Torus, Create Face, Loft / Sweep / Pipe / Extrude Profile / Revolve
- **Modify:** Edit Freeform, Extrude, Slide, Subdivide, More (Bevel, Delete, Split face, Smooth, Flatten, Fill holes, Thicken)
- **Edit curves:** Join, Trim, Extend, Rebuild, Blend
- **Loops:** Insert Edge, Inset, Select Loop, Bridge
- **Lock and align:** Freeze, Align to Curve
- **Topology:** Smooth surface, Weld, Crease, Merge, Unweld edges, Pull, Flip normals
- **Symmetry:** Mirror and weld
- **Quality:** Check Quality
- **Finish:** Convert to NURBS, Convert fewer faces
- **Transform, Inspect, Appearance:** shared sections

## One tool, several jobs

Each of these reads the selection and does what fits it.

| Tool | What it does |
|---|---|
| Merge | Faces join into one. Edges are removed, joining the faces either side. |
| Weld | Points weld into one. Edges and faces collapse to points. |
| Freeze | Locks the selection. On a frozen selection it unlocks it. With nothing selected it unlocks everything. |
| Bevel | Flat cuts the corner off. Tight adds support loops either side. |
| Smooth | Relaxes points toward their neighbours. With Keep moved points on the scan on, it keeps them on the scan. |
| Crease | Sharpens edges or points. Zero removes it. |

## Edit Freeform panel

The panel keeps what the ribbon has no room for, and only shows what fits the selection mode:

- **Selection:** the mode, position, Grow, Shrink, and Select by (angle, open edges, creased edges) with Select matching
- **Soft selection and Live symmetry**, with Place symmetry when symmetry is on
- **Amounts for ribbon tools:** extrude distance, bevel type and width, slide amount, inset, loop count (while Insert Edge is on)
- **Smoothness and display:** smoothness level, mark poles, show triangles
- **Scan tools**, when the document has a scan

Invert and Select All come from **Invert** in the Select section, and undo is Ctrl+Z.

## Feature requirement reviews

- [Extrude Profile](extrude.md) — draft owner-review specification, linked to [Story #8](https://github.com/chrizpro/prism/issues/8). Distinct from **Modify → Extrude** on a selected cage; that operation needs a separate behavior review.
