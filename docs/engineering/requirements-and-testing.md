# Prism requirements, GitHub and testing workflow

This is the shared working agreement for humans and coding agents. It describes the process, **not** approved behavior for any Prism feature.

**Current owner override (2026-11-04):** work on the `develop` branch. `main` is the public, stable branch: pushes to it publish the website, and release tags are cut from it. Merge `develop` into `main` only when the owner says the work is ready. Do not commit directly to `main`. Feature branches and pull requests are not required until the owner reinstates that workflow. This overrides branch/PR requirements below and in agent-specific instructions. Preserve unrelated local work and continue running relevant checks. The owner has also authorized tests characterizing existing observable behavior; unresolved new product requirements still require review.

## Sources of truth

- `README.md`: project setup and orientation.
- `docs/`: human-readable feature behavior, design notes, known limitations and verification guidance. Use the [documentation index](../README.md) to find the domain. Keep shared modeling tools in one feature guide with variant sections. Link to those sections from the [ribbon index](../tabs/README.md) as needed.
- GitHub Issues: time-bounded stories, bugs, changes, questions and decisions, with links to canonical feature documentation. A closed issue documents what was delivered at that time; it is not the living specification.
- Maintained solution-managed tests under `Prism.Tests/` and older executable checks under `tests/`: what is actually implemented and verified; tests do not define product intent. See [the maintained test guide](../../Prism.Tests/README.md) for Unit, Headless and planned Integration coverage.
- Pull requests: reviewable changes linking the relevant issue(s), documentation and verification.

**Proposed behavior is not approved behavior.** If the owner has not approved new or changed requirements, mark them **Proposed — awaiting owner review** and do not implement or test them as settled behavior.

## Feature documentation and test maintenance

The feature's canonical README/guide is the living source of truth. Guides such as [Revolve](../modeling/revolve/README.md) and [Extrude](../modeling/extrude/README.md) document distinct variants in separate sections. Avoid parallel copies of requirements.

Agents must follow this flow for feature, bug-fix and test work:

1. **Read:** locate the canonical feature document and relevant variant issue before editing code or tests. Check whether behavior is approved, existing authorized characterization, or a new proposal.
2. **Decide:** obtain owner review for new/changed or ambiguous product requirements. Existing observable behavior may be characterized under the current authorization; passing tests do not convert proposals into approved requirements.
3. **Change:** implement authorized work and add focused maintained Unit/Headless tests under `Prism.Tests/` as needed. Preserve older executable suites. Use descriptive facts and existing `Feature`, `Layer`, `TestId` traits to explain cases.
4. **Maintain the living document:** in the same change, update affected options, intended/observed behavior, limitations, open decisions and verification information. If behavior is unchanged, check the document for accuracy and update links/coverage gaps where needed; do not rewrite unrelated text or invent approved requirements from the implementation.
5. **Keep the links:** the feature document links to relevant test files or folders, the test class links back to the canonical document in its XML documentation, and the issue links to that document and feature-level tests. Update these links whenever files move. Do not duplicate every option/test in an issue checklist or traceability matrix.
6. **Verify and finish:** run applicable checks, validate changed links, and record actual dated evidence and remaining gaps. Follow the current `develop` branch override; do not stage unrelated local work. Check the feature document and issue references before calling the task complete.

Feature documents describe current behavior and intended requirements; issues preserve a defined work scope/history, and tests provide executable evidence. Keep these responsibilities distinct. For desktop integration, attach representative viewport images to the relevant issue with test ID and source revision.

## Testing at a library boundary

When a feature hands work to a third-party library or external program that is slow, needs a separate install or cannot run on every machine (CalculiX, Netgen, the native solvers), test Prism's side of the hand-off instead of, or as well as, the full run: the input Prism builds (units, element types, which nodes and faces, totals such as applied force or heat), its validation of impossible setups, and the parsing of the program's output from a small sample. These tests are fast, run without the dependency, and catch the mistakes most likely to be ours. Benchmarks against independent hand calculations remain the evidence that the whole chain is right. A one-line class summary saying which hand-off a test class covers is enough; do not add comments to every test.

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

For important, independently checkable rules use a stable ID such as `SKETCH-001` or `CAE-THERMAL-001`; do not renumber IDs or assign one to every trivial case. Use an ID only after the behavior is sufficiently specified. Maintained tests under `Prism.Tests/` already use `Fact`/`AvaloniaFact` display names plus `Feature`, `Layer` and `TestId` traits. Test IDs identify tests, not approved requirements. Additional `Requirement` traits and automatic requirement-coverage tooling remain optional future proposals. **Do not assume the older executable projects under `tests/` are xUnit.**

## Living specifications and immutable delivery history

**The latest owner-approved Markdown on the default branch describes current intended behavior.** The specification can evolve; a closed story should not be rewritten to describe behavior delivered by later stories.

