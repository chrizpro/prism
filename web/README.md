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

Copy describes what Prism does today. The owner requested a CAD-alternatives discovery page; keep comparisons factual, acknowledge the alpha status and avoid claiming feature parity, compatibility or affiliation that has not been established.

## Configuration

| Variable | Default | Purpose |
| --- | --- | --- |
| `PUBLIC_PRISM_DOCS_URL` | `https://chrizpro.github.io/prism/docs` | Where "Docs", "API" and tool links point |
| `PRISM_WEB_SITE` | `https://chrizpro.github.io` | Canonical site origin |
| `PRISM_WEB_BASE` | `/` | Base path when hosted in a subfolder |

The build emits `sitemap.xml` with the configured origin and base. The GitHub Pages workflow sets both for `/prism/`; the documentation emits its own sitemap under `/prism/docs/`. A robots.txt under the project path is not the domain-root robots.txt used by search engines, so submit both sitemap URLs in Google Search Console rather than relying on that file.
