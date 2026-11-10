# Saving, closing and recovery copies

## Status and decisions

Owner authorized save prompts on application close and opt-in timed autosave on 2026-11-05, then explicitly chose **separate recovery copies**, including untitled documents. [Delivery #66](https://github.com/chrizpro/prism/issues/66). The initial interval is 5 minutes, adjustable from 1 to 120 minutes in Settings → Workspace. Autosave is off by default.

## Expected behavior

**Owner approved native typed extensions on 2026-11-09.** They use the same editable document contents as `.prism`, including source geometry and history. Save As offers the general format and the one typed suffix that matches the current content. New saves default to the general format, and Save As on a typed file defaults to that type while it still matches. Open and save validate the suffix against the contents before replacing an existing file or loading into the current session. [Typed document issue #97](https://github.com/chrizpro/prism/issues/97).

| Suffix | What the file contains |
| --- | --- |
| `.prism_dwg` | One or more editable drawing sheets. Source part or assembly geometry stays in the file. Desktop Open and drag-and-drop enter Drawing. |
| `.prism_asm` | One or more assembly components, with any loose bodies retained. There are no drawing sheets. |
| `.prism_prt` | Model geometry such as bodies, surfaces, sketches or curves, with no assembly components or drawing sheets. |
| `.prism`, `.ourcad` | General native documents, including empty or older documents. |

If the document changes kind, use Save As with the matching suffix or the general format. Recovery copies continue to use `.prism`. The Windows installer registers the three typed extensions for opening in Prism. [SAV-H014](../../../Prism.Tests/Headless/DocumentSafety/DocumentSafetyTests.cs) exercises the matching Save As choices, round trips, mismatch refusal, and drawing drag-and-drop.

A mold project with two or more identified halves and no drawing sheets also qualifies for `.prism_asm`. **Save As → Prism mold (`.prism_mld`)** writes a mold-only copy and leaves the working document unsaved if it has changes. The Windows installer registers the mold suffix. [Mold file scope and tests](../../modeling/mold/README.md#saving-for-manufacturing).

- Owner added file drag-and-drop to this delivery on 2026-11-05 ([#67](https://github.com/chrizpro/prism/issues/67)). Drop one or multiple supported local files anywhere on the desktop Home/workspace window. Native `.prism`/`.ourcad` documents open; STEP/STP, IGES/IGS, BREP, DXF, STL, OBJ, PLY and 3MF use the existing importers. Each successful file gets its own tab. Unsaved work is retained. Unsupported files, folders and text are rejected; invalid files are loaded privately and cannot replace an existing document. Unfinished edits or close reviews block drops. Imported files remain unsaved native documents; the dropped source is not moved or overwritten. In-app importing of dropped images, remote URLs, archive folders and arbitrary point-cloud tables is outside this initial scope.

- A normal desktop close checks every open document, including inactive tabs. Changed documents and unfinished edits offer **Save**, **Don't save** and **Cancel**. Untouched documents do not prompt: a document is untouched until something is created or changed, and it becomes untouched again when undo (or an abandoned edit) returns it to the state it was last saved, opened or created in. An empty sketch, or a tool opened but never applied, creates nothing and does not prompt either; a sketch with something drawn still does. Bug fix 2026-11-09: closing used to ask after undoing everything, or after merely starting a sketch or tool. A change outside the timeline (lights, data sheets, studies) keeps the document marked changed even after undo to the start, as it should. SAV-H012 and SAV-H013 passed (Linux cloud container, .NET SDK 10.0.114, native libraries from release v0.1.0-alpha.2, 2026-11-09). Cancel, a canceled Save As, or a failed write keeps the application open. Previously completed saves remain saved if a later document cancels closing.
- Save uses the document's native path, or opens Save As for an untitled document. A pending sketch/command must be finished or canceled before Save can complete; choosing Don't save explicitly discards that pending work when the app closes. The existing tab-close double-click discard behavior is outside this change.
- Save decisions run before the existing optional feedback prompt. Repeated close clicks do not open duplicate prompts. New changes invalidate prior close approval. The MCP `app_close` request respects a pending/canceled close and never force-kills the application.
- Native saves write a temporary file beside the destination, then replace it only after serialization/write succeeds. A failed write does not truncate the previous saved document.
- When enabled, the timer writes the latest native recovery copy per open changed document under the local Prism recovery folder. It does not overwrite the original, change the document path/dirty flag/history pin, or interrupt the user with Save As. Untitled documents are included.
- Incomplete sketches/commands and exploded presentation states are skipped until finished. Copies contain the last committed document, not a promise to preserve a live preview. Serialization runs on the UI thread because native geometry/session access is not thread-safe; large files can briefly pause interaction.
- Settings provides **Recover autosaved work…** to list copies by title/time and restore one into a new unsaved document. Save then asks for a normal destination. Recovery copies remain available until manually removed from their folder; each document/session keeps only its latest copy. No competing startup recovery modal is added.
- Errors remain visible in the recovery status and preserve the previous valid copy. Disabling the feature stops future scheduled writes without deleting existing recovery files. Preferences persist across launches. Copies are local; crashes, forced process termination, disk failure and OS shutdown cannot guarantee a last-second save or modal prompt.

## Implementation and verification

- Native [save and recovery serialization](../../../Prism/Core/CadSession.cs), [atomic file and recovery scheduling](../../../Prism/Core/DocumentRecovery.cs), [save/close view-model flow](../../../Prism/ViewModels/MainViewModel.DocumentSafety.cs), [desktop lifetime](../../../Prism/Views/MainWindow.DocumentSafety.cs), [dialogs](../../../Prism/Views/DocumentSafetyDialogs.cs).
- Maintained [Unit tests](../../../Prism.Tests/Unit/DocumentSafety/) and [Headless tests](../../../Prism.Tests/Headless/DocumentSafety/). [Desktop timer scenario](../../../Prism.Tests/Integration/DesktopDocumentSafetyTests.cs) and actual evidence are recorded below.

Typed file verification on Windows, 2026-11-09, with the changes applied to `fb99758`. SAV-H014 passed in the 61-test Headless selection. The Release desktop build opened the gallery wrench via a `.prism_dwg` command-line path and reported one editable sheet in the Drawing workspace. NSIS 3.11 compiled the association changes against the installer fixture. Shell registration and the interactive native Save As dialog were not exercised.

## Verification — 2026-11-05

Executed on the configured Windows workstation with the staged native libraries; no native rebuild. The tested workspace also contains unrelated pre-existing owner changes, which were preserved and excluded from this delivery's commits. Permanent source/evidence revisions are recorded in [#66](https://github.com/chrizpro/prism/issues/66) and [#67](https://github.com/chrizpro/prism/issues/67).

| Executed check | Actual result |
| --- | --- |
| Unit project, `-c Release --filter Feature=DocumentSafety` | 3 passed, no failures/skips: default/persistent interval, deterministic scheduler, failed atomic replacement retaining the previous file. |
| Headless project, `-c Release --filter 'Feature=DocumentSafety\|Feature=Reporting'` | 15 passed, no failures/skips: 11 DocumentSafety cases plus 4 existing local Reporting cases. Covers inactive/newly created documents, Save As cancellation/failure, captured save target, recovery state/history, deferred edits, actual window close/dialog choices, Settings preferences and routed multi-file drops with native/mesh importers. |
| [Desktop SAV-I001](../../../Prism.Tests/Integration/DesktopDocumentSafetyTests.cs), `-c Release --filter Feature=DocumentSafety` | 1 passed, no failures/skips. Actual one-minute dispatcher timer produced a separate native copy; original bytes/path and dirty state were unchanged; the copy reopened with valid geometry and the newer color. `PRISM_INTEGRATION_BIN=artifacts/document-safety/bin`. |
| Release Desktop/MCP builds | Passed. Final Desktop including drops: `artifacts/document-safety/drop-bin`; MCP: `artifacts/document-safety/final-bin`. Existing compiler/package warnings remain; Avalonia 11's desktop legacy filename-drop fallback has a deprecation warning. |

Manual desktop review confirmed the [save prompt](evidence/save-prompt-light.png), Cancel returning to the unsaved document, [default-off settings](evidence/settings-defaults-light.png), [enabled interval control](evidence/settings-enabled-light.png), and [recovery selection](evidence/recovery-list-light.png). An actual SAV-I001 backup was copied into an isolated recovery folder for the manual recovery UI check; recovering it preserved the existing tab and opened a new dirty, untitled document. These are companion manual checks, not extra automated test assertions. All settings/recovery folders for verification were isolated from the user's normal preferences.

Representative actual SAV-I001 viewport evidence: [changed original](evidence/SAV-I001-changed-original.png) and [reopened recovery](evidence/SAV-I001-recovery-reopened.png). TRX files remain under `artifacts/document-safety/test-results/`. File-drop verification used real Avalonia routed events and the native/mesh importers in the headless runner; a physical Explorer drag and macOS/Linux execution were not performed. [Machine-readable evidence](evidence/verification.json).

## Limits and open decisions

This initial delivery covers normal desktop window close and manual recovery discovery. Automatic startup recovery presentation, versioned recovery history/retention policy, collaborative file locking and a background native serializer are future proposals.
