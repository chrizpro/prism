# Changes

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
