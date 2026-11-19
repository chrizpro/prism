# Fluids

Three levels of flow, from a quick calculation to a 3D picture. Cooling lives on the Thermal tab because it is heat through a solid.

## Ribbon

- **Pipe Calculations:** Pipe Flow, Pipe Network
- **3D Flow:** Flow Study

Owner approved this organization. Pipe calculations keep their live Setup inputs and result readouts. Flow Study groups geometry, fluid and boundaries under Setup, detail and run/stop under Solve, and display, slicing and streamlines under Results. [Maintained ribbon/panel checks](../../../Prism.Tests/Headless/Ribbon/CaeRibbonWorkflowTests.cs).

## Gallery demos

**Symmetric pipe network.** Refresh the gallery and open the card under CAE. Pipe Network opens with a 5,000 mm trunk at 40 mm bore, two 8,000 mm branches at 25 mm bore, water, and gauge pressures of 200 kPa at A and zero at C and D. Both branches carry 234.235 L/min and together match the 468.470 L/min trunk. The symmetry is an independent check on the split. The schematic exaggerates pipe diameter so the branches are visible. The panel rows give the actual bore values. [Model](../../../gallery/models/symmetric-pipe-network.prism) · [Viewport](../../../gallery/previews/symmetric-pipe-network.png) · [Measured results](../../interface/gallery/evidence/symmetric-pipe-network.json).

**Laminar flow through a duct.** The card opens Flow Study with a solid cylinder representing the water inside a 20 mm bore, 120 mm long duct. Choose **Through the body**, select the cylinder, then pick the flat ends at minimum and maximum X for **Inlet** and **Outlet** respectively. Choose **Water (20 °C)**, enter **0.00200761 m/s**, keep **Normal (100 cells)**, and press **Run flow**. The example settled at Reynolds number 40. The velocity slice shows a 0.004 m/s peak, about twice the inlet mean, and the inlet and outlet flow readouts are 0.036 and 0.038 L/min. [Model](../../../gallery/models/laminar-flow-duct.prism) · [Speed slice](../../../gallery/previews/laminar-flow-duct.png) · [Measured results](../../interface/gallery/evidence/laminar-flow-duct.json).

For the same duct in **Pipe Flow**, enter 20 mm **Internal diameter**, 120 mm **Pipe length**, and 0.0378426 L/min **Volume flow**, with the default water properties. Pipe Flow gives 0.01931 Pa, matching Hagen–Poiseuille for fully developed flow. Flow Study gives 0.02104 Pa over the modeled duct, about 9% higher with its inlet boundary and grid. The example demonstrates the profile and the scale of the loss.

Owner requested the gallery demos on 2026-11-19. The [builder](../../../gallery/build_fluids_demos.py) ran both in the Windows desktop at revision `038d801`, with checks for flow balance and the laminar velocity and pressure-drop benchmarks. [Maintained Fluids tests](../../../Prism.Tests/Headless/Fluids) · [Gallery issue #61](https://github.com/chrizpro/prism/issues/61) · [Pipe Network issue #55](https://github.com/chrizpro/prism/issues/55).

## Pipe Network

Steady flow through round, full pipes and pumps: how the flow splits, the pressure at each point, and where a pump settles. Type rows, or pick curves and press **Read picked curves** so the model's own routing becomes the pipes. Give at least one point a known pressure, such as a tank or the open air at 0.

Losses are Darcy–Weisbach: 64/Re when laminar, Colebrook–White when turbulent. Between Re 2000 and 4000 friction is a blend, and the results say so, because no formula is reliable there.

Pump head falls from its no-flow head to zero at its no-head flow, along a parabola. Pumps have a check valve, so nothing runs backward through them.

## Cooling on the Thermal tab

Thermal can now hold faces at a temperature, put heat into faces, and cool faces by convection and radiation. A study needs somewhere for heat to go. The cooling choices are typical figures (about 10, 50 and 1000 W/m²·K), so use your own when you have them. No air or water is simulated.

## Flow Study

Basic 3D flow on a block grid, with no fluid mesh to make.

- **Around:** puts the body in a tunnel, and reports drag and a drag coefficient. The upstream side holds the speed; the other sides are open.
- **Through:** the body *is* the fluid. Model the inside of a duct as a solid, then pick its inlet and outlet faces. It reports pressure drop and flow.

A slice shows speed or pressure, and streamlines start where the flow comes in.

The solver is a lattice Boltzmann method (D3Q19, two relaxation times), run on the CPU. A grid can only hold so high a Reynolds number. Past that the fluid is run thicker, and the results say so. There is no turbulence model.

| Use | How far to trust it |
|---|---|
| Slow, smooth flow through ducts and manifolds | Good numbers |
| Flow around a body at low Reynolds number | Good pattern, drag somewhat high |
| Fast air or water, like a car at road speed | A picture only, not a number |

Curved walls become stair steps, so a body needs many cells across it to be accurate. Normal detail took about four minutes on a sphere on the test machine, and the time grows quickly with Fine. Stop cancels a run.

## Checked against

| Check | Prism | Reference |
|---|---|---|
| One pipe against Pipe Flow | 20000 Pa | 20000 Pa |
| Laminar pipes side by side, 20 and 40 mm | 16 : 1 | 16 : 1 (diameter⁴) |
| Pump against a 10 m lift | Pump and system heads agree | Curve crossing |
| Hot wall cooled by a film | 91.444 °C | 91.444 °C by hand |
| 2 W through a block, cooled at the far end | 65.00, 67.40 °C | 65.00, 67.40 °C by hand |
| Radiation alone | 398.36 °C | 398.36 °C by hand |
| Laminar pipe pressure gradient, 3D | 0.164 Pa/m | 0.161 Pa/m (Hagen–Poiseuille) |
| Peak speed in that pipe | 1.98 × mean | 2 × mean |
| Sphere at Re 100, Normal detail | Cd 1.25 | about 1.09 (measured), 14% high |

## What it does not do

No turbulence model, free surfaces, compressible flow, moving parts, or heat carried by the fluid into a solid. Serious CFD would be a separate solver, which suits an add-on.

## Maintained Pipe Network verification

[Unit cases](../../../Prism.Tests/Unit/Fluids/PipeNetworkTests.cs) compare signed laminar flow with Hagen–Poiseuille and reject a disconnected branch. [Headless cases](../../../Prism.Tests/Headless/Fluids/PipeNetworkCommandTests.cs) exercise typed mm and kPa conversion and malformed rows without document changes. Run `./tools/test.ps1 -Feature Fluids`. [Issue #55](https://github.com/chrizpro/prism/issues/55).

Pumps, elevation, roughness and flow withdrawals, curve import, turbulent network benchmarks, and 3D flow convergence still need maintained cases.
