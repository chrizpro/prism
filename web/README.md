# Prism website

The promotional site for Prism, built with [Astro](https://astro.build). The how-to guides, tool reference and API documentation live in the separate VitePress site in [`../site`](../site).

Use Node.js 22.12 or newer.

```powershell
cd web
npm ci
npm run dev      # http://127.0.0.1:4321
npm run build    # static output in dist/
```

## Content

- `src/data/features.ts` holds the feature areas: copy, screenshots, highlights and which ribbons each covers. Each area gets a page under `/features/<slug>`.
- Tool counts, workspace descriptions and the tool chips on feature pages come from `../site/public/ribbon-catalog.json`, the catalog generated from the app. Chips link to the matching tool section in the guide through `../site/tools/catalog-model.mjs`, so regenerate the catalog rather than editing numbers by hand.
- Screenshots in `src/assets/shots` were captured from the running app through the automation API. Astro converts them to responsive WebP at build time.

Copy describes what Prism does today. Avoid comparisons with other CAD products and claims about licensing or public release that the app does not yet support.

## Configuration

| Variable | Default | Purpose |
| --- | --- | --- |
| `PUBLIC_PRISM_DOCS_URL` | `https://chrizpro.github.io/prism/docs` | Where "Docs", "API" and tool links point |
| `PRISM_WEB_SITE` | `https://prism.example` | Canonical site URL |
| `PRISM_WEB_BASE` | `/` | Base path when hosted in a subfolder |
