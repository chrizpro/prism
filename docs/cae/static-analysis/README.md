# Static structural analysis — living specification

This README is the canonical requirements document for Prism's **Static structural study**, not static code analysis. Maintain current reviewed behavior here; [Story #25](https://github.com/chrizpro/prism/issues/25) tracks its review and test links.

**Review state:** Proposed — awaiting owner review (2026-11-03). Existing behavior can be characterized under owner authorization. Passing benchmarks do not approve new requirements or certify every engineering application. Integration remains deferred.

## Scope

The initial maintained pilot covers a single exact solid, linear-elastic static meshing/solving, setup validation and numerical results through `StudyRunner`. It exercises the installed native solver, not an analytical stand-in.

Thermal, modal, topology optimization, fatigue, contact/nonlinear analysis and multi-body CalculiX workflows are outside this pilot. Their existing implementation/legacy checks are preserved. See [existing CAE studies](../../cae-studies/README.md) for historical broader work.

## Current implementation

Owner approved ribbon organization. The Static ribbon follows Select, Setup, Supports, Loads, Solve, Results, Advanced, and Reuse & Export. Setup keeps study selection and Materials together. Fixed Support, Force and Pressure stay visible. Other support/load types use menus. Mesh Refinement follows Run Study. Fatigue and Generative Design are under Advanced. [CAE ribbon checks](../../../Prism.Tests/Headless/Ribbon/CaeRibbonWorkflowTests.cs) verify the workflow and retained tools.

Source: [study definitions](../../../Prism/Simulation/Study.cs), [setup references](../../../Prism/Simulation/StudySetup.cs), [StudyRunner](../../../Prism/Simulation/StudyRunner.cs), [native interface](../../../Prism.Kernel/Sim.cs) and [native solver](../../../native/sim.cpp). Ribbon entry points are in [MainViewModel.cs](../../../Prism/ViewModels/MainViewModel.cs).

