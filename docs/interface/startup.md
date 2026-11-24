# Start-up card and loading page (v0.5)

**Owner request 2026-11-23, built; the card is tried by headless tests ([STU-H001, STU-H002](../../Prism.Tests/Headless/Interface/StartupSplashTests.cs)), the web page in a browser without the app behind it.** Prism takes a while to open. Like Fusion and JetBrains tools, it now shows a branded card at once and says what it is loading. The owner reviewed mockups of both versions before they were built.

## Desktop

- A borderless 560 × 320 card, centred on the screen and shown in the taskbar, appears before the main window is built ([SplashWindow](../../Prism/Views/SplashWindow.cs)). It is dark navy (`#142338`) in both themes, with the Prism mark and name, the tagline, a large faint copy of the mark, the stage being loaded, the version, a progress bar and a footer line.
- Start-up runs in stages, each shown on the card: *Loading geometry kernel…*, *Preparing the workspace…*, *Building the window…*, *Opening documents…*, *Starting the assistant and automation…*, *Opening Prism…*. The kernel warms up on a background thread. The main view model and window must be built on the UI thread, so the card updates between stages, not during them.
- When the main window opens, the card closes. If a stage fails, the error is saved like any crash and the card closes.
- **Start-up timing.** Every start writes `%LOCALAPPDATA%\Prism\startup.log`: each stage with its time in milliseconds, from the moment the process started, and the total. Use it to find what is slow on a slow machine. Headless, on the owner's workstation on 2026-11-23, the main window took about 1.1 s to build, the main view model 0.3 s and the first kernel call 0.05 s. The runtime and window system come before these and are not in the headless numbers.
- **What made start-up slow, and the fixes (2026-11-23, measured on the owner's workstation with `startup.log` and `dotnet-trace`).** Before: about 10 s until the window showed, then the window froze for a further 14–15 s, so start-up took 20–25 s in all. The freeze was the 3D view's mesh shader: on Windows, OpenGL runs through ANGLE, which translates every shader for Direct3D and compiles it, taking 2.6 s for the mesh shader, and it was compiled once for each of the four viewport panes. Fixes:
  - **Shader cache** ([ShaderCache](../../Prism/Rendering/ShaderCache.cs)): the first pane compiles each program and keeps the driver's binary; the other panes load it in milliseconds, and later starts load it from `%LOCALAPPDATA%\Prism\ShaderCache` (keyed by the shader source and the graphics driver; a refused binary is compiled again). The web version cannot keep binaries and compiles as before.
  - The geometry kernel starts loading on the program's first line, alongside the window system.
  - The assistant and automation server start just after the main window opens, not before it (scripts and MCP connect a moment later).
  - Gallery previews are decoded at card size (640 px) off the UI thread, not full size on it.
  - After: the window shows in about 4.4 s, the first start is about 8.4 s in all (it fills the shader cache), and later starts about 5.4 s. The remaining time is the .NET runtime and window system (about 1.5–1.9 s), the kernel (0.7 s), the main view model (0.3 s), building the main window (1.1 s) and its first layout and frame (0.9 s).
- `PRISM_SPLASH=0` or `--no-splash` skips the card (automation and tests that do not want it).

## Web version

- The page in [index.html](../../Prism.Browser/wwwroot/index.html) and [app.css](../../Prism.Browser/wwwroot/app.css) matches the card: the dark navy page, the mark, the name, a progress bar, the status line and a note that the first visit downloads the app once and later visits open from the browser's cache. It is plain HTML, because it shows before .NET and Avalonia have loaded. Avalonia removes it once the app has drawn.
- [main.js](../../Prism.Browser/wwwroot/main.js) moves the bar from the .NET loader's download progress (files loaded of all files) to 80 %, with *Downloading Prism… N %*, then shows *Starting the geometry kernel…* at 90 % until the app is up.

## Not done

- Tuning the bar's stage positions against real `startup.log` timings from the owner's machine (they are estimates from the headless timing).
- Ready-to-run (precompiled) publishing, which would shorten the .NET runtime part.
- A light-theme version, or a painted background image from Codex behind the mark (options the owner was offered).
