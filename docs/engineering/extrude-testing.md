# Local Extrude characterization and visual evidence

The owner authorized testing existing observable behavior on 2026-11-03, including in the local handoff. This supersedes the earlier workflow-only restriction on test authoring. It does **not** approve unresolved product requirements or authorize changing application behavior to satisfy assumptions.

Feature references: [Solid Extrude draft](../modeling/extrude/README.md#solid-extrude), [Story #6](https://github.com/chrizpro/prism/issues/6), [Surface Story #7](https://github.com/chrizpro/prism/issues/7), [Freeform Profile Story #8](https://github.com/chrizpro/prism/issues/8). Surface/Freeform draft specifications remain in [open PR #9](https://github.com/chrizpro/prism/pull/9); this work does not copy or merge them. The cloud `test/modeling-feature-evidence` branch has no changes beyond merged `main` at `19da46e`.

## Run on Windows

From the repository root, with .NET 10 and native OCCT DLLs in `native/bin`:

```powershell
dotnet run --project tests/Prism.DirectModelTests -c Release -- --extrude-evidence
./tools/capture-modeling-evidence.ps1
```

The [focused suite](../../tests/Prism.DirectModelTests/ExtrudeEvidence.cs) reuses the existing executable project. It runs only these scenarios when `--extrude-evidence` is supplied, continues after individual failures, writes `results.json` and `report.md`, and returns a nonzero exit code for any failed scenario. Without the flag the original suite still runs. `PRISM_EVIDENCE_DIR` optionally sets the geometry output directory; pass the same directory to the capture script with `-EvidenceDirectory`.

Artifacts default to ignored `artifacts/extrude/`: command geometry PNGs, native `.prism` models, quantitative results, actual desktop viewport PNGs and accompanying camera/object state. The [capture script](../../tools/capture-modeling-evidence.ps1) builds and starts its own desktop process with a private discovery file, uses the existing authenticated localhost automation bridge, then stops only that process. It does not attach to the user's existing Prism session. A separate worktree needs native dependencies staged or linked to its own `native/bin`.

`StudioPreview` images render actual command meshes with Prism's software diagnostic renderer and projection camera. They are not synthetic ToolHelp examples. `*-viewport.png` images come from the actual desktop OpenGL viewport via `screenshot(viewport=true)`. Both auto-fit each model independently; compare quantitative dimensions in assertions, not apparent image scale. Screenshots do not replace geometry checks. The capture script opens saved geometry snapshots; it does not yet exercise interactive command picking, handles, or live previews.

## Coverage in the focused suite

| Variant | Existing behavior checked | Independent assertions |
|---|---|---|
| Solid | Distance; negative distance; reverse; negative + reverse; two sides; reversed two sides; symmetric; recipe replay | OCCT validity, one solid, six faces, volume and all bounding limits |
| Solid target extents | Through all; both ways; target body near side; far side + offset | Analytic volume and bounds with explicit New body operation |
| Solid Boolean | Join, Cut, Intersect, explicit target with a second unrelated body | Replacement identity, validity, analytic volume, unrelated body volume |
| Solid lifecycle | Commit; undo/redo; feature distance edit; save/reopen; edit reopened recipe | Exact geometry dimensions and volume after each state change |
| Invalid input | Open solid chain, zero distance, Through all without bodies | No output; diagnostic where implemented; document preservation for open/zero cases |
| Surface | Open three-edge chain; reverse; two sides; symmetric; recipe replay | Validity, three faces, no solid, area and Z bounds |
| Freeform Extrude Profile | Profile normal; X/Y/Z; negative distance; far-end scale; caps on/off; steps/points; commit and undo/redo | Cage vertex/face counts, closure, far-row positions and scaling |

X/Y Freeform cases characterize cage coordinates when pulling an XY profile in its plane. They do not certify a useful nondegenerate surface or approved product behavior. Freeform validity, winding and self-intersection need separate coverage.

## Local verification on 2026-11-03

Base: merged `origin/main` at `19da46e`; .NET SDK 10.0.102; Windows x64; existing workstation native DLLs. No native rebuild was performed, so these results validate the installed binary, not a fresh compilation of native sources. `prism_kernel.dll` SHA256: `07D37FF1F5F7E9F7E008801A919703CF6F49364BC20329207B97576A74CB7B19`.

| Check | Result |
|---|---|
| Focused Extrude suite | 26 scenarios passed; zero failed |
| `Prism.FreeformTests` | 49 checks passed, including existing face-extrusion checks |
| `Prism.PanelWorkflowTests` | 161 checks passed, including edge selection, Solid rejection of open chains, and Surface replay |
| `Prism.FreeformWorkflowTests` | 70 checks passed |
| Original `Prism.DirectModelTests` | 380 PASS lines; reached final pattern-scope assertion, no failure text. This legacy executable emits no final SUCCESS marker; the generic test runner therefore cannot summarize it reliably. |
| Desktop capture | Seven viewport images generated; representative results visually inspected |

The desktop build reports an existing NU1903 warning for `Tmds.DBus.Protocol` 0.21.2. This change does not update dependencies. No confirmed product defects were found in the focused scenarios, so no Bug issues were created. Early fixture placement/expected-volume mistakes were corrected in tests without changing the app.

## Still unverified / awaiting review

Exact-solid taper; region selection; face profiles; plane/face To object; hidden-body extents; automatic Cut and reversal; negative two-sided/symmetric semantics; Freeform invalid normals, winding, smoothness and topology boundaries; isolated Modify Extrude command coverage; interactive selection and preview capture. Existing Freeform tests cover some lower-level face extrusion, but that does not establish full command coverage.

The owner decisions in the Solid draft and PR #9 remain open. Current characterization is not an approval of those requirements. Revolve is the next coverage phase, followed by Loft and Sweep; none has received new coverage in this change. Existing `Prism.LoftTests`, `Prism.LoftPickTests`, `Prism.SurfaceTests` and ToolHelp examples should be inspected and reused when reaching those phases.
