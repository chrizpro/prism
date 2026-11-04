# Prism agent instructions (Codex and other coding agents)

Start with [the shared requirements and testing workflow](docs/engineering/requirements-and-testing.md), then read `README.md` and existing relevant feature documentation under `docs/`.

- Treat the canonical feature README/guide as the living source of truth. Read it before feature work and update affected behavior, options, limits, open decisions and verification links in the same change. Follow [the maintenance flow](docs/engineering/requirements-and-testing.md#feature-documentation-and-test-maintenance).
- Keep links in both directions: feature README/guide → relevant test files/folders, and maintained test classes → canonical feature document. Issues link to the same document and feature-level tests. Use descriptive facts and `Feature`, `Layer`, `TestId` traits; do not maintain per-option/per-test link matrices.
- Put maintained tests in the solution-managed `Prism.Tests/` projects. Preserve older standalone `tests/` programs; do not assume an xUnit conversion or introduce Gherkin/feature files.
- **New or changed feature requirements require owner review before tests or implementation encode them.** Mark unreviewed behavior as proposed and list open decisions.
- Before code/test work, identify the canonical specification and relevant feature issue. Existing observable behavior may be characterized under the owner's 2026-11-03 authorization; mark unsettled requirements as proposed and do not silently change product behavior to satisfy them.
- Add meaningful tests, not tests simply to fill coverage. Integration tests are authorized; representative viewport evidence should be attached to the relevant issue with test ID and source revision.
- Current owner instruction (2026-11-03): work directly on `main` for now, without requiring feature branches or PRs. Preserve unrelated local changes and include actual verification evidence. Return to a branch/PR workflow when the owner requests it.
- Native OCCT/CAE/Avalonia tests may require the configured Windows workstation. Don't claim cloud validation ran when it didn't.

For stable requirement IDs, test links, issue/PR templates, and review gates, follow the shared workflow linked above.
