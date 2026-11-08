"""Which of Prism's commands do the visual scripts exercise? Prints a count per family and lists what is not touched yet.

    python tests/visual/coverage.py            # summary
    python tests/visual/coverage.py --list     # every uncovered command

A command counts as covered when a scenario starts it by its automation name (``name="..."``) or through the cadkit helper of the same name
(``cad.extrude``, ``cad.fillet`` ...). Touching a command is not the same as testing every option: the feature-matrix scenarios vary the options,
the others only run the command once. Read the list as "what has had no automated look at all".
"""
import re
import sys
from collections import defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
source = (ROOT / "Prism/Automation/AutomationApi.cs").read_text(encoding="utf-8")
names = set(re.findall(r'\["([a-z_0-9]+)"\] = \(\) =>', source)) | set(re.findall(r'Registry\.TryAdd\("([a-z_0-9]+)"', source))
scripts = "\n".join(p.read_text(encoding="utf-8") for p in (ROOT / "tests/visual").glob("*.py") if p.name != "coverage.py")
used = set(re.findall(r'name="([a-z_0-9]+)"', scripts))
helpers = {"box": "box", "cylinder": "cylinder", "sphere": "sphere", "union": "union", "fillet": "fillet", "shell": "shell", "extrude": "extrude", "revolve": "revolve",
           "move": "move", "holes": "hole", "fold": "fold", "chamfer": "chamfer"}
used |= {command for helper, command in helpers.items() if re.search(rf"cad\.{helper}\(|cad\.run\(\"{helper}\"", scripts)}
families = defaultdict(lambda: [0, 0, []])
for n in sorted(names):
    f = n.split("_")[0] if n.split("_")[0] in ("cnc", "cam", "cae", "sim", "curve", "mesh", "form", "joint", "motion", "pcb", "pattern", "sketch", "cae") else "solid/surface/other"
    families[f][1] += 1
    if n in used:
        families[f][0] += 1
    else:
        families[f][2].append(n)
total = sum(f[0] for f in families.values())
print(f"{total} of {len(names)} registered commands are exercised with real inputs by the visual scripts ({100 * total // len(names)}%)")
print("The sweep scenario command__every-command-opens-and-cancels also starts and cancels all of them, so a crash on opening is caught for every command.\n")
for f, (done, count, missing) in sorted(families.items(), key=lambda kv: -kv[1][1]):
    print(f"{f:22s} {done:3d} / {count:3d}")
if "--list" in sys.argv:
    print()
    for f, (done, count, missing) in sorted(families.items()):
        print(f"{f}: {', '.join(missing)}")
