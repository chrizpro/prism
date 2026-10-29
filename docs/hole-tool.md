# Hole tool

Open **Home → Modify → Hole** or **Solid → Direct → Hole**. Hole is also available in command search, the body/face context menu, and automation as `hole`.

Select a body or a planar entry face. Body selections drill from the top center along world Z; face selections drill inward from the face center. Set **Center offset U/V** in that entry plane to position the hole. World X/Y/Z directions and Reverse direction support other orientations.

- **Plain** uses the specified hole diameter.
- **Threaded (modeled)** uses a nominal major diameter and pitch. Presets cover M3, M4, M5, M6, M8, M10 and M12 coarse sizes; Custom allows other diameter/pitch pairs.
- **Through all** extends to the far side of the target bounds. **Blind** uses Hole depth.
- Thread depth can be shorter than the bore; zero threads the full hole. Right-hand and left-hand threads are supported.

Threads are truncated 60° helical B-rep geometry with no manufacturing allowance, tolerance class or drill point. They survive document saves and STEP export. Modeled threads are limited to 200 turns per hole.

The feature preserves the body's identity, material and appearance and supports preview, undo/redo and history editing. Imported solids and closed freeform bodies are supported. Applying a hole to a freeform converts the result to an exact B-rep object. Exact surfaces support plain holes; threading requires a closed solid. Convert mesh objects with **Mesh → Mesh to Solid** first. Curved entry faces require an explicit world direction.

Validation: `dotnet run --project tests/Prism.HoleTests` from the repository root. Build and stage the native kernel with `native/build.ps1` after changing the C++ implementation.
