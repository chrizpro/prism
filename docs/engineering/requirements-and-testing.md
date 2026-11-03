# Prism requirements, GitHub and testing workflow

This is the shared working agreement for humans and coding agents. It describes the process, **not** approved behavior for any Prism feature.

## Sources of truth

- `README.md`: project setup and orientation.
- `docs/`: human-readable feature behavior, design notes, known limitations and verification guidance. Prefer extending existing pages (for example `docs/tabs/`, `docs/cae-studies/`, and `docs/gizmo-workflows/`) instead of creating duplicate documents.
- GitHub Issues: time-bounded stories, bugs, changes, questions and decisions, with links to canonical feature documentation. A closed issue documents what was delivered at that time; it is not the living specification.
- Code and executable checks under `tests/`: what is actually implemented and verified; tests do not define product intent.
- Pull requests: reviewable changes linking the relevant issue(s), documentation and verification.

**Proposed behavior is not approved behavior.** If the owner has not approved new or changed requirements, mark them **Proposed — awaiting owner review** and do not implement or test them as settled behavior.

## Minimal feature document pattern

Use an existing document when practical. A new dedicated Markdown page is justified for sufficiently complex behavior; no mandatory one-file-per-tool rule.

1. **Purpose / scope** — what the feature is for, what it is not.
2. **Status** — Proposed / Approved / Implemented / Verified, with an optional review date or linked approval issue. These labels describe distinct states, not one linear guarantee.
3. **Expected behavior** — concrete observable rules and edge cases, including units, tolerances and failure handling where relevant.
4. **Open decisions** — unresolved choices explicitly flagged for the owner.
5. **Acceptance criteria** — stable, reviewable statements (do not use persistent checkmarks as live test results).
6. **Implementation / verification links** — relevant code areas, test project paths and GitHub Issues/PRs when known.
7. **Known limitations** — distinguish verified behavior from assumptions.

Do not invent requirements from an implementation, a test name, or an AI suggestion. Ask for a decision or leave an explicit TODO. Keep docs concise and avoid duplicating long specifications in GitHub Issues.

### Optional requirement identifiers

For important, independently checkable rules use a stable ID such as `SKETCH-001` or `CAE-THERMAL-001`; do not renumber IDs or assign one to every trivial case. Use an ID only after the behavior is sufficiently specified. A future xUnit test could carry multiple `[Trait("Requirement", "SKETCH-001")]` attributes, but **do not assume Prism's existing executable test projects are xUnit**. Test metadata and requirement coverage tooling are future proposals, not installed infrastructure. No automatic traceability is claimed yet.

## Living specifications and immutable delivery history

**The latest owner-approved Markdown on the default branch describes current intended behavior.** The specification can evolve; a closed story should not be rewritten to describe behavior delivered by later stories.

- A feature may have a bookkeeping **feature story** that links its Markdown specification, relevant implementation and real existing test paths. Distinct implementations (for example Solid versus Freeform Extrude) may have distinct stories, even when they share a tool name. No one-issue-per-requirement or one-issue-per-test rule.
- New requirements, enhancements and corrections get **new stories, change issues or bugs**. Closed issues preserve their original scope and acceptance context; do not edit old acceptance criteria to retroactively match new behavior. If a closed issue contains incorrect administrative information, clarify it with an explicitly dated comment instead.
- Proposed behavior is reviewed in a **documentation-only PR** first when substantial or ambiguous. After owner approval, a delivery PR updates the living Markdown and authorized code/tests. Do not mark pending requirements as approved merely because a PR exists.
- A delivery issue should reference a **permanent commit SHA or release tag** that captures its accepted specification, not only a moving `main` link. The issue can additionally link the current spec for convenient navigation; clearly label the two.
- Git's diff, blame and PR history are the primary audit trail. Add concise **why/decision** notes for meaningful semantic changes; do not duplicate a full changelog in every requirement row. Keep stable IDs when the requirement means the same thing; replace or retire an ID when its meaning fundamentally changes.
- Bugs found later get new bug issues and link to the relevant feature story or doc; they do not reopen or rewrite the original delivery story by default.
- GitHub sub-issues may be used where practical, but link relationships must remain understandable even when a particular agent connector cannot manipulate the native hierarchy. Avoid keeping completed stories open solely to behave as permanent feature records.

## Review gate: requirements before tests

1. Identify the affected docs and existing behavior. Classify changes as bug fix, existing approved behavior, or proposed behavior.
2. Draft documentation and acceptance criteria; flag unknowns.
3. Request owner review for new/changed behavior before writing or generating tests that encode those assumptions.
4. Only after approval, implement and add/adjust focused tests in the repository's established style, when explicitly authorized.
5. Run appropriate checks on a machine with the actual SDK/native dependencies; record exact commands and pass/fail/blocked results. Do not claim green checks when not run.
6. Open a PR referencing issues, approved docs, and verification evidence. Owner reviews and merges.

**Current instruction for this setup PR:** do not add tests or modify application behavior. Do not create GitHub Actions test jobs that will predictably fail without the native environment. CI enhancements need a separately reviewed plan.

## GitHub conventions

Issue bodies should link the canonical doc and relevant tests *when those tests exist*. A closed delivery issue should also link to the version of the approved spec it actually delivered (commit SHA or release tag). Do not create a separate issue for every test result. A bug that merits tracking should have reproduction steps, observed and expected behavior, environment and acceptance criteria; record uncertainty rather than guessing. A feature proposal should include alternatives and an explicit owner decision.

Keep PRs focused; work on a branch instead of pushing directly to `main`. Use `Fixes #N` only when the PR actually resolves that issue; otherwise use `Refs #N`. A change that alters behavior should update the existing specification in the same PR after approval. If a test and doc disagree, raise the discrepancy rather than silently changing either.

## Local validation versus cloud validation

Prism uses .NET 10, Avalonia, native Open CASCADE and additional native/CAE dependencies. `README.md` describes build prerequisites. The existing `tests/` directory includes executable .NET projects invoked with `dotnet run --project tests/<project> -c Release`. These are **not necessarily xUnit**. Some checks require native DLLs staged under `native/bin`, external solvers, or Windows GUI resources; validate those on the owner's configured desktop. GitHub's existing `.github/workflows/docs.yml` builds the documentation site, not a full native application test matrix.

On an environment without those dependencies, review source, Markdown, references and lightweight static checks; explicitly report unexecuted native tests. Never fabricate test results.

## Agent handoff

All coding agents should read this file and the relevant existing docs before changing behavior. Agent-specific entry points are `AGENTS.md` (Codex and general agents), `CLAUDE.md` (Claude), and `DEVIN.md` (SWE-2 / Devin Desktop). Keep substantive policy here to prevent agent instructions from drifting.
