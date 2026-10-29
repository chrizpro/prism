# Studio design refinement

This pass implements the visual hierarchy from the studio mockups using working controls. `Prism/Styles/Studio.axaml` provides shared graphite/light surfaces, compact tabs, section bars, restrained selection borders, accessible focus states, native disclosures, and consistent actions. The active studio dock is now 442px overall, including a 48px selector rail; its content area is 394px. Existing narrow layouts remain supported.

- **Diagnostics:** error/warning counts, issue search, an expanded selected issue with metadata and real geometry, collapsed rows for other issues, linked repair/location actions, checks, and activity.
- **References:** grouped origin/construction/sketch/image/curve rows, visibility controls, actual plane/object previews, coordinate-frame metadata, and clickable feature dependencies.
- **Parameters:** pinned values, a searchable expression table, a selected-value editor and computed result, clickable bound-feature usage, and an on-demand add form. Existing validation and rebuild behavior remains in place.
- **Appearance:** larger bordered finish swatches with studio reflections, shared colors/controls, contextual material selection, a compact surface inspector, and a consistent material library with functioning engineering disclosures. Physical material properties remain separate from visual finishes.

## Diagnostic geometry

`StudioPreview` produces a small shaded image from actual viewport tessellation and the application's projection camera. It uses a separate camera, a depth buffer and face/edge highlighting. The camera frames selected geometry and uses adjacent face normals where available. When no localized selection exists, it shows an object overview rather than inventing a problem location.

`GeometryPreview` rasterizes on a cancellable worker and caches the resulting bitmap. No second continuously running viewport or path-tracing job is created. Theme changes, selection changes and detachment invalidate/cancel the relevant work. Native tessellation is obtained from the document's mesh cache before work is dispatched; a previously unmeshed object still needs initial tessellation.

A stopped rebuild now retains its attempted recipe, remapped input selections, continuation and identity map. Thus its preview shows the changed input that failed, not the old successful geometry. **Edit feature** reopens that attempted operation on the changed input; updating can resume the remaining downstream operations. Cancel leaves the failed snapshot and original branch available. These are fast diagnostic previews, not photorealistic final renders or automatic identification of which selected edge is the root cause.

## Review and verification

Dark/light captures for Diagnostics, References, Parameters, Appearance and the Materials library are stored here. These are screenshots of the implemented controls, including a real thin-wall fillet failure.

Run sequentially from the repository root to avoid concurrent builds modifying Avalonia's shared intermediate files:

```
dotnet run --project tests/Prism.StudioPolishTests
dotnet run --project tests/Prism.StudioTests
dotnet run --project tests/Prism.HistoryTests
dotnet run --project tests/Prism.AppearanceTests
dotnet build Prism.Desktop/Prism.Desktop.csproj --no-restore -o artifacts/studio-polish-preview
```

The suites cover actual input/highlight pixels, independent camera framing, cancellation, same-issue geometry refresh, issue filtering, repair/cancel/continuation mapping, document isolation, material data and grouped undo, parameter expressions and rebuilds, and native light/dark layouts. The existing session-only feature-history/file-format limitation is unchanged. The running desktop session was left untouched.
