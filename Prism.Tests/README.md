# Prism tests

Windows consolidation verification on 2026-11-07: `./tools/test.ps1` passed **142 Unit and 740 Headless cases**, with no failures or skips, at application revision `b5f34f58d47fbab01a27862ddf0472ff369f5466`. See the [develop audit](../docs/engineering/develop-audit-2026-11-07.md) for commands, native-runtime reuse and other verification limits. This is dated evidence, not a live test status.

Start here to read, find or run the maintained tests. They live in the top-level `Prism.Tests/` directory and appear under **Prism.Tests → Unit / Headless / Integration** in `Prism.slnx`. Older standalone test programs remain separately under `tests/`.

```text
Prism.Tests/
  Unit/                         Prism.UnitTests.csproj
    Assistant/                  Model/effort catalogs, restricted gate, Codex CLI args and MCP client config merge
    Extrude/                    ExtrudeDirectionMathTests.cs
    Revolve/                    RevolveAxisMathTests.cs
    Sweep/                      SweepPathMathTests.cs
    Loft/                       LoftSectionMathTests.cs
    Solid/                      PatternGeometryTests.cs, CurveExpressionTests.cs, SelectionRegionTests.cs
    StaticAnalysis/             StaticLoadMathTests.cs
  Headless/                     Prism.HeadlessTests.csproj
    Assistant/                  Settings whitelist, restricted endpoint, script_editor and render_setup checks
    Extrude/                    SolidExtrudeTests.cs, ExtrudeToObjectTests.cs
    Revolve/                    SolidRevolveTests.cs
    Sweep/                      SweepCommandTests.cs
    Loft/                       LoftCommandTests.cs
    Solid/                      Primitive, edit, combine, transform, pattern, inspect, Warp and shared tool checks
    StaticAnalysis/             StaticStudyTests.cs, StudySetupToolTests.cs
    Sketch/                     Rectangle and drawing-mode checks
    DirectModeling/             Face edit, cap and direct edit checks
    Curves/                     CurveToolTests.cs
    Surfacing/                  Surface options, surface tools, Freeform build, Boundary and Pipe
    Mesh/                       MeshToolTests.cs, ReverseEngineerTests.cs
    SheetMetal/                 SheetMetalTests.cs
    Freeform/                   FormPrimitiveTests.cs
    Cam/                        Setup, CNC operations, program and manufacturing checks
    Assembly/                   Components, joints and assembly tools
    Ribbon/                     RibbonHelpTests.cs — hover help topics and pictures (HLP-H###)
    Viewport/                   ViewportHandleTests.cs — command drag-handle coverage (HDL-H###)
                                HomeOrientationTests.cs — Set/Reset Home and its persistence (HOME-H###)
    Support/                    HeadlessApplication.cs, ModelingScenario.cs
  Integration/                  Prism.IntegrationTests.csproj and desktop MCP scenarios
```

## The three layers

| Layer | What runs | What it establishes | Dependencies |
|---|---|---|---|
| Unit | Managed geometry, curve expressions and pattern layout | Profile coordinates, path/axis directions, interpolation and boundary area math | .NET 10; no native runtime or Avalonia dispatcher |
| Headless | Real commands, CadSession, OCCT and single-body static solving on an Avalonia dispatcher | Geometry, command modes, cage topology, history, persistence and independent structural benchmarks | .NET 10, Windows, OCCT plus `prism_sim` for structural cases; no viewport |
| Integration | Real desktop workflows through MCP stdio and the application API | Preview and committed state, analytic geometry, history and reviewed viewport evidence | Windows desktop, native binaries, Prism.Mcp; [run guide](Integration/README.md) |

Unit tests cover supporting production math, not a second implementation of the modeling commands or structural solver. Headless checks provide the next layer through actual commands and numerical solves. Screenshots remain a separate source of evidence.

## Run and discover

