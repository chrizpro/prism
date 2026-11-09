# Static structural analysis — living specification

This README is the canonical requirements document for Prism's **Static structural study**, not static code analysis. Maintain current reviewed behavior here; [Story #25](https://github.com/chrizpro/prism/issues/25) tracks its review and test links.

**Review state:** Proposed — awaiting owner review (2026-11-03). Existing behavior can be characterized under owner authorization. Passing benchmarks do not approve new requirements or certify every engineering application. Integration remains deferred.

## Scope

The initial maintained pilot covers a single exact solid, linear-elastic static meshing/solving, setup validation and numerical results through `StudyRunner`. It exercises the installed native solver, not an analytical stand-in.

Thermal, modal, topology optimization, fatigue, contact/nonlinear analysis and multi-body CalculiX workflows are outside this pilot. Their existing implementation/legacy checks are preserved. See [existing CAE studies](../../cae-studies/README.md) for historical broader work.

## Current implementation

Source: [study definitions](../../../Prism/Simulation/Study.cs), [setup references](../../../Prism/Simulation/StudySetup.cs), [StudyRunner](../../../Prism/Simulation/StudyRunner.cs), [native interface](../../../Prism.Kernel/Sim.cs) and [native solver](../../../native/sim.cpp). Ribbon entry points are in [MainViewModel.cs](../../../Prism/ViewModels/MainViewModel.cs).

| Area | Source-observed behavior |
|---|---|
| Body/material | Study references a solid/form body and requires suitable structural material data |
| Mesh | Tetrahedral meshing from tessellated geometry; quadratic 10-node tetrahedra; explicit or automatic element size |
| Fixtures | Fixed, directional component locks, or a slider approximated by its closest global axis |
| Loads | Force, pressure, torque and gravity; force supports total/per-face and explicit/normal direction |
| Units | Geometry mm; forces N; stress MPa; material Young's modulus converted from GPa to MPa; density converted for the solver |
| Results | Nodal displacement/stress/von Mises, support reactions, maximum summaries and yield-based safety factor |
| Safety factor | Uses available yield strength; capped at 1000; unavailable yield gives NaN rather than substituting ultimate strength |
| Validation | Missing body/material/fixture/load and invalid/stale face references have explicit validation paths |

The single-body path uses `prism_sim`; multi-body work uses a separate CalculiX-backed path. Neither should silently substitute for the other in test reporting.

## Independent axial-bar benchmark

Use a 10 × 10 × 100 mm bar with E = 68,900 MPa, fixed at Z=0, and total axial force 1,000 N at Z=100. The independent reference equations are:

- Cross-sectional area A = 100 mm².
- Axial stress `F/A = 10 MPa`, measured away from the constrained end.
- Axial end displacement `FL/(EA) = 0.0145137881 mm`.
- Net support reaction balances the applied 1,000 N force.
- Doubling the load on the same mesh doubles displacement and stress in a linear solve.

