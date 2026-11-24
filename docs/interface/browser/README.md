# Prism in the browser

`Prism.Browser` runs the shared Prism app as WebAssembly with the OCCT kernel compiled to WASM. The goal is the same features as the desktop app; desktop stays the faster, fuller experience. Build notes are in [Run Prism from a fork](../../../README.md#run-prism-from-a-fork); the kernel is built with `native/build-wasm.ps1`.

## Web-version mark

Owner direction (2026-11-22): tell web users when something differs from desktop without a constant banner or a startup popup, which people close without reading. Use a small mark in the bottom-right corner instead.

Requirements:

- In the browser build only, the model view shows a faint `Web version · what's different?` label in its bottom-right corner. The desktop app never shows it.
- The mark stays out of the way: no colour, partly transparent, no layout space taken, and it doesn't appear in the status bar or block modeling.
- Clicking it opens a small flyout that lists how the web version differs (`AppInfo.WebVersionDifferences`), a **Get the desktop app** button that opens the [download page](https://chrizpro.github.io/prism/download) in a new tab, and a **Licenses & source** button (see [Licensing](#licensing)).
- The list is kept current as browser support changes; remove an item once the feature works in the browser.

**Proposed, not yet reviewed:** the wording of the list below.

Current differences (2026-11-22):

| Area | Browser behaviour | Why |
| --- | --- | --- |
| Speed and memory | Slower; the .NET code runs interpreted, single-threaded | WebAssembly limits; AOT not yet verified |
| Files | Open and save go through the browser's file picker; workspace, autosave and recovery live in tab memory only | No local file system |
| Simulation | Meshing and solving unavailable | Netgen is stubbed; CalculiX is a separate process |
| Scripts, AI Assistant, MCP | Unavailable | They start local processes |
| Video export, send to slicer | Unavailable | They start ffmpeg or a slicer |

## Loading time (measured 2026-11-23)

What a first visit downloads, from the boot manifest of a `packaging/Publish-Browser.ps1` build (206 files; one language-data shard of three is used):

| Served as | Size |
| --- | --- |
| Brotli | 19.5 MB |
| gzip | 27.2 MB |
| uncompressed | 78.5 MB |

`dotnet.native.wasm` (the .NET runtime with Open CASCADE linked in) is 7.4 MB of the Brotli total, `Prism.wasm` 2 MB, the rest the .NET library, Avalonia, Skia and Silk.NET assemblies.

Download time alone, by connection (bytes ÷ bandwidth; real connections add latency):

| | 10 Mb/s | 25 Mb/s | 50 Mb/s | 100 Mb/s | 300 Mb/s |
| --- | --- | --- | --- | --- | --- |
| Brotli | 16 s | 6 s | 3 s | 1.6 s | 0.5 s |
| gzip | 22 s | 9 s | 4 s | 2.2 s | 0.7 s |
| uncompressed | 63 s | 25 s | 13 s | 6 s | 2 s |

Start-up after the download, measured on the owner's workstation in the built-in Chromium browser with the files served locally: the downloads and the .NET runtime were done at 1.9–2.6 s and the app replaced the loading page at 8.1–10.8 s. The console report splits that: the main view model 0.4–0.5 s, the main view 1.4–2.1 s, and the first frame until the UI thread is free 6.1–6.7 s. The first frame is the biggest cost; the likely cause is the welcome gallery, whose preview pictures are decoded "in the background", which in the browser means on the one thread the UI also uses. Loading only the visible cards' pictures there is the next thing to try. Slower computers take longer. A later visit skips the download when the host lets the browser cache the files (see Hosting), so it costs only the start-up.

**Changed on 2026-11-23:** browser logging is limited to errors (it did not remove the ~110 binding messages at start-up: Avalonia logs those as errors, though they are harmless null paths before a tool opens), and the 3D view's compiled shader programs are shared between viewport panes, which all draw on Skia's one WebGL context (desktop keeps a binary cache instead, see [start-up](../startup.md)). The browser console prints the start-up stages, the same report as the desktop's `startup.log`.

**Options not taken yet** (each needs owner review):

- **AOT compilation** would cut most of the 6 s of start-up but needs trimming and roughly doubles the native module (about 54 MB raw); see the build notes. Start-up with AOT has not been verified end to end.
- **Trimming the .NET library only** (keeping Avalonia, Prism and Silk.NET whole, since XAML bindings use reflection) could save an estimated 2–3 MB of Brotli download (System.Private.Xml, System.Data.Common, serialisation); every feature would need retesting.
- **A size-optimised native build** (`-Oz`) would shrink `dotnet.native.wasm` but slow the geometry kernel.

## Phones and low-power devices

**Owner request 2026-11-23, built; tried with the built-in browser's phone emulation (375 × 812, touch).** The loading page decides before anything large downloads ([main.js](../../../Prism.Browser/wwwroot/main.js)):

- **No WebGL 2** (any device): *This browser cannot run Prism*, with what to try instead. Prism cannot draw without it, so there is no way on.
- **A phone** (a touch screen whose smaller side is under 600 CSS pixels): *Prism works best on a computer*: it is made for a mouse or trackpad and a large screen, phones may run it slowly or close it, and it downloads about 20 MB. **Get the desktop app** opens the download page; **Continue anyway** starts the download and is remembered in local storage (`prism-continue-on-this-device`). Tablets go straight on.
- **Low memory** (`navigator.deviceMemory` under 4 GB) or **data saver** on: the same notice, worded *Prism may run slowly on this device*.
- Before the choice only `dotnet.js` (the loader) has been fetched.

## Hosting

The web version is served at `/app/` of the project's GitHub Pages site (`https://chrizpro.github.io/prism/app/`):

1. `packaging/Publish-Browser.ps1` writes the site and `artifacts/browser-publish/Prism-web.zip` (the site without the `.br`/`.gz` copies).
2. Attach `Prism-web.zip` to the GitHub release (by hand: the web build needs the Emscripten Open CASCADE build, which CI does not have).
3. The Pages workflow (on a published release, a push to `main`, or by hand) unpacks the newest release's `Prism-web.zip` into `/app/`. The download page shows **Try it in your browser** only when the release carries it.

What the host must do, and why it matters for the wait: serve compressed files (GitHub Pages compresses with gzip, not the build's Brotli copies: 27 MB rather than 19.5 MB; check the `content-encoding` of `dotnet.native.*.wasm` after the first deploy), serve `.wasm` as `application/wasm` so the browser compiles while downloading, and let the browser cache the fingerprinted `_framework` files. A host that serves the pre-compressed `.br` files and sends long cache headers for `_framework` (Cloudflare Pages, Netlify, Azure Static Web Apps) would download about a quarter less on a first visit.

## Fonts

The browser has no system fonts. On desktop, characters missing from Inter (▾ ✕ ⚙ ☰ ⊥ 📁 …) fall back to the OS's symbol fonts; in the browser they drew as empty boxes, which hid every dropdown arrow. `Prism.Browser` now bundles fallback fonts, tried in this order:

| Font | Covers | License |
| --- | --- | --- |
| Noto Sans Symbols 2 | Arrows, shapes, dingbats, braille, ⏮ | SIL OFL 1.1 (`Assets/Fonts/Noto-OFL.txt`) |
| DejaVu Sans | Box drawing, math, modifier letters (ᵀ ᵧ) | Bitstream Vera / public domain (`Assets/Fonts/DejaVu-LICENSE.txt`) |
| Noto Emoji (static Regular instance) | Pictographs (📁 📌 📎) | SIL OFL 1.1 |

Requirement: no interface text draws as a missing-glyph box in the browser. When adding a symbol to the UI, check it is covered by Inter or one of these fonts. Avoid fullwidth forms (＝ ＋); only multi-megabyte CJK fonts have them.

Known gap: monospace text (`Cascadia Mono, Consolas, monospace`) falls back to Inter in the browser, so script and console text is proportional there.

## Licensing

Open CASCADE (LGPL-2.1 with the OCCT exception) is statically linked into `dotnet.native.wasm`, unlike desktop where it is a replaceable shared library. Hosting the web version distributes it, so every hosted build must follow LGPL-2.1 section 6(a):

- **Publish with `packaging/Publish-Browser.ps1`**, never a bare `dotnet publish`. It writes `THIRD-PARTY-NOTICES.txt` (all components and full license texts, including the browser .NET runtime, Emscripten's system libraries and the fallback fonts), `licenses/` and `licenses/Prism-LGPL-sources.zip` (the Open CASCADE archive plus the vcpkg recipe, patches and triplet used) into `wwwroot`. Host all of it.
- **Relinking:** the notices name the exact Prism commit. Prism is MIT and public, so anyone can rebuild the web version against a modified Open CASCADE with `native/build-wasm.ps1` and this script. Publish only from a clean, pushed commit; the script marks builds with uncommitted changes.
- **Reachable from the app:** the web-version mark's flyout has a **Licenses & source** button that opens the notices served next to the page.
- Netgen is stubbed in the browser build and FreeType is not linked, so neither is distributed there.
- **Fonts.** Inter (the app's typeface, inside the Avalonia.Fonts.Inter package) is under the SIL Open Font License; its notice was missing from both the web and desktop notices until 2026-11-23 ([Inter-OFL.txt](../../../packaging/Inter-OFL.txt), added by `Write-Notices.ps1`). The fallback fonts' licences were already there.
- **Audit 2026-11-23.** The notices cover the .NET runtime (with ICU's Unicode licence), every NuGet package, Skia's bundled libraries (libpng, libjpeg-turbo, libwebp, zlib, FreeType, HarfBuzz, expat and the rest), Open CASCADE with its source and relinking offer, Eigen, Emscripten's system libraries and all fonts. Privacy for the web version is described on the guide's [privacy page](../../../site/reference/privacy.md). This is a practical check, not legal advice.

The desktop releases' `Prism-LGPL-sources.zip` comes from the same vcpkg commit and Open CASCADE 8.0.1; the web archive adds the Emscripten triplet.

## Checks

- Manual (2026-11-22, Chromium via Playwright): the app starts in about 10 s, the welcome gallery loads, **Download & open** opens Vermilion GT, Box creates a 40 × 30 × 20 mm solid with 24,000 mm³ volume, and right-drag orbits.
- Manual (2026-11-22): the mark's flyout opens; **Get the desktop app** opens the download page and **Licenses & source** opens `THIRD-PARTY-NOTICES.txt`; `licenses/Prism-LGPL-sources.zip` is served. Ribbon dropdown arrows, tab ✕/⋯, ✦ Assistant and the 📁 workspace icon draw correctly.
- Static scan (2026-11-22): every non-ASCII character in `Prism/` `.cs`, `.axaml`, `.json`, `.md`, `.txt` and `.xml` files is covered by Inter or the fallback fonts.
- Not yet checked in the browser: most modeling tools beyond Box, saving in Firefox and Safari, large models, and AOT builds.
- Automated tests for the mark and the font coverage are not written yet.
