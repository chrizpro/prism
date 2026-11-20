"""Gallery models for the Direct Modeling before/after studies: one assembly per study holding the fresh STEP import (left) and the
revised part (right), each inserted as an unlinked component so the file stands alone.

    python gallery/build_direct_before_after.py --discovery <automation.json>
"""
import argparse
from pathlib import Path

import build_examples as study

cad = study.cad
HERE = Path(__file__).resolve().parent
STUDIES = ["mounting-plate", "reduction-gearbox", "spindle-carrier", "pump-bracket"]


def bounds(path):
    cad.call("file", action="open", path=str(path))
    objects = cad.objects()
    lo = [min(o.info["min"][i] for o in objects) for i in range(3)]
    hi = [max(o.info["max"][i] for o in objects) for i in range(3)]
    return lo, hi, len(objects)


def close_all():
    while len(cad.call("document_list")) > 1:
        cad.call("document_close", document=0, discard=True)


def insert(path, x):
    cad.call("run_command", name="insert_component", params={
        "…or from a part file": str(path), "Place at X": x, "Place at Y": 0, "Place at Z": 0,
        "Ground it (fix it in place)": False, "Keep linked to the file (Refresh updates it)": False, "Move with gizmo": False})


def build(name):
    before, after = HERE / "extras" / f"{name}-imported.prism", HERE / "models" / f"{name}-revised.prism"
    b_lo, b_hi, b_n = bounds(before)
    a_lo, a_hi, a_n = bounds(after)
    gap = 0.15 * max(b_hi[0] - b_lo[0], a_hi[0] - a_lo[0])
    cad.call("document_new")
    insert(before, -gap / 2 - b_hi[0])
    insert(after, gap / 2 - a_lo[0])
    first, second = cad.objects()
    first.rename("Before - STEP import")
    second.rename("After - Direct Modeling")
    out = HERE / "models" / f"{name}-before-after.prism"
    cad.save(str(out))
    objects = cad.objects()
    assert all(cad.call("object_info", object=o.info["id"])["valid"] for o in objects), name
    print(name, len(objects), "objects", [o.name for o in objects][:6], flush=True)
    close_all()


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--discovery", required=True)
    parser.add_argument("--only", nargs="*")
    args = parser.parse_args()
    bridge, log = study.connect_mcp(args.discovery)
    try:
        for name in STUDIES:
            if not args.only or name in args.only:
                build(name)
    finally:
        bridge.terminate()
        bridge.wait(timeout=10)
        log.close()
