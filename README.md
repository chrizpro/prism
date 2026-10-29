# Prism

![Prism logo](Prism/Assets/prism-logo.png)

Prism is a CAD application for solid, surface, freeform, and sketch-based modeling. It uses Avalonia for the interface and Open CASCADE for geometry, with workspaces for drawing, manufacturing, simulation, and electronics.

## Build and run

Install the .NET 10 SDK. For the native geometry and simulation libraries on Windows, install Visual Studio C++ build tools, CMake, and vcpkg with Open CASCADE, Eigen, and Netgen available for the selected triplet.

```powershell
./native/build.ps1
dotnet build Prism.Desktop/Prism.Desktop.csproj -c Release
dotnet run --project Prism.Desktop -c Release
```

Open `Prism.slnx` in your IDE. Android, iOS, and browser targets have separate projects and require the corresponding .NET workloads and platform toolchains. The desktop application requires the native DLLs staged in `native/bin`.

## Documents and preferences

New native documents use `.prism`. Existing `.ourcad` documents remain readable and appear in open dialogs, the workspace, and the parts library. The document data format is unchanged.

Preferences and support files live in `%LOCALAPPDATA%\Prism`. On first launch, Prism imports files from the previous `%LOCALAPPDATA%\OurCAD` folder without overwriting existing Prism files or removing the originals. Saved workspace paths and recent files are preserved. New default workspaces use `Documents\Prism`.

## Logo and icon

The faceted prism mark uses ice cyan, blue, and violet, with a slate background for the app icon. Editable SVGs, PNGs, a multiresolution Windows icon, and a brand preview are in [Prism/Assets](Prism/Assets). The shared mark appears on the home screen and modeling header; each platform has an app icon asset.

To regenerate the assets with Python and Pillow:

```powershell
python tools/generate_prism_brand.py
```

The generator uses the Windows Segoe UI Semibold font for the wordmark.

## Automation and validation

The local MCP bridge is configured in `.mcp.json`. Build it with `dotnet build Prism.Mcp -c Release`. Automation environment variables use the `PRISM_` prefix.

Focused checks are executable projects under `tests`, for example:

```powershell
dotnet run --project tests/Prism.RibbonTests -c Release
dotnet run --project tests/Prism.WorkflowTests -c Release
```

Feature notes and current limitations are in [docs](docs). Generated review artifacts, native model studies under `docs`, local IDE state, and compiled binaries are excluded from source control.

## Ribbons, installers, and the website

Shared ribbon sections follow one workflow order across the app. Transform keeps Move visible and groups Rotate, Scale, Mirror, and Duplicate. AR Export and Stop Sharing are under Share on Home, Assemble, Render, and Animate. Existing customized layouts remain intact; reset a tab in Settings to adopt its shipped layout.

See [packaging](packaging/README.md) for Windows setup, macOS DMG, and Linux DEB builds. Packaging defaults to a self-contained desktop app and requires matching native libraries. Windows is the locally verified target; Mac and Linux require validation on those systems.

The [Prism documentation website](https://prism-cad-guide.christopherbfwilliam.chatgpt.site) is published privately from the VitePress source in `site`. Regenerate its tool reference after changing ribbon definitions:

```powershell
dotnet run --project tools/Prism.DocsCatalog -- .
cd site
npm ci
npm run dev
npm run build
```

The docs workflow checks catalog freshness and builds the site. Narrative guides and the changelog should change with the app. The installer workflow builds target-specific packages when manually dispatched; it does not publish a public release.
