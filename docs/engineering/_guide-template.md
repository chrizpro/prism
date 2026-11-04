# [Feature or tab name]

> **Documentation status:** Draft / Existing behavior under review / Reviewed (record owner decision and link).
>
> This page describes product behavior. It is not, by itself, evidence of approval, implementation completeness, or passing tests.

## Purpose and scope

What problem does this tab or feature solve? How does the user reach it? What is intentionally outside scope?

## Workflow

Describe the normal user path in plain language. Link shared behavior to its canonical guide instead of duplicating it.

## Tools and current behavior

| Section / tool | What it does today | Implementation maturity | Notes / limitations |
| --- | --- | --- | --- |
| Example | Observable behavior | Done / Thin / Gap / Unassessed | Keep this grounded in inspected behavior |

## Requirements review

Only write concrete acceptance criteria after the owner has reviewed intended behavior. Until then, mark rows proposed, never infer approval from existing implementation.

| ID (optional) | Proposed or approved behavior | Review state |
| --- | --- | --- |
| — | [Describe the user-visible behavior] | Proposed — awaiting owner review |

### Decisions needed

- [Record ambiguous behavior, options, tradeoffs, and a link to the owner's decision.]

## Verification and traceability

| Requirement / behavior | Existing test or validation | Last observed result |
| --- | --- | --- |
| [Approved behavior or ID] | [Actual verified path / command, or None] | Not run / Passed / Failed / Blocked, environment and date |

Do not create a test just to fill the table; do not state a historical result as current CI status. Link tests that actually exist. For native OCCT, CAE or UI work, note the desktop dependencies when relevant.

## Known limitations

Describe current gaps accurately. Link actionable work to GitHub Issues as they are created.

## Related stories and change history

- **Feature story:** [Issue #...]; link the current spec for navigation.
- **Delivered versions:** [Story #...] → [commit SHA/tag containing its approved specification]. Don't rewrite closed-story acceptance criteria after future changes.
- **Later changes / bugs:** [New linked issues, if any]. Keep issue lists at feature level instead of one column per requirement.
- **Significant rationale:** [Short explanation of semantic change; rely on Git history for exact diffs].

## Related resources

- [Existing canonical guide or issue]
- [Implementation or test project if verified]
