---
name: prism-freeform
description: How to model organic and product shapes in Prism's Freeform (subdivision) tools through MCP or cadkit, and how to check the result without guessing. Use before building anything curved: bottles, handles, housings, irons, mice, vehicles, characters.
---

# Modeling Freeform shapes in Prism

Read this before building a Freeform. Everything is in millimetres, Z up. `form_*` tools use **0-based** cage indices; solid face/edge numbers elsewhere are 1-based.

The main lesson: **describe proportions, not vertices.** An agent that moves cage points by index builds blind and gets lumps. An agent that states cross-sections and silhouettes, then measures what came out, gets a shape it can correct. The tools below exist for that loop.

## The loop

1. **Decide the parts and the numbers.** Write down overall size (L x W x H), the long axis of each part, and a few key heights/widths. A steam iron is a soleplate, a shell, a handle, a dial and a button, not one blob. Parts that overlap slightly read better than parts that meet exactly.
2. **Build each part from a description**, in one `run_script`:
   - A body, bottle, hull or limb: `cad.form_from_silhouettes(first, second, axis=..., ...)` from two outlines, or `cad.form_skin(stations, axis=...)` from a list of sections.
   - A tube, handle or cable: `cad.form_pipe(path_curve, ...)` along `cad.run("curve_through", points=[...])`.
   - A knob, button, cap: `cad.form_cylinder(...)`, `cad.form_box(...)`.
3. **Measure, then look.** After each part: `cad.profile(obj)` (text, cheap, exact) and, every few steps, a picture: call `screenshot` with `views:["front","right","top","iso"]` and `object:[...]` (see below). Judge the picture against your numbers.
4. **Correct with numbers.** Change a station or outline and rebuild (delete the old object first), or nudge the cage with `form_op`. Do not accept a shape you have not measured.
5. **Finish.** Names, materials, `cad.save(path)`. Convert to NURBS (`cad.form_to_nurbs`) only at the end and only if you need solids or STEP; keep the Freeform too.

## Describing a shape

### Stations: `cad.form_skin`

```python
bottle = cad.form_skin([
    {"at": 0,   "width": 60, "depth": 40, "exponent": 3.2},
    {"at": 70,  "width": 64, "depth": 42, "exponent": 3.2},
    {"at": 110, "width": 40, "depth": 30, "exponent": 2.6},
    {"at": 130, "width": 22, "depth": 22, "exponent": 2.0},
    {"at": 160, "width": 24, "depth": 24, "exponent": 2.0},
], axis="Z", points=24, name="Bottle", symmetry="x")
```

- `at` is the position along `axis`. `width` and `depth` are the full sizes of the section along its two plane axes (axis Z: x then y; axis X: y then z; axis Y: x then z). `dx`/`dy` shift the section centre, which is how a bent or off-centre shape is made.
- `exponent` is the corner shape: 2 an ellipse, 3 to 4 a soft rounded box, 5+ a nearly flat slab with tight corners. Vary it along the part.
- `points` is per section: **keep it the same on every station, and a multiple of 4**. Use 24 to 32. Too few points show as vertical flutes in the smooth surface. Or give a station an `outline` (closed list of (u, v) points) for any shape.
- Put stations closer together where the shape changes fast (shoulders, necks, tips) and let them be sparse on straight runs. 10 to 18 stations is usually plenty.
- Two stations may not share the same `at`. Keep every width/depth above about 0.2 mm; a tip is a small section, not zero.

### Silhouettes: `cad.form_from_silhouettes`

When you can state the outline of a part from two sides, give those outlines and let the tool derive the stations, build, and **correct itself against the real surface** (the smooth surface pulls in from the cage, so a first build comes out a few percent small; `refine=1` fixes that and prints the worst remaining error).

```python
# long axis X (an iron lying along x). Outlines are points (across, along):
top  = [(half_width, x), ..., (-half_width, x)]   # top view as (y, x), closed
side = [(z_top, x), ..., (z_bottom, x)]            # side view as (z, x), closed
shell = cad.form_from_silhouettes(top, side, axis="X", count=18, exponent=2.8, name="Shell")
```

Axis Z: first is the front view (x, z), second the side view (y, z). If you drew (x, y), use `cad.swap_uv(points)`. Outlines can come from SVG (below). Keep outlines closed, in world mm, and overlapping along the axis.

### SVG and outlines

`cad.svg_outlines(svg_text_or_path, fit_width=..., center=True)` returns outlines (corners are respected: a sharp corner splits the outline into runs so splines do not round it off). `cad.svg_to_sketch(svg, plane="XY", fit_width=240)` draws them as a sketch, ready for extrude, loft or sweep. An SVG is the cheapest way to state a curved silhouette or a logo: write the path, scale it with `fit_width`, use it. `svg_outlines(...)[0].points` feeds `form_from_silhouettes` (swap with `swap_uv` if needed). Basic shapes, paths (all commands, arcs included) and nested transforms are read; text, images and gradients are ignored.

