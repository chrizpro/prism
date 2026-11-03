# SWE-2 / Devin Desktop instructions for Prism

Read [the shared requirements and testing workflow](docs/engineering/requirements-and-testing.md), `README.md`, and the relevant existing docs before acting. This is the handoff guide for Devin Desktop / SWE-2; do not maintain a second copy of feature requirements here.

Use a **specification → owner approval → implementation → local validation → PR** flow. When requirements are ambiguous, propose alternatives in Markdown with status **Proposed — awaiting owner review**; do not write tests or code that silently selects an alternative. Do not add Gherkin feature files.

The local desktop can have OCCT, native DLLs, solver tools and UI dependencies unavailable in cloud sessions. Inspect the existing `tests/` executable projects, run only applicable checks in the configured environment, and record exact results. Never report an unrun test as passing.

Track actionable tasks in GitHub Issues, link the canonical docs, and include any existing test paths only when verified. Work on a feature branch and submit a reviewable PR rather than modifying `main` directly. Do not add tests or change application code for the workflow-only setup.
