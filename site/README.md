# Prism VitePress site

Use Node.js 22 or newer, then `npm ci` and `npm run dev`. `npm run build` checks the ribbon catalog, builds VitePress, and stages the output in `dist` for hosting. VitePress's original output remains in `.vitepress/dist`.

The CAD repository is the source of truth. Update guides and `changelog.md` alongside app changes. Regenerate `public/ribbon-catalog.json` with `dotnet run --project tools/Prism.DocsCatalog -- .` from the repository root. CI checks that the committed catalog matches the actual application.

The website's owner-private Sites identity is saved in `.openai/hosting.json`. A hosted source checkout should receive the contents of this directory, excluding `node_modules`, build caches, and Git metadata; use the Sites source helper to synchronize and publish it. Keep credentials out of files. A hosting-only checkout uses the committed catalog and does not need the CAD source or .NET SDK.

The static archive contains the output in `dist`. The Site is private by default. Installer artifacts are local/CI outputs rather than public website downloads until a release is published.
