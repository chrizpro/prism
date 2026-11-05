# Startup model gallery

## Status and scope

Approved by the owner on 2026-11-05 after the on-demand GitHub gallery proposal: use existing Prism studies and Claude's camera lens, represent Solid, Surface/NURBS and Freeform, and add three new finished examples to exercise modeling, Appearance and API/MCP. [Delivery issue #61](https://github.com/chrizpro/prism/issues/61).

## Behavior

- Home shows **Made with Prism** with image cards, workflow/category, description, author and license. Search matches names, descriptions and workflow. Existing workspace, recent files and new/open actions remain available.
- Only the small catalog ships with Prism. Models and previews live in the public repository's `gallery/` directory, outside application assets and packaging. Small initial files use GitHub raw downloads; the catalog also supports GitHub Release asset URLs for larger collections later.
- Home loads the local catalog immediately and refreshes the remote catalog/previews asynchronously. Failure never blocks startup; Refresh retries. Last valid catalog and previews remain available offline. A first offline launch shows catalog text and preview placeholders.
- A card downloads its model only on request, checks size and SHA-256, and atomically caches the verified file. Cached models work offline. A newer content hash gets a separate cache entry.
- Opening always creates a uniquely named editable copy under the workspace's `Gallery` folder (or Documents/Prism/Gallery when no workspace is selected). Cache files and previous working copies are never overwritten. Saving edits cannot alter the downloadable original.
- Catalog entries include stable ID, workflow, author, license, minimum Prism version, HTTPS model/preview URLs, model size and SHA-256. Unsupported newer minimum versions cannot open. Catalog schema errors retain the prior catalog. Downloads are bounded and time out; partial/corrupt downloads are not offered as cached models.
- Curated examples are concept studies. Geometry and appearance are retained in native files; older studies may lack construction history. The Surface pendant retains its hidden meridian and source surface; the Freeform chair retains editable cages. Reproducible scripts are linked from the content README.

## Acceptance and maintenance

Startup stays responsive without a network connection. Models do not enter the installer. Verified cache opens without network; repeated opens create independent copies. Downloads with changed/truncated bytes fail clearly. The collection represents multiple workflows and can replace weaker examples by updating the catalog independently of application releases.

## Implementation and verification

- [Catalog and cache](../../../Prism/Core/Gallery.cs), [view model](../../../Prism/ViewModels/MainViewModel.Gallery.cs), [startup view](../../../Prism/Views/HomeView.axaml).
- [Gallery content and publishing](../../../gallery/README.md).
- Maintained [Unit tests](../../../Prism.Tests/Unit/Gallery/), [Headless tests](../../../Prism.Tests/Headless/Gallery/) and [desktop/MCP model checks](../../../Prism.Tests/Integration/DesktopGalleryTests.cs).

## Curated collection

Eight examples: Atelier side table (Solid), Halo pendant (Surface Revolve and Thicken), Cove lounge chair (Freeform cushions and Solid frame), Claude's 50 mm camera lens (Solid), Glide iron and Azure bottle (Freeform), Arc mouse (NURBS control grid), and transition duct (Surface/NURBS loft). The three new models were built through the actual MCP stdio bridge and bundled Python API. Appearance includes wood grain, brushed metal/anisotropy, clear coat, smooth color gradients and procedural bump. The older iron and duct were refreshed with current appearances and full renders.

The new studies are exterior furniture/lighting concepts, with no certified strength or electrical/thermal design. Render previews use actual Prism output, not generated illustrations. The local parts-library Freeform extraction limitation discovered with the chair is tracked separately in [#62](https://github.com/chrizpro/prism/issues/62).

## Open decisions and limits

Community submissions, ratings, accounts and assembly insertion from online cards are future proposals. No automatic replacement of a user's working copy. GitHub availability is needed for uncached downloads; a separate library repository or release assets can be adopted as the collection grows.
