# Prism desktop installers

`Build-Installer.ps1` publishes a self-contained app, includes the MCP bridge and staged native libraries, then builds a platform package. Use PowerShell 7 on the matching target OS.

| Runtime | Host | Build tools | Output |
| --- | --- | --- | --- |
| `win-x64` | Windows | .NET 10, NSIS 3, staged x64 native DLLs | Per-user setup EXE and SHA256 |
| `osx-arm64`, `osx-x64` | Matching macOS architecture | .NET 10, Xcode command-line tools, staged matching dylibs | App bundle in DMG and SHA256 |
| `linux-x64` | Linux x64 | .NET 10, `dpkg-deb`, staged matching shared libraries | Debian package and SHA256 |

## Windows

```powershell
./native/build.ps1
./packaging/Build-Installer.ps1 -Version 0.1.0 -Compiler 'C:/tools/nsis/makensis.exe'
```

The installer creates a Start menu shortcut and optional desktop shortcut. Uninstall removes only files listed at build time and never recursively deletes the chosen installation directory. Documents and settings are outside that directory. There is no automatic updater or code-signing configuration yet.

The Windows self-contained package includes .NET, the C++ runtime DLLs required by the native shims, the geometry and simulation dependencies, the Python scripting library, and the MCP bridge. Optional Python, CalculiX, and ffmpeg installations are external.

## Native libraries on macOS and Linux

Build with Open CASCADE, Eigen, and Netgen from the same vcpkg baseline and architecture. The release workflow pins the currently used vcpkg commit. Both native libraries must be present, together with their dynamic dependencies. The simulation exports are portable across Windows and Unix; new targets still require native build and runtime validation.

```powershell
./native/build.ps1 -Vcpkg /path/to/vcpkg -Triplet arm64-osx-dynamic -NativeDirectory ./native/stage/osx-arm64
./packaging/Build-Installer.ps1 -Runtime osx-arm64 -NativeDirectory ./native/stage/osx-arm64
```

The macOS script relocates bundled dylib references, makes an icon, ad-hoc signs the app for local testing, and creates a DMG with an Applications shortcut. A public distribution needs Developer ID signing and notarization.

On Linux use `x64-linux-dynamic`, then package with `-Runtime linux-x64`. The build checks for unresolved shim dependencies. Debian package metadata declares the system libraries needed by the desktop interface.

## Existing publish output and low disk space

Use `-OutputDirectory E:/PrismBuilds/release` to put build and installer output on a larger drive. Redirect NuGet's cache and temporary directory as needed. To package a prepared publish folder:

```powershell
./packaging/Build-Installer.ps1 -SkipPublish -PublishDirectory /path/to/publish -NativeDirectory /path/to/native -OutputDirectory /path/to/output
```

`-FrameworkDependent` is available for development packages but requires .NET 10 on the destination. Release builds default to self-contained.

## Automation

`.github/workflows/installers.yml` provides a manual target-platform build matrix and uploads installer artifacts. It does not publish a public release. `.github/workflows/docs.yml` checks the generated ribbon catalog and builds the VitePress site after source changes.
