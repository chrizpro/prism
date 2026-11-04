# CAE workspace — first implementation

The current living requirements and maintained tests for Static structural studies are linked from [the Static analysis specification](../tabs/static-analysis/README.md). This document retains the broader historical CAE implementation/study context.

The former Simulate workspace is now **CAE**, with **Static**, **Thermal**, **Dynamics**, **Motion** and **Fluids** tabs. The internal workspace id `simulate` remains compatible; MCP also accepts `cae`.

## Working analysis types

**Static:** the existing native structural study, fixtures, loads, stress/displacement/safety results and generative tools remain available. An axial-displacement regression passed after the shared meshing refactor.

**Thermal:** CalculiX solves steady or transient conduction in one homogeneous isotropic solid using 10-node tetrahedra from the existing mesher. Assign a material; select the solid and two separated face groups; set their temperatures. Other surfaces are insulated. Transient mode adds a uniform initial temperature and duration; boundary temperatures are held immediately, rather than ramped. The final temperature field appears in the viewport, with a legend and min/max readouts. All increments remain in the solver output; the UI currently displays only the final time.

**Dynamics:** CalculiX solves linear natural frequencies and mode shapes of one homogeneous isotropic solid with fully fixed faces. Request 1–20 modes, then choose a returned mode and press Show mode. Frequency is in Hz; deformation is scaled for visibility and the scalar field is relative magnitude. Mode amplitude is arbitrary, not a predicted vibration amplitude. This is modal free vibration, not forced response, impact, contact or nonlinear transient dynamics.

**Fluids:** see [docs/tabs/fluids](../tabs/fluids/README.md) for Pipe Network and the 3D Flow Study. Pipe Flow calculates steady, fully developed, incompressible flow through a circular full pipe. It reports velocity, Reynolds number, Darcy friction factor, pressure drop, head loss and hydraulic power, with a total minor-loss coefficient. Laminar flow uses 64/Re; turbulent flow solves Colebrook–White. The uncertain 2000–4000 Reynolds transition range is rejected explicitly. Inputs use bore/length/roughness in mm, flow in L/min, density in kg/m³ and viscosity in Pa·s. This is a reduced-order pipe calculation, not 3D CFD, compressible flow, entrance flow, a network solver or conjugate heat transfer.

**Motion:** kinematics and rigid-body motion of an assembly. The mechanism is built in the Assemble workspace (see [docs/motion-analysis.md](../motion-analysis.md)); the Motion tab adds what moves it. It needs no external solver.

## Several bodies and materials

Static, Thermal and Natural Frequencies can study several touching bodies, each with its own material. For Static, select the bodies, then New Study. For Thermal and Natural Frequencies, pick them in the Solid bodies slot.

- Each body is meshed on its own, and CalculiX bonds the touching faces, so the meshes do not need to match.
- A joint is perfect: no sliding, gap, separation or thermal contact resistance. Press fits, bolts and contact are not modeled.
- Each body's stress is read against its own material, so the factor of safety is per body. Stress right at the edge of a joint can grow as the mesh gets finer.
- Bodies that overlap, or that touch nothing, are refused.
- A multi-body Static study always uses CalculiX, so it needs the solver installed.
- Fatigue, Generative Design, mesh convergence and saved setups stay on one body for now.

Checked against hand calculations in `tests/Prism.CaeTests`: heat through aluminum and steel in series gives a joint temperature of 83.40 °C against 83.42 °C by hand; the same bar in tension carries 10 MPa in both materials, and stretches 0.00428 mm against 0.00437 mm by hand (1.9% lower, which the fixed end explains in part); a bar cut in two and bonded vibrates at 819.4 Hz against 819.3 Hz for the whole bar.

## Dependency and execution

