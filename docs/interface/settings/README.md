# Settings

Canonical guide for the Settings window: search, pages, and the safe automation surface. Automation details live in [MCP](../../MCP.md); assistant behavior lives in [AI assistant](../assistant/README.md).

The cog next to Redo opens Settings. Preferences persist to `%LOCALAPPDATA%\Prism\settings.json`; `PRISM_SETTINGS_FILE` overrides the path (tests use it).

## Search

A search box sits above the page list inside the Settings window — search is scoped to settings only and does not feed the global `Ctrl+K` command search. Typing matches a built-in catalog of setting titles, page names, descriptions and aliases; results group by page. Selecting a result (click, Enter, or arrow keys) opens that page and focuses/highlights the matching control; Escape or an empty box returns to the normal page list. Searching never changes a value.

## Pages

Appearance (theme, accent, 3D view backdrop and grid presets), Interaction (right-click marking menu, hold time, reference plane size), Workspace (folder, home screen), Toolbars (in-graphics toolbar and status bar contents), Ribbon (custom sets and commands), AI Assistant (provider, model, effort, file access, API keys, MCP client setup), Keyboard (shortcuts).

## Automation

The `settings` tool exposes a **safe whitelist** catalog (`SettingDef` in `AutomationApi.cs`): `list` returns each key's title, page, type, choices and current value; `get`, `set` (validated like the UI, then `AppSettings.Save()`), `reset` to the documented default, and `open` shows the window on an optional page. Included keys cover theme, accent, grid preset, marking menu, toolbar/status-bar visibility, home screen, workspace behavior, and the assistant `provider`, `model` and `effort`. Secrets are never in the catalog: assistant API keys and the automation token can only be set in the panel, never read or written through automation.

`set`/`reset` reject unknown keys, wrong types and values outside the listed choices. Restricted assistant calls may read settings but the same validation applies to writes.

## Maintained tests

- [Prism.Tests/Headless/Assistant/AssistantToolTests.cs](../../../Prism.Tests/Headless/Assistant/AssistantToolTests.cs) — `settings` whitelist (`AST-H001`: list has assistant model/effort, no secrets), get/set/reset validation (`AST-H002`), restricted-endpoint reads (`AST-H008`). Links back here.
- Unit-level catalog/choice behavior is covered by [Prism.Tests/Unit/Assistant/AssistantCatalogTests.cs](../../../Prism.Tests/Unit/Assistant/AssistantCatalogTests.cs) (`AST-U006`, `AST-U007`) for the restricted gate.
