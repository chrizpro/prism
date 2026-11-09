# Script

Canonical guide for the in-app Script Editor. Automation entry points are described in [MCP](../../MCP.md); the assistant workflow is described in [AI assistant](../assistant/README.md).

- **Write:** Script Editor (shows or hides the panel), New Script, Run Script
- **Your own editor:** VS Code, PyCharm, Scripts Folder
- **Write with AI:** the **AI** button in the panel header opens the assistant with the script-writing workflow; when the user accepts the answer, the whole editor text is replaced.

The panel shows the script's name, then Run and Stop, New, Open and Save, the **AI** assistant entry point, and Edit in (VS Code, PyCharm, Scripts folder). Below that are the editor, a console you can resize, and a status line. Python is colored as you type; a file over 200,000 characters stays plain so typing never lags.

![The script panel](script-panel-dark.png)

## AI writing and rollback

Accepting assistant code replaces the **entire** script text — the editor holds one file and scripts are short, so generated code is meant to be read as a whole. Before each replacement (or AI `insert`), the previous text is recorded as a snapshot; the assistant's **Roll back AI edit** action (or the `script_editor` `rollback` tool) restores the last snapshot, one step per call, in last-in-first-out order. Snapshots are kept for the session only and are not written to the script file. Edits made by typing do not create snapshots.

AI edits mark the script dirty like typed edits; nothing is saved or run automatically — **Save** and **Run** stay under user control. When the assistant's file-access permission is off, `run`, `save`, `open` and `new` are rejected on the assistant automation endpoint.

## Automation

The `script_editor` tool mirrors the panel for MCP clients and the assistant: `state`, `get`, `set`, `insert`, `rollback`, `new`, `open`, `save`, `run`.

## Maintained tests

- [Prism.Tests/Headless/Assistant/AssistantToolTests.cs](../../../Prism.Tests/Headless/Assistant/AssistantToolTests.cs) — `script_editor` replace/insert/rollback/dirty state and the restricted-endpoint write blocks (`AST-H003`–`AST-H005`). Links back here.
