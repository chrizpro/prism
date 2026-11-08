# Prism automation / MCP

`app_close` requests a normal desktop close and respects unsaved-document prompts. If Prism remains open, complete or cancel its save review; the bridge never force-kills it. See [saving, closing and recovery](interface/document-safety/README.md).

Prism can be driven by an AI assistant (or any script) through the Model Context Protocol.

```
Claude Code  <--stdio MCP-->  Prism.Mcp (bridge)  <--HTTP 127.0.0.1 + token-->  Prism app (AutomationServer -> AutomationApi)
```

* **App side** (`Prism/Automation`): on startup the desktop app opens a localhost-only endpoint and writes
  `%LOCALAPPDATA%\Prism\automation.json` (port, random token, pid). Tools run on the UI thread through the same
  session/command code as the mouse, so everything is undoable and looks identical to interactive work.
  Set `PRISM_AUTOMATION=0` to disable. Requests without the token, or carrying an `Origin` header (browsers), get 403.
* **Bridge** (`Prism.Mcp`): speaks MCP over stdio, fetches the tool catalogue from the app (cached for offline listing)
  and auto-launches the app on the first call if it is not running. Override the exe with `PRISM_EXE`.

The [desktop integration project](../Prism.Tests/Integration/README.md) exercises this bridge and the application API together and saves viewport evidence.

The bridge's compact catalogue exposes `describe_api`, `call_api` and `run_script`. Use `describe_api(names=["solid_check"])` for a tool's schema, then pass its name and arguments to `call_api`. Discover modeling command IDs with `list_commands` and inspect their slots and parameter choices with `run_command` and `commit:false`.

For an isolated review instance, set `PRISM_AUTOMATION_DISCOVERY` to the same separate absolute JSON-file path in the bridge and app. The normal discovery file stays unchanged. `run_command` also supports `refold`: select an original cylindrical flat pattern and the curves/sketches drawn on it. `object_info`/`get_state` include `flatPattern` dimensions and refoldability. See [Flatten and Refold](flatten-refold.md).

## Setup
1. Build the app once (`native/build.ps1`, then build `Prism.Desktop`).
2. `dotnet build Prism.Mcp -c Release`
3. Claude Code picks up `.mcp.json` in the repo root (approve it when prompted; `/mcp` shows status).

