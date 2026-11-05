# Main branch integration verification — 2026-11-05

This records the owner's request to make the release branch ready and bring in Claude's work. The repository uses `main`, not `master`. The candidate combines `develop` at `ab51fcb1297d9528b9087944a425bffe5e5e81ef` with Claude's `origin/ccr-2bf545bb-jdqkyc` at `afbab50d1f7a116d60eb433ae91336497c72925f`. The six older local `claude/*` branches were already ancestors of `main`.

## Scope and integration corrections

The candidate includes appearance, product rendering, Drawing, Claude's maintained tool tests and six documented bug fixes. Its canonical feature guides retain existing approvals, observations and unresolved decisions. This integration does not approve new product requirements.

- Resolved the test-guide conflict by retaining Appearance and Rendering and Claude's Assembly coverage links.
- Regenerated `site/public/ribbon-catalog.json`; the Drawing workspace had made its provenance stale and blocked both catalog checks and the documentation build.
- Added Appearance, Rendering and Drawing to the maintained runner's feature filter choices so the documented focused commands work.
- Rebuilt both native libraries from the candidate source. Claude's Solid to Mesh fix needs the new `ck_copy` export; using the old installed DLL would not validate this candidate.

The shared `develop` checkout's uncommitted reporting, native-safety and generated study/image changes were excluded and preserved. Validation used the separate `C:/Users/chris/RiderProjects/Prism-release-validation` checkout.

## Actual Windows verification

Environment: Windows workstation, .NET SDK 10.0.102, MSVC 19.43, local vcpkg `x64-windows-rel` OCCT/Netgen/Eigen, Node 22.14.0. Release configuration throughout.

| Check | Result |
| --- | --- |
| `./native/build.ps1` with separate build/staging directories | Both native libraries rebuilt and staged successfully. |
| `dotnet test Prism.Tests/Unit/Prism.UnitTests.csproj -c Release` | 86 passed, zero failures/skips. |
| `dotnet test Prism.Tests/Headless/Prism.HeadlessTests.csproj -c Release` | 592 passed, zero failures/skips, using the rebuilt native libraries. |
| Desktop and MCP builds into `artifacts/tests/integration/bin` | Passed. |
| `dotnet test Prism.Tests/Integration/Prism.IntegrationTests.csproj -c Release` | 12 passed, zero failures/skips; real desktop/MCP, native viewport, persistence and render/export scenarios. |
| `dotnet run --project tools/Prism.DocsCatalog -c Release -- . --check` | Passed after catalog regeneration. |
| `npm ci` and `npm run build` in `site` | Passed; catalog verified, VitePress pages built and staged. |
| `npm ci` and `npm run build` in `web` | Passed; 15 pages built. |
| Git whitespace/conflict checks | Passed. |

TRX files are retained locally under the validation checkout's `artifacts/tests/release/`; desktop screenshots are under `artifacts/tests/integration/`. These results characterize the fixtures and assertions, not every option or platform.

Reviewed representative captures: [DRW-I001 light Drawing workspace](evidence/2026-11-05/DRW-I001-light.png), [APP-I001 weathered metal viewport](evidence/2026-11-05/APP-I001-weathering.png), and [RND-I001 denoised HDR render](evidence/2026-11-05/RND-I001-denoised.png). These images come from the combined source candidate recorded above; its permanent merge revision is recorded in the linked feature issues. The 32-sample render is draft quality, not a production benchmark.

Rebuilt DLL SHA256:

- `prism_kernel.dll`: `35360D1BCE6F7A15079911A815FD18669C43396E90BB084673938EE7E6613063`
- `prism_sim.dll`: `146418A975581940442EA5B5B685BFA1279573822014D47C4303CBBCFB698B54`

## Limits

Existing compiler/analyzer and NuGet warnings remain, including NU1903 for Tmds.DBus.Protocol 0.21.2. npm reported dependency advisories and an engine warning for undici on local Node 22.14.0; the actual website build passed. No dependency upgrade was mixed into this integration. Installer/tagged releases, macOS/Linux runtime checks, mobile and WebAssembly builds, external solver execution and legacy standalone suites were not rerun. Rebuild the WebAssembly kernel before using the changed managed wrapper with it. No release tag was created.

Feature-level tests and remaining gaps are linked from the [maintained test guide](../../Prism.Tests/README.md), [readiness inventory](test-readiness-report.md) and each canonical feature guide.
