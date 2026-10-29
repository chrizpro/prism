# Scripting

Scripts drive the running application through the same localhost tools the MCP bridge uses, so anything you can click you can script.

## Three ways to run a script
1. **Script workspace** (pick Script in the workspace bar). The editor sits to the right of the model, like a script persona: the model stays visible while you work. It has a code editor, line numbers, Run (F5) / Stop (Shift+F5), a console, and an Open menu with the examples. A failed script puts the cursor on the failing line.
2. **Run Script...** (ribbon: Script). Pick any `.py` file and it runs, showing the console.
3. **Your own editor.** VS Code / PyCharm buttons open the script there. The scripts folder (`Documents\<App>\Scripts`) holds a copy of the `cadkit` library next to your scripts, so `import cadkit` works; run from the IDE while the application is open (it finds the application by itself). Install Python 3 first (python.org, "Add to PATH").

Each script (inside `with cad.history("name"):`) can be undone in a single step.

## Asking the user (dialogs)
```python
answers = cad.ask("Plate", width=80, depth=50, holes=True, material=["Steel", "Aluminum"])
if answers is None: raise SystemExit("Cancelled")
```

`cad.ask` shows a dialog and waits: the default you give picks the field (number, text, checkbox, or a list for a choice). `cad.dialog(title, fields)` gives full control. Example: `04_ask_for_a_plate.py`.

## Python API (`cadkit`, standard library only)
```python
import cadkit as cad
with cad.history("Bracket"):
    with cad.sketch("XZ") as s:
        s.polyline([(0, 25), (0, 0), (50, 0), (50, 25)])
    body = cad.flange(s.obj, thickness=2, bend_radius=3, width=40)
    cad.flat_pattern(body)
```
`cad.run("any_command", slots={...}, some_parameter=value)` reaches every modeling command; `cad.call("tool", **args)` reaches every application tool. Parameter names are snake_case and matched to on-screen labels ignoring punctuation (`k_factor` finds "K-factor").

## Adding another language (e.g. C#)
Implement `IScriptLanguage` (Core/Scripting/ScriptLanguage.cs): `Name`, `Extension`, `Starter`, `IsAvailable`, and `RunAsync(scriptPath, env, output, ct)` which receives the address of the automation service in `ScriptEnvironment`. Register it in `ScriptLanguages.All`; the editor, console, Stop button, language box and file filters then work unchanged. A C# language can run in-process (Roslyn scripting) or by launching `dotnet`; a `CadKit` .NET library would mirror `cadkit`'s functions over the same `/call` endpoint.

## Renaming the product
The name appears in `ScriptPaths.AppFolder` (C#), `_APP_FOLDERS` in `cadkit/_conn.py` (the discovery-file folder), and the `X-Prism-Token` header.
