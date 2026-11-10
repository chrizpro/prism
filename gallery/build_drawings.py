"""Build editable gallery drawings from existing Prism models via desktop automation."""

import argparse
import json
from pathlib import Path
from urllib.request import Request, urlopen


GALLERY = Path(__file__).resolve().parent
EXAMPLES = [
    ("wrench", "Wrench", "Combination wrench", "WR-013", "Chrome-vanadium steel",
     1, (147, 105), (147, 181), (323, 117), "Isometric view 1:2. Gallery reference."),
    ("serving-board", "Serving board", "Maple serving board", "SB-300", "Maple",
     0.5, (132, 106), (132, 195), (317, 105), "Recess and handle slot. Gallery reference."),
    ("fold-stand", "Fold stand", "Fold tablet stand", "FS-140", "Aluminium",
     0.75, (123, 91), (123, 213), (305, 112), "Folded part only. Gallery reference."),
]


def build(call, output):
    for source, body, title, number, material, scale, top, front, iso, notes in EXAMPLES:
        target = GALLERY / "models" / f"{source}-drawing.prism"
        call("file", action="new")
        call("file", action="open", path=str(GALLERY / "models" / f"{source}.prism"))
        call("command_cancel")
        call("drawing_document", action="new_sheet")
        call("drawing_document", action="sheet_settings", settings={
            "title": title, "number": number, "material": material, "notes": notes,
            "format": "A3", "scale": scale,
        })
        views = []
        for orientation, (x, y) in (("top", top), ("front", front), ("iso", iso)):
            view_scale = 0.5 if source == "wrench" and orientation == "iso" else scale
            state = call("drawing_document", action="add_view", sources=[body],
                         orientation=orientation, x=x, y=y, scale=view_scale)
            view = state["Sheets"][0]["Views"][-1]
            assert view["Strokes"] and not view["Unresolved"], (title, orientation)
            views.append(view)

        for view in views[:2]:
            suggestions = call("drawing_document", action="suggest", view_id=view["Id"],
                               category="Overall size")
            # The top already carries the overall width. Keep the front height only.
            ids = [item["Id"] for item in suggestions
                   if view is views[0] or item["Type"] == "vertical"]
            assert ids, (title, view["Name"])
            call("drawing_document", action="accept", suggestions=ids)
            for item in suggestions:
                if item["Id"] in ids and item["Type"] == "horizontal":
                    # Leave a clear gap between the view caption and the dimension.
                    call("drawing_document", action="move", annotation_id=item["Id"], x=0, y=10)

        # Clear the source's saved workflow and opening command on the drawing copy.
        call("select_tab", title="Solid")
        call("select_tab", title="Home")
        call("file", action="save_as", path=str(target))
        saved = json.loads(target.read_text(encoding="utf-8"))
        assert saved["Tab"] == "Home" and saved["OpenCommand"] is None
        before = call("drawing_document", action="state")
        call("file", action="new")
        call("file", action="open", path=str(target))
        after = call("drawing_document", action="state")
        assert before == after and len(after["Sheets"][0]["Annotations"]) == 3, title
        call("drawing_document", action="export", path=str(output / f"{target.stem}.svg"))
        print(title, "reopened with", len(views), "views and 3 dimensions", flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--discovery", required=True, type=Path)
    parser.add_argument("--svg-output", required=True, type=Path)
    args = parser.parse_args()
    connection = json.loads(args.discovery.read_text())
    output = args.svg_output.resolve()
    output.mkdir(parents=True, exist_ok=True)

    def call(tool, **arguments):
        request = Request(f"http://127.0.0.1:{connection['port']}/call",
                          data=json.dumps({"tool": tool, "args": arguments}).encode(),
                          headers={"X-Prism-Token": connection["token"],
                                   "Content-Type": "application/json"})
        with urlopen(request, timeout=120) as response:
            result = json.load(response)
        if not result["ok"]:
            raise RuntimeError(f"{tool}: {result['error']}")
        return result["result"]

    build(call, output)


if __name__ == "__main__":
    main()
