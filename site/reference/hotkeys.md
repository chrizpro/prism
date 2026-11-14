# Keyboard shortcuts

Prism's shortcuts, and when each one works. You can change any of them, add your own for any ribbon command, or clear one, in **Settings → Keyboard**. This page lists the defaults.

## How shortcuts are chosen

A key can mean different things in different places. Prism looks at what you are doing and uses the most specific meaning:

1. **While editing Freeform**, then
2. **While a command is open**, then
3. **While sketching**, then
4. the **workspace** you are in (a shortcut can be limited to one), then
5. **Everywhere**.

So <kbd>R</kbd> rotates a cage selection while you edit a Freeform, rotates the gizmo or draws a rectangle while you sketch, and does nothing elsewhere. A key typed into a text box is text, never a shortcut. Search results (<kbd>Ctrl</kbd>+<kbd>K</kbd>) show the key next to each command that has one, including any you changed or added.

## Fit the view

<kbd>F</kbd> fits everything in the window (Zoom Extents), and so does <kbd>Home</kbd> outside the animation timeline. <kbd>F</kbd> was chosen because it is one hand from the mouse, is not used by any other shortcut in any context, and is the key the drawing editor already uses for Fit.

## Sketch

| Action | Key | Works | Notes |
| --- | --- | --- | --- |
| Start or resume sketch | <kbd>K</kbd> | Everywhere |  |
| Gizmo: move | <kbd>W</kbd> | While sketching | Needs a gizmo target |
| Gizmo: rotate | <kbd>R</kbd> | While sketching | Needs a gizmo target |
| Gizmo: scale | <kbd>S</kbd> | While sketching | Needs a gizmo target |
| Toggle geometry editing (3D) | <kbd>E</kbd> | While sketching |  |
| Draw on the YZ plane | <kbd>Shift</kbd> + <kbd>X</kbd> | While sketching |  |
| Draw on the XZ plane | <kbd>Shift</kbd> + <kbd>Y</kbd> | While sketching |  |
| Draw on the XY plane | <kbd>Shift</kbd> + <kbd>Z</kbd> | While sketching |  |

## File

| Action | Key | Works | Notes |
| --- | --- | --- | --- |
| New part | <kbd>Ctrl</kbd> + <kbd>N</kbd> | Everywhere |  |
| Open… | <kbd>Ctrl</kbd> + <kbd>O</kbd> | Everywhere |  |
| Save | <kbd>Ctrl</kbd> + <kbd>S</kbd> | Everywhere |  |
| Save as… | <kbd>Ctrl</kbd> + <kbd>Shift</kbd> + <kbd>S</kbd> | Everywhere |  |

## Edit

| Action | Key | Works | Notes |
| --- | --- | --- | --- |
| Undo | <kbd>Ctrl</kbd> + <kbd>Z</kbd> | Everywhere |  |
| Redo | <kbd>Ctrl</kbd> + <kbd>Y</kbd> | Everywhere |  |
| Duplicate selection | <kbd>Ctrl</kbd> + <kbd>D</kbd> | Everywhere |  |
| Select all | <kbd>Ctrl</kbd> + <kbd>A</kbd> | Everywhere | Selects everything at the current pick level: objects, faces, edges or corner points. While you edit a Freeform, it selects all cage components |
| Copy selection | <kbd>Ctrl</kbd> + <kbd>C</kbd> | Everywhere | Remembers the selected objects |
| Paste | <kbd>Ctrl</kbd> + <kbd>V</kbd> | Everywhere | Pastes independent copies in the same place, selected and ready to move. Works in another open document too |
| Group selection | <kbd>Ctrl</kbd> + <kbd>G</kbd> | Everywhere | Puts the selected objects under one parent row in the browser |
| Ungroup selection | <kbd>Ctrl</kbd> + <kbd>Shift</kbd> + <kbd>G</kbd> | Everywhere | Takes the objects out of their group |
| Delete selection | <kbd>Delete</kbd> | Everywhere |  |
| Cancel / end the current step | <kbd>Escape</kbd> | Everywhere |  |
| Accept / finish | <kbd>Enter</kbd> | Everywhere |  |

