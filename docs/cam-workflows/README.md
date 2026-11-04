# CAM workflow review

The CAM workspace has three process tabs: **Subtractive**, **3D FDM**, and **3D Resin**. Open CAM from the workspace chooser, or use `cam` / `make` through automation.

## CNC

Create a named **CAM setup**, choose a tool, and create operations. **3D Rough** clears rectangular stock around a top-accessible body; **3D Finish** supplies parallel surface-following passes. Both are sampled three-axis strategies.

Use **Operation order** to change the program sequence. The change is undoable and persists when saving. **Select setup** switches named stock/work-zero definitions; each generated operation retains its own setup reference. Updating the definition or model requires regenerating its paths. Hide unwanted operations to omit them from the program.

**Stock review** uses visible milling operations in the active setup. Pick the setup's model, set the grid size, flute/holder dimensions and comparison tolerance, then click **Run checks**. Adjust program position and rerun for intermediate stock. Blue indicates stock close to the target, orange indicates remaining stock and red indicates possible target gouges. Readouts also report sampled rapid/holder contacts. Changing inputs clears old results.

**Design check** screens +Z access, reach and thin walls, with corner-radius guidance. It does not automatically recognize or certify all hole/corner constraints. **Setup sheet** provides feed-based estimates and source checks; **Post G-code** outputs one setup at a time.

## Printing

Both check panels work directly on CAD bodies and imported meshes. A single available body is picked automatically; for multiple bodies, choose the model explicitly. Orange/red overlays flag areas for review. Analyses run explicitly after settings are chosen.

- **3D FDM**: nozzle/perimeter wall targets, overhang area, bed contact, build fit, suggested low-support orientation and filament weight estimate. Actual bridges, layer strength, supports and first-layer settings belong in the external slicer.
- **3D Resin**: sealed cavities/trapped volume, potential cups during build progression, unsupported layer starts, thin features, peak sampled layer area, build fit and resin estimate. Hollow body and Drain / vent hole reuse the exact CAD Shell and Hole tools. Rotate/tilt, then rerun checks to compare geometry. Mesh hollowing and actual supports/exposures belong in the resin slicer.

Use **Export for printing** or **Export for resin** to save a stable 3MF/STL/OBJ/PLY path. Export preferences are separate for FDM/resin and remembered by project. Choose the slicer and supply its executable path if discovery does not locate it. Real external-app launch/import behavior is not yet verified.

## Evidence and limits

`Prism.CamTests` creates the adjacent PNGs by rendering the actual Avalonia ribbons and command panels in dark/light themes at a 1024-pixel ribbon width and 394-pixel panel width. Results below a panel's visible area are available through its scroll view.

The geometry fixtures include a solid cube, an analytically known sealed cubic cavity, an open cup, a floor-level side vent, floating material, deliberate CNC gouges/contacts and an automatically generated 3D roughing path. Roundtrip and undo tests cover setups and ordering.

These are sampled geometry screens. The resin grid is bounded to roughly 82 cells per dimension, so narrow drains and thin features may be missed. CNC stock is a heightfield with global tool dimensions, excluding undercuts, machine/fixture geometry and exact swept-volume collision. See [CAM status and remaining scope](../cam/README.md).

Validation commands:

```powershell
dotnet run --project tests/Prism.CamTests
dotnet run --project tests/Prism.PrintExportTests
dotnet run --project tests/Prism.WorkflowTests
dotnet run --project tests/Prism.ApiCoverageTests
dotnet build Prism.Desktop/Prism.Desktop.csproj
```

The broader `Prism.RibbonTests` run currently stops at the unrelated assertion `Surface exposes its four creation tools`. CAM tab visibility, workflow order and process-specific commands are checked independently in `Prism.CamTests`.