CalculiX 2.23 was downloaded from the [official distribution](https://www.dhondt.de/) and tested on this Windows machine. It is an external GPL-2.0-or-later solver, launched as a separate hidden process; its binaries are not bundled into the application source tree.

Install on another Windows machine from the repository root:

```powershell
./tools/Install-CaeSolver.ps1
```

The installer downloads `https://www.dhondt.de/calculix_2.23_4win.zip`, checks SHA256 `87FC4E1721DF37370EC67D0A244BB14F9BF804B0A95DF49E2AE7369D30EC72E8`, and extracts under `%LOCALAPPDATA%/Prism/Solvers/CalculiX`. The default executable is `calculix_2.23_4win/ccx_static.exe`. Set `PRISM_CCX` to use a different compatible solver executable. Keep the upstream license/source obligations with any redistributed solver package.

Runs mesh/solve outside the UI thread, with progress, Stop, a ten-minute solver timeout, source-body change detection and explicit error reporting. Netgen meshing is serialized because its initialization/state is process-global. Stop can terminate the solver process; it waits for an in-progress native meshing call to return. Starting another CAE run requires the previous run to finish or stop.

Material data use consistent units: structural N/mm/s with density in tonne/mm³; thermal J/mm/kg/s/Celsius, conductivity W/(mm·K), density kg/mm³ and heat capacity J/(kg·K). All values come from the assigned library material; missing required properties are rejected.

Each run retains `analysis.inp`, `analysis.dat`, `analysis.frd`, `solver.log`, and `summary.json` under `%LOCALAPPDATA%/Prism/CAE/<unique run>`. Thermal runs also write `temperatures.csv`. Input decks include mesh, material and boundary conditions for reproducibility. CAE setup/results are currently session state and are not embedded in `.prism` documents; the case files remain after closing the app. This matches a first adapter implementation, not a complete persisted multiphysics study manager.

## Verification

- `dotnet run --project tests/Prism.CaeTests`: **19 checks passed** using the installed solver, including steady/transient thermal runs, modal solving, static regression, field construction/invalidation, boundary conflicts, cancellation before solving, and pipe-flow checks.
- Steady 10 x 10 x 20 mm conduction block: max error **4.99e-6 °C** against the analytic linear temperature field.
- Aluminum 10 x 10 x 100 mm cantilever: first bending mode **819.458 Hz**, versus **816.034 Hz** from Euler–Bernoulli theory (about 0.42% difference). Six frequencies and shapes returned. See `benchmarks.json` and `modal-cantilever/`.
- Laminar pipe pressure loss matches Hagen–Poiseuille; turbulent friction satisfies the Colebrook residual to better than 1e-9; transitional flow is rejected. Default inputs produce a valid laminar case.
- `Prism.RibbonTests`: **38 checks passed**, including CAE tab availability and 1024-pixel Thermal/Dynamics/Fluids layouts.
- Desktop Release build passed. Live MCP runs verified temperature contours (20–100 °C), six modal results, mode display, and solver status.

MCP commands: `cae_thermal`, `cae_modal`, `cae_pipe_flow`, `cae_kinematics`, `cae_motion`, `cae_robot_path`, `motion_spring`, `motion_force`, `motion_torque`, `motion_load_to_static` (plus `gear_pair` and `rack_pinion` for the assembly couplings). For the solver commands, use `run_command(commit=false)`, configure inputs, and trigger `Run analysis` through `command_set`. Poll `cae_state`; use `cae_state(action=stop)` to stop. `get_state` includes CAE status. Detailed result data are retained in the case directory.

The benchmark `.prism` contains test solids, not stored solver setups. `live-cae-demo.prism` is the saved viewport model. Numeric evidence and screenshots are alongside this report.

## Next capabilities

Useful next steps are convection/heat-flux boundaries, thermal expansion coupling, persisted study definitions and result reopening, transient frame playback, harmonic/forced response, and an adapter to a dedicated 3D CFD backend with inlet/outlet/wall/domain setup. General contact, assemblies, radiation, turbulence and fluid-structure interaction are not implemented by this first pass.

References: [CalculiX capabilities and license](https://www.calculix.de/), [Hydraulic Institute pipe-loss equations](https://datatool.pumps.org/fluid-flow-iii/general), [EPA SWMM manual on Darcy–Weisbach/Colebrook](https://www.epa.gov/system/files/documents/2022-04/swmm-users-manual-version-5.2.pdf).
