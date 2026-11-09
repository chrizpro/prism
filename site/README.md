# Prism VitePress site

The documentation site: guides, a page for every ribbon tool, and the scripting and API reference. The promotional site is separate, in the repository's `web` folder.

Use Node.js 22 or newer, then `npm ci` and `npm run dev`. `npm run build` checks the ribbon catalog, builds VitePress, and stages the output in `dist` for hosting. VitePress's original output remains in `.vitepress/dist`.

The CAD repository is the source of truth. Update guides and `changelog.md` alongside app changes.

## Generated pages and their data

| Pages | Built from | Refresh with |
| --- | --- | --- |
| `/tool/<tool>`, one page per tool | `public/ribbon-catalog.json` and `data/commands.json`, through `tool/[tool].paths.mjs` | `dotnet run --project tools/Prism.DocsCatalog -- .` (catalog) and `python site/scripts/sync-api.py` (commands) |
| `/tools/<ribbon>`, `/tools/shared`: index pages linking to the tools | `public/ribbon-catalog.json`, through `tools/[tab].paths.mjs` | as above |
| `/api/python` | `data/cadkit.json` | `python site/scripts/sync-api.py` |
| `/api/automation` | `data/automation-tools.json` | `python site/scripts/sync-api.py` |
| `/api/commands` | `data/commands.json` | `python site/scripts/sync-api.py` |
| `/api/examples` | `data/examples/*.py` | `python site/scripts/sync-api.py` |

Run both refresh commands from the repository root. `sync-api.py` reads `cadkit` from the source tree and asks the running app for its commands and automation tools, so start Prism first; without the app it refreshes only `cadkit.json` and the examples. CI checks that the catalog matches the application, but not the API snapshots, so refresh them whenever commands, their inputs or the automation tools change.

Hand-written parts of the tool pages are in `tools/guide-data.mjs`: ribbon introductions, section tips, and the mapping from a ribbon button to its command where the labels differ. `tools/catalog-model.mjs` decides which buttons are the same tool (the same command, or the same button on several ribbons), each tool's page slug, and which sections are shared. The promo site uses it too, so keep slugs stable. `tools/render.mjs` writes the How to use, Settings and Script it sections.

## Guide images

The homepage, all 16 guides, the Python scripting guide and selected generated reference pages use `components/GuideImage.vue`. For a registered capture use `<GuideImage image="cam-stock-setup" />`; `data/guide-images.json` supplies its descriptive alt text, factual caption and original pixel dimensions. Explicit `src`, `alt`, `caption`, `width` and `height` props remain supported for existing images. Images resize to the page width and link to their full-size files; `withBase` preserves the GitHub Pages `/prism/docs/` prefix.

The six original PNGs in `public/images/guides/` are copies of the same-named existing captures in `web/src/assets/shots/`. The 38 additional WebP images are actual Prism screenshots captured through the local Prism.Mcp stdio bridge on 2026-11-07, converted losslessly at their original dimensions. `data/image-provenance.json` records capture hashes, the base revision and exact application/bridge assembly hashes. The capture binary came from the shared working tree; its source must not be attributed solely to the base revision.

`tools/image-data.mjs` maps captures to 27 tool pages and 22 ribbon pages; the path generators insert the shared image component. Add a mapping only where the capture illustrates that tool or workflow. Many individual tools still need their own captures. Keep documentation images when synchronizing the standalone site, and refresh them from actual Prism captures when the illustrated UI changes.

The broader capture session used a separate discovery file, settings file and documents under ignored `artifacts/docs-imagery/2026-11-07/`; it did not edit gallery source files. Its raw PNGs, API call records, model examples and capture-state records remain there. Static stress, thermal, modal and movement-only studies completed for the examples shown. The FDM/resin images show preparation settings, not completed checks: the MCP button action did not run those checks, and the computer-use fallback failed to bind the capture window. A trial 3D flow run failed and another was stopped; neither is presented as a solved result. The published fluid illustration uses the separate analytical Pipe Flow tool.

The modeling follow-up adds nine captures: Solid Fillet, confirmed open Shell and Linear Pattern; Surface Loft and Thicken; NURBS Control Grid and selected point editing; Freeform soft selection and quality markers. Surface and NURBS use distinct ribbon illustrations. Solid and the combined Surface/NURBS guide each have six images; Freeform has four.

These are documentation examples, not new acceptance tests or engineering certification. Existing maintained feature-test links remain in the canonical feature guides. No CAD product behavior changed.

Broader verification on 2026-11-07: the VitePress production build passed with `PRISM_SITE_BASE=/prism/docs/`. Asset validation checked all 38 new images, dimensions and hashes. Production HTML contained 97 image placements across 67 pages (homepage, 16 guides, Python scripting, 27 tool pages and 22 ribbon pages); every image had alt text, dimensions and an existing staged asset under the correct base. Browser QA loaded all five CAM images and all six simulation images at 1440px/390px without horizontal overflow, and checked the Playback tool and Thermal ribbon pages at 390px. The local preview was restarted after the build so it serves the new hashed assets. The earlier capture runs encountered a stale tool catalog. The develop consolidation audit refreshed the 29-tab catalog and current API snapshots; the normal `npm run build` now passes. See the [develop audit](https://github.com/chrizpro/prism/blob/develop/docs/engineering/develop-audit-2026-11-07.md) for the verification record.

Owner request and verification: [documentation imagery #71](https://github.com/chrizpro/prism/issues/71), 2026-11-07. The modeling follow-up build passed; Solid, Surface/NURBS and Freeform images loaded at 1440px and 390px without horizontal overflow. The NURBS ribbon page loaded its distinct point-editing image. All nine added captures were visually inspected and their lossless conversions verified. No CAD behavior changed, so no new maintained CAD tests were added.

### Joint illustrations

`components/JointGuide.vue` adds six responsive, accessible SVG schematics to the assembly guide. Each names the joint, allowed relative motion and a physical example. Gray denotes the reference component, blue the connected component, and orange arrows the allowed motion before limits. These are explanatory drawings, not Prism screenshots. The simulation guide links to them and the hinge walkthrough. Keep wording consistent with [Assembly](https://github.com/chrizpro/prism/blob/develop/docs/modeling/assembly/README.md) and [Motion analysis](https://github.com/chrizpro/prism/blob/develop/docs/motion-analysis.md); do not imply contact simulation or flexible bodies.

Follow-up browser verification on 2026-11-07: all six SVGs have accessible titles/descriptions; the assembly grid has two columns at 1440px and one at 390px without horizontal overflow. Joint cards and the mobile simulation Motion section were visually inspected; both cross-links include `/prism/docs/` and target the assembly guide's actual headings. No application behavior changed or native tests ran for these documentation additions.

## Hosting

The website's owner-private Sites identity is saved in `.openai/hosting.json`. A hosted source checkout should receive the contents of this directory, excluding `node_modules`, build caches, and Git metadata; use the Sites source helper to synchronize and publish it. Keep credentials out of files. A hosting-only checkout uses the committed catalog and data snapshots and does not need the CAD source or .NET SDK.

The static archive contains the output in `dist`. The Site is private by default. Installer artifacts are local/CI outputs rather than public website downloads until a release is published.