For end users, **Settings → AI Assistant → MCP** generates the same configuration for Claude (`claude_desktop_config.json` / `.mcp.json`) and Codex (`~/.codex/config.toml`): **Set up** writes it into the client config with a `.bak` backup, **Copy** puts it on the clipboard. See [AI assistant](interface/assistant/README.md#mcp-client-setup).

## Tools (all sizes mm, Z up; face/edge indices are 1-based like the UI labels)
| Tool | Purpose |
|---|---|
| `get_state`, `get_log`, `object_info` | Inspect objects, selection, active command + parameters, sketch, camera, status messages |
| `screenshot` | PNG of the whole window (ribbon, panels, viewport) |
| `list_ribbon`, `ribbon_click` | Click any ribbon button by label (optionally `tab`) |
| `run_command` | Start + fill + commit any modeling command (box … loft, fillet, booleans, transforms, surfaces, nurbs_patch, curves) |
| `command_set`, `command_commit`, `command_cancel` | Work with a command left open for preview |
| `solid_find` | Find faces or edges of a solid by geometry rather than by index: `type` (Plane, Cylinder, Line, Circle…), `facing`+`angle`, `direction`+`angle`, `radius`, `box`, `near`+`distance`, `of_face` (edges bounding a face), `min_size`/`max_size`, `sort` (`size`, `x`, `y`, `z`, prefix `-` for descending), `limit`. `select:true` selects the hits; `summary:true` returns only counts per type. Hits are 1-based and can be used directly in `run_command` slots. |
| `solid_check` | With `object` alone, check validity, volume, area, bounds, centre of mass and topology. With `a` and `b`, measure minimum distance and closest points between objects, faces or edges. Use `object` as a shorthand for the first body when only `b` is given. Whole solid pairs can also report overlap volume. |
| `sketch_begin`, `sketch_add`, `sketch_finish` | Programmatic sketching (line, polyline, rect, circle, arc, spline) on XY/XZ/YZ or a planar face |
| `get_control_points`, `set_control_points` | Read/write NURBS control nets for freeform edits |
| `select`, `object_update`, `delete_objects` | Selection, rename/recolor/hide, delete |
| `view`, `history`, `file`, `batch` | Camera/display, undo/redo, new/open/save/import/export, multi-step calls in one round trip |

`run_command` and `command_commit` report what the command did: `created` bodies, `changed` bodies (`volumeDelta`, `areaDelta`, `faces`/`edges` as `before->after`, `size`, `valid`), `removed` names and a `warning` if a result is invalid. A failure names the command, the reason, its slots (with how many items each holds) and its parameters. Items in `select` and slots can also be found by position: `{object, face_at:[x,y,z]}` / `{object, edge_at:[x,y,z]}` pick the face or edge nearest a point, so they survive index changes after booleans.

`view` accepts `display` as `shaded`, `shaded_edges`, or `wireframe`, and `xray` as a separate boolean. `get_state` reports both settings. Shaded + edges adds edges to shaded faces; X-ray makes shaded faces translucent and can be combined with either shaded style. Wireframe shows edges without faces.

The [SFC-I001 integration scenario](../Prism.Tests/Integration/DesktopModelingTests.cs) checks surface area and measures a known offset through MCP using both distance argument forms. Verified on Windows on 2026-11-04 with `dotnet test Prism.Tests/Integration/Prism.IntegrationTests.csproj -c Release --filter TestId=SFC-I001`.

### More tools
| Tool | Purpose |
|---|---|
| `sketch_input` | Drives the open 2D sketch like the mouse and keyboard (pick a tool, send `move/down/up/click/dclick/drag/path` at sketch coordinates, or `key` events such as `Char:5`, `Tab`, `Enter`, `Char:L`). Returns the on-screen read-out labels, the status line and the sketch state |
| `pointer` | Mouse and key events for the 3D viewport at world positions (3D sketch, command grab handles, tools). Returns the camera and the active command's grab handle |
| `wait_idle` | Waits until the view has stopped animating, so the next pointer event or screenshot sees the final camera |
| `sketch_constrain`, `sketch_dimension`, `sketch_project`, `sketch_mirror`, `sketch_trim`, `sketch_pattern`, `sketch_delete` | Constraints, dimensions and sketch edits |
| `form_info`, `form_find`, `form_analyze`, `form_op`, `form_select`, `form_to_nurbs` | Freeform (subdivision) cages: inspect, edit, select, convert to NURBS. `form_info` takes `summary` (counts and bounding box) or `only:[vertex indices]`. `form_find` picks vertices, faces or edges by geometry (`box`, `near`+`radius`, `facing`+`angle`, `open`, `all`) so no one has to read the whole cage; `form_select` and `form_op` accept `all:true`; `form_select` also takes `op` (grow, shrink, invert, all, angle, boundary, creased). A form with a persistent symmetry repeats topology edits on the mirror images of the selection. `form_analyze` reports poles, triangles, n-gons, stretched quads, curvature and, with `reference`, the deviation (`display:true` colors the form). `form_to_nurbs` takes `detail: fewer` for a lighter solid. `form_op` ops: move, rotate (`axis`, `angle`, `pivot`), scale (`factor` or `scale:[sx,sy,sz]`, `pivot`), set_vertices, extrude_faces, extrude_edges, inset (`faces`, `amount`, `depth`, `individual`), bevel (`edges` or `faces`, `amount`), insert_loop (`edge`, `count`, `position`), thicken (`amount`), fill_holes, merge_faces (`faces`), collapse_edges (`edges` or `faces`), split_face (`faces:[f]`, `vertices:[a,b]`), set_symmetry (`symmetry` x/y/z/radial:N/off, `enforce`), chamfer (`edges` or `faces`, `amount`), slide (`edges`, `amount` -1..1), crease_vertices (`vertices`, `value`), merge_edges (`edges`), unweld_edges (`edges` or `faces`), pull (`vertices` or all, `strength`), smooth (`vertices` or all, `strength`, `passes`), flatten (`vertices`), subdivide, delete_faces, weld, flip, crease, mirror, add_face, level. move/rotate/scale/set_vertices also accept `soft_radius` + `falloff` (smooth, linear, sharp) and `symmetry` (x, y, z, `radial:N`). |
| `material_list`, `material_apply` | Library materials (`material_list` is short by default; `brief:false` for the full data) |
| `render_control` | Poll render progress and save the finished PNG. Set the camera with `view`, call `wait_idle`, then open `run_command` with `name:"render", commit:false`. Set render options and run `Start render` through `command_set`. Poll `status` for `finished` or `error` before calling `save` with a PNG path. |
| `render_setup` | Read/change the document render setup without opening the render panel: `state`, `set` (`background` none/white/transparent/environment/image, `environment` studio/sky, `exposure`, `hdr` rotate/intensity, `denoise`, `camera` preset), `set_background` (PNG/JPG/BMP/WEBP path, `fit` fill/fit/stretch), `clear_background`. See [Product rendering](rendering/README.md). |
| `script_editor` | Drive the in-app Script Editor: `state`, `get`, `set` (replaces the whole script and records a rollback snapshot), `insert` (before a 1-based `line`, or at the end), `rollback` (restores the last AI edit), `new`, `open`, `save`, `run`. See [Script](interface/script/README.md). |
| `settings` | Inspect and change safe preferences: `list`, `get`, `set`, `reset`, `open` (opens the Settings window, optional `page`). Keys come from `list`; secrets (API keys, tokens) are never exposed. Assistant `provider`, `model` and `effort` are included. See [Settings](interface/settings/README.md). |
| `document_new`, `document_list`, `document_switch`, `document_close` | Work in several document tabs |
| `study_new`, `study_run`, `study_state` | Simulation studies |

## Prompts (ready-made test scenarios)
The bridge offers MCP prompts so you can ask for a scenario by name (in Claude Code they appear as `/mcp__prism__build_freeform_model` and so on):
`build_solid_part`, `build_lofted_surface`, `build_freeform_model`, `test_2d_sketcher`, `test_3d_sketch`, `bug_hunt`.

## Good to know
* Slot and parameter labels in `run_command` match by partial, case-insensitive text; objects can be named by name or id prefix.
* Wrong or missing arguments produce a message that lists what the tool accepts; unknown tool names get "did you mean".
* The app rewrites `%LOCALAPPDATA%\Prism\mcp-tools-cache.json` on every start, so listing tools works even before the app is running.
* **Restricted assistant endpoint:** the in-app AI assistant calls the same `/tools` and `/call` routes through a separate assistant token advertised in `assistant-automation.json` (see `AutomationServer.AssistantDiscoveryPath`). When *Let the assistant open, save and export files* is off, the restricted catalogue omits `file`, `mesh_export`, `mesh_import` and `drawing_export`, the bridge drops `run_script`, `/script` is refused, and `/call` rejects the write-capable actions `script_editor` `run`/`save`, `render_control` `save` and `render_setup` `set_background` — including inside `batch` calls — so code cannot write files with the user's permissions. `describe_api`/`call_api` stay available (they reflect the restricted catalogue). External MCP clients using the normal token keep the full catalogue.
* Commands seed empty selection slots from the current selection.
* Replies are short by default: `sketch_add` / `sketch_finish` return a status and entity list (pass `verbose:true` for the full sketch or document state), `object_update` returns the object's name, kind, visibility and colour, and `form_info` accepts `summary:true` (counts and bounding box only).
* Numbers and booleans sent as text ("5", "true") are accepted.
* A very slow kernel operation (for example a large plate-surface fill) blocks the app's UI thread, so other calls wait until it finishes; the bridge gives up after 3 minutes.

## Calling the app from a script (no MCP needed)
The endpoint is plain HTTP + JSON, so any language can use it. Python needs only the standard library:

```python
import json, os, urllib.request
d = json.load(open(os.path.expandvars(r"%LOCALAPPDATA%\Prism\automation.json")))
def call(tool, **args):
    req = urllib.request.Request(f"http://127.0.0.1:{d['port']}/call", method="POST",
        data=json.dumps({"tool": tool, "args": args}).encode(),
        headers={"X-Prism-Token": d["token"], "Content-Type": "application/json"})
    r = json.load(urllib.request.urlopen(req))
    if not r["ok"]: raise RuntimeError(r["error"])
    return r["result"]

call("run_command", name="box", params={"Width": 40, "Depth": 30, "Height": 20})
print(call("get_state")["objects"])
```

A friendlier `prism` Python package (functions like `box()`, `sketch()`, `loft()`, plus a script console in the app) is the planned next step; a C# client can follow the same protocol later.


Freeform generators (run_command): `form_loft` (sections), `form_sweep` (profile + path), `form_pipe` (path, start/end radius, sides), `form_extrude` (profile, distance, direction, end scale), `form_revolve` (profile, axis X/Y/Z, angle). All create an editable Freeform cage.

## Drawings
`drawing_export` writes an engineering drawing as SVG: hidden-line-removed orthographic views (third-angle by default; `first_angle:true` to swap), automatic standard scale, overall dimensions, title block (`title`, `part_number`, `material`, `drawn_by`, `company`, `revision`, `notes`), sheet sizes A4-A0, Letter, Tabloid, ANSI-C/D. Example: `drawing_export {path:"part.svg", views:["front","top","right","iso"], sheet:"A3"}`. Sample output: docs/drawing_test.svg. Kernel side: `ck_hlr` (OCCT HLRBRep) in native/kernel.cpp, `Occ.Hlr`, model and writer in Prism/Core/Drawing/DrawingSheet.cs.

### Editable drawing workspace

`workspace {id:"drawing"}` opens the sheet editor. `drawing_document` edits the persisted drawing through the same session/history services as the desktop UI: `state`, `new_sheet`, `sheet_settings`, `add_view`, `suggest`, `accept`, `move`, `update`, `parts`, `delete_annotation`, `export`. Base-view sources accept body names or IDs; returned sheets/views/annotations use full GUIDs. Projected orientations are `projected:above`, `projected:below`, `projected:left`, `projected:right` with a `parent_id`. Suggestions remain optional until `accept`; export supports all-sheet PDF and current-sheet SVG/DXF. See the [canonical Drawing guide](drawing/README.md) for workflow, limits and maintained tests. Legacy `drawing_export` keeps its existing generated-export behavior.

### Drawings (update)
`drawing_export` now saves `.svg`, `.pdf` or `.dxf` (by extension) and supports: automatic hole/boss callouts (`4X Ø8 THRU`, depth), centerlines and center marks, hole position dimensions, `sections:[{parent,direction,offset}]` (hatched, lettered A-A with cutting line), `details:[{parent,center,radius,ratio}]`, `annotations:[...]` (dimension with tolerance, diameter, radius, note, balloon, datum, gdt frames with 14 symbols, surface finish), BOM table (several bodies), `units:"in"`, `first_angle`, general tolerance/notes. In the app: Analyze > Make Drawing (sheet, views, one section, options, Create drawing). Samples: docs/drawing_test.svg / .pdf / .dxf.

### Deeper feature tools
- **extrude** (and extrude surface): `Extent` = Distance | Two sides | Symmetric | To object | Through all | Through all, both ways; `Distance`, `Distance 2 (other side)`, `Taper angle`, `Extend to` slot (face, plane or body) with `Stop at` (Near side / Far side) and `Offset from the target`, `Reverse direction`.
- **revolve**: `Extent` = Angle | Full 360° | Two sides | Symmetric; `Angle`, `Angle 2 (other side)`, `Reverse direction`; angle handle follows the swept arc.
- **sweep**: `Profile orientation` (Follow the path | Keep parallel | Keep upright), `Scale at the end`.
- **loft**: `Guide rails` slot (one or two curves), `Close the loft`, `Guide rail accuracy`.
- Viewport: with a command's grab handle showing, type a number (digits, `.`, `-`), Enter applies, Tab switches handle (two-sided), Esc cancels. `pointer` key events `Char:4`, `Enter`, `Tab` drive it.

### Surface tools added
`unstitch` (shown as Unsew: faces or whole surfaces/solids into separate surfaces), `untrim` (`What to restore`: the whole natural surface | fill the holes only), `extend_surface` (`Extend by`; slots `Surface or face`, `Edge to extend`), `reverse_normal`. Construction plane methods added: Tangent to a round face, Through two straight edges, Through a flat curve or sketch. Normal to a curve defaults to a single plane at the start (0 %, draggable along the curve in the viewport with the translate handle). New documents and sessions open in Shaded display (no edge outlines); `view display:shaded_edges` restores them.

### Sheet metal (essentials)
`contour_flange` (slot `Profile sketch`: an open chain of straight lines with sharp corners; params `Thickness`, `Bend radius (inside)`, `The metal lies` Left/Right of the profile, `Width`, centred, reverse) builds bent sheet metal with rounded bends. `flat_pattern` (slot `Sheet metal body`, `K-factor`, place beside the part) unfolds bodies whose bends are all parallel into a flat surface with holes plus a bend line per bend; the panel action "Save the flat pattern as DXF" writes Documents\Prism\Drawings\<name>_flat.dxf (outline continuous, bends centre-line style). Ribbon: Sheet Metal tab (Design workspace).

`fold` (slot `Flat sketch (outline and bend lines)`: outline as normal lines, holes as closed loops inside it, each bend as a construction line across the sheet; params `Thickness`, `Bend radius (inside)`, `K-factor`, `Bend angle` degrees, `Angle of each bend (optional)` signed degrees in order e.g. `90, -90, 180`, `Radius of each bend (optional)` inside radii in order e.g. `3, 80, 3`, `The metal lies` on the normal side or opposite, `Also make the flat pattern beside the part` (default on: a surface with its holes plus a `Bend lines` curve), action `Save the flat pattern as DXF`) folds the flat sheet along its bend lines. Each bend line is the middle of a zone as wide as the bend allowance θ·(r + K·t); the zone becomes a cylindrical strip, the flat regions between stay rigid, and the largest region stays in place. It makes parts bent in several directions; keep holes and notches clear of the zones. The sketch is the part's blank. The result is the folded solid plus, by default, the flat pattern. Errors name the cause: no construction lines, a bend that does not cross the sheet, bends that cross inside the sheet, a zone through a hole or notch, or regions not joined by a bend.

`edge_flange` (slot `Thin end face of the sheet`: the thin end face along the edge to flange, `{object, face}`; params `Flange length` (the straight leg after the bend), `Bend angle` 0.1 to 180 degrees, `Bend radius (inside)`, `Fold toward` Up or Down) adds a bend and flange along the whole edge and replaces the body. Up is the upper face when the sheet is level. The face must be a flat rectangle much longer than it is thick; anything else is refused. cadkit: `cad.edge_flange({"object": plate.name, "face": 4}, length=17, angle=90, bend_radius=3)`.

### Reference pictures
`place_image` (`Image file`, `Place it on` Front/Top/Right plane or a picked plane/face, `Width`, `Opacity`, `Move it back or forward`, mirror), `edit_image` (slot `Picture`: opacity, width, turn, move right/up, mirror), `set_image_scale` (slot `Picture`, then two clicks on the picture with the pointer tool, `Real distance between the two points` and unit `In` mm/cm/m/in/ft; the picture scales about the first point). Pictures are plane objects, behind the model, never exported, saved inside the document. The `pointer` tool takes WORLD positions (x, y, z), not pixels. `script_dialog` shows a dialog for scripts.

### Marking menu
Hold the right mouse button still (about a quarter of a second) over the viewport: eight entries appear around the pointer; drag toward one and let go. A quick right click opens the same menu and you click an entry; moving before the pause still orbits. The entries follow the context (empty space, a face, an edge, a solid, several solids, a sketch, a surface, a curve, a Freeform body, a mesh, a picture, a plane, an open sketch, an open command). A tool that wants the right click (for example ending a line chain) keeps it. The `marking_menu` tool (list | open | run) exposes it for scripts and tests.

### Settings, ribbon customisation, split buttons
The cog next to Redo opens Settings (theme Dark/Light/Match Windows, right-click menu on/off and hold time, ribbon editor). Stored in %LOCALAPPDATA%\Prism\settings.json. Primitives are one split button (icon runs the last one used, name or right-click lists the rest). The marking menu's 6 o'clock entry opens an ordinary menu. Extrude has a Region number for sketches with several shapes.

### In-graphics toolbar, status bar, X-ray
The floating toolbar over the 3D view and the status bar are both customised in Settings > Toolbars (any ribbon command can sit in the toolbar). X-ray (View tab, toolbar, status bar, right-click menu) makes bodies translucent so hidden edges show.

### Workspace and home screen
A workspace is the folder parts are saved in (chosen on the home screen or Settings > Workspace). File dialogs start there, new parts are named Part 1, Part 2... and the home screen lists its parts and recent files. The ribbon sets (Design, Scan & Print...) are now called modes. Set PRISM_NO_HOME=1 to start without the home screen (the MCP launcher does this).

### Grid looks
Settings > Appearance > 3D view backdrop and grid: Classic (follows the theme), Blueprint, Neon, Dusk, Terminal, Dots, Graph paper, Studio. Defined in Rendering/GridPresets.cs; the grid shader reads them.

### Editing features, sketch groups, tool panels
Edit Feature (Home/Solid tabs, History right-click or double-click, right-click menu) rolls the model back to before a step made with a command, reopens it with the same picks and numbers, and on OK rebuilds the later steps (edit_feature tool: list | open). Sketch rectangles, slots and polygons are grouped in the sketch panel. Sketch tools can show their own panel (Offset does). Ellipse has Center / 2-point / 3-point; Arc is 3-point (start, end, bend), Center (sweep follows the pointer) and Tangent.
