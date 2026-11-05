# Made with Prism

Curated native examples for the [startup gallery](../docs/interface/gallery/README.md), tracked by [#61](https://github.com/chrizpro/prism/issues/61). Models and preview images are hosted here and downloaded on demand; the app embeds only `catalog.json`.

Models and scripts are MIT licensed under the repository [license](../LICENSE). These are concept examples, not certified engineering designs. Claude created the camera lens; Codex created the other initial studies through Prism's actual modeling API. The gallery covers Solid, Freeform and Surface/NURBS construction.

## Updating the collection

1. Build and save a `.prism` model in Prism. Reopen it and inspect geometry, materials and appearance. Keep useful source curves/surfaces hidden in the document. Supply a reproducible script where possible.
2. Put it under `models/`, with a real viewport capture or Prism render under `previews/`. Previews should be no larger than 810 × 504, preferably on a light studio background. Do not use generated illustrations as model evidence.
3. Update the catalog entry: unique stable lowercase ID, description, actual construction workflow, author/license, minimum app version, HTTPS URLs, exact byte length and SHA-256. Update the hash whenever the native file changes. For large files use GitHub Release download URLs instead of committing binaries.
4. Validate all entries and links, run maintained Gallery tests, record actual model evidence, and publish the content on `develop`. The catalog refreshes independently of app releases. No merge to `main` is needed for these direct raw-content URLs.

The cache is content-addressed. A changed model never replaces an already opened working copy. Delete or replace an entry to curate the next collection; existing user copies remain intact.
