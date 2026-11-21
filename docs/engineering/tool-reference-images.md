# Images throughout the tool reference

The owner requested an image for every tool on 2026-11-14, with output examples for distinctive tools. This is documentation work on `codex/v0.4-illustrated-docs`, branched from `v0.4-work`; no product behavior or feature requirements change.

## What the pictures mean

Every generated `/tool/…` page has a primary illustration. Tools shared between ribbons keep their existing shared page; the same tool can reuse its illustration. Sketch operations have separate illustrations from solid operations even when their names match.

- **Prism help example:** before-and-after geometry from the application's existing `ToolHelp.Example`, rendered through `StudioPreview.Render`. Many examples use the exact native geometry kernel, while others are handmade meshes or schematic scalar fields. They are educational examples, not runs of every command. Analysis colors and deformation are explicitly labeled illustrative; they are not solved studies or numerical evidence.
- **Concept diagram:** an explicit drawing of a tool's action, selection or output. File formats, editor actions and view controls need different explanations from geometry tools. There is no catch-all illustration for unknown tools. Sketch Fillet, Chamfer and Mirror use 2D diagrams rather than the shared 3D help pictures.
- **Prism result capture:** a viewport screenshot after executing the command in an isolated Windows desktop instance. Five primary captures show a helix, scale-table Surface Sweep, coplanar Sew Merge, a fillet between touching separate sheets, and solid-face Imprint. These are documentation examples, not new acceptance-test claims.

The registries contain alt text, captions, dimensions, source kind and file hashes. Original source diagrams and existing workflow screenshots remain on pages where they add detail. Each image links to its full-size asset.

## Reproduce and extend

Use a work directory on a drive with room for .NET output and raw images. The owner authorized E: for this session. Only compact finished images and source are committed; builds, raw pixel buffers, screenshot records and model documents live under `E:\Prism-docs-work`.

1. Export `toolPages` from [catalog-model.mjs](../../site/tools/catalog-model.mjs) as JSON to the work directory.
2. Build/run [Prism.DocsImages](../../tools/Prism.DocsImages/Program.cs) with the JSON path and a `help-examples` output directory. It matches each ribbon button by stable ID and uses its resolved help topic. Native libraries and their dependencies must be available. The optional `--artifacts-path` .NET argument keeps build files on the work drive.
3. For actual result captures, start an **isolated, disposable** Prism instance with its own settings, recovery directory and automation discovery file. Run [capture_doc_tool_results.py](../../tools/capture_doc_tool_results.py) with that discovery file and the `captures` output directory. It creates documents, executes commands and saves screenshot hashes, states, native models and API call records there. Never point it at a user's working instance.
4. Run `python tools/build_doc_tool_images.py --work E:/Prism-docs-work`. It renders explicit non-preview diagrams, composes before/after examples and converts verified result captures to lossless WebP. The final [image registry](../../site/data/tool-images.json) and [provenance](../../site/data/tool-image-provenance.json) are generated together.
5. Run `node site/scripts/check-tool-images.mjs` and build the documentation. Adding a tool without an image fails the build. Adding a non-preview action requires a deliberate diagram recipe; there is no generic fallback.

To keep documentation build/cache files on E:, set `PRISM_DOCS_BUILD_ROOT=E:/Prism-docs-work/site-build` before `npm run build` in `site`. VitePress writes to `vitepress/` and `cache/` there, and the staging helper writes `dist/` there. Without the variable, the existing repository-local build behavior is unchanged.

## Verification and related coverage

The documentation checker verifies every current tool page has metadata, an accessible image type, an existing asset with the expected hash and SVG title/description where applicable. It deliberately checks the **page set**, not just the command list: selection, file, sketch, editor and view buttons without automation commands count too.

The existing [RibbonHelpTests](../../Prism.Tests/Headless/Ribbon/RibbonHelpTests.cs) cover the underlying application help topics and geometry examples. This task does not add or claim execution of maintained native feature tests. Actual capture records remain separate from those tests.

Related owner-request tracking: [documentation imagery #71](https://github.com/chrizpro/prism/issues/71). Canonical behavior stays in the [feature guides](../README.md); these images do not settle proposed requirements. See [site maintenance and dated verification](../../site/README.md#every-tool-has-an-image) for the delivery evidence.


### Delivery evidence — 2026-11-14

All 429 pages have a primary illustration: 294 help examples, 130 explicit action diagrams and five actual result captures. The normal documentation image check passed; the E:-hosted VitePress production build and Astro build passed. Built-HTML validation checked every page and its base-prefixed asset. Browser checks covered 39 pages across all 29 ribbons at both 390px and 1440px, with all images loaded and no page overflow. All 130 SVGs passed browser text-bound checks. The .NET exporter built successfully and exported 265 distinct help examples with no errors using the installed native kernel; that kernel was not rebuilt and no maintained native test suite or CAE solver run is claimed.
