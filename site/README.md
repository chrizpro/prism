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

## Hosting

The website's owner-private Sites identity is saved in `.openai/hosting.json`. A hosted source checkout should receive the contents of this directory, excluding `node_modules`, build caches, and Git metadata; use the Sites source helper to synchronize and publish it. Keep credentials out of files. A hosting-only checkout uses the committed catalog and data snapshots and does not need the CAD source or .NET SDK.

The static archive contains the output in `dist`. The Site is private by default. Installer artifacts are local/CI outputs rather than public website downloads until a release is published.
