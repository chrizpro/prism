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

The desktop releases' `Prism-LGPL-sources.zip` comes from the same vcpkg commit and Open CASCADE 8.0.1; the web archive adds the Emscripten triplet.

## Checks

- Manual (2026-11-22, Chromium via Playwright): the app starts in about 10 s, the welcome gallery loads, **Download & open** opens Vermilion GT, Box creates a 40 × 30 × 20 mm solid with 24,000 mm³ volume, and right-drag orbits.
- Manual (2026-11-22): the mark's flyout opens; **Get the desktop app** opens the download page and **Licenses & source** opens `THIRD-PARTY-NOTICES.txt`; `licenses/Prism-LGPL-sources.zip` is served. Ribbon dropdown arrows, tab ✕/⋯, ✦ Assistant and the 📁 workspace icon draw correctly.
- Static scan (2026-11-22): every non-ASCII character in `Prism/` `.cs`, `.axaml`, `.json`, `.md`, `.txt` and `.xml` files is covered by Inter or the fallback fonts.
- Not yet checked in the browser: most modeling tools beyond Box, saving in Firefox and Safari, large models, and AOT builds.
- Automated tests for the mark and the font coverage are not written yet.
