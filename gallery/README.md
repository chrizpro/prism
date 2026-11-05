# Made with Prism

Curated native examples for the [startup gallery](../docs/interface/gallery/README.md), tracked by [#61](https://github.com/chrizpro/prism/issues/61). Models and preview images are hosted here and downloaded on demand; the app embeds only `catalog.json`.

Models and scripts are MIT licensed under the repository [license](../LICENSE). These are concept examples, not certified engineering designs. Claude created the camera lens; Codex created the other initial studies through Prism's actual modeling API. The gallery covers Solid, Freeform and Surface/NURBS construction.

## Updating the collection

1. Build and save a `.prism` model in Prism. Reopen it and inspect geometry, materials and appearance. Keep useful source curves/surfaces hidden in the document. Supply a reproducible script where possible.
2. Put it under `models/`, with a real viewport capture or Prism render under `previews/`. Previews should be no larger than 810 × 504, preferably on a light studio background. Do not use generated illustrations as model evidence.
3. Update the catalog entry: unique stable lowercase ID, description, actual construction workflow, author/license and minimum app version. Run `python gallery/update_catalog.py` to set raw-content URLs, exact byte lengths, SHA-256 hashes and content-specific preview revisions. `python gallery/update_catalog.py --check` verifies consistency without modifying files. For large files use GitHub Release download URLs instead of committing binaries and adapt the catalog publishing script accordingly.
4. Validate all entries and links, run maintained Gallery tests, record actual model evidence, and publish the content on `develop`. The catalog refreshes independently of app releases. No merge to `main` is needed for these direct raw-content URLs.

The cache is content-addressed. A changed model never replaces an already opened working copy. Delete or replace an entry to curate the next collection; existing user copies remain intact.

## Reproduce the new studies

`python gallery/build_aircraft.py --discovery <discovery.json>` builds the Skylark light-aircraft concept through the same actual MCP bridge. Its Freeform fuselage, canopy and propeller remain editable; exact lofted wing/tail bodies retain hidden airfoil sections. `--render-only` reopens the saved model for camera/preview iteration without rebuilding geometry. The aircraft replaces the iron in the active catalog; the older iron assets remain archived here. The design is a visual concept, with no aerodynamic or flightworthiness validation.

Launch an isolated built desktop with a private `PRISM_AUTOMATION_DISCOVERY` and `PRISM_SETTINGS_FILE`, then run `python gallery/build_examples.py --discovery <discovery.json>`. The script expects the built MCP bridge in `artifacts/gallery/bin/Prism.Mcp.dll`; it drives the real MCP stdio bridge and bundled Python `cadkit`, records the calls under `artifacts/gallery/`, saves/reopens each document and validates exact shapes or cage topology. It also renders each model at 960 × 540, 96 samples, six bounces, final denoising. `--only table|pendant|chair` selects one study. `finish_existing.py` refreshes the iron and duct's appearances/previews in the same way. Preview downscaling uses Pillow; modeling scripts use only the Python standard library and bundled cadkit.
