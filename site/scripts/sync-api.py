"""Refresh the API snapshots the guide is built from.

    python site/scripts/sync-api.py

Writes, under site/data:
  cadkit.json            the public cadkit functions, read from the library source
  examples/*.py          the example scripts shipped with the app
  commands.json          every modeling command with its inputs and settings   (needs Prism running)
  automation-tools.json  the automation tool catalogue with argument schemas  (needs Prism running)

The app is found the same way any cadkit script finds it. Without it, only cadkit.json is refreshed.
"""
import ast
import json
import os
import sys

root = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
library = os.path.join(root, "Prism", "Scripting", "python")
sys.path.insert(0, library)
import cadkit as cad  # noqa: E402

out = os.path.join(root, "site", "data")
os.makedirs(out, exist_ok=True)


def write(file, data):
    with open(os.path.join(out, file), "w", encoding="utf-8", newline="\n") as f:
        json.dump(data, f, indent=1, ensure_ascii=False)
        f.write("\n")


def cadkit_reference():
    """Signatures and docstrings of everything cadkit exports, in source order."""
    exported = set(cad.__all__)
    entries = []
    for module in ("_conn.py", "api.py"):
        with open(os.path.join(library, "cadkit", module), encoding="utf-8") as f:
            tree = ast.parse(f.read())
        for node in tree.body:
            if isinstance(node, (ast.FunctionDef, ast.ClassDef)) and node.name in exported:
                entry = {"name": node.name, "kind": "class" if isinstance(node, ast.ClassDef) else "function",
                         "doc": ast.get_docstring(node) or "", "line": node.lineno, "module": module}
                if isinstance(node, ast.FunctionDef):
                    entry["signature"] = "%s(%s)" % (node.name, ast.unparse(node.args))
                    entry["decorators"] = [ast.unparse(d) for d in node.decorator_list]
                else:
                    entry["members"] = [{"name": m.name, "signature": "%s(%s)" % (m.name, ast.unparse(m.args)),
                                         "doc": ast.get_docstring(m) or "",
                                         "property": any(ast.unparse(d) == "property" for d in m.decorator_list)}
                                        for m in node.body if isinstance(m, ast.FunctionDef) and not m.name.startswith("_")]
                entries.append(entry)
    return entries


write("cadkit.json", cadkit_reference())

# The example scripts shipped with the app, so the hosted guide does not need the CAD source.
examples = os.path.join(root, "Prism", "Scripting", "examples")
os.makedirs(os.path.join(out, "examples"), exist_ok=True)
for name in sorted(os.listdir(examples)):
    if name.endswith(".py"):
        with open(os.path.join(examples, name), encoding="utf-8") as src, \
                open(os.path.join(out, "examples", name), "w", encoding="utf-8", newline="\n") as dst:
            dst.write(src.read())

try:
    names = cad.call("list_commands", limit=10000)["names"]
except cad.CadError as e:
    print("cadkit.json refreshed;", e)
    sys.exit(0)

commands = []
for name in sorted(names):
    try:
        commands.append(cad.call("describe_command", name=name))
    except cad.CadError as e:
        print("skipped", name, e)

write("commands.json", commands)
write("automation-tools.json", sorted(cad.tools(), key=lambda t: t["name"]))
print(len(commands), "commands and the automation tools refreshed")
