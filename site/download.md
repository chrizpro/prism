# Install Prism

Download Prism from the [download page](https://chrizpro.github.io/prism/download) or from [GitHub Releases](https://github.com/chrizpro/prism/releases). ::: warning Prism is in alpha
Features and workflows can still change between releases, and you will run into bugs. Prism is not yet ready for production work. Keep backups of your models, check the [known issues](/reference/known-issues), and [report bugs](https://github.com/chrizpro/prism/issues/new?template=bug_report.yml) when you find them.
:::

| Platform | Download | Notes |
| --- | --- | --- |
| Windows 10 and 11, x64 | `Prism-windows-x64-Setup.exe` | Per-user installer with Start menu shortcut and uninstaller |
| macOS, Apple silicon | `Prism-macos-arm64.dmg` | M1 and newer |
| macOS, Intel | `Prism-macos-x64.dmg` | Intel Macs |
| Linux x64 | `Prism-linux-x64.deb` | Debian, Ubuntu and derivatives |

Each download has a `.sha256` file next to it so you can check the file.

## Windows

Run the setup program. It installs for your user, creates a Start menu shortcut and, if you choose, a desktop shortcut. The uninstaller removes only the files it installed and leaves your documents and preferences in place.

Alpha builds are not code-signed yet. If Windows SmartScreen warns about an unrecognized app, choose **More info › Run anyway**.

## macOS

Open the `.dmg` and drag **Prism** to **Applications**. Alpha builds are not notarized yet, so the first time you open Prism, right-click it in Applications and choose **Open**, then confirm.

## Linux

```bash
sudo apt install ./Prism-linux-x64.deb
```

The package declares the system libraries the desktop interface needs, so `apt` installs them for you.

## What is included

The installers contain a self-contained app with the .NET runtime, the native geometry and simulation libraries, the Python `cadkit` library and the MCP bridge. These are optional and installed separately:

- **Python 3** for scripts.
- **CalculiX** for thermal and modal studies.
- **ffmpeg** for MP4 animation output.

## Update

Close Prism before installing a newer build, and install to the same location.

## Build from source

See [Build and maintain](/development) and the repository README for building Prism yourself, including from a fork.