Reference equations: [COMSOL structural verification examples](https://doc.comsol.com/6.3/doc/com.comsol.help.sme/StructuralMechanicsVerificationExamples.pdf), axial displacement and axial stress of a beam/bar. These equations provide independent expected values, not Prism's own result as its expected answer.

The pilot comparison allows 5% error in mean loaded-end displacement and mean interior von Mises stress to accommodate finite mesh/boundary effects; reaction balance allows 1 N. Load scaling on an identical mesh uses a tighter relative comparison. These are benchmark-test tolerances, not approved universal solver accuracy requirements. Stress peaks at clamps/point loads are not compared to uniform bar stress.

## Saved studies

A study saved with a document must still run when the file is reopened. Its stored shape signature is computed on the form a save and reopen produces (the body's B-rep read back from its own text), because a body built in the same session can differ from the reloaded one in unimportant B-rep details. Before this a filleted or cut body's saved study failed with *The body geometry changed* on reopening (STA-H020). Results are not saved; the user presses Run.

## Requirements and open decisions

Proposed expectations include dimensionally consistent loads/materials, stable face references, meaningful failure without document corruption, finite results for supported well-posed cases, and sufficient diagnostics for equilibrium/mesh quality. Owner review must define supported assumptions and acceptance criteria.

Open decisions include mesh convergence targets, incompletely constrained models, singularities, stress averaging/reporting, material-data requirements, slider approximation, torque distribution, result persistence/regeneration and quantitative UI warnings. A single axial benchmark does not establish bending, torsion, near-incompressibility, contact or general mesh independence.

## Tests and evidence

- [Unit load/geometry math](../../../Prism.Tests/Unit/StaticAnalysis/) exercises supporting production vector operations without native calls.
- [Maintained headless static study tests](../../../Prism.Tests/Headless/StaticAnalysis/) exercise real study setup and native solving.
- [Solver input tests](../../../Prism.Tests/Headless/StaticAnalysis/SolverInputTests.cs) check Prism's side of each solver hand-off without solving: supports and loads given to the native static solver, the CalculiX input for thermal, natural-frequency and bonded multi-body studies (units, elements, boundaries, fluxes, film and radiation, gravity), and reading CalculiX results. They run without CalculiX installed.
- [Study setup tool tests](../../../Prism.Tests/Headless/StaticAnalysis/StudySetupToolTests.cs) cover the three fixtures, the four loads and their directions, Probe, Save / Recall Face Selection, Export CAE Results and the Pipe Flow calculator.
- Legacy [CAE checks](../../../tests/Prism.CaeTests/Program.cs) remain available and are not assumed migrated.
- [Runner and metadata](../../../Prism.Tests/README.md).

Use `./tools/test.ps1 -Feature StaticAnalysis`. Individual cases remain in descriptive facts and Feature/Layer/TestId traits. Verification must name the actual native binary/environment and distinguish executed/blocked tests; source review alone is not a pass. Solver meshes/results are disposed after tests and generated review evidence belongs under ignored `artifacts/`.

### Local benchmark evidence

On 2026-11-03, **2 Unit and 5 Headless static cases passed** on Windows/.NET SDK 10.0.102 using installed native binaries, without rebuilding native code. The full maintained collection passed 14 Unit and 39 Headless cases. A subsequent static-only run also passed all 5 cases and recorded these values in test output/TRX:

| Quantity | Independent reference | Measured |
|---|---|---|
| Mean end extension | 0.0145137880987 mm | 0.0144453510335 mm |
| Mean interior von Mises stress | 10 MPa | 10.0000000367 MPa |
| Support reaction Z | −1000 N | −1000 N |

This run used 934 nodes and 388 quadratic tetrahedra. The extension differs by about 0.47% from the ideal bar value; the fixed-face boundary and finite mesh are part of this benchmark. No convergence claim follows from one mesh. The pressure, same-mesh load scaling and setup-rejection cases also passed. Local review artifacts are `artifacts/tests/StaticAnalysis.trx` and `artifacts/tests/static-analysis-report.md`; they are not committed images or an integration result.

Installed `prism_sim.dll` SHA256: `73C2A479C8FEB3713896DB55021BB74215C1590CF656F42DAAFFD920926BC930`. Installed `prism_kernel.dll` SHA256: `07D37FF1F5F7E9F7E008801A919703CF6F49364BC20329207B97576A74CB7B19`. Existing local production changes were preserved; validation uses the current workstation checkout/binaries, not a fresh native build or a clean-checkout certification.

Keep requirements current here and preserve approved spec commits in completed issues. Integration/viewport testing remains deferred; later attach actual result images to the relevant issue with test ID/source revision, alongside quantitative assertions. See the [maintenance flow](../../engineering/requirements-and-testing.md#feature-documentation-and-test-maintenance).

Windows verification on 2026-11-05: the combined candidate passed 86 Unit and 592 Headless tests with rebuilt native libraries. See the [integration verification report](../../engineering/release-readiness-2026-11-05.md) for source revisions, commands and limits.
