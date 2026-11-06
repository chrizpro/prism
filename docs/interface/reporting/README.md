# Feedback, bug and crash reporting

## Purpose and status

Keep reporting accessible through Help and GitHub, with minimal local state and an explicit privacy review for crash reports.

Owner direction recorded 2026-11-05: put **Report a bug** inside Help instead of the header; ask for feedback on closures 1 and 5 and then every ten closures; offer **Don't show again**; remove sensitive paths from crash reports and require stack-trace approval before submission. The owner explicitly selected closures **1, 5, 15, 25, …**. GitHub web forms are acceptable for all three report types.

**Approved and implemented (2026-11-05):** the owner approved the complete proposed flow with “Yes” after reviewing the local prompts, next-launch crash review, omission of paths/messages, and GitHub submission flow. Verification results are recorded below. Follow the [requirements and testing workflow](../../engineering/requirements-and-testing.md#review-gate-requirements-before-tests).

## Previous behavior (before this change)

- Both the header button and Help menu open the GitHub bug form with Prism version and system information.
- The desktop unhandled-exception handler appends the exception's full text to a local `crash.log`. This can contain source paths and exception messages with private information. There is no automatic upload or review dialog.
- The [published reporting instructions](../../../site/reference/known-issues.md#report-a-bug) and [bug form](../../../.github/ISSUE_TEMPLATE/bug_report.yml) currently ask users to attach that raw log after removing private information.
- No exit-feedback schedule or persisted feedback preference currently exists.

## Approved behavior and acceptance criteria

### Help and GitHub forms

- Remove the standalone **Report a bug** header button. Keep **Help (?) → Report a bug** and add **Give feedback** there.
- Provide separate GitHub web forms for bugs, feedback and crashes. Prism opens a draft; the user edits and submits it on GitHub. Prism does not infer submission from opening the browser.
- Explain that GitHub reports are public and require a GitHub account. Do not embed repository credentials in the app.
- Update the published guide, issue forms and affected UI text together. Feedback should be explicitly accepted by the contribution policy rather than routed into essential-feature requests.

### Feedback on closure

- Count normal desktop application closures locally, once per session. Show a short optional prompt immediately before the scheduled closure completes: **1, 5, 15, 25, …**.
- Offer **Give feedback**, **Not now**, and an unchecked **Don't show again** preference. Dismissing the prompt behaves as **Not now**. Either action completes closure; **Give feedback** opens the GitHub feedback form first.
- Persist only a closure count and suppression preference for this schedule. Do not add a user identifier or track whether a report was submitted. Browser opening does not suppress future reminders.
- Exclude crashes, forced termination, cancelled closure attempts, automated test sessions and runtime-verification runs. Repeated close requests while a prompt is open must not count again or open another prompt.
- Persist the suppression preference when the prompt closes, including when **Give feedback** is selected. A settings reset or another local profile can restart the schedule.
- Failure to save preferences or open a browser must not trap the user in Prism or prevent closure.

### Crash privacy and approval

- Capture a bounded pending diagnostic locally for review on the next successful interactive desktop launch, avoiding dependency on a functioning UI during a crash.
- Build the shareable diagnostic from structured exception types and stack frames. Omit source-file paths, filenames, line-location text, exception messages, exception data, command arguments, environment variables, document/model names and raw log contents. A regex applied to `Exception.ToString()` alone is insufficient.
- Restrict shared frames to known Prism/runtime code; replace unknown/custom frames with a placeholder so script/plugin names are not inadvertently shared. Include Prism version and general OS/architecture only.
- Show only this prepared trace in a local review dialog before passing any diagnostic to a browser, URL or clipboard. Provide **Continue to GitHub** and **Dismiss**. Approval applies to the exact previewed content.
- GitHub provides the final editable draft and submission step. No automatic submission or raw-log attachment. Replace instructions encouraging raw-log upload with instructions for the approved diagnostic.
- Dismissal or handoff consumes the pending prompt without claiming submission succeeded. Native crashes that cannot produce a managed diagnostic remain a documented limitation; do not invent a stack trace.
- Bound trace and URL sizes; if a draft cannot be safely opened, retain the local review and offer an explicitly requested copy of the approved text, without exposing the raw diagnostic in an error message.

## Open decisions and limitations

- No open product decisions remain in this scope. Next-launch review, conservative diagnostic fields and closure semantics were approved on 2026-11-05.
- Native crashes that bypass the managed exception handler may produce no pending diagnostic. No native crash capture was added.
- Older `crash.log` files remain local and are never read or uploaded by this flow.
- Only the latest prepared diagnostic is retained, in `pending-crash.json` under the local Prism folder. Diagnostic reads reject malformed/oversized content; disk failures are non-fatal. A read-only folder can prevent pending-file dismissal or preference persistence.
- The shared trace includes known code identifiers only. Frames from other assemblies and compiler-generated method names are omitted. This reduces diagnostic detail intentionally; exception messages are never read.
- GitHub templates must reach the repository's default branch before the corresponding web forms work. This change does not merge `develop` into `main` or publish a release.

The reminder sequence is settled by the owner's 2026-11-05 answer and is not an open decision.

## Implementation and verification

Review and delivery issue: [#59](https://github.com/chrizpro/prism/issues/59).

Relevant implementation: [public report URLs](../../../Prism/Core/AppInfo.cs), [Help menu](../../../Prism/Views/MainView.axaml.cs), [header](../../../Prism/Views/MainView.axaml), [desktop window](../../../Prism/Views/MainWindow.axaml.cs), [preferences](../../../Prism/Core/AppSettings.cs), [app lifetime](../../../Prism/App.axaml.cs), and [crash logging](../../../Prism.Desktop/Program.cs).

Maintained tests: [reminder, diagnostic and storage Unit tests](../../../Prism.Tests/Unit/Reporting/ReportingTests.cs) and [reporting dialog Headless tests](../../../Prism.Tests/Headless/Reporting/ReportingDialogTests.cs). They link back here and use `Feature=Reporting`, `Layer` and stable `RPT-` test IDs. The tests cover the schedule/persistence, duplicate/cancelled session requests, Windows/macOS/Linux/UNC paths in messages, custom frames, diagnostic bounds, and approval before browser handoff. Desktop evidence and remaining gaps belong in [#59](https://github.com/chrizpro/prism/issues/59).

Run with `dotnet test Prism.Tests/Unit/Prism.UnitTests.csproj -c Release --filter Feature=Reporting` and the equivalent Headless project command. Desktop integration guidance: [Integration README](../../../Prism.Tests/Integration/README.md).

Interactive prompts are skipped when `PRISM_AUTOMATION=1` or `PRISM_AUTOMATION_DISCOVERY` is set. Runtime verification returns before app lifetime initialization. `PRISM_REPORTING_DIRECTORY` redirects pending diagnostics for isolated verification; `PRISM_SETTINGS_FILE` redirects settings. The reminder counter advances only after an actual normal window closure; OS shutdown does not show a reminder or increment it.

2026-11-05 verification on the configured Windows workstation:

- `dotnet build Prism.Desktop/Prism.Desktop.csproj -c Release --no-restore -o artifacts/tests/reporting/bin` succeeded. The normal output directory was locked by two existing Prism sessions, which were left running. Existing compiler warnings and the existing Tmds.DBus.Protocol vulnerability warning remain.
- `dotnet test Prism.Tests/Unit/Prism.UnitTests.csproj -c Release --no-restore --logger "trx;LogFileName=reporting-unit.trx" --results-directory artifacts/tests/reporting` passed **85 cases**, including **9 Reporting cases**, no failures/skips. TRX is local under `artifacts/tests/reporting`.
- `dotnet test Prism.Tests/Headless/Prism.HeadlessTests.csproj -c Release --no-restore --filter Feature=Reporting --logger "trx;LogFileName=reporting-headless.trx" --results-directory artifacts/tests/reporting` passed **4 Reporting cases**, no failures/skips. This exercised real Avalonia dialogs with a stubbed browser and the installed Windows native runtime available; no native geometry/solver scenario was run.
- Actual desktop checks with isolated settings confirmed Help-only report links, the next-launch review for a **synthetic** pending diagnostic, dismissal without browser handoff, first-close feedback, and persisted count `1` after **Not now**. The pending diagnostic was removed on dismissal. [Screenshots and source details](evidence/README.md) include the actual viewport behind each dialog; no real crash was induced or report submitted.
- Issue-form YAML parsed successfully, field IDs were unique, local guide/evidence links resolved, and `git diff --check` passed for the changed tracked files. `npm run build` in `site` was blocked by the existing stale ribbon-catalog provenance check (the changed ribbon sources belong to concurrent work). A direct `npx vitepress build` succeeded in 128 seconds, verifying the updated pages without claiming catalog freshness.
- Remaining verification gaps: actual OS shutdown/cancelled save-handler integration, a real unhandled crash, macOS/Linux UI, and the published GitHub form handoff. The form templates are local until publication to the default branch. Query prefilling follows [GitHub's documented issue-form URL support](https://docs.github.com/en/issues/tracking-your-work-with-issues/using-issues/creating-an-issue#creating-an-issue-from-a-url-query).