## View

| Action | Key | Works | Notes |
| --- | --- | --- | --- |
| Look at the selected plane, face or sketch | <kbd>N</kbd> | Everywhere |  |
| Zoom to fit everything (Zoom Extents) | <kbd>F</kbd> | Everywhere | F for Fit; the same key fits the view in the drawing editor |
| Zoom to fit everything (Home key) | <kbd>Home</kbd> | Everywhere | A second key for Zoom Extents; while the animation timeline is open Home goes to the start instead |
| Maximize or restore hovered view (Four Views) | <kbd>Space</kbd> | Everywhere | Hover a pane in Four Views and press Space to maximize it, or press Space again to return; while the animation timeline is open Space plays or pauses |
| Search commands | <kbd>Ctrl</kbd> + <kbd>K</kbd> | Everywhere |  |

## Modeling

| Action | Key | Works | Notes |
| --- | --- | --- | --- |
| Extrude | <kbd>Alt</kbd> + <kbd>X</kbd> | Everywhere | Outside sketching and active editors |
| Edit Gizmo | <kbd>G</kbd> | Everywhere | At the viewport cursor (origin if the pointer is outside it) |

## Freeform

| Action | Key | Works | Notes |
| --- | --- | --- | --- |
| Edit selected Freeform (Modify) | <kbd>M</kbd> | Everywhere |  |
| Translate cage selection | <kbd>T</kbd> | While editing Freeform |  |
| Rotate cage selection | <kbd>R</kbd> | While editing Freeform |  |
| Scale cage selection | <kbd>S</kbd> | While editing Freeform |  |
| Extrude cage selection | <kbd>E</kbd> | While editing Freeform |  |
| Crease sharpness… | <kbd>C</kbd> | While editing Freeform |  |
| Select vertices | <kbd>1</kbd> | While editing Freeform |  |
| Select edges | <kbd>2</kbd> | While editing Freeform |  |
| Select faces | <kbd>3</kbd> | While editing Freeform |  |
| Select whole form | <kbd>4</kbd> | While editing Freeform |  |
| Select all cage components | <kbd>Ctrl</kbd> + <kbd>A</kbd> | While editing Freeform |  |
| Select edge loop | <kbd>Alt</kbd> + <kbd>L</kbd> | While editing Freeform |  |
| Select edge ring | <kbd>Alt</kbd> + <kbd>R</kbd> | While editing Freeform |  |

## Sketch tools

| Action | Key | Works | Notes |
| --- | --- | --- | --- |
| Line | <kbd>L</kbd> | While sketching |  |
| Rectangle | <kbd>R</kbd> | While sketching | Also rotates a rectangle while you place it |
| Ellipse / circle | <kbd>C</kbd> | While sketching |  |
| Spline | <kbd>S</kbd> | While sketching |  |
| Arc | <kbd>A</kbd> | While sketching |  |
| Dimension | <kbd>D</kbd> | While sketching |  |
| Toggle construction geometry | <kbd>X</kbd> | While sketching |  |

## Animation

| Action | Key | Works | Notes |
| --- | --- | --- | --- |
| Play / stop the animation | <kbd>Space</kbd> | While a command is open | While the animation timeline is open |
| Animation back to the start | <kbd>Home</kbd> | While a command is open | While the animation timeline is open |

## Keys inside a tool

Some tools take their own keys while they are active, for example <kbd>Enter</kbd> to finish, <kbd>Backspace</kbd> or <kbd>Delete</kbd> to remove the last point, and <kbd>Esc</kbd> to step back. The prompt at the top of the viewport lists them while the tool is open, and each [tool page](/tools/) describes them.
