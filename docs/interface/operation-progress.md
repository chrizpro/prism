# Operation progress

Owner requested consistent progress for slow work on 2026-11-11, [#126](https://github.com/chrizpro/prism/issues/126). The shared operation strip appears at the bottom of the window for the active document. Each job has its own title, stage and progress bar. Known fractions use actual samples, frames or reported import progress; unknown stages use an indeterminate bar.

Stop appears only for cancellable work. It requests cooperative cancellation, displays “Stopping after the current step…” and stays until that job exits. Unsupported cancellation has no Stop button. Completion, cancellation and failure remove the running item; existing tool/result status retains the outcome. Concurrent jobs own independent entries; completed jobs ignore late messages. Switching documents shows that document's work.

Integrated sources: image/animation rendering, static meshing/solving, Thermal and Natural Frequencies, Motion, Flow, Generative Design, CAD import, mesh reading/preparation, point-cloud reading/reconstruction and mesh refinement. Animation rendering reports frame/sample progress, with indeterminate GIF/MP4 stages. Static solvers, CAD import and point-cloud reading do not gain cancellation support from this display.

This presents existing background work; synchronous native preview/geometry calls can still pause the UI and need tool-specific scheduling. Cross-document aggregate progress and finished-job history remain outside this change.

Implementation: [OperationProgress](../../Prism/Core/OperationProgress.cs). Maintained checks: [usability workflows](../../Prism.Tests/Headless/Interface/UsabilityWorkflowTests.cs), [Motion workflows](../../Prism.Tests/Headless/Assembly/MotionWorkflowTests.cs). Manual checks: [interface checklist](../tabs/checklists/interface-manual-test-checklist.md).

## Verification — 2026-11-11

Windows Release passed 134 Headless checks across Interface, Assembly, Motion, Rendering, CAE, Mesh, Fluids and Ribbon (excluding the existing RibbonHelpTests gaps tracked in [#127](https://github.com/chrizpro/prism/issues/127)). UX-H001–005 exercise empty/pending part return, real grounding/joint transitions, workspace/ribbon availability, concurrent/late operation messages and stopping an actual renderer job. MOT-H002 checks Motion's shared progress lifecycle. Ribbon section checks cover both themes. Desktop build: zero errors, nine existing warnings. Generated ribbon catalog freshness and whitespace checks passed.

Native UX-I001 on the gallery orrery confirmed readable [assembly status and modeling tabs](evidence/UX-I001-assembly.png), the [Mold inspection tools](evidence/UX-I001-mold.png), and [materials-first rendering with live progress](evidence/UX-I001-render-progress.png). The render was stopped after three of 4096 samples. The source revision and commands are recorded in [#126](https://github.com/chrizpro/prism/issues/126). Live solver/reconstruction cancellation cases, small-window/dark progress layout and pointer-driven part finishing remain manual checks; no new full solver certification is claimed.
