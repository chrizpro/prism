# Prism agent instructions (Codex and other coding agents)

Start with [the shared requirements and testing workflow](docs/engineering/requirements-and-testing.md), then read `README.md` and existing relevant feature documentation under `docs/`.

- Reuse the repo's documentation and test conventions; do not introduce Gherkin/feature files or an assumed xUnit conversion.
- **New or changed feature requirements require owner review before tests or implementation encode them.** Mark unreviewed behavior as proposed and list open decisions.
- Before any code/test task, link or identify the approved specification and relevant GitHub Issue; if approval is missing, limit work to documentation proposals.
- Do not generate unit tests simply to fill coverage. For this workflow-setup change, add no tests and change no production code.
- Current owner instruction (2026-11-03): work directly on `main` for now, without requiring feature branches or PRs. Preserve unrelated local changes and include actual verification evidence. Return to a branch/PR workflow when the owner requests it.
- Native OCCT/CAE/Avalonia tests may require the configured Windows workstation. Don't claim cloud validation ran when it didn't.

For stable requirement IDs, test links, issue/PR templates, and review gates, follow the shared workflow linked above.
