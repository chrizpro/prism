# Interface

- [Ribbon and workspace index](../tabs/README.md)
- [Consistent operation progress](operation-progress.md)
- [Startup model gallery](gallery/README.md)
- [Saving, closing and recovery copies](document-safety/README.md)
- [Document tabs](document-tabs/README.md)
- [Reference images](reference-images/README.md)
- [Gizmos and snapping](../gizmo-workflows/README.md)
- [Data panel](data/README.md)
- [Script panel](script/README.md)
- [Settings](settings/README.md)
- [AI assistant](assistant/README.md)
- [Sketch hotkeys](../sketch-hotkeys.md)
- [All keyboard shortcuts](../../site/reference/hotkeys.md) (published at `/reference/hotkeys`; the catalogue is `Prism/Core/Hotkeys.cs`, so update the page when it changes). Owner direction 2026-11-09: Zoom Extents gets **F**, with Home kept as a second key; command search rows show each command's current key.
- [Selection](selection/README.md)
- **Zoom Selected** (owner request 2026-11-15, test HDL-H041): on the View tab, Zoom Extents is now a dropdown whose second choice, Zoom Selected, centres the selected bodies, faces or edges in every visible view and fills the window with them (never closer than a 10 mm box, so one corner point does not zoom in absurdly). It is also in the viewport right-click menu as "Zoom to selection" when something is selected. With nothing selected, or in a sketch, it says so in the status bar. No keyboard shortcut yet.
- **Extrude arrow position** (HDL-H040/H042): the arrow stands on the middle of the profile that was picked, wherever it is: a closed shape, an open outline or a chain of edges. It used to jump to the origin for outlines with no surface.
- [Home orientation](view-home/README.md)
- [Inspect and Appearance](inspection/README.md)
- [Feedback, bug and crash reporting](reporting/README.md)

## Buttons with a list (owner rule 2026-11-20)

Every ribbon button with a list behaves the same. A click anywhere on the button runs its action: the one picked last, or, when nothing has been picked yet, the first item that can run. Only the arrow opens the list (the right end of a small button, the label line of a large one), as does a right-click. Picking from the list runs that action at once and makes it the one the button runs next; the choice is remembered between sessions. Before, plain lists (Export As and others) only opened, and small split buttons such as Face / Edge / Vertex opened their list on a click on the name. Check: [RIB-H006](../../Prism.Tests/Headless/Ribbon/RibbonListButtonTests.cs).
