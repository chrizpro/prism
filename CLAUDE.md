# Claude instructions for Prism

Follow [Prism's shared requirements and testing workflow](docs/engineering/requirements-and-testing.md) and the repository's existing `README.md` and feature docs. That shared page is authoritative if agent notes disagree.

**Requirements first:** propose observable behavior and acceptance criteria in the appropriate existing Markdown guide, clearly label unapproved requirements, and obtain owner review before implementing behavior or writing tests against it. Do not infer product intent from current code.

Follow the shared [feature-documentation and test maintenance flow](docs/engineering/requirements-and-testing.md#feature-documentation-and-test-maintenance): keep canonical README/guide requirements current in the same change as feature work, maintain links between the document, test classes and relevant issues, and keep detailed cases in the facts/traits rather than a duplicate option matrix.

Maintained tests live in the solution-managed `Prism.Tests/` Unit/Headless projects; preserve older executable suites under `tests/`. Existing-behavior characterization is authorized; new/ambiguous requirements still need review. Integration is deferred. Do not introduce Gherkin or assume a conversion of older suites.

Current owner direction is direct work on `develop`, as recorded in the shared workflow. `main` is the public branch that publishes the website and releases; do not commit to it unless the owner asks for a merge. Preserve unrelated local changes and generated assets. Report exactly which checks ran, failed or remain unverified, including native workstation requirements.

**Visual scripts:** to find bugs in tools and options, follow the [visual feature scripts technique](docs/engineering/requirements-and-testing.md#visual-feature-scripts-testsvisual): scripted scenarios with known answers, small pictures, pointer-driven mouse tools, `KNOWN ISSUE` lines for filed bugs, full command coverage via `tests/visual/coverage.py`.
