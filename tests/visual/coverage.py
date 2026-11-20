"""Which explicitly registered commands do the visual scripts reference? Prints a count per family and lists gaps.

    python tests/visual/coverage.py            # summary
    python tests/visual/coverage.py --list     # every uncovered command

A reference counts when a scenario names the command or uses its corresponding cadkit helper.
The runtime startup sweep also checks automatically registered commands. Run the scenarios for execution evidence.
"""
import re
import sys
from collections import defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
source = (ROOT / "Prism/Automation/AutomationApi.cs").read_text(encoding="utf-8")
names = set(re.findall(r'\["([a-z_0-9]+)"\] = \(\) =>', source)) | set(re.findall(r'Registry\.TryAdd\("([a-z_0-9]+)"', source))
scripts = "\n".join(p.read_text(encoding="utf-8") for p in (ROOT / "tests/visual").glob("*.py") if p.name != "coverage.py")
sweep_source = scripts.split("def every_command_opens")[0] + "@scenario".join(scripts.split("def every_command_opens")[1].split("@scenario")[1:]) if "def every_command_opens" in scripts else scripts
used = {n for n in names if re.search(rf'["\']{n}["\']', sweep_source)}   # a command named as a string literal in a scenario other than the start-and-cancel sweep
helpers = {"box": "box", "cylinder": "cylinder", "sphere": "sphere", "union": "union", "fillet": "fillet", "shell": "shell", "extrude": "extrude", "revolve": "revolve",
           "move": "move", "holes": "hole", "fold": "fold", "chamfer": "chamfer"}
if '"joint_" +' in scripts:
    used |= {n for n in names if n.startswith("joint_")}      # the joint scenarios build the command name from the joint kind
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
print(f"Visual script references found for {total} of {len(names)} explicitly registered commands ({100 * total // len(names)}%)")
print("The separate command__every-command-opens-and-cancels scenario checks the live registry, including automatically registered commands.\n")
for f, (done, count, missing) in sorted(families.items(), key=lambda kv: -kv[1][1]):
    print(f"{f:22s} {done:3d} / {count:3d}")
if "--list" in sys.argv:
    print()
    for f, (done, count, missing) in sorted(families.items()):
        print(f"{f}: {', '.join(missing)}")
