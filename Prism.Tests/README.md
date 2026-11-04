# Prism tests

Start here to read, find or run the maintained tests. They live in the top-level `Prism.Tests/` directory and appear under **Prism.Tests → Unit / Headless / Integration** in `Prism.slnx`. Older standalone test programs remain separately under `tests/`.

```text
Prism.Tests/
  Unit/                         Prism.UnitTests.csproj
    Extrude/                    ExtrudeDirectionMathTests.cs
    Revolve/                    RevolveAxisMathTests.cs
    Sweep/                      SweepPathMathTests.cs
    Loft/                       LoftSectionMathTests.cs
    Solid/                      PatternGeometryTests.cs, CurveExpressionTests.cs
    StaticAnalysis/             StaticLoadMathTests.cs
  Headless/                     Prism.HeadlessTests.csproj
    Extrude/                    SolidExtrudeTests.cs, ExtrudeToObjectTests.cs
    Revolve/                    SolidRevolveTests.cs
    Sweep/                      SweepCommandTests.cs
    Loft/                       LoftCommandTests.cs
    Solid/                      Primitive, edit, combine, transform, pattern and shared tool checks
    StaticAnalysis/             StaticStudyTests.cs
    Support/                    HeadlessApplication.cs, ModelingScenario.cs
  Integration/                  Planned viewport workflows; README only for now
```

## The three layers

| Layer | What runs | What it establishes | Dependencies |
|---|---|---|---|
| Unit | Managed geometry, curve expressions and pattern layout | Profile coordinates, path/axis directions, interpolation and boundary area math | .NET 10; no native runtime or Avalonia dispatcher |
| Headless | Real commands, CadSession, OCCT and single-body static solving on an Avalonia dispatcher | Geometry, command modes, cage topology, history, persistence and independent structural benchmarks | .NET 10, Windows, OCCT plus `prism_sim` for structural cases; no viewport |
| Integration | Future scripted desktop selection → preview → commit workflows | Visible model, selection, handles and application state together | Desktop app and automation bridge; [planned scenarios](Integration/README.md) |

Unit tests cover supporting production math, not a second implementation of the modeling commands or structural solver. Headless checks provide the next layer through actual commands and numerical solves. Screenshots remain a separate source of evidence.

## Run and discover

```powershell
./tools/test.ps1                              # Unit + Headless; TRX reports under artifacts/tests
./tools/test.ps1 -Layer Unit                  # no native runtime needed
./tools/test.ps1 -Layer Headless -Feature Revolve
./tools/test.ps1 -Layer Headless -Feature StaticAnalysis
./tools/test.ps1 -Feature Solid
./tools/test.ps1 -List                        # list descriptive IDs and test names

# Standard test runner commands also work, including Rider's test explorer:
dotnet test Prism.Tests/Unit/Prism.UnitTests.csproj
dotnet test Prism.Tests/Headless/Prism.HeadlessTests.csproj --filter "TestId=EXT-H001"
```

The focused runner builds the test projects and their dependencies; it does not require Android/iOS workloads from the full solution. Headless tests look upward from the test assembly for `Prism.slnx`, then load `native/bin`. Set `PRISM_NATIVE_DIR` to an absolute DLL directory to override it, including when using a separate worktree. Missing dependencies fail with an explicit message instead of reporting passing/skipped geometry tests.

