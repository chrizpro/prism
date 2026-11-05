# Third-party components

Installers do not ship this file. `Build-Installer.ps1` runs `Write-Notices.ps1`, which writes `THIRD-PARTY-NOTICES.txt` into the published app with the full license text of every component in that build:

- every NuGet package that ships, read from the restore output, so build-only packages such as Avalonia's developer tools are left out;
- the .NET runtime pack's license and third-party notices;
- every native library staged by `native/build.ps1`, with its version and upstream source from vcpkg's SPDX file;
- the source-code notice for the LGPL libraries (Open CASCADE Technology and Netgen) and the FreeType credit.

The native license files are also shipped in the app's `licenses` folder.

## LGPL source code

Open CASCADE Technology and Netgen are LGPL-2.1 and dynamically linked. The release workflow uploads `Prism-LGPL-sources.zip` with each release: the upstream source archives vcpkg built, plus the vcpkg ports (recipes and patches). Keep that asset on every release that ships those libraries.

## Not bundled

CalculiX (GPL-2.0), ffmpeg, Python and slicers are optional tools that users install themselves. Prism runs them as separate programs. Do not add them to the installer without reviewing their licenses.

## Credits

The public credits and privacy pages are `site/reference/credits.md` and `site/reference/privacy.md`. Update them when dependencies or network features change.
