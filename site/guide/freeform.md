# Freeform

Freeform modeling shapes a smooth subdivision surface by moving a coarse cage of faces, edges and vertices. It is the quickest way to reach organic product shapes. Convert the result to NURBS to continue with exact CAD tools. Reference: [Freeform ribbon](/tools/freeform).

<GuideImage src="/images/guides/freeform-bottle-vp.png" alt="A smooth blue bottle with an integrated handle in the Prism viewport" caption="A freeform bottle example: a broad body, narrow neck and curved handle form one flowing shape." :width="664" :height="488" />

## Where each tool lives

| Kind | Acts on | Where |
| --- | --- | --- |
| Create | Nothing yet, or curves | **Create**: Plane, Box, Sphere, Cylinder, Torus, Create Face, Loft, Sweep, Pipe, Extrude Profile, Revolve. Each is its own command with OK and Cancel. |
| Edit | Selected vertices, edges or faces | One editor, **Edit Freeform**. Modify, Loops, Lock and align, Topology and Symmetry are shortcuts into it and are enabled while it is open. |
| Check | The whole form, unchanged | **Check Quality**, **Check Cage** |
| Finish | The whole form, leaving Freeform | **Convert to NURBS**, **Convert fewer faces** |

If you choose components and change them, it is part of Edit Freeform. If it makes a new form, it is a Create command.

<GuideImage image="freeform-cage-editor" />

## Edit a form

1. Select the form and choose **Edit Freeform**.
2. Pick Face, Edge or Vertex mode. Double-click an edge to select its loop, a vertex for a chain through it, or a face for a strip. <kbd>Shift</kbd> adds.
3. Drag the gizmo to move, rotate or scale. Soft selection moves nearby vertices with a smooth, linear or sharp falloff.
4. Use the Modify tools: **Extrude**, **Inset**, **Bevel**, **Slide**, **Subdivide**, **Bridge**, **Fill holes**, **Thicken** and more.
5. Confirm the editor to keep the changes as one step.

**Insert Edge** stays on: each click adds a loop across the quads of the edge you click, previewed in green, until you press <kbd>Esc</kbd>.

<GuideImage image="freeform-soft-selection" />

## Sharp edges and symmetry

**Crease** opens a temporary page with a sharpness value: 0 removes the crease, fractional values give a gradual transition and larger values stay sharp. **Mirror and weld** turns on symmetry across X, Y, Z or radially; topology edits then repeat on the mirrored side.

## Check and convert

**Check Quality** marks poles with three edges (yellow) or five or more (red), n-gons (blue) and triangles (green), shows stretched quads and curvature, and can color the surface by distance to a scan or CAD reference. When the shape is right, **Convert to NURBS** produces an exact body for fillets, holes, shells, simulation and manufacturing. **Convert fewer faces** gives a lighter result.

<GuideImage image="freeform-quality-review" />
