# Install Prism

Prism is currently a development preview. Installer builds are prepared from the repository; a public download release has not been published yet.

| Platform | Package | Status |
| --- | --- | --- |
| Windows x64 | Setup `.exe` | Primary installer target |
| macOS Apple Silicon / Intel | `.app` in `.dmg` | Packaging configured; target-native dependencies and validation required |
| Linux x64 | `.deb` | Packaging configured; target-native dependencies and validation required |

## Windows

Run the Prism setup executable supplied with a build. The installer uses a per-user installation, creates a Start menu shortcut, and includes an uninstaller. Desktop shortcut creation is optional. It leaves documents and preferences in place when uninstalling.

The release pipeline produces a self-contained application with the .NET runtime included. Native geometry and simulation libraries must be staged before packaging. The preview installer is unsigned.

## Build an installer

See [Build and maintain](/development) for the packaging commands and prerequisites. A package built for one operating system is not a substitute for testing on another.

## Update a preview

Close Prism before installing an updated build. Use the same installation directory. Keep backups of your model documents while using preview releases.
