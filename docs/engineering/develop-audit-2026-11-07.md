# Develop consolidation audit — 2026-11-07

Owner direction: check the local Prism work left by multiple agents and push the completed work to `develop`. The public branch is named `main`; no merge, tag, release or public-site deployment was requested.

## Source reviewed

The application and maintained tests were checked at `b5f34f58d47fbab01a27862ddf0472ff369f5466`. Its parent `0a084639cd6dc4188ecebe00b39f7f4cbfa4cd34` contains the guide images and site work. Both commits were local and ahead of `origin/develop` when the audit began. This audit adds generated documentation refreshes, local-output ignore rules and the verification record; it does not change application behavior or approve proposed requirements.

## Local work inventory

- All 11 local branch tips, including the six Claude branches, both Codex feature branches and `wasm-browser`, are ancestors of `develop`. No separate feature branch needs merging.
- Reviewed all 12 registered worktrees. The ordinary feature/release worktrees are clean. The older history worktree has generated images and output; all 170 untracked paths there already exist in the current checkout.
- The dirty appearance and rendering validation worktrees are preserved. All 30 and 21 changed source/project files respectively match blobs already reachable in `develop` history. Their new maintained test files and evidence are already integrated.
- The separate `../Prism-headcheck` directory is an older source copy. None of its 156 checked source/document paths is absent from `develop`. Of its 138 C#/XAML/project files, 137 match historical blobs; the remaining sketch test differs only by lacking the current explicit New body fix. It supplies no newer source to copy back.
- The retained 2026-11-05 stash contains older source and generated evidence. Its substantive added source lines are present in current files. It was inspected without applying or dropping it.
- Git object checking reported no corruption. Unreachable commits are older stash/index snapshots and superseded Direct Modeling snapshots; no recovery commit was applied over current work.
- Local agent settings/checkouts, input attachments and browser inspection output remain on disk and are now explicitly ignored. Build products, raw captures, recovery/discovery files and native study outputs remain local under the existing ignore rules. No credentials were found by the added-line token/private-key pattern scan of the pending delivery.

These checks inventory Prism-related repositories and copies; they do not delete old worktrees, stashes or local evidence.

## Corrections made before push

- Regenerated `site/public/ribbon-catalog.json` from the current application: 29 ribbon tabs. This fixes the stale-catalog gate that blocked the normal site build during image work.
- Refreshed the 237 command descriptions and automation schemas using the freshly rebuilt desktop in an isolated session. The Python library reference and shipped examples were also refreshed; unchanged files need no commit.
- Kept `.claude`, `.codex-remote-attachments` and `.playwright-mcp` output out of source control.

## Actual Windows verification

| Check | Result |
| --- | --- |
| `./tools/test.ps1` | 142 Unit and 740 Headless cases passed, no failures or skips |
| `dotnet build Prism.Desktop/Prism.Desktop.csproj -c Release --no-restore` | Passed; existing NU1903 warning for Tmds.DBus.Protocol 0.21.2 remains |
| `dotnet build Prism.Mcp/Prism.Mcp.csproj -c Release --no-restore -o artifacts/tests/develop-audit-bin/mcp` | Passed with no warnings/errors |
| Fresh isolated MCP initialize / `get_state` | Passed against the rebuilt desktop |
| `dotnet Prism.Desktop/bin/Release/net10.0/Prism.Desktop.dll --verify-runtime` | Geometry and simulation runtimes verified |
| `dotnet run --project tools/Prism.DocsCatalog -c Release -- .` | Regenerated current catalog |
| `npm run build` in `site`, after the API refresh | Passed, including catalog freshness, VitePress rendering and staging |
| `npm run build` in `web` | Passed, 16 pages built |
| `git diff --check` | Passed |

The native DLLs already staged on this Windows workstation were reused; no C++ rebuild, macOS/Linux validation or complete desktop integration collection was claimed. Normal MCP output replacement was blocked by existing agent host processes holding the DLL. Those processes were left intact; the isolated bridge build and protocol smoke check establish that the source builds and connects.

Raw execution logs and TRX results are under local `artifacts/tests/` (`develop-*-2026-11-07.log`, `Unit.trx`, `Headless.trx`). See the [maintained test guide](../../Prism.Tests/README.md), [site maintenance guide](../../site/README.md) and [documentation imagery issue #71](https://github.com/chrizpro/prism/issues/71). Historical capture hashes retain the capture binaries' identities; rebuilding the application does not retroactively change screenshot provenance.
