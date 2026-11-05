# Credits and licenses

Prism is built on open-source software. Thank you to everyone who works on these projects.

The installed app includes a `THIRD-PARTY-NOTICES.txt` file with the full license text of every component it ships, and a `licenses` folder with the license files of the native libraries.

## In the desktop app

| Component | Used for | License |
| --- | --- | --- |
| [Open CASCADE Technology](https://github.com/Open-Cascade-SAS/OCCT) | Exact geometry kernel: solids, surfaces, booleans, exchange | LGPL-2.1 with the OCCT exception |
| [Netgen](https://github.com/NGSolve/netgen) | Tetrahedral meshing for simulation | LGPL-2.1 |
| [Eigen](https://eigen.tuxfamily.org/) | Linear algebra in the simulation solver | MPL-2.0 |
| [FreeType](https://freetype.org/) | Font rendering in the geometry kernel | FreeType License |
| [libpng](http://www.libpng.org/pub/png/libpng.html), [zlib](https://zlib.net/), [bzip2](https://sourceware.org/bzip2/), [Brotli](https://github.com/google/brotli) | Compression and image support for the kernel | libpng, zlib, bzip2 and MIT licenses |
| [.NET](https://github.com/dotnet/runtime) | Runtime | MIT |
| [Avalonia](https://github.com/AvaloniaUI/Avalonia) | User interface | MIT |
| [SkiaSharp](https://github.com/mono/SkiaSharp) and [Skia](https://skia.org/) | 2D drawing | MIT and BSD-3-Clause |
| [HarfBuzz](https://github.com/harfbuzz/harfbuzz) | Text shaping | Old MIT |
| [ANGLE](https://chromium.googlesource.com/angle/angle) | OpenGL on Windows | BSD-3-Clause |
| [Silk.NET](https://github.com/dotnet/Silk.NET) | OpenGL bindings for the 3D view | MIT |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | Interface plumbing | MIT |
| [Tmds.DBus](https://github.com/tmds/Tmds.DBus) | Desktop integration on Linux | MIT |
| [Inter](https://rsms.me/inter/) | Interface font | SIL Open Font License 1.1 |
| Microsoft Visual C++ runtime | Native runtime on Windows | Microsoft distributable code terms |

Portions of this software are copyright © The FreeType Project ([www.freetype.org](https://freetype.org/)). All rights reserved.

### Source code of the LGPL libraries

Open CASCADE Technology and Netgen are linked as separate shared libraries, so you can replace them with your own builds of compatible versions. Each [release](https://github.com/chrizpro/prism/releases) includes `Prism-LGPL-sources.zip` with the exact source used: the upstream archives plus the [vcpkg](https://github.com/microsoft/vcpkg) build recipes and patches. If it is missing, [ask for it](https://github.com/chrizpro/prism/issues) and it will be provided.

## Optional tools you install yourself

Prism can use these if they are on your computer. They are not included in the installer and run as separate programs.

| Tool | Used for | License |
| --- | --- | --- |
| [CalculiX](http://www.calculix.de/) | Thermal and modal studies | GPL-2.0 |
| [ffmpeg](https://ffmpeg.org/) | MP4 output for animations | LGPL-2.1 or GPL, depending on the build |
| [Python](https://www.python.org/) | Scripts | PSF License |
| Your slicer (Bambu Studio, OrcaSlicer, PrusaSlicer and others) | Opening exported prints | The slicer's own license |

## On the phone (AR sharing)

The page Prism serves to your phone loads Google's [model-viewer](https://modelviewer.dev/) (Apache-2.0) from Google's servers to display the model.

## The websites

The website is built with [Astro](https://astro.build/) and this documentation with [VitePress](https://vitepress.dev/), both MIT. They use the [Inter](https://rsms.me/inter/), [Space Grotesk](https://github.com/floriankarsten/space-grotesk) and [JetBrains Mono](https://www.jetbrains.com/lp/mono/) fonts under the SIL Open Font License 1.1, served from the site itself. Both are hosted on GitHub Pages.

## Trademarks

Open CASCADE is a trademark of Open Cascade SAS. Other names are trademarks of their respective owners. Their use here identifies the software Prism uses and does not imply endorsement.
