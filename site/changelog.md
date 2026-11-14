# Changes

## 0.3.0 alpha

Selecting, grouping and copying; more of what you can do to a surface; and a lot of workflow work across printing, manufacturing, electronics, animation and simulation.

### Selecting, grouping and copying

- **Ctrl+A** selects everything at the current pick level, **Ctrl+C** and **Ctrl+V** copy and paste whole objects (between open documents too), and **Group** (**Ctrl+G**, **Ctrl+Shift+G** to ungroup) puts several objects under one parent row in the browser that hides, shows and selects them together. See [Pick levels, select all and groups](/guide/modeling#pick-levels-select-all-and-groups).
- Hold **Alt** while you drag the **Move** gizmo to drag a copy and leave the original in place.
- A **Vertex** pick level for the corner points of solids, surfaces and curves, with drag-box selection; **Select Same Color, Adjacent and Linked** faces; per-face colors; and **Isolate** that shows only the faces you selected.
- In **Four Views**, hover a pane and press **Space** to maximize it, and again to restore it.
- Picking one curve of a sketch for a profile keeps just that curve, and a feature built from a sketch keeps using the curves it was made from. While you edit a sketch, the rest of the model stays visible as faint context.

### Moving and sizing

- **Scale** can stretch each axis by its own factor (**Per axis**), with an arrow on each face of the selection.
- The shared gizmo now sets position, rotation and scale numbers for primitives, Move Face, Move Hole, Insert Component, Direct Edit and Component Pattern. Sizes stay on drag handles. Rotate shows three rings, and the handle nearest the pointer wins when handles overlap.
- **Hole**: drag the circle in the viewport to set its diameter. **Rib** fills the web between a sketch line and the body (a gusset) by default, with **Fixed height** as an option. Inserting a shape or component no longer shows the move gizmo unless you ask for it.

### Surfaces and NURBS

- **Split Surface** divides a surface along a plane, surface, solid, sketch or curve, and keeps every piece, either as one surface with several faces or as separate surfaces. In Face pick mode you can split just one face. **Delete** removes selected surface faces. See [Split a surface into faces](/guide/surfacing#split-a-surface-into-faces).
- **Draw on Surface** can split the faces it was drawn on, and clicking the first point again closes the curve into a loop. **Fillet / Chamfer** works on the edge between two faces of a surface. **Offset Face**, **Move Face**, **Merge Faces** and **Push / Pull** work on surface faces.
- **Enclosed Body** is on the NURBS ribbon, and **Union**, **Subtract** and **Intersect** are on Surface, NURBS and Sheet Metal as well as Home and Solid. **Subtract** with a surface as the tool now says so instead of reporting success and changing nothing.
- **Rebuild Surface** draws the grid it fits through and updates it as you change the counts; **Edit Control Points** box-selects with a drag in empty space; **Intersection Curve** recognises two picked curves.
- Fixed **Warp** on stored NURBS surfaces, which failed with "Face has no surface." and could close the app on lofted and swept surfaces.

### Printing and manufacturing

- Streamlined **FDM** and **resin** print preparation, with a clearer check and export, and fixed hollowing of closed bodies.
- **Constant-Z Finish** for steep walls, **Subtractive** posting after review, a first CNC pocket exercise, and illustrated tool guides.
- Native **mold copies**, a movable section plane for the mold, and importing a part from a Prism file as a mold reference.

### Electronics, assemblies and animation

- Board placement, fit and clearance checks, mounting holes through a board face, and STEP board exchange.
- The Assemble ribbon is reorganized and keeps poses with rigid joints; joint creation stays on the Joints tab and keeps linkages closed while you create joints and animate.
- Animation keys are precise, the timeline and panel are organized, table parameters can be animated with segment timing and multiple turns, and a reusable curve editor sets custom timing.

### Simulation, mesh and reverse engineering

- Static, Thermal, Dynamics, Fluids and Motion panels group their setup, solving and results controls, and Fluids adds 3D flow setup. Mesh tools are organized and Repair is limited to selected regions. Reverse Engineer routes are clearer, with checked parametric rebuilding.
- Long operations share one progress display in the status bar.

### The application

- Prism checks for updates at startup and offers to clean up old Windows installs; the Windows installer offers to launch Prism when it finishes.
- Saving defaults to Prism's own file type, and **Home → Export As** is new.
- Ribbon section names show in both themes.

## 0.2.0 alpha

Curves, selection and sketching polish, plus new keys and links.

- **Draw on Surface** (Curves group, next to Project): click a few points on a face, surface, freeform form, NURBS surface or mesh and a smooth curve passes through them, over the edge onto the next face. Drag a point to move it, Delete removes it, Esc stops adding points so you can adjust, and Enter or **Make curve** finishes. This replaces the old "draw on the curved surface" option of Create Sketch.
- **Chain selection you can see.** Under Connected chain, Tangent chain and Partial loop, every edge a click would take lights up with a magenta halo, the hovered edge heavier. In Face selection mode a tool that picks edges offers none, while whole curves and sketches (text, outlines) stay pickable in every mode.
- **Loft, Sweep, Boundary, Ruled, Patch, Blend, Fillet and Chamfer, Shell, and Freeform Loft and Sweep** open in perspective at the home view when you start them from an orthographic or straight-on view.
- **View cube**: the projection (orthographic and perspective) and roll buttons work again.
- **Sketching**: Slot has a **From center** option; Mirror has a two-step panel and fills in the line it thinks you mean; a construction curve previews dashed while you draw it; Offset no longer leaves gaps where lines only touch; editing a saved sketch no longer shows its curves twice.
- **Closing** no longer asks to save a document where nothing was created, including after undoing everything or opening a sketch or tool without using it.
- **F** fits the view (Zoom Extents), alongside Home. Command search shows each command's current key, including changed and custom shortcuts, and a new [Keyboard shortcuts](/reference/hotkeys) page lists them all. **Learn More** on a tool's hover help opens that tool's row on its ribbon's page of this site.

## 0.1.0 alpha

- Prism is in alpha: an Alpha badge next to the wordmark opens the known issues. **Help (?) → Report a bug** opens the GitHub form; **Give feedback** is also in Help. Optional exit-feedback reminders appear on closures 1, 5, 15, 25, … . Crash diagnostics are reviewed locally before opening a GitHub draft.
- The AI assistant offers Claude with an API key; Codex can still use your login.
- MIT license, full third-party notices in installers, and the LGPL library sources attached to each release.
- Removed Pipe from the Solid ribbon. Removed Sweep and Loft from the Solid ribbon and the sketch marking menu.
- Shared gizmo styling with active-operation, combined and custom visibility; persistent display preferences, live preview, and move/angle/scale snapping.
- Collapsible context tree with source sketches under their owning body and branch lines that follow the preference accent.
- Consistent numbered command steps, including Profile → Axis → Extent → Result for Revolve, and shared styling across the right-side studios.
- All sketch-tool families now share numbered, collapsible option panels, including Rectangle, Ellipse, Offset, drawing variants, editing tools, dimensions, and constraints. Live refreshes preserve typed values and section disclosure state.
- Selection intent for individual edges, connected and tangent chains, and partial loops; open profiles can be extruded as surfaces.

- Prism identity, faceted logo, and platform icons.
- Shared ribbon section order and consistent Transform, Inspect, Select, and Appearance groups.
- Grouped creation, pattern, and related tool menus across the application.
- AR Export and Stop Sharing moved to Home, Assemble, Render, and Animate.
- Installer infrastructure for Windows, macOS, and Linux.
- VitePress guides and an application-generated, searchable tool catalog.
- A documentation page for every ribbon tool, with the inputs, settings and script call taken from the app; workflow guides for sketching, solids, surfaces, freeform, assemblies, simulation, manufacturing, rendering, scanning and ECAD; and a scripting and API section covering `cadkit`, the HTTP protocol, the MCP bridge and every automation tool and command.
- A promotional website.

This page records user-visible changes. Update it alongside changes to the app rather than treating it as a fixed product description.
