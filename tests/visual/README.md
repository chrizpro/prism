# Visual review scripts

Scripts that build things in Prism through its API, take pictures and save them for a person to look at. They are meant to be run later **without an AI**:

```
python tests/visual/visual.py --discovery <discovery.json>            # every scenario (about 8 minutes)
python tests/visual/visual.py --discovery <discovery.json> --only extrude mold --fast
python tests/visual/visual.py --list
python tests/visual/visual.py --discovery <discovery.json> --compare   # how far each sheet is from its approved copy
```

Start Prism with a private `PRISM_AUTOMATION_DISCOVERY` (as for the gallery scripts); set `PRISM_CCX` for scenarios that need CalculiX. Run **one** visual run at a time: two runs on one app mix up their pictures (the runner refuses to start a second).

## Re-testing everything with few tokens

1. Run the whole set (about 10 minutes, no AI needed): `python tests/visual/visual.py --discovery <discovery.json> --compare`.
2. Read only `artifacts/visual/summary.txt`: one line per scenario (`ok` / `FAIL` / `CRASHED`), then the failing check lines with their numbers. The exit code is non-zero if anything failed. This is a few dozen lines, so an AI can read it for almost nothing.
3. Open a picture only when it matters: a failing scenario, or one whose `[n from approved]` difference is large. Approved sheets and unchanged scenarios never need to be looked at.

When asking an AI to do this, say: *run the visual scripts, read summary.txt, look only at the failing or changed sheets, and fix what is broken.*

## Finding flaky tests

- `--repeat N` runs the whole selection N times and reports every check whose result or numbers differ between runs (`UNSTABLE` in `summary.txt`, with both values), and exits non-zero if any did.
- `--shuffle SEED` runs the scenarios in a random (seeded) order, which finds tests that only pass because of what ran before them. Use it with `--repeat`: each repetition gets a new order.
- `--slow` adds the scenarios that take minutes (topology optimization).
- A good soak before a release: `python tests/visual/visual.py --discovery <json> --fast --repeat 3 --shuffle 1`.

What this does not cover: timing races that need a person's speed, such as clicking while a preview is still computing; those need the Windows desktop tests.

## What a sheet is

One scenario = one **contact sheet**, a small PNG named for what it tests, with several labeled views and a text strip of checks (each line `PASS` or `FAIL` with the numbers behind it). The files go to `artifacts/visual/` (ignored by git), with a `report.json` of sizes and timings. A scenario that crashes is reported as `FAILED` and the others still run.

Naming: `<area>__<feature-and-what-varies>.png`, for example `solid__extrude-every-extent-and-operation.png`.

Two kinds of scenarios:

- **Feature matrices** (`solid__extrude-every-extent-and-operation`): every option of one tool, each variant built beside the others in one document, so one picture shows them all and each has a numeric check. This is the kind to grow: one per tool.
- **Workflows** (the gallery and CAM/CAE sheets): a finished model through its real workflow.

## Approving and comparing

Look at a sheet; when it is right, copy it into `tests/visual/approved/`. `--compare` then prints a mean pixel difference (0 to 255) between the new sheet and the approved one. Path-traced renders are noisy, so only large differences matter; a later test can assert a threshold. Only approved sheets belong in git (about 60 to 100 KB each, so a few MB for the whole set). Re-approve rarely: every change to a committed PNG adds its size to the history again.

## How much is covered

`python tests/visual/coverage.py` counts which registered commands the scripts touch (`--list` names the rest). Touching is weaker than testing every option: only the feature-matrix scenarios vary options.

## Limits

Prism's renderer does not draw toolpaths, reference pictures, curves or analysis colours, and the window screenshot works only on Windows. So CAM scenarios plot the toolpath from the posted G-code, CAE scenarios print their numbers into the strip, and curves are checked numerically.
