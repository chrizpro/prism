# Current scope and limitations

Prism is in alpha and under active development. For bugs we know about, see [Known issues](/reference/known-issues). A tool appearing in the catalog does not imply that every geometric case is supported.

## Platforms

Windows is the most thoroughly tested desktop platform. Installers for macOS (Apple silicon and Intel) and Linux are built by the release workflow on those systems and check that the native geometry and simulation libraries load, but they have had less hands-on testing. Preview builds are not code-signed or notarized.

## Geometry and history

Imported geometry does not bring another application's feature history with it. Some derived sketch references are fixed snapshots. Surface matching and freeform conversion have restrictions around compatible boundaries, trimming, irregular topology, and exactness.

## Manufacturing

Toolpath playback is a motion preview. It does not establish that a program is safe to machine or provide complete stock-removal and collision verification. External slicer G-code import supports a defined subset of linear motion commands.

## Analysis

Static, thermal, and modal workflows have material and setup restrictions. Thermal and modal analysis use an external CalculiX installation. Pipe flow is a reduced-order circular-pipe calculation, rather than a general 3D fluid solver.

## Rendering and AR

Animation video output requires ffmpeg for MP4 generation. AR support depends on the phone, browser, and installed viewer. Local network sharing is separate from hosted publication.