| Area | Source-observed behavior |
|---|---|
| Body/material | Study references a solid/form body and requires suitable structural material data |
| Mesh | Tetrahedral meshing from tessellated geometry; quadratic 10-node tetrahedra; explicit or automatic element size |
| Fixtures | Fixed, directional component locks, or a slider approximated by its closest global axis |
| Loads | Force, pressure, torque and gravity; force supports total/per-face and a direction along the face normal, an axis, any custom vector, a tilt from the face normal or a direction taken from geometry (see [the vector input](#force-and-gravity-direction-the-shared-vector-input-owner-request-2026-11-16-tests-only-not-tried-by-hand-in-the-app)) |
| Units | Geometry mm; forces N; stress MPa; material Young's modulus converted from GPa to MPa; density converted for the solver |
| Results | Nodal displacement/stress/von Mises, support reactions, maximum summaries and yield-based safety factor |
| Safety factor | Uses available yield strength; capped at 1000; unavailable yield gives NaN rather than substituting ultimate strength |
| Validation | Missing body/material/fixture/load and invalid/stale face references have explicit validation paths |

The single-body path uses `prism_sim`; multi-body work uses a separate CalculiX-backed path. Neither should silently substitute for the other in test reporting.

**New Study brings its panel forward (2026-11-12):** creating a study selects the context tab on the right so the Simulation panel is what the user sees, even when Properties or another tab had been selected by hand (the owner reported staring at Properties after New Study). The Simulation context behaves like a command or sketch: it takes the front while a study is active and gives the previous tab back afterwards. Maintained check: UX-H006 in [UsabilityWorkflowTests](../../../Prism.Tests/Headless/Interface/UsabilityWorkflowTests.cs).

## Independent axial-bar benchmark

Use a 10 × 10 × 100 mm bar with E = 68,900 MPa, fixed at Z=0, and total axial force 1,000 N at Z=100. The independent reference equations are:

- Cross-sectional area A = 100 mm².
- Axial stress `F/A = 10 MPa`, measured away from the constrained end.
- Axial end displacement `FL/(EA) = 0.0145137881 mm`.
- Net support reaction balances the applied 1,000 N force.
- Doubling the load on the same mesh doubles displacement and stress in a linear solve.

Reference equations: [COMSOL structural verification examples](https://doc.comsol.com/6.3/doc/com.comsol.help.sme/StructuralMechanicsVerificationExamples.pdf), axial displacement and axial stress of a beam/bar. These equations provide independent expected values, not Prism's own result as its expected answer.

The pilot comparison allows 5% error in mean loaded-end displacement and mean interior von Mises stress to accommodate finite mesh/boundary effects; reaction balance allows 1 N. Load scaling on an identical mesh uses a tighter relative comparison. These are benchmark-test tolerances, not approved universal solver accuracy requirements. Stress peaks at clamps/point loads are not compared to uniform bar stress.


## Force and gravity direction: the shared vector input (owner request 2026-11-16; tests only, not tried by hand in the app)

**Why.** A force could only push along the face normal or along one of the six axes, and a per-face force split itself by area. The owner asked for finer control over how a force acts on a face, and for a vector that other CAE tools can share.

**What the panel offers.** *Direction* keeps its old first entries (**Into the face**, then +X, +Y, +Z, -X, -Y, -Z, in the same places) and adds:
- **Custom: type X, Y, Z.** Any vector in the model's axes. Only the ratio matters; the size of the force is the *Force* value, so (1, 1, 0) and (5, 5, 0) are the same direction. A vector with no length, or that is not a number, is refused.
- **Tilted from the face normal** (Force only). *Tilt* from 0° (straight into the face) to 90° (along the face, pure shear), and *Turn around the face normal* to choose which way it leans. The turn is counter-clockwise seen from outside the face, from the face's own X reference: the part of global +X lying in the face (+Y for a face square to X). It needs flat faces that all point the same way; otherwise it is refused and Custom is suggested. A **turning handle** on the first face drags the tilt.
- **Along an edge, or a plane's normal.** Pick a straight edge (its direction start to end) or a plane or flat face (its outward normal); *Reverse the direction* flips it. Curved edges are refused.

**What it tells you.** The panel reports the unit direction, the **force vector** in N, and, when the picked faces are flat and parallel, how much **pushes into** (or pulls out of) the face and how much **slides across** it. The viewport shows an arrow for the direction on each picked face before you add the load. Gravity takes the same choices (without the face-based ones), so gravity can be tilted, and reports its acceleration vector.

**What the solver is given.** The load stores one unit direction in global axes (or "along each face's normal") and the value in N, as it always did; no file format changed. Net applied force is exactly `value x unit direction` for any direction ([SIN-H020](../../../Prism.Tests/Headless/StaticAnalysis/SolverInputTests.cs)).

**Fixed at the same time: per-face force.** *The force is the total over all picked faces* off means each picked face carries the whole value. The old code divided by the average face area, so faces of different size got the wrong share (a 100 mm² and a 1,000 mm² face given 100 N each carried about 18 N and 182 N). Each face now carries exactly its value ([SIN-H019](../../../Prism.Tests/Headless/StaticAnalysis/SolverInputTests.cs); it fails on the old code). A load whose direction has no length now stops the run with a message instead of silently applying nothing ([SIN-H021](../../../Prism.Tests/Headless/StaticAnalysis/SolverInputTests.cs)).

**Evidence.** [VEC-H001 to VEC-H009](../../../Prism.Tests/Headless/StaticAnalysis/VectorInputTests.cs) check the maths by hand (3-4-0 is 0.6, 0.8, 0; a 30° tilt is (sin 30, 0, -cos 30); 90° lies in the face; 100 N along (0.6, 0, -0.8) is 80 N pushing in and 60 N sliding). [STA-H010](../../../Prism.Tests/Headless/StaticAnalysis/StaticStudyTests.cs) runs the real solver on the 10 x 10 x 100 mm bar with 1,000 N tilted 30° from the top face's normal: reactions equal and opposite the vector (within 1 N), axial shortening matches `F_z L/(EA)` (0.1% apart on one 5 mm mesh), and the sideways tip deflection matches the cantilever formula with its shear term, 2.887 mm against 2.926 mm (1.3% apart; tolerance 5%, one mesh, so no convergence claim).

**Limits.**
1. **The force on part of a face is built** (see the next section); this list item used to say it was not.
2. **No typed point-and-moment force.** An area load placed off-centre does produce the bending that follows from where it sits, but there is no way to type a point and a moment directly.
3. **Tilt works on flat faces** that all point the same way. On curved faces use *Into the face* (normal at each point) or Custom.
4. **Direction is fixed in global axes** once added; moving or rotating the body afterwards does not turn it (the study checks the shape signature before running).
5. **Not yet adopted by other tools.** Fixtures with directions, Motion forces and thermal or fluid directions still use their own choices. The input is a small class (`VectorInput`) built to be added to them one at a time.
6. The viewport arrow and handle are checked by tests of their positions, not by looking at them.


### A force or pressure on part of a face (owner request 2026-11-16; tests only, not tried by hand in the app)

**Why this exists.** The vector above fixes *which way* a force acts. The owner's other half was *where*: a force could only go on a whole face. The first delivery of the vector left this out, which was a mistake; it is built now.

**What the panel offers (Force and Pressure).** *Apply to*: **The whole face** (as before), **A circle on the face**, or **A rectangle on the face**. The centre is *Centre across* and *Centre up* from the middle of the face, in millimetres, along the same two in-face directions the tilt uses (across is the part of the model's +X lying in the face, +Y for a face square to X; up is counter-clockwise from it seen from outside). A circle has a *Circle radius*; a rectangle has *width*, *height* and a *turn* about its centre. Picking a face sets a starting size from the face. It works with every Direction choice (into the face, axes, custom vector, tilted, from geometry). It needs **exactly one flat face**; two faces, or a curved face, are refused with a message.

**What you see.** The panel reports the **loaded area** and, for a force, the **average pressure on the area**, or for a pressure the **force it adds up to**. The viewport draws the area's outline on the face and the direction arrow lands in its middle. Drag handles move the area (two arrows from the face centre) and size it (a radius arrow, or width and height arrows). The Simulation panel row says "a 24 mm circle on a face" or "a 20 x 10 mm rectangle on a face" and which way the force points.

**What the solver is given.** The load stores the region in global axes (centre, two unit directions, sizes), so no face frame is needed later; loads saved before this open as the whole face. The solver loads only the mesh faces whose middle lies in the region and shares the value over the area they actually cover, so **the total force is exactly the value you typed** however the mesh falls; a pressure acts over that same area. If the mesh covers less than 75% or more than 125% of the area asked for (a coarse mesh, or part of the area hangs off the face) it says so in the study notes, with the area it really loaded. A region too small for the mesh loads nothing and says so.

**Evidence.** [REG-H001 to REG-H011](../../../Prism.Tests/Headless/StaticAnalysis/LoadRegionTests.cs). On a 100 x 100 x 10 mm plate meshed at 4 mm: a circle of radius 20 mm loaded 1,290 mm² against 1,257 mm² asked (2.7% more), radius 12 mm loaded 446 mm² against 452 mm²; every loaded mesh face lies inside the circle or turned rectangle; the applied force is exact (to 1e-6 N) for any direction. A real solve ([REG-H011](../../../Prism.Tests/Headless/StaticAnalysis/LoadRegionTests.cs)) pulls the 10 x 10 x 100 mm bar with 1,000 N over only the outer half of its end (2.5 mm off the axis): mean extension 0.014454 mm against `FL/(EA)` = 0.014514 mm (0.4% apart), and the top face tilts with a slope of 0.004307 against `F e L/(EI)` = 0.004354 (1.1% apart; tolerance 5%, one 2.5 mm mesh, no convergence claim).

**Limits.**
1. **One flat face, one circle or rectangle per load.** To load several areas, add several loads. No free-form outline, no ellipse or polygon.
2. **The area is cut out of the mesh by element centres**, so its edge follows the mesh, not the exact circle: expect a jagged boundary and an area within roughly 15% on a coarse mesh (2.7% and 1.4% in the checks above). Refine the mesh for a sharper edge. The force total is always exact.
3. **Stress near the edge of a small loaded area is a singularity**, as at any point-like load: it grows with a finer mesh, so judge it away from the edge.
4. **A force at a point with an offset, and the twisting effect from it, is not a separate option;** an area load placed off-centre does give the bending and twist that follows from where it sits (as in REG-H011), but there is no way to type a point and a moment directly.
5. **Fixed in the model's axes and on the face's position when added.** If the body is edited afterwards the study's shape check applies as before; the area does not follow the face.
6. **Torque and gravity have no area.** Gravity acts on the whole body; torque twists about the face as a whole.
7. **Handles, the outline and the arrow are checked by tests of their positions and counts, not by looking at them.**

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
