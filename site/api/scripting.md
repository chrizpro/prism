# Python scripting

Scripts drive the running application through `cadkit`, a Python library that ships with Prism and needs only the standard library. Install [Python 3](https://www.python.org/downloads/) first and tick **Add to PATH**.

## Three ways to run a script

1. **Script workspace.** Pick **Script** in the workspace bar. The editor sits to the right of the model, with line numbers, **Run** (<kbd>F5</kbd>), **Stop** (<kbd>Shift+F5</kbd>), a console and an **Open** menu with the [examples](/api/examples). A failed script puts the cursor on the failing line.
2. **Run Script...** on the Script ribbon. Pick any `.py` file; it runs and shows the console.
3. **Your own editor.** The **VS Code** and **PyCharm** buttons open the script there. The scripts folder (`Documents\Prism\Scripts`) holds a copy of `cadkit` next to your scripts, so `import cadkit` just works. Run from the IDE while Prism is open; `cadkit` finds the application by itself.

## A first script

```python
import cadkit as cad

with cad.history("Plate"):                  # one undo step for the whole block
    plate = cad.box(width=80, depth=50, height=6)
    cad.holes(plate, [(-25, 0), (25, 0)], diameter=8)

print(plate.refresh())  # Box 1 [Solid], size 80 x 50 x 6 mm, volume 23396.8 mm3, 8 faces, 18 edges
cad.view("iso", fit=True)
```

Every modeling call returns an `Obj` (or a list of them). Pass it, or its name, to the next call. `print(obj)` gives a one-line summary with size, volume and face count.

## Group work into one undo step

Wrap edits in `with cad.history("name"):` and the whole block is a single step in the design history. Undo removes all of it at once.

## Sketches

```python
with cad.sketch("XZ") as s:                  # XY, XZ, YZ, a construction plane, or a face
    s.polyline([(0, 25), (0, 0), (50, 0), (50, 25)])
    s.circle((25, 12), 4)
profile = s.obj
```

Sketch methods: `line(a, b)`, `polyline(points, closed=False)`, `rect(corner1, corner2)`, `circle(center, radius)`, `arc(start, through, end)` and `spline(points, closed=False)`. To sketch on a face, pass `{"object": "Box 1", "face": 6}`.

## Find faces and edges by geometry

Indices change after booleans and fillets. Find geometry by what it is instead:

```python
top = cad.faces(body, type="Plane", facing=(0, 0, 1))
vertical = cad.edges(body, direction=(0, 0, 1), min_size=10)
print(cad.check(body))                      # validity, volume, bounding box, open edges ...
```

The filters are those of the [`solid_find`](/api/automation#solid-find) tool: `type`, `facing` and `angle`, `direction`, `radius`, `box`, `near` and `distance`, `of_face`, `min_size`, `max_size`, `sort` and `limit`.

## Reach every command

The friendly functions cover common work. Everything else goes through `run()`:

```python
cad.run("fillet", slots={"Edges": [{"object": "Box 1", "edge": 3}]}, radius=2)
cad.run("shell", slots={"Faces to remove": [{"object": "Box 1", "face": 3}]}, wall_thickness=2)
```

Use `cad.commands("query")` to list command names, `cad.describe("name")` to see a command's slots and parameters, and `cad.help("query")` to search both commands and functions. The [command reference](/api/commands) lists them all. `cad.call("tool", **args)` reaches any [automation tool](/api/automation).

## Ask the user

```python
answers = cad.ask("Plate", width=80, depth=50, holes=True, material=["Steel", "Aluminum"])
if answers is None:
    raise SystemExit("Cancelled")
```

`cad.ask` shows a dialog and waits. The default you give picks the field: a number, text, a checkbox, or a list for a choice. `cad.dialog(title, fields)` gives full control over the fields.

## Errors

When the application refuses a request, `cadkit` raises `cad.CadError` with the same message the app would show. Wrong or missing arguments list what the tool accepts; unknown tool names get a "did you mean" suggestion.

## Adding another language

Script languages implement `IScriptLanguage` (`Core/Scripting/ScriptLanguage.cs`): `Name`, `Extension`, `Starter`, `IsAvailable` and `RunAsync(scriptPath, env, output, ct)`, which receives the automation address in `ScriptEnvironment`. Register it in `ScriptLanguages.All`; the editor, console, Stop button and file filters then work unchanged.