```powershell
./tools/test.ps1                              # Unit + Headless; TRX reports under artifacts/tests
./tools/test.ps1 -Layer Unit                  # no native runtime needed
./tools/test.ps1 -Layer Headless -Feature Revolve
./tools/test.ps1 -Layer Headless -Feature StaticAnalysis
./tools/test.ps1 -Feature Solid
./tools/test.ps1 -Feature Sketch
./tools/test.ps1 -Feature DirectModeling
./tools/test.ps1 -List                        # list descriptive IDs and test names

# Standard test runner commands also work, including Rider's test explorer:
dotnet test Prism.Tests/Unit/Prism.UnitTests.csproj
dotnet test Prism.Tests/Headless/Prism.HeadlessTests.csproj --filter "TestId=EXT-H001"
```

`-Feature` also accepts `Curves`, `Mesh` and `SheetMetal`.

On Linux or macOS, headless tests can run against a locally built kernel: set `PRISM_NATIVE_DIR` to a directory holding `libprism_kernel.so` (and optionally `libprism_sim.so` for structural solves). This is a developer convenience; Windows with the staged `native/bin` remains the reference platform.

The focused runner builds the test projects and their dependencies; it does not require Android/iOS workloads from the full solution. Headless tests look upward from the test assembly for `Prism.slnx`, then load `native/bin`. Set `PRISM_NATIVE_DIR` to an absolute DLL directory to override it, including when using a separate worktree. Missing dependencies fail with an explicit message instead of reporting passing/skipped geometry tests.

