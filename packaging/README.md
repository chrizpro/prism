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
./packaging/Build-Installer.ps1 -Version 0.2.0 -Compiler 'C:/tools/nsis/makensis.exe'
```

The installer creates a Start menu shortcut and optional desktop shortcut. Uninstall removes only files listed at build time and never recursively deletes the chosen installation directory. Documents and settings are outside that directory. There is no automatic updater or code-signing configuration yet.

The Windows setup completion page has **Launch Prism** checked by default. Click **Finish** to open Prism, or clear the box to finish setup without opening it. [Installer issue #104](https://github.com/chrizpro/prism/issues/104).

On 2026-11-10, NSIS 3.11 compiled a fixture installer and its expanded finish page set the checkbox to checked and gated the installed executable on that state. Interactive install and launch were outside this check.

The Windows installer registers `.prism` as a **Prism Document** for the current user and uses the application's embedded icon in Explorer. Prism appears in **Open with** and becomes the fallback when the extension has no registered handler. An existing default app choice takes priority. Double-clicking a document associated with Prism launches it and opens the model. Uninstall removes Prism's registration.

To verify, install the setup EXE and check that a `.prism` file with spaces in its path shows the Prism icon and opens the model on double-click. Uninstall and check that the Prism handler is gone. A development build can also be checked by launching `Prism.Desktop.exe "C:\path with spaces\model.prism"`.

On 2026-11-04, a temporary Windows install built with NSIS 3.11 resolved the embedded icon through the Windows association API, loaded all ten visible bodies from `Kestrel Runabout.prism` when opened through the Windows shell, and removed the association on uninstall.

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

Every package gets a `THIRD-PARTY-NOTICES.txt` with the full license texts of what it ships, written by `Write-Notices.ps1` from the restore output. When packaging a prepared folder with `-SkipPublish`, run `Write-Notices.ps1` on it first. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

`-FrameworkDependent` is available for development packages but requires .NET 10 on the destination. Release builds default to self-contained.

## Automation

`.github/workflows/installers.yml` provides a manual target-platform build matrix and uploads installer artifacts. It does not publish a public release. `.github/workflows/docs.yml` checks the generated ribbon catalog and builds the VitePress site after source changes.
