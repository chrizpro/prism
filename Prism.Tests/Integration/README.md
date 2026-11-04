# Desktop integration tests

These xUnit tests launch an isolated Prism Desktop instance through the real Prism.Mcp stdio bridge. The bridge calls the authenticated localhost API. Each scenario uses a private discovery file and its own document. Run on an interactive Windows desktop with the installed native DLLs.

```powershell
$out = Join-Path (Get-Location) 'artifacts/tests/integration/bin'
dotnet build Prism.Desktop/Prism.Desktop.csproj -c Release "-p:OutDir=$out/"
dotnet build Prism.Mcp/Prism.Mcp.csproj -c Release "-p:OutDir=$out/"
dotnet test Prism.Tests/Integration/Prism.IntegrationTests.csproj -c Release
```

Set `PRISM_INTEGRATION_BIN` to an absolute folder containing both binaries to use a different build. The default Unit/Headless runner does not launch a desktop. Filter the integration project with `--filter TestId=EXT-I001` to run one scenario.

| Test ID | Workflow and assertions |
| --- | --- |
| EXT-I001 | MCP initialization and discovery, rejection of unauthenticated and cross-origin HTTP requests, rectangle sketch, Extrude preview edit, committed analytic volume and bounds, undo and redo. |
| REV-I001 | Rectangle sketch, world-Z Revolve preview, forward and reversed 90° results, analytic volume and quadrant bounds. |
| SFC-I001 | MCP error propagation, flat NURBS Control Grid, Thicken, analytic area and solid volume, native document save and reopen. |
| SKT-I001 | Horizontal and Vertical constraints on an oblique line, driving length, solver and endpoint assertions, glyph and dimension capture. |
| LFT-I001 | Surface Loft preview and commit for Smooth and Ruled transitions, with analytic sheet area and bounds. |
| SWP-I001 | Surface Sweep preview and commit for Follow the path and Keep parallel on a straight path, with analytic area and bounds. |

Each run captures real viewport PNGs under `artifacts/tests/integration/<test-id>/<run-id>/`. The [checked-in evidence](../../docs/integration-evidence/README.md) contains reviewed captures organized by test ID. API assertions cover model state and dimensions. The images provide separate visual evidence for preview and committed states. These scenarios drive command slots and options through MCP. Mouse picking, dragging, and broader workflow coverage remain open.

[Tracking issue #56](https://github.com/chrizpro/prism/issues/56) · [MCP guide](../../docs/MCP.md)
