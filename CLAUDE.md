# Claude instructions for Prism

Follow [Prism's shared requirements and testing workflow](docs/engineering/requirements-and-testing.md) and the repository's existing `README.md` and feature docs. That shared page is authoritative if agent notes disagree.

**Requirements first:** propose observable behavior and acceptance criteria in the appropriate existing Markdown guide, clearly label unapproved requirements, and obtain owner review before implementing behavior or writing tests against it. Do not infer product intent from current code.

Use GitHub Issues for actionable work and PRs for review, linking the canonical documentation. Reuse existing `tests/` projects and their actual conventions rather than assuming xUnit or introducing `.feature` files. The native geometry and CAE stack may only be runnable on the owner's Windows workstation; report precisely what was and wasn't validated.

For the documentation-workflow setup, avoid application changes and new tests. Preserve existing docs, generated-catalog workflows, and test assets.