The new projects use [xUnit](https://xunit.net/docs/getting-started/v2/getting-started) and [Avalonia's headless xUnit support](https://docs.avaloniaui.net/docs/testing/headless-xunit). Existing executable suites keep their original runner and dependencies; `tools/run-tests.ps1` remains their entry point.

## Read an individual test

Each test has a descriptive C# method name, a readable runner name, and `TestId`, `Feature` and `Layer` traits. IDs identify tests, not approved requirements. `EXT-H001` means Extrude / Headless / case 001. Numbers stay stable when tests are renamed.

Each maintained feature test class also links back to its canonical living specification with an XML `seealso` link. Feature READMEs/guides link to the test files or folders below. Keep those links current when behavior or file locations change; follow the [agent maintenance flow](../docs/engineering/requirements-and-testing.md#feature-documentation-and-test-maintenance).

For example, [EXT-H001](Headless/Extrude/SolidExtrudeTests.cs) creates a 10 × 6 mm rectangle, sets Distance to 12 mm, and asserts a valid solid with 720 mm³ volume, bounds (0,0,0)..(10,6,12), and six planar faces. Setup, action and expected results are visible together in the method. Shared helpers contain fixture mechanics and numerical tolerance, while expected measurements remain in the individual tests.

Use named options such as `SetChoice(extrude, "Extent", "Symmetric")`, named fixture dimensions and short setup/action/assert sections. Avoid anonymous scenario tables with numeric mode indices and avoid tests that merely assert a command returned something.

## Feature issues and tests

| Feature issues | Living specification | Unit | Headless |
|---|---|---|---|
| Extrude: [Solid #6](https://github.com/chrizpro/prism/issues/6), [Surface #7](https://github.com/chrizpro/prism/issues/7), [NURBS #16](https://github.com/chrizpro/prism/issues/16) | [Solid](../docs/modeling/extrude/README.md#solid-extrude) · [Surface / NURBS](../docs/modeling/extrude/README.md#surface-and-nurbs-extrude) | [ExtrudeDirectionMathTests.cs](Unit/Extrude/ExtrudeDirectionMathTests.cs) | [Extrude headless tests](Headless/Extrude/), including To object body references and the named Surface case; NURBS shares the Surface command but its tab workflow is unverified |
| Revolve: [Solid #12](https://github.com/chrizpro/prism/issues/12), [Surface #13](https://github.com/chrizpro/prism/issues/13), [NURBS #15](https://github.com/chrizpro/prism/issues/15) | [Revolve README](../docs/modeling/revolve/README.md) | [RevolveAxisMathTests.cs](Unit/Revolve/RevolveAxisMathTests.cs) | [SolidRevolveTests.cs](Headless/Revolve/SolidRevolveTests.cs), including the named Surface case; NURBS shares this command but its tab workflow is unverified |
| Sweep: [Surface #18](https://github.com/chrizpro/prism/issues/18), [Freeform #19](https://github.com/chrizpro/prism/issues/19), [NURBS #20](https://github.com/chrizpro/prism/issues/20) | [Sweep README](../docs/modeling/sweep/README.md) | [Sweep math](Unit/Sweep/) | [Sweep commands](Headless/Sweep/), including representative exact and cage cases; NURBS workflow unverified |
| Loft: [Surface #22](https://github.com/chrizpro/prism/issues/22), [Freeform #23](https://github.com/chrizpro/prism/issues/23), [NURBS #24](https://github.com/chrizpro/prism/issues/24) | [Loft README](../docs/modeling/loft/README.md) | [Loft math](Unit/Loft/) | [Loft commands](Headless/Loft/), including representative exact and cage cases; NURBS workflow unverified |
| Solid ribbon [#31](https://github.com/chrizpro/prism/issues/31) | [Solid ribbon and linked feature guides](../docs/modeling/solid/README.md) | [Pattern and curve logic](Unit/Solid/) | [Solid ribbon and command tests](Headless/Solid/) |
| Static structural analysis: [#25](https://github.com/chrizpro/prism/issues/25) | [Static analysis README](../docs/cae/static-analysis/README.md) | [Load/area math](Unit/StaticAnalysis/) | [Static studies](Headless/StaticAnalysis/), real single-body solver and independent axial-bar checks |

Keep links at this level. The facts and traits in the files describe individual options and expectations; there is no separate option-to-test mapping to maintain. Use `./tools/test.ps1 -List` to see the current cases.

[Freeform Extrude Profile #8](https://github.com/chrizpro/prism/issues/8) currently links legacy executable characterization. It has no dedicated maintained Unit/Headless classes yet; the exact Solid/Surface tests above do not establish Freeform coverage.

[Freeform Revolve #14](https://github.com/chrizpro/prism/issues/14) has no identified dedicated maintained tests. Its canonical behavior and open decisions live with all four variants in the [Revolve README](../docs/modeling/revolve/README.md). The Surface/NURBS Extrude specification is shared in [its living guide](../docs/modeling/extrude/README.md#surface-and-nurbs-extrude).

Integration remains [planned](Integration/README.md) and is deferred for now. When implemented, attach useful viewport images to the relevant feature/bug issue with test ID and source revision. Reserved integration IDs are not implemented or counted as passing tests.

## Scope and authorization

Initial pilot verification on 2026-11-03: **8 Unit tests and 16 Headless tests passed**, with no skipped tests. The solution lists both projects; Integration remains planned. Tested with .NET SDK 10.0.102 and the workstation's installed OCCT binaries, without rebuilding native code. `prism_kernel.dll` SHA256: `07D37FF1F5F7E9F7E008801A919703CF6F49364BC20329207B97576A74CB7B19`. Headless execution produced no viewport screenshots. The existing standalone suites were left unchanged and were not rerun for this structure-only pilot.

Follow-up on 2026-11-03: **14 Extrude Headless tests passed**, including six added To object body-target checks. Run with `./tools/test.ps1 -Layer Headless -Feature Extrude`. Unit and Revolve tests were not rerun for this follow-up; no product behavior changed. Body-target characterization does not establish face/plane/curved-target coverage.

Sweep/Loft/Static expansion on 2026-11-03: **14 Unit and 39 Headless tests passed**, no failures/skips. Sweep and Loft each add 2 Unit and 6 Headless cases; static structural analysis adds 2 Unit and 5 Headless cases. Tests used installed native DLLs without rebuilding them; integration and the legacy executable suites were not run for this expansion.

The owner explicitly requested this new solution-managed structure and existing-behavior examples on 2026-11-03. These tests characterize observable implementation; they do not approve all proposed requirements. Related references: [Solid Extrude draft](../docs/modeling/extrude/README.md#solid-extrude), [Surface Extrude draft](../docs/modeling/extrude/README.md#surface-and-nurbs-extrude), [Freeform Profile draft](../docs/modeling/extrude/README.md#freeform-extrusion), [modeling guide](../site/guide/modeling.md), and the actual [ExtrudeCommand / RevolveCommand implementation](../Prism/Commands/SolidCommands.cs). The feature issues above track review and test links. This pilot preserves the earlier executable suites and does not change product behavior.

This is representative coverage, not complete feature certification. Extrude Boolean/taper/regions and additional face/plane/curved-target cases, additional Revolve world/picked axes, two-sided angles, feature regeneration/persistence and invalid profiles, Freeform variants and desktop workflows need further coverage. New/unclear behavior still requires owner review. Retire existing executable suites only after their useful checks are inventoried and replaced deliberately.

The Sweep/Loft expansion does not establish rails, complex/closed paths, Loft endpoints/closed lofts or complete cage validity. Static coverage is a single-body axial benchmark plus pressure/scaling and setup checks; bending, torsion, general mesh convergence, contact and multi-body solving remain outside this pilot. Canonical READMEs hold the current limitations and owner decisions.
