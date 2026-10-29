# Build and maintain Prism

## Desktop build

Install .NET 10 and build the native Open CASCADE and simulation shims first. On Windows:

```powershell
./native/build.ps1
dotnet build Prism.Desktop -c Release
dotnet run --project Prism.Desktop -c Release
```

The native build requires Open CASCADE, Eigen, and Netgen. The desktop publish directory must contain the shims and their runtime dependencies.

## Installer builds

```powershell
# Windows, after staging native/bin and installing the NSIS compiler
./packaging/Build-Installer.ps1 -Runtime win-x64 -Version 0.1.0

# Run on macOS with matching native libraries
./packaging/Build-Installer.ps1 -Runtime osx-arm64 -Version 0.1.0 -NativeDirectory ./native/stage/osx-arm64

# Run on Linux with matching native libraries and dpkg-deb
./packaging/Build-Installer.ps1 -Runtime linux-x64 -Version 0.1.0 -NativeDirectory ./native/stage/linux-x64
```

Pass `-OutputDirectory` to place build output on another drive, and `-SkipPublish -PublishDirectory` to package an already published build. The script checks the target platform and required native libraries before creating an installer. macOS signing/notarization and Windows signing require your release credentials and are not enabled by default.

## Keep the site aligned with the app

The site lives in `site/` and uses VitePress. Guides are Markdown; the landing page and searchable tool catalog are Vue components.

```powershell
# Regenerate the actual ribbon/workspace catalog after application changes
dotnet run --project tools/Prism.DocsCatalog -- .

cd site
npm ci
npm run dev
npm run build
```

The build detects stale ribbon data when running from the CAD repository. CI regenerates and compares the catalog so changed tools cannot silently leave outdated reference data. Update the relevant narrative guides and changelog in the same change as the app.

The generated static site is in `site/.vitepress/dist`. A standalone hosting checkout can build from the committed catalog. Hosting identity is in `site/.openai/hosting.json`.

## Validation

```powershell
dotnet run --project tests/Prism.RibbonTests -c Release
dotnet run --project tools/Prism.DocsCatalog -- . --check
npm --prefix site run build
```

Ribbon tests check common sections, transform grouping, AR placement, command identities, and overflow behavior across every ribbon.
