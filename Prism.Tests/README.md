# Prism tests

Start here to read, find or run the maintained tests. They live in the top-level `Prism.Tests/` directory and appear under **Prism.Tests → Unit / Headless / Integration** in `Prism.slnx`. Older standalone test programs remain separately under `tests/`.

```text
Prism.Tests/
  Unit/                         Prism.UnitTests.csproj
    Extrude/                    ExtrudeDirectionMathTests.cs
    Revolve/                    RevolveAxisMathTests.cs
  Headless/                     Prism.HeadlessTests.csproj
    Extrude/                    SolidExtrudeTests.cs
    Revolve/                    SolidRevolveTests.cs
    Support/                    HeadlessApplication.cs, ModelingScenario.cs
  Integration/                  Planned viewport workflows; README only for now
```

## The three layers

| Layer | What runs | What it establishes | Dependencies |
|---|---|---|---|
| Unit | Real managed `Plane3`/`Vec3` operations used by Extrude/Revolve | Plane normal, profile coordinates, axis normalization, tangent and axial projection math | .NET 10; no native DLLs or Avalonia |
| Headless | Real commands, CadSession and native OCCT on an Avalonia dispatcher | Result geometry, command modes, history and representative persistence | .NET 10, Windows, `native/bin` DLLs; no viewport |
| Integration | Future scripted desktop selection → preview → commit workflows | Visible model, selection, handles and application state together | Desktop app and automation bridge; [planned scenarios](Integration/README.md) |

Unit tests cover supporting production math, not a second implementation of Extrude/Revolve. Their success alone says nothing about whether the commands build valid solids. Headless command tests provide that next layer. Screenshots remain a separate source of evidence.

## Run and discover

```powershell
./tools/test.ps1                              # Unit + Headless; TRX reports under artifacts/tests
./tools/test.ps1 -Layer Unit                  # no native runtime needed
./tools/test.ps1 -Layer Headless -Feature Revolve
./tools/test.ps1 -List                        # list descriptive IDs and test names

# Standard test runner commands also work, including Rider's test explorer:
dotnet test Prism.Tests/Unit/Prism.UnitTests.csproj
dotnet test Prism.Tests/Headless/Prism.HeadlessTests.csproj --filter "TestId=EXT-H001"
```

The focused runner builds the test projects and their dependencies; it does not require Android/iOS workloads from the full solution. Headless tests look upward from the test assembly for `Prism.slnx`, then load `native/bin`. Set `PRISM_NATIVE_DIR` to an absolute DLL directory to override it, including when using a separate worktree. Missing dependencies fail with an explicit message instead of reporting passing/skipped geometry tests.

The new projects use [xUnit](https://xunit.net/docs/getting-started/v2/getting-started) and [Avalonia's headless xUnit support](https://docs.avaloniaui.net/docs/testing/headless-xunit). Existing executable suites keep their original runner and dependencies; `tools/run-tests.ps1` remains their entry point.

## Read an individual test

Each test has a descriptive C# method name, a readable runner name, and `TestId`, `Feature` and `Layer` traits. IDs identify tests, not approved requirements. `EXT-H001` means Extrude / Headless / case 001. Numbers stay stable when tests are renamed.

For example, [EXT-H001](Headless/Extrude/SolidExtrudeTests.cs) creates a 10 × 6 mm rectangle, sets Distance to 12 mm, and asserts a valid solid with 720 mm³ volume, bounds (0,0,0)..(10,6,12), and six planar faces. Setup, action and expected results are visible together in the method. Shared helpers contain fixture mechanics and numerical tolerance, while expected measurements remain in the individual tests.

Use named options such as `SetChoice(extrude, "Extent", "Symmetric")`, named fixture dimensions and short setup/action/assert sections. Avoid anonymous scenario tables with numeric mode indices and avoid tests that merely assert a command returned something.

## Extrude / Revolve pilot inventory

| Test IDs | File | Coverage |
|---|---|---|
| EXT-U001–004 | [Extrude math](Unit/Extrude/ExtrudeDirectionMathTests.cs) | XY normal; scaled axes; winding reversal; offset XZ profile coordinates |
| REV-U001–004 | [Revolve math](Unit/Revolve/RevolveAxisMathTests.cs) | Axis normalization; tangent direction; reversed axis; signed axial projection |
| EXT-H001–004 | [Extrude commands](Headless/Extrude/SolidExtrudeTests.cs) | Distance; reverse; symmetric; two sides; analytic volumes and full bounds |
| EXT-H005–008 | [Extrude commands](Headless/Extrude/SolidExtrudeTests.cs) | Open-profile rejection; Surface area/topology; commit/undo/redo; feature edit/save/reopen |
| REV-H001–004 | [Revolve commands](Headless/Revolve/SolidRevolveTests.cs) | Full; quarter; reverse quarter; symmetric half; analytic volumes and full bounds |
| REV-H005–008 | [Revolve commands](Headless/Revolve/SolidRevolveTests.cs) | Picked origin axis; zero symmetric angle; Surface area/topology; commit/undo/redo |
| EXT-I001, REV-I001 | [Integration plan](Integration/README.md) | Reserved; not implemented or counted as passing tests |

## Scope and authorization

Local verification on 2026-11-03: **8 Unit tests and 16 Headless tests passed**, with no skipped tests. The solution lists both projects; Integration remains planned. Tested with .NET SDK 10.0.102 and the workstation's installed OCCT binaries, without rebuilding native code. `prism_kernel.dll` SHA256: `07D37FF1F5F7E9F7E008801A919703CF6F49364BC20329207B97576A74CB7B19`. Headless execution produced no viewport screenshots. The existing standalone suites were left unchanged and were not rerun for this structure-only pilot.

The owner explicitly requested this new solution-managed structure and existing-behavior examples on 2026-11-03. These tests characterize observable implementation; they do not approve all proposed requirements. Related references: [Solid Extrude draft](../docs/tabs/solid/extrude.md), [Story #6](https://github.com/chrizpro/prism/issues/6), [Surface Story #7](https://github.com/chrizpro/prism/issues/7), [modeling guide](../site/guide/modeling.md), and the actual [ExtrudeCommand / RevolveCommand implementation](../Prism/Commands/SolidCommands.cs). Surface/Freeform draft docs remain in PR #9. This pilot preserves the earlier executable suites and does not change product behavior.

This is representative coverage, not complete feature certification. Extrude Boolean/target/taper/regions, additional Revolve world/picked axes, two-sided angles, feature regeneration/persistence and invalid profiles, Freeform variants and desktop workflows need further coverage. New/unclear behavior still requires owner review. Retire existing executable suites only after their useful checks are inventoried and replaced deliberately.
