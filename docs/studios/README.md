# Right-side studios

The sidebar now has a compact selector rail for Properties, Appearance, Diagnostics, References and Parameters. Studio panels use a 340px content area plus the rail; the ordinary inspector retains its previous overall width. Modeling commands and sketch editing take priority over studios. Opening a studio does not abandon an active operation. The script editor yields to an explicitly selected studio and returns when it closes.

## Diagnostics

Run checks explicitly to evaluate existing B-rep validity, sketch constraint status and captured feature input availability. Sketch checks operate on clones. Results are marked stale after document changes. A structured failure record connects a stopped feature rebuild to the original failed feature; its original saved inputs can be opened for inspection. Locate selects and frames an affected object; sketch issues open the real sketch editor. Checks and Activity tabs keep validation results distinct from historical session messages. Activity follows the active document only.

These checks do not certify surface continuity, manufacturing readiness, or general self-intersection freedom. They are synchronous, explicitly requested checks; large documents may take time to inspect.

## References

The panel reuses actual origin planes/axes/point, sketches, curves, construction planes and image-plane objects. It provides name search, visible-only filtering, visibility switches, selection/framing, reference origins/normals, and captured feature usage on the current branch. Editable sketches and construction features open their existing editors. Imported references without a recipe cannot be reconstructed into an editable feature. Origin references are fixed. Create construction plane opens the existing plane command.

## Parameters

Document values support names, pinning, arithmetic, functions from the existing expression parser, and references such as `rib = wall * 0.75`. Length literals accept `mm`, `cm`, and `inch`; `deg` is accepted for angle literals. Names are case-insensitive. Cycles, missing names, non-finite results and duplicate/reserved names are rejected. Units are scalar conversions, not a full dimensional-analysis system.

Edit document formulas in the draft, evaluate them, then apply. Removing a parameter used by a current-branch feature is blocked. Feature tab lists current-branch recipes and their dimensional numeric inputs. Enter a literal or a formula using saved document values, then update the feature. The full feature editor remains available for selections, integer counts, switches and other settings. A bound formula is shown in the standard numeric editor; typing a different literal detaches it.

Applying document changes validates all bound values against feature limits before touching the history. It rebuilds from the first affected feature, re-evaluating downstream bindings. Existing branches remain available. If native geometry rebuilding fails later, the session stops at the last successful result, reports the failed feature and preserves the complete original branch.

Named parameter definitions are immutable history snapshots and are stored in `.prism` files along with final geometry. The existing file format still does **not** save construction history or feature recipes. Consequently feature bindings remain session-only and cannot be reconstructed after reopening, though the named definitions and final shapes survive. Old files without parameter definitions remain compatible.

## Verification and preview

- `dotnet run --project tests/Prism.StudioTests`: 50 checks covering real geometry rebuilds, formula validation, downstream failures, persistence, references, diagnostics, document isolation, theme layouts and the native selector rail.
- History regression: 41 checks; Appearance regression: 26 checks; material/file compatibility regression: 100 checks.
- `dotnet build Prism.Desktop/Prism.Desktop.csproj --no-restore -o artifacts/studios-preview`

The desktop preview is `artifacts/studios-preview/Prism.Desktop.exe`. The running application was left untouched. Dark/light panel captures and separate Feature-tab captures are saved in this directory.
