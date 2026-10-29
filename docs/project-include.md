# Sketch references and ECAD workspace

ECAD now has its own workspace, with ECAD as its landing tab. Design keeps the mechanical modeling tabs. Command search can switch to the ECAD workspace when required.

In a sketch, choose **Project / Include**, then **Intersect**. Click an outside sketch curve or model edge to include the points where it crosses the active plane; click inside a body face to include its section curves. The preview shows crossing points before clicking. For a loft with circles on two offset planes, start the rail sketch on a plane crossing both circles, intersect each circle, and snap the rail endpoints to the resulting points.

**As construction** creates reference geometry excluded from feature profiles. Expand a saved sketch in the browser and toggle its **Construction** eye. While editing, the same control appears under **Active sketch**. Hidden construction and its unshared points are excluded from picking and snapping. A point shared with a normal rail remains visible as that rail's endpoint. Construction-only sketches can be finished, reopened and saved, and their visibility is stored with the document.

References are fixed snapshots. They do not follow subsequent edits to source geometry. This is stated in the tool panel. Body sections and projected curves continue to use tessellated curve reconstruction; crossing points come directly from the exact kernel intersection.

## Research and follow-up scope


Further work should add associative references with source identity and lost-reference repair, a true cross-sketch Pierce constraint, exact projected conics/splines, face boundary and silhouette selection, and 3D inclusion/project-to-surface tools. These require dependency and topology handling beyond fixed references; they are not represented as supported in the current UI.

## Validation

Run `dotnet run --project tests/Prism.ProjectionTests -c Release` from the repository root. It uses the native libraries in `native/bin` and checks the offset-circle rail workflow, tangent and non-crossing cases, body sections, projection duplicates, tool picking/preview, construction visibility, undo, and file roundtrips.
