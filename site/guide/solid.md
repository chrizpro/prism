# Solid modeling

Solid modeling builds exact, closed bodies from sketches and features. Every feature is a step in the design history that you can reopen and change. Reference: [Home](/tools/home), [Solid](/tools/solid), [Direct Modeling](/tools/direct-modeling), [Sheet Metal](/tools/sheet-metal).

## How a command works

Every modeling command opens a panel on the right with numbered, collapsible steps.

1. **Pick inputs.** Each step lists what it needs, such as a profile, edges or faces. Anything you selected before starting fills the matching input.
2. **Set options.** Numbers, choices and switches update the preview as you type.
3. **Check the preview** in the viewport. A command's grab handle can be dragged, or type a number while it shows: <kbd>Enter</kbd> applies, <kbd>Tab</kbd> switches handle and <kbd>Esc</kbd> cancels.
4. **Confirm** to apply, or cancel to leave the model unchanged.

Hover a ribbon button to see what it does. Extrude, Revolve, Loft, Sweep, Fillet, Chamfer and several CAE and CAM tools show an illustrated example; press <kbd>F1</kbd> for the same help on the active tool.

## Create

- **Primitives:** Box, Cylinder, Sphere, Cone and Torus share one split button that remembers the last one used.
- **Extrude** gives a profile depth. **Extent** can be a distance, two sides, symmetric, up to an object, or through all. Add a taper angle, and choose whether the result is a new body or joins, cuts or intersects the target.
- **Revolve** turns a profile about an axis: **Profile → Axis → Extent → Result**. Choose a world axis or pick an axis line.
- **Rib** and **Emboss** add material from a sketched line or text.

Extrude and Revolve offer **Selection intent** in the profile picker: the whole profile, single edges, connected or tangent chains, or a partial loop. A solid extrusion needs a closed profile; open chains can be extruded as surfaces with **Surface › Extrude**.

## Modify

- **Fillet** rounds edges; give an end radius to vary it along each edge. **Chamfer** bevels them.
- **Hole** drills plain or modeled threaded holes (M3 to M12 presets or custom pitch), blind or through all, from a face or the top of a body.
- **Shell** hollows a body, removing the faces you pick. **Draft** tilts faces for molding. **Split Body** cuts a body with a plane, face or another body.

## Combine, pattern and transform

**Union**, **Subtract** and **Intersect** combine bodies. Patterns copy features or bodies **Linear**, **Circular**, **Along Curve**, to **Fill** a region, or from a **Table** of positions. **Move** stays visible; Rotate, Scale, Mirror and Duplicate share one menu.

## Change your design

Double-click a step in the **Design history** (or right-click it and choose **Edit Feature**) to roll the model back to that step, reopen it with the same picks and numbers, and rebuild the later steps when you confirm.

For parts without history, such as imported STEP files, use **Solid › Direct Edit**. Click a face, hole, slot, pocket, boss or fillet and drag the edit gizmo: arrows move, the orange handle pulls along the normal, the purple arc tilts a flat face and the teal rim changes a radius. **Finish Editing** goes back. To remove a hole, fillet or boss, select it and press <kbd>Delete</kbd>.

## Sheet metal

1. Sketch the cross-section as an open chain of straight lines with sharp corners.
2. **Sheet Metal › Flange**: set thickness, inside bend radius, width and which side the metal lies on. Corners become bends.
3. Add cut-outs with Extrude, Hole, Fillet and Chamfer.
4. **Flat Pattern** unfolds bodies whose bends are parallel, with a bend line per bend. Save it as DXF for laser or waterjet cutting.

## Drawings

**Analyze › Make Drawing** creates an engineering drawing with hidden-line views, automatic scale, overall dimensions, hole callouts, centerlines, an optional section and a title block, saved as SVG, PDF or DXF.