## Checking the result

### Text: `cad.profile(obj, ...)`

Prints a table of cross-sections along an axis and ASCII silhouettes (front, right, top), and returns the raw numbers. Each section row gives the min/max extent along the two other axes, the area, the centroid and `outlines` (more than 1 means the cut splits into pieces, as it should through a handle; an unexpected 2 means a gap). `empty` means the shape does not reach that offset.

- `print(obj)` shows the **cage** size. `profile` measures the **smooth surface**, which is what you see and what you export. Trust `profile`.
- Use `axis=` along each part's length, `count=` for how many cuts, `at=[...]` for exact offsets (for example your station positions), `views=()` to skip pictures, `quiet=True` to get numbers without printing.
- `run_script` output is cut at 8,000 characters. Print summaries, not whole cages.

### Pictures: `screenshot` with `views`

Through `call_api`, `screenshot {views:["front","right","top","iso"], object:["Shell","Handle"], cell_width:420}` returns one labelled sheet: orthographic views at one scale (the reply gives `pixelsPerMm`), only the named objects, no grid, shaded. It restores the camera and visibility afterwards. Images only come back through `call_api` and MCP tool results, not from `run_script`, so look after the script, not inside it. Add `display:"shaded_edges"` or `"wireframe"` to see the cage. Compare silhouette and proportions against the numbers you wrote down in step 1.

### Edit replies

`form_op` returns what changed, not only that the cage is valid: `created` (inclusive index ranges of appended vertices/faces), `bounds`, `sizeChange`, `moved` (count, farthest distance and vertex), and `warnings`. Read the warnings: `flippedFaces` counts faces whose normals turned substantially. Inspect the faces in `flippedFaceIds` using `form_info` and a viewport image. Intentional bending can also turn normals. "stretched faces" means insert a loop or even out spacing. "no longer closed" means a hole appeared: `fill_holes`.

## Without scripting

If `run_script` is not available (the in-app assistant with file access off), the same loop works with tools: sketch each section with `sketch_begin` / `sketch_add` (a closed `spline` or `polyline`) on offset planes, `run_command form_loft` with the sections in order, then `solid_profile` and `screenshot` with `views`. The cadkit helpers are Python-only; `solid_profile`, `screenshot` views and the `form_*` tools work anywhere.

## Editing a cage when you must

- Find, don't enumerate: `form_find` by `box`, `near`+`radius`, `facing`+`angle`, `open`. Never read the whole cage with `form_info` unless it is tiny; use `summary:true` or `only:[...]`.
- Prefer region edits with falloff: `form_op move` with `soft_radius` and `falloff:"smooth"`. Prefer `scale` and `rotate` of a selection over setting points one by one.
- Use `set_symmetry` for left/right parts so edits mirror. Use `insert_loop` to add detail only where needed, `crease` for deliberate hard edges (a sharp edge needs a crease or a tight pair of loops), `level` to control smoothing (3 is the default).
- A flattened handle or limb: make a round pipe, then `form_op scale` with `scale:[1,1.5,1]` and `all:true`.

## Failure modes and what to do

| You see | Cause | Do |
|---|---|---|
| Vertical flutes or faceting | too few section points, or an exponent that makes corners tight | `points=32`, lower the exponent a little |
| Part too small, or all parts slightly off | smooth surface pulls in from the cage | use `form_from_silhouettes` (corrects), or scale stations by target / measured |
| Surface ends 1 to 3 mm short of the outline | the outermost stations sit 1% inside it and the cap rounds off | lengthen the outline by that much, or pass `margin=0.002` |
| Twisted or pinched loft | different point counts or seam start per section | same `points` on all stations; use `form_skin`, which starts every section at +u |
| Handle detached or floating | ends exactly on the surface | start and end the path inside the body |
| Everything looks lumpy | edits done by single vertex | rebuild from stations; edit with soft selection |
| Hundreds of leftover sketches | helper sketches kept | `form_skin` deletes its section sketches; delete your own |

## Habits that pay

- One `run_script` per part with everything inside; wrap a multi-part build in `with cad.history("Iron"):` so one Undo removes it.
- Name every object. Hide or delete construction curves.
- Build, measure, fix: at most two corrections per part, then move on. Finish all parts coarsely before polishing one.
- Say what you measured when reporting. "Soleplate 274 x 112 x 10.6 mm (target 280 x 112 x 11)" is evidence; "looks good" is not.
- Examples: `Prism/Scripting/examples/07_freeform_iron_from_silhouettes.py` builds a steam iron this way.