- A feature may have a bookkeeping **feature story** that links its Markdown specification, relevant implementation and real existing test paths. Distinct implementations (for example Solid versus Freeform Extrude) may have distinct stories, even when they share a tool name. No one-issue-per-requirement or one-issue-per-test rule.
- New requirements, enhancements and corrections get **new stories, change issues or bugs**. Closed issues preserve their original scope and acceptance context; do not edit old acceptance criteria to retroactively match new behavior. If a closed issue contains incorrect administrative information, clarify it with an explicitly dated comment instead.
- Proposed behavior gets **documentation review first** when substantial or ambiguous. After owner approval, delivery updates the living Markdown and authorized code/tests. Use documentation/delivery PRs when that workflow is in effect; do not mark pending requirements as approved merely because a PR exists.
- A delivery issue should reference a **permanent commit SHA or release tag** that captures its accepted specification, not only a moving `main` link. The issue can additionally link the current spec for convenient navigation; clearly label the two.
- Git's diff, blame and PR history are the primary audit trail. Add concise **why/decision** notes for meaningful semantic changes; do not duplicate a full changelog in every requirement row. Keep stable IDs when the requirement means the same thing; replace or retire an ID when its meaning fundamentally changes.
- Bugs found later get new bug issues and link to the relevant feature story or doc; they do not reopen or rewrite the original delivery story by default.
- GitHub sub-issues may be used where practical, but link relationships must remain understandable even when a particular agent connector cannot manipulate the native hierarchy. Avoid keeping completed stories open solely to behave as permanent feature records.

## Review gate: requirements before tests

1. Identify the affected docs and existing behavior. Classify changes as bug fix, existing approved behavior, or proposed behavior.
2. Draft documentation and acceptance criteria; flag unknowns.
3. Request owner review for new/changed behavior before writing or generating tests that encode those assumptions.
4. Implement approved behavior and add/adjust focused tests when authorized. Explicitly authorized characterization of existing observable behavior can proceed without approving new requirements.
5. Run appropriate checks on a machine with the actual SDK/native dependencies; record exact commands and pass/fail/blocked results. Do not claim green checks when not run.
6. Complete the feature-documentation maintenance flow and follow the current branch/commit policy. When PRs are in use, link issues, approved docs and verification evidence for owner review.

The original workflow-setup restriction on all code/tests applied to that setup change, not subsequent owner-authorized feature work. Do not create GitHub Actions test jobs that will predictably fail without the native environment; CI enhancements need a separately reviewed plan.

## GitHub conventions

Issue bodies should link the canonical doc and relevant tests *when those tests exist*. A closed delivery issue should also link to the version of the approved spec it actually delivered (commit SHA or release tag). Do not create a separate issue for every test result. A bug that merits tracking should have reproduction steps, observed and expected behavior, environment and acceptance criteria; record uncertainty rather than guessing. A feature proposal should include alternatives and an explicit owner decision.

Use the same story sections: **Purpose → Scope → Specification → Acceptance criteria → Tests and evidence → Open decisions**. Group related issues with a simple feature label, such as `extrude` or `revolve`; a parent/epic is not required. Link to relevant test files or feature folders once per layer. Individual options, expected results and cases belong in the test code and its existing names/traits, rather than a manually maintained issue-to-option or issue-to-test matrix. Clearly identify variants with no maintained tests and distinguish legacy executable coverage.

The owner has authorized desktop integration testing. Attach representative actual viewport images to the relevant issue with the test ID and source revision. Record dated execution evidence separately from acceptance criteria; do not imply that a historical pass count is a live test status.

Follow the current owner branch policy, presently direct work on `develop`. When PRs are in use, keep them focused and use `Fixes #N` only when the change actually resolves that issue; otherwise use `Refs #N`. A change that alters behavior should update the existing specification in the same change after approval. If a test and doc disagree, raise the discrepancy rather than silently changing either.

## Local validation versus cloud validation

Prism uses .NET 10, Avalonia, native Open CASCADE and additional native/CAE dependencies. `README.md` describes build prerequisites. The existing `tests/` directory includes executable .NET projects invoked with `dotnet run --project tests/<project> -c Release`. These are **not necessarily xUnit**. Some checks require native DLLs staged under `native/bin`, external solvers, or Windows GUI resources; validate those on the owner's configured desktop. GitHub's existing `.github/workflows/docs.yml` builds the documentation site, not a full native application test matrix.

On an environment without those dependencies, review source, Markdown, references and lightweight static checks; explicitly report unexecuted native tests. Never fabricate test results.

## First feature-review pilot

- [Solid Extrude draft](../modeling/extrude/README.md#solid-extrude) and [review story #6](https://github.com/chrizpro/prism/issues/6) demonstrate the living-specification approach.
- The draft is **not approved**, no requirement IDs have been assigned, and no new tests should be written solely from its proposals.
- Solid, Surface and Freeform extrusion are distinct feature behaviors even where the same command name appears.

## Agent handoff

All coding agents should read this file and the relevant existing docs before changing behavior. Agent-specific entry points are `AGENTS.md` (Codex and general agents), `CLAUDE.md` (Claude), and `DEVIN.md` (SWE-2 / Devin Desktop). Keep substantive policy here to prevent agent instructions from drifting.
