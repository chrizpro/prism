# Known issues

::: warning Prism is in alpha
Prism's feature set is in place and the work now is polishing it. Until Prism reaches a stable release, expect the following:

- **You will run into bugs.** Some tools have edge cases that fail or give the wrong result.
- **Features and workflows can change** between releases, including ribbon layouts and command options.
- **It is not ready for production work.** Check important results, such as dimensions, simulation numbers and toolpaths, before relying on them.
- **Keep backups** of your models, especially before installing a newer release.
:::

This page lists problems we know about. The live list is the set of [open bug reports](https://github.com/chrizpro/prism/issues?q=is%3Aissue+is%3Aopen+label%3Abug) on GitHub.

## Open bugs

| Issue | Area | Problem |
| --- | --- | --- |
| [#32](https://github.com/chrizpro/prism/issues/32) | [Shell](/tool/shell) | Shelling a whole body inward returns the inner core instead of a closed hollow. A 10 mm cube with a 2 mm inside wall comes back as a 6 mm cube. |
| [#51](https://github.com/chrizpro/prism/issues/51) | [Boundary Surface](/tool/boundary-surface) › Patch | Patch rejects some bent closed loops as bulging outside their outline. **Workaround:** Boundary Surface's smooth path fills the same loop. |

## Platforms

| Platform | Status |
| --- | --- |
| Windows 10 and 11 | Most tested. Builds are not code-signed, so SmartScreen may warn on install. |
| macOS (Apple silicon and Intel) | Installers are built and checked automatically, including that the geometry and simulation libraries load. Less hands-on testing than Windows. Builds are not notarized: right-click Prism and choose **Open** the first time. |
| Linux (Debian, Ubuntu) | Installers are built and checked automatically in the same way. Less hands-on testing than Windows. |

## Limits by design

Some behavior is a current limit rather than a bug: for example, toolpath playback is a motion preview, stock review samples a heightfield, the 3D flow study has no turbulence model, and imported geometry doesn't bring its feature history with it. See [Current limitations](/reference/limitations) for the full list.

## Report a bug

Choose **Report a bug** in Prism's header, or **? › Report a bug**. It opens the [bug form](https://github.com/chrizpro/prism/issues/new?template=bug_report.yml) on GitHub with your Prism version and system filled in. The most useful reports include:

1. **The steps:** the tools you used and the values you entered, in order.
2. **What you expected,** and what happened instead, including any message Prism showed.
3. **A small model** that shows the problem, and a screenshot.
4. **`crash.log`** from `%LOCALAPPDATA%\Prism` if Prism closed unexpectedly (Windows).

Search the [open issues](https://github.com/chrizpro/prism/issues?q=is%3Aissue+is%3Aopen) first; if your problem is already there, add your details to it. Feature requests are limited for now; see the [contribution policy](https://github.com/chrizpro/prism#contributing).
