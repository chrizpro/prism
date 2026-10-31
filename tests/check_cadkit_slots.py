"""Checks that every slot label a cadkit wrapper uses is one the command really has (needs the app running).
A stale label only shows up when somebody calls the wrapper, so this reads api.py and asks the app.
    python tests/check_cadkit_slots.py"""
import os
import re
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "Prism", "Scripting", "python"))
import cadkit as cad

source = open(os.path.join(os.path.dirname(__file__), "..", "Prism", "Scripting", "python", "cadkit", "api.py"), encoding="utf-8").read()
bad, checked = [], 0
for m in re.finditer(r'run\(\s*"(\w+)"\s*,\s*slots=\{(.*?)\}', source, re.S):
    command, body = m.group(1), m.group(2)
    labels = re.findall(r'"([^"]+)"\s*:', body)
    try:
        have = [s["label"].lower() for s in cad.describe(command)["slots"]]
    except cad.CadError as e:
        bad.append("%s: %s" % (command, e)); continue
    for label in labels:
        checked += 1
        if not any(label.lower() in h for h in have):       # the app matches by partial, case-insensitive text
            bad.append("%s: slot %r not found; the command has %s" % (command, label, have))
print("%d slot labels checked" % checked)
for b in bad:
    print("FAIL " + b)
sys.exit(1 if bad else 0)
