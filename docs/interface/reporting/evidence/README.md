# Reporting desktop evidence — 2026-11-05

Canonical guide: [Feedback, bug and crash reporting](../README.md). Delivery issue: [#59](https://github.com/chrizpro/prism/issues/59).

Captured from an actual Windows Prism desktop instance built into `artifacts/tests/reporting/bin`, with isolated `PRISM_SETTINGS_FILE` and `PRISM_REPORTING_DIRECTORY`. Existing user sessions and preferences were left untouched. No report was sent to GitHub.

Source context: work began at `8dcec2c2bacddac06635e3c56f02dcc9535da06c`; the shared checkout advanced to `fe9cc170bd26674a7d16f3bd8c90609f91105f19` during work. These images include the uncommitted reporting changes; neither revision alone represents their complete source. See `source-hashes.json` for the captured source/binary fingerprint. Unrelated ongoing Drawing/Appearance work was preserved.

After capture, the browser helper was corrected to accept a successful shell handoff that returns no new process (an existing browser may handle the URL). The four Headless cases were rerun and passed. This correction does not change the captured layouts.

| Test ID | Evidence | Observation |
| --- | --- | --- |
| RPT-D001 | [Actual viewport with crash review](RPT-D001-crash-review-0.png), [dialog detail](RPT-D001-crash-review-1.png) | A synthetic, prepared diagnostic was loaded on startup. The trace is read-only and has no paths/messages. Dismiss returned to the app and removed the pending diagnostic; no browser was opened. This is UI evidence, not a real crash capture. |
| RPT-D002 | [Help menu and actual viewport](RPT-D002-help-menu.png) | Report a bug and Give feedback appear in Help; no standalone report button remains in the header. |
| RPT-D003 | [First-close feedback with actual viewport](RPT-D003-feedback.png) | Normal first closure displayed the optional invitation, unchecked Don't show again, Not now and Give feedback. Not now completed closure and persisted ClosureCount=1, Disabled=false. |

Automated verification: 85 Unit cases passed (9 Reporting), and 4 focused Headless reporting dialog cases passed. Source-linked test classes and exact commands are in the canonical guide. Repeated-reminder/opt-out/payload approval cases are automated evidence; they are not claimed as manually repeated desktop interactions.

GitHub attachment upload is unavailable through the connected GitHub tools, and the fallback browser session is signed out. Images are saved here for review and publication with the change; the issue records that attachment limitation explicitly.
