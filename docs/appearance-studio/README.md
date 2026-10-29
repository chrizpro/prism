# Appearance studio

Open **Materials** from the ribbon to use the redesigned right-side panel. The Appearance tab contains six locally drawn finish swatches, a selection preview, color palette and hex input, metallic/gloss controls, pattern size and grain angle. Finish changes apply immediately to selected bodies. Reset restores each body's assigned material finish. The Materials tab retains the existing material search, engineering properties, assignment and comparison tools.

Presets change visual appearance only and preserve pinned engineering properties. Applying a preset or resetting several selected bodies creates one undo step. Swatch previews are illustrative procedural drawings, not viewport render captures.

Verification: `dotnet run --project tests/Prism.AppearanceTests` runs 26 native geometry and headless UI checks, including engineering data preservation, grouped undo, no-selection state, tab navigation and the existing 312px sidebar. It saves light and dark panel screenshots here. The desktop build is available at `artifacts/appearance-preview/Prism.Desktop.exe`; the running application was left untouched.