The new projects use [xUnit](https://xunit.net/docs/getting-started/v2/getting-started) and [Avalonia's headless xUnit support](https://docs.avaloniaui.net/docs/testing/headless-xunit). Existing executable suites keep their original runner and dependencies; `tools/run-tests.ps1` remains their entry point.

## Read an individual test

Each test has a descriptive C# method name, a readable runner name, and `TestId`, `Feature` and `Layer` traits. IDs identify tests, not approved requirements. `EXT-H001` means Extrude / Headless / case 001. Numbers stay stable when tests are renamed.

Each maintained feature test class also links back to its canonical living specification with an XML `seealso` link. Feature READMEs/guides link to the test files or folders below. Keep those links current when behavior or file locations change; follow the [agent maintenance flow](../docs/engineering/requirements-and-testing.md#feature-documentation-and-test-maintenance).

For example, [EXT-H001](Headless/Extrude/SolidExtrudeTests.cs) creates a 10 × 6 mm rectangle, sets Distance to 12 mm, and asserts a valid solid with 720 mm³ volume, bounds (0,0,0)..(10,6,12), and six planar faces. Setup, action and expected results are visible together in the method. Shared helpers contain fixture mechanics and numerical tolerance, while expected measurements remain in the individual tests.

Use named options such as `SetChoice(extrude, "Extent", "Symmetric")`, named fixture dimensions and short setup/action/assert sections. Avoid anonymous scenario tables with numeric mode indices and avoid tests that merely assert a command returned something.

## Feature issues and tests

Document safety: [save/close/recovery guide](../docs/interface/document-safety/README.md), [issue #66](https://github.com/chrizpro/prism/issues/66), [Unit](Unit/DocumentSafety/), [Headless](Headless/DocumentSafety/) and [actual desktop timer](Integration/DesktopDocumentSafetyTests.cs). Filter with `Feature=DocumentSafety`.

Startup gallery: [canonical guide](../docs/interface/gallery/README.md), [issue #61](https://github.com/chrizpro/prism/issues/61), [Unit tests](Unit/Gallery/), [Headless tests](Headless/Gallery/) and [desktop/MCP examples](Integration/DesktopGalleryTests.cs). Filter with `--filter Feature=Gallery`.

Feedback, bug and crash reporting: [canonical guide](../docs/interface/reporting/README.md), [issue #59](https://github.com/chrizpro/prism/issues/59), [Unit tests](Unit/Reporting/ReportingTests.cs) and [Headless dialog tests](Headless/Reporting/ReportingDialogTests.cs). Filter with `-Feature Reporting` or `--filter Feature=Reporting`.

See the [readiness report](../docs/engineering/test-readiness-report.md) for the reasons behind remaining gaps.

| Feature issues | Living specification | Unit | Headless |
|---|---|---|---|
| [Drawing #58](https://github.com/chrizpro/prism/issues/58) | [Drawing workspace](../docs/drawing/README.md) | [Drawing data, measurements and composition](Unit/Drawing/) | [Drawing geometry, history and persistence](Headless/Drawing/); [desktop scenario](Integration/DesktopDrawingTests.cs) |
| Extrude: [Solid #6](https://github.com/chrizpro/prism/issues/6), [Surface #7](https://github.com/chrizpro/prism/issues/7), [NURBS #16](https://github.com/chrizpro/prism/issues/16) | [Solid](../docs/modeling/extrude/README.md#solid-extrude) · [Surface / NURBS](../docs/modeling/extrude/README.md#surface-and-nurbs-extrude) | [ExtrudeDirectionMathTests.cs](Unit/Extrude/ExtrudeDirectionMathTests.cs) | [Extrude headless tests](Headless/Extrude/), including To object body references and the named Surface case; NURBS shares the Surface command but its tab workflow is unverified |
| Revolve: [Solid #12](https://github.com/chrizpro/prism/issues/12), [Surface #13](https://github.com/chrizpro/prism/issues/13), [NURBS #15](https://github.com/chrizpro/prism/issues/15) | [Revolve README](../docs/modeling/revolve/README.md) | [RevolveAxisMathTests.cs](Unit/Revolve/RevolveAxisMathTests.cs) | [SolidRevolveTests.cs](Headless/Revolve/SolidRevolveTests.cs), including the named Surface case; NURBS shares this command but its tab workflow is unverified |
| Sweep: [Surface #18](https://github.com/chrizpro/prism/issues/18), [Freeform #19](https://github.com/chrizpro/prism/issues/19), [NURBS #20](https://github.com/chrizpro/prism/issues/20) | [Sweep README](../docs/modeling/sweep/README.md) | [Sweep math](Unit/Sweep/) | [Sweep commands](Headless/Sweep/), including representative exact and cage cases; NURBS workflow unverified |
| Loft: [Surface #22](https://github.com/chrizpro/prism/issues/22), [Freeform #23](https://github.com/chrizpro/prism/issues/23), [NURBS #24](https://github.com/chrizpro/prism/issues/24) | [Loft README](../docs/modeling/loft/README.md) | [Loft math](Unit/Loft/) | [Loft commands](Headless/Loft/), including representative exact and cage cases; NURBS workflow unverified |
| Solid ribbon [#31](https://github.com/chrizpro/prism/issues/31) | [Solid ribbon and linked feature guides](../docs/modeling/solid/README.md) | [Pattern, curve and selection logic](Unit/Solid/) | [Solid ribbon and command tests](Headless/Solid/) |
| Curves [#36](https://github.com/chrizpro/prism/issues/36) | [Curves](../docs/modeling/curves/README.md) | — | [Curve editing tools](Headless/Curves/CurveToolTests.cs) |
| Sheet metal and Flatten / Refold | [Sheet Metal](../docs/modeling/sheet-metal/README.md) · [Flatten and Refold](../docs/flatten-refold.md) | — | [Flange, Flat Pattern, Flatten / Refold](Headless/SheetMetal/SheetMetalTests.cs) |
| Mesh and reverse engineering | [Mesh and Reverse Engineer](../docs/modeling/mesh-and-reverse-engineer/README.md) | — | [Mesh tools and scan-to-CAD](Headless/Mesh/) |
| Sketch [#38](https://github.com/chrizpro/prism/issues/38), Rectangle [#39](https://github.com/chrizpro/prism/issues/39) | [Sketch](../docs/modeling/sketches/README.md) · [Rectangle](../docs/modeling/sketches/rectangle/README.md) | — | [Drawing modes](Headless/Sketch/) |
| Direct Modeling [#40](https://github.com/chrizpro/prism/issues/40) | [Direct Modeling](../docs/modeling/direct-modeling/README.md) | — | [Face edits, caps and direct edits](Headless/DirectModeling/) |
| [Surface #48](https://github.com/chrizpro/prism/issues/48) and [NURBS #47](https://github.com/chrizpro/prism/issues/47) | [Surface and NURBS](../docs/modeling/surface-and-nurbs/README.md) | — | [Surface and NURBS options](Headless/Surfacing/SurfaceOptionTests.cs), [surface tools](Headless/Surfacing/SurfaceToolTests.cs) |
| [Freeform #46](https://github.com/chrizpro/prism/issues/46) | [Freeform](../docs/modeling/freeform/README.md) | [Cage topology](Unit/Freeform/) | [Freeform build options](Headless/Surfacing/FreeformBuildTests.cs), [primitives and cage checks](Headless/Freeform/FormPrimitiveTests.cs) |
| [Boundary Surface #50](https://github.com/chrizpro/prism/issues/50) | [Boundary Surface, Ruled and Patch](../docs/modeling/boundary-surface/README.md) | — | [Boundary options](Headless/Surfacing/BoundarySurfaceTests.cs) |
| [Pipe #49](https://github.com/chrizpro/prism/issues/49) | [Pipe](../docs/modeling/pipe/README.md) | — | [Exact and cage Pipe](Headless/Surfacing/PipeTests.cs) |
| [CAM #52](https://github.com/chrizpro/prism/issues/52) | [CAM guide](../docs/cam/README.md) | [Linear G-code](Unit/Cam/LinearGcodeTests.cs) | [Setup and import](Headless/Cam/CamWorkflowTests.cs), [CNC operations](Headless/Cam/CncOperationTests.cs), [program and manufacturing checks](Headless/Cam/CamProgramTests.cs) |
| [ECAD #53](https://github.com/chrizpro/prism/issues/53) | [ECAD guide](../docs/ecad/README.md) | — | [Board and envelope](Headless/Ecad/BoardTests.cs) |
| [Appearance #45](https://github.com/chrizpro/prism/issues/45) | [Appearance studio](../docs/appearance-studio/README.md) | [Color, surface and backdrop math](Unit/Appearance/SurfaceAppearanceTests.cs) | [Layers and persistence](Headless/Solid/AppearanceStudioTests.cs); [desktop integration](Integration/DesktopAppearanceTests.cs) |
| [Rendering #60](https://github.com/chrizpro/prism/issues/60) | [Product rendering](../docs/rendering/README.md) | [HDR, denoising and scattering](Unit/Rendering/RenderingQualityTests.cs) | [Transport and persistence](Headless/Rendering/RenderingQualityTests.cs); [desktop gallery](Integration/DesktopRenderingTests.cs) |
| [Assembly #54](https://github.com/chrizpro/prism/issues/54) | [Assembly guide](../docs/modeling/assembly/README.md) | [Kinematics](Unit/Assembly/) | [Components, joints and assembly tools](Headless/Assembly/) |
| [Fluids #55](https://github.com/chrizpro/prism/issues/55) | [Fluids guide](../docs/cae/fluids/README.md) | [Pipe network solver](Unit/Fluids/) | [Pipe Network command](Headless/Fluids/) |
| Static structural analysis: [#25](https://github.com/chrizpro/prism/issues/25) | [Static analysis README](../docs/cae/static-analysis/README.md) | [Load/area math](Unit/StaticAnalysis/) | [Static studies](Headless/StaticAnalysis/), real single-body solver and independent axial-bar checks |

Keep links at this level. The facts and traits in the files describe individual options and expectations; there is no separate option-to-test mapping to maintain. Use `./tools/test.ps1 -List` to see the current cases.

[Freeform Extrude Profile #8](https://github.com/chrizpro/prism/issues/8) has [maintained cage checks](Headless/Surfacing/FreeformBuildTests.cs) alongside legacy executable characterization.

[Freeform Revolve #14](https://github.com/chrizpro/prism/issues/14) has [maintained cage checks](Headless/Surfacing/FreeformBuildTests.cs). Its canonical behavior and open decisions live with all four variants in the [Revolve README](../docs/modeling/revolve/README.md). The Surface/NURBS Extrude specification is shared in [its living guide](../docs/modeling/extrude/README.md#surface-and-nurbs-extrude).

Desktop [integration tests](Integration/README.md) run separately from Unit and Headless. Attach representative viewport images to the relevant issue with test ID and source revision.

## Scope and authorization

Initial pilot verification on 2026-11-03: **8 Unit tests and 16 Headless tests passed**, with no skipped tests. The solution lists both projects; Integration remains planned. Tested with .NET SDK 10.0.102 and the workstation's installed OCCT binaries, without rebuilding native code. `prism_kernel.dll` SHA256: `07D37FF1F5F7E9F7E008801A919703CF6F49364BC20329207B97576A74CB7B19`. Headless execution produced no viewport screenshots. The existing standalone suites were left unchanged and were not rerun for this structure-only pilot.

Follow-up on 2026-11-03: **14 Extrude Headless tests passed**, including six added To object body-target checks. Run with `./tools/test.ps1 -Layer Headless -Feature Extrude`. Unit and Revolve tests were not rerun for this follow-up; no product behavior changed. Body-target characterization does not establish face/plane/curved-target coverage.

Sweep/Loft/Static expansion on 2026-11-03: **14 Unit and 39 Headless tests passed**, no failures/skips. Sweep and Loft each add 2 Unit and 6 Headless cases; static structural analysis adds 2 Unit and 5 Headless cases. Tests used installed native DLLs without rebuilding them; integration and the legacy executable suites were not run for this expansion.

The owner explicitly requested this new solution-managed structure and existing-behavior examples on 2026-11-03. These tests characterize observable implementation; they do not approve all proposed requirements. Related references: [Solid Extrude draft](../docs/modeling/extrude/README.md#solid-extrude), [Surface Extrude draft](../docs/modeling/extrude/README.md#surface-and-nurbs-extrude), [Freeform Profile draft](../docs/modeling/extrude/README.md#freeform-extrusion), [modeling guide](../site/guide/modeling.md), and the actual [ExtrudeCommand / RevolveCommand implementation](../Prism/Commands/SolidCommands.cs). The feature issues above track review and test links. This pilot preserves the earlier executable suites and does not change product behavior.

This is representative coverage, not complete feature certification. Extrude Boolean/taper/regions and additional face/plane/curved-target cases, additional Revolve world/picked axes, two-sided angles, feature regeneration/persistence and invalid profiles, Freeform variants and desktop workflows need further coverage. New/unclear behavior still requires owner review. Retire existing executable suites only after their useful checks are inventoried and replaced deliberately.

The Sweep/Loft expansion does not establish rails, complex/closed paths, Loft endpoints/closed lofts or complete cage validity. Static coverage is a single-body axial benchmark plus pressure/scaling and setup checks; bending, torsion, general mesh convergence, contact and multi-body solving remain outside this pilot. Canonical READMEs hold the current limitations and owner decisions.

Contextual coverage on 2026-11-03: 20 Unit and 119 Headless cases passed locally on Windows with installed native binaries. The 33 added Headless cases cover Sketch drawing modes and Direct Modeling face edits and caps. The guides identify options still awaiting maintained verification.

Option coverage follow-up on 2026-11-03: 20 Unit and 135 Headless cases passed locally on Windows using installed native binaries. The 16 new headless cases cover sketch edits and dimensions, direct hole feature and pattern actions, and primitive result operations.

Surfacing and Freeform option coverage added 5 Unit and 22 Headless cases. The full Windows run passed 25 Unit and 157 Headless cases using installed native binaries. A follow-up rerun of all 22 new Headless cases passed after tightening the Isocurve position and direction assertions.

CAM and ECAD follow-up: the full Unit suite passed 38 cases and an isolated Windows headless run passed all 14 new CAM and ECAD cases with installed native binaries.

Windows integration verification on 2026-11-05 passed 86 Unit and 592 Headless cases with rebuilt native libraries. See the [release integration report](../docs/engineering/release-readiness-2026-11-05.md) for the combined source revisions and remaining platform limits.
