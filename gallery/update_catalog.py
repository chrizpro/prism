"""Refresh content hashes and preview revisions, or check the published catalog without modifying it."""
import argparse
import hashlib
import json
from pathlib import Path


def updated_catalog(folder):
    catalog = json.loads((folder / "catalog.json").read_text(encoding="utf-8"))
    ids = set()
    for item in catalog["Items"]:
        id = item["Id"]
        assert id not in ids and id.replace("-", "").isalnum() and id.isascii(), id
        ids.add(id)
        model = (folder / "models" / (id + ".prism")).read_bytes()
        preview = (folder / "previews" / (id + ".png")).read_bytes()
        item["Bytes"] = len(model)
        item["Sha256"] = hashlib.sha256(model).hexdigest()
        base = "https://raw.githubusercontent.com/chrizpro/prism/develop/gallery/"
        item["ModelUrl"] = base + "models/" + id + ".prism"
        item["PreviewUrl"] = base + "previews/" + id + ".png?v=" + hashlib.sha256(preview).hexdigest()[:16]
    return json.dumps(catalog, indent=2) + "\n"


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    folder = Path(__file__).resolve().parent
    content = updated_catalog(folder)
    readme = (folder / "README.md").read_text(encoding="utf-8")
    missing = [item["Id"] for item in json.loads(content)["Items"] if "`" + item["Id"] + "`" not in readme]
    assert not missing, "Add these models to the index in gallery/README.md: " + ", ".join(missing)
    if args.check:
        assert (folder / "catalog.json").read_text(encoding="utf-8") == content, "Run python gallery/update_catalog.py"
        print("Catalog model sizes, hashes and preview revisions match.")
    else:
        (folder / "catalog.json").write_text(content, encoding="utf-8")
