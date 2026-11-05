# SWE-2 / Devin Desktop instructions for Prism

Read [the shared requirements and testing workflow](docs/engineering/requirements-and-testing.md), `README.md`, and the relevant existing docs before acting. This is the handoff guide for Devin Desktop / SWE-2; do not maintain a second copy of feature requirements here.

Follow the shared [feature-documentation and test maintenance flow](docs/engineering/requirements-and-testing.md#feature-documentation-and-test-maintenance): read the canonical feature README/guide, obtain owner decisions for new or changed requirements, implement authorized work, validate locally, and update the living document and links in the same change. Keep ambiguous requirements **Proposed — awaiting owner review**. Existing-behavior characterization is separately authorized.

The local desktop can have OCCT, native DLLs, solver tools and UI dependencies unavailable in cloud sessions. Inspect the existing `tests/` executable projects, run only applicable checks in the configured environment, and record exact results. Never report an unrun test as passing.

Keep feature documents, maintained test classes and relevant issues linked at file/folder level. Individual cases remain in descriptive facts and `Feature`, `Layer`, `TestId` traits. Do not maintain option-to-test matrices or introduce Gherkin files. Maintained tests belong under the solution-managed `Prism.Tests/` projects; preserve older standalone `tests/` suites.

Current owner direction is direct work on `develop`, as recorded in the shared workflow. `main` is the public branch that publishes the website and releases; do not commit to it unless the owner asks for a merge. Preserve unrelated local changes. Integration is deferred; when introduced, attach representative viewport images to the relevant issue with test ID and source revision.
